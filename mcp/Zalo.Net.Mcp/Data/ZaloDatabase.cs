// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Zalo.Net.Mcp.Data;

/// <summary>
/// Manages SQLite connection, schema creation, FTS5 full-text search indexing, and AI smart CRM memory tables for Zalo local persistence.
/// </summary>
public sealed class ZaloDatabase
{
    private readonly string _dbPath;

    public ZaloDatabase(string? dbPath = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            string appData = OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "..", "Library", "Application Support", "Zalo.Net.Mcp")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zalo.Net.Mcp");

            _ = Directory.CreateDirectory(appData);
            this._dbPath = Path.Combine(appData, "zalo_data.db");
        }
        else
        {
            this._dbPath = dbPath;
            string? dir = Path.GetDirectoryName(this._dbPath);
            if (!string.IsNullOrEmpty(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }
        }
    }

    /// <summary>Gets the filesystem path to the SQLite database file.</summary>
    public string DbPath => this._dbPath;

    /// <summary>Gets the default application data directory for Zalo.Net.Mcp.</summary>
    public static string GetDefaultAppDataDir() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "..", "Library", "Application Support", "Zalo.Net.Mcp")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zalo.Net.Mcp");

    /// <summary>Gets the isolated database file path for a specific account UID.</summary>
    public static string GetAccountDbPath(string accountUid, string? baseDir = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        if (accountUid.Contains("..", StringComparison.Ordinal) ||
            accountUid.Contains('/', StringComparison.Ordinal) ||
            accountUid.Contains('\\', StringComparison.Ordinal) ||
            Path.GetInvalidFileNameChars().Any(accountUid.Contains))
        {
            throw new ArgumentException($"Invalid account UID '{accountUid}': contains illegal path characters.", nameof(accountUid));
        }

        string root = baseDir ?? GetDefaultAppDataDir();
        return Path.Combine(root, "accounts", accountUid, "zalo_data.db");
    }

    /// <summary>Creates or gets an isolated <see cref="ZaloDatabase"/> instance for a specific account UID.</summary>
    public static ZaloDatabase ForAccount(string accountUid, string? baseDir = null)
    {
        string path = GetAccountDbPath(accountUid, baseDir);
        return new ZaloDatabase(path);
    }

    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = this._dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Default
    }.ToString();

    public SqliteConnection CreateConnection()
    {
        SqliteConnection conn = new(this.ConnectionString);
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();

        // High-scale PRAGMAs for concurrent operation across 1,000+ isolated databases:
        // - foreign_keys=ON: relational integrity
        // - busy_timeout=5000: graceful wait on transient locks
        // - cache_size=-512: 512 KiB page cache limit per account DB (saves ~1.5 GB memory at 1,000 accounts)
        // - wal_autocheckpoint=250: checkpoints WAL after 250 pages (~1MB) instead of default 1000 (~4MB), saving ~3GB disk at 1000 DBs
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA cache_size=-512; PRAGMA temp_store=MEMORY; PRAGMA wal_autocheckpoint=250;";
        _ = cmd.ExecuteNonQuery();
        return conn;
    }

    public void Initialize()
    {
        using SqliteConnection conn = this.CreateConnection();

        using (SqliteCommand pragmaCmd = conn.CreateCommand())
        {
            // WAL mode allows non-blocking reads while writing.
            // synchronous=NORMAL is crash-safe with WAL and eliminates ~90% of redundant fsync disk operations.
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            _ = pragmaCmd.ExecuteScalar();
        }

        using SqliteCommand cmd = conn.CreateCommand();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                user_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                avatar_url TEXT,
                phone_number TEXT,
                is_friend INTEGER DEFAULT 0,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS groups (
                group_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                avatar_url TEXT,
                member_count INTEGER DEFAULT 0,
                owner_id TEXT,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS messages (
                msg_id TEXT PRIMARY KEY,
                cli_msg_id TEXT,
                thread_id TEXT NOT NULL,
                thread_type TEXT NOT NULL,
                uid_from TEXT NOT NULL,
                display_name TEXT,
                content TEXT,
                msg_type TEXT,
                attachments_json TEXT,
                is_self INTEGER DEFAULT 0,
                is_urgent INTEGER DEFAULT 0,
                timestamp_ms INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            """;
        _ = cmd.ExecuteNonQuery();

        // Migration: Add is_urgent column if missing in legacy messages table
        try
        {
            using SqliteCommand alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE messages ADD COLUMN is_urgent INTEGER DEFAULT 0;";
            _ = alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists
        }

        using SqliteCommand indexCmd = conn.CreateCommand();
        indexCmd.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_messages_thread ON messages(thread_id, timestamp_ms DESC);
            CREATE INDEX IF NOT EXISTS idx_messages_urgent ON messages(is_urgent, timestamp_ms DESC);

            -- SQLite FTS5 Virtual Table for sub-millisecond full-text message search.
            -- remove_diacritics 2 folds the accents of the Latin/Vietnamese alphabet, so a search
            -- typed without accents ("tai lieu", "hoc phi", "gap") still matches the accented text
            -- that people actually write ("Tài liệu", "học phí", "gấp").
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                msg_id UNINDEXED,
                thread_id UNINDEXED,
                display_name,
                content,
                tokenize = "unicode61 remove_diacritics 2"
            );

            -- Triggers to sync FTS5 table automatically on INSERT/UPDATE
            CREATE TRIGGER IF NOT EXISTS trg_messages_ai AFTER INSERT ON messages BEGIN
                INSERT INTO messages_fts(msg_id, thread_id, display_name, content)
                VALUES (new.msg_id, new.thread_id, new.display_name, new.content);
            END;

            -- Extracted Entities Table (Bank Accounts, Phone Numbers, URLs)
            CREATE TABLE IF NOT EXISTS extracted_entities (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                msg_id TEXT NOT NULL,
                thread_id TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                value TEXT NOT NULL,
                raw_text TEXT,
                created_at TEXT NOT NULL,
                FOREIGN KEY(msg_id) REFERENCES messages(msg_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_entities_type ON extracted_entities(entity_type, created_at DESC);

            -- Reminders & Event Detection Table
            CREATE TABLE IF NOT EXISTS reminders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                msg_id TEXT NOT NULL,
                thread_id TEXT NOT NULL,
                title TEXT NOT NULL,
                event_time TEXT,
                raw_text TEXT,
                created_at TEXT NOT NULL,
                FOREIGN KEY(msg_id) REFERENCES messages(msg_id) ON DELETE CASCADE
            );

            -- CRM Contact Interaction Insights
            CREATE TABLE IF NOT EXISTS contact_insights (
                user_id TEXT PRIMARY KEY,
                display_name TEXT,
                first_seen_at TEXT NOT NULL,
                last_active_at TEXT NOT NULL,
                total_messages INTEGER DEFAULT 1,
                notes TEXT
            );

            -- Smart Auto-Reply Rules Table
            CREATE TABLE IF NOT EXISTS auto_reply_rules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                pattern TEXT NOT NULL,
                reply_template TEXT NOT NULL,
                is_active INTEGER DEFAULT 1,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT PRIMARY KEY,
                uid TEXT NOT NULL,
                material_json TEXT NOT NULL,
                is_active INTEGER DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        _ = indexCmd.ExecuteNonQuery();

        this.MigrateDerivedRowDuplicates(conn);

        using SqliteCommand derivedIndexCmd = conn.CreateCommand();
        derivedIndexCmd.CommandText = """
            -- A message owns at most one reminder and at most one row per (type, value) entity.
            -- These unique indexes are what ON CONFLICT(msg_id) / ON CONFLICT(msg_id, entity_type, value)
            -- in MessageRepository target, and they also index the child rows by parent message so
            -- "which entities came from this message" is a lookup instead of a full table scan.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_reminders_msg ON reminders(msg_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_entities_msg_value ON extracted_entities(msg_id, entity_type, value);

            -- Keep the FTS index in step with edits and deletions (the INSERT trigger alone leaves
            -- stale text behind when a replayed message updates its content).
            CREATE TRIGGER IF NOT EXISTS trg_messages_au AFTER UPDATE OF content ON messages BEGIN
                DELETE FROM messages_fts WHERE msg_id = old.msg_id;
                INSERT INTO messages_fts(msg_id, thread_id, display_name, content)
                VALUES (new.msg_id, new.thread_id, new.display_name, new.content);
            END;

            CREATE TRIGGER IF NOT EXISTS trg_messages_ad AFTER DELETE ON messages BEGIN
                DELETE FROM messages_fts WHERE msg_id = old.msg_id;
            END;
            """;

        try
        {
            _ = derivedIndexCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Legacy rows that still collide (e.g. a message whose reminder appears twice) must not
            // keep the server from starting: the insert paths de-duplicate on their own.
        }

        this.MigrateFullTextSearchTokenizer(conn);
    }

    /// <summary>
    /// Rebuilds the full-text index with the diacritic-insensitive tokenizer declared in
    /// <see cref="Initialize"/>. Databases created by earlier builds carry an FTS table whose
    /// tokenizer only matches the exact accented spelling, so searching "tai lieu" for "Tài liệu"
    /// returned nothing. FTS5 stores the tokenizer inside the virtual table definition and offers no
    /// ALTER, so the index has to be dropped and rebuilt from the <c>messages</c> table.
    /// <para>
    /// The guard reads the stored definition instead of <c>PRAGMA user_version</c>: this migration is
    /// a no-op for every database whose index is already correct, and it cannot swallow (or be
    /// swallowed by) another migration that also uses the version counter.
    /// </para>
    /// </summary>
    private void MigrateFullTextSearchTokenizer(SqliteConnection conn)
    {
        using (SqliteCommand definitionCmd = conn.CreateCommand())
        {
            definitionCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'messages_fts';";
            if (definitionCmd.ExecuteScalar() is string definition
                && definition.Contains("remove_diacritics", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using SqliteTransaction tx = conn.BeginTransaction();

        using (SqliteCommand rebuildCmd = conn.CreateCommand())
        {
            rebuildCmd.Transaction = tx;
            rebuildCmd.CommandText = """
                DROP TABLE IF EXISTS messages_fts;

                CREATE VIRTUAL TABLE messages_fts USING fts5(
                    msg_id UNINDEXED,
                    thread_id UNINDEXED,
                    display_name,
                    content,
                    tokenize = "unicode61 remove_diacritics 2"
                );

                INSERT INTO messages_fts(msg_id, thread_id, display_name, content)
                SELECT msg_id, thread_id, display_name, content FROM messages;
                """;
            _ = rebuildCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>
    /// One-off repair for databases written by earlier builds: the WebSocket history sync replays
    /// messages that are already stored, and the derived rows (reminders, entities) were re-inserted
    /// on every replay, so a single message could accumulate hundreds of duplicate rows (and the CRM
    /// counters counted each replay as a new message). Collapsing those duplicates shrinks the file
    /// and the read queries, and is what makes the UNIQUE indexes above possible.
    /// </summary>
    private void MigrateDerivedRowDuplicates(SqliteConnection conn)
    {
        using SqliteCommand versionCmd = conn.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        long version = Convert.ToInt64(versionCmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        if (version >= 1)
        {
            return;
        }

        using SqliteCommand repairCmd = conn.CreateCommand();
        repairCmd.CommandText = """
            DELETE FROM reminders
            WHERE id NOT IN (SELECT MIN(id) FROM reminders GROUP BY msg_id);

            DELETE FROM extracted_entities
            WHERE id NOT IN (SELECT MIN(id) FROM extracted_entities GROUP BY msg_id, entity_type, value);

            -- The CRM counter also grew by one per replay. Only lower counters that provably
            -- over-count the messages still stored for that contact; never raise one.
            UPDATE contact_insights
            SET total_messages = (
                SELECT COUNT(*) FROM messages m WHERE m.uid_from = contact_insights.user_id
            )
            WHERE total_messages > (
                SELECT COUNT(*) FROM messages m WHERE m.uid_from = contact_insights.user_id
            );

            -- Backfill the full-text index for messages that were stored before it existed.
            INSERT INTO messages_fts(msg_id, thread_id, display_name, content)
            SELECT m.msg_id, m.thread_id, m.display_name, m.content
            FROM messages m
            WHERE NOT EXISTS (SELECT 1 FROM messages_fts f WHERE f.msg_id = m.msg_id);

            PRAGMA user_version = 1;
            """;

        _ = repairCmd.ExecuteNonQuery();
    }
}
