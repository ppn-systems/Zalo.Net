// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Zalo.Net.Contracts;

namespace Zalo.Net.Mcp.Data;

/// <summary>
/// DTO representing a persisted chat message record.
/// </summary>
public sealed record SavedMessage(
    string MsgId,
    string CliMsgId,
    string ThreadId,
    string ThreadType,
    string UidFrom,
    string DisplayName,
    string Content,
    string MsgType,
    string? AttachmentsJson,
    bool IsSelf,
    bool IsUrgent,
    long TimestampMs,
    string CreatedAt);

/// <summary>
/// DTO representing a chat thread summary.
/// </summary>
public sealed record SavedThread(
    string ThreadId,
    string ThreadType,
    string LastMessageContent,
    long LastMessageTimestampMs,
    int MessageCount);

/// <summary>
/// DTO representing an extracted smart entity (bank account, phone, link, email).
/// </summary>
public sealed record ExtractedEntity(
    long Id,
    string MsgId,
    string ThreadId,
    string EntityType,
    string Value,
    string? RawText,
    string CreatedAt);

/// <summary>
/// DTO representing an extracted appointment or reminder.
/// </summary>
public sealed record ExtractedReminder(
    long Id,
    string MsgId,
    string ThreadId,
    string Title,
    string? EventTime,
    string? RawText,
    string CreatedAt);

/// <summary>
/// DTO representing CRM memory insights for a Zalo contact.
/// </summary>
public sealed record ContactInsight(
    string UserId,
    string? DisplayName,
    string FirstSeenAt,
    string LastActiveAt,
    int TotalMessages,
    string? Notes);

/// <summary>
/// DTO representing a smart auto-reply rule.
/// </summary>
public sealed record AutoReplyRule(
    long Id,
    string Pattern,
    string ReplyTemplate,
    bool IsActive,
    string CreatedAt);

/// <summary>
/// DTO representing a unified smart search response for AI models.
/// </summary>
public sealed record SmartSearchResult(
    IReadOnlyList<SavedMessage> Messages,
    IReadOnlyList<ExtractedEntity> ExtractedEntities,
    IReadOnlyList<ExtractedReminder> Reminders,
    int TotalCount);

/// <summary>
/// High-performance repository for Zalo messages, entity extraction, CRM insights, and intelligent SQLite search.
/// </summary>
public sealed partial class MessageRepository(ZaloDatabase db)
{
    private readonly ZaloDatabase _db = db;

