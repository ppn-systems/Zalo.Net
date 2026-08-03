using Xunit;
using Zalo.Net.Contracts;

namespace Zalo.Net.WebSocket.Tests;

public class ZaloWsListenerTests
{
    [Fact]
    public void Constructor_ValidSession_InitializesListener()
    {
        ZaloSessionMaterial material = new("{}", "key", "imei", "12345", "UA");
        ZaloSession session = new(material, "12345", ["wss://test.zalo.me"], new System.Collections.Generic.Dictionary<string, string[]>(), 20000);

        ZaloWsListener listener = new(
            session,
            onMessage: _ => { },
            onStatus: _ => { });

        Assert.NotNull(listener);
    }
}
