// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Zalo.Net.Mcp.Data;
using Zalo.Net.Mcp.Tools;

namespace Zalo.Net.Mcp;

internal class Program
{
    private static async Task Main(string[] args)
    {
        string currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "Zalo.Net.Mcp.dll");

        if (args.Contains("--setup"))
        {
            SetupCommand.Run(currentExe);
            return;
        }

        if (args.Contains("--login"))
        {
            await LoginCommand.RunAsync().ConfigureAwait(false);
            return;
        }

        if (args.Contains("--diagnose"))
        {
            await DiagnoseCommand.RunAsync().ConfigureAwait(false);
            return;
        }

        if (args.Contains("--import-session"))
        {
            string sessionPath = args.Length > 1 ? args[^1] : "session.json";
            await ImportSessionCommand.RunAsync(sessionPath).ConfigureAwait(false);
            return;
        }

        if (args.Contains("--send-test"))
        {
            string target = args.Length > 2 ? args[1] : "";
            string text = args.Length > 2 ? args[2] : "Xin chào từ Zalo.Net.Mcp Server!";
            await TestSendMessageCommand.RunAsync(target, text).ConfigureAwait(false);
            return;
        }

        if (args.Contains("--test-all-tools"))
        {
            await TestAllMcpToolsCommand.RunAsync().ConfigureAwait(false);
            return;
        }

        if (args.Contains("--test-mcp"))
        {
            string targetExe = Path.Combine(AppContext.BaseDirectory, "Zalo.Net.Mcp.dll");
            await TestMcpStdClient.RunAsync(targetExe).ConfigureAwait(false);
            return;
        }

        if (args.Contains("--web") || args.Contains("--http"))
        {
            await WebCommand.RunAsync(args).ConfigureAwait(false);
            return;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Standard stdout is reserved for MCP JSON-RPC framing — all logs must strictly go to stderr.
        LogLevel logLevel = Enum.TryParse(Environment.GetEnvironmentVariable("ZALO_MCP_LOG_LEVEL"), ignoreCase: true, out LogLevel parsed)
            ? parsed
            : LogLevel.Warning;

        _ = builder.Logging.ClearProviders();
        _ = builder.Logging.SetMinimumLevel(logLevel);
        _ = builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        // Database & Repositories
        _ = builder.Services.AddSingleton<ZaloDatabase>();
        _ = builder.Services.AddSingleton<MessageRepository>();
        _ = builder.Services.AddSingleton<ZaloSessionManager>();

        _ = builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                {
                    Name = "Zalo.Net.Mcp",
                    Version = "1.0.0"
                };

                // Add call tool logging filter
                options.Filters.Request.CallToolFilters.Add(next => async (context, ct) =>
                {
                    ILogger logger = context.Services!.GetRequiredService<ILoggerFactory>().CreateLogger("Zalo.Net.Mcp.Tool");
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("MCP Tool Invoked: {ToolName} (Args: {ArgCount})", context.Params?.Name, context.Params?.Arguments?.Count ?? 0);
                    }
                    return await next(context, ct).ConfigureAwait(false);
                });
            })
            .WithResources<Resources>()
            .WithPrompts<Prompts>()
            .WithTools<AuthTools>()
            .WithTools<MessageTools>()
            .WithTools<GroupTools>()
            .WithTools<SmartTools>()
            // The stdio transport is what wires the server to standard input/output. Without it the
            // host starts with no transport attached, so an `initialize` request sent over stdio is
            // never read and the client hangs until its own timeout (no stdout, no stderr).
            .WithStdioServerTransport();

        using IHost app = builder.Build();

        // Initialize SQLite Database (local, milliseconds) before the transport starts.
        ZaloDatabase db = app.Services.GetRequiredService<ZaloDatabase>();
        db.Initialize();

        ZaloSessionManager sessionManager = app.Services.GetRequiredService<ZaloSessionManager>();

        // Restoring the saved Zalo session talks to Zalo over the network and starts the WebSocket
        // listener. It runs in the background on purpose: the MCP handshake must not wait for it.
        // Clients sent `initialize` while the login was still in flight, got no answer within their
        // own timeout, and reported the server as broken even though it was merely still connecting.
        sessionManager.StartBootstrapInBackground();

        await app.RunAsync().ConfigureAwait(false);
    }
}
