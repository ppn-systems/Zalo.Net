// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;

namespace Zalo.Net.Cryptography;

/// <summary>
/// AES-GCM cho IV độ dài bất kỳ (đặc biệt IV 16 byte của Zalo) với thuật toán GHASH + GCTR không cấp phát heap (Zero GC).
/// </summary>
/// <remarks>
/// <see cref="AesGcm"/> của .NET chỉ chấp nhận nonce 12 byte và ném
/// <c>ArgumentException: The specified nonce is not a valid size for this algorithm</c> với
/// nonce 16 byte. GCM theo NIST SP 800-38D cho phép IV dài bất kỳ (khi đó J0 được suy ra bằng
/// GHASH thay vì đệm IV), nên lớp này tự cài GHASH + CTR để giải mã đúng những khung Zalo
/// dùng IV 16 byte. Với IV 12 byte, lớp uỷ quyền cho <see cref="AesGcm"/> của .NET.
/// Toàn bộ các mảng tạm đều dùng <c>stackalloc</c> để giảm tối đa áp lực GC.
/// </remarks>
public static class AesGcmAnyNonce
{
    private const int BlockSize = 16;

    /// <summary>Mã hoá AES-GCM với IV độ dài bất kỳ.</summary>
    public static (byte[] Ciphertext, byte[] Tag) Encrypt(byte[] key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> plaintext)
    {
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[BlockSize];

        if (iv.Length == 12)
        {
            using AesGcm gcm = new(key, 16);
            gcm.Encrypt(iv, plaintext, ciphertext, tag, aad);
            return (ciphertext, tag);
        }

        Span<byte> h = stackalloc byte[BlockSize];
        Span<byte> zero = stackalloc byte[BlockSize];
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            _ = aes.EncryptEcb(zero, h, PaddingMode.None);
        }

        Span<byte> j0 = stackalloc byte[BlockSize];
        ComputeJ0(h, iv, j0);
        Gctr(key, j0, plaintext, ciphertext, incrementFirst: true);

        Span<byte> s = stackalloc byte[BlockSize];
        ComputeS(h, aad, ciphertext, s);
        Gctr(key, j0, s, tag, incrementFirst: false);

