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
        Console.WriteLine("=== Zalo.Net.Bot Pure Native AOT & Automation Sample Bot ===");

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

        // 100% Pure Native AOT Lambda Handlers (Zero Reflection)
        ZaloBotEngine bot = ZaloBotBuilder.Create()
            .UseSession(session)
            .OnCommand("/ping", async ctx =>
            {
                Console.WriteLine($"[AOT BOT] Received /ping from {ctx.SenderUid}");
                _ = await ctx.ReplyQuoteAsync("🏓 Pong! Pure Native AOT Zalo Bot is active!").ConfigureAwait(false);
            })
            .OnKeyword(["stk", "chuyển khoản"], async ctx =>
            {
                Console.WriteLine($"[AOT BOT] Received payment keyword from {ctx.SenderUid}");
                await ctx.ReplyBankCardAsync(
                    binBank: "970458",
                    accountNumber: "1234567890",
                    accountName: "NGUYEN VAN A"
                ).ConfigureAwait(false);
            })
            .RegisterHandlers<MyZaloBotHandlers>()
            .Build();

        bot.OnError += (sender, ex) => Console.WriteLine($"[BOT ERROR]: {ex.Message}");

        Console.WriteLine("Zalo Bot registered with pure Native AOT Handlers (/ping, /help, [stk/chuyển khoản]).");
        await bot.StartAsync().ConfigureAwait(false);
    }
}

public sealed class MyZaloBotHandlers
{
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
}