    [GeneratedRegex(@"(\+?84|0)[3|5|7|8|9][0-9]{8}")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"https?://[^\s]+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?i)\b(stk|tk|số tài khoản|ngân hàng|bank|vcb|mbbank|tpb|tcb|vpb)\b.*?\b\d{6,18}\b")]
    private static partial Regex BankCardRegex();

    [GeneratedRegex(@"(?i)\b(hẹn|họp|gặp|nhắc|lịch|lúc|sáng|chiều|tối|ngày)\b.*")]
    private static partial Regex ReminderRegex();

    [GeneratedRegex(@"(?i)\b(gấp|khẩn cấp|khiếu nại|lỗi|hỗ trợ ngay|sos)\b")]
    private static partial Regex UrgentRegex();

    public async Task SaveMessageAsync(ZaloMessageEvent msg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(msg);

        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO messages (
                msg_id, cli_msg_id, thread_id, thread_type, uid_from,
                display_name, content, msg_type, attachments_json, is_self, is_urgent, timestamp_ms, created_at
            ) VALUES (
                @msg_id, @cli_msg_id, @thread_id, @thread_type, @uid_from,
                @display_name, @content, @msg_type, @attachments_json, @is_self, @is_urgent, @timestamp_ms, @created_at
            ) ON CONFLICT(msg_id) DO UPDATE SET
                content = EXCLUDED.content,
                attachments_json = EXCLUDED.attachments_json;
            """;

        string contentText = msg.Content?.ToString() ?? string.Empty;
        bool isUrgent = UrgentRegex().IsMatch(contentText);

        string? attachmentsJson = msg.Attachments is { Count: > 0 }
            ? JsonSerializer.Serialize(msg.Attachments, ZaloMcpJsonContext.Default.Options)
            : null;

        long ts = long.TryParse(msg.TimestampMs, CultureInfo.InvariantCulture, out long parsedTs)
            ? parsedTs
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _ = cmd.Parameters.AddWithValue("@msg_id", msg.MsgId);
        _ = cmd.Parameters.AddWithValue("@cli_msg_id", msg.CliMsgId ?? string.Empty);
        _ = cmd.Parameters.AddWithValue("@thread_id", msg.ThreadId);
        _ = cmd.Parameters.AddWithValue("@thread_type", msg.ThreadType.ToString());
        _ = cmd.Parameters.AddWithValue("@uid_from", msg.UidFrom);
        _ = cmd.Parameters.AddWithValue("@display_name", msg.DisplayName ?? string.Empty);
        _ = cmd.Parameters.AddWithValue("@content", contentText);
        _ = cmd.Parameters.AddWithValue("@msg_type", msg.MsgType ?? "text");
        _ = cmd.Parameters.AddWithValue("@attachments_json", (object?)attachmentsJson ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("@is_self", msg.IsSelf ? 1 : 0);
        _ = cmd.Parameters.AddWithValue("@is_urgent", isUrgent ? 1 : 0);
        _ = cmd.Parameters.AddWithValue("@timestamp_ms", ts);
        _ = cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Update CRM Contact Insights
        if (!string.IsNullOrWhiteSpace(msg.UidFrom))
        {
            await this.UpdateContactInsightInternalAsync(conn, msg.UidFrom, msg.DisplayName, ct).ConfigureAwait(false);
        }

        // Perform smart entity & reminder extraction
        if (!string.IsNullOrWhiteSpace(contentText))
        {
            await this.ExtractAndSaveEntitiesInternalAsync(conn, msg.MsgId, msg.ThreadId, contentText, ct).ConfigureAwait(false);
        }
    }

    private async Task UpdateContactInsightInternalAsync(SqliteConnection conn, string userId, string? displayName, CancellationToken ct)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        cmd.CommandText = """
            INSERT INTO contact_insights (user_id, display_name, first_seen_at, last_active_at, total_messages)
            VALUES (@user_id, @display_name, @now, @now, 1)
            ON CONFLICT(user_id) DO UPDATE SET
                display_name = COALESCE(EXCLUDED.display_name, contact_insights.display_name),
                last_active_at = EXCLUDED.last_active_at,
                total_messages = contact_insights.total_messages + 1;
            """;
        _ = cmd.Parameters.AddWithValue("@user_id", userId);
        _ = cmd.Parameters.AddWithValue("@display_name", (object?)displayName ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("@now", now);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ExtractAndSaveEntitiesInternalAsync(SqliteConnection conn, string msgId, string threadId, string text, CancellationToken ct)
    {
        string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        // Extract Phone Numbers
        foreach (Match match in PhoneRegex().Matches(text))
        {
            await InsertEntityAsync(conn, msgId, threadId, "phone", match.Value, text, now, ct).ConfigureAwait(false);
        }

        // Extract URLs
        foreach (Match match in UrlRegex().Matches(text))
        {
            await InsertEntityAsync(conn, msgId, threadId, "url", match.Value, text, now, ct).ConfigureAwait(false);
        }

        // Extract Bank Accounts / STK
        foreach (Match match in BankCardRegex().Matches(text))
        {
            await InsertEntityAsync(conn, msgId, threadId, "bank_card", match.Value, text, now, ct).ConfigureAwait(false);
        }

        // Extract Reminders / Schedules
        Match reminderMatch = ReminderRegex().Match(text);
        if (reminderMatch.Success)
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO reminders (msg_id, thread_id, title, raw_text, created_at)
                VALUES (@msg_id, @thread_id, @title, @raw_text, @now);
                """;
            _ = cmd.Parameters.AddWithValue("@msg_id", msgId);
            _ = cmd.Parameters.AddWithValue("@thread_id", threadId);
            _ = cmd.Parameters.AddWithValue("@title", reminderMatch.Value);
            _ = cmd.Parameters.AddWithValue("@raw_text", text);
            _ = cmd.Parameters.AddWithValue("@now", now);
            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task InsertEntityAsync(SqliteConnection conn, string msgId, string threadId, string type, string value, string rawText, string now, CancellationToken ct)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO extracted_entities (msg_id, thread_id, entity_type, value, raw_text, created_at)
            VALUES (@msg_id, @thread_id, @type, @value, @raw_text, @now);
            """;
        _ = cmd.Parameters.AddWithValue("@msg_id", msgId);
        _ = cmd.Parameters.AddWithValue("@thread_id", threadId);
        _ = cmd.Parameters.AddWithValue("@type", type);
        _ = cmd.Parameters.AddWithValue("@value", value);
        _ = cmd.Parameters.AddWithValue("@raw_text", rawText);
        _ = cmd.Parameters.AddWithValue("@now", now);
        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExtractedReminder>> GetRemindersAsync(int limit = 50, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, msg_id, thread_id, title, event_time, raw_text, created_at FROM reminders ORDER BY id DESC LIMIT @limit;";
        _ = cmd.Parameters.AddWithValue("@limit", limit);

        List<ExtractedReminder> list = [];
        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            bool isNullTime = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false);
            bool isNullRaw = await reader.IsDBNullAsync(5, ct).ConfigureAwait(false);
            list.Add(new ExtractedReminder(
                Id: reader.GetInt64(0),
                MsgId: reader.GetString(1),
                ThreadId: reader.GetString(2),
                Title: reader.GetString(3),
                EventTime: isNullTime ? null : reader.GetString(4),
                RawText: isNullRaw ? null : reader.GetString(5),
                CreatedAt: reader.GetString(6)
            ));
        }

        return list;
    }

    public async Task<ContactInsight?> GetContactInsightAsync(string userId, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_id, display_name, first_seen_at, last_active_at, total_messages, notes FROM contact_insights WHERE user_id = @user_id;";
        _ = cmd.Parameters.AddWithValue("@user_id", userId);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            bool isNullName = await reader.IsDBNullAsync(1, ct).ConfigureAwait(false);
            bool isNullNotes = await reader.IsDBNullAsync(5, ct).ConfigureAwait(false);
            return new ContactInsight(
                UserId: reader.GetString(0),
                DisplayName: isNullName ? null : reader.GetString(1),
                FirstSeenAt: reader.GetString(2),
                LastActiveAt: reader.GetString(3),
                TotalMessages: reader.GetInt32(4),
                Notes: isNullNotes ? null : reader.GetString(5)
            );
        }

        return null;
    }

    public async Task<IReadOnlyList<SavedMessage>> GetUrgentMessagesAsync(int limit = 50, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT msg_id, cli_msg_id, thread_id, thread_type, uid_from,
                   display_name, content, msg_type, attachments_json, is_self, is_urgent, timestamp_ms, created_at
            FROM messages
            WHERE is_urgent = 1
            ORDER BY timestamp_ms DESC
            LIMIT @limit;
            """;
        _ = cmd.Parameters.AddWithValue("@limit", limit);

        List<SavedMessage> list = [];
        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            bool isNullAtt = await reader.IsDBNullAsync(8, ct).ConfigureAwait(false);
            list.Add(new SavedMessage(
                MsgId: reader.GetString(0),
                CliMsgId: reader.GetString(1),
                ThreadId: reader.GetString(2),
                ThreadType: reader.GetString(3),
                UidFrom: reader.GetString(4),
                DisplayName: reader.GetString(5),
                Content: reader.GetString(6),
                MsgType: reader.GetString(7),
                AttachmentsJson: isNullAtt ? null : reader.GetString(8),
                IsSelf: reader.GetInt32(9) == 1,
                IsUrgent: reader.GetInt32(10) == 1,
                TimestampMs: reader.GetInt64(11),
                CreatedAt: reader.GetString(12)
            ));
        }

        return list;
    }

    public async Task<IReadOnlyList<ExtractedEntity>> GetExtractedEntitiesAsync(string? entityType = null, int limit = 50, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();

        if (string.IsNullOrWhiteSpace(entityType))
        {
            cmd.CommandText = "SELECT id, msg_id, thread_id, entity_type, value, raw_text, created_at FROM extracted_entities ORDER BY id DESC LIMIT @limit;";
        }
        else
        {
            cmd.CommandText = "SELECT id, msg_id, thread_id, entity_type, value, raw_text, created_at FROM extracted_entities WHERE entity_type = @type ORDER BY id DESC LIMIT @limit;";
            _ = cmd.Parameters.AddWithValue("@type", entityType.Trim().ToLowerInvariant());
        }

        _ = cmd.Parameters.AddWithValue("@limit", limit);

        List<ExtractedEntity> list = [];
        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            bool isNullRaw = await reader.IsDBNullAsync(5, ct).ConfigureAwait(false);
            list.Add(new ExtractedEntity(
                Id: reader.GetInt64(0),
                MsgId: reader.GetString(1),
                ThreadId: reader.GetString(2),
                EntityType: reader.GetString(3),
                Value: reader.GetString(4),
                RawText: isNullRaw ? null : reader.GetString(5),
                CreatedAt: reader.GetString(6)
            ));
        }

        return list;
    }

    public async Task<SmartSearchResult> SmartSearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        IReadOnlyList<SavedMessage> messages = await this.SearchMessagesAsync(query, limit, ct).ConfigureAwait(false);
        IReadOnlyList<ExtractedEntity> entities = await this.GetExtractedEntitiesAsync(null, limit, ct).ConfigureAwait(false);
        IReadOnlyList<ExtractedReminder> reminders = await this.GetRemindersAsync(limit, ct).ConfigureAwait(false);

        List<ExtractedEntity> filteredEntities = [.. entities
            .Where(e => e.Value.Contains(query, StringComparison.OrdinalIgnoreCase) || (e.RawText != null && e.RawText.Contains(query, StringComparison.OrdinalIgnoreCase)))];

        List<ExtractedReminder> filteredReminders = [.. reminders
            .Where(r => r.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || (r.RawText != null && r.RawText.Contains(query, StringComparison.OrdinalIgnoreCase)))];

        return new SmartSearchResult(messages, filteredEntities, filteredReminders, messages.Count + filteredEntities.Count + filteredReminders.Count);
    }

    public async Task<IReadOnlyList<SavedMessage>> GetChatHistoryAsync(string threadId, int limit = 50, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT msg_id, cli_msg_id, thread_id, thread_type, uid_from,
                   display_name, content, msg_type, attachments_json, is_self, is_urgent, timestamp_ms, created_at
            FROM messages
            WHERE thread_id = @thread_id
            ORDER BY timestamp_ms DESC
            LIMIT @limit;
            """;

        _ = cmd.Parameters.AddWithValue("@thread_id", threadId);
        _ = cmd.Parameters.AddWithValue("@limit", limit);

        List<SavedMessage> list = [];
        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            bool isNullAtt = await reader.IsDBNullAsync(8, ct).ConfigureAwait(false);
            list.Add(new SavedMessage(
                MsgId: reader.GetString(0),
                CliMsgId: reader.GetString(1),
                ThreadId: reader.GetString(2),
                ThreadType: reader.GetString(3),
                UidFrom: reader.GetString(4),
                DisplayName: reader.GetString(5),
                Content: reader.GetString(6),
                MsgType: reader.GetString(7),
                AttachmentsJson: isNullAtt ? null : reader.GetString(8),
                IsSelf: reader.GetInt32(9) == 1,
                IsUrgent: reader.GetInt32(10) == 1,
                TimestampMs: reader.GetInt64(11),
                CreatedAt: reader.GetString(12)
            ));
        }

        list.Reverse();
        return list;
    }

    public async Task<IReadOnlyList<SavedMessage>> SearchMessagesAsync(string keyword, int limit = 50, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT m.msg_id, m.cli_msg_id, m.thread_id, m.thread_type, m.uid_from,
                   m.display_name, m.content, m.msg_type, m.attachments_json, m.is_self, m.is_urgent, m.timestamp_ms, m.created_at
            FROM messages m
            INNER JOIN messages_fts fts ON m.msg_id = fts.msg_id
            WHERE messages_fts MATCH @query
            ORDER BY m.timestamp_ms DESC
            LIMIT @limit;
            """;

        string ftsQuery = $"\"{keyword.Replace("\"", "", StringComparison.Ordinal)}\"*";
        _ = cmd.Parameters.AddWithValue("@query", ftsQuery);
        _ = cmd.Parameters.AddWithValue("@limit", limit);

        List<SavedMessage> list = [];
        try
        {
            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                bool isNullAtt = await reader.IsDBNullAsync(8, ct).ConfigureAwait(false);
                list.Add(new SavedMessage(
                    MsgId: reader.GetString(0),
                    CliMsgId: reader.GetString(1),
                    ThreadId: reader.GetString(2),
                    ThreadType: reader.GetString(3),
                    UidFrom: reader.GetString(4),
                    DisplayName: reader.GetString(5),
                    Content: reader.GetString(6),
                    MsgType: reader.GetString(7),
                    AttachmentsJson: isNullAtt ? null : reader.GetString(8),
                    IsSelf: reader.GetInt32(9) == 1,
                    IsUrgent: reader.GetInt32(10) == 1,
                    TimestampMs: reader.GetInt64(11),
                    CreatedAt: reader.GetString(12)
                ));
            }
        }
        catch (SqliteException)
        {
            cmd.CommandText = """
                SELECT msg_id, cli_msg_id, thread_id, thread_type, uid_from,
                       display_name, content, msg_type, attachments_json, is_self, is_urgent, timestamp_ms, created_at
                FROM messages
                WHERE content LIKE @likeQuery
                ORDER BY timestamp_ms DESC
                LIMIT @limit;
                """;
            cmd.Parameters.Clear();
            _ = cmd.Parameters.AddWithValue("@likeQuery", $"%{keyword}%");
            _ = cmd.Parameters.AddWithValue("@limit", limit);

            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                bool isNullAtt = await reader.IsDBNullAsync(8, ct).ConfigureAwait(false);
                list.Add(new SavedMessage(
                    MsgId: reader.GetString(0),
                    CliMsgId: reader.GetString(1),
                    ThreadId: reader.GetString(2),
                    ThreadType: reader.GetString(3),
                    UidFrom: reader.GetString(4),
                    DisplayName: reader.GetString(5),
                    Content: reader.GetString(6),
                    MsgType: reader.GetString(7),
                    AttachmentsJson: isNullAtt ? null : reader.GetString(8),
                    IsSelf: reader.GetInt32(9) == 1,
                    IsUrgent: reader.GetInt32(10) == 1,
                    TimestampMs: reader.GetInt64(11),
                    CreatedAt: reader.GetString(12)
                ));
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<SavedThread>> GetRecentThreadsAsync(int limit = 20, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT thread_id, thread_type, content, timestamp_ms, COUNT(*) over(PARTITION BY thread_id)
            FROM messages
            GROUP BY thread_id
            ORDER BY timestamp_ms DESC
            LIMIT @limit;
            """;

        _ = cmd.Parameters.AddWithValue("@limit", limit);

        List<SavedThread> list = [];
        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new SavedThread(
                ThreadId: reader.GetString(0),
                ThreadType: reader.GetString(1),
                LastMessageContent: reader.GetString(2),
                LastMessageTimestampMs: reader.GetInt64(3),
                MessageCount: reader.GetInt32(4)
            ));
        }

        return list;
    }

    public async Task SaveSessionMaterialAsync(string uid, ZaloSessionMaterial material, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO sessions (session_id, uid, material_json, is_active, created_at, updated_at)
            VALUES (@session_id, @uid, @material_json, 1, @now, @now)
            ON CONFLICT(session_id) DO UPDATE SET
                material_json = EXCLUDED.material_json,
                is_active = 1,
                updated_at = EXCLUDED.updated_at;
            """;

        string json = JsonSerializer.Serialize(material, ZaloMcpJsonContext.Default.ZaloSessionMaterial);
        string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        _ = cmd.Parameters.AddWithValue("@session_id", uid);
        _ = cmd.Parameters.AddWithValue("@uid", uid);
        _ = cmd.Parameters.AddWithValue("@material_json", json);
        _ = cmd.Parameters.AddWithValue("@now", now);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ZaloSessionMaterial?> GetActiveSessionMaterialAsync(CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT material_json FROM sessions
            WHERE is_active = 1
            ORDER BY updated_at DESC
            LIMIT 1;
            """;

        object? result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is string json && !string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize(json, ZaloMcpJsonContext.Default.ZaloSessionMaterial);
        }

        return null;
    }

    public async Task DeactivateSessionAsync(string? uid = null, CancellationToken ct = default)
    {
        using SqliteConnection conn = this._db.CreateConnection();
        using SqliteCommand cmd = conn.CreateCommand();

        if (string.IsNullOrWhiteSpace(uid))
        {
            cmd.CommandText = "UPDATE sessions SET is_active = 0;";
        }
        else
        {
            cmd.CommandText = "UPDATE sessions SET is_active = 0 WHERE uid = @uid OR session_id = @uid;";
            _ = cmd.Parameters.AddWithValue("@uid", uid);
        }

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
