// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Zalo.Net.Mcp;

/// <summary>
/// Runtime guide resources and dynamic data endpoints for AI assistants.
/// </summary>
[McpServerResourceType]
public sealed class Resources
{
    [McpServerResource(UriTemplate = "guide://overview", Name = "overview", MimeType = "text/markdown")]
    [Description("Tổng quan và hướng dẫn sử dụng Zalo MCP Server bằng Tiếng Việt.")]
    public static string Overview() =>
        """
        # Zalo.Net MCP Server — Hướng dẫn sử dụng (Vietnamese Context)

        Zalo MCP Server đóng vai trò là cầu nối giữa Trợ lý AI (Claude, Antigravity, Cursor) và tài khoản Zalo Web Client của bạn.

        ## Các tính năng chính:
        1. **Xác thực QR Code (`zalo_login_qr`, `zalo_check_qr_status`)**: Đăng nhập Zalo bằng mã QR.
        2. **Gửi & Nhận tin nhắn (`zalo_send_text`, `zalo_quote_message`, `zalo_send_file`, `zalo_send_sticker`, `zalo_react_message`, `zalo_recall_message`)**: Gửi tin nhắn cá nhân hoặc nhóm chat.
        3. **Truy vấn Thông minh & SQLite FTS5 (`zalo_smart_search`, `zalo_get_extracted_entities`, `zalo_get_chat_history`)**: Tìm kiếm thông tin, số tài khoản ngân hàng, số điện thoại cực nhanh trong dưới 1ms.
        4. **Quản lý danh bạ & Nhóm (`zalo_find_user`, `zalo_list_contacts`, `zalo_list_groups`, `zalo_create_group`, `zalo_manage_group`)**: Tìm người dùng theo số điện thoại, tạo nhóm chat mới, quản lý thành viên.

        ## Quy trình làm việc đề xuất cho AI:
        - **Bước 1**: Gọi `zalo_get_profile` để kiểm tra tài khoản đã đăng nhập chưa.
        - **Bước 2**: Nếu chưa đăng nhập, gọi `zalo_login_qr` để lấy mã QR cho người dùng quét.
        - **Bước 3**: Sau khi đăng nhập thành công, sử dụng `zalo_get_chat_history` hoặc `zalo_smart_search` để đọc ngữ cảnh trò chuyện trước khi gửi tin nhắn qua `zalo_send_text`.
        """;

    [McpServerResource(UriTemplate = "guide://smart-agent", Name = "smart-agent", MimeType = "text/markdown")]
    [Description("Quy tắc xử lý thông minh dành cho AI Agent khi tương tác qua Zalo MCP.")]
    public static string SmartAgentGuide() =>
        """
        # Smart AI Agent Guidelines for Zalo MCP

        1. **Tra cứu thông tin tài khoản ngân hàng / Số điện thoại**:
           Khi người dùng hỏi "Ai vừa gửi số tài khoản?", "Số điện thoại của ai?", hãy ưu tiên gọi tool `zalo_get_extracted_entities` với `entity_type = 'bank_card'` hoặc `'phone'` thay vì đọc lại toàn bộ chat.

        2. **Gửi tin nhắn an toàn**:
           - Luôn dùng `zalo_find_user` hoặc `zalo_list_contacts` để kiểm tra `thread_id` hoặc User ID chính xác trước khi gửi tin nhắn.
           - Đối với nhóm chat (`threadType = 'Group'`), đảm bảo `thread_id` là Group ID chuẩn.

        3. **Tóm tắt & Báo cáo hàng ngày**:
           Sử dụng `zalo_get_chat_summary` để có cái nhìn tổng quan về các cuộc trò chuyện gần đây, sau đó dùng `zalo_get_chat_history` để đi sâu vào cuộc trò chuyện cụ thể.
        """;
}
