// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp.Tests;

/// <summary>
/// The WebSocket listener hands every message it receives to <see cref="MessageIngestPipeline"/>. A
/// reconnect replays the recent history (cmd 510/511), so the queue can receive a burst of messages at
/// once: each one must be written exactly once, in arrival order, without a single failure taking the
/// rest of the burst down with it.
/// </summary>
public sealed class MessageIngestPipelineTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir;
    private readonly ZaloDatabase _db;
    private readonly MessageRepository _repo;

    public MessageIngestPipelineTests(ITestOutputHelper output)
    {
        this._output = output;
        this._dir = Path.Combine(Path.GetTempPath(), "zalo-mcp-ingest-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(this._dir);

        this._db = new ZaloDatabase(Path.Combine(this._dir, "zalo_data.db"));
        this._db.Initialize();
        this._repo = new MessageRepository(this._db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(this._dir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task BurstOfRealtimeMessages_IsPersistedExactlyOnce()
    {
        await using MessageIngestPipeline queue = new(this._repo);

        const int burst = 200;
        Task[] writers =
        [
            .. Enumerable.Range(0, burst)
                .Select(i => Task.Run(() => queue.Enqueue(Message("burst-" + i.ToString(CultureInfo.InvariantCulture), i))))
        ];

        await Task.WhenAll(writers);
        await queue.DrainAsync(TimeSpan.FromSeconds(30));

        long stored = await this.ScalarAsync("SELECT COUNT(*) FROM messages;");

        this._output.WriteLine($"enqueued={queue.EnqueuedCount} persisted={queue.PersistedCount} failed={queue.FailedCount} stored={stored}");

        Assert.Equal(burst, queue.EnqueuedCount);
        Assert.Equal(burst, queue.PersistedCount);
        Assert.Equal(0, queue.FailedCount);
        Assert.Equal(burst, stored);

        // The CRM counters are read-modify-write rows: a pipeline that allowed overlapping saves would
        // lose some of these increments.
        Assert.Equal(burst, await this.ScalarAsync("SELECT total_messages FROM contact_insights WHERE user_id = '6586684611809565061';"));
    }

    [Fact]
    public async Task MessagesArrivingAfterDrainAreRejectedInsteadOfSilentlyLost()
    {
        MessageIngestPipeline queue = new(this._repo);
        Assert.True(queue.Enqueue(Message("before-drain", 0)));

        await queue.DrainAsync(TimeSpan.FromSeconds(30));

        Assert.False(queue.Enqueue(Message("after-drain", 1)));
        Assert.Equal(1, queue.PersistedCount);

        await queue.DisposeAsync();
    }

    private static ZaloMessageEvent Message(string msgId, int index) => new(
        MsgId: msgId,
        CliMsgId: "cli-" + msgId,
        MsgType: "webchat",
        UidFrom: "6586684611809565061",
        IdTo: "631140517573206063",
        DisplayName: "Phan Phuc Nguyen",
        ThreadId: "6586684611809565061",
        ThreadType: ZaloThreadType.User,
        TimestampMs: (1_789_000_000_000 + index).ToString(CultureInfo.InvariantCulture),
        Content: $"Tin realtime {index}: nhắc họp lúc 9h",
        Attachments: null,
        IsSelf: false);

    private async Task<long> ScalarAsync(string sql)
    {
        using SqliteConnection conn = new(this._db.ConnectionString);
        await conn.OpenAsync();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
}
