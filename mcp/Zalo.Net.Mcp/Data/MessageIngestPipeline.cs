// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Threading.Channels;
using Zalo.Net.Contracts;

namespace Zalo.Net.Mcp.Data;

/// <summary>
/// The write pipeline for the messages pushed by the Zalo WebSocket.
/// <para>
/// The event handler used to be an <c>async void</c> that saved each message inline, so a burst (a
/// reconnect replays the recent history, cmd 510/511) started one overlapping save per message: the
/// writes raced each other for the same SQLite file, the message that finished first — not the one
/// that arrived first — was the one counted, and an exception thrown inside the handler had nowhere to
/// go but the process-wide unhandled exception handler.
/// </para>
/// <para>
/// Messages are now handed to a single ordered reader, which writes them one at a time and survives a
/// failing message without losing the ones behind it. <see cref="EnqueuedCount"/>,
/// <see cref="PersistedCount"/> and <see cref="FailedCount"/> make the pipeline observable.
/// </para>
/// </summary>
public sealed class MessageIngestPipeline : IAsyncDisposable
{
    private readonly MessageRepository _repository;
    private readonly ILogger? _logger;
    private readonly Channel<ZaloMessageEvent> _channel;
    private readonly Task _pump;
    private long _enqueued;
    private long _persisted;
    private long _failed;

    public MessageIngestPipeline(MessageRepository repository, ILogger? logger = null)
    {
        this._repository = repository;
        this._logger = logger;
        this._channel = Channel.CreateUnbounded<ZaloMessageEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        this._pump = Task.Run(this.PumpAsync);
    }

    /// <summary>Messages handed to the queue since the process started.</summary>
    public long EnqueuedCount => Interlocked.Read(ref this._enqueued);

    /// <summary>Messages written to SQLite since the process started.</summary>
    public long PersistedCount => Interlocked.Read(ref this._persisted);

    /// <summary>Messages that could not be written (they are logged and skipped, never retried).</summary>
    public long FailedCount => Interlocked.Read(ref this._failed);

    /// <summary>
    /// Queues a realtime message for persistence. Returns <see langword="false"/> once the queue has
    /// been drained for shutdown, so the caller knows the message was not accepted.
    /// </summary>
    public bool Enqueue(ZaloMessageEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!this._channel.Writer.TryWrite(message))
        {
            this._logger?.LogWarning("Realtime Zalo message {MsgId} was not queued: the ingest queue is closed.", message.MsgId);
            return false;
        }

        _ = Interlocked.Increment(ref this._enqueued);
        return true;
    }

    private async Task PumpAsync()
    {
        await foreach (ZaloMessageEvent message in this._channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await this._repository.SaveMessageAsync(message).ConfigureAwait(false);
                _ = Interlocked.Increment(ref this._persisted);
            }
            catch (Exception ex)
            {
                // One unwritable message must not stop the pipeline for every message after it.
                _ = Interlocked.Increment(ref this._failed);
                this._logger?.LogError(ex, "Failed to save realtime Zalo message {MsgId} to SQLite database.", message.MsgId);
            }
        }
    }

    /// <summary>
    /// Stops accepting messages and waits for the ones already queued to reach the database.
    /// </summary>
    public async Task DrainAsync(TimeSpan timeout)
    {
        _ = this._channel.Writer.TryComplete();

        try
        {
            await this._pump.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            this._logger?.LogWarning("The message ingest queue did not drain in time; some realtime messages may not be stored.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.DrainAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }
}
