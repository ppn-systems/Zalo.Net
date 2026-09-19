// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
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
/// Covers the read path of the MCP tools: the full-text index has to match Vietnamese text typed
/// without accents, the smart search has to look at the whole table instead of at the page it fetched,
/// and the chat summary has to report the real number of messages of a thread.
/// </summary>
public sealed class MessageRepositoryQueryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir;
    private readonly string _dbPath;
    private readonly ZaloDatabase _db;
    private readonly MessageRepository _repo;

    public MessageRepositoryQueryTests(ITestOutputHelper output)
    {
        this._output = output;
        this._dir = Path.Combine(Path.GetTempPath(), "zalo-mcp-query-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(this._dir);
        this._dbPath = Path.Combine(this._dir, "zalo_data.db");

        this._db = new ZaloDatabase(this._dbPath);
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

    [Theory]
    [InlineData("tai lieu")]
    [InlineData("hoc phi")]
    [InlineData("hop phu huynh")]
    public async Task SearchMessages_MatchesVietnameseTextTypedWithoutDiacritics(string query)
    {
        await this._repo.SaveMessageAsync(Message("vn-1", "Tài liệu họp phụ huynh và học phí tháng 9", "t-vn", 1_789_800_000_000));

        IReadOnlyList<SavedMessage> hits = await this._repo.SearchMessagesAsync(query);

        this._output.WriteLine($"query \"{query}\" -> {hits.Count} hit(s)");
        _ = Assert.Single(hits);
        Assert.Equal("vn-1", hits[0].MsgId);
    }

    [Fact]
    public async Task SearchMessages_StillMatchesAccentedText()
    {
        await this._repo.SaveMessageAsync(Message("vn-2", "Học phí tháng 9 của lớp 1A", "t-vn", 1_789_800_000_000));

        _ = Assert.Single(await this._repo.SearchMessagesAsync("học phí"));
        _ = Assert.Single(await this._repo.SearchMessagesAsync("HỌC PHÍ"));
    }

    [Fact]
    public async Task Initialize_RebuildsALegacyFullTextIndexAndBackfillsItOnce()
    {
        await this._repo.SaveMessageAsync(Message("legacy-fts", "Tài liệu họp phụ huynh lớp 1A", "t-vn", 1_789_800_000_000));

        // Put the index back the way an earlier build created it: without the diacritic-insensitive
        // tokenizer, and with the indexed rows missing (only the triggers kept it in step).
        await this.ExecuteAsync("""
            DROP TABLE messages_fts;
            CREATE VIRTUAL TABLE messages_fts USING fts5(
                msg_id UNINDEXED,
                thread_id UNINDEXED,
                display_name,
                content
            );
            """);

        Assert.Empty(await this._repo.SearchMessagesAsync("tai lieu")); // unaccented search used to find nothing

        ZaloDatabase reopened = new(this._dbPath);
        reopened.Initialize();

        IReadOnlyList<SavedMessage> hits = await this._repo.SearchMessagesAsync("tai lieu");
        _ = Assert.Single(hits);
        Assert.Equal("legacy-fts", hits[0].MsgId);

        // Running the migration again must not index the same message twice.
        reopened.Initialize();
        _ = Assert.Single(await this._repo.SearchMessagesAsync("tai lieu"));
        Assert.Equal(1, await this.ScalarAsync("SELECT COUNT(*) FROM messages_fts;"));
    }

    [Fact]
    public async Task SmartSearch_FindsAnEntityThatIsOlderThanThePageItReturns()
    {
        // The bank account is the oldest row; everything after it is unrelated chatter. Filtering the
        // fetched page in memory could never see it once the page was filled with newer rows.
        await this._repo.SaveMessageAsync(Message("pay-1", "Số tài khoản: 970458 nhận học phí tháng 9", "t-pay", 1_789_000_000_000));
        await this.SaveChatterAsync("noise", 120);

        SmartSearchResult result = await this._repo.SmartSearchAsync("970458", limit: 10);

        this._output.WriteLine($"entities={result.ExtractedEntities.Count} reminders={result.Reminders.Count} messages={result.Messages.Count}");
        Assert.Contains(result.ExtractedEntities, e => e.MsgId == "pay-1");
    }

    [Fact]
    public async Task SmartSearch_FindsAReminderThatIsOlderThanThePageItReturns()
    {
        await this._repo.SaveMessageAsync(Message("hop-1", "Nhắc họp phụ huynh lớp 1A lúc 9h sáng thứ bảy", "t-hop", 1_789_000_000_000));
        await this.SaveChatterAsync("noise", 120);

        SmartSearchResult result = await this._repo.SmartSearchAsync("1A", limit: 10);

        Assert.Contains(result.Reminders, r => r.MsgId == "hop-1");
    }

    [Fact]
    public async Task GetRecentThreads_CountsEveryMessageAndReportsTheLatestOne()
    {
        // Thread A gets three messages, inserted oldest-last so the "latest" one is not simply the row
        // that was written last.
        await this._repo.SaveMessageAsync(Message("a-2", "Tin giữa", "thread-a", 1_789_000_002_000));
        await this._repo.SaveMessageAsync(Message("a-3", "Tin mới nhất", "thread-a", 1_789_000_003_000));
        await this._repo.SaveMessageAsync(Message("a-1", "Tin cũ nhất", "thread-a", 1_789_000_001_000));
        await this._repo.SaveMessageAsync(Message("b-1", "Thread B", "thread-b", 1_789_000_000_500));

        IReadOnlyList<SavedThread> threads = await this._repo.GetRecentThreadsAsync();

        Assert.Equal(2, threads.Count);
        SavedThread threadA = threads.Single(t => t.ThreadId == "thread-a");
        SavedThread threadB = threads.Single(t => t.ThreadId == "thread-b");

        Assert.Equal(3, threadA.MessageCount);
        Assert.Equal("Tin mới nhất", threadA.LastMessageContent);
        Assert.Equal(1_789_000_003_000, threadA.LastMessageTimestampMs);
        Assert.Equal(1, threadB.MessageCount);
    }

    private async Task SaveChatterAsync(string prefix, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await this._repo.SaveMessageAsync(Message(
                $"{prefix}-{i}",
                $"Trao đổi {i} về lịch học và thời khoá biểu",
                $"thread-{prefix}-{i % 4}",
                1_789_100_000_000 + i));
        }
    }

    private static ZaloMessageEvent Message(string msgId, string content, string threadId, long timestampMs) => new(
        MsgId: msgId,
        CliMsgId: "cli-" + msgId,
        MsgType: "webchat",
        UidFrom: "6586684611809565061",
        IdTo: "631140517573206063",
        DisplayName: "Phan Phuc Nguyen",
        ThreadId: threadId,
        ThreadType: ZaloThreadType.User,
        TimestampMs: timestampMs.ToString(CultureInfo.InvariantCulture),
        Content: content,
        Attachments: null,
        IsSelf: false);

    private async Task ExecuteAsync(string sql)
    {
        using SqliteConnection conn = new(this._db.ConnectionString);
        await conn.OpenAsync();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = await cmd.ExecuteNonQueryAsync();
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
}
