// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// Web Server mode (<c>Zalo.Net.Mcp --web</c>) exposing OpenAPI JSON and REST Endpoints
/// for ChatGPT Web Custom GPT Actions and external HTTP webhooks.
/// </summary>
public static class WebCommand
{
    public static async Task RunAsync(string[] args)
    {
        int port = 5000;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int p))
            {
                port = p;
            }
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls($"http://localhost:{port}");

        _ = builder.Services.AddEndpointsApiExplorer();
        _ = builder.Services.AddSingleton<ZaloDatabase>();
        _ = builder.Services.AddSingleton<MessageRepository>();
        _ = builder.Services.AddSingleton<ZaloSessionManager>();

        WebApplication app = builder.Build();

        // Initialize SQLite Database and auto-load saved Zalo Session
        ZaloDatabase db = app.Services.GetRequiredService<ZaloDatabase>();
        db.Initialize();

        ZaloSessionManager sessionManager = app.Services.GetRequiredService<ZaloSessionManager>();
        await sessionManager.InitializeFromDatabaseAsync().ConfigureAwait(false);

        Console.WriteLine($"=== Zalo.Net.Mcp ChatGPT OpenAPI Action Web Server running at http://localhost:{port} ===");
        Console.WriteLine($"[OpenAPI Schema URL for ChatGPT]: http://localhost:{port}/openapi.json");

        // OpenAPI JSON Schema Endpoint for ChatGPT Custom GPT Actions
        _ = app.MapGet("/openapi.json", () => Results.Text(GetOpenApiJsonSchema(port), "application/json"));

        // REST Endpoints for ChatGPT Actions
        _ = app.MapGet("/api/zalo/login_qr", async (CancellationToken ct) =>
        {
            ZaloQrSession qrSession = await sessionManager.StartQrLoginAsync(ct).ConfigureAwait(false);
            return Results.Ok(qrSession);
        });

        _ = app.MapPost("/api/zalo/send_text", async (SendTextRequest req, CancellationToken ct) =>
        {
            sessionManager.EnsureAuthenticated();
            ZaloSession session = sessionManager.ActiveSession!;

            ZaloThreadType type = Enum.TryParse<ZaloThreadType>(req.ThreadType, ignoreCase: true, out ZaloThreadType parsedType)
                ? parsedType
                : ZaloThreadType.User;

            ZaloSendResult result = await ZaloWebClient.SendTextAsync(session, req.ThreadId, type, req.Text, ct).ConfigureAwait(false);

            ZaloMessageEvent outgoingMsg = new(
                MsgId: result.MsgId,
                CliMsgId: result.MsgId,
                MsgType: "text",
                UidFrom: session.Uid,
                IdTo: req.ThreadId,
                DisplayName: "Self",
                ThreadId: req.ThreadId,
                ThreadType: type,
                TimestampMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                Content: req.Text,
                Attachments: null,
                IsSelf: true
            );
            await sessionManager.Repository.SaveMessageAsync(outgoingMsg, ct).ConfigureAwait(false);

            return Results.Ok(new { status = "success", msg_id = result.MsgId, thread_id = req.ThreadId });
        });

        _ = app.MapGet("/api/zalo/search", async (string q, int limit = 50, CancellationToken ct = default) =>
        {
            SmartSearchResult results = await sessionManager.Repository.SmartSearchAsync(q, limit, ct).ConfigureAwait(false);
            return Results.Ok(results);
        });

        _ = app.MapGet("/api/zalo/reminders", async (int limit = 50, CancellationToken ct = default) =>
        {
            IReadOnlyList<ExtractedReminder> reminders = await sessionManager.Repository.GetRemindersAsync(limit, ct).ConfigureAwait(false);
            return Results.Ok(reminders);
        });

        _ = app.MapGet("/api/zalo/chat_summary", async (int limit = 20, CancellationToken ct = default) =>
        {
            IReadOnlyList<SavedThread> threads = await sessionManager.Repository.GetRecentThreadsAsync(limit, ct).ConfigureAwait(false);
            return Results.Ok(threads);
        });

        _ = app.MapGet("/api/zalo/analytics", async (int days = 30, CancellationToken ct = default) =>
        {
            ZaloAnalyticsResult analytics = await sessionManager.Repository.GetAnalyticsAsync(days, ct).ConfigureAwait(false);
            return Results.Ok(analytics);
        });

        await app.RunAsync().ConfigureAwait(false);
    }

    private record SendTextRequest(string ThreadId, string ThreadType, string Text);

    private static string GetOpenApiJsonSchema(int port) => $$"""
        {
          "openapi": "3.0.1",
          "info": {
            "title": "Zalo API for ChatGPT Custom GPT Actions",
            "description": "Zalo.Net Web API exposing Zalo messaging, search, and reminder capabilities to ChatGPT.",
            "version": "v1.0.0"
          },
          "servers": [
            {
              "url": "http://localhost:{{port}}"
            }
          ],
          "paths": {
            "/api/zalo/send_text": {
              "post": {
                "summary": "Send Zalo Message",
                "operationId": "sendZaloMessage",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "properties": {
                          "threadId": { "type": "string" },
                          "threadType": { "type": "string", "enum": ["User", "Group"] },
                          "text": { "type": "string" }
                        },
                        "required": ["threadId", "threadType", "text"]
                      }
                    }
                  }
                },
                "responses": {
                  "200": { "description": "Success" }
                }
              }
            },
            "/api/zalo/search": {
              "get": {
                "summary": "Search Zalo Messages & Entities",
                "operationId": "searchZalo",
                "parameters": [
                  { "name": "q", "in": "query", "required": true, "schema": { "type": "string" } }
                ],
                "responses": {
                  "200": { "description": "Success" }
                }
              }
            },
            "/api/zalo/reminders": {
              "get": {
                "summary": "Get Zalo Reminders & Schedules",
                "operationId": "getZaloReminders",
                "responses": {
                  "200": { "description": "Success" }
                }
              }
            }
          }
        }
        """;
}
