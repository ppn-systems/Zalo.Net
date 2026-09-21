// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zalo.Net.Contracts;

namespace Zalo.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Zalo Contact, Friend Request, and Group management operations.
/// Supports targeting a specific account via optional accountUid.
/// </summary>
[McpServerToolType]
public sealed class GroupTools(ZaloSessionManager sessionManager)
{
    private readonly ZaloSessionManager _sessionManager = sessionManager;

    private ZaloSession ResolveSession(string? accountUid)
    {
        this._sessionManager.EnsureAuthenticated(accountUid);
        return this._sessionManager.GetAccount(accountUid)!.Session;
    }

    [McpServerTool(Name = "zalo_find_user")]
    [Description("Find Zalo user profile by phone number.")]
    public async Task<string> FindUserByPhoneAsync(
        [Description("Phone number to search (e.g. '0912345678' or '+84912345678')")] string phoneNumber,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        ZaloUserProfile profile = await ZaloWebClient.FindUserByPhoneAsync(session, phoneNumber, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(profile, ZaloMcpJsonContext.Default.ZaloUserProfile);
    }

    [McpServerTool(Name = "zalo_list_contacts")]
    [Description("Get friends and contacts list for the current Zalo account.")]
    public async Task<string> ListContactsAsync(
        [Description("Maximum number of contacts to return (default 1000)")] int count = 1000,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        IReadOnlyList<ZaloFriendInfo> friends = await ZaloWebClient.GetAllFriendsAsync(session, count: count, ct: ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(friends, ZaloMcpJsonContext.Default.IReadOnlyListZaloFriendInfo);
    }

    [McpServerTool(Name = "zalo_send_friend_request")]
    [Description("Send a friend request to a Zalo user by User ID.")]
    public async Task<string> SendFriendRequestAsync(
        [Description("User ID of the recipient")] string targetUserId,
        [Description("Optional friend request message")] string? message = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        await ZaloWebClient.SendFriendRequestAsync(session, targetUserId, message, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "friend_request_sent", from_uid = session.Uid });
    }

    [McpServerTool(Name = "zalo_accept_friend_request")]
    [Description("Accept an incoming friend request from another user.")]
    public async Task<string> AcceptFriendRequestAsync(
        [Description("User ID of the friend request sender")] string targetUserId,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        await ZaloWebClient.AcceptFriendRequestAsync(session, targetUserId, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "friend_request_accepted" });
    }

