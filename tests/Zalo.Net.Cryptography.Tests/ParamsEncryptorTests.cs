// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using Xunit;

namespace Zalo.Net.Cryptography.Tests;

public sealed class ParamsEncryptorTests
{
    private const int Type = 30;
    private const string Imei = "test-imei-12345";
    private const long FirstLaunchTime = 1_700_000_000_000L;

    [Fact]
    public void CreateZcid_IsHexUppercase()
    {
        ParamsEncryptor p = new(Type, Imei, FirstLaunchTime);
        (string zcid, _, _) = p.GetParams();
        Assert.NotEmpty(zcid);
        Assert.Equal(zcid.ToUpperInvariant(), zcid);
        Assert.True(zcid.All(c => "0123456789ABCDEF".Contains(c, StringComparison.Ordinal)), "zcid must be hex uppercase");
    }

    [Fact]
    public void CreateZcid_IsDeterministic()
    {
        ParamsEncryptor p1 = new(Type, Imei, FirstLaunchTime);
        ParamsEncryptor p2 = new(Type, Imei, FirstLaunchTime);
        Assert.Equal(p1.GetParams().Zcid, p2.GetParams().Zcid);
    }

    [Fact]
    public void GetEncryptKey_WithSeededZcidExt_IsExactly32Chars()
    {
        ParamsEncryptor p = new(Type, Imei, FirstLaunchTime, zcidExt: "abc123");
        Assert.Equal(32, p.GetEncryptKey().Length);
    }

    [Fact]
    public void GetEncryptKey_WithSeededZcidExt_IsDeterministic()
    {
        ParamsEncryptor p1 = new(Type, Imei, FirstLaunchTime, zcidExt: "abc123");
        ParamsEncryptor p2 = new(Type, Imei, FirstLaunchTime, zcidExt: "abc123");
        Assert.Equal(p1.GetEncryptKey(), p2.GetEncryptKey());
    }

    [Fact]
    public void EncryptData_RoundTrips_WithZaloCipher()
    {
        ParamsEncryptor p = new(Type, Imei, FirstLaunchTime, zcidExt: "deadbeef12");
        string key = p.GetEncryptKey();
        const string json = """{"computer_name":"Web","imei":"test-imei-12345","language":"vi","ts":1700000000000}""";
        string enc = p.EncryptData(json);
        string? dec = ZaloCipher.DecodeAesUtf8Key(key, enc);
        Assert.Equal(json, dec);
    }

    [Fact]
    public void GetParams_EncVer_IsV2()
    {
        ParamsEncryptor p = new(Type, Imei, FirstLaunchTime);
        Assert.Equal("v2", p.GetParams().EncVer);
    }

    [Fact]
    public void Md5_IsLowercaseHex()
    {
        string md5 = ParamsEncryptor.ComputeMd5Hex("abc123");
        Assert.Equal(md5.ToLowerInvariant(), md5);
        Assert.Equal(32, md5.Length);
    }

    [Fact]
    public void Constructor_NullOrEmptyImei_ThrowsArgumentException()
    {
        _ = Assert.Throws<ArgumentException>(() => new ParamsEncryptor(Type, "", FirstLaunchTime));
        _ = Assert.Throws<ArgumentException>(() => new ParamsEncryptor(Type, null!, FirstLaunchTime));
    }
}
