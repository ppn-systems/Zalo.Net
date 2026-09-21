// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Xunit;
using Zalo.Net.Bot.Engine;
using Zalo.Net.Bot.Routing;
using Zalo.Net.Contracts;

namespace Zalo.Net.Bot.Tests;

public class ZaloBotEngineTests
{
    private sealed class MockClient : IZaloClient
    {
        public IWebProxy? Proxy => null;
        public event EventHandler<ZaloMessageEvent>? MessageReceived;
        public event EventHandler<ZaloSessionStatusChanged>? StatusChanged;

        public TaskCompletionSource<bool> RunningStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void TriggerMessage(ZaloMessageEvent msg) => this.MessageReceived?.Invoke(this, msg);
        public void TriggerStatus(ZaloSessionStatusChanged status) => this.StatusChanged?.Invoke(this, status);

        public void Dispose() { }
        public Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ZaloLoginState> PollQrStatusAsync(Guid sessionId, CancellationToken ct = default) => throw new NotImplementedException();
        public ZaloSessionMaterial? ConsumePendingMaterial(Guid sessionId) => null;
        public Task StartListenerAsync(ZaloSession session, CancellationToken ct = default) => Task.CompletedTask;

        public async Task RunWithReconnectAsync(ZaloSessionMaterial material, IWebProxy? proxy = null, CancellationToken ct = default)
        {
            _ = this.RunningStarted.TrySetResult(true);
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

    private static ZaloSession CreateSession()
    {
        ZaloSessionMaterial material = new("[]", "key_1", "imei_1", "uid_1", "ua");
        return new ZaloSession(material, "uid_1", ["wss://zalo"], new Dictionary<string, string[]>(), 30000);
    }

    private static ZaloMessageEvent CreateMsg(string id, string text) =>
        new(id, "cli_1", "text", "uid_2", "uid_1", "Sender", "uid_2", ZaloThreadType.User, "12345", text, null, false);

    [Fact]
    public async Task Engine_ProcessesMessagesThroughChannelQueue()
    {
        // Arrange
        MockClient client = new();
        ZaloSession session = CreateSession();
        ZaloBotDispatcher dispatcher = new();
        TaskCompletionSource<string> messageProcessedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = dispatcher.OnCommand("/test", ctx =>
        {
            _ = messageProcessedTcs.TrySetResult(ctx.RawArguments);
            return Task.CompletedTask;
        });

        ZaloBotEngine engine = new(session, client, dispatcher);
        using CancellationTokenSource cts = new();

        Task engineTask = Task.Run(() => engine.StartAsync(cts.Token));
        _ = await client.RunningStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        client.TriggerMessage(CreateMsg("m1", "/test payload_123"));

        // Assert
        string result = await messageProcessedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("payload_123", result);

        // Cleanup
        await cts.CancelAsync();
        await engineTask;
    }

    [Fact]
    public async Task Engine_LimitsConcurrency_RespectsMaxConcurrency()
    {
        // Arrange
        MockClient client = new();
        ZaloSession session = CreateSession();
        ZaloBotDispatcher dispatcher = new();

        int concurrentCount = 0;
        int peakConcurrency = 0;
        object countLock = new();

        _ = dispatcher.OnCommand("/work", async ctx =>
        {
            lock (countLock)
            {
                concurrentCount++;
                if (concurrentCount > peakConcurrency)
                {
                    peakConcurrency = concurrentCount;
                }
            }

            await Task.Delay(100).ConfigureAwait(false);

            lock (countLock)
            {
                concurrentCount--;
            }
        });

        ZaloBotEngine engine = new(session, client, dispatcher)
        {
            MaxConcurrency = 2
        };

        using CancellationTokenSource cts = new();
        Task engineTask = Task.Run(() => engine.StartAsync(cts.Token));
        _ = await client.RunningStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act: Enqueue 6 messages
        for (int i = 0; i < 6; i++)
        {
            client.TriggerMessage(CreateMsg($"m_{i}", "/work"));
        }

        // Wait a bit for processing
        await Task.Delay(400);

        // Assert: Peak concurrency should never exceed MaxConcurrency (2)
        Assert.True(peakConcurrency <= 2, $"Peak concurrency was {peakConcurrency}, expected <= 2");

        // Cleanup
        await cts.CancelAsync();
        await engineTask;
    }

    [Fact]
    public async Task Engine_RaisesOnError_WhenHandlerThrows()
    {
        // Arrange
        MockClient client = new();
        ZaloSession session = CreateSession();
        ZaloBotDispatcher dispatcher = new();
        TaskCompletionSource<Exception> errorTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = dispatcher.OnCommand("/fail", ctx =>
        {
            throw new InvalidOperationException("Engine error test");
        });

        ZaloBotEngine engine = new(session, client, dispatcher);
        engine.OnError += (sender, ex) =>
        {
            _ = errorTcs.TrySetResult(ex);
        };

        using CancellationTokenSource cts = new();
        Task engineTask = Task.Run(() => engine.StartAsync(cts.Token));
        _ = await client.RunningStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        client.TriggerMessage(CreateMsg("m_err", "/fail"));

        // Assert
        Exception caughtEx = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Engine error test", caughtEx.Message);

        // Cleanup
        await cts.CancelAsync();
        await engineTask;
    }
}