    [McpServerTool(Name = "zalo_block_user")]
    [Description("Block a Zalo user.")]
    public async Task<string> BlockUserAsync(
        [Description("User ID to block")] string targetUserId,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        await ZaloWebClient.BlockUserAsync(session, targetUserId, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "user_blocked" });
    }

    [McpServerTool(Name = "zalo_unblock_user")]
    [Description("Unblock a Zalo user.")]
    public async Task<string> UnblockUserAsync(
        [Description("User ID to unblock")] string targetUserId,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        await ZaloWebClient.UnblockUserAsync(session, targetUserId, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "user_unblocked" });
    }

    [McpServerTool(Name = "zalo_change_friend_alias")]
    [Description("Change the nickname (alias) of a contact in your address book.")]
    public async Task<string> ChangeFriendAliasAsync(
        [Description("User ID of the friend")] string targetUserId,
        [Description("New alias/nickname to assign")] string alias,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        await ZaloWebClient.ChangeFriendAliasAsync(session, targetUserId, alias, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, alias });
    }

    [McpServerTool(Name = "zalo_list_groups")]
    [Description("List all Zalo group chats that the current account belongs to.")]
    public async Task<string> ListGroupsAsync(
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        IReadOnlyList<ZaloGroupInfo> groups = await ZaloWebClient.GetAllGroupsAsync(session, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(groups, ZaloMcpJsonContext.Default.IReadOnlyListZaloGroupInfo);
    }

    [McpServerTool(Name = "zalo_create_group")]
    [Description("Create a new Zalo group chat with specified member contacts.")]
    public async Task<string> CreateGroupAsync(
        [Description("Name of the new group chat")] string groupName,
        [Description("Comma-separated list of member User IDs")] string memberIdsCsv,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(memberIdsCsv);

        ZaloSession session = this.ResolveSession(accountUid);
        string[] memberIds = memberIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ZaloGroupCreateResult result = await ZaloWebClient.CreateGroupAsync(session, groupName, memberIds, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(result, ZaloMcpJsonContext.Default.ZaloGroupCreateResult);
    }

    [McpServerTool(Name = "zalo_manage_group")]
    [Description("Manage group chat: Add members ('add'), Remove members ('remove'), Rename group ('rename'), or Leave ('leave').")]
    public async Task<string> ManageGroupAsync(
        [Description("Group chat ID (GroupId)")] string groupId,
        [Description("Action: 'add', 'remove', 'rename', 'leave'")] string action,
        [Description("Comma-separated member User IDs (for add/remove) or new group name (for rename)")] string value,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(value);

        ZaloSession session = this.ResolveSession(accountUid);

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
                return JsonSerializer.Serialize(new { error = "Invalid action. Supported actions: 'add', 'remove', 'rename', 'leave'." });
        }
    }

    [McpServerTool(Name = "zalo_join_group_via_link")]
    [Description("Join a Zalo group chat via invite link URL (e.g. https://zalo.me/g/XXXXXXXXX).")]
    public async Task<string> JoinGroupViaLinkAsync(
        [Description("Group invitation link URL")] string inviteUrl,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        using ZaloWebClient client = new(session.Proxy);
        await client.JoinGroupViaLinkAsync(session, inviteUrl, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", invite_url = inviteUrl, message = "Group join request submitted." });
    }

    [McpServerTool(Name = "zalo_review_join_requests")]
    [Description("Approve or decline pending user join requests for a group chat.")]
    public async Task<string> ReviewJoinRequestsAsync(
        [Description("Group chat ID (GroupId)")] string groupId,
        [Description("Comma-separated list of applicant User IDs")] string memberUidsCsv,
        [Description("True to approve, false to decline")] bool approve,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(memberUidsCsv);

        ZaloSession session = this.ResolveSession(accountUid);
        string[] uids = memberUidsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using ZaloWebClient client = new(session.Proxy);
        await client.ReviewJoinRequestsAsync(session, groupId, uids, approve, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, approved = approve, member_count = uids.Length });
    }

    [McpServerTool(Name = "zalo_leave_group_silently")]
    [Description("Leave a Zalo group chat silently without broadcasting notification.")]
    public async Task<string> LeaveGroupSilentlyAsync(
        [Description("Group chat ID (GroupId)")] string groupId,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        using ZaloWebClient client = new(session.Proxy);
        await client.LeaveGroupSilentlyAsync(session, groupId, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, action = "left_group_silently" });
    }

    [McpServerTool(Name = "zalo_kick_group_member")]
    [Description("Kick a member from a Zalo group chat.")]
    public async Task<string> KickGroupMemberAsync(
        [Description("Group chat ID (GroupId)")] string groupId,
        [Description("User ID of the member to remove")] string memberUid,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        using ZaloWebClient client = new(session.Proxy);
        await client.KickGroupMemberAsync(session, groupId, memberUid, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, kicked_member_uid = memberUid });
    }

    [McpServerTool(Name = "zalo_promote_group_admin")]
    [Description("Promote a group member to admin / deputy admin in a Zalo group chat.")]
    public async Task<string> PromoteGroupAdminAsync(
        [Description("Group chat ID (GroupId)")] string groupId,
        [Description("User ID of the member to promote")] string memberUid,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        using ZaloWebClient client = new(session.Proxy);
        await client.PromoteGroupAdminAsync(session, groupId, memberUid, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, promoted_admin_uid = memberUid });
    }

    [McpServerTool(Name = "zalo_pin_group_message")]
    [Description("Pin an important announcement message at the top of a Zalo group chat.")]
    public async Task<string> PinGroupMessageAsync(
        [Description("Group chat ID (GroupId)")] string groupId,
        [Description("Message ID to pin (MsgId)")] string msgId,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        using ZaloWebClient client = new(session.Proxy);
        await client.PinGroupMessageAsync(session, groupId, msgId, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", group_id = groupId, pinned_msg_id = msgId });
    }

    /// <summary>Overload for calling without accountUid.</summary>
    public Task<string> ListGroupsAsync(CancellationToken ct) => this.ListGroupsAsync(accountUid: null, ct);
}
