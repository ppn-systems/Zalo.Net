// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;
using Zalo.Net.Bot.Attributes;
using Zalo.Net.Bot.Builder;
using Zalo.Net.Bot.Context;
using Zalo.Net.Bot.Engine;
using Zalo.Net.Contracts;

namespace Zalo.Net.SampleBot;

public static class Program
{
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters")]
    public static async Task Main()
    {
        Console.WriteLine("=== Zalo.Net.Bot AI & Automation Sample Bot ===");

        // Note: Replace with active session material or import from Zalo.Net.Mcp SQLite database
        ZaloSessionMaterial material = new(
            CookiesJson: "[]",
            SecretKey: "sample-secret-key",
            Imei: "sample-imei",
            Uid: "1234567890",
            UserAgent: ZaloConstants.Protocol.DefaultUserAgent,
            Language: "vi"
        );
        ZaloSession session = new(
            Material: material,
            Uid: material.Uid,
            WsUrls: ["wss://chat-wpa.chat.zalo.me/ws"],
            ServiceMap: new Dictionary<string, string[]>(),
            PingIntervalMs: 30000,
            Proxy: null
        );

        ZaloBotEngine bot = ZaloBotBuilder.Create()
            .UseSession(session)
            .RegisterHandlers<MyZaloBotHandlers>()
            .Build();

        bot.OnError += (sender, ex) => Console.WriteLine($"[BOT ERROR]: {ex.Message}");

        Console.WriteLine("Zalo Bot registered with Handlers: /ping, /help, [chuyển khoản/stk]. Press Ctrl+C to exit.");
        await bot.StartAsync().ConfigureAwait(false);
    }
}

public sealed class MyZaloBotHandlers
{
    [ZaloCommand("/ping")]
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters")]
    public static async Task HandlePingAsync(ZaloBotContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        Console.WriteLine($"[BOT] Received /ping from {ctx.SenderUid}");
        _ = await ctx.ReplyQuoteAsync("🏓 Pong! Zalo.Net.Bot is active and running!").ConfigureAwait(false);
    }

    [ZaloCommand("/help")]
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters")]
    public static async Task HandleHelpAsync(ZaloBotContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string helpText = """
            🤖 **Zalo.Net.Bot Assistant Help**:
            - `/ping`: Test bot latency & connection status
            - `/help`: Display this help menu
            - Gửi 'stk' hoặc 'chuyển khoản': Nhận thông tin tài khoản ngân hàng
            """;
        _ = await ctx.ReplyTextAsync(helpText).ConfigureAwait(false);
    }

    [ZaloKeyword("stk", "chuyển khoản", "thanh toán")]
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters")]
    public static async Task HandlePaymentRequestAsync(ZaloBotContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        Console.WriteLine($"[BOT] Received payment keyword from {ctx.SenderUid}");
        await ctx.ReplyBankCardAsync(
            binBank: "970458", // TPBank
            accountNumber: "1234567890",
            accountName: "NGUYEN VAN A"
        ).ConfigureAwait(false);
    }
}
