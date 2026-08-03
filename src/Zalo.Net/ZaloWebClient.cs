// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;
using Zalo.Net.Endpoints;
using Zalo.Net.WebSocket;

namespace Zalo.Net;

/// <summary>
/// Primary client facade for Zalo Web API interactions, authentication flows, and real-time WebSocket messaging.
/// </summary>
public sealed class ZaloWebClient : IDisposable
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    private readonly System.Net.IWebProxy? _proxy;
    private readonly Dictionary<Guid, QrSession> _qrSessions = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets the configured proxy instance for this client, if assigned.
    /// </summary>
    public System.Net.IWebProxy? Proxy => _proxy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloWebClient"/> class with an optional Proxy handler.
    /// </summary>
    public ZaloWebClient(System.Net.IWebProxy? proxy = null) => _proxy = proxy;

#pragma warning disable CS0067
    /// <summary>Occurs when an inbound WebSocket message is received.</summary>
    public event EventHandler<ZaloMessageEvent>? MessageReceived;

    /// <summary>Occurs when the session connection status changes.</summary>
    public event EventHandler<ZaloSessionStatusChanged>? StatusChanged;
#pragma warning restore CS0067

    /// <summary>Starts QR login flow.</summary>
    public async Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct)
    {
        string ua = DefaultUserAgent;
        ZaloHttpClient http = new(ua, proxy: _proxy);

        string version = await LoginQrApis.LoadLoginPageAsync(http, ct).ConfigureAwait(false);

        await LoginQrApis.GetLoginInfoAsync(http, version, ct).ConfigureAwait(false);
        await LoginQrApis.VerifyClientAsync(http, version, ct).ConfigureAwait(false);

        JsonNode qrData = await LoginQrApis.GenerateQrAsync(http, version, ct).ConfigureAwait(false);

        string code = qrData["code"]?.GetValue<string>()
                     ?? throw new ZaloApiException("QR response missing 'code'");
        string image = qrData["image"]?.GetValue<string>()
                     ?? throw new ZaloApiException("QR response missing 'image'");

        image = image.Replace("data:image/png;base64,", "", StringComparison.Ordinal);

        Guid sessionId = Guid.NewGuid();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(100);

        QrSession qrSession = new(http, version, code, ua, expiresAt, sessionId);
        lock (_lock)
        {
            _qrSessions[sessionId] = qrSession;
        }

        _ = Task.Run(() => this.RunQrLoginFlowAsync(sessionId, qrSession.Cts.Token), CancellationToken.None);

        return new ZaloQrSession(sessionId, image, code, expiresAt);
    }

    /// <summary>Polls QR login status.</summary>
    public Task<ZaloLoginState> PollLoginAsync(Guid sessionId)
    {
        lock (_lock)
        {
            if (!_qrSessions.TryGetValue(sessionId, out QrSession? session))
            {
                return Task.FromResult(new ZaloLoginState(sessionId, ZaloLoginStatus.Expired));
            }

            if (DateTimeOffset.UtcNow >= session.ExpiresAt)
            {
                session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired);
                return Task.FromResult(session.CurrentState);
            }

            return Task.FromResult(session.CurrentState);
        }
    }

    private async Task RunQrLoginFlowAsync(Guid sessionId, CancellationToken sessionCt)
    {
        QrSession session;
        lock (_lock)
        {
            if (!_qrSessions.TryGetValue(sessionId, out session!))
            {
                return;
            }
        }

        string? displayName = null;
        string? avatar = null;

        try
        {

            while (!sessionCt.IsCancellationRequested && DateTimeOffset.UtcNow < session.ExpiresAt)
            {
                JsonNode? scanResult = null;
                try
                {
                    scanResult = await LoginQrApis.WaitingScanAsync(session.Http, session.Version, session.Code, sessionCt).ConfigureAwait(false);
                }
                catch (ZaloApiException ex) when (ex.Message.Contains("429", StringComparison.Ordinal))
                {
                    await Task.Delay(4000, sessionCt).ConfigureAwait(false);
                    continue;
                }

                if (scanResult is null)
                {
                    continue;
                }

                int errorCode = scanResult["error_code"]?.GetValue<int>() ?? -1;
                if (errorCode == -13)
                {
                    lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Declined); }
                    return;
                }
                if (errorCode == 8)
                {
                    await Task.Delay(1000, sessionCt).ConfigureAwait(false);
                    continue;
                }
                if (errorCode != 0)
                {
                    await Task.Delay(3000, sessionCt).ConfigureAwait(false);
                    continue;
                }

                displayName = scanResult["data"]?["displayName"]?.GetValue<string>();
                avatar = scanResult["data"]?["avatar"]?.GetValue<string>();
                lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Scanned, displayName, avatar); }

                if (scanResult["data"]?["confirmed"]?.GetValue<bool>() == true)
                {
                    break;
                }

                break;
            }

            if (sessionCt.IsCancellationRequested || DateTimeOffset.UtcNow >= session.ExpiresAt)
            {
                lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired); }
                return;
            }

            while (!sessionCt.IsCancellationRequested && DateTimeOffset.UtcNow < session.ExpiresAt)
            {
                JsonNode? confirmResult = null;
                try
                {
                    confirmResult = await LoginQrApis.WaitingConfirmAsync(session.Http, session.Version, session.Code, sessionCt).ConfigureAwait(false);
                }
                catch (ZaloApiException ex) when (ex.Message.Contains("429", StringComparison.Ordinal))
                {
                    await Task.Delay(4000, sessionCt).ConfigureAwait(false);
                    continue;
                }

                if (confirmResult is null)
                {
                    continue;
                }

                int errorCode = confirmResult["error_code"]?.GetValue<int>() ?? -1;
                if (errorCode == -13)
                {
                    lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Declined); }
                    return;
                }
                if (errorCode == 8)
                {
                    await Task.Delay(1000, sessionCt).ConfigureAwait(false);
                    continue;
                }
                if (errorCode != 0)
                {
                    lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired); }
                    return;
                }

                await LoginQrApis.CheckSessionAsync(session.Http, sessionCt).ConfigureAwait(false);

                JsonNode? userInfo = await LoginQrApis.GetUserInfoAsync(session.Http, sessionCt).ConfigureAwait(false);
                string uid = userInfo?["uid"]?.GetValue<string>()
                               ?? confirmResult["data"]?["userId"]?.GetValue<string>()
                               ?? "";

                displayName = userInfo?["displayName"]?.GetValue<string>() ?? displayName ?? "";
                avatar = userInfo?["avatar"]?.GetValue<string>() ?? avatar;

                string imei = Hashing.GenerateImei(session.UserAgent);
                ParamsEncryptor encryptor = new(ZaloHttpClient.ApiType, imei, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                string enk = encryptor.GetEncryptKey();

                JsonNode? loginData = await LoginApis.GetLoginInfoAsync(session.Http, imei, "vi", enk, sessionCt).ConfigureAwait(false);
                _ = await LoginApis.GetServerInfoAsync(session.Http, imei, sessionCt).ConfigureAwait(false);

                string secretKey = loginData?["zpw_enk"]?.GetValue<string>()
                                 ?? throw new ZaloApiException("Missing zpw_enk in login response");

                string cookiesJson = session.Http.Cookies.ToJson();
                ZaloSessionMaterial material = new(cookiesJson, secretKey, imei, uid, session.UserAgent);

                lock (_lock)
                {
                    session.Material = material;
                    session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Connected, displayName, avatar);
                }
                return;
            }

            if (sessionCt.IsCancellationRequested || DateTimeOffset.UtcNow >= session.ExpiresAt)
            {
                lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired, displayName, avatar, ex.Message); }
            this.StatusChanged?.Invoke(this, new ZaloSessionStatusChanged("", ZaloConnectionStatus.SessionExpired, ex.Message));
        }
    }

    /// <summary>Consumes pending session material after successful login.</summary>
    public ZaloSessionMaterial? ConsumePendingMaterial(Guid sessionId)
    {
        lock (_lock)
        {
            if (_qrSessions.TryGetValue(sessionId, out QrSession? session) && session.Material != null)
            {
                ZaloSessionMaterial material = session.Material;
                this.RemoveQrSession(sessionId);
                return material;
            }
            return null;
        }
    }

    /// <summary>Logs in from session material with optional proxy routing.</summary>
    public static async Task<ZaloSession> LoginWithSessionAsync(
        ZaloSessionMaterial material,
        CancellationToken ct,
        System.Net.IWebProxy? proxy = null)
    {
        ArgumentNullException.ThrowIfNull(material);

        CookieStore cookies = CookieStore.FromJson(material.CookiesJson);
        using ZaloHttpClient http = new(material.UserAgent, cookies, proxy);

        JsonNode? loginData = await LoginApis.GetLoginInfoAsync(http, material.Imei, material.Language, material.SecretKey, ct).ConfigureAwait(false);
        JsonNode? serverData = await LoginApis.GetServerInfoAsync(http, material.Imei, ct).ConfigureAwait(false);

        string uid = loginData?["uid"]?.GetValue<string>() ?? material.Uid;
        string secretKey = loginData?["zpw_enk"]?.GetValue<string>() ?? material.SecretKey;
        string[] wsUrls = ExtractWsUrls(loginData);
        IReadOnlyDictionary<string, string[]> svcMap = ExtractServiceMap(loginData);
        _ = serverData;

        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Auth.SessionLoaded))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Auth.SessionLoaded, new { AuthenticatedUid = uid, HasProxy = proxy != null });
        }

        return new ZaloSession(
            material with { SecretKey = secretKey, Uid = uid },
            uid, wsUrls, svcMap,
            PingIntervalMs: 20_000,
            Proxy: proxy);
    }

    private static string BuildWsUrl(string baseUrl)
    {
        string sep = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{baseUrl}{sep}zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}&t={now}";
    }

    /// <summary>Starts WebSocket listener.</summary>
    public Task StartListenerAsync(ZaloSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.WsUrls is not { Length: > 0 })
        {
            throw new ZaloApiException("No WebSocket URLs in session");
        }
        ZaloWsListener listener = new(
            session,
            msg => this.MessageReceived?.Invoke(this, msg),
            status => this.StatusChanged?.Invoke(this, status));

        string wsUrl = BuildWsUrl(session.WsUrls[0]);
        return listener.RunAsync(wsUrl, ct).ContinueWith(_ => { }, ct, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Sends a text message.</summary>
    public static async Task<ZaloSendResult> SendTextAsync(
        ZaloSession session, string threadId, ZaloThreadType threadType, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        string msgId = await MessageApis.SendTextAsync(http, session, threadId, threadType, text, ct).ConfigureAwait(false);
        return new ZaloSendResult(msgId);
    }

    /// <summary>Sends an image attachment message.</summary>
    public static async Task<ZaloSendResult> SendAttachmentAsync(
        ZaloSession session, string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName, string? caption,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        return await AttachmentApis.SendImageAttachmentAsync(http, session, threadId, threadType, fileBytes, fileName, caption, ct).ConfigureAwait(false);
    }

    /// <summary>Fetches profile information for a user.</summary>
    public static async Task<ZaloUserProfile> GetUserInfoAsync(ZaloSession session, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        (string uid, string name, string? avatar) = await MessageApis.GetUserInfoAsync(http, session, userId, ct).ConfigureAwait(false);
        return new ZaloUserProfile(uid, name, avatar);
    }

    /// <summary>Sends a quote/reply message.</summary>
    public static async Task<string> SendQuoteMessageAsync(
        ZaloSession session, string threadId, ZaloThreadType threadType, string text,
        string quoteMsgId, string quoteCliMsgId, string quoteSenderUid, string quoteContent,
        long quoteTs = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        return await MessageApis.SendQuoteAsync(http, session, threadId, threadType, text, quoteMsgId, quoteCliMsgId, quoteSenderUid, quoteContent, quoteTs, ct).ConfigureAwait(false);
    }

    /// <summary>Recalls/undos a previously sent message.</summary>
    public static async Task UndoMessageAsync(
        ZaloSession session, string threadId, string msgId, string cliMsgId, ZaloThreadType type,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await UndoApis.UndoMessageAsync(http, session, threadId, msgId, cliMsgId, type, ct).ConfigureAwait(false);
    }

    /// <summary>Adds a reaction (emoji) to a message.</summary>
    public static async Task AddReactionAsync(
        ZaloSession session, string threadId, string msgId, string cliMsgId, ZaloThreadType type,
        ZaloReactionType reaction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await ReactionApis.AddReactionAsync(http, session, threadId, msgId, cliMsgId, type, reaction, ct).ConfigureAwait(false);
    }

    /// <summary>Sends a Zalo sticker message.</summary>
    public static async Task SendStickerAsync(
        ZaloSession session, string threadId, int stickerId, int cateId, int stickerType, ZaloThreadType type,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await StickerApis.SendStickerAsync(http, session, threadId, stickerId, cateId, stickerType, type, ct).ConfigureAwait(false);
    }

    #region Group Management (Group 2)

    /// <summary>Fetches all group chats the user belongs to.</summary>
    public static async Task<IReadOnlyList<ZaloGroupInfo>> GetAllGroupsAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        return await GroupApis.GetAllGroupsAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a new Zalo group chat.</summary>
    public static async Task<ZaloGroupCreateResult> CreateGroupAsync(
        ZaloSession session, string groupName, IEnumerable<string> memberIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        return await GroupApis.CreateGroupAsync(http, session, groupName, memberIds, ct).ConfigureAwait(false);
    }

    /// <summary>Leaves a Zalo group chat.</summary>
    public static async Task LeaveGroupAsync(
        ZaloSession session, string groupId, bool silent = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await GroupApis.LeaveGroupAsync(http, session, groupId, silent, ct).ConfigureAwait(false);
    }

    /// <summary>Adds user(s) to an existing Zalo group chat.</summary>
    public static async Task AddUserToGroupAsync(
        ZaloSession session, string groupId, IEnumerable<string> memberIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await GroupApis.AddUserToGroupAsync(http, session, groupId, memberIds, ct).ConfigureAwait(false);
    }

    /// <summary>Removes user(s) from a Zalo group chat.</summary>
    public static async Task RemoveUserFromGroupAsync(
        ZaloSession session, string groupId, IEnumerable<string> memberIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await GroupApis.RemoveUserFromGroupAsync(http, session, groupId, memberIds, ct).ConfigureAwait(false);
    }

    /// <summary>Changes the display name of a Zalo group chat.</summary>
    public static async Task ChangeGroupNameAsync(
        ZaloSession session, string groupId, string newName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await GroupApis.ChangeGroupNameAsync(http, session, groupId, newName, ct).ConfigureAwait(false);
    }

    #endregion

    #region Friends & Contacts Management (Group 4)

    /// <summary>Retrieves all friends in the user's Zalo contact list.</summary>
    public static async Task<IReadOnlyList<ZaloFriendInfo>> GetAllFriendsAsync(
        ZaloSession session, int count = 20000, int page = 1, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        return await FriendApis.GetAllFriendsAsync(http, session, count, page, ct).ConfigureAwait(false);
    }

    /// <summary>Finds a Zalo user profile by phone number.</summary>
    public static async Task<ZaloUserProfile> FindUserByPhoneAsync(
        ZaloSession session, string phoneNumber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        return await FriendApis.FindUserByPhoneAsync(http, session, phoneNumber, ct).ConfigureAwait(false);
    }

    /// <summary>Sends a friend request to a user.</summary>
    public static async Task SendFriendRequestAsync(
        ZaloSession session, string userId, string? message = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await FriendApis.SendFriendRequestAsync(http, session, userId, message, ct).ConfigureAwait(false);
    }

    /// <summary>Accepts an incoming friend request from a user.</summary>
    public static async Task AcceptFriendRequestAsync(
        ZaloSession session, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await FriendApis.AcceptFriendRequestAsync(http, session, userId, ct).ConfigureAwait(false);
    }

    /// <summary>Blocks a user.</summary>
    public static async Task BlockUserAsync(
        ZaloSession session, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await FriendApis.BlockUserAsync(http, session, userId, ct).ConfigureAwait(false);
    }

    /// <summary>Unblocks a user.</summary>
    public static async Task UnblockUserAsync(
        ZaloSession session, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await FriendApis.UnblockUserAsync(http, session, userId, ct).ConfigureAwait(false);
    }

    /// <summary>Changes the display alias (nickname) of a friend for CRM contact tracking.</summary>
    public static async Task ChangeFriendAliasAsync(
        ZaloSession session, string userId, string alias, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        await FriendApis.ChangeFriendAliasAsync(http, session, userId, alias, ct).ConfigureAwait(false);
    }

    #endregion

    /// <summary>Runs WebSocket listener with automatic exponential backoff reconnects and optional proxy routing.</summary>
    public async Task RunWithReconnectAsync(
        ZaloSessionMaterial material,
        CancellationToken ct,
        System.Net.IWebProxy? proxy = null)
    {
        ArgumentNullException.ThrowIfNull(material);

        TimeSpan backoff = TimeSpan.FromSeconds(2);
        const int maxBackoffSec = 60;

        while (!ct.IsCancellationRequested)
        {
            ZaloSession session;
            try
            {
                session = await LoginWithSessionAsync(material, ct, proxy ?? _proxy).ConfigureAwait(false);
            }
            catch (ZaloApiException ex) when (IsAuthError(ex))
            {
                this.StatusChanged?.Invoke(this, new ZaloSessionStatusChanged(material.Uid, ZaloConnectionStatus.SessionExpired, ex.Message));
                return;
            }
            catch (OperationCanceledException) { return; }

            this.StatusChanged?.Invoke(this, new ZaloSessionStatusChanged(session.Uid, ZaloConnectionStatus.Reconnecting));

            bool disconnected = false;
            bool isDuplicate = false;
            bool isExpired = false;

            void OnStatus(object? _, ZaloSessionStatusChanged e)
            {
                if (e.Status == ZaloConnectionStatus.DuplicateConnection)
                {
                    isDuplicate = true;
                }
                if (e.Status == ZaloConnectionStatus.SessionExpired)
                {
                    isExpired = true;
                }
                if (e.Status == ZaloConnectionStatus.Disconnected)
                {
                    disconnected = true;
                }
            }
            this.StatusChanged += OnStatus;
            try
            {
                await this.StartListenerAsync(session, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            finally { this.StatusChanged -= OnStatus; }

            if (isDuplicate || isExpired || ct.IsCancellationRequested)
            {
                return;
            }
            if (!disconnected)
            {
                return;
            }

            this.StatusChanged?.Invoke(this, new ZaloSessionStatusChanged(material.Uid, ZaloConnectionStatus.Reconnecting));
            try
            {
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, maxBackoffSec));
        }
    }

    private static bool IsAuthError(ZaloApiException ex)
        => ex.Code is 4 or 2 or -101 or -102;

    private static string[] ExtractWsUrls(JsonNode? loginData)
    {
        JsonNode? wsNode = loginData?["zpw_ws"];
        return wsNode is JsonArray arr
            ? [.. arr.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0)]
            : wsNode?.GetValue<string>() is { Length: > 0 } single ? [single] : [];
    }

    private static IReadOnlyDictionary<string, string[]> ExtractServiceMap(JsonNode? loginData)
    {
        JsonNode? mapNode = loginData?["zpw_service_map_v3"];
        if (mapNode is not JsonObject obj)
        {
            return new Dictionary<string, string[]>();
        }
        Dictionary<string, string[]> result = [];
        foreach (KeyValuePair<string, JsonNode?> kvp in obj)
        {
            if (kvp.Value is JsonArray arr)
            {
                result[kvp.Key] = [.. arr.Select(n => n?.GetValue<string>() ?? "")];
            }
        }
        return result;
    }

    private static ZaloHttpClient CreateHttpForSession(ZaloSession session)
        => new(session.Material.UserAgent, CookieStore.FromJson(session.Material.CookiesJson), session.Proxy);

    private void RemoveQrSession(Guid sessionId)
    {
        lock (_lock)
        {
            if (_qrSessions.TryGetValue(sessionId, out QrSession? s))
            {
                s.Dispose();
                _ = _qrSessions.Remove(sessionId);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            foreach (QrSession s in _qrSessions.Values)
            {
                s.Dispose();
            }
            _qrSessions.Clear();
        }
    }

    private sealed class QrSession : IDisposable
    {
        public ZaloHttpClient Http { get; }
        public string Version { get; }
        public string Code { get; }
        public string UserAgent { get; }
        public DateTimeOffset ExpiresAt { get; }

        public ZaloLoginState CurrentState { get; set; }
        public ZaloSessionMaterial? Material { get; set; }
        public CancellationTokenSource Cts { get; }

        public QrSession(ZaloHttpClient http, string version, string code, string userAgent, DateTimeOffset expiresAt, Guid sessionId)
        {
            this.Http = http;
            this.Version = version;
            this.Code = code;
            this.UserAgent = userAgent;
            this.ExpiresAt = expiresAt;
            this.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Pending);
            this.Cts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            this.Cts.Cancel();
            this.Cts.Dispose();
            this.Http.Dispose();
        }
    }
}
