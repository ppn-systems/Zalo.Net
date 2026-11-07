// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Zalo.Net.Contracts;

/// <summary>
/// Abstraction for Zalo Web client API operations, authentication flows, and real-time messaging.
/// Enables dependency injection and mocking for unit tests.
/// </summary>
public interface IZaloClient : IDisposable
{
    /// <summary>Gets the configured proxy instance for this client, if assigned.</summary>
    IWebProxy? Proxy { get; }

    /// <summary>Occurs when an inbound WebSocket message is received.</summary>
    event EventHandler<ZaloMessageEvent>? MessageReceived;

    /// <summary>Occurs when the session connection status changes.</summary>
    event EventHandler<ZaloSessionStatusChanged>? StatusChanged;

    /// <summary>Starts QR login flow.</summary>
    Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct = default);

    /// <summary>Polls for QR login status.</summary>
    Task<ZaloLoginState> PollQrStatusAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Consumes and retrieves session material after login confirmation.</summary>
    ZaloSessionMaterial? ConsumePendingMaterial(Guid sessionId);

    /// <summary>Starts real-time WebSocket listener using an active session.</summary>
    Task StartListenerAsync(ZaloSession session, CancellationToken ct = default);

    /// <summary>Runs WebSocket listener with automatic exponential backoff reconnects.</summary>
    Task RunWithReconnectAsync(ZaloSessionMaterial material, IWebProxy? proxy = null, CancellationToken ct = default);

    /// <summary>Sends a bank account card for quick transfer via Zalo API.</summary>
    Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloBankCard bankCard, CancellationToken ct = default);

    /// <summary>Sends a bank account card for quick transfer via Zalo API.</summary>
    Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string binBank, string accountNumber, string accountName, CancellationToken ct = default);

    /// <summary>Sends a user contact card recommendation via Zalo API.</summary>
    Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloContactCard contactCard, CancellationToken ct = default);

    /// <summary>Sends a user contact card recommendation via Zalo API.</summary>
    Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string userId, string? phoneNumber = null, string? qrCodeUrl = null, CancellationToken ct = default);

    /// <summary>Joins a group via invite link (e.g. https://zalo.me/g/XXXXXXXXX).</summary>
    Task JoinGroupViaLinkAsync(ZaloSession session, string inviteUrl, CancellationToken ct = default);

    /// <summary>Reviews pending group join requests (approves or rejects).</summary>
    Task ReviewJoinRequestsAsync(ZaloSession session, string groupId, string[] memberUids, bool approve, CancellationToken ct = default);

    /// <summary>Leaves a group silently without broadcasting a leave message.</summary>
    Task LeaveGroupSilentlyAsync(ZaloSession session, string groupId, CancellationToken ct = default);

    /// <summary>Removes/Kicks a member from a group chat.</summary>
    Task KickGroupMemberAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default);

    /// <summary>Promotes a group member to co-admin / admin.</summary>
    Task PromoteGroupAdminAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default);

    /// <summary>Pins an important announcement message in a group chat.</summary>
    Task PinGroupMessageAsync(ZaloSession session, string groupId, string msgId, CancellationToken ct = default);

    /// <summary>Sends an image payload to a user or group thread.</summary>
    Task SendImageAsync(ZaloSession session, string threadId, ZaloThreadType threadType, byte[] imageBytes, string fileName, string? caption = null, CancellationToken ct = default);

    /// <summary>Gets or sets the client-side privacy guard options (block typing, delivered, seen).</summary>
    ZaloPrivacyOptions PrivacyOptions { get => new(); set { } }

    /// <summary>Authenticates using phone number and password via LoginByPasswv2.</summary>
    Task<ZaloSessionMaterial> LoginByPasswordAsync(string phone, string password, string isoCountryCode = "VN", string? imei = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Parses rich link metadata (title, description, thumbnail) for a URL.</summary>
    Task<ZaloLinkMetadata> ParseLinkAsync(ZaloSession session, string url, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Sends a rich link card message to a user or group thread.</summary>
    Task<ZaloSendResult> SendLinkAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string url, string? message = null, ZaloLinkMetadata? metadata = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Deletes a message for the current user only (delete-for-me).</summary>
    Task DeleteMessageForMeAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string msgId, string cliMsgId, string ownerId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Forwards a text message or sticker to a target thread.</summary>
    Task<ZaloSendResult> ForwardMessageAsync(ZaloSession session, string targetThreadId, ZaloThreadType targetThreadType, string text, int? stickerId = null, int? stickerCateId = null, int stickerType = 7, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Sends a real-time typing indicator to a user or group thread (respects PrivacyOptions.BlockTyping).</summary>
    Task SendTypingAsync(ZaloSession session, string threadId, ZaloThreadType threadType, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Sends a seen/read receipt v2 to a user or group thread (respects PrivacyOptions.BlockSeen).</summary>
    Task SendSeenV2Async(ZaloSession session, string threadId, ZaloThreadType threadType, string lastMsgId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Fetches recent conversation previews and last messages from server.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloHistoryMessage>> GetLastMessagesAsync(ZaloSession session, string[]? threadIds = null, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloHistoryMessage>>([]);

    /// <summary>Fetches group message history from server.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloHistoryMessage>> GetGroupHistoryAsync(ZaloSession session, string groupId, int count = 50, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloHistoryMessage>>([]);

    /// <summary>Fetches cloud message history for a group or Cloud của tôi.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloHistoryMessage>> GetCloudMessagesAsync(ZaloSession session, string convId, string globalMsgId = "0", bool isOld = false, int count = 50, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloHistoryMessage>>([]);

    /// <summary>Fetches official contextual sticker trigger keywords.</summary>
    Task<System.Collections.Generic.IReadOnlyList<string>> GetStickerSuggestKeywordsAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>([]);

    /// <summary>Searches Zalo stickers by Vietnamese, English, or slang keyword across suggest and search endpoints.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloStickerItem>> SearchStickersAsync(ZaloSession session, string keyword, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloStickerItem>>([]);

    /// <summary>Fetches the Zalo Sticker Store catalog packs.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloStickerPack>> GetStickerStorePacksAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloStickerPack>>([]);

    /// <summary>Fetches all sticker items in a specific sticker category/pack.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloStickerItem>> GetStickerPackItemsAsync(ZaloSession session, int cateId, string packName = "", CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloStickerItem>>([]);

    /// <summary>Gets the list of pinned conversation thread IDs.</summary>
    Task<System.Collections.Generic.IReadOnlyList<string>> GetPinnedConversationsAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>([]);

    /// <summary>Pins or unpins a conversation thread.</summary>
    Task SetPinnedConversationAsync(ZaloSession session, string threadId, ZaloThreadType threadType, bool pin, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Gets all currently muted conversations from the server.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloMutedConversation>> GetMutedConversationsAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloMutedConversation>>([]);

    /// <summary>Mutes or unmutes notifications for a conversation thread.</summary>
    Task SetMuteConversationAsync(ZaloSession session, string threadId, ZaloThreadType threadType, bool mute, long durationSeconds = -1, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Gets manually marked unread conversations.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloUnreadMark>> GetUnreadMarksAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloUnreadMark>>([]);

    /// <summary>Marks a conversation as unread or removes the unread mark.</summary>
    Task SetUnreadMarkAsync(ZaloSession session, string threadId, ZaloThreadType threadType, bool unread, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Hides or unhides a conversation thread.</summary>
    Task SetHiddenConversationAsync(ZaloSession session, string threadId, ZaloThreadType threadType, bool hide, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Deletes conversation history for the current user.</summary>
    Task DeleteConversationHistoryAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string? lastCliMsgId = null, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Queries the online status and last active time of a user.</summary>
    Task<ZaloOnlineStatus> GetUserLastOnlineAsync(ZaloSession session, string userId, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>Checks the friendship/request status with a target user.</summary>
    Task<ZaloFriendshipStatus> GetFriendRequestStatusAsync(ZaloSession session, string targetUserId, CancellationToken ct = default)
        => Task.FromResult(ZaloFriendshipStatus.Unknown);

    /// <summary>Rejects an incoming friend request.</summary>
    Task RejectFriendRequestAsync(ZaloSession session, string targetUserId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Cancels/Undoes an outgoing friend request.</summary>
    Task UndoFriendRequestAsync(ZaloSession session, string targetUserId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Fetches mini profiles (display name, avatar) for a batch of user IDs.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloUserProfile>> GetMiniProfilesAsync(ZaloSession session, string[] userIds, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloUserProfile>>([]);

    // ─── Group 1: Advanced Group Admin & Invite Links ────────────────────────

    /// <summary>Disbands/disperses a group owned by the authenticated user.</summary>
    Task DisperseGroupAsync(ZaloSession session, string groupId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Transfers group ownership (golden key) to another member.</summary>
    Task ChangeGroupOwnerAsync(ZaloSession session, string groupId, string newOwnerId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Gets the current invite link details for a group.</summary>
    Task<ZaloGroupLinkDetail> GetGroupLinkDetailAsync(ZaloSession session, string groupId, CancellationToken ct = default)
        => Task.FromResult(new ZaloGroupLinkDetail("", 0, false));

    /// <summary>Enables or regenerates a new invite link for a group.</summary>
    Task<ZaloGroupLinkDetail> EnableGroupLinkAsync(ZaloSession session, string groupId, CancellationToken ct = default)
        => Task.FromResult(new ZaloGroupLinkDetail("", 0, true));

    /// <summary>Disables the invite link for a group.</summary>
    Task DisableGroupLinkAsync(ZaloSession session, string groupId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Inspects group information from a public invite link URL without joining.</summary>
    Task<ZaloGroupInfo> GetGroupLinkInfoAsync(ZaloSession session, string groupLink, int memberPage = 1, CancellationToken ct = default)
        => Task.FromResult(new ZaloGroupInfo("", "", null, 0, ""));

    /// <summary>Updates group moderation and permission settings.</summary>
    Task UpdateGroupSettingsAsync(ZaloSession session, string groupId, ZaloGroupSettingsOptions options, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Gets the list of user IDs waiting for join approval in a group.</summary>
    Task<System.Collections.Generic.IReadOnlyList<string>> GetPendingGroupMembersAsync(ZaloSession session, string groupId, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>([]);

    /// <summary>Gets the list of blocked member user IDs in a group.</summary>
    Task<System.Collections.Generic.IReadOnlyList<string>> GetGroupBlockedMembersAsync(ZaloSession session, string groupId, int page = 1, int count = 50, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>([]);

    /// <summary>Blocks one or more members from a group.</summary>
    Task AddGroupBlockedMemberAsync(ZaloSession session, string groupId, string[] memberIds, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Unblocks one or more members from a group.</summary>
    Task RemoveGroupBlockedMemberAsync(ZaloSession session, string groupId, string[] memberIds, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Removes deputy admin (silver key) privileges from one or more group members.</summary>
    Task RemoveGroupDeputyAsync(ZaloSession session, string groupId, string[] memberIds, CancellationToken ct = default)
        => Task.CompletedTask;

    // ─── Group 2: Group Polls, Board Notes & Official Zalo Reminders ─────────

    /// <summary>Creates a new poll in a group chat.</summary>
    Task<ZaloPollDetail> CreatePollAsync(ZaloSession session, string groupId, ZaloPollCreateOptions options, CancellationToken ct = default)
        => Task.FromResult(new ZaloPollDetail(0, "", options?.Question ?? "", false, true, true, false, false, 0, 0, 0, []));

    /// <summary>Gets detailed options and votes for a group poll.</summary>
    Task<ZaloPollDetail> GetPollDetailAsync(ZaloSession session, long pollId, CancellationToken ct = default)
        => Task.FromResult(new ZaloPollDetail(pollId, "", "", false, true, true, false, false, 0, 0, 0, []));

    /// <summary>Casts votes on one or more option IDs in a group poll.</summary>
    Task<ZaloPollDetail> VotePollAsync(ZaloSession session, long pollId, long[] optionIds, CancellationToken ct = default)
        => Task.FromResult(new ZaloPollDetail(pollId, "", "", false, true, true, false, false, 0, 0, 0, []));

    /// <summary>Locks/ends an active group poll.</summary>
    Task LockPollAsync(ZaloSession session, long pollId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Re-shares a group poll to the group chat stream.</summary>
    Task SharePollAsync(ZaloSession session, long pollId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Creates a group board note (optionally pinned to the top of the group).</summary>
    Task<ZaloBoardItem> CreateGroupNoteAsync(ZaloSession session, string groupId, string title, bool pinToTop = false, CancellationToken ct = default)
        => Task.FromResult(new ZaloBoardItem("", 1, title));

    /// <summary>Edits an existing group board note.</summary>
    Task<ZaloBoardItem> EditGroupNoteAsync(ZaloSession session, string groupId, string topicId, string title, bool pinToTop = false, CancellationToken ct = default)
        => Task.FromResult(new ZaloBoardItem(topicId, 1, title));

    /// <summary>Lists notes, pinned topics, and polls on a group board.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloBoardItem>> GetGroupBoardListAsync(ZaloSession session, string groupId, int page = 1, int count = 20, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloBoardItem>>([]);

    /// <summary>Creates an official Zalo reminder in a 1-on-1 or group conversation.</summary>
    Task<ZaloReminderItem> CreateReminderAsync(ZaloSession session, string threadId, bool isGroup, ZaloReminderOptions options, CancellationToken ct = default)
        => Task.FromResult(new ZaloReminderItem("", threadId, isGroup, options?.Title ?? ""));

    /// <summary>Edits an existing official Zalo reminder in a 1-on-1 or group conversation.</summary>
    Task<ZaloReminderItem> EditReminderAsync(ZaloSession session, string threadId, bool isGroup, string reminderId, ZaloReminderOptions options, CancellationToken ct = default)
        => Task.FromResult(new ZaloReminderItem(reminderId, threadId, isGroup, options?.Title ?? ""));

    /// <summary>Removes an official Zalo reminder from a 1-on-1 or group conversation.</summary>
    Task RemoveReminderAsync(ZaloSession session, string threadId, bool isGroup, string reminderId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Gets details of a group reminder by its ID.</summary>
    Task<ZaloReminderItem> GetReminderAsync(ZaloSession session, string reminderId, CancellationToken ct = default)
        => Task.FromResult(new ZaloReminderItem(reminderId, "", true, ""));

    /// <summary>Lists official Zalo reminders in a 1-on-1 or group conversation.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloReminderItem>> GetListReminderAsync(ZaloSession session, string threadId, bool isGroup, int page = 1, int count = 20, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloReminderItem>>([]);

    // ─── Group 3: Extended Friend & Alias Management ─────────────────────────

    /// <summary>Unfriends / removes a user from the authenticated user's friend list.</summary>
    Task RemoveFriendAsync(ZaloSession session, string friendId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Gets the list of outgoing friend requests sent by the authenticated user.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloFriendRequestItem>> GetSentFriendRequestsAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloFriendRequestItem>>([]);

    /// <summary>Gets the list of incoming friend requests and friend recommendations.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloFriendRequestItem>> GetFriendRecommendationsAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloFriendRequestItem>>([]);

    /// <summary>Gets all friend aliases configured on the account.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloAliasEntry>> GetAliasListAsync(ZaloSession session, int page = 1, int count = 100, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloAliasEntry>>([]);

    /// <summary>Removes the custom alias assigned to a friend.</summary>
    Task RemoveFriendAliasAsync(ZaloSession session, string friendId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Blocks or unblocks a friend from viewing the authenticated user's timeline feed.</summary>
    Task BlockViewFeedAsync(ZaloSession session, string userId, bool isBlocked, CancellationToken ct = default)
        => Task.CompletedTask;

    // ─── Group 4: Conversation Labels, Quick Messages & Auto-Delete Chat ─────

    /// <summary>Gets all conversation CRM labels configured on the account.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloLabelItem>> GetLabelsAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloLabelItem>>([]);

    /// <summary>Updates conversation CRM labels and their assigned conversation IDs.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloLabelItem>> UpdateLabelsAsync(ZaloSession session, System.Collections.Generic.IReadOnlyList<ZaloLabelItem> labels, int version = 0, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloLabelItem>>(labels);

    /// <summary>Gets all quick message templates configured on the account.</summary>
    Task<System.Collections.Generic.IReadOnlyList<ZaloQuickMessage>> GetQuickMessagesAsync(ZaloSession session, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<ZaloQuickMessage>>([]);

    /// <summary>Adds a new quick message shortcut template.</summary>
    Task<ZaloQuickMessage> AddQuickMessageAsync(ZaloSession session, string keyword, string title, CancellationToken ct = default)
        => Task.FromResult(new ZaloQuickMessage(0, keyword, title));

    /// <summary>Updates an existing quick message shortcut template by ID.</summary>
    Task<ZaloQuickMessage> UpdateQuickMessageAsync(ZaloSession session, int itemId, string keyword, string title, CancellationToken ct = default)
        => Task.FromResult(new ZaloQuickMessage(itemId, keyword, title));

    /// <summary>Removes one or more quick message templates by ID.</summary>
    Task RemoveQuickMessageAsync(ZaloSession session, int[] itemIds, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Configures disappearing messages (auto-delete TTL in milliseconds: 0, 86400000, 604800000, 1209600000) for a conversation.</summary>
    Task UpdateAutoDeleteChatAsync(ZaloSession session, string threadId, bool isGroup, long ttlMs, CancellationToken ct = default)
        => Task.CompletedTask;

    // ─── Group 5: Account Profile, Settings & Hidden Conversation PIN ────────

    /// <summary>Updates the authenticated account's display name, date of birth, gender, and optional business profile.</summary>
    Task UpdateProfileAsync(ZaloSession session, ZaloProfileUpdateOptions options, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Updates an account privacy/feature setting key (e.g. show_online_status, display_seen_status, view_birthday, receive_message).</summary>
    Task UpdateSettingsAsync(ZaloSession session, string settingKey, int value, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Pings active status or marks the account as inactive on Zalo Web.</summary>
    Task UpdateActiveStatusAsync(ZaloSession session, bool isActive, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Updates the 4-digit PIN used to lock/unlock hidden conversations.</summary>
    Task UpdateHiddenConversPinAsync(ZaloSession session, string fourDigitPin, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Resets the hidden conversations PIN.</summary>
    Task ResetHiddenConversPinAsync(ZaloSession session, CancellationToken ct = default)
        => Task.CompletedTask;
}
