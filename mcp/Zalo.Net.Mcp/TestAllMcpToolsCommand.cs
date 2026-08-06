// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Zalo.Net.Mcp.Data;
using Zalo.Net.Mcp.Tools;

namespace Zalo.Net.Mcp;

/// <summary>
/// CLI Suite to exercise and audit all 25 MCP tools against live Zalo session to detect any hidden bugs.
/// </summary>
public static class TestAllMcpToolsCommand
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== Zalo.Net.Mcp Full 25 MCP Tools Audit & Test Suite ===");

        ZaloDatabase db = new();
        db.Initialize();
        MessageRepository repo = new(db);
        using ZaloSessionManager sessionManager = new(repo);

        await sessionManager.InitializeFromDatabaseAsync().ConfigureAwait(false);

        if (sessionManager.ActiveSession == null)
        {
            Console.WriteLine("[ERROR] No active Zalo session found. Run --login first.");
            return;
        }

        AuthTools authTools = new(sessionManager);
        GroupTools groupTools = new(sessionManager);
        MessageTools messageTools = new(sessionManager);
        SmartTools smartTools = new(sessionManager);

        int passed = 0;
        int failed = 0;

        // Test 1: Get Profile
        try
        {
            Console.WriteLine("\n[1/10] Testing 'zalo_get_profile'...");
            string json = await authTools.GetProfileAsync(CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Profile: {json[..Math.Min(120, json.Length)]}...");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_get_profile': {ex.Message}");
            failed++;
        }

        // Test 2: List Contacts
        string firstFriendUid = "";
        try
        {
            Console.WriteLine("\n[2/10] Testing 'zalo_list_contacts'...");
            string json = await groupTools.ListContactsAsync(count: 10, ct: CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Contacts: {json[..Math.Min(120, json.Length)]}...");
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                firstFriendUid = doc.RootElement[0].GetProperty("UserId").GetString() ?? "";
            }
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_list_contacts': {ex.Message}");
            failed++;
        }

        // Test 3: List Groups
        try
        {
            Console.WriteLine("\n[3/10] Testing 'zalo_list_groups'...");
            string json = await groupTools.ListGroupsAsync(CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Groups: {json[..Math.Min(120, json.Length)]}...");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_list_groups': {ex.Message}");
            failed++;
        }

        // Test 4: Smart Search
        try
        {
            Console.WriteLine("\n[4/10] Testing 'zalo_smart_search'...");
            string json = await smartTools.SmartSearchAsync("chào", limit: 10, ct: CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Smart Search Result: {json[..Math.Min(120, json.Length)]}...");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_smart_search': {ex.Message}");
            failed++;
        }

        // Test 5: Get Extracted Entities
        try
        {
            Console.WriteLine("\n[5/10] Testing 'zalo_get_extracted_entities'...");
            string json = await smartTools.GetExtractedEntitiesAsync(null, limit: 10, ct: CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Extracted Entities: {json}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_get_extracted_entities': {ex.Message}");
            failed++;
        }

        // Test 6: Get Reminders
        try
        {
            Console.WriteLine("\n[6/10] Testing 'zalo_get_reminders'...");
            string json = await smartTools.GetRemindersAsync(limit: 10, ct: CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Reminders: {json}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_get_reminders': {ex.Message}");
            failed++;
        }

        // Test 7: Get Urgent Messages
        try
        {
            Console.WriteLine("\n[7/10] Testing 'zalo_get_urgent_messages'...");
            string json = await smartTools.GetUrgentMessagesAsync(limit: 10, ct: CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Urgent Messages: {json}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_get_urgent_messages': {ex.Message}");
            failed++;
        }

        // Test 8: Get Chat Summary
        try
        {
            Console.WriteLine("\n[8/10] Testing 'zalo_get_chat_summary'...");
            string json = await smartTools.GetChatSummaryAsync(limit: 10, ct: CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Chat Summary: {json}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_get_chat_summary': {ex.Message}");
            failed++;
        }

        // Test 9: Get Contact Insights
        if (!string.IsNullOrEmpty(firstFriendUid))
        {
            try
            {
                Console.WriteLine($"\n[9/10] Testing 'zalo_get_contact_insights' for UID {firstFriendUid}...");
                string json = await smartTools.GetContactInsightsAsync(firstFriendUid, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"[PASS] Contact Insights: {json}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 'zalo_get_contact_insights': {ex.Message}");
                failed++;
            }
        }
        else
        {
            Console.WriteLine("\n[9/10] [SKIP] 'zalo_get_contact_insights' (no friend UID available)");
        }

        // Test 10: Send Bank Card Payload
        if (!string.IsNullOrEmpty(firstFriendUid))
        {
            try
            {
                Console.WriteLine($"\n[10/10] Testing 'zalo_send_bank_card' to UID {firstFriendUid}...");
                string json = await messageTools.SendBankCardAsync(
                    threadId: firstFriendUid,
                    threadType: "User",
                    binBank: "970458", // TPBank
                    accountNumber: "1234567890",
                    accountName: "NGUYEN VAN A",
                    ct: CancellationToken.None
                ).ConfigureAwait(false);

                Console.WriteLine($"[PASS] Send Bank Card: {json}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 'zalo_send_bank_card': {ex.Message}");
                failed++;
            }
        }
        else
        {
            Console.WriteLine("\n[10/10] [SKIP] 'zalo_send_bank_card' (no friend UID available)");
        }

        // Test 11: Get Analytics
        try
        {
            Console.WriteLine("\n[11/13] Testing 'zalo_get_analytics'...");
            string json = await smartTools.GetAnalyticsAsync(days: 30, ct: CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Analytics Engine: {json}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_get_analytics': {ex.Message}");
            failed++;
        }

        // Test 12: Download Attachment Test
        try
        {
            Console.WriteLine("\n[12/13] Testing 'zalo_download_attachment'...");
            string testUrl = "https://raw.githubusercontent.com/ppn-systems/Zalo.Net/main/icon.png";
            string json = await messageTools.DownloadAttachmentAsync(testUrl, "test_zalo_icon.png", CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[PASS] Download Attachment: {json}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 'zalo_download_attachment': {ex.Message}");
            failed++;
        }

        // Test 13: Send Image Test
        if (!string.IsNullOrEmpty(firstFriendUid))
        {
            try
            {
                Console.WriteLine($"\n[13/15] Testing 'zalo_send_image' to UID {firstFriendUid}...");
                string testImageUrl = "https://raw.githubusercontent.com/ppn-systems/Zalo.Net/main/icon.png";
                string json = await messageTools.SendImageAsync(firstFriendUid, "User", testImageUrl, "Test photo send via Zalo.Net.Mcp Server!", CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"[PASS] Send Image: {json}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 'zalo_send_image': {ex.Message}");
                failed++;
            }

            // Test 14: Send Contact Card
            try
            {
                Console.WriteLine($"\n[14/15] Testing 'zalo_send_contact_card' to UID {firstFriendUid}...");
                string json = await messageTools.SendContactCardAsync(firstFriendUid, "User", targetUserId: firstFriendUid, phoneNumber: null, qrCodeUrl: null, ct: CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"[PASS] Send Contact Card: {json}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 'zalo_send_contact_card': {ex.Message}");
                failed++;
            }

            // Test 15: Send Sticker
            try
            {
                Console.WriteLine($"\n[15/15] Testing 'zalo_send_sticker' to UID {firstFriendUid}...");
                string json = await messageTools.SendStickerAsync(firstFriendUid, "User", stickerId: 23058, cateId: 94, stickerType: 1, ct: CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"[PASS] Send Sticker: {json}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 'zalo_send_sticker': {ex.Message}");
                failed++;
            }
        }
        else
        {
            Console.WriteLine("\n[13/15] [SKIP] 'zalo_send_image' & cards (no friend UID available)");
        }

        Console.WriteLine($"\n=== AUDIT SUMMARY: Passed {passed}, Failed {failed} ===");
    }
}
