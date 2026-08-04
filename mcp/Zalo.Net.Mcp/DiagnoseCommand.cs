// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// Core of <c>Zalo.Net.Mcp --diagnose</c> CLI command to inspect local database health,
/// session validity, and network connectivity.
/// </summary>
public static class DiagnoseCommand
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== Zalo.Net.Mcp System Diagnostic ===");
        Console.WriteLine($"[INFO] Operating System: {Environment.OSVersion}");
        Console.WriteLine($"[INFO] .NET Runtime: {Environment.Version}");

        // 1. Check SQLite Database
        Stopwatch sw = Stopwatch.StartNew();
        ZaloDatabase db = new();
        try
        {
            db.Initialize();
            MessageRepository repo = new(db);
            ZaloSessionMaterial? session = await repo.GetActiveSessionMaterialAsync().ConfigureAwait(false);
            sw.Stop();

            Console.WriteLine($"[OK] SQLite Database connection check passed in {sw.ElapsedMilliseconds}ms.");
            if (session != null)
            {
                Console.WriteLine($"[OK] Active Zalo Session found for UID: {session.Uid}");
            }
            else
            {
                Console.WriteLine("[INFO] No active Zalo session stored. Run zalo_login_qr to authenticate.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] SQLite Database check failed: {ex.Message}");
        }

        // 2. Check Client Configuration files
        Console.WriteLine("\n--- Checking AI Client MCP Config Status ---");
        string claudePath = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "..", "Library", "Application Support", "Claude", "claude_desktop_config.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");

        string chatGptPath = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "..", "Library", "Application Support", "ChatGPT", "mcp.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatGPT", "mcp.json");

        string antiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-ide", "mcp_config.json");

        Console.WriteLine($"ChatGPT Desktop Config Present: {File.Exists(chatGptPath)} ({chatGptPath})");
        Console.WriteLine($"Claude Desktop Config Present:  {File.Exists(claudePath)} ({claudePath})");
        Console.WriteLine($"Antigravity Config Present:     {File.Exists(antiPath)} ({antiPath})");

        Console.WriteLine("\n[DONE] Diagnostic check complete.");
    }
}
