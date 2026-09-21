// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
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
}
