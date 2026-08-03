// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Zalo.Net.Cryptography;

/// <summary>
/// Provides AES-CBC encryption and decryption routines for Zalo Web API requests and responses.
/// </summary>
public static class ZaloCipher
{
    private static readonly byte[] s_zeroIv = new byte[16];

    /// <summary>
    /// Encrypts plaintext using a Base64-encoded AES key (AES-CBC, zero IV, PKCS7 padding).
    /// </summary>
    public static string EncodeAes(string base64Key, string plaintext)
    {
        byte[] key = Convert.FromBase64String(base64Key);
        return EncryptCbc(key, Encoding.UTF8.GetBytes(plaintext));
    }

    /// <summary>
    /// Decrypts Base64 ciphertext using a Base64-encoded AES key.
    /// URL-unescapes input prior to Base64 decoding. Returns null on failure.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Zalo protocol retry behavior treats invalid ciphertext payload as null")]
    public static string? DecodeAes(string base64Key, string base64Ciphertext)
    {
        try
        {
            byte[] key = Convert.FromBase64String(base64Key);
            string decoded = Uri.UnescapeDataString(base64Ciphertext);
            byte[] ct = Convert.FromBase64String(decoded);
            return DecryptCbc(key, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Encrypts plaintext using a raw UTF-8 string AES key.
    /// </summary>
    public static string EncodeAesUtf8Key(string utf8Key, string plaintext, bool hex = false, bool uppercase = false)
    {
        byte[] key = Encoding.UTF8.GetBytes(utf8Key);
        byte[] cipherBytes = RawEncryptCbc(key, Encoding.UTF8.GetBytes(plaintext));
        if (hex)
        {
            string hexStr = Convert.ToHexString(cipherBytes);
            return uppercase ? hexStr : hexStr.ToLowerInvariant();
        }
        return Convert.ToBase64String(cipherBytes);
    }

    /// <summary>
    /// Decrypts Base64 ciphertext using a raw UTF-8 string AES key.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Zalo protocol retry behavior treats invalid ciphertext payload as null")]
    public static string? DecodeAesUtf8Key(string utf8Key, string base64Ciphertext)
    {
        try
        {
            byte[] key = Encoding.UTF8.GetBytes(utf8Key);
            string decoded = Uri.UnescapeDataString(base64Ciphertext);
            byte[] ct = Convert.FromBase64String(decoded);
            return DecryptCbc(key, ct);
        }
        catch
        {
            return null;
        }
    }

    private static string EncryptCbc(byte[] key, byte[] plaintext)
        => Convert.ToBase64String(RawEncryptCbc(key, plaintext));

    private static byte[] RawEncryptCbc(byte[] key, byte[] plaintext)
    {
        using Aes aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(plaintext, s_zeroIv, PaddingMode.PKCS7);
    }

    private static string DecryptCbc(byte[] key, byte[] ciphertext)
    {
        using Aes aes = Aes.Create();
        aes.Key = key;
        byte[] plain = aes.DecryptCbc(ciphertext, s_zeroIv, PaddingMode.PKCS7);
        return Encoding.UTF8.GetString(plain);
    }
}
