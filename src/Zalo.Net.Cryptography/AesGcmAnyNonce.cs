// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;

namespace Zalo.Net.Cryptography;

/// <summary>
/// AES-GCM cho IV không phải 12 byte (Zalo dùng IV 16 byte).
/// </summary>
/// <remarks>
/// <see cref="AesGcm"/> của .NET chỉ chấp nhận nonce 12 byte và ném
/// <c>ArgumentException: The specified nonce is not a valid size for this algorithm</c> với
/// nonce 16 byte. GCM theo NIST SP 800-38D cho phép IV dài bất kỳ (khi đó J0 được suy ra bằng
/// GHASH thay vì đệm IV), nên lớp này tự cài GHASH + CTR để giải mã đúng những khung Zalo
/// dùng IV 16 byte. Với IV 12 byte, lớp uỷ quyền cho <see cref="AesGcm"/> của .NET.
/// </remarks>
internal static class AesGcmAnyNonce
{
    private const int BlockSize = 16;

    /// <summary>Giải mã AES-GCM với IV độ dài bất kỳ; ném nếu tag không khớp.</summary>
    public static byte[] Decrypt(byte[] key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> ctWithTag)
    {
        if (ctWithTag.Length < 16)
        {
            throw new InvalidOperationException("Ciphertext with tag must be at least 16 bytes");
        }

        ReadOnlySpan<byte> ciphertext = ctWithTag[..^16];
        ReadOnlySpan<byte> tag = ctWithTag[^16..];

        if (iv.Length == 12)
        {
            // Đường chuẩn: uỷ quyền cho .NET (nhanh và đã được kiểm chứng).
            using AesGcm gcm = new(key, 16);
            byte[] plainStandard = new byte[ciphertext.Length];
            gcm.Decrypt(iv, ciphertext, tag, plainStandard, aad);
            return plainStandard;
        }

        byte[] h = new byte[BlockSize];
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            _ = aes.EncryptEcb(new byte[BlockSize], h, PaddingMode.None);
        }

        byte[] j0 = ComputeJ0(h, iv);
        byte[] plaintext = new byte[ciphertext.Length];
        Gctr(key, j0, ciphertext, plaintext, incrementFirst: true);

        byte[] expected = new byte[BlockSize];
        byte[] s = ComputeS(h, aad, ciphertext);
        byte[] mask = new byte[BlockSize];
        Gctr(key, j0, s, mask, incrementFirst: false);
        Array.Copy(mask, expected, BlockSize);

        if (!CryptographicOperations.FixedTimeEquals(expected, tag))
        {
            throw new CryptographicException("AES-GCM tag mismatch (IV non-12-byte path)");
        }

        return plaintext;
    }

    /// <summary>J0 cho IV khác 96 bit: GHASH_H(IV || 0^(s+64) || [len(IV)]_64).</summary>
    private static byte[] ComputeJ0(byte[] h, ReadOnlySpan<byte> iv)
    {
        int padBytes = (BlockSize - (iv.Length % BlockSize)) % BlockSize;
        byte[] data = new byte[iv.Length + padBytes + 8 + 8];
        iv.CopyTo(data);
        WriteUInt64BigEndian(data.AsSpan(iv.Length + padBytes + 8), (ulong)iv.Length * 8);
        return GHash(h, data);
    }

    /// <summary>S = GHASH_H(A || pad || C || pad || [len(A)]_64 || [len(C)]_64).</summary>
    private static byte[] ComputeS(byte[] h, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> ciphertext)
    {
        int aadPad = (BlockSize - (aad.Length % BlockSize)) % BlockSize;
        int ctPad = (BlockSize - (ciphertext.Length % BlockSize)) % BlockSize;
        byte[] data = new byte[aad.Length + aadPad + ciphertext.Length + ctPad + 16];
        Span<byte> w = data;
        aad.CopyTo(w);
        int offset = aad.Length + aadPad;
        ciphertext.CopyTo(w[offset..]);
        offset += ciphertext.Length + ctPad;
        WriteUInt64BigEndian(w[offset..], (ulong)aad.Length * 8);
        WriteUInt64BigEndian(w[(offset + 8)..], (ulong)ciphertext.Length * 8);
        return GHash(h, data);
    }

    private static byte[] GHash(byte[] h, ReadOnlySpan<byte> data)
    {
        byte[] y = new byte[BlockSize];
        for (int i = 0; i < data.Length; i += BlockSize)
        {
            for (int b = 0; b < BlockSize; b++)
            {
                y[b] ^= data[i + b];
            }
            y = Multiply(h, y);
        }
        return y;
    }

    /// <summary>Nhân trong GF(2^128) theo quy ước GCM (bit MSB-first).</summary>
    private static byte[] Multiply(byte[] x, byte[] y)
    {
        byte[] z = new byte[BlockSize];
        byte[] v = (byte[])y.Clone();
        for (int i = 0; i < 128; i++)
        {
            if ((x[i >> 3] & (1 << (7 - (i & 7)))) != 0)
            {
                for (int b = 0; b < BlockSize; b++)
                {
                    z[b] ^= v[b];
                }
            }

            bool lsb = (v[BlockSize - 1] & 1) != 0;
            for (int b = BlockSize - 1; b > 0; b--)
            {
                v[b] = (byte)((v[b] >> 1) | ((v[b - 1] & 1) << 7));
            }
            v[0] >>= 1;
            if (lsb)
            {
                v[0] ^= 0xE1;
            }
        }
        return z;
    }

    /// <summary>Chế độ CTR của GCM (bộ đếm 32 bit cuối, big-endian).</summary>
    private static void Gctr(byte[] key, byte[] j0, ReadOnlySpan<byte> input, Span<byte> output, bool incrementFirst)
    {
        byte[] counter = (byte[])j0.Clone();
        if (incrementFirst)
        {
            Increment32(counter);
        }

        using Aes aes = Aes.Create();
        aes.Key = key;
        byte[] keystream = new byte[BlockSize];
        for (int offset = 0; offset < input.Length; offset += BlockSize)
        {
            _ = aes.EncryptEcb(counter, keystream, PaddingMode.None);
            int count = Math.Min(BlockSize, input.Length - offset);
            for (int b = 0; b < count; b++)
            {
                output[offset + b] = (byte)(input[offset + b] ^ keystream[b]);
            }
            Increment32(counter);
        }
    }

    private static void Increment32(byte[] counter)
    {
        for (int b = BlockSize - 1; b >= BlockSize - 4; b--)
        {
            if (++counter[b] != 0)
            {
                break;
            }
        }
    }

    private static void WriteUInt64BigEndian(Span<byte> destination, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            destination[i] = (byte)(value >> (56 - (8 * i)));
        }
    }
}
