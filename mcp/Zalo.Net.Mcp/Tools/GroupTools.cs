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

    [McpServerTool(Name = "zalo_get_online_status")]
    [Description("Check whether a Zalo user is currently online or get their last active timestamp.")]
    public async Task<string> GetUserOnlineStatusAsync(
        [Description("Target Zalo User ID")] string targetUserId,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ZaloSession session = this.ResolveSession(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;
        ZaloOnlineStatus status = await account.Client.GetUserLastOnlineAsync(session, targetUserId, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(status, ZaloMcpJsonContext.Default.ZaloOnlineStatus);
    }

    [McpServerTool(Name = "zalo_respond_friend_request")]
    [Description("Query friendship status ('status'), reject an incoming friend request ('reject'), or undo an outgoing friend request ('undo').")]
    public async Task<string> RespondFriendRequestAsync(
        [Description("Target Zalo User ID")] string targetUserId,
        [Description("Action: 'status', 'reject', or 'undo'")] string action = "status",
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ZaloSession session = this.ResolveSession(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;

        switch (action.Trim().ToLowerInvariant())
        {
            case "status":
                ZaloFriendshipStatus st = await account.Client.GetFriendRequestStatusAsync(session, targetUserId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(st, ZaloMcpJsonContext.Default.ZaloFriendshipStatus);
            case "reject":
                await account.Client.RejectFriendRequestAsync(session, targetUserId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "friend_request_rejected" });
            case "undo":
                await account.Client.UndoFriendRequestAsync(session, targetUserId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", target_user_id = targetUserId, action = "friend_request_undone" });
            default:
                return JsonSerializer.Serialize(new { error = "Invalid action. Supported actions: 'status', 'reject', 'undo'." });
        }
    }

    [McpServerTool(Name = "zalo_manage_conversation")]
    [Description("Manage conversation state: 'pin', 'unpin', 'mute', 'unmute', 'mark_unread', 'mark_read', 'hide', 'unhide', or 'delete_history'.")]
    public async Task<string> ManageConversationAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Action: 'pin', 'unpin', 'mute', 'unmute', 'mark_unread', 'mark_read', 'hide', 'unhide', 'delete_history'")] string action,
        [Description("Optional mute duration in seconds (-1 for until re-enabled) or lastMsgId for delete_history")] string? parameter = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ZaloSession session = this.ResolveSession(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        switch (action.Trim().ToLowerInvariant())
        {
            case "pin":
                await account.Client.SetPinnedConversationAsync(session, threadId, type, pin: true, ct).ConfigureAwait(false);
                break;
            case "unpin":
                await account.Client.SetPinnedConversationAsync(session, threadId, type, pin: false, ct).ConfigureAwait(false);
                break;
            case "mute":
                long dur = long.TryParse(parameter, out long parsedDur) ? parsedDur : -1;
                await account.Client.SetMuteConversationAsync(session, threadId, type, mute: true, durationSeconds: dur, ct: ct).ConfigureAwait(false);
                break;
            case "unmute":
                await account.Client.SetMuteConversationAsync(session, threadId, type, mute: false, durationSeconds: 0, ct: ct).ConfigureAwait(false);
                break;
            case "mark_unread":
                await account.Client.SetUnreadMarkAsync(session, threadId, type, unread: true, ct).ConfigureAwait(false);
                break;
            case "mark_read":
                await account.Client.SetUnreadMarkAsync(session, threadId, type, unread: false, ct).ConfigureAwait(false);
                break;
            case "hide":
                await account.Client.SetHiddenConversationAsync(session, threadId, type, hide: true, ct).ConfigureAwait(false);
                break;
            case "unhide":
                await account.Client.SetHiddenConversationAsync(session, threadId, type, hide: false, ct).ConfigureAwait(false);
                break;
            case "delete_history":
                await account.Client.DeleteConversationHistoryAsync(session, threadId, type, parameter, ct).ConfigureAwait(false);
                break;
            case "auto_delete":
                long ttl = long.TryParse(parameter, out long parsedTtl) ? parsedTtl : 0;
                await account.Client.UpdateAutoDeleteChatAsync(session, threadId, type == ZaloThreadType.Group, ttl, ct).ConfigureAwait(false);
                break;
            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'." });
        }

        return JsonSerializer.Serialize(new { status = "success", thread_id = threadId, thread_type = type.ToString(), action });
    }

    [McpServerTool(Name = "zalo_manage_group_link")]
    [Description("Manage group invite links: 'detail' (get current link), 'enable' (generate/enable link), 'disable' (revoke link), or 'inspect' (preview group info from link).")]
    public async Task<string> ManageGroupLinkAsync(
        [Description("Action: 'detail', 'enable', 'disable', or 'inspect'")] string action,
        [Description("Group ID (required for detail/enable/disable) or Invite Link URL (required for inspect)")] string target,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        ZaloSession session = this.ResolveSession(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;

        switch (action.Trim().ToLowerInvariant())
        {
            case "detail":
                ZaloGroupLinkDetail detail = await account.Client.GetGroupLinkDetailAsync(session, target, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(detail, ZaloMcpJsonContext.Default.ZaloGroupLinkDetail);
            case "enable":
                ZaloGroupLinkDetail enabled = await account.Client.EnableGroupLinkAsync(session, target, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(enabled, ZaloMcpJsonContext.Default.ZaloGroupLinkDetail);
            case "disable":
                await account.Client.DisableGroupLinkAsync(session, target, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", group_id = target, action = "link_disabled" });
            case "inspect":
                ZaloGroupInfo info = await account.Client.GetGroupLinkInfoAsync(session, target, 1, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(info, ZaloMcpJsonContext.Default.ZaloGroupInfo);
            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'. Use 'detail', 'enable', 'disable', or 'inspect'." });
        }
    }

    [McpServerTool(Name = "zalo_manage_group_admin")]
    [Description("Advanced group administration: 'disperse' (disband group), 'change_owner', 'remove_deputy', 'list_pending', 'list_blocked', 'block_members', 'unblock_members', or 'update_settings'.")]
    public async Task<string> ManageGroupAdminAsync(
        [Description("Group chat ID (GroupId)")] string groupId,
        [Description("Action: 'disperse', 'change_owner', 'remove_deputy', 'list_pending', 'list_blocked', 'block_members', 'unblock_members', 'update_settings'")] string action,
        [Description("Comma-separated member UIDs (for change_owner/remove_deputy/block_members/unblock_members) or comma-separated enabled flags for update_settings (joinApproval,lockSendMsg,enableMsgHistory,signAdminMsg,blockName)")] string? parameter = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(action);

        ZaloSession session = this.ResolveSession(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;
        string[] items = string.IsNullOrWhiteSpace(parameter)
            ? []
            : parameter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        switch (action.Trim().ToLowerInvariant())
        {
            case "disperse":
                await account.Client.DisperseGroupAsync(session, groupId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", group_id = groupId, action = "dispersed" });
            case "change_owner":
                if (items.Length == 0)
                {
                    return JsonSerializer.Serialize(new { error = "New owner UID is required in 'parameter'." });
                }
                await account.Client.ChangeGroupOwnerAsync(session, groupId, items[0], ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", group_id = groupId, new_owner_id = items[0] });
            case "remove_deputy":
                await account.Client.RemoveGroupDeputyAsync(session, groupId, items, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", group_id = groupId, removed_deputies = items.Length });
            case "list_pending":
                IReadOnlyList<string> pending = await account.Client.GetPendingGroupMembersAsync(session, groupId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(pending, ZaloMcpJsonContext.Default.IReadOnlyListString);
            case "list_blocked":
                IReadOnlyList<string> blocked = await account.Client.GetGroupBlockedMembersAsync(session, groupId, 1, 50, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(blocked, ZaloMcpJsonContext.Default.IReadOnlyListString);
            case "block_members":
                await account.Client.AddGroupBlockedMemberAsync(session, groupId, items, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", group_id = groupId, blocked_count = items.Length });
            case "unblock_members":
                await account.Client.RemoveGroupBlockedMemberAsync(session, groupId, items, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", group_id = groupId, unblocked_count = items.Length });
            case "update_settings":
                HashSet<string> flags = new(items, StringComparer.OrdinalIgnoreCase);
                ZaloGroupSettingsOptions opts = new(
                    BlockName: flags.Contains("blockName"),
                    SignAdminMsg: flags.Contains("signAdminMsg"),
                    SetTopicOnly: flags.Contains("setTopicOnly"),
                    EnableMsgHistory: !flags.Contains("disableMsgHistory"),
                    JoinApproval: flags.Contains("joinApproval"),
                    LockCreatePost: flags.Contains("lockCreatePost"),
                    LockCreatePoll: flags.Contains("lockCreatePoll"),
                    LockSendMsg: flags.Contains("lockSendMsg"),
                    LockViewMember: flags.Contains("lockViewMember"));
                await account.Client.UpdateGroupSettingsAsync(session, groupId, opts, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", group_id = groupId, action = "settings_updated" });
            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'." });
        }
    }

    [McpServerTool(Name = "zalo_manage_group_poll")]
    [Description("Create or interact with group polls: 'create', 'detail', 'vote', 'lock', or 'share'.")]
    public async Task<string> ManageGroupPollAsync(
        [Description("Action: 'create', 'detail', 'vote', 'lock', or 'share'")] string action,
        [Description("Group ID (for 'create') or Poll ID (for 'detail', 'vote', 'lock', 'share')")] string targetId,
        [Description("Poll question (for 'create')")] string? question = null,
        [Description("Comma-separated poll option texts (for 'create') or option IDs (for 'vote')")] string? optionsCsv = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        ZaloSession session = this.ResolveSession(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;

        switch (action.Trim().ToLowerInvariant())
        {
            case "create":
                if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(optionsCsv))
                {
                    return JsonSerializer.Serialize(new { error = "Both 'question' and 'optionsCsv' are required to create a poll." });
                }
                string[] optTexts = optionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                ZaloPollDetail created = await account.Client.CreatePollAsync(
                    session,
                    targetId,
                    new ZaloPollCreateOptions(question, optTexts),
                    ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(created, ZaloMcpJsonContext.Default.ZaloPollDetail);

            case "detail":
                long detailId = long.Parse(targetId, System.Globalization.CultureInfo.InvariantCulture);
                ZaloPollDetail detail = await account.Client.GetPollDetailAsync(session, detailId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(detail, ZaloMcpJsonContext.Default.ZaloPollDetail);

            case "vote":
                long votePollId = long.Parse(targetId, System.Globalization.CultureInfo.InvariantCulture);
                string[] rawIds = string.IsNullOrWhiteSpace(optionsCsv)
                    ? []
                    : optionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                List<long> optIds = [];
                foreach (string r in rawIds)
                {
                    if (long.TryParse(r, System.Globalization.CultureInfo.InvariantCulture, out long parsedId))
                    {
                        optIds.Add(parsedId);
                    }
                }
                ZaloPollDetail voted = await account.Client.VotePollAsync(session, votePollId, [.. optIds], ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(voted, ZaloMcpJsonContext.Default.ZaloPollDetail);

            case "lock":
                long lockPollId = long.Parse(targetId, System.Globalization.CultureInfo.InvariantCulture);
                await account.Client.LockPollAsync(session, lockPollId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", poll_id = lockPollId, action = "locked" });

            case "share":
                long sharePollId = long.Parse(targetId, System.Globalization.CultureInfo.InvariantCulture);
                await account.Client.SharePollAsync(session, sharePollId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", poll_id = sharePollId, action = "shared" });

            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'." });
        }
    }

    [McpServerTool(Name = "zalo_manage_group_board")]
    [Description("Manage group board notes and announcements: 'list', 'create_note', or 'edit_note'.")]
    public async Task<string> ManageGroupBoardAsync(
        [Description("Group chat ID (GroupId)")] string groupId,
        [Description("Action: 'list', 'create_note', or 'edit_note'")] string action,
        [Description("Note content/title (required for create_note/edit_note)")] string? title = null,
        [Description("Existing topic/note ID (required for edit_note)")] string? topicId = null,
        [Description("Whether to pin the note to the top of the group chat")] bool pinToTop = false,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(action);

        ZaloSession session = this.ResolveSession(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;

        switch (action.Trim().ToLowerInvariant())
        {
            case "list":
                IReadOnlyList<ZaloBoardItem> items = await account.Client.GetGroupBoardListAsync(session, groupId, 1, 20, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(items, ZaloMcpJsonContext.Default.IReadOnlyListZaloBoardItem);

            case "create_note":
                if (string.IsNullOrWhiteSpace(title))
                {
                    return JsonSerializer.Serialize(new { error = "'title' is required for 'create_note'." });
                }
                ZaloBoardItem created = await account.Client.CreateGroupNoteAsync(session, groupId, title, pinToTop, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(created, ZaloMcpJsonContext.Default.ZaloBoardItem);

            case "edit_note":
                if (string.IsNullOrWhiteSpace(topicId) || string.IsNullOrWhiteSpace(title))
                {
                    return JsonSerializer.Serialize(new { error = "Both 'topicId' and 'title' are required for 'edit_note'." });
                }
                ZaloBoardItem edited = await account.Client.EditGroupNoteAsync(session, groupId, topicId, title, pinToTop, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(edited, ZaloMcpJsonContext.Default.ZaloBoardItem);

            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'." });
        }
    }

    [McpServerTool(Name = "zalo_manage_friends_extended")]
    [Description("Extended friend and alias management: 'remove_friend', 'list_sent_requests', 'list_recommendations', 'list_aliases', 'remove_alias', 'block_feed', or 'unblock_feed'.")]
    public async Task<string> ManageFriendsExtendedAsync(
        [Description("Action: 'remove_friend', 'list_sent_requests', 'list_recommendations', 'list_aliases', 'remove_alias', 'block_feed', or 'unblock_feed'")] string action,
        [Description("Target User ID (required for remove_friend, remove_alias, block_feed, unblock_feed)")] string? targetUserId = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ZaloSession session = this.ResolveSession(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;

        switch (action.Trim().ToLowerInvariant())
        {
            case "remove_friend":
                if (string.IsNullOrWhiteSpace(targetUserId))
                {
                    return JsonSerializer.Serialize(new { error = "'targetUserId' is required." });
                }
                await account.Client.RemoveFriendAsync(session, targetUserId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", user_id = targetUserId, action = "friend_removed" });

            case "list_sent_requests":
                IReadOnlyList<ZaloFriendRequestItem> sent = await account.Client.GetSentFriendRequestsAsync(session, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(sent, ZaloMcpJsonContext.Default.IReadOnlyListZaloFriendRequestItem);

            case "list_recommendations":
                IReadOnlyList<ZaloFriendRequestItem> recs = await account.Client.GetFriendRecommendationsAsync(session, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(recs, ZaloMcpJsonContext.Default.IReadOnlyListZaloFriendRequestItem);

            case "list_aliases":
                IReadOnlyList<ZaloAliasEntry> aliases = await account.Client.GetAliasListAsync(session, 1, 100, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(aliases, ZaloMcpJsonContext.Default.IReadOnlyListZaloAliasEntry);

            case "remove_alias":
                if (string.IsNullOrWhiteSpace(targetUserId))
                {
                    return JsonSerializer.Serialize(new { error = "'targetUserId' is required." });
                }
                await account.Client.RemoveFriendAliasAsync(session, targetUserId, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", user_id = targetUserId, action = "alias_removed" });

            case "block_feed":
                if (string.IsNullOrWhiteSpace(targetUserId))
                {
                    return JsonSerializer.Serialize(new { error = "'targetUserId' is required." });
                }
                await account.Client.BlockViewFeedAsync(session, targetUserId, isBlocked: true, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", user_id = targetUserId, action = "feed_blocked" });

            case "unblock_feed":
                if (string.IsNullOrWhiteSpace(targetUserId))
                {
                    return JsonSerializer.Serialize(new { error = "'targetUserId' is required." });
                }
                await account.Client.BlockViewFeedAsync(session, targetUserId, isBlocked: false, ct).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { status = "success", user_id = targetUserId, action = "feed_unblocked" });

            default:
                return JsonSerializer.Serialize(new { error = $"Unsupported action '{action}'." });
        }
    }
}
