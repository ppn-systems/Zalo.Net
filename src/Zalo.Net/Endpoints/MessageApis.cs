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
/// Endpoint helpers for sending direct/group messages and fetching user profile information.
/// </summary>
public static class MessageApis
{
    private static string GetHost(ZaloSession session, string serviceKey, string defaultHost)
    {
        if (session.ServiceMap.TryGetValue(serviceKey, out string[]? hosts) && hosts.Length > 0)
        {
            return hosts[0].StartsWith("http", StringComparison.OrdinalIgnoreCase) ? hosts[0] : $"https://{hosts[0]}";
        }
        return defaultHost;
    }

    private static string MakeUrl(string baseUrl, string path)
    {
        string baseClean = baseUrl.EndsWith('/') ? baseUrl[..^1] : baseUrl;
        string sep = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseClean}{path}{sep}zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";
    }

    /// <summary>Sends a text message to a user or group thread.</summary>
    public static async Task<string> SendTextAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string text,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        bool isGroup = threadType == ZaloThreadType.Group;
        string host = isGroup ? GetHost(session, "group", ZaloConstants.Hosts.Group) : GetHost(session, "chat", ZaloConstants.Hosts.Chat);
        string path = isGroup ? "/api/group/sendmsg" : "/api/message/sms";
        string url = MakeUrl(host, path);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject payload = isGroup
            ? new JsonObject
            {
                ["message"] = text,
                ["clientId"] = now,
                ["grid"] = threadId,
                ["visibility"] = 0,
                ["ttl"] = 0
            }
            : new JsonObject
            {
                ["message"] = text,
                ["clientId"] = now,
                ["imei"] = session.Material.Imei,
                ["ttl"] = 0,
                ["toid"] = threadId
            };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt sendMessage payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from sendMessage");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }

        JsonNode? dataNode = node["data"];
        if (dataNode?.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            string encStr = dataNode.GetValue<string>();
            string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr);
            if (!string.IsNullOrWhiteSpace(decrypted))
            {
                try { dataNode = JsonNode.Parse(decrypted); } catch { }
            }
        }

        string msgId = dataNode?["msgId"]?.GetValue<string>()
                    ?? dataNode?["message_id"]?.GetValue<string>()
                    ?? now.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Message.TextSent))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Message.TextSent, new { TargetThreadId = threadId, TargetThreadType = threadType.ToString(), OutboundMsgId = msgId });
        }
        return msgId;
    }

    /// <summary>Sends a reply/quote message quoting an existing message.</summary>
    public static async Task<string> SendQuoteAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string text,
        string quoteMsgId, string quoteCliMsgId, string quoteSenderUid, string quoteContent,
        long quoteTs = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteMsgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteCliMsgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteSenderUid);

        bool isGroup = threadType == ZaloThreadType.Group;
        string host = isGroup ? GetHost(session, "group", ZaloConstants.Hosts.Group) : GetHost(session, "chat", ZaloConstants.Hosts.Group);
        string path = isGroup ? "/api/group/quote" : "/api/message/quote";
        string url = MakeUrl(host, path) + "&nretry=0";

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject payload = new()
        {
            ["message"] = text,
            ["clientId"] = now,
            ["qmsgOwner"] = quoteSenderUid,
            ["qmsgId"] = quoteMsgId,
            ["qmsgCliId"] = quoteCliMsgId,
            ["qmsgType"] = 1,
            ["qmsgTs"] = quoteTs > 0 ? quoteTs : now,
            ["qmsg"] = quoteContent ?? "",
            ["ttl"] = 0
        };

        if (isGroup)
        {
            payload["grid"] = threadId;
            payload["visibility"] = 0;
            payload["qmsgAttach"] = /*lang=json,strict*/ "{\"msgBubbleLayoutType\":1}";
        }
        else
        {
            payload["toid"] = threadId;
            payload["imei"] = session.Material.Imei;
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Mã hóa tham số trích dẫn tin nhắn thất bại.");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from SendQuoteAsync");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }

        JsonNode? dataNode = node["data"];
        if (dataNode?.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            string encStr = dataNode.GetValue<string>();
            string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr);
            if (!string.IsNullOrWhiteSpace(decrypted))
            {
                try { dataNode = JsonNode.Parse(decrypted); } catch { }
            }
        }

        string msgId = dataNode?["msgId"]?.GetValue<string>()
                    ?? dataNode?["message_id"]?.GetValue<string>()
                    ?? now.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Message.QuoteSent))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Message.QuoteSent, new { TargetThreadId = threadId, TargetThreadType = threadType.ToString(), OutboundMsgId = msgId, OriginalQuoteMsgId = quoteMsgId });
        }
        return msgId;
    }

    /// <summary>Fetches profile information for a user.</summary>
    public static async Task<(string Uid, string DisplayName, string? AvatarUrl)> GetUserInfoAsync(
        ZaloHttpClient http, ZaloSession session, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string host = GetHost(session, "profile", ZaloConstants.Hosts.Profile);
        string url = MakeUrl(host, "/api/social/profile/me");

        JsonObject payload = new()
        {
            ["uid"] = userId,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt getUserInfo payload");
        }

        string requestUrl = $"{url}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        if (node?["data"] is null)
        {
            throw new ZaloApiException(node?["error_message"]?.GetValue<string>() ?? "getUserInfo failed");
        }

        JsonNode? dataNode = node["data"];
        if (dataNode?.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            string encStr = dataNode.GetValue<string>();
            string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr);
            if (!string.IsNullOrWhiteSpace(decrypted))
            {
                try { dataNode = JsonNode.Parse(decrypted); } catch { }
            }
        }

        string uid = dataNode?["uid"]?.GetValue<string>() ?? userId;
        string displayName = dataNode?["displayName"]?.GetValue<string>()
                           ?? dataNode?["zaloName"]?.GetValue<string>()
                           ?? dataNode?["dpName"]?.GetValue<string>()
                           ?? dataNode?["name"]?.GetValue<string>()
                           ?? "";
        string? avatar = dataNode?["avatar"]?.GetValue<string>()
                       ?? dataNode?["avatarUrl"]?.GetValue<string>();

        return (uid, displayName, avatar);
    }
}
