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
    [Description("List all authenticated Zalo accounts with their active and connection states.")]
    public Task<string> ListAccountsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ZaloAccountSummary> accounts = this._sessionManager.ListAccounts();
        return Task.FromResult(JsonSerializer.Serialize(accounts, ZaloMcpJsonContext.Default.IReadOnlyListZaloAccountSummary));
    }

    [McpServerTool(Name = "zalo_switch_account")]
    [Description("Switch the active Zalo account for subsequent operations.")]
    public Task<string> SwitchAccountAsync(
        [Description("UID of the Zalo account to activate")] string accountUid,
        CancellationToken ct = default)
    {
        bool success = this._sessionManager.SwitchActiveAccount(accountUid);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            success = success,
            active_uid = success ? accountUid : this._sessionManager.ActiveAccountUid,
            message = success ? $"Switched active account to {accountUid} successfully." : $"Account UID {accountUid} not found."
        }));
    }

    [McpServerTool(Name = "zalo_disconnect_account")]
    [Description("Log out or disconnect a specific Zalo account.")]
    public async Task<string> DisconnectAccountAsync(
        [Description("UID of the Zalo account to disconnect")] string accountUid,
        CancellationToken ct = default)
    {
        bool success = await this._sessionManager.DisconnectAccountAsync(accountUid, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            success = success,
            message = success ? $"Disconnected account {accountUid} successfully." : $"Account UID {accountUid} not found."
        });
    }

    [McpServerTool(Name = "zalo_login_qr")]
    [Description("Start Zalo QR code login flow. Returns Base64 PNG QR image and Session ID to poll status.")]
    public async Task<string> StartQrLoginAsync(CancellationToken ct = default)
    {
        ZaloQrSession qrSession = await this._sessionManager.StartQrLoginAsync(ct).ConfigureAwait(false);
        var response = new
        {
            session_id = qrSession.SessionId,
            status = "Pending",
            expires_at = qrSession.ExpiresAt.ToString("o"),
            qr_image_base64 = qrSession.QrImageBase64,
            instruction = "Scan this QR code using the Zalo mobile app, then call zalo_check_qr_status to complete authentication."
        };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_check_qr_status")]
    [Description("Check QR scan status of a login session (Pending, Scanned, Connected, Expired).")]
    public async Task<string> CheckQrStatusAsync(
        [Description("Session ID obtained from zalo_login_qr")] string sessionId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedId))
        {
            return JsonSerializer.Serialize(new { error = "Invalid session ID format." });
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
    [Description("Get profile information for the authenticated Zalo account (or by accountUid).")]
    public async Task<string> GetProfileAsync(
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        if (!this._sessionManager.IsAuthenticated)
        {
            return JsonSerializer.Serialize(new
            {
                is_authenticated = false,
                message = "No Zalo account logged in. Call zalo_login_qr to authenticate."
            });
        }

        this._sessionManager.EnsureAuthenticated(accountUid);
        ZaloAccountContext? account = this._sessionManager.GetAccount(accountUid);
        if (account == null)
        {
            return JsonSerializer.Serialize(new
            {
                is_authenticated = false,
                message = $"Zalo account with UID '{accountUid}' not found."
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
