// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
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
    private const string DefaultUserAgent = ZaloConstants.Protocol.DefaultUserAgent;

    private readonly ZaloSession _session;
    private readonly Action<ZaloMessageEvent> _onMessage;
    private readonly Action<ZaloSessionStatusChanged> _onStatus;

    /// <summary>Gets or sets the send throttle function.</summary>
    public Func<CancellationToken, Task>? SendThrottle { get; set; }

    private const int InitialBufferSize = 4 * 1024;
    private const int CloseCodeDuplicate = ZaloConstants.WebSocket.CloseCodeDuplicate;
    private const int CloseCodeKicked = ZaloConstants.WebSocket.CloseCodeKicked;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloWsListener"/> class.
    /// </summary>
    public ZaloWsListener(
        ZaloSession session,
        Action<ZaloMessageEvent> onMessage,
        Action<ZaloSessionStatusChanged> onStatus)
    {
        _session = session;
        _onMessage = onMessage;
        _onStatus = onStatus;
    }

    /// <summary>Disconnect reason enum.</summary>
    public enum DisconnectReason
    {
        /// <summary>Clean disconnection.</summary>
        Clean,
        /// <summary>Transient network disconnection.</summary>
        Transient,
        /// <summary>Duplicate connection login elsewhere.</summary>
        Duplicate,
        /// <summary>Session expired.</summary>
        SessionExpired
    }

    /// <summary>Runs the WebSocket receive loop.</summary>
    public async Task<DisconnectReason> RunAsync(string wsUrl, CancellationToken ct)
    {
        using ClientWebSocket ws = new();
        this.ConfigureWs(ws);

        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
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
        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.WebSocket.Connected))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.WebSocket.Connected, new { SessionUid = _session.Uid });
        }

        using CancellationTokenSource pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task pingTask = this.PingLoopAsync(ws, _session.PingIntervalMs, pingCts.Token);

        DisconnectReason reason;
        try
        {
            reason = await this.ReceiveLoopAsync(ws, ct).ConfigureAwait(false);
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
            await pingCts.CancelAsync().ConfigureAwait(false);
            try { await pingTask.ConfigureAwait(false); } catch { /* ignore */ }

            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false); }
                catch { /* ignore */ }
            }
        }

        ZaloConnectionStatus finalStatus = reason switch
        {
            DisconnectReason.Clean => ZaloConnectionStatus.Disconnected,
            DisconnectReason.Transient => ZaloConnectionStatus.Disconnected,
            DisconnectReason.Duplicate => ZaloConnectionStatus.DuplicateConnection,
            DisconnectReason.SessionExpired => ZaloConnectionStatus.SessionExpired,
            _ => ZaloConnectionStatus.Disconnected,
        };
        _onStatus(new ZaloSessionStatusChanged(_session.Uid, finalStatus));
        return reason;
    }

    private void ConfigureWs(ClientWebSocket ws)
    {
        string ua = string.IsNullOrEmpty(_session.Material.UserAgent)
            ? DefaultUserAgent
            : _session.Material.UserAgent;
        ws.Options.SetRequestHeader("User-Agent", ua);
        ws.Options.SetRequestHeader("Cookie", this.ExtractCookieHeader());
        ws.Options.SetRequestHeader("Origin", "https://chat.zalo.me");
        ws.Options.SetRequestHeader("Accept-Language", "vi-VN,vi;q=0.9");

        if (_session.Proxy != null)
        {
            ws.Options.Proxy = _session.Proxy;
        }
    }

    private string ExtractCookieHeader()
    {
        if (string.IsNullOrEmpty(_session.Material.CookiesJson))
        {
            return "";
        }
        try
        {
            CookieStore store = CookieStore.FromJson(_session.Material.CookiesJson);
            return store.GetCookieHeader("https://chat.zalo.me");
        }
        catch { return ""; }
    }

    internal sealed class CipherState { public string? Key; }

    private async Task<DisconnectReason> ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        CipherState state = new();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                (byte[] frameBytes, WebSocketMessageType msgType) = await ReceiveFullFrameAsync(ws, buffer, ct).ConfigureAwait(false);

                if (msgType == WebSocketMessageType.Close)
                {
                    return InterpretCloseCode(ws);
                }

                if (msgType != WebSocketMessageType.Binary || frameBytes.Length < 4)
                {
                    continue;
                }

                await this.DispatchFrameAsync(frameBytes, state, ct).ConfigureAwait(false);
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
        int? code = (int?)ws.CloseStatus;
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
        List<byte[]> segments = [];
        int total = 0;

        while (true)
        {
            WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
            segments.Add([.. buf[..result.Count]]);
            total += result.Count;

            if (result.EndOfMessage)
            {
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return ([], WebSocketMessageType.Close);
                }
                if (segments.Count == 1)
                {
                    return (segments[0], result.MessageType);
                }
                byte[] out_ = new byte[total];
                int off = 0;
                foreach (byte[] s in segments)
                {
                    s.CopyTo(out_, off);
                    off += s.Length;
                }
                return (out_, result.MessageType);
            }

            buf = ArrayPool<byte>.Shared.Rent((total * 2) + InitialBufferSize);
        }
    }

    internal async Task DispatchFrameAsync(byte[] frameBytes, CipherState state, CancellationToken ct)
    {
        (_, int cmd, byte subCmd) = WsFrameCodec.ParseHeader(frameBytes.AsSpan());
        ReadOnlyMemory<byte> body = new(frameBytes, 4, frameBytes.Length - 4);

        switch (cmd)
        {
            case 1 when subCmd == 1:
                state.Key = await ExtractCipherKeyAsync(body).ConfigureAwait(false);
                break;

            case 501:
            case 510:
                try
                {
                    JsonNode? payload = await WsFrameCodec.DecodeFrameBodyAsync(body, state.Key, ct).ConfigureAwait(false);
                    if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.WebSocket.FrameDecoded))
                    {
                        ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.WebSocket.FrameDecoded, new { Cmd = cmd, SubCmd = subCmd, FrameLength = body.Length });
                    }
                    this.DispatchMessages(payload, ZaloThreadType.User);
                }
                catch (Exception ex)
                {
                    if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.WebSocket.FrameError))
                    {
                        ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.WebSocket.FrameError, new { Cmd = cmd, SubCmd = subCmd, Error = ex.Message });
                    }
                }
                break;

            case 521:
            case 511:
                try
                {
                    JsonNode? payload = await WsFrameCodec.DecodeFrameBodyAsync(body, state.Key, ct).ConfigureAwait(false);
                    if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.WebSocket.FrameDecoded))
                    {
                        ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.WebSocket.FrameDecoded, new { Cmd = cmd, SubCmd = subCmd, FrameLength = body.Length });
                    }
                    this.DispatchMessages(payload, ZaloThreadType.Group);
                }
                catch (Exception ex)
                {
                    if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.WebSocket.FrameError))
                    {
                        ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.WebSocket.FrameError, new { Cmd = cmd, SubCmd = subCmd, Error = ex.Message });
                    }
                }
                break;

            case 601:
                try
                {
                    JsonNode? payload = await WsFrameCodec.DecodeFrameBodyAsync(body, state.Key, ct).ConfigureAwait(false);
                    this.DispatchControls(payload);
                }
                catch (Exception ex)
                {
                    if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.WebSocket.FrameError))
                    {
                        ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.WebSocket.FrameError, new { Cmd = cmd, SubCmd = subCmd, Error = ex.Message });
                    }
                }
                break;

            default:
                break;
        }
    }

    private static string? GetStringSafe(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        if (node is JsonValue val)
        {
            return val.ToString();
        }
        return node.GetValue<string>();
    }

    private void DispatchControls(JsonNode? payload)
    {
        if (payload is null)
        {
            return;
        }
        JsonNode? data = payload["data"];
        if (data?["controls"] is JsonArray controls)
        {
            foreach (JsonNode? control in controls)
            {
                JsonNode? content = control?["content"];
                string? actType = GetStringSafe(content?["act_type"]);
                if (actType == "file_done")
                {
                    string? fileId = GetStringSafe(content?["fileId"])
                                  ?? GetStringSafe(content?["file_id"])
                                  ?? GetStringSafe(content?["data"]?["fileId"]);

                    string? fileUrl = GetStringSafe(content?["data"]?["url"])
                                   ?? GetStringSafe(content?["fileUrl"])
                                   ?? GetStringSafe(content?["url"]);

                    if (!string.IsNullOrEmpty(fileId) && !string.IsNullOrEmpty(fileUrl))
                    {
                        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Internal.Information))
                        {
                            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Internal.Information, new { FileId = fileId, FileUrl = fileUrl });
                        }
                        ZaloFileDoneRegistry.Set(fileId, fileUrl);
                    }
                }
            }
        }
    }



    private static Task<string?> ExtractCipherKeyAsync(ReadOnlyMemory<byte> body)
    {
        if (body.Length == 0)
        {
            return Task.FromResult<string?>(null);
        }
        string text = Encoding.UTF8.GetString(body.Span);
        try
        {
            JsonNode? node = JsonNode.Parse(text);
            string? key = node?["key"]?.GetValue<string>()
                ?? node?["cipherKey"]?.GetValue<string>();
            return Task.FromResult(key);
        }
        catch { return Task.FromResult<string?>(text.Trim()); }
    }

    private void DispatchMessages(JsonNode? payload, ZaloThreadType threadType)
    {
        if (payload is null)
        {
            return;
        }

        JsonNode? data = payload["data"];
        string arrName = threadType == ZaloThreadType.Group ? "groupMsgs" : "msgs";
        if (data?[arrName] is not JsonArray msgs)
        {
            ZaloMessageEvent? single = data is JsonObject ? this.ParseMessageEvent(data, threadType) : null;
            if (single is not null)
            {
                _onMessage(single);
            }
            return;
        }

        foreach (JsonNode? msg in msgs)
        {
            if (msg is null)
            {
                continue;
            }
            ZaloMessageEvent? evt = this.ParseMessageEvent(msg, threadType);
            if (evt is not null)
            {
                _onMessage(evt);
            }
        }
    }

    private ZaloMessageEvent? ParseMessageEvent(JsonNode msg, ZaloThreadType threadType)
    {
        string? msgId = msg["msgId"]?.ToJsonString()?.Trim('"');
        string? cliMsgId = msg["cliMsgId"]?.ToJsonString()?.Trim('"');
        if (msgId is null)
        {
            return null;
        }

        string uidFrom = msg["uidFrom"]?.GetValue<string>() ?? "";
        string idTo = msg["idTo"]?.GetValue<string>() ?? msg["toUid"]?.GetValue<string>() ?? "";

        string threadId = threadType == ZaloThreadType.Group
            ? (msg["threadId"]?.GetValue<string>() ?? idTo)
            : (string.IsNullOrEmpty(uidFrom) || uidFrom == "0" || uidFrom == _session.Uid ? idTo : uidFrom);

        string? isSelfRaw = msg["isSelf"]?.ToJsonString();
        bool isSelf = isSelfRaw == "1" || isSelfRaw == "true"
                     || uidFrom == "0"
                     || (!string.IsNullOrEmpty(_session.Uid) && uidFrom == _session.Uid);

        string msgType = msg["msgType"]?.GetValue<string>() ?? "";
        JsonNode? content = msg["content"];
        List<ZaloAttachment>? attachments = null;

        if (content is JsonObject contentObj &&
            (msgType == "chat.photo" || msgType == "chat.gif" || msgType == "chat.video.msg" || msgType == "share.file" || msgType == "chat.voice"))
        {
            string? url = contentObj["href"]?.GetValue<string>() ?? contentObj["url"]?.GetValue<string>() ?? contentObj["thumb"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(url))
            {
                string fileName = contentObj["title"]?.GetValue<string>() ?? contentObj["description"]?.GetValue<string>() ?? "attachment";
                attachments = [new ZaloAttachment(url, fileName, msgType)];
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
            IsSelf: isSelf,
            RawJson: msg.ToJsonString());
    }

    private async Task PingLoopAsync(ClientWebSocket ws, int intervalMs, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, ct).ConfigureAwait(false);
                if (ws.State != WebSocketState.Open)
                {
                    break;
                }
                if (this.SendThrottle is not null)
                {
                    await this.SendThrottle(ct).ConfigureAwait(false);
                }
                ReadOnlyMemory<byte> frame = new byte[] { 0x01, 0x00, 0x00, 0x00 };
                await ws.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* expected */ }
    }
}
