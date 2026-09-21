// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zalo.Net.Bot.Context;
using Zalo.Net.Bot.Routing;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;

namespace Zalo.Net.Bot.Engine;

/// <summary>
/// Execution engine managing background WebSocket listener, controlled-concurrency message queuing,
/// and dispatching events to handlers with full failure isolation.
/// </summary>
public sealed class ZaloBotEngine
{
    private readonly ZaloSession _session;
    private readonly IZaloClient _client;
    private readonly ZaloBotDispatcher _dispatcher;
    private readonly IWebProxy? _proxy;
    private readonly ILogger<ZaloBotEngine>? _logger;

    /// <summary>
    /// Occurs when an unhandled exception occurs inside a bot event handler.
    /// </summary>
    public event EventHandler<Exception>? OnError;

    /// <summary>Gets the maximum number of concurrent message handlers allowed to execute in parallel.</summary>
    public int MaxConcurrency { get; init; } = 50;

    /// <summary>Gets or sets optional service provider for dependency injection across context instances.</summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloBotEngine"/> class.
    /// </summary>
    public ZaloBotEngine(
        ZaloSession session,
        IZaloClient client,
        ZaloBotDispatcher dispatcher,
        IWebProxy? proxy = null,
        ILogger<ZaloBotEngine>? logger = null)
    {
        this._session = session ?? throw new ArgumentNullException(nameof(session));
        this._client = client ?? throw new ArgumentNullException(nameof(client));
        this._dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this._proxy = proxy;
        this._logger = logger;
    }

    /// <summary>
    /// Starts the background Zalo WebSocket listener and runs the controlled-concurrency Bot handler loop until cancelled.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (this._logger != null && this._logger.IsEnabled(LogLevel.Information))
        {
            this._logger.LogInformation("Zalo.Net.Bot Engine starting for Session UID {Uid} (MaxConcurrency: {Concurrency})", this._session.Uid, this.MaxConcurrency);
        }

        Channel<ZaloMessageEvent> channel = Channel.CreateUnbounded<ZaloMessageEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        using SemaphoreSlim semaphore = new(this.MaxConcurrency, this.MaxConcurrency);
        using CancellationTokenSource workerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        List<Task> runningTasks = [];
        Lock tasksLock = new();

        void OnMessageReceived(object? sender, ZaloMessageEvent msg)
        {
            if (msg.IsSelf || msg.UidFrom == this._session.Uid)
            {
                return;
            }

            if (!channel.Writer.TryWrite(msg))
            {
                this._logger?.LogWarning("Realtime Zalo message {MsgId} could not be enqueued into Bot Engine channel.", msg.MsgId);
            }
        }

        async Task ProcessQueueAsync(CancellationToken workerToken)
        {
            while (await channel.Reader.WaitToReadAsync(workerToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out ZaloMessageEvent? msg))
                {
                    if (workerToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await semaphore.WaitAsync(workerToken).ConfigureAwait(false);

                    Task handlerTask = Task.Run(async () =>
                    {
                        try
                        {
                            ZaloBotContext ctx = new(msg, this._session, this._client, this.Services);
                            await this._dispatcher.DispatchAsync(ctx, workerToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            this._logger?.LogError(ex, "Unhandled error dispatching message {MsgId} from {Sender}: {Error}", msg.MsgId, msg.DisplayName ?? msg.UidFrom, ex.Message);
                            this.OnError?.Invoke(this, ex);
                        }
                        finally
                        {
                            _ = semaphore.Release();
                        }
                    }, workerToken);

                    lock (tasksLock)
                    {
                        _ = runningTasks.RemoveAll(t => t.IsCompleted);
                        runningTasks.Add(handlerTask);
                    }
                }
            }
        }

        Task pumpTask = Task.Run(() => ProcessQueueAsync(workerCts.Token), CancellationToken.None);
        this._client.MessageReceived += OnMessageReceived;

        try
        {
            await this._client.RunWithReconnectAsync(this._session.Material, proxy: this._proxy, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (this._logger != null && this._logger.IsEnabled(LogLevel.Information))
            {
                this._logger.LogInformation("Zalo.Net.Bot Engine stopped gracefully for UID {Uid}.", this._session.Uid);
            }
        }
        catch (ZaloApiException ex)
        {
            if (this._logger != null && this._logger.IsEnabled(LogLevel.Error))
            {
                this._logger.LogError(ex, "Zalo.Net.Bot API Exception for UID {Uid}: {Message}", this._session.Uid, ex.Message);
            }
            throw;
        }
        finally
        {
            this._client.MessageReceived -= OnMessageReceived;
            _ = channel.Writer.TryComplete();

            try
            {
                await workerCts.CancelAsync().ConfigureAwait(false);
                await pumpTask.ConfigureAwait(false);
            }
            catch { }

            Task[] remaining;
            lock (tasksLock)
            {
                remaining = [.. runningTasks.Where(t => !t.IsCompleted)];
            }
            if (remaining.Length > 0)
            {
                try
                {
                    await Task.WhenAll(remaining).WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }
}
