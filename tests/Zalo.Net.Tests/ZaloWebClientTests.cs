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
    private static ZaloSession MakeDummySession()
        => new(new ZaloSessionMaterial("[]", "secret", "imei", "uid", "ua"),
            "uid", ["wss://ws.zalo.me"], new Dictionary<string, string[]>(), 20_000);

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

    [Fact]
    public async Task CreateGroupAsync_NullSession_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => ZaloWebClient.CreateGroupAsync(null!, "Test Group", ["m1"], CancellationToken.None));

    [Fact]
    public async Task CreateGroupAsync_EmptyMembers_ThrowsZaloApiException()
    {
        ZaloSession session = MakeDummySession();
        _ = await Assert.ThrowsAsync<ZaloApiException>(() => ZaloWebClient.CreateGroupAsync(session, "Test Group", Array.Empty<string>(), CancellationToken.None));
    }

    [Fact]
    public async Task FindUserByPhoneAsync_NullSession_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => ZaloWebClient.FindUserByPhoneAsync(null!, "0901234567", CancellationToken.None));

    [Fact]
    public async Task SendFriendRequestAsync_NullSession_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => ZaloWebClient.SendFriendRequestAsync(null!, "u123", "Hello", CancellationToken.None));

    [Fact]
    public async Task UndoMessageAsync_NullSession_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => ZaloWebClient.UndoMessageAsync(null!, "thread1", "msg1", "cli1", ZaloThreadType.User, CancellationToken.None));

    [Fact]
    public async Task AddReactionAsync_NullSession_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => ZaloWebClient.AddReactionAsync(null!, "thread1", "msg1", "cli1", ZaloThreadType.User, Endpoints.ZaloReactionType.Heart, CancellationToken.None));

    [Fact]
    public async Task SendStickerAsync_NullSession_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => ZaloWebClient.SendStickerAsync(null!, "thread1", 1, 1, 1, ZaloThreadType.User, CancellationToken.None));

    [Fact]
    public async Task SendQuoteMessageAsync_NullSession_ThrowsArgumentNullException()
        => _ = await Assert.ThrowsAsync<ArgumentNullException>(() => ZaloWebClient.SendQuoteMessageAsync(null!, "thread1", ZaloThreadType.User, "Rep", "m1", "c1", "u1", "Quote", quoteTs: 0, ct: CancellationToken.None));
}
