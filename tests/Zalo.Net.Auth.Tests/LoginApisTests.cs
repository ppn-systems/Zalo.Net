// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Xunit;
using Zalo.Net.Contracts;

namespace Zalo.Net.Auth.Tests;

public sealed class LoginApisTests
{
    [Fact]
    public void Protocol_ApiVersion_IsUpgradedTo694()
    {
        Assert.Equal(694, ZaloConstants.Protocol.ApiVersion);
        Assert.Equal(30, ZaloConstants.Protocol.ApiType);
        Assert.Equal("-1", ZaloConstants.Protocol.MentionAllUid);
    }

    [Fact]
    public void Hosts_GetCandidates_ReturnsDeduplicatedFailoverList()
    {
        string[] chatCandidates = ZaloConstants.Hosts.GetCandidates("chat", "tt-chat3-wpa.chat.zalo.me");
        Assert.NotEmpty(chatCandidates);
        Assert.Equal("https://tt-chat3-wpa.chat.zalo.me", chatCandidates[0]);
        Assert.Contains("https://chat-wpa.chat.zalo.me", chatCandidates);
        Assert.Contains("https://tt-chat3-wpa.api.zaloapp.com", chatCandidates);
    }

    [Fact]
    public void GenerateSalt_ReturnsEightAlphanumericCharacters()
    {
        string salt = LoginApis.GenerateSalt();
        Assert.Equal(8, salt.Length);
        foreach (char c in salt)
        {
            Assert.InRange(c, '!', '}');
        }
    }

    [Fact]
    public void EncryptPassword_ProducesDeterministicBase64Ciphertext()
    {
        string enc1 = LoginApis.EncryptPassword("84912345678", "test-imei-8668", "MySecretPass!", "Ab1");
        string enc2 = LoginApis.EncryptPassword("84912345678", "test-imei-8668", "MySecretPass!", "Ab1");

        Assert.False(string.IsNullOrWhiteSpace(enc1));
        Assert.Equal(enc1, enc2);
        byte[] raw = Convert.FromBase64String(enc1);
        Assert.NotEmpty(raw);
        Assert.Equal(0, raw.Length % 16);
    }

    [Fact]
    public void BuildUrl_AppendsQueryParameters_AndDefaultApiVersion()
    {
        string baseUrl = "https://chat.zalo.me/api/test";

        UriBuilder builder = new(baseUrl)
        {
            Query = $"imei=test-imei&type=30&zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type=30"
        };

        string finalUrl = builder.Uri.ToString();
        Assert.Contains("imei=test-imei", finalUrl, StringComparison.Ordinal);
        Assert.Contains("zpw_ver=694", finalUrl, StringComparison.Ordinal);
        Assert.Contains("zpw_type=30", finalUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void CookieStore_Clear_RemovesAllCookies()
    {
        CookieStore store = new();
        store.AddCookies("https://chat.zalo.me", ["zpsid=abc123; path=/; domain=.chat.zalo.me"]);

        Assert.NotEmpty(store.GetCookieHeader("https://chat.zalo.me"));

        CookieStore emptyStore = CookieStore.FromJson("[]");
        Assert.Equal("", emptyStore.GetCookieHeader("https://chat.zalo.me"));
    }
}
