// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zalo.Net.Contracts;

namespace Zalo.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Zalo Authentication and Session operations.
/// </summary>
[McpServerToolType]
public sealed class AuthTools(ZaloSessionManager sessionManager)
{
    private readonly ZaloSessionManager _sessionManager = sessionManager;

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
    [Description("Lấy thông tin tài khoản Zalo đang đăng nhập hiện tại.")]
    public async Task<string> GetProfileAsync(CancellationToken ct = default)
    {
        if (!this._sessionManager.IsAuthenticated || this._sessionManager.ActiveSession == null)
        {
            return JsonSerializer.Serialize(new
            {
                is_authenticated = false,
                message = "Chưa đăng nhập tài khoản Zalo. Hãy gọi zalo_login_qr để đăng nhập."
            });
        }

        ZaloSession session = this._sessionManager.ActiveSession;
        ZaloUserProfile profile = await ZaloWebClient.GetUserInfoAsync(session, session.Uid, ct).ConfigureAwait(false);

        var response = new
        {
            is_authenticated = true,
            uid = profile.Uid,
            display_name = profile.DisplayName,
            avatar_url = profile.AvatarUrl ?? ""
        };
        return JsonSerializer.Serialize(response);
    }
}
