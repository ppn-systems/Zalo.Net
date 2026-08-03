namespace Zalo.Net.Contracts;

/// <summary>
/// Specifies the type of Zalo chat thread (User DM or Group).
/// </summary>
public enum ZaloThreadType
{
    User,
    Group
}

/// <summary>
/// Status of an active QR code login attempt.
/// </summary>
public enum ZaloLoginStatus
{
    Pending,
    Scanned,
    Connected,
    Expired,
    Declined
}

/// <summary>
/// Real-time connection status for Zalo WebSocket sessions.
/// </summary>
public enum ZaloConnectionStatus
{
    Connected,
    Disconnected,
    Reconnecting,
    SessionExpired,
    DuplicateConnection
}

/// <summary>
/// Log levels for Zalo client diagnostics.
/// </summary>
public enum ZaloLogLevel
{
    Debug,
    Information,
    Warning,
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
    string? AvatarUrl = null);

/// <summary>
/// Active Zalo session details including service mappings and WS endpoints.
/// </summary>
public sealed record ZaloSession(
    ZaloSessionMaterial Material,
    string Uid,
    string[] WsUrls,
    IReadOnlyDictionary<string, string[]> ServiceMap,
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
    IReadOnlyList<ZaloAttachment>? Attachments,
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
