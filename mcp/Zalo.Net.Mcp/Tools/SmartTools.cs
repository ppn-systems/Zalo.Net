// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Smart AI Search, Entity Extraction, Reminders, Urgent Messages, and CRM Insights.
/// Fully isolated per account database.
/// </summary>
[McpServerToolType]
public sealed class SmartTools(ZaloSessionManager sessionManager)
{
    private readonly ZaloSessionManager _sessionManager = sessionManager;

    private MessageRepository ResolveRepository(string? accountUid)
    {
        if (!string.IsNullOrWhiteSpace(accountUid))
        {
            this._sessionManager.EnsureAuthenticated(accountUid);
            return this._sessionManager.GetAccount(accountUid)!.Repository;
        }

        return this._sessionManager.Repository;
    }

    [McpServerTool(Name = "zalo_smart_search")]
    [Description("Smart unified search across messages, extracted entities (bank accounts, phones, links), and reminders.")]
    public async Task<string> SmartSearchAsync(
        [Description("Keyword or search query text")] string query,
        [Description("Maximum number of results to return (default 50)")] int limit = 50,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        SmartSearchResult result = await repo.SmartSearchAsync(query, limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, ZaloMcpJsonContext.Default.SmartSearchResult);
    }

    [McpServerTool(Name = "zalo_get_extracted_entities")]
    [Description("Get auto-extracted entities from messages (bank accounts 'bank_card', phone numbers 'phone', links 'url').")]
    public async Task<string> GetExtractedEntitiesAsync(
        [Description("Entity type filter: 'bank_card', 'phone', 'url' (empty for all)")] string? entityType = null,
        [Description("Maximum number of results to return (default 50)")] int limit = 50,
        [Description("Content filter: substring match against value or raw text (optional)")] string? contains = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<ExtractedEntity> entities = await repo.GetExtractedEntitiesAsync(entityType, limit, contains, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(entities, ZaloMcpJsonContext.Default.IReadOnlyListExtractedEntity);
    }

    [McpServerTool(Name = "zalo_get_reminders")]
    [Description("Get auto-detected appointments, meetings, and dates from Zalo messages.")]
    public async Task<string> GetRemindersAsync(
        [Description("Maximum number of results to return (default 50)")] int limit = 50,
        [Description("Content filter: substring match against reminder title or raw text (optional)")] string? contains = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<ExtractedReminder> reminders = await repo.GetRemindersAsync(limit, contains, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(reminders, ZaloMcpJsonContext.Default.IReadOnlyListExtractedReminder);
    }

    [McpServerTool(Name = "zalo_get_urgent_messages")]
    [Description("Filter urgent or high-priority Zalo messages (e.g. urgent requests, escalations, priority support).")]
    public async Task<string> GetUrgentMessagesAsync(
        [Description("Maximum number of results to return (default 50)")] int limit = 50,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<SavedMessage> urgentMessages = await repo.GetUrgentMessagesAsync(limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(urgentMessages, ZaloMcpJsonContext.Default.IReadOnlyListSavedMessage);
    }

    [McpServerTool(Name = "zalo_get_contact_insights")]
    [Description("View CRM interaction memory for a Zalo user (total message exchange, last active timestamp, first contact date).")]
    public async Task<string> GetContactInsightsAsync(
        [Description("User ID of the Zalo user")] string userId,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        ContactInsight? insight = await repo.GetContactInsightAsync(userId, ct).ConfigureAwait(false);
        if (insight == null)
        {
            return JsonSerializer.Serialize(new { message = "No interaction history found for this user." });
        }
        return JsonSerializer.Serialize(insight, ZaloMcpJsonContext.Default.ContactInsight);
    }

    [McpServerTool(Name = "zalo_get_chat_summary")]
    [Description("Get recent Zalo conversations with their latest messages for AI executive summaries.")]
    public async Task<string> GetChatSummaryAsync(
        [Description("Maximum number of recent conversations to retrieve (default 20)")] int limit = 20,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<SavedThread> threads = await repo.GetRecentThreadsAsync(limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(threads, ZaloMcpJsonContext.Default.IReadOnlyListSavedThread);
    }

    [McpServerTool(Name = "zalo_get_analytics")]
    [Description("Analytics and statistics on Zalo chat data (sent/received counts, top active contacts, entity distribution).")]
    public async Task<string> GetAnalyticsAsync(
        [Description("Analysis time window in days (default 30)")] int days = 30,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        ZaloAnalyticsResult analytics = await repo.GetAnalyticsAsync(days, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(analytics);
    }

    /// <summary>Overload for calling without accountUid.</summary>
    public Task<string> GetContactInsightsAsync(string userId, CancellationToken ct) =>
        this.GetContactInsightsAsync(userId, accountUid: null, ct);

    [McpServerTool(Name = "zalo_schedule_message")]
    [Description("Schedule a text message to be sent at a future time (e.g. '+15m', '+2h', '+1d', '18:30', or '2026-12-31 09:00').")]
    public async Task<string> ScheduleMessageAsync(
        [Description("Recipient User ID or Group ID (ThreadId)")] string threadId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Schedule time expression ('+15m', '+2h', 'HH:MM', or 'YYYY-MM-DD HH:MM')")] string when,
        [Description("Message content to send")] string text,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        if (!MessageRepository.TryParseScheduleTime(when, DateTimeOffset.Now, out long sendAtUnix))
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Invalid schedule time expression '{when}'. Use '+15m', '+2h', '+1d', 'HH:MM', or 'YYYY-MM-DD HH:MM' in the future."
            });
        }

        MessageRepository repo = this.ResolveRepository(accountUid);
        long id = await repo.ScheduleMessageAsync(threadId, threadType, text, sendAtUnix, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            status = "scheduled",
            id,
            thread_id = threadId,
            thread_type = threadType,
            send_at_unix = sendAtUnix,
            send_at_iso = DateTimeOffset.FromUnixTimeSeconds(sendAtUnix).ToLocalTime().ToString("o")
        });
    }

    [McpServerTool(Name = "zalo_list_scheduled_messages")]
    [Description("List scheduled messages in the queue ('pending', 'sent', 'failed', or 'all').")]
    public async Task<string> ListScheduledMessagesAsync(
        [Description("Status filter: 'pending' (default), 'sent', 'failed', or 'all'")] string status = "pending",
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<ScheduledMessageRow> list = await repo.GetScheduledMessagesAsync(status, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(list, ZaloMcpJsonContext.Default.IReadOnlyListScheduledMessageRow);
    }

    [McpServerTool(Name = "zalo_cancel_scheduled_message")]
    [Description("Cancel a pending scheduled message by its ID.")]
    public async Task<string> CancelScheduledMessageAsync(
        [Description("Scheduled message ID to cancel")] long id,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        bool cancelled = await repo.CancelScheduledMessageAsync(id, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            status = cancelled ? "cancelled" : "not_found_or_already_dispatched",
            id
        });
    }

    [McpServerTool(Name = "zalo_configure_autoreply")]
    [Description("Manage automatic reply rules: 'list' rules, 'add' a rule (substring, 'regex:...', or '*'), or 'delete' a rule by ID.")]
    public async Task<string> ConfigureAutoReplyAsync(
        [Description("Action: 'list', 'add', or 'delete'")] string action = "list",
        [Description("Trigger pattern for 'add' (e.g. 'xin chào', 'regex:^gia\\b', or '*' for catch-all)")] string? pattern = null,
        [Description("Reply text template for 'add'")] string? replyTemplate = null,
        [Description("Rule ID for 'delete'")] long? ruleId = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        MessageRepository repo = this.ResolveRepository(accountUid);

        switch (action.Trim().ToLowerInvariant())
        {
            case "list":
                IReadOnlyList<AutoReplyRule> rules = await repo.GetAutoReplyRulesAsync(activeOnly: false, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(rules, ZaloMcpJsonContext.Default.IReadOnlyListAutoReplyRule);

            case "add":
                if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(replyTemplate))
                {
                    return JsonSerializer.Serialize(new { error = "Both 'pattern' and 'replyTemplate' are required for 'add'." });
                }
                long id = await repo.UpsertAutoReplyRuleAsync(pattern, replyTemplate, isActive: true, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "added", id, pattern, reply_template = replyTemplate });

            case "delete":
                if (ruleId is null or <= 0)
                {
                    return JsonSerializer.Serialize(new { error = "Valid 'ruleId' is required for 'delete'." });
                }
                bool deleted = await repo.DeleteAutoReplyRuleAsync(ruleId.Value, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = deleted ? "deleted" : "not_found", id = ruleId.Value });

            default:
                return JsonSerializer.Serialize(new { error = "Invalid action. Supported actions: 'list', 'add', 'delete'." });
        }
    }

    private (ZaloSession Session, ZaloAccountContext Account) ResolveAccountSession(string? accountUid)
    {
        this._sessionManager.EnsureAuthenticated(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;
        return (account.Session, account);
    }

    [McpServerTool(Name = "zalo_manage_zalo_reminders")]
    [Description("Manage official Zalo reminders (/api/board/*) in 1-on-1 or group chats: 'list', 'create', 'edit', 'remove', or 'get'.")]
    public async Task<string> ManageZaloRemindersAsync(
        [Description("Action: 'list', 'create', 'edit', 'remove', or 'get'")] string action,
        [Description("Thread ID (User ID or Group ID; for 'get' pass the reminderId here)")] string threadId,
        [Description("True if threadId is a group chat, false for 1-on-1 user chat")] bool isGroup = false,
        [Description("Reminder title/content (required for 'create' and 'edit')")] string? title = null,
        [Description("Reminder ID (required for 'edit' and 'remove')")] string? reminderId = null,
        [Description("Unix timestamp in milliseconds for reminder start time (0 for now)")] long startTimeMs = 0,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        (ZaloSession session, ZaloAccountContext account) = this.ResolveAccountSession(accountUid);

        switch (action.Trim().ToLowerInvariant())
        {
            case "list":
                IReadOnlyList<ZaloReminderItem> list = await account.Client.GetListReminderAsync(session, threadId, isGroup, 1, 20, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(list, ZaloMcpJsonContext.Default.IReadOnlyListZaloReminderItem);

            case "create":
                if (string.IsNullOrWhiteSpace(title))
                {
                    return JsonSerializer.Serialize(new { error = "'title' is required for 'create'." });
                }
                ZaloReminderItem created = await account.Client.CreateReminderAsync(
                    session,
                    threadId,
                    isGroup,
                    new ZaloReminderOptions(title, StartTimeMs: startTimeMs),
                    ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(created, ZaloMcpJsonContext.Default.ZaloReminderItem);

            case "edit":
                if (string.IsNullOrWhiteSpace(reminderId) || string.IsNullOrWhiteSpace(title))
                {
                    return JsonSerializer.Serialize(new { error = "Both 'reminderId' and 'title' are required for 'edit'." });
                }
                ZaloReminderItem edited = await account.Client.EditReminderAsync(
                    session,
                    threadId,
                    isGroup,
                    reminderId,
                    new ZaloReminderOptions(title, StartTimeMs: startTimeMs),
                    ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(edited, ZaloMcpJsonContext.Default.ZaloReminderItem);

            case "remove":
                if (string.IsNullOrWhiteSpace(reminderId))
                {
                    return JsonSerializer.Serialize(new { error = "'reminderId' is required for 'remove'." });
                }
                await account.Client.RemoveReminderAsync(session, threadId, isGroup, reminderId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", thread_id = threadId, reminder_id = reminderId, action = "removed" });

            case "get":
                ZaloReminderItem item = await account.Client.GetReminderAsync(session, threadId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(item, ZaloMcpJsonContext.Default.ZaloReminderItem);

            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'." });
        }
    }

    [McpServerTool(Name = "zalo_manage_labels")]
    [Description("Manage Zalo CRM conversation labels: 'list' all labels, 'assign' a thread to a label ID, or 'unassign' a thread from a label ID.")]
    public async Task<string> ManageLabelsAsync(
        [Description("Action: 'list', 'assign', or 'unassign'")] string action,
        [Description("Label ID (required for 'assign' and 'unassign')")] int? labelId = null,
        [Description("Conversation Thread ID (required for 'assign' and 'unassign')")] string? threadId = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        (ZaloSession session, ZaloAccountContext account) = this.ResolveAccountSession(accountUid);

        IReadOnlyList<ZaloLabelItem> current = await account.Client.GetLabelsAsync(session, ct).ConfigureAwait(false);
        string act = action.Trim().ToLowerInvariant();
        if (act == "list")
        {
            return JsonSerializer.Serialize(current, ZaloMcpJsonContext.Default.IReadOnlyListZaloLabelItem);
        }

        if (labelId is null || string.IsNullOrWhiteSpace(threadId))
        {
            return JsonSerializer.Serialize(new { error = "Both 'labelId' and 'threadId' are required for 'assign' / 'unassign'." });
        }

        List<ZaloLabelItem> updated = new(current.Count);
        foreach (ZaloLabelItem item in current)
        {
            if (item.Id != labelId.Value)
            {
                updated.Add(item);
                continue;
            }

            List<string> convs = [.. item.Conversations];
            if (act == "assign" && !convs.Contains(threadId, StringComparer.Ordinal))
            {
                convs.Add(threadId);
            }
            else if (act == "unassign")
            {
                _ = convs.RemoveAll(c => string.Equals(c, threadId, StringComparison.Ordinal));
            }

            updated.Add(item with { Conversations = convs });
        }

        IReadOnlyList<ZaloLabelItem> result = await account.Client.UpdateLabelsAsync(session, updated, 0, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, ZaloMcpJsonContext.Default.IReadOnlyListZaloLabelItem);
    }

    [McpServerTool(Name = "zalo_manage_quick_messages")]
    [Description("Manage Zalo Quick Message shortcut templates (/api/quickmessage/*): 'list', 'add', 'update', or 'remove'.")]
    public async Task<string> ManageQuickMessagesAsync(
        [Description("Action: 'list', 'add', 'update', or 'remove'")] string action,
        [Description("Shortcut keyword (required for 'add' and 'update')")] string? keyword = null,
        [Description("Template message content (required for 'add' and 'update')")] string? title = null,
        [Description("Quick message item ID (required for 'update' and 'remove')")] int? itemId = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        (ZaloSession session, ZaloAccountContext account) = this.ResolveAccountSession(accountUid);

        switch (action.Trim().ToLowerInvariant())
        {
            case "list":
                IReadOnlyList<ZaloQuickMessage> items = await account.Client.GetQuickMessagesAsync(session, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(items, ZaloMcpJsonContext.Default.IReadOnlyListZaloQuickMessage);

            case "add":
                if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrWhiteSpace(title))
                {
                    return JsonSerializer.Serialize(new { error = "Both 'keyword' and 'title' are required for 'add'." });
                }
                ZaloQuickMessage added = await account.Client.AddQuickMessageAsync(session, keyword, title, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(added, ZaloMcpJsonContext.Default.ZaloQuickMessage);

            case "update":
                if (itemId is null || string.IsNullOrWhiteSpace(keyword) || string.IsNullOrWhiteSpace(title))
                {
                    return JsonSerializer.Serialize(new { error = "'itemId', 'keyword', and 'title' are required for 'update'." });
                }
                ZaloQuickMessage updated = await account.Client.UpdateQuickMessageAsync(session, itemId.Value, keyword, title, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(updated, ZaloMcpJsonContext.Default.ZaloQuickMessage);

            case "remove":
                if (itemId is null)
                {
                    return JsonSerializer.Serialize(new { error = "'itemId' is required for 'remove'." });
                }
                await account.Client.RemoveQuickMessageAsync(session, [itemId.Value], ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", item_id = itemId.Value, action = "removed" });

            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'." });
        }
    }

    [McpServerTool(Name = "zalo_manage_account_profile")]
    [Description("Manage Zalo account profile, privacy settings, active ping, and hidden conversation PIN: 'update_profile', 'update_setting', 'active_status', 'update_hidden_pin', or 'reset_hidden_pin'.")]
    public async Task<string> ManageAccountProfileAsync(
        [Description("Action: 'update_profile', 'update_setting', 'active_status', 'update_hidden_pin', or 'reset_hidden_pin'")] string action,
        [Description("Display name (for update_profile), setting key (for update_setting), 'true'/'false' (for active_status), or 4-digit PIN (for update_hidden_pin)")] string? primaryValue = null,
        [Description("Date of birth YYYY-MM-DD (for update_profile) or integer setting value (for update_setting)")] string? secondaryValue = null,
        [Description("Gender: 0 for Male, 1 for Female (for update_profile)")] int gender = 0,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        (ZaloSession session, ZaloAccountContext account) = this.ResolveAccountSession(accountUid);

        switch (action.Trim().ToLowerInvariant())
        {
            case "update_profile":
                if (string.IsNullOrWhiteSpace(primaryValue) || string.IsNullOrWhiteSpace(secondaryValue))
                {
                    return JsonSerializer.Serialize(new { error = "Display name ('primaryValue') and DOB YYYY-MM-DD ('secondaryValue') are required." });
                }
                await account.Client.UpdateProfileAsync(session, new ZaloProfileUpdateOptions(primaryValue, secondaryValue, gender), ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "profile_updated", name = primaryValue, dob = secondaryValue });

            case "update_setting":
                if (string.IsNullOrWhiteSpace(primaryValue) || !int.TryParse(secondaryValue, System.Globalization.CultureInfo.InvariantCulture, out int settingVal))
                {
                    return JsonSerializer.Serialize(new { error = "Setting key ('primaryValue') and integer value ('secondaryValue') are required." });
                }
                await account.Client.UpdateSettingsAsync(session, primaryValue, settingVal, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "setting_updated", setting = primaryValue, value = settingVal });

            case "active_status":
                bool isActive = !string.Equals(primaryValue, "false", StringComparison.OrdinalIgnoreCase) && primaryValue != "0";
                await account.Client.UpdateActiveStatusAsync(session, isActive, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "active_status_updated", is_active = isActive });

            case "update_hidden_pin":
                if (string.IsNullOrWhiteSpace(primaryValue))
                {
                    return JsonSerializer.Serialize(new { error = "4-digit PIN ('primaryValue') is required." });
                }
                await account.Client.UpdateHiddenConversPinAsync(session, primaryValue, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "hidden_pin_updated" });

            case "reset_hidden_pin":
                await account.Client.ResetHiddenConversPinAsync(session, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "hidden_pin_reset" });

            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'." });
        }
    }
}
