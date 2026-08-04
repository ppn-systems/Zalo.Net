// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zalo.Net.Mcp;

/// <summary>
/// Handles <c>Zalo.Net.Mcp --setup</c> command to automatically configure Zalo MCP server across
/// ChatGPT Desktop, Claude Desktop, Antigravity IDE, and Cursor/VS Code.
/// </summary>
public static class SetupCommand
{
    public static string AppDataDir => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "..", "Library", "Application Support", "Zalo.Net.Mcp")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zalo.Net.Mcp");

    private static string ClaudeConfigPath => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "..", "Library", "Application Support", "Claude", "claude_desktop_config.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");

    private static string ChatGPTConfigPath => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "..", "Library", "Application Support", "ChatGPT", "mcp.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatGPT", "mcp.json");

    private static string AntigravityConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-ide", "mcp_config.json");

    private static string CursorConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "mcp_config.json");

    private sealed record ClientTarget(string Name, string ConfigPath);

    public static void Run(string exePath)
    {
        ArgumentNullException.ThrowIfNull(exePath);

        Console.WriteLine("=== Zalo.Net.Mcp Auto-Setup ===");

        List<ClientTarget> targets = [
            new ClientTarget("ChatGPT Desktop", ChatGPTConfigPath),
            new ClientTarget("Claude Desktop", ClaudeConfigPath),
            new ClientTarget("Antigravity IDE", AntigravityConfigPath),
            new ClientTarget("Cursor / VS Code", CursorConfigPath)
        ];

        foreach (ClientTarget target in targets)
        {
            MergeClientConfig(target, exePath);
        }

        Console.WriteLine("\n[SUCCESS] Config setup finished!");
        Console.WriteLine("Restart ChatGPT Desktop / Claude Desktop / Antigravity / Cursor to apply the new Zalo MCP server.");
        Console.WriteLine("Khởi động lại ChatGPT Desktop / Claude Desktop / Antigravity để nhận server.");
    }

    private static void MergeClientConfig(ClientTarget target, string exePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(target.ConfigPath);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            if (!Directory.Exists(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }

            JsonObject rootObj = [];
            if (File.Exists(target.ConfigPath))
            {
                try
                {
                    string jsonText = File.ReadAllText(target.ConfigPath);
                    if (JsonNode.Parse(jsonText) is JsonObject parsed)
                    {
                        rootObj = parsed;
                    }
                }
                catch
                {
                    // Invalid JSON, re-initialize
                }
            }

            if (rootObj["mcpServers"] is not JsonObject mcpServers)
            {
                mcpServers = [];
                rootObj["mcpServers"] = mcpServers;
            }

            JsonObject zaloEntry = new()
            {
                ["command"] = exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : exePath
            };

            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                zaloEntry["args"] = new JsonArray { JsonValue.Create(exePath) };
            }
            else
            {
                zaloEntry["args"] = new JsonArray();
            }

            mcpServers["zalo"] = zaloEntry;

            JsonSerializerOptions options = new() { WriteIndented = true };
            File.WriteAllText(target.ConfigPath, rootObj.ToJsonString(options));

            Console.WriteLine($"[OK] Updated config for {target.Name} at: {target.ConfigPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Could not update config for {target.Name}: {ex.Message}");
        }
    }
}
