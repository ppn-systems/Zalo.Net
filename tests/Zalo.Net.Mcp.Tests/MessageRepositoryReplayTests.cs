// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
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
/// Reproduces what the WebSocket listener does after every reconnect: it replays recently received
/// messages (history sync, cmd 510/511) through <see cref="MessageRepository.SaveMessageAsync"/>.
/// The <c>messages</c> row is de-duplicated by the ON CONFLICT clause, but the rows derived from a
/// message (extracted entities, reminders, CRM counters) must be written exactly once as well —
/// otherwise the database grows without bound and every read of those tables gets slower.
/// </summary>
public sealed class MessageRepositoryReplayTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir;
    private readonly ZaloDatabase _db;
    private readonly MessageRepository _repo;

    public MessageRepositoryReplayTests(ITestOutputHelper output)
    {
        this._output = output;
        this._dir = Path.Combine(Path.GetTempPath(), "zalo-mcp-replay-" + Guid.NewGuid().ToString("N"));
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
            // temp directory is best-effort cleanup
        }
    }

    [Fact]
    public async Task ReplayedHistorySync_DoesNotDuplicateDerivedRows()
    {
        ZaloMessageEvent msg = Message("8279337478975", "Nhắc hẹn lúc 9h sáng: gọi 0912345678 hoặc xem https://eyc.asia/hoc-phi");

        // 20 replays of the same message = 20 sync rounds of the same history window.
        for (int i = 0; i < 20; i++)
        {
            await this._repo.SaveMessageAsync(msg);
        }

        Assert.Equal(1, await this.CountAsync("messages"));
        Assert.Equal(2, await this.CountAsync("extracted_entities")); // one phone + one url
        Assert.Equal(1, await this.CountAsync("reminders"));
        Assert.Equal(1, await this.ScalarAsync("SELECT total_messages FROM contact_insights WHERE user_id = '6586684611809565061'"));
    }

    [Fact]
    public async Task ReplayWithChangedContent_RebuildsDerivedRowsAndRefreshesFlags()
    {
        await this._repo.SaveMessageAsync(Message("m1", "Nhắc hẹn lúc 9h, xem https://eyc.asia/a"));
        await this._repo.SaveMessageAsync(Message("m1", "Khiếu nại khẩn cấp! Xem https://eyc.asia/b"));

        Assert.Equal(1, await this.CountAsync("messages"));
        Assert.Equal(1, await this.CountAsync("extracted_entities"));
        Assert.Equal("https://eyc.asia/b", await this.ScalarStringAsync("SELECT value FROM extracted_entities LIMIT 1"));
        Assert.Equal(0, await this.CountAsync("reminders")); // the new content has no schedule keyword
        Assert.Equal(1, await this.ScalarAsync("SELECT is_urgent FROM messages WHERE msg_id = 'm1'"));
        Assert.Equal(1, await this.ScalarAsync("SELECT total_messages FROM contact_insights WHERE user_id = '6586684611809565061'"));
    }

    [Fact]
    public async Task ReplayOfSavedHistory_StaysBoundedInTimeAndRows()
    {
        ZaloMessageEvent[] batch =
        [
            .. Enumerable.Range(0, 40).Select(i =>
                Message($"bench-{i}", $"Tin {i}: nhắc lịch lúc 8h, chi tiết tại https://eyc.asia/tin-{i}"))
        ];

        foreach (ZaloMessageEvent m in batch)
        {
            await this._repo.SaveMessageAsync(m);
        }

        int replays = 25;
        Stopwatch sw = Stopwatch.StartNew();
        for (int round = 0; round < replays; round++)
        {
            foreach (ZaloMessageEvent m in batch)
            {
                await this._repo.SaveMessageAsync(m);
            }
        }

        sw.Stop();

        long messages = await this.CountAsync("messages");
        long entities = await this.CountAsync("extracted_entities");
        long reminders = await this.CountAsync("reminders");

        this._output.WriteLine($"{replays * batch.Length} replay saves: {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds / (replays * batch.Length):F2} ms/save)");
        this._output.WriteLine($"rows after replay: messages={messages} entities={entities} reminders={reminders}");

        Assert.Equal(batch.Length, messages);
        Assert.Equal(batch.Length, entities);
        Assert.Equal(batch.Length, reminders);
    }

    private static ZaloMessageEvent Message(string msgId, string content) => new(
        MsgId: msgId,
        CliMsgId: "cli-" + msgId,
        MsgType: "webchat",
        UidFrom: "6586684611809565061",
        IdTo: "631140517573206063",
        DisplayName: "Phan Phuc Nguyen",
        ThreadId: "6586684611809565061",
        ThreadType: ZaloThreadType.User,
        TimestampMs: "1789807200000",
        Content: content,
        Attachments: null,
        IsSelf: false);

    private async Task<long> CountAsync(string table)
    {
        string sql = $"SELECT COUNT(*) FROM {table};";
        return await this.ScalarAsync(sql);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        using SqliteConnection conn = new(this._db.ConnectionString);
        await conn.OpenAsync();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private async Task<string> ScalarStringAsync(string sql)
    {
        using SqliteConnection conn = new(this._db.ConnectionString);
        await conn.OpenAsync();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? value = await cmd.ExecuteScalarAsync();
        return value as string ?? string.Empty;
    }
}
