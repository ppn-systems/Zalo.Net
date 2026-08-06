// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zalo.Net.Contracts;

namespace Zalo.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Zalo Contact, Friend Request, and Group management operations.
/// </summary>
[McpServerToolType]
public sealed class GroupTools(ZaloSessionManager sessionManager)
{
    private readonly ZaloSessionManager _sessionManager = sessionManager;

    [McpServerTool(Name = "zalo_find_user")]
    [Description("Tìm kiếm thông tin người dùng Zalo thông qua số điện thoại.")]
    public async Task<string> FindUserByPhoneAsync(
        [Description("Số điện thoại cần tìm kiếm (VD: '0912345678' hoặc '+84912345678')")] string phoneNumber,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloUserProfile profile = await ZaloWebClient.FindUserByPhoneAsync(session, phoneNumber, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(profile, ZaloMcpJsonContext.Default.ZaloUserProfile);
    }

    [McpServerTool(Name = "zalo_list_contacts")]
    [Description("Lấy danh sách bạn bè / danh bạ Zalo của tài khoản hiện tại.")]
    public async Task<string> ListContactsAsync(
        [Description("Số lượng bạn bè tối đa cần lấy (mặc định 1000)")] int count = 1000,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        IReadOnlyList<ZaloFriendInfo> friends = await ZaloWebClient.GetAllFriendsAsync(session, count: count, ct: ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(friends, ZaloMcpJsonContext.Default.IReadOnlyListZaloFriendInfo);
    }

    [McpServerTool(Name = "zalo_send_friend_request")]
    [Description("Gửi lời mời kết bạn đến người dùng Zalo qua User ID.")]
    public async Task<string> SendFriendRequestAsync(
        [Description("User ID của người nhận lời mời")] string targetUserId,
        [Description("Lời nhắn kết bạn (tùy chọn)")] string? message = null,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        await ZaloWebClient.SendFriendRequestAsync(session, targetUserId, message, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "friend_request_sent" });
    }

    [McpServerTool(Name = "zalo_accept_friend_request")]
    [Description("Chấp nhận lời mời kết bạn từ người dùng khác.")]
    public async Task<string> AcceptFriendRequestAsync(
        [Description("User ID của người gửi lời mời kết bạn")] string targetUserId,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        await ZaloWebClient.AcceptFriendRequestAsync(session, targetUserId, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "friend_request_accepted" });
    }

