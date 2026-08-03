using System;
using Xunit;

namespace Zalo.Net.Auth.Tests;

public sealed class CookieStoreTests
{
    [Fact]
    public void RoundTrip_PreservesNameAndValue()
    {
        CookieStore store = new();
        store.AddCookies("https://chat.zalo.me",
            ["zpsid=abc123; path=/; domain=.chat.zalo.me; Secure; HttpOnly"]);

        string json = store.ToJson();
        CookieStore store2 = CookieStore.FromJson(json);

        string header = store2.GetCookieHeader("https://chat.zalo.me");
        Assert.Contains("zpsid=abc123", header, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_MultipleCookies()
    {
        CookieStore store = new();
        store.AddCookies("https://id.zalo.me",
        [
            "session=xyz; path=/; domain=.zalo.me; Secure",
            "zac=hello; path=/; domain=.zalo.me",
        ]);

        string json = store.ToJson();
        CookieStore store2 = CookieStore.FromJson(json);

        string header = store2.GetCookieHeader("https://id.zalo.me");
        Assert.Contains("session=xyz", header, StringComparison.Ordinal);
        Assert.Contains("zac=hello", header, StringComparison.Ordinal);
    }

    [Fact]
    public void FromJson_EmptyArray_ReturnsEmptyStore()
    {
        CookieStore store = CookieStore.FromJson("[]");
        string header = store.GetCookieHeader("https://chat.zalo.me");
        Assert.Equal("", header);
    }

    [Fact]
    public void FromJson_ToughCookieShape_CompatibleKeyField()
    {
        const string json = """
            [{"key":"zpsid","value":"secret123","domain":"chat.zalo.me","path":"/","secure":true,"httpOnly":true}]
            """;
        CookieStore store = CookieStore.FromJson(json);
        string header = store.GetCookieHeader("https://chat.zalo.me");
        Assert.Contains("zpsid=secret123", header, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_DoesNotContainValue_InPlainText_WhenUsedWithSecretRedactor()
    {
        CookieStore store = new();
        store.AddCookies("https://chat.zalo.me",
            ["zpsid=TOPSECRET; path=/; domain=.chat.zalo.me"]);
        string json = store.ToJson();
        Assert.Contains("TOPSECRET", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GetCookieHeader_WrongDomain_ReturnsEmpty()
    {
        CookieStore store = new();
        store.AddCookies("https://chat.zalo.me",
            ["zpsid=abc; path=/; domain=.chat.zalo.me; Secure"]);
        string header = store.GetCookieHeader("https://google.com");
        Assert.DoesNotContain("zpsid", header, StringComparison.Ordinal);
    }
}
