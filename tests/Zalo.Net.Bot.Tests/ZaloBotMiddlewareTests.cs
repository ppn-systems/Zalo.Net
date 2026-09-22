// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Xunit;
using Zalo.Net.Bot.Context;
using Zalo.Net.Bot.Routing;
using Zalo.Net.Contracts;

namespace Zalo.Net.Bot.Tests;

public class ZaloBotMiddlewareTests
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

    private static ZaloBotContext CreateContext(string text)
    {
        ZaloSessionMaterial material = new("[]", "key_1", "imei_1", "uid_1", "ua");
        ZaloSession session = new(material, "uid_1", ["wss://zalo"], new Dictionary<string, string[]>(), 30000);
        ZaloMessageEvent msg = new("msg_1", "cli_1", "text", "uid_2", "uid_1", "Sender", "uid_2", ZaloThreadType.User, "12345", text, null, false);
        return new ZaloBotContext(msg, session, new DummyClient());
    }

    [Fact]
    public async Task Middleware_ExecutesInCorrectPipelineOrder()
    {
        // Arrange
        ZaloBotDispatcher dispatcher = new();
        List<string> trace = [];

        _ = dispatcher.Use(async (ctx, next, ct) =>
        {
            trace.Add("M1_Start");
            await next().ConfigureAwait(false);
            trace.Add("M1_End");
        });

        _ = dispatcher.Use(async (ctx, next) =>
        {
            trace.Add("M2_Start");
            await next().ConfigureAwait(false);
            trace.Add("M2_End");
        });

        _ = dispatcher.OnCommand("/test", ctx =>
        {
            trace.Add("Handler");
            return Task.CompletedTask;
        });

        ZaloBotContext ctx = CreateContext("/test");

        // Act
        await dispatcher.DispatchAsync(ctx);

        // Assert
        Assert.Equal(["M1_Start", "M2_Start", "Handler", "M2_End", "M1_End"], trace);
    }

    [Fact]
    public async Task Middleware_ShortCircuit_PreventsSubsequentExecution()
    {
        // Arrange
        ZaloBotDispatcher dispatcher = new();
        bool handlerExecuted = false;
        bool m2Executed = false;

        _ = dispatcher.Use((ctx, next) =>
        {
            // Do not call next(), short-circuit
            return Task.CompletedTask;
        });

        _ = dispatcher.Use(async (ctx, next) =>
        {
            m2Executed = true;
            await next().ConfigureAwait(false);
        });

        _ = dispatcher.OnCommand("/test", ctx =>
        {
            handlerExecuted = true;
            return Task.CompletedTask;
        });

        ZaloBotContext ctx = CreateContext("/test");

        // Act
        await dispatcher.DispatchAsync(ctx);

        // Assert
        Assert.False(m2Executed);
        Assert.False(handlerExecuted);
    }

    [Fact]
    public async Task Middleware_PassesStateViaItems()
    {
        // Arrange
        ZaloBotDispatcher dispatcher = new();
        string? retrievedRole = null;

        _ = dispatcher.Use(async (ctx, next) =>
        {
            ctx.Items["role"] = "superadmin";
            await next().ConfigureAwait(false);
        });

        _ = dispatcher.OnCommand("/admin", ctx =>
        {
            if (ctx.Items.TryGetValue("role", out object? roleObj))
            {
                retrievedRole = roleObj as string;
            }
            return Task.CompletedTask;
        });

        ZaloBotContext ctx = CreateContext("/admin");

        // Act
        await dispatcher.DispatchAsync(ctx);

        // Assert
        Assert.Equal("superadmin", retrievedRole);
    }

    [Fact]
    public async Task Middleware_CatchesDownstreamException()
    {
        // Arrange
        ZaloBotDispatcher dispatcher = new();
        string? caughtError = null;

        _ = dispatcher.Use(async (ctx, next) =>
        {
            try
            {
                await next().ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                caughtError = ex.Message;
            }
        });

        _ = dispatcher.OnCommand("/fail", ctx =>
        {
            throw new InvalidOperationException("Boom!");
        });

        ZaloBotContext ctx = CreateContext("/fail");

        // Act
        await dispatcher.DispatchAsync(ctx);

        // Assert
        Assert.Equal("Boom!", caughtError);
    }
}
