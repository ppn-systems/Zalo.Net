// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Zalo.Net.Contracts;

/// <summary>
/// Specifies the type of Zalo chat thread (User DM or Group).
/// </summary>
public enum ZaloThreadType
{
    /// <summary>Direct message thread with a single user.</summary>
    User,

    /// <summary>Group chat thread.</summary>
    Group
}

/// <summary>
/// Status of an active QR code login attempt.
/// </summary>
public enum ZaloLoginStatus
{
    /// <summary>QR login initialized, waiting for user scan.</summary>
    Pending,

    /// <summary>QR code scanned by mobile app, waiting for user confirmation.</summary>
    Scanned,

    /// <summary>Login confirmed and session material retrieved successfully.</summary>
    Connected,

    /// <summary>QR code session expired.</summary>
    Expired,

    /// <summary>Login attempt declined by user.</summary>
    Declined,

    /// <summary>Login attempt failed due to an unexpected error (see <see cref="ZaloLoginState.ErrorMessage"/>).</summary>
    Failed,

    /// <summary>No login session exists for the given session id (unknown or already consumed).</summary>
    NotFound
}

/// <summary>
/// Real-time connection status for Zalo WebSocket sessions.
/// </summary>
public enum ZaloConnectionStatus
{
    /// <summary>WebSocket listener is connected.</summary>
    Connected,

    /// <summary>WebSocket listener disconnected.</summary>
    Disconnected,

    /// <summary>Session is attempting automatic reconnection.</summary>
    Reconnecting,

    /// <summary>Session has expired or credentials became invalid.</summary>
    SessionExpired,

    /// <summary>Connection rejected due to duplicate login session elsewhere.</summary>
    DuplicateConnection
}

/// <summary>
/// Log levels for Zalo client diagnostics.
/// </summary>
public enum ZaloLogLevel
{
    /// <summary>Detailed diagnostic information.</summary>
    Debug,

    /// <summary>General informational operational messages.</summary>
    Information,

    /// <summary>Warning messages for non-fatal issues.</summary>
    Warning,

    /// <summary>Error messages for failed operations.</summary>
    Error
}

/// <summary>
/// Serialized material describing an authenticated Zalo session.
/// </summary>
public sealed record ZaloSessionMaterial(
    string CookiesJson,
    string SecretKey,
    string Imei,
    string Uid,
    string UserAgent,
    string Language = "vi");

/// <summary>
/// Session details returned when initiating QR code login.
/// </summary>
public sealed record ZaloQrSession(
    System.Guid SessionId,
    string QrImageBase64,
    string QrCode,
    System.DateTimeOffset ExpiresAt);

/// <summary>
/// State snapshot during QR code login polling.
/// </summary>
public sealed record ZaloLoginState(
    System.Guid SessionId,
    ZaloLoginStatus Status,
    string? DisplayName = null,
    string? AvatarUrl = null,
    string? ErrorMessage = null);

/// <summary>
/// Active Zalo session details including service mappings, WS endpoints, and optional Proxy routing.
/// </summary>
public sealed record ZaloSession(
    ZaloSessionMaterial Material,
    string Uid,
    string[] WsUrls,
    System.Collections.Generic.IReadOnlyDictionary<string, string[]> ServiceMap,
    int PingIntervalMs,
    System.Net.IWebProxy? Proxy = null)
{
    /// <summary>
    /// Resolves the primary gateway URL for the specified <paramref name="serviceKey"/> from <see cref="ServiceMap"/>, or <c>null</c> if not present.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "Gateway host strings are combined with relative paths.")]
    public string? GetServiceUrl(string serviceKey)
    {
        if (this.ServiceMap is not null &&
            this.ServiceMap.TryGetValue(serviceKey, out string[]? urls) &&
            urls is { Length: > 0 } &&
            !string.IsNullOrWhiteSpace(urls[0]))
        {
            return urls[0];
        }

        return null;
    }
}

/// <summary>
/// Represents a message media or file attachment.
/// </summary>
public sealed record ZaloAttachment(
    string Url,
    string FileName,
    string Type);

/// <summary>
/// Event payload emitted when a message is received over WebSocket.
/// </summary>
public sealed record ZaloMessageEvent(
    string MsgId,
    string CliMsgId,
    string MsgType,
    string UidFrom,
    string IdTo,
    string DisplayName,
    string ThreadId,
    ZaloThreadType ThreadType,
    string TimestampMs,
    object? Content,
    System.Collections.Generic.IReadOnlyList<ZaloAttachment>? Attachments,
    bool IsSelf,
    string? RawJson = null,
    System.Collections.Generic.IReadOnlyList<string>? MentionedUids = null,
    bool IsMentionAll = false);

/// <summary>
/// Event payload emitted when the WebSocket connection status changes.
/// </summary>
public sealed record ZaloSessionStatusChanged(
    string Uid,
    ZaloConnectionStatus Status,
    string? Reason = null);

/// <summary>
/// Result returned after sending a message.
/// </summary>
/// <param name="MsgId">Server-assigned message id.</param>
/// <param name="CliMsgId">Client-generated message id sent in the request, echoed back so callers can de-duplicate.</param>
public sealed record ZaloSendResult(string MsgId, string CliMsgId = "");

