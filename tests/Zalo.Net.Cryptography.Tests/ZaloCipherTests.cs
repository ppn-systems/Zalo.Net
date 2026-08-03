using System;
using System.Linq;
using Xunit;

namespace Zalo.Net.Cryptography.Tests;

public class ZaloCipherTests
{
    private const string Base64Key = "k+87G0/8Y8k4/8+8/8/8+w==";
    private const string PlainText = "Zalo.Net Cipher Test Data 123";

    [Fact]
    public void EncodeAndDecode_Base64Key_RoundTripsSuccessfully()
    {
        string cipherText = ZaloCipher.EncodeAes(Base64Key, PlainText);
        Assert.NotNull(cipherText);

        string? decrypted = ZaloCipher.DecodeAes(Base64Key, cipherText);
        Assert.Equal(PlainText, decrypted);
    }

    [Fact]
    public void EncodeAndDecode_Utf8Key_RoundTripsSuccessfully()
    {
        string utf8Key = "1234567890123456";
        string cipherText = ZaloCipher.EncodeAesUtf8Key(utf8Key, PlainText);
        Assert.NotNull(cipherText);

        string? decrypted = ZaloCipher.DecodeAesUtf8Key(utf8Key, cipherText);
        Assert.Equal(PlainText, decrypted);
    }

    [Fact]
    public void EncodeAesUtf8Key_HexOutput_ProducesHexUpper()
    {
        string utf8Key = "1234567890123456";
        string hexCipher = ZaloCipher.EncodeAesUtf8Key(utf8Key, PlainText, hex: true, uppercase: true);
        Assert.NotNull(hexCipher);
        Assert.Equal(hexCipher.ToUpperInvariant(), hexCipher);
        Assert.True(hexCipher.All(c => "0123456789ABCDEF".Contains(c, StringComparison.Ordinal)));
    }

    [Fact]
    public void DecodeAes_InvalidBase64_ReturnsNull()
    {
        string? result = ZaloCipher.DecodeAes(Base64Key, "NotValidBase64!!!");
        Assert.Null(result);
    }

    [Fact]
    public void DecodeAesUtf8Key_InvalidCipher_ReturnsNull()
    {
        string? result = ZaloCipher.DecodeAesUtf8Key("1234567890123456", "NotValidBase64!!!");
        Assert.Null(result);
    }

    [Fact]
    public void EncodeAes_NullInput_ThrowsException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => ZaloCipher.EncodeAes(Base64Key, null!));
        _ = Assert.Throws<ArgumentNullException>(() => ZaloCipher.EncodeAes(null!, PlainText));
    }
}
