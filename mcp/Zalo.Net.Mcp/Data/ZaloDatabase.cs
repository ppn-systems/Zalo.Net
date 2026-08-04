// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.Data.Sqlite;

namespace Zalo.Net.Mcp.Data;

/// <summary>
/// Manages SQLite connection, schema creation, FTS5 full-text search indexing, and AI smart CRM memory tables for Zalo local persistence.
/// </summary>
public sealed class ZaloDatabase
{
    private readonly string _dbPath;
    private readonly ILogger<ZaloDatabase>? _logger;

    public ZaloDatabase(string? dbPath = null, ILogger<ZaloDatabase>? logger = null)
    {
        this._logger = logger;
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

    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = this._dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    public SqliteConnection CreateConnection()
    {
        SqliteConnection conn = new(this.ConnectionString);
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        _ = cmd.ExecuteNonQuery();
        return conn;
    }

    public void Initialize()
    {
        try
        {
            using SqliteConnection conn = this.CreateConnection();
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

                CREATE INDEX IF NOT EXISTS idx_messages_thread ON messages(thread_id, timestamp_ms DESC);
                CREATE INDEX IF NOT EXISTS idx_messages_urgent ON messages(is_urgent, timestamp_ms DESC);

                -- SQLite FTS5 Virtual Table for sub-millisecond full-text message search
                CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                    msg_id UNINDEXED,
                    thread_id UNINDEXED,
                    display_name,
                    content
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

            _ = cmd.ExecuteNonQuery();
            this._logger?.LogInformation("Zalo SQLite database, FTS5, Reminders, and CRM Insights schema initialized at {Path}", this._dbPath);
        }
        catch (Exception ex)
        {
            this._logger?.LogError(ex, "Failed to initialize Zalo SQLite database at {Path}", this._dbPath);
            throw;
        }
    }
}
