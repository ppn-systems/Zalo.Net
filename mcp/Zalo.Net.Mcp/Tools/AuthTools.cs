// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zalo.Net.Contracts;

namespace Zalo.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Zalo Authentication and Session operations.
/// Supports multi-account listing, switching, and independent session management.
/// </summary>
[McpServerToolType]
public sealed class AuthTools(ZaloSessionManager sessionManager)
{
    private readonly ZaloSessionManager _sessionManager = sessionManager;

    [McpServerTool(Name = "zalo_list_accounts")]
    [Description("Liệt kê tất cả các tài khoản Zalo đã đăng nhập trên hệ thống cùng trạng thái kích hoạt và kết nối.")]
    public Task<string> ListAccountsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ZaloAccountSummary> accounts = this._sessionManager.ListAccounts();
        return Task.FromResult(JsonSerializer.Serialize(accounts, ZaloMcpJsonContext.Default.IReadOnlyListZaloAccountSummary));
    }

    [McpServerTool(Name = "zalo_switch_account")]
    [Description("Chuyển đổi tài khoản Zalo đang kích hoạt (active) cho các tác vụ tiếp theo.")]
    public Task<string> SwitchAccountAsync(
        [Description("UID của tài khoản Zalo muốn kích hoạt")] string accountUid,
        CancellationToken ct = default)
    {
        bool success = this._sessionManager.SwitchActiveAccount(accountUid);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            success = success,
            active_uid = success ? accountUid : this._sessionManager.ActiveAccountUid,
            message = success ? $"Đã chuyển active sang tài khoản {accountUid} thành công." : $"Không tìm thấy tài khoản UID {accountUid}."
        }));
    }

    [McpServerTool(Name = "zalo_disconnect_account")]
    [Description("Đăng xuất hoặc ngắt kết nối một tài khoản Zalo cụ thể.")]
    public async Task<string> DisconnectAccountAsync(
        [Description("UID của tài khoản Zalo cần ngắt kết nối")] string accountUid,
        CancellationToken ct = default)
    {
        bool success = await this._sessionManager.DisconnectAccountAsync(accountUid, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            success = success,
            message = success ? $"Đã ngắt kết nối tài khoản {accountUid} thành công." : $"Không tìm thấy tài khoản UID {accountUid}."
        });
    }

    [McpServerTool(Name = "zalo_login_qr")]
    [Description("Bắt đầu luồng đăng nhập Zalo bằng mã QR code. Trả về mã QR dạng Base64 PNG và Session ID để kiểm tra trạng thái.")]
    public async Task<string> StartQrLoginAsync(CancellationToken ct = default)
    {
        ZaloQrSession qrSession = await this._sessionManager.StartQrLoginAsync(ct).ConfigureAwait(false);
        var response = new
        {
            session_id = qrSession.SessionId,
            status = "Pending",
            expires_at = qrSession.ExpiresAt.ToString("o"),
            qr_image_base64 = qrSession.QrImageBase64,
            instruction = "Quét mã QR bằng ứng dụng Zalo di động, sau đó gọi zalo_check_qr_status để hoàn tất."
        };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_check_qr_status")]
    [Description("Kiểm tra trạng thái quét mã QR của phiên đăng nhập (Pending, Scanned, Connected, Expired).")]
    public async Task<string> CheckQrStatusAsync(
        [Description("ID của phiên quét mã QR thu được từ zalo_login_qr")] string sessionId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedId))
        {
            return JsonSerializer.Serialize(new { error = "Session ID không hợp lệ." });
        }

        ZaloLoginState state = await this._sessionManager.PollQrStatusAsync(parsedId, ct).ConfigureAwait(false);
        var response = new
        {
            session_id = state.SessionId,
            status = state.Status.ToString(),
            display_name = state.DisplayName ?? "",
            avatar_url = state.AvatarUrl ?? "",
            error_message = state.ErrorMessage ?? ""
        };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_get_profile")]
    [Description("Lấy thông tin tài khoản Zalo đang đăng nhập (hoặc của tài khoản theo accountUid).")]
    public async Task<string> GetProfileAsync(
        [Description("UID của tài khoản Zalo cần xem thông tin (để trống nếu lấy tài khoản đang active)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        if (!this._sessionManager.IsAuthenticated)
        {
            return JsonSerializer.Serialize(new
            {
                is_authenticated = false,
                message = "Chưa đăng nhập tài khoản Zalo nào. Hãy gọi zalo_login_qr để đăng nhập."
            });
        }

        this._sessionManager.EnsureAuthenticated(accountUid);
        ZaloAccountContext? account = this._sessionManager.GetAccount(accountUid);
        if (account == null)
        {
            return JsonSerializer.Serialize(new
            {
                is_authenticated = false,
                message = $"Không tìm thấy tài khoản Zalo với UID '{accountUid}'."
            });
        }

        ZaloSession session = account.Session;
        ZaloUserProfile profile = await ZaloWebClient.GetUserInfoAsync(session, session.Uid, ct).ConfigureAwait(false);

        var response = new
        {
            is_authenticated = true,
            uid = profile.Uid,
            display_name = profile.DisplayName,
            avatar_url = profile.AvatarUrl ?? "",
            is_active = profile.Uid == this._sessionManager.ActiveAccountUid
        };
        return JsonSerializer.Serialize(response);
    }

    /// <summary>Overload for calling without accountUid.</summary>
    public Task<string> GetProfileAsync(CancellationToken ct) => this.GetProfileAsync(accountUid: null, ct);
}
