// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// CLI command to test live messaging via imported Zalo session.
/// </summary>
public static class TestSendMessageCommand
{
    public static async Task RunAsync(string target, string text)
    {
        Console.WriteLine("=== Zalo Live Messaging Test ===");

        ZaloDatabase db = new();
        db.Initialize();
        MessageRepository repo = new(db);
        using ZaloSessionManager sessionManager = new(repo);

        await sessionManager.InitializeFromDatabaseAsync().ConfigureAwait(false);

        if (sessionManager.ActiveSession == null)
        {
            Console.WriteLine("[ERROR] No active Zalo session found. Run --import-session or --login first.");
            return;
        }

        ZaloSession session = sessionManager.ActiveSession;
        Console.WriteLine($"[OK] Authenticated with Zalo Session UID: {session.Uid}");

        if (string.IsNullOrWhiteSpace(target))
        {
            Console.WriteLine("\nFetching friend list to find recipients...");
            try
            {
                IReadOnlyList<ZaloFriendInfo> friends = await ZaloWebClient.GetAllFriendsAsync(session, count: 10).ConfigureAwait(false);
                Console.WriteLine($"Found {friends.Count} friends in contacts:");
                foreach (ZaloFriendInfo f in friends)
                {
                    Console.WriteLine($" - {f.DisplayName} (UserId: {f.UserId}, Phone: {f.PhoneNumber})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] Could not fetch friends: {ex.Message}");
            }
            return;
        }

        Console.WriteLine($"Sending message '{text}' to target: {target}...");
        try
        {
            ZaloThreadType threadType = target.StartsWith("g_", StringComparison.OrdinalIgnoreCase)
                ? ZaloThreadType.Group
                : ZaloThreadType.User;

            ZaloSendResult result = await ZaloWebClient.SendTextAsync(session, target, threadType, text, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[SUCCESS] Message sent successfully! MsgId: {result.MsgId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to send message: {ex.Message}");
        }
    }
}
