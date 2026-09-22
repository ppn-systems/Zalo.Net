// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Xunit;
using Zalo.Net.Bot.Cluster;
using Zalo.Net.Contracts;

namespace Zalo.Net.Bot.Tests;

public class ZaloBotClusterTests
{
    private sealed class TestMockClient : IZaloClient
    {
        public IWebProxy? Proxy => null;

        public event EventHandler<ZaloMessageEvent>? MessageReceived;
        public event EventHandler<ZaloSessionStatusChanged>? StatusChanged;

        public TaskCompletionSource<bool> ListenerStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void TriggerMessage(ZaloMessageEvent msg) => this.MessageReceived?.Invoke(this, msg);
        public void TriggerStatus(ZaloSessionStatusChanged status) => this.StatusChanged?.Invoke(this, status);

        public void Dispose() { }

        public Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ZaloLoginState> PollQrStatusAsync(Guid sessionId, CancellationToken ct = default) => throw new NotImplementedException();
        public ZaloSessionMaterial? ConsumePendingMaterial(Guid sessionId) => null;
        public Task StartListenerAsync(ZaloSession session, CancellationToken ct = default) => Task.CompletedTask;

        public async Task RunWithReconnectAsync(ZaloSessionMaterial material, IWebProxy? proxy = null, CancellationToken ct = default)
        {
            _ = this.ListenerStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

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

    private static ZaloSession CreateSession(string uid)
    {
        ZaloSessionMaterial material = new(
            CookiesJson: "[]",
            SecretKey: $"key-{uid}",
            Imei: $"imei-{uid}",
            Uid: uid,
            UserAgent: "TestUA");

        return new ZaloSession(
            Material: material,
            Uid: uid,
            WsUrls: ["wss://test.zalo.me/ws"],
            ServiceMap: new Dictionary<string, string[]>(),
            PingIntervalMs: 30000);
    }

    [Fact]
    public void ZaloBotCluster_RegistersMultipleAccounts_MaintainsIsolation()
    {
        using ZaloBotCluster cluster = new();
        ZaloSession session1 = CreateSession("acc_1");
        ZaloSession session2 = CreateSession("acc_2");

        _ = cluster.AddAccount(session1);
        _ = cluster.AddAccount(session2);

        Assert.Equal(2, cluster.AccountCount);
        Assert.Contains("acc_1", cluster.AccountUids);
        Assert.Contains("acc_2", cluster.AccountUids);
        Assert.NotNull(cluster.GetEngine("acc_1"));
        Assert.NotNull(cluster.GetEngine("acc_2"));
        Assert.Equal("acc_1", cluster.GetSession("acc_1")?.Uid);
        Assert.Equal("acc_2", cluster.GetSession("acc_2")?.Uid);
    }

    [Fact]
    public async Task ZaloBotCluster_DispatchesIncomingMessages_ToCorrectAccountContext()
    {
        TestMockClient client1 = new();
        TestMockClient client2 = new();
        ZaloSession session1 = CreateSession("acc_1");
        ZaloSession session2 = CreateSession("acc_2");

        List<string> handledByAccountUids = [];
        using CancellationTokenSource cts = new();

        using ZaloBotCluster cluster = ZaloBotClusterBuilder.Create()
            .AddAccount(session1, client1)
            .AddAccount(session2, client2)
            .OnCommand("/ping", ctx =>
            {
                lock (handledByAccountUids)
                {
                    handledByAccountUids.Add(ctx.Session.Uid);
                }
                return Task.CompletedTask;
            })
            .Build();

        Task runTask = cluster.StartAllAsync(cts.Token);
        _ = await Task.WhenAll(client1.ListenerStarted.Task, client2.ListenerStarted.Task);

        // Send /ping to Account 1
        client1.TriggerMessage(new ZaloMessageEvent(
            MsgId: "msg_1",
            CliMsgId: "cli_1",
            MsgType: "chat.text",
            UidFrom: "sender_user",
            IdTo: "acc_1",
            DisplayName: "Sender",
            ThreadId: "thread_1",
            ThreadType: ZaloThreadType.User,
            TimestampMs: "1000",
            Content: "/ping",
            Attachments: null,
            IsSelf: false));

        // Send /ping to Account 2
        client2.TriggerMessage(new ZaloMessageEvent(
            MsgId: "msg_2",
            CliMsgId: "cli_2",
            MsgType: "chat.text",
            UidFrom: "sender_user",
            IdTo: "acc_2",
            DisplayName: "Sender",
            ThreadId: "thread_2",
            ThreadType: ZaloThreadType.User,
            TimestampMs: "1001",
            Content: "/ping",
            Attachments: null,
            IsSelf: false));

        await Task.Delay(100);

        Assert.Equal(2, handledByAccountUids.Count);
        Assert.Contains("acc_1", handledByAccountUids);
        Assert.Contains("acc_2", handledByAccountUids);

        await cts.CancelAsync();
        await runTask;
    }

    [Fact]
    public async Task ZaloBotCluster_AccountError_DoesNotAffectOtherAccounts()
    {
        TestMockClient client1 = new();
        TestMockClient client2 = new();
        ZaloSession session1 = CreateSession("acc_faulty");
        ZaloSession session2 = CreateSession("acc_healthy");

        using CancellationTokenSource cts = new();
        string? errorAccountUid = null;
        List<string> successfulUids = [];

        using ZaloBotCluster cluster = ZaloBotClusterBuilder.Create()
            .AddAccount(session1, client1)
            .AddAccount(session2, client2)
            .OnCommand("/test", ctx =>
            {
                if (ctx.Session.Uid == "acc_faulty")
                {
                    throw new InvalidOperationException("Simulated failure in account 1");
                }

                lock (successfulUids)
                {
                    successfulUids.Add(ctx.Session.Uid);
                }
                return Task.CompletedTask;
            })
            .Build();

        cluster.OnAccountError += (_, args) => errorAccountUid = args.Uid;

        Task runTask = cluster.StartAllAsync(cts.Token);
        _ = await Task.WhenAll(client1.ListenerStarted.Task, client2.ListenerStarted.Task);

        // Trigger command on faulty account
        client1.TriggerMessage(new ZaloMessageEvent(
            MsgId: "msg_err",
            CliMsgId: "cli_err",
            MsgType: "chat.text",
            UidFrom: "sender_user",
            IdTo: "acc_faulty",
            DisplayName: "Sender",
            ThreadId: "thread_err",
            ThreadType: ZaloThreadType.User,
            TimestampMs: "1000",
            Content: "/test",
            Attachments: null,
            IsSelf: false));

        // Trigger command on healthy account
        client2.TriggerMessage(new ZaloMessageEvent(
            MsgId: "msg_ok",
            CliMsgId: "cli_ok",
            MsgType: "chat.text",
            UidFrom: "sender_user",
            IdTo: "acc_healthy",
            DisplayName: "Sender",
            ThreadId: "thread_ok",
            ThreadType: ZaloThreadType.User,
            TimestampMs: "1001",
            Content: "/test",
            Attachments: null,
            IsSelf: false));

        await Task.Delay(100);

        Assert.Equal("acc_faulty", errorAccountUid);
        _ = Assert.Single(successfulUids);
        Assert.Equal("acc_healthy", successfulUids[0]);

        await cts.CancelAsync();
        await runTask;
    }

    [Fact]
    public void ZaloBotCluster_DynamicAddAndRemoveAccount()
    {
        using ZaloBotCluster cluster = new();
        ZaloSession session1 = CreateSession("acc_1");
        ZaloSession session2 = CreateSession("acc_2");

        _ = cluster.AddAccount(session1);
        _ = cluster.AddAccount(session2);
        Assert.Equal(2, cluster.AccountCount);

        bool removed = cluster.RemoveAccount("acc_1");
        Assert.True(removed);
        Assert.Equal(1, cluster.AccountCount);
        Assert.Null(cluster.GetEngine("acc_1"));
        Assert.NotNull(cluster.GetEngine("acc_2"));

        bool removeNonExistent = cluster.RemoveAccount("acc_non_existent");
        Assert.False(removeNonExistent);
    }
}
