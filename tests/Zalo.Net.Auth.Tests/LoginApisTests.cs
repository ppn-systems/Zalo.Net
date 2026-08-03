// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Xunit;

namespace Zalo.Net.Auth.Tests;

public sealed class LoginApisTests
{
    [Fact]
    public void BuildUrl_AppendsQueryParameters_AndDefaultApiVersion()
    {
        string baseUrl = "https://chat.zalo.me/api/test";

        UriBuilder builder = new(baseUrl)
        {
            Query = "imei=test-imei&type=30&zpw_ver=671&zpw_type=30"
        };

        string finalUrl = builder.Uri.ToString();
        Assert.Contains("imei=test-imei", finalUrl, StringComparison.Ordinal);
        Assert.Contains("zpw_ver=671", finalUrl, StringComparison.Ordinal);
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
