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
/// Endpoint helpers for sending sticker messages.
/// </summary>
public static class StickerApis
{
    /// <summary>Sends a Zalo sticker to a thread.</summary>
    public static async Task SendStickerAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, int stickerId, int cateId, int stickerType, ZaloThreadType type,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        bool isGroup = type == ZaloThreadType.Group;
        string endpointHost = isGroup
            ? (session.ServiceMap.TryGetValue("group", out string[]? gHosts) && gHosts.Length > 0 ? gHosts[0] : "https://tt-group-wpa.chat.zalo.me")
            : (session.ServiceMap.TryGetValue("chat", out string[]? cHosts) && cHosts.Length > 0 ? cHosts[0] : "https://tt-chat2-wpa.chat.zalo.me");

        if (!endpointHost.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            endpointHost = $"https://{endpointHost}";
        }

        string path = isGroup ? "/api/group/sticker" : "/api/message/sticker";
        string url = $"{endpointHost.TrimEnd('/')}{path}?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&nretry=0";

        JsonObject payload = new()
        {
            ["stickerId"] = stickerId,
            ["cateId"] = cateId,
            ["type"] = stickerType,
            ["clientId"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["imei"] = session.Material.Imei,
            ["zsource"] = 101
        };

        if (isGroup)
        {
            payload["grid"] = threadId;
        }
        else
        {
            payload["toid"] = threadId;
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Mã hóa tham số gửi sticker thất bại.");
        }

        Dictionary<string, string> formBody = new()
        {
            ["params"] = encryptedParams
        };

        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, new FormUrlEncodedContent(formBody), ct: ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new ZaloApiException($"Gửi sticker thất bại với mã HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }

        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = json?["error_message"]?.GetValue<string>() ?? "Không rõ nguyên nhân";
            throw new ZaloApiException($"Zalo Server báo lỗi gửi sticker ({errorCode}): {msg}");
        }

        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Message.StickerSent))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Message.StickerSent, new { TargetThreadId = threadId, TargetThreadType = type.ToString(), StickerId = stickerId });
        }
    }
}
