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
    Declined
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
/// Active Zalo session details including service mappings and WS endpoints.
/// </summary>
public sealed record ZaloSession(
    ZaloSessionMaterial Material,
    string Uid,
    string[] WsUrls,
    System.Collections.Generic.IReadOnlyDictionary<string, string[]> ServiceMap,
    int PingIntervalMs);

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
    bool IsSelf);

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
public sealed record ZaloSendResult(string MsgId);

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
