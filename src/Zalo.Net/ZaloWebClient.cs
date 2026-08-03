using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Errors;
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

    private readonly Dictionary<Guid, QrSession> _qrSessions = [];
    private readonly Lock _lock = new();

#pragma warning disable CS0067
    public event EventHandler<ZaloMessageEvent>? MessageReceived;
    public event EventHandler<ZaloSessionStatusChanged>? StatusChanged;
#pragma warning restore CS0067

    public async Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct)
    {
        var ua = DefaultUserAgent;
        var http = new ZaloHttpClient(ua);

        var version = await LoginQrApis.LoadLoginPageAsync(http, ct);

        await LoginQrApis.GetLoginInfoAsync(http, version, ct);
        await LoginQrApis.VerifyClientAsync(http, version, ct);

        var qrData = await LoginQrApis.GenerateQrAsync(http, version, ct);

        var code = qrData["code"]?.GetValue<string>()
                     ?? throw new ZaloApiError("QR response missing 'code'");
        var image = qrData["image"]?.GetValue<string>()
                     ?? throw new ZaloApiError("QR response missing 'image'");

        image = Regex.Replace(image, "^data:image/png;base64,", "");

        var sessionId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var qrSession = new QrSession(http, version, code, ua, expiresAt, sessionId);
        lock (_lock)
        {
            _qrSessions[sessionId] = qrSession;
        }

        _ = Task.Run(() => RunQrLoginFlowAsync(sessionId, qrSession.Cts.Token));

        return new ZaloQrSession(sessionId, image, code, expiresAt);
    }

    public Task<ZaloLoginState> PollLoginAsync(Guid sessionId, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_qrSessions.TryGetValue(sessionId, out var session))
                return Task.FromResult(new ZaloLoginState(sessionId, ZaloLoginStatus.Expired));

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
            if (!_qrSessions.TryGetValue(sessionId, out session!)) return;
        }

        try
        {
            string? displayName = null;
            string? avatar = null;

            while (!sessionCt.IsCancellationRequested && DateTimeOffset.UtcNow < session.ExpiresAt)
            {
                JsonNode? scanResult = null;
                try
                {
                    scanResult = await LoginQrApis.WaitingScanAsync(session.Http, session.Version, session.Code, sessionCt);
                }
                catch (ZaloApiError ex) when (ex.Message.Contains("429"))
                {
                    await Task.Delay(4000, sessionCt);
                    continue;
                }

                if (scanResult is null) continue;

                var errorCode = scanResult["error_code"]?.GetValue<int>() ?? -1;
                if (errorCode == -13)
                {
                    lock (_lock) session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Declined);
                    return;
                }
                if (errorCode == 8)
                {
                    await Task.Delay(1000, sessionCt);
                    continue;
                }
                if (errorCode != 0)
                {
                    await Task.Delay(3000, sessionCt);
                    continue;
                }

                displayName = scanResult["data"]?["displayName"]?.GetValue<string>();
                avatar = scanResult["data"]?["avatar"]?.GetValue<string>();
                lock (_lock) session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Scanned, displayName, avatar);

                if (scanResult["data"]?["confirmed"]?.GetValue<bool>() == true)
                    break;

                break;
            }

            if (sessionCt.IsCancellationRequested || DateTimeOffset.UtcNow >= session.ExpiresAt)
            {
                lock (_lock) session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired);
                return;
            }

            while (!sessionCt.IsCancellationRequested && DateTimeOffset.UtcNow < session.ExpiresAt)
            {
                JsonNode? confirmResult = null;
                try
                {
                    confirmResult = await LoginQrApis.WaitingConfirmAsync(session.Http, session.Version, session.Code, sessionCt);
                }
                catch (ZaloApiError ex) when (ex.Message.Contains("429"))
                {
                    await Task.Delay(4000, sessionCt);
                    continue;
                }

                if (confirmResult is null) continue;

                var errorCode = confirmResult["error_code"]?.GetValue<int>() ?? -1;
                if (errorCode == -13)
                {
                    lock (_lock) session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Declined);
                    return;
                }
                if (errorCode == 8)
                {
                    await Task.Delay(1000, sessionCt);
                    continue;
                }
                if (errorCode != 0)
                {
                    await Task.Delay(3000, sessionCt);
                    continue;
                }

                await LoginQrApis.CheckSessionAsync(session.Http, sessionCt);

                var userInfo = await LoginQrApis.GetUserInfoAsync(session.Http, sessionCt);
                var uid = userInfo?["uid"]?.GetValue<string>()
                               ?? confirmResult["data"]?["userId"]?.GetValue<string>()
                               ?? "";

                displayName = userInfo?["displayName"]?.GetValue<string>() ?? displayName ?? "";
                avatar = userInfo?["avatar"]?.GetValue<string>() ?? avatar;

                var imei = Hashing.GenerateImei(session.UserAgent);
                var encryptor = new ParamsEncryptor(ZaloHttpClient.ApiType, imei, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var enk = encryptor.GetEncryptKey();

                var loginData = await LoginApis.GetLoginInfoAsync(session.Http, imei, "vi", enk, sessionCt);
                _ = await LoginApis.GetServerInfoAsync(session.Http, imei, sessionCt);

                var secretKey = loginData?["zpw_enk"]?.GetValue<string>()
                                 ?? throw new ZaloApiError("Missing zpw_enk in login response");

                var cookiesJson = session.Http.Cookies.ToJson();
                var material = new ZaloSessionMaterial(cookiesJson, secretKey, imei, uid, session.UserAgent);

                lock (_lock)
                {
                    session.Material = material;
                    session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Connected, displayName, avatar);
                }
                return;
            }

            if (sessionCt.IsCancellationRequested || DateTimeOffset.UtcNow >= session.ExpiresAt)
            {
                lock (_lock) session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            lock (_lock) session.CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Expired);
            StatusChanged?.Invoke(this, new ZaloSessionStatusChanged("", ZaloConnectionStatus.SessionExpired, ex.Message));
        }
    }

    public ZaloSessionMaterial? ConsumePendingMaterial(Guid sessionId)
    {
        lock (_lock)
        {
            if (_qrSessions.TryGetValue(sessionId, out var session) && session.Material != null)
            {
                var material = session.Material;
                RemoveQrSession(sessionId);
                return material;
            }
            return null;
        }
    }

    public async Task<ZaloSession> LoginWithSessionAsync(ZaloSessionMaterial material, CancellationToken ct)
    {
        var cookies = CookieStore.FromJson(material.CookiesJson);
        var http = new ZaloHttpClient(material.UserAgent, cookies);

        var loginData = await LoginApis.GetLoginInfoAsync(http, material.Imei, material.Language, material.SecretKey, ct);
        var serverData = await LoginApis.GetServerInfoAsync(http, material.Imei, ct);

        var uid = loginData?["uid"]?.GetValue<string>() ?? material.Uid;
        var secretKey = loginData?["zpw_enk"]?.GetValue<string>() ?? material.SecretKey;
        var wsUrls = ExtractWsUrls(loginData);
        var svcMap = ExtractServiceMap(loginData);
        _ = serverData;

        return new ZaloSession(
            material with { SecretKey = secretKey, Uid = uid },
            uid, wsUrls, svcMap,
            PingIntervalMs: 20_000);
    }

    private static string BuildWsUrl(string baseUrl)
    {
        var sep = baseUrl.Contains('?') ? "&" : "?";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{baseUrl}{sep}zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}&t={now}";
    }

    public Task StartListenerAsync(ZaloSession session, CancellationToken ct, Action<ZaloLogLevel, string>? log = null)
    {
        if (session.WsUrls is not { Length: > 0 })
            throw new ZaloApiError("No WebSocket URLs in session");
        var listener = new ZaloWsListener(
            session,
            msg => MessageReceived?.Invoke(this, msg),
            status => StatusChanged?.Invoke(this, status),
            log);

        var wsUrl = BuildWsUrl(session.WsUrls[0]);
        return listener.RunAsync(wsUrl, ct).ContinueWith(_ => { }, ct, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public async Task<ZaloSendResult> SendTextAsync(
        ZaloSession session, string threadId, ZaloThreadType threadType, string text, CancellationToken ct)
    {
        using var http = CreateHttpForSession(session.Material);
        var msgId = await MessageApis.SendTextAsync(http, session, threadId, threadType, text, ct);
        return new ZaloSendResult(msgId);
    }

    public async Task<ZaloSendResult> SendAttachmentAsync(
        ZaloSession session, string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName, string? caption,
        CancellationToken ct)
    {
        using var http = CreateHttpForSession(session.Material);
        return await AttachmentApis.SendImageAttachmentAsync(http, session, threadId, threadType, fileBytes, fileName, caption, ct);
    }

    public static async Task<ZaloUserProfile> GetUserInfoAsync(ZaloSession session, string userId, CancellationToken ct)
    {
        using var http = CreateHttpForSession(session.Material);
        var (uid, name, avatar) = await MessageApis.GetUserInfoAsync(http, session, userId, ct);
        return new ZaloUserProfile(uid, name, avatar);
    }

    public async Task RequestOldMessagesAsync(ZaloSession session, ZaloThreadType type,
        string? lastMsgId, CancellationToken ct)
    {
        using var http = CreateHttpForSession(session.Material);
        _ = await MessageHistoryApis.GetOldMessagesAsync(http, session, type, lastMsgId, ct);
    }

    public async Task RunWithReconnectAsync(ZaloSessionMaterial material, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(2);
        const int MaxBackoffSec = 60;

        while (!ct.IsCancellationRequested)
        {
            ZaloSession session;
            try
            {
                session = await LoginWithSessionAsync(material, ct);
            }
            catch (ZaloApiError ex) when (IsAuthError(ex))
            {
                StatusChanged?.Invoke(this, new ZaloSessionStatusChanged(material.Uid, ZaloConnectionStatus.SessionExpired, ex.Message));
                return;
            }
            catch (OperationCanceledException) { return; }

            StatusChanged?.Invoke(this, new ZaloSessionStatusChanged(session.Uid, ZaloConnectionStatus.Reconnecting));

            var disconnected = false;
            var isDuplicate = false;
            var isExpired = false;

            void OnStatus(object? _, ZaloSessionStatusChanged e)
            {
                if (e.Status == ZaloConnectionStatus.DuplicateConnection) isDuplicate = true;
                if (e.Status == ZaloConnectionStatus.SessionExpired) isExpired = true;
                if (e.Status == ZaloConnectionStatus.Disconnected) disconnected = true;
            }
            StatusChanged += OnStatus;
            try { await StartListenerAsync(session, ct); }
            catch (OperationCanceledException) { return; }
            finally { StatusChanged -= OnStatus; }

            if (isDuplicate || isExpired || ct.IsCancellationRequested) return;
            if (!disconnected) return;

            StatusChanged?.Invoke(this, new ZaloSessionStatusChanged(material.Uid, ZaloConnectionStatus.Reconnecting));
            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { return; }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoffSec));
        }
    }

    private static bool IsAuthError(ZaloApiError ex)
        => ex.Code is 4 or 2 or -101 or -102;

    private static string[] ExtractWsUrls(JsonNode? loginData)
    {
        var wsNode = loginData?["zpw_ws"];
        return wsNode is JsonArray arr
            ? arr.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToArray()
            : wsNode?.GetValue<string>() is { Length: > 0 } single ? [single] : [];
    }

    private static IReadOnlyDictionary<string, string[]> ExtractServiceMap(JsonNode? loginData)
    {
        var mapNode = loginData?["zpw_service_map_v3"];
        if (mapNode is not JsonObject obj) return new Dictionary<string, string[]>();
        var result = new Dictionary<string, string[]>();
        foreach (var (key, value) in obj)
        {
            if (value is JsonArray arr)
                result[key] = arr.Select(n => n?.GetValue<string>() ?? "").ToArray();
        }
        return result;
    }

    private static ZaloHttpClient CreateHttpForSession(ZaloSessionMaterial m)
        => new(m.UserAgent, CookieStore.FromJson(m.CookiesJson));

    private void RemoveQrSession(Guid sessionId)
    {
        lock (_lock)
        {
            if (_qrSessions.TryGetValue(sessionId, out var s))
            {
                s.Dispose();
                _ = _qrSessions.Remove(sessionId);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var s in _qrSessions.Values) s.Dispose();
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
            Http = http;
            Version = version;
            Code = code;
            UserAgent = userAgent;
            ExpiresAt = expiresAt;
            CurrentState = new ZaloLoginState(sessionId, ZaloLoginStatus.Pending);
            Cts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            Cts.Cancel();
            Cts.Dispose();
            Http.Dispose();
        }
    }
}
