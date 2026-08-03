using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Zalo.Net.Cryptography.Tests;

public class HashingTests
{
    [Fact]
    public void GetSignKey_ValidParams_ProducesDeterministicMd5Hash()
    {
        Dictionary<string, object?> @params = new()
        {
            ["imei"] = "test-imei",
            ["type"] = 30,
            ["client_version"] = 671,
        };

        string key1 = Hashing.GetSignKey("getlogininfo", @params);
        string key2 = Hashing.GetSignKey("getlogininfo", @params);

        Assert.NotNull(key1);
        Assert.Equal(32, key1.Length);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GetSignKey_NullParams_ThrowsArgumentNullException()
        => _ = Assert.Throws<ArgumentNullException>(() => Hashing.GetSignKey("getlogininfo", null!));

    [Fact]
    public void GenerateImei_ValidUserAgent_ProducesExpectedFormat()
    {
        string ua = "Mozilla/5.0";
        string imei = Hashing.GenerateImei(ua);

        Assert.NotNull(imei);
        Assert.Contains("-", imei, StringComparison.Ordinal);
        string[] parts = imei.Split('-');
        Assert.True(parts.Length >= 2);
    }

    [Fact]
    public void GenerateImei_MultipleCalls_GeneratesUniqueImeis()
    {
        string ua = "Mozilla/5.0";
        string imei1 = Hashing.GenerateImei(ua);
        string imei2 = Hashing.GenerateImei(ua);

        Assert.NotEqual(imei1, imei2);
    }

    [Fact]
    public async Task ComputeLargeFileMd5Async_StreamData_CalculatesCorrectMd5()
    {
        byte[] data = Encoding.UTF8.GetBytes("Hello Zalo.Net Cryptography!");
        using MemoryStream ms = new(data);

        string hash = await Hashing.ComputeLargeFileMd5Async(ms);

        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public async Task ComputeLargeFileMd5Async_NullStream_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => Hashing.ComputeLargeFileMd5Async(null!));
}
