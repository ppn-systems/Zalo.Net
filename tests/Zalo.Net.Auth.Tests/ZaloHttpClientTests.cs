namespace Zalo.Net.Auth.Tests;

public class ZaloHttpClientTests
{
    [Xunit.Fact]
    public void Constructor_CustomUserAgent_ExposesCookiesAndHandler()
    {
        string ua = "TestAgent/1.0";
        using ZaloHttpClient client = new(ua);

        Xunit.Assert.NotNull(client.Cookies);
    }
}
