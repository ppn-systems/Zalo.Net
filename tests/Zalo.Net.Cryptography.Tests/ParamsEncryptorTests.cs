using System;
using Xunit;

namespace Zalo.Net.Cryptography.Tests;

public class ParamsEncryptorTests
{
    [Fact]
    public void Constructor_ValidInputs_GeneratesZcidAndEncryptKey()
    {
        string imei = "test-imei-12345";
        long firstLaunchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        ParamsEncryptor encryptor = new(30, imei, firstLaunchTime);

        string key = encryptor.GetEncryptKey();
        (string zcid, string zcidExt, string encVer) = encryptor.GetParams();

        Assert.Equal(32, key.Length);
        Assert.False(string.IsNullOrEmpty(zcid));
        Assert.False(string.IsNullOrEmpty(zcidExt));
        Assert.Equal("v2", encVer);
    }

    [Fact]
    public void EncryptData_ValidJson_ReturnsEncryptedString()
    {
        string imei = "test-imei-12345";
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ParamsEncryptor encryptor = new(30, imei, ts);

        string json = "{\"test\":\"value\"}";
        string encrypted = encryptor.EncryptData(json);

        Assert.False(string.IsNullOrEmpty(encrypted));
    }
}
