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
            ? (session.ServiceMap.TryGetValue("group", out string[]? gHosts) && gHosts.Length > 0 ? gHosts[0] : "https://tt-group-wpa.chat.zalo.me")
            : (session.ServiceMap.TryGetValue("chat", out string[]? cHosts) && cHosts.Length > 0 ? cHosts[0] : "https://tt-chat2-wpa.chat.zalo.me");

        if (!endpointHost.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            endpointHost = $"https://{endpointHost}";
        }

        string path = isGroup ? "/api/group/undomsg" : "/api/message/undo";
        string url = $"{endpointHost.TrimEnd('/')}{path}?zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}";

        JsonObject payload = new()
        {
            ["msgId"] = msgId,
            ["clientId"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["cliMsgIdUndo"] = cliMsgId
        };

        if (isGroup)
        {
            payload["grid"] = threadId;
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
        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = json?["error_message"]?.GetValue<string>() ?? "Không rõ nguyên nhân";
            throw new ZaloApiException($"Zalo Server báo lỗi thu hồi tin nhắn ({errorCode}): {msg}");
        }

        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Message.UndoSent))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Message.UndoSent, new { TargetThreadId = threadId, TargetThreadType = type.ToString(), RecalledMsgId = msgId });
        }
    }
}
