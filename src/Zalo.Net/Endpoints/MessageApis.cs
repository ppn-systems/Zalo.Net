// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    private static string GetChatHost(ZaloSession session) => GetHost(session, "chat", ZaloConstants.Hosts.Chat);

    private static string GetGroupHost(ZaloSession session) => GetHost(session, "group", ZaloConstants.Hosts.Group);

    private static string MakeUrl(string baseUrl, string path)
    {
        string baseClean = baseUrl.EndsWith('/') ? baseUrl[..^1] : baseUrl;
        string sep = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseClean}{path}{sep}zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";
    }

    /// <summary>Sends a text message to a user or group thread. Returns the server msgId and the client-generated cliMsgId (for de-duplication).</summary>
    public static async Task<(string MsgId, string CliMsgId)> SendTextAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string text,
        CancellationToken ct,
        IReadOnlyList<ZaloMention>? mentions = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        bool isGroup = threadType == ZaloThreadType.Group;
        string serviceKey = isGroup ? "group" : "chat";
        string host = isGroup ? GetHost(session, "group", ZaloConstants.Hosts.Group) : GetHost(session, "chat", ZaloConstants.Hosts.Chat);
        string path = isGroup ? "/api/group/sendmsg" : "/api/message/sms";
        string pathAndQuery = $"{path}?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject payload = isGroup
            ? new JsonObject
            {
                ["message"] = text,
                ["clientId"] = now,
                ["grid"] = UndoApis.CleanGroupId(threadId),
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

        if (isGroup)
        {
            string? mentionJson = BuildMentionInfoJson(text, mentions);
            if (!string.IsNullOrEmpty(mentionJson))
            {
                payload["mentionInfo"] = mentionJson;
            }
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt sendMessage payload");
        }

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            serviceKey,
            host,
            pathAndQuery,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);
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
        return (msgId, now.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Builds the Zalo <c>mentionInfo</c> JSON array string from explicit mentions or automatic <c>@All</c> detection.</summary>
    public static string? BuildMentionInfoJson(string text, IReadOnlyList<ZaloMention>? mentions = null)
    {
        JsonArray arr = [];
        if (mentions is { Count: > 0 })
        {
            foreach (ZaloMention m in mentions)
            {
                bool isAll = m.IsMentionAll || string.Equals(m.Uid, ZaloConstants.Protocol.MentionAllUid, StringComparison.Ordinal);
                arr.Add((JsonNode)new JsonObject
                {
                    ["pos"] = m.Position,
                    ["len"] = m.Length,
                    ["uid"] = isAll ? ZaloConstants.Protocol.MentionAllUid : m.Uid,
                    ["type"] = isAll ? 1 : 0,
                });
            }
            return arr.ToJsonString();
        }

        if (!string.IsNullOrEmpty(text))
        {
            int idx = text.IndexOf("@All", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                arr.Add((JsonNode)new JsonObject
                {
                    ["pos"] = idx,
                    ["len"] = 4,
                    ["uid"] = ZaloConstants.Protocol.MentionAllUid,
                    ["type"] = 1,
                });
                return arr.ToJsonString();
            }
        }

        return null;
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
        string host = isGroup ? GetHost(session, "group", ZaloConstants.Hosts.Group) : GetHost(session, "chat", ZaloConstants.Hosts.Chat);
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

    /// <summary>Sends an image/photo to a user or group thread.</summary>
    public static async Task<string> SendPhotoAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType,
        byte[] imageBytes, string fileName, string? caption,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(imageBytes);

        bool isGroup = threadType == ZaloThreadType.Group;
        string host = isGroup ? GetHost(session, "group", ZaloConstants.Hosts.Group) : GetHost(session, "chat", ZaloConstants.Hosts.Chat);
        string path = isGroup ? "/api/group/photo" : "/api/message/photo";
        string url = MakeUrl(host, path);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject payload = new()
        {
            ["clientId"] = now,
            ["caption"] = caption ?? "",
            ["ttl"] = 0
        };

        if (isGroup)
        {
            payload["grid"] = threadId;
            payload["visibility"] = 0;
        }
        else
        {
            payload["toid"] = threadId;
            payload["imei"] = session.Material.Imei;
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Mã hóa tham số gửi ảnh thất bại.");
        }

        using MultipartFormDataContent formData = new();
        formData.Add(new StringContent(encryptedParams), "params");

        ByteArrayContent fileContent = new(imageBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        formData.Add(fileContent, "chunkContent", fileName);

        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: formData, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from SendPhotoAsync");

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

        if (dataNode?["error_code"]?.GetValue<int>() is int innerCode && innerCode != 0)
        {
            string innerMsg = dataNode["error_message"]?.GetValue<string>() ?? $"Inner photo error {innerCode}";
            throw new ZaloApiException(innerMsg, innerCode);
        }

        string msgId = dataNode?["msgId"]?.GetValue<string>()
                    ?? dataNode?["message_id"]?.GetValue<string>()
                    ?? now.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return msgId;
    }

    /// <summary>Fetches profile information for a user.</summary>
    public static async Task<(string Uid, string DisplayName, string? AvatarUrl)> GetUserInfoAsync(
        ZaloHttpClient http, ZaloSession session, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string host = GetHost(session, "profile", ZaloConstants.Hosts.Profile);
        bool isSelf = string.Equals(userId, session.Uid, StringComparison.Ordinal);
        string url = MakeUrl(host, isSelf ? "/api/social/profile/me" : "/api/social/friend/getprofiles/v2");

        JsonObject payload = isSelf
            ? new JsonObject
            {
                ["uid"] = userId,
                ["imei"] = session.Material.Imei
            }
            : new JsonObject
            {
                ["phonebook_version"] = 0,
                ["friend_pversion_map"] = new JsonArray($"{userId}_0"),
                ["avatar_size"] = 120,
                ["show_online_status"] = 1,
                ["language"] = session.Material.Language,
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

        if (!isSelf && dataNode?["changed_profiles"] is JsonObject changedProfiles)
        {
            dataNode = changedProfiles.FirstOrDefault(kv => kv.Key.StartsWith(userId, StringComparison.Ordinal)).Value
                    ?? changedProfiles.FirstOrDefault().Value;
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

    /// <summary>
    /// Sends a bank account card via zimsg/api/transfer/card.
    /// </summary>
    public static async Task SendBankCardAsync(
        this ZaloHttpClient http,
        ZaloSession session,
        string threadId,
        ZaloThreadType threadType,
        string binBank,
        string accountNumber,
        string accountName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binBank);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);

        string host = GetHost(session, "zimsg", ZaloConstants.Hosts.Chat);
        string url = MakeUrl(host, "/api/transfer/card");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject payload = new()
        {
            ["binBank"] = binBank,
            ["numAccBank"] = accountNumber,
            ["nameAccBank"] = (accountName ?? "---").ToUpperInvariant(),
            ["cliMsgId"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["tsMsg"] = now,
            ["destUid"] = threadId,
            ["destType"] = threadType == ZaloThreadType.Group ? 1 : 0
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt SendBankCard payload");
        }

        using FormUrlEncodedContent content = new([new KeyValuePair<string, string>("params", encryptedParams)]);

        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: content, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        int error = node?["error_code"]?.GetValue<int>() ?? node?["error"]?.GetValue<int>() ?? 0;
        if (error != 0)
        {
            throw new ZaloApiException(node?["error_message"]?.GetValue<string>() ?? $"SendBankCard failed with code {error}", error);
        }
    }

    /// <summary>
    /// Sends a user contact card recommendation via file/api/message/forward or file/api/group/forward.
    /// </summary>
    public static async Task SendContactCardAsync(
        this ZaloHttpClient http,
        ZaloSession session,
        string threadId,
        ZaloThreadType threadType,
        string userId,
        string? phoneNumber = null,
        string? qrCodeUrl = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        bool isGroup = threadType == ZaloThreadType.Group;
        string host = GetHost(session, "file", ZaloConstants.Hosts.File);
        string path = isGroup ? "/api/group/forward" : "/api/message/forward";
        string url = MakeUrl(host, path);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject msgInfo = new()
        {
            ["contactUid"] = userId,
            ["qrCodeUrl"] = qrCodeUrl ?? ""
        };
        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            msgInfo["phone"] = phoneNumber;
        }

        JsonObject payload = new()
        {
            ["ttl"] = 0,
            ["msgType"] = 6,
            ["clientId"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["msgInfo"] = msgInfo.ToJsonString()
        };

        if (isGroup)
        {
            payload["visibility"] = 0;
            payload["grid"] = threadId;
        }
        else
        {
            payload["toId"] = threadId;
            payload["imei"] = session.Material.Imei;
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt SendContactCard payload");
        }

        using FormUrlEncodedContent content = new([new KeyValuePair<string, string>("params", encryptedParams)]);

        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: content, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        int error = node?["error_code"]?.GetValue<int>() ?? node?["error"]?.GetValue<int>() ?? 0;
        if (error != 0)
        {
            throw new ZaloApiException(node?["error_message"]?.GetValue<string>() ?? $"SendContactCard failed with code {error}", error);
        }
    }

    /// <summary>
    /// Parses rich link metadata (<c>title</c>, <c>desc</c>, <c>thumb</c>, <c>src</c>) via <c>GET /api/message/parselink</c> (cmd 11285).
    /// </summary>
    public static async Task<ZaloLinkMetadata> ParseLinkAsync(
        ZaloHttpClient http, ZaloSession session, string rawUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawUrl);

        string url = rawUrl.Trim();
        string domain = "Liên kết";
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri) && !string.IsNullOrEmpty(parsedUri.Host))
        {
            domain = parsedUri.Host;
        }

        string title = domain;
        string desc = string.Empty;
        string thumb = string.Empty;
        string src = domain;

        string primaryHost = session.ServiceMap.TryGetValue("file", out string[]? fHosts) && fHosts.Length > 0
            ? fHosts[0]
            : ZaloConstants.Hosts.File;

        JsonObject body = new()
        {
            ["link"] = url,
            ["version"] = 1,
            ["imei"] = session.Material.Imei,
        };

        try
        {
            string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString()));
            string path = $"/api/message/parselink?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";
            using HttpResponseMessage resp = await http.RequestWithFailoverAsync("file", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
            JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
            JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
            JsonNode? d = root?["data"]?["data"] ?? root?["data"] ?? root;

            string? t = d?["title"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(t) && t != url)
            {
                title = t;
            }

            string? ds = d?["desc"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(ds) && ds != url)
            {
                desc = ds;
            }

            string? th = d?["thumb"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(th) && !th.Contains("im_parselink", StringComparison.OrdinalIgnoreCase))
            {
                thumb = th;
            }

            string? sr = d?["src"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(sr))
            {
                src = sr;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Fallback to domain metadata if parselink fails
        }

        return new ZaloLinkMetadata(url, title, desc, thumb, src);
    }

    /// <summary>
    /// Sends a rich link card message (<c>POST /api/message/link</c> or <c>/api/group/sendlink</c>, cmd 11286 / 11193).
    /// </summary>
    public static async Task<ZaloSendResult> SendLinkAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string url, string? message = null, ZaloLinkMetadata? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        ZaloLinkMetadata meta = metadata ?? await ParseLinkAsync(http, session, url, ct).ConfigureAwait(false);
        string msgStr = string.IsNullOrWhiteSpace(message) ? meta.Url : message.Trim();
        string titleStr = string.IsNullOrWhiteSpace(meta.Title) ? meta.Url : meta.Title.Trim();
        long clientId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string mediaJson = "{\"type\":0,\"count\":0,\"mediaTitle\":\"\",\"artist\":\"\",\"streamUrl\":\"\",\"stream_icon\":\"\"}";
        bool isGroup = threadType == ZaloThreadType.Group;
        string serviceKey = isGroup ? "group" : "chat";
        string primaryHost = isGroup ? GetGroupHost(session) : GetChatHost(session);

        JsonObject payload = new()
        {
            ["msg"] = msgStr,
            ["href"] = meta.Url,
            ["src"] = meta.SourceDomain,
            ["title"] = titleStr,
            ["desc"] = meta.Description,
            ["thumb"] = meta.ThumbnailUrl,
            ["type"] = 0,
            ["media"] = mediaJson,
            ["clientId"] = clientId,
            ["mentionInfo"] = isGroup ? "[]" : "",
            ["language"] = session.Material.Language,
            ["ttl"] = 0,
            ["imei"] = session.Material.Imei,
        };

        if (isGroup)
        {
            payload["grid"] = UndoApis.CleanGroupId(threadId);
        }
        else
        {
            payload["toId"] = threadId;
        }

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = isGroup
            ? $"/api/group/sendlink?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}"
            : $"/api/message/link?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

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
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"SendLink failed with code {errorCode}", errorCode);
        }

        string cliMsgId = clientId.ToString(CultureInfo.InvariantCulture);
        string msgId = root?["data"]?["msgId"]?.ToString()
                    ?? root?["msgId"]?.ToString()
                    ?? cliMsgId;
        return new ZaloSendResult(msgId, cliMsgId);
    }

    /// <summary>
    /// Forwards a message or sticker to another conversation.
    /// </summary>
    public static async Task<ZaloSendResult> ForwardMessageAsync(
        ZaloHttpClient http, ZaloSession session,
        string targetThreadId, ZaloThreadType targetThreadType,
        string text, int? stickerId = null, int? stickerCateId = null, int stickerType = 7,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetThreadId);

        if (stickerId is > 0)
        {
            return await StickerApis.SendStickerAsync(
                http, session, targetThreadId, stickerId.Value, stickerCateId ?? 0, stickerType, targetThreadType, ct).ConfigureAwait(false);
        }

        string cleanText = text ?? string.Empty;
        if (cleanText.StartsWith("[Đã thu hồi] ", StringComparison.Ordinal))
        {
            cleanText = cleanText["[Đã thu hồi] ".Length..];
        }

        (string msgId, string cliMsgId) = await SendTextAsync(http, session, targetThreadId, targetThreadType, cleanText, ct: ct).ConfigureAwait(false);
        return new ZaloSendResult(msgId, cliMsgId);
    }

    /// <summary>
    /// Sends a real-time typing indicator (<c>POST /api/message/typing</c> or <c>/api/group/typing</c>, cmd 11144 / 11196).
    /// Automatically suppressed if <see cref="ZaloPrivacyOptions.BlockTyping"/> is enabled on <paramref name="http"/>.
    /// </summary>
    public static async Task SendTypingAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        if (http.PrivacyOptions.BlockTyping || string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        bool isGroup = threadType == ZaloThreadType.Group;
        string clean = UndoApis.CleanGroupId(threadId);
        string serviceKey = isGroup ? "group" : "chat";
        string primaryHost = isGroup ? GetGroupHost(session) : GetChatHost(session);

        JsonObject payload = isGroup
            ? new JsonObject
            {
                ["grid"] = clean,
                ["imei"] = session.Material.Imei,
            }
            : new JsonObject
            {
                ["toid"] = clean,
                ["destType"] = 0,
                ["imei"] = session.Material.Imei,
            };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = isGroup
            ? $"/api/group/typing?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}"
            : $"/api/message/typing?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            serviceKey,
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);
        _ = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a "seen" receipt v2 (<c>POST /api/message/seenv2</c> or <c>/api/group/seenv2</c>, cmd 12118 / 12124).
    /// Automatically suppressed if <see cref="ZaloPrivacyOptions.BlockSeen"/> is enabled on <paramref name="http"/>.
    /// </summary>
    public static async Task SendSeenV2Async(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string lastMsgId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        if (http.PrivacyOptions.BlockSeen || string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(lastMsgId))
        {
            return;
        }

        bool isGroup = threadType == ZaloThreadType.Group;
        string clean = UndoApis.CleanGroupId(threadId);
        string serviceKey = isGroup ? "group" : "chat";
        string primaryHost = isGroup ? GetGroupHost(session) : GetChatHost(session);

        JsonObject msgInfos = new()
        {
            ["data"] = new JsonArray(
                (JsonNode)new JsonObject
                {
                    ["gmi"] = lastMsgId,
                    ["cmi"] = lastMsgId,
                })
        };

        if (isGroup)
        {
            msgInfos["grid"] = clean;
        }
        else
        {
            msgInfos["senderId"] = clean;
        }

        JsonObject payload = new()
        {
            ["msgInfos"] = msgInfos.ToJsonString(),
            ["imei"] = session.Material.Imei,
        };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = isGroup
            ? $"/api/group/seenv2?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}"
            : $"/api/message/seenv2?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            serviceKey,
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);
        _ = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches recent conversation previews and last messages (<c>GET /api/preloadconvers/get-last-msgs</c>, cmd 10053).
    /// </summary>
    public static async Task<IReadOnlyList<ZaloHistoryMessage>> GetLastMessagesAsync(
        ZaloHttpClient http, ZaloSession session, string[]? threadIds = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        JsonObject map = new();
        if (threadIds is { Length: > 0 and <= 25 })
        {
            foreach (string id in threadIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }
                bool isGrp = id.StartsWith("group_", StringComparison.OrdinalIgnoreCase) || (id.Length > 1 && (id[0] == 'g' || id[0] == 'G') && char.IsDigit(id[1]));
                string clean = UndoApis.CleanGroupId(id);
                map[isGrp ? $"{clean}_1" : $"{clean}_0"] = "1";
            }
        }

        JsonObject body = new()
        {
            ["threadIdLocalMsgId"] = map.ToJsonString(),
            ["imei"] = session.Material.Imei,
        };

        string primaryHost = session.ServiceMap.TryGetValue("conversation", out string[]? cHosts) && cHosts.Length > 0
            ? cHosts[0]
            : ZaloConstants.Hosts.Conversation;

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString()));
        string path = $"/api/preloadconvers/get-last-msgs?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("conversation", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        return ParseHistoryMessages(root, session.Uid, defaultThreadId: null, defaultIsGroup: false);
    }

    /// <summary>
    /// Fetches group message history (<c>GET /api/group/history</c>, cmd 11240).
    /// </summary>
    public static async Task<IReadOnlyList<ZaloHistoryMessage>> GetGroupHistoryAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, int count = 50, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        string cleanGrid = UndoApis.CleanGroupId(groupId);
        JsonObject body = new()
        {
            ["grid"] = cleanGrid,
            ["count"] = count > 0 ? count : 50,
            ["imei"] = session.Material.Imei,
        };

        string primaryHost = GetGroupHost(session);
        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString()));
        string path = $"/api/group/history?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("group", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        return ParseHistoryMessages(root, session.Uid, defaultThreadId: cleanGrid, defaultIsGroup: true);
    }

    /// <summary>
    /// Fetches cloud message history (<c>GET /api/cm/getrecentv2</c> or <c>/api/cm/getoldv2</c>, cmd 12506 / 12507).
    /// </summary>
    public static async Task<IReadOnlyList<ZaloHistoryMessage>> GetCloudMessagesAsync(
        ZaloHttpClient http, ZaloSession session,
        string convId, string globalMsgId = "0", bool isOld = false, int count = 50,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(convId);

        string cleanId = UndoApis.CleanGroupId(convId);
        JsonObject body = new()
        {
            ["groupId"] = cleanId,
            ["globalMsgId"] = string.IsNullOrWhiteSpace(globalMsgId) || globalMsgId == "0" ? "9223372036854775808" : globalMsgId,
            ["count"] = count > 0 ? count : 50,
            ["msgIds"] = new JsonArray(),
            ["imei"] = session.Material.Imei,
            ["src"] = isOld ? 3 : 1,
        };

        string primaryHost = session.ServiceMap.TryGetValue("zcloud", out string[]? zHosts) && zHosts.Length > 0
            ? zHosts[0]
            : ZaloConstants.Hosts.CloudMsg;

        string ep = isOld ? "/api/cm/getoldv2" : "/api/cm/getrecentv2";
        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString()));
        string path = $"{ep}?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}&nretry=0";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("cloud_msg", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        return ParseHistoryMessages(root, session.Uid, defaultThreadId: cleanId, defaultIsGroup: true);
    }

    internal static IReadOnlyList<ZaloHistoryMessage> ParseHistoryMessages(
        JsonNode? root, string? sessionUid, string? defaultThreadId, bool defaultIsGroup)
    {
        if (root is null)
        {
            return [];
        }

        JsonNode? dataNode = root["data"]?["data"] ?? root["data"] ?? root;
        if (dataNode is JsonValue strVal && strVal.TryGetValue(out string? innerJson) && !string.IsNullOrWhiteSpace(innerJson))
        {
            try { dataNode = JsonNode.Parse(innerJson); } catch { /* ignore */ }
        }

        List<(JsonNode Node, bool IsGroup)> candidates = new();
        if (dataNode is JsonArray arr)
        {
            foreach (JsonNode? it in arr)
            {
                if (it is not null)
                {
                    candidates.Add((it, defaultIsGroup));
                }
            }
        }
        else if (dataNode is JsonObject obj)
        {
            if (obj["msgs"] is JsonArray uMsgs)
            {
                foreach (JsonNode? it in uMsgs)
                {
                    if (it is not null)
                    {
                        candidates.Add((it, defaultIsGroup));
                    }
                }
            }
            if (obj["groupMsgs"] is JsonArray gMsgs)
            {
                foreach (JsonNode? it in gMsgs)
                {
                    if (it is not null)
                    {
                        candidates.Add((it, true));
                    }
                }
            }
            if (obj["messages"] is JsonArray mArr)
            {
                foreach (JsonNode? it in mArr)
                {
                    if (it is not null)
                    {
                        candidates.Add((it, defaultIsGroup));
                    }
                }
            }
        }

        List<ZaloHistoryMessage> result = new(candidates.Count);
        foreach ((JsonNode item, bool isGroupBucket) in candidates)
        {
            string msgId = item["msgId"]?.ToString()
                        ?? item["globalMsgId"]?.ToString()
                        ?? item["cliMsgId"]?.ToString()
                        ?? string.Empty;
            string cliMsgId = item["cliMsgId"]?.ToString() ?? msgId;
            if (string.IsNullOrEmpty(msgId))
            {
                continue;
            }

            string uidFrom = item["uidFrom"]?.ToString() ?? item["fromUid"]?.ToString() ?? string.Empty;
            string idTo = item["idTo"]?.ToString() ?? item["toId"]?.ToString() ?? defaultThreadId ?? string.Empty;
            string senderName = item["dName"]?.ToString() ?? item["displayName"]?.ToString() ?? uidFrom;
            string msgType = item["msgType"]?.ToString() ?? "webchat";
            bool isSelf = uidFrom == "0" || (!string.IsNullOrEmpty(sessionUid) && uidFrom == sessionUid);

            string threadId = isGroupBucket
                ? (!string.IsNullOrEmpty(defaultThreadId) ? defaultThreadId : idTo)
                : (isSelf ? idTo : uidFrom);
            if (string.IsNullOrEmpty(threadId))
            {
                threadId = defaultThreadId ?? idTo;
            }

            string contentText = item["content"] is JsonValue cVal && cVal.TryGetValue(out string? s)
                ? s
                : (item["content"]?.ToJsonString() ?? item["msg"]?.ToString() ?? string.Empty);

            long ts = 0;
            if (item["ts"] is JsonValue tsVal)
            {
                if (!tsVal.TryGetValue(out ts) && tsVal.TryGetValue(out string? tsStr))
                {
                    _ = long.TryParse(tsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out ts);
                }
            }

            result.Add(new ZaloHistoryMessage(
                MsgId: msgId,
                CliMsgId: cliMsgId,
                ThreadId: threadId,
                IsGroup: isGroupBucket,
                SenderId: isSelf && !string.IsNullOrEmpty(sessionUid) ? sessionUid : uidFrom,
                SenderName: senderName,
                Content: contentText,
                MsgType: msgType,
                Timestamp: ts,
                IsSelf: isSelf));
        }

        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }
}
