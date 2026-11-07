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
public sealed class ZaloWebClient : IZaloClient
{
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
    public async Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct = default)
    {
        string ua = ZaloConstants.Protocol.DefaultUserAgent;
        ZaloHttpClient http = new(ua, proxy: _proxy);

        string code, image;
        string version;
        try
        {
            version = await LoginQrApis.LoadLoginPageAsync(http, ct).ConfigureAwait(false);

            await LoginQrApis.GetLoginInfoAsync(http, version, ct).ConfigureAwait(false);
            await LoginQrApis.VerifyClientAsync(http, version, ct).ConfigureAwait(false);

            JsonNode qrData = await LoginQrApis.GenerateQrAsync(http, version, ct).ConfigureAwait(false);

            code = qrData["code"]?.GetValue<string>()
                         ?? throw new ZaloApiException("QR response missing 'code'");
            image = qrData["image"]?.GetValue<string>()
                         ?? throw new ZaloApiException("QR response missing 'image'");
        }
        catch
        {
            http.Dispose();
            throw;
        }

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
    public Task<ZaloLoginState> PollQrStatusAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<ZaloLoginState>(ct);
        }

        return this.PollLoginAsync(sessionId);
    }

    /// <summary>Polls QR login status.</summary>
    public Task<ZaloLoginState> PollLoginAsync(Guid sessionId)
    {
        lock (_lock)
        {
            if (!_qrSessions.TryGetValue(sessionId, out QrSession? session))
            {
                return Task.FromResult(new ZaloLoginState(sessionId, ZaloLoginStatus.NotFound));
            }

            bool isTerminal = session.CurrentState.Status is ZaloLoginStatus.Connected
                or ZaloLoginStatus.Failed or ZaloLoginStatus.Declined or ZaloLoginStatus.Expired;

            if (!isTerminal && DateTimeOffset.UtcNow >= session.ConfirmExpiresAt)
            {
                session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired);
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
                lock (_lock)
                {
                    session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Scanned, displayName, avatar);
                    // Confirming on the phone can happen well after the 100s QR image itself would
                    // expire; give the user a fresh window to confirm once the scan is seen.
                    session.ConfirmExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60);
                }

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

            while (!sessionCt.IsCancellationRequested && DateTimeOffset.UtcNow < session.ConfirmExpiresAt)
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
                    lock (_lock)
                    {
                        session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Failed, displayName, avatar,
                            $"Unexpected confirm error_code {errorCode}");
                    }
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
                ParamsEncryptor encryptor = new(ZaloConstants.Protocol.ApiType, imei, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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

            if (sessionCt.IsCancellationRequested || DateTimeOffset.UtcNow >= session.ConfirmExpiresAt)
            {
                lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Don't surface raw exception text publicly: it can include proxy URIs, credentials,
            // or other internal details. Full detail still goes to diagnostics for local debugging.
            if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Auth.SessionExpired))
            {
                ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Auth.SessionExpired, new { QrLoginError = ex.Message });
            }
            const string safeMessage = "QR login failed due to an internal error.";
            lock (_lock) { session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Failed, displayName, avatar, safeMessage); }
            this.StatusChanged?.Invoke(this, new ZaloSessionStatusChanged("", ZaloConnectionStatus.SessionExpired, safeMessage));
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

        CookieStore cookies = CookieStore.ForMaterial(material);
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
        return $"{baseUrl}{sep}zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&t={now}";
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
        (string msgId, string cliMsgId) = await MessageApis.SendTextAsync(http, session, threadId, threadType, text, ct).ConfigureAwait(false);
        return new ZaloSendResult(msgId, cliMsgId);
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
    public static async Task<ZaloSendResult> SendStickerAsync(
        ZaloSession session, string threadId, int stickerId, int cateId, int stickerType, ZaloThreadType type,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using ZaloHttpClient http = CreateHttpForSession(session);
        return await StickerApis.SendStickerAsync(http, session, threadId, stickerId, cateId, stickerType, type, ct).ConfigureAwait(false);
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

    /// <summary>Retrieves one page of the user's Zalo friend list. Call again with an increasing <paramref name="page"/> to fetch more.</summary>
    public static async Task<IReadOnlyList<ZaloFriendInfo>> GetAllFriendsAsync(
        ZaloSession session, int count = 200, int page = 1, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);

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
        System.Net.IWebProxy? proxy = null,
        CancellationToken ct = default)
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
                if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Auth.SessionExpired))
                {
                    ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Auth.SessionExpired, new { ReconnectAuthError = ex.Message });
                }
                this.StatusChanged?.Invoke(this, new ZaloSessionStatusChanged(material.Uid, ZaloConnectionStatus.SessionExpired, "Session authentication failed."));
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
                TimeSpan jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(100, 1500));
                await Task.Delay(backoff + jitter, ct).ConfigureAwait(false);
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

    private static ZaloHttpClient CreateHttpForSession(ZaloSession session, ZaloPrivacyOptions? privacy = null)
        => new(session.Material.UserAgent, CookieStore.ForMaterial(session.Material), session.Proxy)
        {
            PrivacyOptions = privacy ?? new ZaloPrivacyOptions()
        };

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

    /// <inheritdoc/>
    public Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloBankCard bankCard, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bankCard);
        return this.SendBankCardAsync(session, threadId, threadType, bankCard.BinBank, bankCard.AccountNumber, bankCard.AccountName, ct);
    }

    /// <inheritdoc/>
    public async Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string binBank, string accountNumber, string accountName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session);
        await http.SendBankCardAsync(session, threadId, threadType, binBank, accountNumber, accountName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloContactCard contactCard, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contactCard);
        return this.SendContactCardAsync(session, threadId, threadType, contactCard.UserId, contactCard.PhoneNumber, contactCard.QrCodeUrl, ct);
    }

    /// <inheritdoc/>
    public async Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string userId, string? phoneNumber = null, string? qrCodeUrl = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session);
        await http.SendContactCardAsync(session, threadId, threadType, userId, phoneNumber, qrCodeUrl, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task JoinGroupViaLinkAsync(ZaloSession session, string inviteUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session);
        await http.JoinGroupViaLinkAsync(session, inviteUrl, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ReviewJoinRequestsAsync(ZaloSession session, string groupId, string[] memberUids, bool approve, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session);
        await http.ReviewJoinRequestsAsync(session, groupId, memberUids, approve, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task LeaveGroupSilentlyAsync(ZaloSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session);
        await http.LeaveGroupSilentlyAsync(session, groupId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task KickGroupMemberAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session);
        await GroupApis.RemoveUserFromGroupAsync(http, session, groupId, [memberUid], ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task PromoteGroupAdminAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session);
        await GroupApis.PromoteGroupAdminAsync(http, session, groupId, memberUid, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task PinGroupMessageAsync(ZaloSession session, string groupId, string msgId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session);
        await GroupApis.PinGroupMessageAsync(http, session, groupId, msgId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SendImageAsync(ZaloSession session, string threadId, ZaloThreadType threadType, byte[] imageBytes, string fileName, string? caption = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        _ = await MessageApis.SendPhotoAsync(http, session, threadId, threadType, imageBytes, fileName, caption, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ZaloPrivacyOptions PrivacyOptions { get; set; } = new();

    /// <inheritdoc/>
    public async Task<ZaloSessionMaterial> LoginByPasswordAsync(
        string phone, string password, string isoCountryCode = "VN", string? imei = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        string effectiveImei = string.IsNullOrWhiteSpace(imei) ? $"{Guid.NewGuid():N}-8668" : imei.Trim();
        string ua = ZaloConstants.Protocol.DefaultUserAgent;
        using ZaloHttpClient http = new(ua, proxy: _proxy);
        JsonNode? loginData = await LoginApis.LoginByPasswordAsync(http, phone, password, effectiveImei, isoCountryCode, "vi", ct).ConfigureAwait(false);

        string uid = loginData?["uid"]?.ToString()
                  ?? loginData?["userId"]?.ToString()
                  ?? loginData?["account"]?["uid"]?.ToString()
                  ?? loginData?["account"]?["userId"]?.ToString()
                  ?? string.Empty;
        string secretKey = loginData?["zpw_enk"]?.ToString() ?? string.Empty;
        string? zpwSek = loginData?["zpw_sek"]?.ToString() ?? loginData?["account"]?["zpw_sek"]?.ToString();

        if (!string.IsNullOrEmpty(zpwSek))
        {
            http.Cookies.AddCookies("https://chat.zalo.me", [$"zpw_sek={zpwSek}; Domain=.zalo.me; Path=/"]);
        }

        if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(uid))
        {
            JsonNode? info = await LoginApis.GetLoginInfoAsync(http, effectiveImei, "vi", string.Empty, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(uid))
            {
                uid = info?["uid"]?.ToString() ?? info?["userId"]?.ToString() ?? string.Empty;
            }
            if (string.IsNullOrEmpty(secretKey))
            {
                secretKey = info?["zpw_enk"]?.ToString() ?? string.Empty;
            }
        }

        return new ZaloSessionMaterial(
            CookiesJson: http.Cookies.ToJson(),
            SecretKey: secretKey,
            Imei: effectiveImei,
            Uid: uid,
            UserAgent: ua,
            Language: "vi");
    }

    /// <inheritdoc/>
    public async Task<ZaloLinkMetadata> ParseLinkAsync(ZaloSession session, string url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await MessageApis.ParseLinkAsync(http, session, url, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloSendResult> SendLinkAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string url, string? message = null, ZaloLinkMetadata? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await MessageApis.SendLinkAsync(http, session, threadId, threadType, url, message, metadata, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteMessageForMeAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string msgId, string cliMsgId, string ownerId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await UndoApis.DeleteMessageForMeAsync(http, session, threadId, threadType, msgId, cliMsgId, ownerId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloSendResult> ForwardMessageAsync(ZaloSession session, string targetThreadId, ZaloThreadType targetThreadType, string text, int? stickerId = null, int? stickerCateId = null, int stickerType = 7, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await MessageApis.ForwardMessageAsync(http, session, targetThreadId, targetThreadType, text, stickerId, stickerCateId, stickerType, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SendTypingAsync(ZaloSession session, string threadId, ZaloThreadType threadType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await MessageApis.SendTypingAsync(http, session, threadId, threadType, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SendSeenV2Async(ZaloSession session, string threadId, ZaloThreadType threadType, string lastMsgId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await MessageApis.SendSeenV2Async(http, session, threadId, threadType, lastMsgId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloHistoryMessage>> GetLastMessagesAsync(ZaloSession session, string[]? threadIds = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await MessageApis.GetLastMessagesAsync(http, session, threadIds, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloHistoryMessage>> GetGroupHistoryAsync(ZaloSession session, string groupId, int count = 50, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await MessageApis.GetGroupHistoryAsync(http, session, groupId, count, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloHistoryMessage>> GetCloudMessagesAsync(ZaloSession session, string convId, string globalMsgId = "0", bool isOld = false, int count = 50, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await MessageApis.GetCloudMessagesAsync(http, session, convId, globalMsgId, isOld, count, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetStickerSuggestKeywordsAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await StickerApis.GetStickerSuggestKeywordsAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloStickerItem>> SearchStickersAsync(ZaloSession session, string keyword, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await StickerApis.SearchStickersAsync(http, session, keyword, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloStickerPack>> GetStickerStorePacksAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await StickerApis.GetStickerStorePacksAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloStickerItem>> GetStickerPackItemsAsync(ZaloSession session, int cateId, string packName = "", CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await StickerApis.GetStickerPackItemsAsync(http, session, cateId, packName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetPinnedConversationsAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await ConversationApis.GetPinnedConversationsAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetPinnedConversationAsync(ZaloSession session, string threadId, ZaloThreadType threadType, bool pin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.SetPinnedConversationAsync(http, session, threadId, threadType, pin, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloMutedConversation>> GetMutedConversationsAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await ConversationApis.GetMutedConversationsAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetMuteConversationAsync(ZaloSession session, string threadId, ZaloThreadType threadType, bool mute, long durationSeconds = -1, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.SetMuteConversationAsync(http, session, threadId, threadType, mute, durationSeconds, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloUnreadMark>> GetUnreadMarksAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await ConversationApis.GetUnreadMarksAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetUnreadMarkAsync(ZaloSession session, string threadId, ZaloThreadType threadType, bool unread, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.SetUnreadMarkAsync(http, session, threadId, threadType, unread, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetHiddenConversationAsync(ZaloSession session, string threadId, ZaloThreadType threadType, bool hide, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.SetHiddenConversationAsync(http, session, threadId, threadType, hide, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteConversationHistoryAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string? lastCliMsgId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.DeleteConversationHistoryAsync(http, session, threadId, threadType, lastCliMsgId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloOnlineStatus> GetUserLastOnlineAsync(ZaloSession session, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await FriendApis.GetUserLastOnlineAsync(http, session, userId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloFriendshipStatus> GetFriendRequestStatusAsync(ZaloSession session, string targetUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await FriendApis.GetFriendRequestStatusAsync(http, session, targetUserId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RejectFriendRequestAsync(ZaloSession session, string targetUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await FriendApis.RejectFriendRequestAsync(http, session, targetUserId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UndoFriendRequestAsync(ZaloSession session, string targetUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await FriendApis.UndoFriendRequestAsync(http, session, targetUserId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloUserProfile>> GetMiniProfilesAsync(ZaloSession session, string[] userIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await FriendApis.GetMiniProfilesAsync(http, session, userIds, ct).ConfigureAwait(false);
    }

    // ─── Group 1: Advanced Group Admin & Invite Links ────────────────────────

    /// <inheritdoc/>
    public async Task DisperseGroupAsync(ZaloSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await GroupApis.DisperseGroupAsync(http, session, groupId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ChangeGroupOwnerAsync(ZaloSession session, string groupId, string newOwnerId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await GroupApis.ChangeGroupOwnerAsync(http, session, groupId, newOwnerId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloGroupLinkDetail> GetGroupLinkDetailAsync(ZaloSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await GroupApis.GetGroupLinkDetailAsync(http, session, groupId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloGroupLinkDetail> EnableGroupLinkAsync(ZaloSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await GroupApis.EnableGroupLinkAsync(http, session, groupId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DisableGroupLinkAsync(ZaloSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await GroupApis.DisableGroupLinkAsync(http, session, groupId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloGroupInfo> GetGroupLinkInfoAsync(ZaloSession session, string groupLink, int memberPage = 1, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await GroupApis.GetGroupLinkInfoAsync(http, session, groupLink, memberPage, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateGroupSettingsAsync(ZaloSession session, string groupId, ZaloGroupSettingsOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await GroupApis.UpdateGroupSettingsAsync(http, session, groupId, options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetPendingGroupMembersAsync(ZaloSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await GroupApis.GetPendingGroupMembersAsync(http, session, groupId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetGroupBlockedMembersAsync(ZaloSession session, string groupId, int page = 1, int count = 50, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await GroupApis.GetGroupBlockedMembersAsync(http, session, groupId, page, count, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AddGroupBlockedMemberAsync(ZaloSession session, string groupId, string[] memberIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await GroupApis.AddGroupBlockedMemberAsync(http, session, groupId, memberIds, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveGroupBlockedMemberAsync(ZaloSession session, string groupId, string[] memberIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await GroupApis.RemoveGroupBlockedMemberAsync(http, session, groupId, memberIds, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveGroupDeputyAsync(ZaloSession session, string groupId, string[] memberIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await GroupApis.RemoveGroupDeputyAsync(http, session, groupId, memberIds, ct).ConfigureAwait(false);
    }

    // ─── Group 2: Group Polls, Board Notes & Official Zalo Reminders ─────────

    /// <inheritdoc/>
    public async Task<ZaloPollDetail> CreatePollAsync(ZaloSession session, string groupId, ZaloPollCreateOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.CreatePollAsync(http, session, groupId, options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloPollDetail> GetPollDetailAsync(ZaloSession session, long pollId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.GetPollDetailAsync(http, session, pollId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloPollDetail> VotePollAsync(ZaloSession session, long pollId, long[] optionIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.VotePollAsync(http, session, pollId, optionIds, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task LockPollAsync(ZaloSession session, long pollId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await BoardApis.LockPollAsync(http, session, pollId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SharePollAsync(ZaloSession session, long pollId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await BoardApis.SharePollAsync(http, session, pollId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloBoardItem> CreateGroupNoteAsync(ZaloSession session, string groupId, string title, bool pinToTop = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.CreateGroupNoteAsync(http, session, groupId, title, pinToTop, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloBoardItem> EditGroupNoteAsync(ZaloSession session, string groupId, string topicId, string title, bool pinToTop = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.EditGroupNoteAsync(http, session, groupId, topicId, title, pinToTop, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloBoardItem>> GetGroupBoardListAsync(ZaloSession session, string groupId, int page = 1, int count = 20, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.GetGroupBoardListAsync(http, session, groupId, page, count, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloReminderItem> CreateReminderAsync(ZaloSession session, string threadId, bool isGroup, ZaloReminderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.CreateReminderAsync(http, session, threadId, isGroup, options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloReminderItem> EditReminderAsync(ZaloSession session, string threadId, bool isGroup, string reminderId, ZaloReminderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.EditReminderAsync(http, session, threadId, isGroup, reminderId, options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveReminderAsync(ZaloSession session, string threadId, bool isGroup, string reminderId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await BoardApis.RemoveReminderAsync(http, session, threadId, isGroup, reminderId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloReminderItem> GetReminderAsync(ZaloSession session, string reminderId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.GetReminderAsync(http, session, reminderId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloReminderItem>> GetListReminderAsync(ZaloSession session, string threadId, bool isGroup, int page = 1, int count = 20, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await BoardApis.GetListReminderAsync(http, session, threadId, isGroup, page, count, ct).ConfigureAwait(false);
    }

    // ─── Group 3: Extended Friend & Alias Management ─────────────────────────

    /// <inheritdoc/>
    public async Task RemoveFriendAsync(ZaloSession session, string friendId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await FriendApis.RemoveFriendAsync(http, session, friendId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloFriendRequestItem>> GetSentFriendRequestsAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await FriendApis.GetSentFriendRequestsAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloFriendRequestItem>> GetFriendRecommendationsAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await FriendApis.GetFriendRecommendationsAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloAliasEntry>> GetAliasListAsync(ZaloSession session, int page = 1, int count = 100, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await FriendApis.GetAliasListAsync(http, session, page, count, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveFriendAliasAsync(ZaloSession session, string friendId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await FriendApis.RemoveFriendAliasAsync(http, session, friendId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task BlockViewFeedAsync(ZaloSession session, string userId, bool isBlocked, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await FriendApis.BlockViewFeedAsync(http, session, userId, isBlocked, ct).ConfigureAwait(false);
    }

    // ─── Group 4: Conversation Labels, Quick Messages & Auto-Delete Chat ─────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloLabelItem>> GetLabelsAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await ConversationApis.GetLabelsAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloLabelItem>> UpdateLabelsAsync(ZaloSession session, IReadOnlyList<ZaloLabelItem> labels, int version = 0, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await ConversationApis.UpdateLabelsAsync(http, session, labels, version, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ZaloQuickMessage>> GetQuickMessagesAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await ConversationApis.GetQuickMessagesAsync(http, session, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloQuickMessage> AddQuickMessageAsync(ZaloSession session, string keyword, string title, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await ConversationApis.AddQuickMessageAsync(http, session, keyword, title, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ZaloQuickMessage> UpdateQuickMessageAsync(ZaloSession session, int itemId, string keyword, string title, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        return await ConversationApis.UpdateQuickMessageAsync(http, session, itemId, keyword, title, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveQuickMessageAsync(ZaloSession session, int[] itemIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.RemoveQuickMessageAsync(http, session, itemIds, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateAutoDeleteChatAsync(ZaloSession session, string threadId, bool isGroup, long ttlMs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.UpdateAutoDeleteChatAsync(http, session, threadId, isGroup, ttlMs, ct).ConfigureAwait(false);
    }

    // ─── Group 5: Account Profile, Settings & Hidden Conversation PIN ────────

    /// <inheritdoc/>
    public async Task UpdateProfileAsync(ZaloSession session, ZaloProfileUpdateOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.UpdateProfileAsync(http, session, options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateSettingsAsync(ZaloSession session, string settingKey, int value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.UpdateSettingsAsync(http, session, settingKey, value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateActiveStatusAsync(ZaloSession session, bool isActive, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.UpdateActiveStatusAsync(http, session, isActive, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateHiddenConversPinAsync(ZaloSession session, string fourDigitPin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.UpdateHiddenConversPinAsync(http, session, fourDigitPin, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ResetHiddenConversPinAsync(ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using ZaloHttpClient http = CreateHttpForSession(session, this.PrivacyOptions);
        await ConversationApis.ResetHiddenConversPinAsync(http, session, ct).ConfigureAwait(false);
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

        /// <summary>Deadline for the confirm step, extended past <see cref="ExpiresAt"/> once the QR is scanned.</summary>
        public DateTimeOffset ConfirmExpiresAt { get; set; }

        public QrSession(ZaloHttpClient http, string version, string code, string userAgent, DateTimeOffset expiresAt, Guid sessionId)
        {
            this.Http = http;
            this.Version = version;
            this.Code = code;
            this.UserAgent = userAgent;
            this.ExpiresAt = expiresAt;
            this.ConfirmExpiresAt = expiresAt;
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