/// <summary>
/// Profile information for a Zalo user.
/// </summary>
public sealed record ZaloUserProfile(
    string Uid,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// Result returned after creating a new Zalo group.
/// </summary>
public sealed record ZaloGroupCreateResult(
    string GroupId,
    System.Collections.Generic.IReadOnlyList<string> SuccessMembers,
    System.Collections.Generic.IReadOnlyList<string> ErrorMembers);

/// <summary>
/// Information describing a Zalo group chat.
/// </summary>
public sealed record ZaloGroupInfo(
    string GroupId,
    string Name,
    string? AvatarUrl,
    int MemberCount,
    string OwnerId);

/// <summary>
/// Detailed contact information for a Zalo friend.
/// </summary>
public sealed record ZaloFriendInfo(
    string UserId,
    string DisplayName,
    string? AvatarUrl,
    string? PhoneNumber = null,
    string? Alias = null);

/// <summary>
/// Represents a bank account card for Zalo transfer requests.
/// </summary>
/// <param name="BinBank">The bank BIN code (e.g. 970458 for TPBank, 970436 for Vietcombank).</param>
/// <param name="AccountNumber">The bank account number.</param>
/// <param name="AccountName">The bank account holder name.</param>
public sealed record ZaloBankCard(string BinBank, string AccountNumber, string AccountName);

/// <summary>
/// Represents a user contact card recommendation payload.
/// </summary>
/// <param name="UserId">The target user ID of the contact being shared.</param>
/// <param name="PhoneNumber">Optional phone number associated with the contact.</param>
/// <param name="QrCodeUrl">Optional QR Code profile URL for the contact.</param>
public sealed record ZaloContactCard(string UserId, string? PhoneNumber = null, string? QrCodeUrl = null);

/// <summary>
/// Client-side privacy guard options to suppress outbound typing, delivered, and seen receipts.
/// </summary>
public sealed record ZaloPrivacyOptions(
    bool BlockTyping = false,
    bool BlockDelivered = false,
    bool BlockSeen = false);

/// <summary>
/// Rich link preview metadata parsed from a URL via <c>/api/message/parselink</c>.
/// </summary>
public sealed record ZaloLinkMetadata(
    string Url,
    string Title,
    string Description = "",
    string ThumbnailUrl = "",
    string SourceDomain = "");

/// <summary>
/// Metadata for an official Zalo sticker pack in the Sticker Store.
/// </summary>
public sealed record ZaloStickerPack(
    long Id,
    string Name,
    int TotalImages,
    string IconUrl);

/// <summary>
/// Metadata for an individual Zalo sticker item returned from search/suggest or pack details.
/// </summary>
public sealed record ZaloStickerItem(
    int StickerId,
    int CateId,
    int StickerType = 7,
    string Label = "");

/// <summary>
/// Online status and last active timestamp for a Zalo user.
/// </summary>
public sealed record ZaloOnlineStatus(
    string UserId,
    bool IsOnline,
    long LastOnlineTimestamp,
    string StatusText);

/// <summary>
/// Friendship state between the authenticated account and a target user.
/// </summary>
public enum ZaloFriendshipStatus
{
    /// <summary>Status could not be determined.</summary>
    Unknown,

    /// <summary>Both users are friends.</summary>
    Friend,

    /// <summary>Current user sent a pending friend request to the target user.</summary>
    OutgoingPending,

    /// <summary>Target user sent a pending friend request to the current user.</summary>
    IncomingPending,

    /// <summary>Users are not friends and have no pending requests.</summary>
    Stranger
}

/// <summary>
/// Represents a muted conversation entry returned by <c>/api/social/profile/getmute</c>.
/// </summary>
public sealed record ZaloMutedConversation(
    string ThreadId,
    bool IsGroup,
    long UntilTimestamp);

/// <summary>
/// Represents a manually marked unread conversation returned by <c>/api/conv/getUnreadMark</c>.
/// </summary>
public sealed record ZaloUnreadMark(
    string ThreadId,
    bool IsGroup,
    string ClientMsgId = "",
    string FromUid = "");

/// <summary>
/// Represents an outbound or inbound @mention span within a group message.
/// </summary>
public sealed record ZaloMention(
    string Uid,
    int Position,
    int Length,
    bool IsMentionAll = false);

/// <summary>
/// Represents a historical message fetched from server history (<c>get-last-msgs</c>, <c>group/history</c>, or <c>cm/getrecentv2</c>).
/// </summary>
public sealed record ZaloHistoryMessage(
    string MsgId,
    string CliMsgId,
    string ThreadId,
    bool IsGroup,
    string SenderId,
    string SenderName,
    string Content,
    string MsgType,
    long Timestamp,
    bool IsSelf);

/// <summary>
/// Group permission and moderation settings for <c>/api/group/setting/update</c>.
/// </summary>
public sealed record ZaloGroupSettingsOptions(
    bool BlockName = false,
    bool SignAdminMsg = false,
    bool SetTopicOnly = false,
    bool EnableMsgHistory = true,
    bool JoinApproval = false,
    bool LockCreatePost = false,
    bool LockCreatePoll = false,
    bool LockSendMsg = false,
    bool LockViewMember = false);

/// <summary>
/// Details of a group invite link returned by <c>/api/group/link/detail</c> or <c>/api/group/link/new</c>.
/// </summary>
public sealed record ZaloGroupLinkDetail(
    string Link,
    long ExpirationDate = 0,
    bool Enabled = true);

/// <summary>
/// Options for creating a group poll via <c>/api/poll/create</c>.
/// </summary>
public sealed record ZaloPollCreateOptions(
    string Question,
    System.Collections.Generic.IReadOnlyList<string> Options,
    long ExpiredTime = 0,
    bool AllowMultiChoices = true,
    bool AllowAddNewOption = true,
    bool HideVotePreview = false,
    bool IsAnonymous = false);

/// <summary>
/// Represents an individual voting option in a Zalo group poll.
/// </summary>
public sealed record ZaloPollOption(
    long OptionId,
    string Content,
    int Votes,
    bool Voted,
    System.Collections.Generic.IReadOnlyList<string> Voters);

/// <summary>
/// Detailed state of a Zalo group poll returned by <c>/api/poll/create</c>, <c>/api/poll/detail</c>, or <c>/api/poll/vote</c>.
/// </summary>
public sealed record ZaloPollDetail(
    long PollId,
    string CreatorId,
    string Question,
    bool Closed,
    bool AllowMultiChoices,
    bool AllowAddNewOption,
    bool IsAnonymous,
    bool HideVotePreview,
    int TotalVotes,
    long CreatedTime,
    long ExpiredTime,
    System.Collections.Generic.IReadOnlyList<ZaloPollOption> Options);

/// <summary>
/// Represents a group board item (note, pinned topic, or poll) returned by <c>/api/board/list</c> or <c>/api/board/topic/createv2</c>.
/// </summary>
public sealed record ZaloBoardItem(
    string Id,
    int BoardType,
    string Title,
    string CreatorId = "",
    long CreatedTime = 0,
    long UpdatedTime = 0);

/// <summary>
/// Repeat schedule mode for official Zalo reminders.
/// </summary>
public enum ZaloReminderRepeatMode
{
    /// <summary>One-time reminder (no repeat).</summary>
    None = 0,

    /// <summary>Repeats every day.</summary>
    Daily = 1,

    /// <summary>Repeats every week.</summary>
    Weekly = 2,

    /// <summary>Repeats every month.</summary>
    Monthly = 3
}

/// <summary>
/// Options for creating or updating an official Zalo reminder (<c>/api/board/oneone/*</c> or <c>/api/board/topic/*</c>).
/// </summary>
public sealed record ZaloReminderOptions(
    string Title,
    string Emoji = "⏰",
    long StartTimeMs = 0,
    ZaloReminderRepeatMode Repeat = ZaloReminderRepeatMode.None,
    bool PinAct = false);

/// <summary>
/// Represents an official Zalo reminder entry in a 1-on-1 or group thread.
/// </summary>
public sealed record ZaloReminderItem(
    string ReminderId,
    string ThreadId,
    bool IsGroup,
    string Title,
    string CreatorId = "",
    string Emoji = "⏰",
    long StartTimeMs = 0,
    ZaloReminderRepeatMode Repeat = ZaloReminderRepeatMode.None,
    long CreatedTimeMs = 0);

/// <summary>
/// Represents an outgoing or incoming friend request / recommendation entry.
/// </summary>
public sealed record ZaloFriendRequestItem(
    string UserId,
    string DisplayName,
    string ZaloName = "",
    string AvatarUrl = "",
    string Message = "",
    long Timestamp = 0,
    bool IsIncoming = false);

/// <summary>
/// Represents a user alias mapping returned by <c>/api/alias/list</c>.
/// </summary>
public sealed record ZaloAliasEntry(
    string UserId,
    string Alias);

/// <summary>
/// Represents a conversation CRM label returned by <c>/api/convlabel/get</c>.
/// </summary>
public sealed record ZaloLabelItem(
    int Id,
    string Text,
    string TextKey,
    string Color,
    string Emoji,
    System.Collections.Generic.IReadOnlyList<string> Conversations,
    long CreateTime = 0);

/// <summary>
/// Represents a quick message template returned by <c>/api/quickmessage/list</c>.
/// </summary>
public sealed record ZaloQuickMessage(
    int Id,
    string Keyword,
    string Title,
    int Type = 0,
    long CreatedTime = 0,
    long LastModified = 0);

/// <summary>
/// Options for updating the authenticated account's profile and business info via <c>/api/social/profile/update</c>.
/// </summary>
public sealed record ZaloProfileUpdateOptions(
    string Name,
    string DateOfBirth,
    int Gender = 0,
    string? BizDescription = null,
    int? BizCategory = null,
    string? BizAddress = null,
    string? BizWebsite = null,
    string? BizEmail = null);


