using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;

namespace Zalo.Net.Tests;

public class ZaloWebClientTests
{
    [Fact]
    public void Dispose_CleanState_DisposesWithoutThrowing()
    {
        using ZaloWebClient client = new();
        Assert.NotNull(client);
    }

    [Fact]
    public void ConsumePendingMaterial_UnknownSession_ReturnsNull()
    {
        using ZaloWebClient client = new();
        ZaloSessionMaterial? material = client.ConsumePendingMaterial(Guid.NewGuid());

        Assert.Null(material);
    }

    [Fact]
    public async Task LoginWithSessionAsync_NullMaterial_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => ZaloWebClient.LoginWithSessionAsync(null!, CancellationToken.None));

    [Fact]
    public async Task StartListenerAsync_NullSession_ThrowsArgumentNullException()
    {
        using ZaloWebClient client = new();
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.StartListenerAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task StartListenerAsync_EmptyWsUrls_ThrowsZaloApiException()
    {
        using ZaloWebClient client = new();
        ZaloSession session = new(
            new ZaloSessionMaterial("[]", "secret", "imei", "uid", "ua"),
            "uid",
            Array.Empty<string>(),
            new Dictionary<string, string[]>(),
            20_000);

        _ = await Assert.ThrowsAsync<ZaloApiException>(() => client.StartListenerAsync(session, CancellationToken.None));
    }
}
