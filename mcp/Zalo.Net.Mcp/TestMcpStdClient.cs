// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Zalo.Net.Mcp;

/// <summary>
/// Lightweight MCP JSON-RPC 2.0 Client Tester communicating with Zalo.Net.Mcp over Stdio.
/// </summary>
public static class TestMcpStdClient
{
    public static async Task RunAsync(string exePath)
    {
        Console.WriteLine("=== Testing Zalo.Net.Mcp Server over Stdio JSON-RPC 2.0 Protocol ===");

        ProcessStartInfo psi = new()
        {
            FileName = exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : exePath,
            Arguments = exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? $"\"{exePath}\"" : "",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = psi };
        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine($"[SERVER STDERR] {e.Data}");
            }
        };

        _ = process.Start();
        process.BeginErrorReadLine();

        StreamWriter writer = process.StandardInput;
        StreamReader reader = process.StandardOutput;

        // 1. Initialize
        Console.WriteLine("\n--> Sending 'initialize' JSON-RPC Request...");
        JsonObject initReq = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "AntigravityMCPTestClient",
                    ["version"] = "1.0.0"
                }
            }
        };

        await writer.WriteLineAsync(initReq.ToJsonString()).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        string? initRespStr = await reader.ReadLineAsync().ConfigureAwait(false);
        Console.WriteLine($"<-- Received Initialize Response:\n{initRespStr}");

        // 2. Initialized notification
        JsonObject initNotif = new()
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized"
        };
        await writer.WriteLineAsync(initNotif.ToJsonString()).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        // 3. Tools List
        Console.WriteLine("\n--> Sending 'tools/list' JSON-RPC Request...");
        JsonObject listReq = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["method"] = "tools/list"
        };

        await writer.WriteLineAsync(listReq.ToJsonString()).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        string? listRespStr = await reader.ReadLineAsync().ConfigureAwait(false);
        Console.WriteLine($"<-- Received Tools List Response:\n{listRespStr}");

        // 4. Call Tool 'zalo_login_qr'
        Console.WriteLine("\n--> Calling MCP Tool 'zalo_login_qr'...");
        JsonObject callReq = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 3,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "zalo_login_qr",
                ["arguments"] = new JsonObject()
            }
        };

        await writer.WriteLineAsync(callReq.ToJsonString()).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        string? callRespStr = await reader.ReadLineAsync().ConfigureAwait(false);
        Console.WriteLine($"<-- Received Tool Call 'zalo_login_qr' Response:\n{callRespStr}");

        process.Kill();
    }
}