    [McpServerTool(Name = "zalo_block_user")]
    [Description("Chặn người dùng Zalo.")]
    public async Task<string> BlockUserAsync(
        [Description("User ID của người cần chặn")] string targetUserId,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        await ZaloWebClient.BlockUserAsync(session, targetUserId, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "user_blocked" });
    }

    [McpServerTool(Name = "zalo_unblock_user")]
    [Description("Bỏ chặn người dùng Zalo.")]
    public async Task<string> UnblockUserAsync(
        [Description("User ID của người cần bỏ chặn")] string targetUserId,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        await ZaloWebClient.UnblockUserAsync(session, targetUserId, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "user_unblocked" });
    }

    [McpServerTool(Name = "zalo_change_friend_alias")]
    [Description("Đổi biệt danh (gợi nhớ/nickname CRM) của người bạn trong danh bạ.")]
    public async Task<string> ChangeFriendAliasAsync(
        [Description("User ID của người bạn")] string targetUserId,
        [Description("Biệt danh mới cần đặt")] string alias,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        await ZaloWebClient.ChangeFriendAliasAsync(session, targetUserId, alias, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, alias });
    }

    [McpServerTool(Name = "zalo_list_groups")]
    [Description("Lấy danh sách tất cả các nhóm chat Zalo mà tài khoản hiện tại tham gia.")]
    public async Task<string> ListGroupsAsync(CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        IReadOnlyList<ZaloGroupInfo> groups = await ZaloWebClient.GetAllGroupsAsync(session, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(groups, ZaloMcpJsonContext.Default.IReadOnlyListZaloGroupInfo);
    }

    [McpServerTool(Name = "zalo_create_group")]
    [Description("Tạo một nhóm chat Zalo mới với danh sách danh bạ chỉ định.")]
    public async Task<string> CreateGroupAsync(
        [Description("Tên của nhóm chat mới")] string groupName,
        [Description("Danh sách các User ID của thành viên tham gia (phân cách bằng dấu phẩy)")] string memberIdsCsv,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(memberIdsCsv);

        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        string[] memberIds = memberIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ZaloGroupCreateResult result = await ZaloWebClient.CreateGroupAsync(session, groupName, memberIds, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(result, ZaloMcpJsonContext.Default.ZaloGroupCreateResult);
    }

    [McpServerTool(Name = "zalo_manage_group")]
    [Description("Quản lý nhóm chat: Thêm thành viên ('add'), Xóa thành viên ('remove'), Đổi tên nhóm ('rename'), Rời nhóm ('leave').")]
    public async Task<string> ManageGroupAsync(
        [Description("ID của nhóm chat (GroupId)")] string groupId,
        [Description("Hành động: 'add', 'remove', 'rename', 'leave'")] string action,
        [Description("Danh sách User ID thành viên (nếu add/remove) hoặc Tên nhóm mới (nếu rename)")] string value,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(value);

        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        string normalizedAction = action.Trim().ToLowerInvariant();
        switch (normalizedAction)
        {
            case "add":
                string[] addIds = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                await ZaloWebClient.AddUserToGroupAsync(session, groupId, addIds, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "add_members", group_id = groupId });

            case "remove":
                string[] removeIds = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                await ZaloWebClient.RemoveUserFromGroupAsync(session, groupId, removeIds, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "remove_members", group_id = groupId });

            case "rename":
                await ZaloWebClient.ChangeGroupNameAsync(session, groupId, value, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "rename_group", group_id = groupId, new_name = value });

            case "leave":
                await ZaloWebClient.LeaveGroupAsync(session, groupId, silent: false, ct: ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", action = "left_group", group_id = groupId });

            default:
                return JsonSerializer.Serialize(new { error = "Hành động không hợp lệ. Chọn 'add', 'remove', 'rename', hoặc 'leave'." });
        }
    }

    [McpServerTool(Name = "zalo_join_group_via_link")]
    [Description("Tham gia nhóm chat Zalo qua đường dẫn mời (VD: https://zalo.me/g/XXXXXXXXX).")]
    public async Task<string> JoinGroupViaLinkAsync(
        [Description("Đường dẫn liên kết mời tham gia nhóm")] string inviteUrl,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        using ZaloWebClient client = new(session.Proxy);
        await client.JoinGroupViaLinkAsync(session, inviteUrl, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", invite_url = inviteUrl, message = "Đã gửi yêu cầu tham gia nhóm." });
    }

    [McpServerTool(Name = "zalo_review_join_requests")]
    [Description("Duyệt hoặc từ chối danh sách người dùng xin tham gia nhóm chat.")]
    public async Task<string> ReviewJoinRequestsAsync(
        [Description("ID của nhóm chat (GroupId)")] string groupId,
        [Description("Danh sách User ID xin tham gia (phân cách bằng dấu phẩy)")] string memberUidsCsv,
        [Description("Đồng ý tham gia (true) hoặc Từ chối (false)")] bool approve,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(memberUidsCsv);

        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        string[] uids = memberUidsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using ZaloWebClient client = new(session.Proxy);
        await client.ReviewJoinRequestsAsync(session, groupId, uids, approve, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, approved = approve, member_count = uids.Length });
    }

    [McpServerTool(Name = "zalo_leave_group_silently")]
    [Description("Rời khỏi nhóm chat Zalo trong âm thầm (không phát thông báo rời nhóm).")]
    public async Task<string> LeaveGroupSilentlyAsync(
        [Description("ID của nhóm chat (GroupId)")] string groupId,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        using ZaloWebClient client = new(session.Proxy);
        await client.LeaveGroupSilentlyAsync(session, groupId, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, action = "left_group_silently" });
    }

    [McpServerTool(Name = "zalo_kick_group_member")]
    [Description("Xóa (Kick) một thành viên khỏi nhóm chat Zalo.")]
    public async Task<string> KickGroupMemberAsync(
        [Description("ID nhóm chat Zalo (GroupId)")] string groupId,
        [Description("User ID của thành viên cần xóa")] string memberUid,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        using ZaloWebClient client = new(session.Proxy);
        await client.KickGroupMemberAsync(session, groupId, memberUid, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, kicked_member_uid = memberUid });
    }

    [McpServerTool(Name = "zalo_promote_group_admin")]
    [Description("Thăng cấp phó nhóm / quản trị viên cho thành viên nhóm chat Zalo.")]
    public async Task<string> PromoteGroupAdminAsync(
        [Description("ID nhóm chat Zalo (GroupId)")] string groupId,
        [Description("User ID của thành viên được thăng cấp")] string memberUid,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        using ZaloWebClient client = new(session.Proxy);
        await client.PromoteGroupAdminAsync(session, groupId, memberUid, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, promoted_admin_uid = memberUid });
    }

    [McpServerTool(Name = "zalo_pin_group_message")]
    [Description("Ghim tin nhắn thông báo quan trọng lên đầu nhóm chat Zalo.")]
    public async Task<string> PinGroupMessageAsync(
        [Description("ID nhóm chat Zalo (GroupId)")] string groupId,
        [Description("ID của tin nhắn cần ghim (MsgId)")] string msgId,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        using ZaloWebClient client = new(session.Proxy);
        await client.PinGroupMessageAsync(session, groupId, msgId, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, pinned_msg_id = msgId });
    }
}
