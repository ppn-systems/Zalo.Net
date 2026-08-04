// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Smart AI Search, Entity Extraction, Reminders, Urgent Messages, and CRM Insights.
/// </summary>
[McpServerToolType]
public sealed class SmartTools(ZaloSessionManager sessionManager)
{
    private readonly ZaloSessionManager _sessionManager = sessionManager;

    [McpServerTool(Name = "zalo_smart_search")]
    [Description("Tìm kiếm thông minh trên cả tin nhắn, thực thể trích xuất (STK, SĐT, Link) và lịch hẹn nhắc nhở.")]
    public async Task<string> SmartSearchAsync(
        [Description("Từ khóa hoặc thông tin cần tìm kiếm")] string query,
        [Description("Số lượng kết quả tối đa (mặc định 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        SmartSearchResult result = await this._sessionManager.Repository.SmartSearchAsync(query, limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, ZaloMcpJsonContext.Default.SmartSearchResult);
    }

    [McpServerTool(Name = "zalo_get_extracted_entities")]
    [Description("Lấy danh sách thông tin đã tự động trích xuất từ tin nhắn Zalo (Số tài khoản ngân hàng 'bank_card', Số điện thoại 'phone', Links 'url').")]
    public async Task<string> GetExtractedEntitiesAsync(
        [Description("Loại thực thể cần lấy: 'bank_card', 'phone', 'url' (để trống nếu lấy tất cả)")] string? entityType = null,
        [Description("Số lượng kết quả tối đa (mặc định 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        IReadOnlyList<ExtractedEntity> entities = await this._sessionManager.Repository.GetExtractedEntitiesAsync(entityType, limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(entities, ZaloMcpJsonContext.Default.IReadOnlyListExtractedEntity);
    }

    [McpServerTool(Name = "zalo_get_reminders")]
    [Description("Lấy danh sách các lịch hẹn, cuộc họp, thời gian đã tự động phát hiện từ các tin nhắn Zalo.")]
    public async Task<string> GetRemindersAsync(
        [Description("Số lượng kết quả tối đa (mặc định 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        IReadOnlyList<ExtractedReminder> reminders = await this._sessionManager.Repository.GetRemindersAsync(limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(reminders, ZaloMcpJsonContext.Default.IReadOnlyListExtractedReminder);
    }

    [McpServerTool(Name = "zalo_get_urgent_messages")]
    [Description("Lọc danh sách các tin nhắn Zalo quan trọng / khẩn cấp (gấp, khiếu nại, hỗ trợ ngay).")]
    public async Task<string> GetUrgentMessagesAsync(
        [Description("Số lượng kết quả tối đa (mặc định 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        IReadOnlyList<SavedMessage> urgentMessages = await this._sessionManager.Repository.GetUrgentMessagesAsync(limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(urgentMessages, ZaloMcpJsonContext.Default.IReadOnlyListSavedMessage);
    }

    [McpServerTool(Name = "zalo_get_contact_insights")]
    [Description("Xem hồ sơ tương tác CRM memory của một người dùng Zalo (tổng tin nhắn trao đổi, lần hoạt động cuối, ngày đầu giao tiếp).")]
    public async Task<string> GetContactInsightsAsync(
        [Description("User ID của người dùng Zalo")] string userId,
        CancellationToken ct = default)
    {
        ContactInsight? insight = await this._sessionManager.Repository.GetContactInsightAsync(userId, ct).ConfigureAwait(false);
        if (insight == null)
        {
            return JsonSerializer.Serialize(new { message = "Chưa có hồ sơ tương tác cho người dùng này." });
        }
        return JsonSerializer.Serialize(insight, ZaloMcpJsonContext.Default.ContactInsight);
    }

    [McpServerTool(Name = "zalo_get_chat_summary")]
    [Description("Lấy danh sách các cuộc trò chuyện Zalo mới nhất cùng tin nhắn cuối cùng để AI tổng hợp báo cáo.")]
    public async Task<string> GetChatSummaryAsync(
        [Description("Số lượng cuộc trò chuyện gần nhất (mặc định 20)")] int limit = 20,
        CancellationToken ct = default)
    {
        IReadOnlyList<SavedThread> threads = await this._sessionManager.Repository.GetRecentThreadsAsync(limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(threads, ZaloMcpJsonContext.Default.IReadOnlyListSavedThread);
    }
}
