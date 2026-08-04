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
            .WithTools<SmartTools>();

        using IHost app = builder.Build();

        // Initialize SQLite Database and auto-load saved Zalo Session
        ZaloDatabase db = app.Services.GetRequiredService<ZaloDatabase>();
        db.Initialize();

        ZaloSessionManager sessionManager = app.Services.GetRequiredService<ZaloSessionManager>();
        await sessionManager.InitializeFromDatabaseAsync().ConfigureAwait(false);

        await app.RunAsync().ConfigureAwait(false);
    }
}
