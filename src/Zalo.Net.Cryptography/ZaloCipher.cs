using System;
using System.Security.Cryptography;
using System.Text;

namespace Zalo.Net.Cryptography;

/// <summary>
/// Provides AES-CBC encryption and decryption routines for Zalo Web API requests and responses.
/// </summary>
public static class ZaloCipher
{
    private static readonly byte[] ZeroIv = new byte[16];

    /// <summary>
    /// Encrypts plaintext using a Base64-encoded AES key (AES-CBC, zero IV, PKCS7 padding).
    /// </summary>
    public static string EncodeAes(string base64Key, string plaintext)
    {
        var key = Convert.FromBase64String(base64Key);
        return EncryptCbc(key, Encoding.UTF8.GetBytes(plaintext));
    }

    /// <summary>
    /// Decrypts Base64 ciphertext using a Base64-encoded AES key.
    /// URL-unescapes input prior to Base64 decoding. Returns null on failure.
    /// </summary>
    public static string? DecodeAes(string base64Key, string base64Ciphertext)
    {
        try
        {
            var key = Convert.FromBase64String(base64Key);
            var decoded = Uri.UnescapeDataString(base64Ciphertext);
            var ct = Convert.FromBase64String(decoded);
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
        var key = Encoding.UTF8.GetBytes(utf8Key);
        var cipherBytes = RawEncryptCbc(key, Encoding.UTF8.GetBytes(plaintext));
        if (hex)
        {
            var hexStr = Convert.ToHexString(cipherBytes);
            return uppercase ? hexStr : hexStr.ToLowerInvariant();
        }
        return Convert.ToBase64String(cipherBytes);
    }

    /// <summary>
    /// Decrypts Base64 ciphertext using a raw UTF-8 string AES key.
    /// </summary>
    public static string? DecodeAesUtf8Key(string utf8Key, string base64Ciphertext)
    {
        try
        {
            var key = Encoding.UTF8.GetBytes(utf8Key);
            var decoded = Uri.UnescapeDataString(base64Ciphertext);
            var ct = Convert.FromBase64String(decoded);
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
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = ZeroIv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    private static string DecryptCbc(byte[] key, byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = ZeroIv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        return Encoding.UTF8.GetString(plain);
    }
}
