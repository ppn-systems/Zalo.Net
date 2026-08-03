using System;
using System.Collections.Generic;
using Xunit;

namespace Zalo.Net.Auth.Tests;

public class CookieStoreTests
{
    [Fact]
    public void AddCookies_ValidSetCookieHeaders_ParsesAndStoresCookies()
    {
        CookieStore store = new();
        List<string> headers = ["zpw_sek=secret123; Domain=chat.zalo.me; Path=/"];

        store.AddCookies("https://chat.zalo.me", headers);

        string cookieHeader = store.GetCookieHeader("https://chat.zalo.me");
        Assert.Contains("zpw_sek=secret123", cookieHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJsonAndFromJson_CookieStore_RoundtripsSuccessfully()
    {
        CookieStore store1 = new();
        store1.AddCookies("https://chat.zalo.me", ["zpw_sek=abcxyz; Domain=chat.zalo.me; Path=/"]);

        string json = store1.ToJson();
        Assert.False(string.IsNullOrEmpty(json));

        CookieStore store2 = CookieStore.FromJson(json);
        string header2 = store2.GetCookieHeader("https://chat.zalo.me");

        Assert.Contains("zpw_sek=abcxyz", header2, StringComparison.Ordinal);
    }
}