        return (ciphertext, tag);
    }

    /// <summary>Giải mã AES-GCM với IV độ dài bất kỳ; ném nếu tag không khớp.</summary>
    public static byte[] Decrypt(byte[] key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> ctWithTag)
    {
        if (ctWithTag.Length < 16)
        {
            throw new InvalidOperationException("Ciphertext with tag must be at least 16 bytes");
        }

        byte[] plaintext = new byte[ctWithTag.Length - 16];
        Decrypt(key, iv, aad, ctWithTag, plaintext);
        return plaintext;
    }

    /// <summary>Giải mã AES-GCM với IV độ dài bất kỳ ghi trực tiếp vào span đích (Zero GC).</summary>
    public static void Decrypt(byte[] key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> ctWithTag, Span<byte> plaintext)
    {
        if (ctWithTag.Length < 16)
        {
            throw new InvalidOperationException("Ciphertext with tag must be at least 16 bytes");
        }

        ReadOnlySpan<byte> ciphertext = ctWithTag[..^16];
        ReadOnlySpan<byte> tag = ctWithTag[^16..];

        if (plaintext.Length < ciphertext.Length)
        {
            throw new ArgumentException("Plaintext destination span is too small", nameof(plaintext));
        }

        if (iv.Length == 12)
        {
            // Đường chuẩn: uỷ quyền cho .NET (nhanh và đã được kiểm chứng).
            using AesGcm gcm = new(key, 16);
            gcm.Decrypt(iv, ciphertext, tag, plaintext[..ciphertext.Length], aad);
            return;
        }

        Span<byte> h = stackalloc byte[BlockSize];
        Span<byte> zero = stackalloc byte[BlockSize];
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            _ = aes.EncryptEcb(zero, h, PaddingMode.None);
        }

        Span<byte> j0 = stackalloc byte[BlockSize];
        ComputeJ0(h, iv, j0);
        Gctr(key, j0, ciphertext, plaintext[..ciphertext.Length], incrementFirst: true);

        Span<byte> s = stackalloc byte[BlockSize];
        ComputeS(h, aad, ciphertext, s);
        Span<byte> expected = stackalloc byte[BlockSize];
        Gctr(key, j0, s, expected, incrementFirst: false);

        if (!CryptographicOperations.FixedTimeEquals(expected, tag))
        {
            throw new CryptographicException("AES-GCM tag mismatch (IV non-12-byte path)");
        }
    }

    /// <summary>J0 cho IV khác 96 bit: GHASH_H(IV || 0^(s+64) || [len(IV)]_64).</summary>
    private static void ComputeJ0(ReadOnlySpan<byte> h, ReadOnlySpan<byte> iv, Span<byte> j0)
    {
        int padBytes = (BlockSize - (iv.Length % BlockSize)) % BlockSize;
        int totalLen = iv.Length + padBytes + 16;
        Span<byte> data = totalLen <= 256 ? stackalloc byte[totalLen] : new byte[totalLen];
        data.Clear();
        iv.CopyTo(data);
        WriteUInt64BigEndian(data[(iv.Length + padBytes + 8)..], (ulong)iv.Length * 8);
        GHash(h, data, j0);
    }

    /// <summary>S = GHASH_H(A || pad || C || pad || [len(A)]_64 || [len(C)]_64).</summary>
    private static void ComputeS(ReadOnlySpan<byte> h, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> ciphertext, Span<byte> s)
    {
        int aadPad = (BlockSize - (aad.Length % BlockSize)) % BlockSize;
        int ctPad = (BlockSize - (ciphertext.Length % BlockSize)) % BlockSize;
        int totalLen = aad.Length + aadPad + ciphertext.Length + ctPad + 16;
        Span<byte> data = totalLen <= 512 ? stackalloc byte[totalLen] : new byte[totalLen];
        data.Clear();
        aad.CopyTo(data);
        int offset = aad.Length + aadPad;
        ciphertext.CopyTo(data[offset..]);
        offset += ciphertext.Length + ctPad;
        WriteUInt64BigEndian(data[offset..], (ulong)aad.Length * 8);
        WriteUInt64BigEndian(data[(offset + 8)..], (ulong)ciphertext.Length * 8);
        GHash(h, data, s);
    }

    private static void GHash(ReadOnlySpan<byte> h, ReadOnlySpan<byte> data, Span<byte> y)
    {
        y.Clear();
        for (int i = 0; i < data.Length; i += BlockSize)
        {
            for (int b = 0; b < BlockSize; b++)
            {
                y[b] ^= data[i + b];
            }
            Multiply(h, y);
        }
    }

    /// <summary>Nhân trong GF(2^128) theo quy ước GCM (bit MSB-first) với Zero GC.</summary>
    private static void Multiply(ReadOnlySpan<byte> x, Span<byte> y)
    {
        Span<byte> z = stackalloc byte[BlockSize];
        Span<byte> v = stackalloc byte[BlockSize];
        y.CopyTo(v);

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

        z.CopyTo(y);
    }

    /// <summary>Chế độ CTR của GCM (bộ đếm 32 bit cuối, big-endian) với Zero GC.</summary>
    private static void Gctr(byte[] key, ReadOnlySpan<byte> j0, ReadOnlySpan<byte> input, Span<byte> output, bool incrementFirst)
    {
        Span<byte> counter = stackalloc byte[BlockSize];
        j0.CopyTo(counter);
        if (incrementFirst)
        {
            Increment32(counter);
        }

        using Aes aes = Aes.Create();
        aes.Key = key;
        Span<byte> keystream = stackalloc byte[BlockSize];
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

    private static void Increment32(Span<byte> counter)
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
