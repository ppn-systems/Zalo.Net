// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Xunit;
using Zalo.Net.Bot.Attributes;
using Zalo.Net.Bot.Context;
using Zalo.Net.Bot.Routing;
using Zalo.Net.Contracts;

namespace Zalo.Net.Bot.Tests;

public class ZaloBotDispatcherTests
{
    private sealed class DummyClient : IZaloClient
    {
        public IWebProxy? Proxy => null;

#pragma warning disable CS0067
        public event EventHandler<ZaloMessageEvent>? MessageReceived;
        public event EventHandler<ZaloSessionStatusChanged>? StatusChanged;
#pragma warning restore CS0067

        public void Dispose() { }

        public Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ZaloLoginState> PollQrStatusAsync(Guid sessionId, CancellationToken ct = default) => throw new NotImplementedException();
        public ZaloSessionMaterial? ConsumePendingMaterial(Guid sessionId) => null;
        public Task StartListenerAsync(ZaloSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task RunWithReconnectAsync(ZaloSessionMaterial material, IWebProxy? proxy = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloBankCard bankCard, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string binBank, string accountNumber, string accountName, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloContactCard contactCard, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string userId, string? phoneNumber = null, string? qrCodeUrl = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task JoinGroupViaLinkAsync(ZaloSession session, string inviteUrl, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReviewJoinRequestsAsync(ZaloSession session, string groupId, string[] memberUids, bool approve, CancellationToken ct = default) => Task.CompletedTask;
        public Task LeaveGroupSilentlyAsync(ZaloSession session, string groupId, CancellationToken ct = default) => Task.CompletedTask;
        public Task KickGroupMemberAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default) => Task.CompletedTask;
        public Task PromoteGroupAdminAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default) => Task.CompletedTask;
        public Task PinGroupMessageAsync(ZaloSession session, string groupId, string msgId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendImageAsync(ZaloSession session, string threadId, ZaloThreadType threadType, byte[] imageBytes, string fileName, string? caption = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SampleBotHandlers
    {
        public bool CommandTriggered { get; private set; }
        public bool KeywordTriggered { get; private set; }
        public bool OnMessageTriggered { get; private set; }

        [ZaloCommand("/help")]
        public void HandleHelp(ZaloBotContext ctx) => this.CommandTriggered = ctx != null;

        [ZaloKeyword("chuyển khoản", "stk")]
        public void HandlePayment(ZaloBotContext ctx) => this.KeywordTriggered = ctx != null;

        [ZaloOnMessage]
        public void HandleAny(ZaloBotContext ctx) => this.OnMessageTriggered = ctx != null;
    }

    [Fact]
    public async Task Dispatcher_RoutesCommandMessage_Correctly()
    {
        // Arrange
        ZaloBotDispatcher dispatcher = new();
        SampleBotHandlers handlers = new();
        dispatcher.RegisterHandlers(handlers);

        ZaloSessionMaterial material = new("cookie", "key_1", "imei_1", "uid_1", "ua");
        ZaloSession session = new(material, "uid_1", ["wss://zalo"], new Dictionary<string, string[]>(), 30000);
        ZaloMessageEvent msg = new("msg_1", "cli_1", "text", "uid_2", "uid_1", "Sender", "uid_2", ZaloThreadType.User, "12345", "/help me", null, false);
        ZaloBotContext ctx = new(msg, session, new DummyClient());

        // Act
        await dispatcher.DispatchAsync(ctx);

        // Assert
        Assert.True(handlers.CommandTriggered);
        Assert.False(handlers.KeywordTriggered);
    }

    [Fact]
    public async Task Dispatcher_RoutesKeywordMessage_Correctly()
    {
        // Arrange
        ZaloBotDispatcher dispatcher = new();
        SampleBotHandlers handlers = new();
        dispatcher.RegisterHandlers(handlers);

        ZaloSessionMaterial material = new("cookie", "key_1", "imei_1", "uid_1", "ua");
        ZaloSession session = new(material, "uid_1", ["wss://zalo"], new Dictionary<string, string[]>(), 30000);
        ZaloMessageEvent msg = new("msg_1", "cli_1", "text", "uid_2", "uid_1", "Sender", "uid_2", ZaloThreadType.User, "12345", "Cho xin STK ngân hàng với", null, false);
        ZaloBotContext ctx = new(msg, session, new DummyClient());

        // Act
        await dispatcher.DispatchAsync(ctx);

        // Assert
        Assert.False(handlers.CommandTriggered);
        Assert.True(handlers.KeywordTriggered);
    }
}
