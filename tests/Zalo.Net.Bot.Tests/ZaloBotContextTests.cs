// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Xunit;
using Zalo.Net.Auth;
using Zalo.Net.Bot.Context;
using Zalo.Net.Contracts;

namespace Zalo.Net.Bot.Tests;

public class ZaloBotContextTests
{
    private sealed class TrackingClient : IZaloClient
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

        public string? SentContactUserId { get; private set; }
        public string? SentBankBin { get; private set; }

        public Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloBankCard bankCard, CancellationToken ct = default)
        {
            this.SentBankBin = bankCard.BinBank;
            return Task.CompletedTask;
        }

        public Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string binBank, string accountNumber, string accountName, CancellationToken ct = default)
        {
            this.SentBankBin = binBank;
            return Task.CompletedTask;
        }

        public Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloContactCard contactCard, CancellationToken ct = default)
        {
            this.SentContactUserId = contactCard.UserId;
            return Task.CompletedTask;
        }

        public Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string userId, string? phoneNumber = null, string? qrCodeUrl = null, CancellationToken ct = default)
        {
            this.SentContactUserId = userId;
            return Task.CompletedTask;
        }

        public Task JoinGroupViaLinkAsync(ZaloSession session, string inviteUrl, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReviewJoinRequestsAsync(ZaloSession session, string groupId, string[] memberUids, bool approve, CancellationToken ct = default) => Task.CompletedTask;
        public Task LeaveGroupSilentlyAsync(ZaloSession session, string groupId, CancellationToken ct = default) => Task.CompletedTask;
        public Task KickGroupMemberAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default) => Task.CompletedTask;
        public Task PromoteGroupAdminAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default) => Task.CompletedTask;
        public Task PinGroupMessageAsync(ZaloSession session, string groupId, string msgId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendImageAsync(ZaloSession session, string threadId, ZaloThreadType threadType, byte[] imageBytes, string fileName, string? caption = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (ZaloBotContext Context, TrackingClient Client) CreateContext(string text)
    {
        TrackingClient client = new();
        ZaloSessionMaterial material = new("[]", "key_1", "imei_1", "uid_1", "ua");
        ZaloSession session = new(material, "uid_1", ["wss://zalo"], new Dictionary<string, string[]>(), 30000);
        ZaloMessageEvent msg = new("msg_1", "cli_1", "text", "uid_2", "uid_1", "Sender", "uid_2", ZaloThreadType.User, "12345", text, null, false);
        return (new ZaloBotContext(msg, session, client), client);
    }

    [Theory]
    [InlineData("/help", "/help", "", 0)]
    [InlineData("!ping now", "!ping", "now", 1)]
    [InlineData(".echo hello world", ".echo", "hello world", 2)]
    [InlineData("/ban @user 7d \"Spamming links\"", "/ban", "@user 7d \"Spamming links\"", 3)]
    [InlineData("/say 'single quoted arg' trailing", "/say", "'single quoted arg' trailing", 2)]
    public void Context_ParsesCommandAndArguments_Correctly(string input, string expectedCommand, string expectedRaw, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Act
        (ZaloBotContext ctx, _) = CreateContext(input);

        // Assert
        Assert.Equal(expectedCommand, ctx.Command);
        Assert.Equal(expectedRaw, ctx.RawArguments);
        Assert.Equal(expectedCount, ctx.Arguments.Count);

        if (input.Contains("\"Spamming links\"", StringComparison.Ordinal))
        {
            Assert.Equal("Spamming links", ctx.Arguments[2]);
        }
        else if (input.Contains("'single quoted arg'", StringComparison.Ordinal))
        {
            Assert.Equal("single quoted arg", ctx.Arguments[0]);
        }
    }

    [Fact]
    public void Context_NonCommandMessage_HasNullCommandAndEmptyArguments()
    {
        // Act
        (ZaloBotContext ctx, _) = CreateContext("Hello there!");

        // Assert
        Assert.Null(ctx.Command);
        Assert.Equal(string.Empty, ctx.RawArguments);
        Assert.Empty(ctx.Arguments);
    }

    [Fact]
    public void Context_CookieStore_ReturnsCachedInstance()
    {
        // Act
        (ZaloBotContext ctx, _) = CreateContext("/test");

        CookieStore store1 = ctx.CookieStore;
        CookieStore store2 = ctx.CookieStore;

        // Assert
        Assert.NotNull(store1);
        Assert.Same(store1, store2);
    }

    [Fact]
    public async Task Context_ReplyContactCard_InvokesClientDirectly()
    {
        // Act
        (ZaloBotContext ctx, TrackingClient client) = CreateContext("/contact");

        await ctx.ReplyContactCardAsync("target_uid_99", phoneNumber: "0900000000");

        // Assert
        Assert.Equal("target_uid_99", client.SentContactUserId);
    }

    [Fact]
    public async Task Context_ReplyBankCard_ReusesClientDirectly()
    {
        // Act
        (ZaloBotContext ctx, TrackingClient client) = CreateContext("/bank");

        await ctx.ReplyBankCardAsync("970422", "123456789", "NGUYEN VAN A");

        // Assert
        Assert.Equal("970422", client.SentBankBin);
    }
}
