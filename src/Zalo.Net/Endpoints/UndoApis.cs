// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Endpoint helpers for un-sending (recalling) messages.
/// </summary>
public static class UndoApis
{
    /// <summary>Recalls/undos a previously sent message.</summary>
    public static async Task UndoMessageAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, string msgId, string cliMsgId, ZaloThreadType type,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(msgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliMsgId);

        bool isGroup = type == ZaloThreadType.Group;
        string endpointHost = isGroup
            ? (session.ServiceMap.TryGetValue("group", out string[]? gHosts) && gHosts.Length > 0 ? gHosts[0] : ZaloConstants.Hosts.Group)
            : (session.ServiceMap.TryGetValue("chat", out string[]? cHosts) && cHosts.Length > 0 ? cHosts[0] : ZaloConstants.Hosts.Chat);

        if (!endpointHost.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            endpointHost = $"https://{endpointHost}";
        }

        string path = isGroup ? "/api/group/undomsg" : "/api/message/undo";
        string url = $"{endpointHost.TrimEnd('/')}{path}?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        JsonObject payload = new()
        {
            ["msgId"] = msgId,
            ["clientId"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["cliMsgIdUndo"] = cliMsgId
        };

        if (isGroup)
        {
            payload["grid"] = CleanGroupId(threadId);
            payload["visibility"] = 0;
            payload["imei"] = session.Material.Imei;
        }
        else
        {
            payload["toid"] = threadId;
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Mã hóa tham số thu hồi tin nhắn thất bại.");
        }

        Dictionary<string, string> formBody = new()
        {
            ["params"] = encryptedParams
        };

        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, new FormUrlEncodedContent(formBody), ct: ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new ZaloApiException($"Thu hồi tin nhắn thất bại với mã HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }

        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = root?["error_message"]?.GetValue<string>() ?? json?["error_message"]?.GetValue<string>() ?? "Không rõ nguyên nhân";
            throw new ZaloApiException($"Zalo Server báo lỗi thu hồi tin nhắn ({errorCode}): {msg}");
        }

        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Message.UndoSent))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Message.UndoSent, new { TargetThreadId = threadId, TargetThreadType = type.ToString(), RecalledMsgId = msgId });
        }
    }

    /// <summary>
    /// Deletes a message for the current user only (<c>POST /api/message/delete</c> or <c>/api/group/deletemsg</c>, cmd 11779 / 11834).
    /// </summary>
    public static async Task DeleteMessageForMeAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType type, string msgId, string cliMsgId, string ownerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(msgId);

        bool isGroup = type == ZaloThreadType.Group;
        string serviceKey = isGroup ? "group" : "chat";
        string primaryHost = isGroup
            ? (session.ServiceMap.TryGetValue("group", out string[]? gHosts) && gHosts.Length > 0 ? gHosts[0] : ZaloConstants.Hosts.Group)
            : (session.ServiceMap.TryGetValue("chat", out string[]? cHosts) && cHosts.Length > 0 ? cHosts[0] : ZaloConstants.Hosts.Chat);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string effectiveCliId = string.IsNullOrWhiteSpace(cliMsgId) ? now.ToString(System.Globalization.CultureInfo.InvariantCulture) : cliMsgId;
        string effectiveOwner = string.IsNullOrWhiteSpace(ownerId) ? session.Uid : ownerId;
        string cleanTarget = isGroup ? CleanGroupId(threadId) : threadId;

        JsonArray msgsArr =
        [
            new JsonObject
            {
                ["cliMsgId"] = effectiveCliId,
                ["globalMsgId"] = msgId,
                ["ownerId"] = effectiveOwner,
                ["destId"] = cleanTarget,
            }
        ];

        JsonObject payload = new()
        {
            ["cliMsgId"] = now,
            ["msgs"] = msgsArr,
            ["onlyMe"] = 1,
        };

        if (isGroup)
        {
            payload["grid"] = cleanTarget;
        }
        else
        {
            payload["toid"] = cleanTarget;
            payload["imei"] = session.Material.Imei;
        }

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = isGroup
            ? $"/api/group/deletemsg?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}"
            : $"/api/message/delete?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            serviceKey,
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);

        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? json?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            string msg = root?["error_message"]?.GetValue<string>() ?? json?["error_message"]?.GetValue<string>() ?? "Không rõ nguyên nhân";
            throw new ZaloApiException($"Xóa tin nhắn phía tôi thất bại ({errorCode}): {msg}", errorCode);
        }
    }

    /// <summary>Strips optional <c>group_</c> or <c>g</c> prefixes from a group ID.</summary>
    public static string CleanGroupId(string threadId)
    {
        ArgumentNullException.ThrowIfNull(threadId);
        string s = threadId.Trim();
        if (s.StartsWith("group_", StringComparison.OrdinalIgnoreCase))
        {
            s = s[6..];
        }
        else if (s.Length > 1 && (s[0] == 'g' || s[0] == 'G') && char.IsDigit(s[1]))
        {
            s = s[1..];
        }
        return s;
    }
}
