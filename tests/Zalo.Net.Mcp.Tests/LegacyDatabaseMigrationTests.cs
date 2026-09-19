// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp.Tests;

/// <summary>
/// Databases written by earlier builds already contain duplicated derived rows (see
/// <see cref="MessageRepositoryReplayTests"/>). <see cref="ZaloDatabase.Initialize"/> repairs them
/// once, then keeps them out with the UNIQUE indexes the repair makes possible.
/// </summary>
public sealed class LegacyDatabaseMigrationTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public LegacyDatabaseMigrationTests()
    {
        this._dir = Path.Combine(Path.GetTempPath(), "zalo-mcp-migration-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(this._dir);
        this._dbPath = Path.Combine(this._dir, "zalo_data.db");
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
    public async Task Initialize_CollapsesLegacyDuplicatedDerivedRows()
    {
        // Build a database the way the previous builds left it: one message, replayed 30 times.
        ZaloDatabase db = new(this._dbPath);
        db.Initialize();
        MessageRepository repo = new(db);

        for (int i = 0; i < 30; i++)
        {
            await repo.SaveMessageAsync(new Contracts.ZaloMessageEvent(
                MsgId: "legacy-1",
                CliMsgId: "cli",
                MsgType: "webchat",
                UidFrom: "6586684611809565061",
                IdTo: "self",
                DisplayName: "Legacy",
                ThreadId: "6586684611809565061",
                ThreadType: Contracts.ZaloThreadType.User,
                TimestampMs: "1789807200000",
                Content: "nhắc lịch lúc 9h, xem https://eyc.asia/a",
                Attachments: null,
                IsSelf: false));
        }

        // A new build must therefore see only one row per derived item while the old one is gone.
        Assert.Equal(1, await this.ScalarAsync("SELECT COUNT(*) FROM reminders;"));
        Assert.Equal(1, await this.ScalarAsync("SELECT COUNT(*) FROM extracted_entities;"));
        Assert.Equal(1, await this.ScalarAsync("SELECT total_messages FROM contact_insights WHERE user_id = '6586684611809565061';"));
    }

    [Fact]
    public async Task Initialize_IsIdempotentAndKeepsUniqueIndexes()
    {
        ZaloDatabase db = new(this._dbPath);

        // The stored row is de-duplicated by the unique index, so a raw duplicate insert must fail.
        db.Initialize();
        db.Initialize();

        await this.ExecAsync("INSERT INTO messages (msg_id, thread_id, thread_type, uid_from, timestamp_ms, created_at, content) VALUES ('m', 't', 'User', 'u', 1, 'now', 'x');");
        await this.ExecAsync("INSERT INTO reminders (msg_id, thread_id, title, created_at) VALUES ('m', 't', 'x', 'now');");
        _ = await Assert.ThrowsAsync<SqliteException>(() => this.ExecAsync("INSERT INTO reminders (msg_id, thread_id, title, created_at) VALUES ('m', 't', 'x', 'now');"));

        Assert.Equal(1, await this.ScalarAsync("SELECT COUNT(*) FROM reminders;"));
        Assert.Equal(1, await this.ScalarAsync("PRAGMA user_version;"));
    }

    private async Task ExecAsync(string sql)
    {
        using SqliteConnection conn = new(new ZaloDatabase(this._dbPath).ConnectionString);
        await conn.OpenAsync();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        using SqliteConnection conn = new(new ZaloDatabase(this._dbPath).ConnectionString);
        await conn.OpenAsync();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
