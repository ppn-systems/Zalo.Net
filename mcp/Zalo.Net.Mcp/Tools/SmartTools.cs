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

    private MessageRepository ResolveRepository(string? accountUid) =>
        this._sessionManager.GetAccount(accountUid)?.Repository ?? this._sessionManager.Repository;

    [McpServerTool(Name = "zalo_smart_search")]
    [Description("Tìm kiếm thông minh trên cả tin nhắn, thực thể trích xuất (STK, SĐT, Link) và lịch hẹn nhắc nhở.")]
    public async Task<string> SmartSearchAsync(
        [Description("Từ khóa hoặc thông tin cần tìm kiếm")] string query,
        [Description("Số lượng kết quả tối đa (mặc định 50)")] int limit = 50,
        [Description("UID tài khoản Zalo (tùy chọn, mặc định lấy tài khoản đang active)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        SmartSearchResult result = await repo.SmartSearchAsync(query, limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, ZaloMcpJsonContext.Default.SmartSearchResult);
    }

    [McpServerTool(Name = "zalo_get_extracted_entities")]
    [Description("Lấy danh sách thông tin đã tự động trích xuất từ tin nhắn Zalo (Số tài khoản ngân hàng 'bank_card', Số điện thoại 'phone', Links 'url').")]
    public async Task<string> GetExtractedEntitiesAsync(
        [Description("Loại thực thể cần lấy: 'bank_card', 'phone', 'url' (để trống nếu lấy tất cả)")] string? entityType = null,
        [Description("Số lượng kết quả tối đa (mặc định 50)")] int limit = 50,
        [Description("Lọc theo nội dung: chỉ lấy thực thể có giá trị hoặc tin nhắn gốc chứa chuỗi này (tùy chọn)")] string? contains = null,
        [Description("UID tài khoản Zalo (tùy chọn, mặc định lấy tài khoản đang active)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<ExtractedEntity> entities = await repo.GetExtractedEntitiesAsync(entityType, limit, contains, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(entities, ZaloMcpJsonContext.Default.IReadOnlyListExtractedEntity);
    }

    [McpServerTool(Name = "zalo_get_reminders")]
    [Description("Lấy danh sách các lịch hẹn, cuộc họp, thời gian đã tự động phát hiện từ các tin nhắn Zalo.")]
    public async Task<string> GetRemindersAsync(
        [Description("Số lượng kết quả tối đa (mặc định 50)")] int limit = 50,
        [Description("Lọc theo nội dung: chỉ lấy nhắc hẹn có tiêu đề hoặc tin nhắn gốc chứa chuỗi này (tùy chọn)")] string? contains = null,
        [Description("UID tài khoản Zalo (tùy chọn, mặc định lấy tài khoản đang active)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<ExtractedReminder> reminders = await repo.GetRemindersAsync(limit, contains, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(reminders, ZaloMcpJsonContext.Default.IReadOnlyListExtractedReminder);
    }

    [McpServerTool(Name = "zalo_get_urgent_messages")]
    [Description("Lọc danh sách các tin nhắn Zalo quan trọng / khẩn cấp (gấp, khiếu nại, hỗ trợ ngay).")]
    public async Task<string> GetUrgentMessagesAsync(
        [Description("Số lượng kết quả tối đa (mặc định 50)")] int limit = 50,
        [Description("UID tài khoản Zalo (tùy chọn, mặc định lấy tài khoản đang active)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<SavedMessage> urgentMessages = await repo.GetUrgentMessagesAsync(limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(urgentMessages, ZaloMcpJsonContext.Default.IReadOnlyListSavedMessage);
    }

    [McpServerTool(Name = "zalo_get_contact_insights")]
    [Description("Xem hồ sơ tương tác CRM memory của một người dùng Zalo (tổng tin nhắn trao đổi, lần hoạt động cuối, ngày đầu giao tiếp).")]
    public async Task<string> GetContactInsightsAsync(
        [Description("User ID của người dùng Zalo")] string userId,
        [Description("UID tài khoản Zalo (tùy chọn, mặc định lấy tài khoản đang active)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        ContactInsight? insight = await repo.GetContactInsightAsync(userId, ct).ConfigureAwait(false);
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
        [Description("UID tài khoản Zalo (tùy chọn, mặc định lấy tài khoản đang active)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this.ResolveRepository(accountUid);
        IReadOnlyList<SavedThread> threads = await repo.GetRecentThreadsAsync(limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(threads, ZaloMcpJsonContext.Default.IReadOnlyListSavedThread);
    }

    [McpServerTool(Name = "zalo_get_analytics")]
    [Description("Thống kê phân tích dữ liệu trò chuyện Zalo (tổng số tin nhắn gửi/nhận, top bạn bè tương tác nhiều nhất, phân bổ STK ngân hàng/SĐT bóc tách).")]
    public async Task<string> GetAnalyticsAsync(
        [Description("Số ngày cần phân tích thống kê (mặc định 30)")] int days = 30,
        [Description("UID tài khoản Zalo (tùy chọn, mặc định lấy tài khoản đang active)")] string? accountUid = null,
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
