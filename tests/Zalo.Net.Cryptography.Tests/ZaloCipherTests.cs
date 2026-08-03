using System;
using System.Security.Cryptography;
using Xunit;

namespace Zalo.Net.Cryptography.Tests;

public class ZaloCipherTests
{
    [Fact]
    public void Base64Aes_EncryptAndDecrypt_ReturnsOriginalString()
    {
        byte[] keyBytes = new byte[16];
        RandomNumberGenerator.Fill(keyBytes);
        string base64Key = Convert.ToBase64String(keyBytes);
        string plaintext = "Test Zalo.Net AES Payload 123";

        string ciphertext = ZaloCipher.EncodeAes(base64Key, plaintext);
        string? decrypted = ZaloCipher.DecodeAes(base64Key, ciphertext);

        Assert.NotNull(ciphertext);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Utf8Aes_EncryptAndDecrypt_ReturnsOriginalString()
    {
        string utf8Key = "1234567890123456"; // 16-byte key
        string plaintext = "Hello AES-CBC UTF8!";

        string ciphertext = ZaloCipher.EncodeAesUtf8Key(utf8Key, plaintext);
        string? decrypted = ZaloCipher.DecodeAesUtf8Key(utf8Key, ciphertext);

        Assert.NotNull(ciphertext);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void DecodeAes_InvalidCiphertext_ReturnsNull()
    {
        string base64Key = Convert.ToBase64String(new byte[16]);
        string invalidCiphertext = "InvalidBase64###";

        string? result = ZaloCipher.DecodeAes(base64Key, invalidCiphertext);

        Assert.Null(result);
    }
}
