using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Cryptography;

namespace Zalo.Net.WebSocket;

/// <summary>
/// Maintains a WebSocket connection to a Zalo WS endpoint and dispatches inbound events.
/// </summary>
public sealed class ZaloWsListener
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    private readonly ZaloSession _session;
    private readonly Action<ZaloMessageEvent> _onMessage;
    private readonly Action<ZaloSessionStatusChanged> _onStatus;
    private readonly Action<ZaloLogLevel, string>? _log;

    public Func<CancellationToken, Task>? SendThrottle { get; set; }

    private const int InitialBufferSize = 4 * 1024;
    private const int CloseCodeDuplicate = 3000;
    private const int CloseCodeKicked = 3003;

    public ZaloWsListener(
        ZaloSession session,
        Action<ZaloMessageEvent> onMessage,
        Action<ZaloSessionStatusChanged> onStatus,
        Action<ZaloLogLevel, string>? log = null)
    {
        _session = session;
        _onMessage = onMessage;
        _onStatus = onStatus;
        _log = log;
    }

    public enum DisconnectReason { Clean, Transient, Duplicate, SessionExpired }

    public async Task<DisconnectReason> RunAsync(string wsUrl, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ConfigureWs(ws);

        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), ct);
        }
        catch (OperationCanceledException)
        {
            return DisconnectReason.Clean;
        }
        catch (Exception ex)
        {
            _onStatus(new ZaloSessionStatusChanged(_session.Uid, ZaloConnectionStatus.Disconnected, ex.Message));
            return DisconnectReason.Transient;
        }

        _onStatus(new ZaloSessionStatusChanged(_session.Uid, ZaloConnectionStatus.Connected));
        _log?.Invoke(ZaloLogLevel.Information, $"Zalo WS connected: uid={_session.Uid}");

        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pingTask = PingLoopAsync(ws, _session.PingIntervalMs, pingCts.Token);

        DisconnectReason reason;
        try
        {
            reason = await ReceiveLoopAsync(ws, ct);
        }
        catch (OperationCanceledException)
        {
            reason = DisconnectReason.Clean;
        }
        catch (WebSocketException ex)
        {
            reason = DisconnectReason.Transient;
            _onStatus(new ZaloSessionStatusChanged(_session.Uid, ZaloConnectionStatus.Disconnected, ex.Message));
        }
        finally
        {
            await pingCts.CancelAsync();
            try { await pingTask; } catch { /* ignore */ }

            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
                catch { /* ignore */ }
            }
        }

        var finalStatus = reason switch
        {
            DisconnectReason.Duplicate => ZaloConnectionStatus.DuplicateConnection,
            DisconnectReason.SessionExpired => ZaloConnectionStatus.SessionExpired,
            DisconnectReason.Clean => throw new NotImplementedException(),
            DisconnectReason.Transient => throw new NotImplementedException(),
            _ => ZaloConnectionStatus.Disconnected,
        };
        _onStatus(new ZaloSessionStatusChanged(_session.Uid, finalStatus));
        return reason;
    }

    private void ConfigureWs(ClientWebSocket ws)
    {
        var ua = string.IsNullOrEmpty(_session.Material.UserAgent)
            ? DefaultUserAgent
            : _session.Material.UserAgent;
        ws.Options.SetRequestHeader("User-Agent", ua);
        ws.Options.SetRequestHeader("Cookie", ExtractCookieHeader());
        ws.Options.SetRequestHeader("Origin", "https://chat.zalo.me");
        ws.Options.SetRequestHeader("Accept-Language", "vi-VN,vi;q=0.9");
    }

    private string ExtractCookieHeader()
    {
        if (string.IsNullOrEmpty(_session.Material.CookiesJson)) return "";
        try
        {
            var store = CookieStore.FromJson(_session.Material.CookiesJson);
            return store.GetCookieHeader("https://chat.zalo.me");
        }
        catch { return ""; }
    }

    internal sealed class CipherState { public string? Key; }

    private async Task<DisconnectReason> ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var state = new CipherState();
        var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var (frameBytes, msgType) = await ReceiveFullFrameAsync(ws, buffer, ct);

                if (msgType == WebSocketMessageType.Close)
                    return InterpretCloseCode(ws);

                if (msgType != WebSocketMessageType.Binary || frameBytes.Length < 4) continue;

                await DispatchFrameAsync(frameBytes, state, ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return ct.IsCancellationRequested ? DisconnectReason.Clean : DisconnectReason.Transient;
    }

    private static DisconnectReason InterpretCloseCode(ClientWebSocket ws)
    {
        var code = (int?)ws.CloseStatus;
        return code switch
        {
            (int)WebSocketCloseStatus.NormalClosure => DisconnectReason.Clean,
            CloseCodeDuplicate => DisconnectReason.Duplicate,
            CloseCodeKicked => DisconnectReason.SessionExpired,
            _ => DisconnectReason.Transient,
        };
    }

    private static async Task<(byte[] Bytes, WebSocketMessageType Type)> ReceiveFullFrameAsync(
        ClientWebSocket ws, byte[] buf, CancellationToken ct)
    {
        var segments = new List<byte[]>();
        var total = 0;

        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
            segments.Add([.. buf[..result.Count]]);
            total += result.Count;

            if (result.EndOfMessage)
            {
                if (result.MessageType == WebSocketMessageType.Close)
                    return ([], WebSocketMessageType.Close);
                if (segments.Count == 1) return (segments[0], result.MessageType);
                var out_ = new byte[total];
                var off = 0;
                foreach (var s in segments) { s.CopyTo(out_, off); off += s.Length; }
                return (out_, result.MessageType);
            }

            buf = ArrayPool<byte>.Shared.Rent((total * 2) + InitialBufferSize);
        }
    }

    internal async Task DispatchFrameAsync(byte[] frameBytes, CipherState state, CancellationToken ct)
    {
        var (_, cmd, subCmd) = WsFrameCodec.ParseHeader(frameBytes.AsSpan());
        var body = new ReadOnlyMemory<byte>(frameBytes, 4, frameBytes.Length - 4);

        switch (cmd)
        {
            case 1 when subCmd == 1:
                _log?.Invoke(ZaloLogLevel.Debug, $"Zalo WS frame: cmd={cmd} subCmd={subCmd} thread=none");
                state.Key = ExtractCipherKey(body);
                _log?.Invoke(ZaloLogLevel.Debug, $"Zalo WS cipherKey set: {(state.Key is null ? "NULL" : $"len={state.Key.Length}")}");
                break;

            case 501:
            case 510:
                try
                {
                    var payload = await WsFrameCodec.DecodeFrameBodyAsync(body, state.Key, ct);
                    _log?.Invoke(ZaloLogLevel.Debug, $"Zalo WS frame: cmd={cmd} subCmd={subCmd} thread={ExtractThreadIdForLog(payload, ZaloThreadType.User)}");
                    DispatchMessages(payload, ZaloThreadType.User);
                }
                catch (Exception ex)
                {
                    _log?.Invoke(ZaloLogLevel.Warning, $"Zalo WS decode FAILED: cmd={cmd} subCmd={subCmd} err={ex.GetType().Name}:{ex.Message}");
                }
                break;

            case 521:
            case 511:
                try
                {
                    var payload = await WsFrameCodec.DecodeFrameBodyAsync(body, state.Key, ct);
                    _log?.Invoke(ZaloLogLevel.Debug, $"Zalo WS frame: cmd={cmd} subCmd={subCmd} thread={ExtractThreadIdForLog(payload, ZaloThreadType.Group)}");
                    DispatchMessages(payload, ZaloThreadType.Group);
                }
                catch (Exception ex)
                {
                    _log?.Invoke(ZaloLogLevel.Warning, $"Zalo WS decode FAILED: cmd={cmd} subCmd={subCmd} err={ex.GetType().Name}:{ex.Message}");
                }
                break;

            default:
                _log?.Invoke(ZaloLogLevel.Debug, $"Zalo WS frame (unhandled): cmd={cmd} subCmd={subCmd} len={body.Length}");
                break;
        }
    }

    private string ExtractThreadIdForLog(JsonNode? payload, ZaloThreadType type)
    {
        if (payload is null) return "unknown";
        var data = payload["data"];
        var arrName = type == ZaloThreadType.Group ? "groupMsgs" : "msgs";
        var msgs = data?[arrName] as JsonArray;
        var firstMsg = msgs?.FirstOrDefault() ?? data;
        if (firstMsg is null) return "unknown";

        var evt = ParseMessageEvent(firstMsg, type);
        return evt?.ThreadId ?? "unknown";
    }

    private static string? ExtractCipherKey(ReadOnlyMemory<byte> body)
    {
        if (body.Length == 0) return null;
        var text = Encoding.UTF8.GetString(body.Span);
        try
        {
            var node = JsonNode.Parse(text);
            return node?["key"]?.GetValue<string>()
                ?? node?["cipherKey"]?.GetValue<string>();
        }
        catch { return text.Trim(); }
    }

    private void DispatchMessages(JsonNode? payload, ZaloThreadType threadType)
    {
        if (payload is null) return;

        var data = payload["data"];
        var arrName = threadType == ZaloThreadType.Group ? "groupMsgs" : "msgs";

        if (data?[arrName] is not JsonArray msgs)
        {
            var single = data is JsonObject ? ParseMessageEvent(data, threadType) : null;
            if (single is not null) _onMessage(single);
            return;
        }

        foreach (var msg in msgs)
        {
            if (msg is null) continue;
            var evt = ParseMessageEvent(msg, threadType);
            if (evt is not null) _onMessage(evt);
        }
    }

    private ZaloMessageEvent? ParseMessageEvent(JsonNode msg, ZaloThreadType threadType)
    {
        var msgId = msg["msgId"]?.ToJsonString()?.Trim('"');
        var cliMsgId = msg["cliMsgId"]?.ToJsonString()?.Trim('"');
        if (msgId is null) return null;

        var uidFrom = msg["uidFrom"]?.GetValue<string>() ?? "";
        var idTo = msg["idTo"]?.GetValue<string>() ?? msg["toUid"]?.GetValue<string>() ?? "";

        var threadId = threadType == ZaloThreadType.Group
            ? (msg["threadId"]?.GetValue<string>() ?? idTo)
            : (string.IsNullOrEmpty(uidFrom) || uidFrom == "0" || uidFrom == _session.Uid ? idTo : uidFrom);

        var isSelfRaw = msg["isSelf"]?.ToJsonString();
        var isSelf = isSelfRaw == "1" || isSelfRaw == "true"
                     || uidFrom == "0"
                     || (!string.IsNullOrEmpty(_session.Uid) && uidFrom == _session.Uid);

        var msgType = msg["msgType"]?.GetValue<string>() ?? "";
        var content = msg["content"];
        List<ZaloAttachment>? attachments = null;

        if (content is JsonObject contentObj &&
            (msgType == "chat.photo" || msgType == "chat.gif" || msgType == "chat.video.msg" || msgType == "share.file" || msgType == "chat.voice"))
        {
            var url = contentObj["href"]?.GetValue<string>() ?? contentObj["url"]?.GetValue<string>() ?? contentObj["thumb"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(url))
            {
                var fileName = contentObj["title"]?.GetValue<string>() ?? contentObj["description"]?.GetValue<string>() ?? "attachment";
                attachments = new List<ZaloAttachment> { new ZaloAttachment(url, fileName, msgType) };
            }
        }

        return new ZaloMessageEvent(
            MsgId: msgId,
            CliMsgId: cliMsgId ?? "",
            MsgType: msgType,
            UidFrom: uidFrom,
            IdTo: idTo,
            DisplayName: msg["dName"]?.GetValue<string>() ?? "",
            ThreadId: threadId,
            ThreadType: threadType,
            TimestampMs: msg["ts"]?.ToJsonString()?.Trim('"') ?? "",
            Content: content,
            Attachments: attachments,
            IsSelf: isSelf);
    }

    private async Task PingLoopAsync(ClientWebSocket ws, int intervalMs, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, ct);
                if (ws.State != WebSocketState.Open) break;
                if (SendThrottle is not null) await SendThrottle(ct);
                ReadOnlyMemory<byte> frame = new byte[] { 0x01, 0x00, 0x00, 0x00 };
                await ws.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, ct);
            }
        }
        catch (OperationCanceledException) { /* expected */ }
    }
}
