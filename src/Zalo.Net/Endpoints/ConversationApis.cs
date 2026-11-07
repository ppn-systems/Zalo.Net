// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Endpoint helpers for conversation state management (Pin/Unpin, Mute/Unmute, Unread Mark, Hide/Unhide, Delete Conversation).
/// </summary>
public static class ConversationApis
{
    private static readonly string[] s_pinnedArrayKeys = ["conversations", "pins", "pinned", "convs", "data", "list", "items"];
    private static readonly string[] s_pinnedIdKeys = ["convId", "id", "conversId", "threadId", "grid", "uid", "targetId"];

    /// <summary>
    /// Gets the list of pinned conversation thread IDs (<c>GET /api/pinconvers/list</c>, cmd 12120).
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetPinnedConversationsAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.ServiceMap.TryGetValue("conversation", out string[]? cHosts) && cHosts.Length > 0
            ? cHosts[0]
            : ZaloConstants.Hosts.Conversation;

        string path = $"/api/pinconvers/list?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";
        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("conversation", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        return ParsePinnedConversations(root);
    }

    internal static IReadOnlyList<string> ParsePinnedConversations(JsonNode? root)
    {
        if (root is null)
        {
            return [];
        }

        JsonNode? dataNode = root["data"] ?? root;
        if (dataNode is JsonValue strVal && strVal.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
        {
            try { dataNode = JsonNode.Parse(s); } catch { /* ignore */ }
        }

        List<string> output = new();

        void CheckArray(JsonArray arr)
        {
            foreach (JsonNode? item in arr)
            {
                string? id = ExtractPinnedId(item);
                if (!string.IsNullOrEmpty(id) && !output.Contains(id))
                {
                    output.Add(id);
                }
            }
        }

        if (dataNode is JsonArray directArr)
        {
            CheckArray(directArr);
        }
        else if (dataNode is JsonObject obj)
        {
            foreach (string key in s_pinnedArrayKeys)
            {
                if (obj[key] is JsonArray subArr)
                {
                    CheckArray(subArr);
                }
            }
        }

        return output;
    }

    private static string? ExtractPinnedId(JsonNode? item)
    {
        if (item is null)
        {
            return null;
        }
        if (item is JsonValue val)
        {
            if (val.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
            {
                return s.Trim();
            }
            if (val.TryGetValue(out long l) && l > 0)
            {
                return l.ToString(CultureInfo.InvariantCulture);
            }
        }
        else if (item is JsonObject obj)
        {
            foreach (string key in s_pinnedIdKeys)
            {
                if (obj[key] is JsonValue kv)
                {
                    if (kv.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
                    {
                        return s.Trim();
                    }
                    if (kv.TryGetValue(out long l) && l > 0)
                    {
                        return l.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Pins or unpins a conversation (<c>POST /api/pinconvers/updatev2</c>, cmd 12122).
    /// </summary>
    public static async Task SetPinnedConversationAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, bool pin,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        string clean = UndoApis.CleanGroupId(threadId).TrimStart('u', 'U');
        string prefixed = threadType == ZaloThreadType.Group ? $"g{clean}" : $"u{clean}";

        JsonObject body = new()
        {
            ["actionType"] = pin ? 1 : 2,
            ["conversations"] = new JsonArray((JsonNode)prefixed),
            ["tab"] = 0,
        };

        string primaryHost = session.ServiceMap.TryGetValue("conversation", out string[]? cHosts) && cHosts.Length > 0
            ? cHosts[0]
            : ZaloConstants.Hosts.Conversation;

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString());
        string path = $"/api/pinconvers/updatev2?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "conversation",
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
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"SetPinnedConversation failed ({errorCode})", errorCode);
        }
    }

    /// <summary>
    /// Gets all muted conversations (<c>GET /api/social/profile/getmute</c>, cmd 11280).
    /// </summary>
    public static async Task<IReadOnlyList<ZaloMutedConversation>> GetMutedConversationsAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.ServiceMap.TryGetValue("profile", out string[]? pHosts) && pHosts.Length > 0
            ? pHosts[0]
            : ZaloConstants.Hosts.Profile;

        JsonObject body = new() { ["imei"] = session.Material.Imei };
        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString()));
        string path = $"/api/social/profile/getmute?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("profile", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        JsonNode? payload = root?["data"] ?? root;

        List<ZaloMutedConversation> result = new();
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        void ExtractEntries(JsonArray? entries, bool isGroup)
        {
            if (entries is null)
            {
                return;
            }
            foreach (JsonNode? entry in entries)
            {
                if (entry is null)
                {
                    continue;
                }
                string? rawId = entry["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(rawId) || rawId == "0")
                {
                    continue;
                }
                int mode = entry["muteMode"]?.GetValue<int>() ?? 1;
                if (mode != 1)
                {
                    continue;
                }
                long duration = entry["duration"]?.GetValue<long>() ?? -1;
                if (duration == 0)
                {
                    continue;
                }
                long start = entry["startTime"]?.GetValue<long>() ?? entry["systemTime"]?.GetValue<long>() ?? nowUnix;
                long until = duration < 0 ? -1 : start + duration;
                if (until > 0 && until <= nowUnix)
                {
                    continue;
                }
                result.Add(new ZaloMutedConversation(UndoApis.CleanGroupId(rawId), isGroup, until));
            }
        }

        ExtractEntries(payload?["chatEntries"] as JsonArray, isGroup: false);
        ExtractEntries(payload?["groupChatEntries"] as JsonArray, isGroup: true);
        return result;
    }

    /// <summary>
    /// Mutes or unmutes notifications for a conversation (<c>POST /api/social/profile/setmute</c>, cmd 11288).
    /// </summary>
    public static async Task SetMuteConversationAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, bool mute, long durationSeconds = -1,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        string clean = UndoApis.CleanGroupId(threadId);
        JsonObject body = new()
        {
            ["toid"] = clean,
            ["duration"] = durationSeconds,
            ["action"] = mute ? 1 : 2,
            ["startTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["muteType"] = threadType == ZaloThreadType.Group ? 2 : 1,
            ["imei"] = session.Material.Imei,
        };

        string primaryHost = session.ServiceMap.TryGetValue("profile", out string[]? pHosts) && pHosts.Length > 0
            ? pHosts[0]
            : ZaloConstants.Hosts.Profile;

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString());
        string path = $"/api/social/profile/setmute?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "profile",
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
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"SetMuteConversation failed ({errorCode})", errorCode);
        }
    }

    /// <summary>
    /// Gets manually marked unread conversations (<c>GET /api/conv/getUnreadMark</c>, cmd 12011).
    /// </summary>
    public static async Task<IReadOnlyList<ZaloUnreadMark>> GetUnreadMarksAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.ServiceMap.TryGetValue("conversation", out string[]? cHosts) && cHosts.Length > 0
            ? cHosts[0]
            : ZaloConstants.Hosts.Conversation;

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, "{}"));
        string path = $"/api/conv/getUnreadMark?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("conversation", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        JsonNode? data = root?["data"] ?? root;
        if (data is JsonValue strVal && strVal.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
        {
            try { data = JsonNode.Parse(s); } catch { /* ignore */ }
        }

        List<ZaloUnreadMark> marks = new();
        void ParseRows(JsonArray? rows, bool isGroup)
        {
            if (rows is null)
            {
                return;
            }
            foreach (JsonNode? row in rows)
            {
                string? id = row?["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id) || id == "0")
                {
                    continue;
                }
                string cliMsgId = row?["cliMsgId"]?.ToString() ?? string.Empty;
                string fromUid = row?["fromUid"]?.ToString() ?? string.Empty;
                marks.Add(new ZaloUnreadMark(UndoApis.CleanGroupId(id), isGroup, cliMsgId, fromUid));
            }
        }

        ParseRows(data?["convsGroup"] as JsonArray, isGroup: true);
        ParseRows(data?["convsUser"] as JsonArray, isGroup: false);
        return marks;
    }

    /// <summary>
    /// Marks a conversation as unread or read (<c>POST /api/conv/addUnreadMark</c> or <c>/api/conv/removeUnreadMark</c>, cmd 12013 / 12014).
    /// </summary>
    public static async Task SetUnreadMarkAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, bool unread,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        string clean = UndoApis.CleanGroupId(threadId);
        bool isGroup = threadType == ZaloThreadType.Group;
        JsonArray convsGroup = isGroup ? [clean] : [];
        JsonArray convsUser = isGroup ? [] : [clean];

        string endpointPath;
        JsonObject inner;
        if (unread)
        {
            endpointPath = "/api/conv/addUnreadMark";
            inner = new JsonObject
            {
                ["convsGroup"] = convsGroup,
                ["convsUser"] = convsUser,
                ["imei"] = session.Material.Imei,
            };
        }
        else
        {
            endpointPath = "/api/conv/removeUnreadMark";
            inner = new JsonObject
            {
                ["convsGroup"] = convsGroup,
                ["convsUser"] = convsUser,
                ["convsGroupData"] = new JsonArray(),
                ["convsUserData"] = new JsonArray(),
            };
        }

        JsonObject outer = new() { ["param"] = inner.ToJsonString() };
        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, outer.ToJsonString());

        string primaryHost = session.ServiceMap.TryGetValue("conversation", out string[]? cHosts) && cHosts.Length > 0
            ? cHosts[0]
            : ZaloConstants.Hosts.Conversation;

        string path = $"{endpointPath}?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";
        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "conversation",
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
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"SetUnreadMark failed ({errorCode})", errorCode);
        }
    }

    /// <summary>
    /// Hides or unhides a conversation (<c>POST /api/hiddenconvers/add-remove</c>, cmd 10049).
    /// </summary>
    public static async Task SetHiddenConversationAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, bool hide,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        string clean = UndoApis.CleanGroupId(threadId);
        JsonArray entry =
        [
            new JsonObject
            {
                ["thread_id"] = clean,
                ["is_group"] = threadType == ZaloThreadType.Group ? 1 : 0,
            }
        ];

        string entryJson = entry.ToJsonString();
        JsonObject body = new()
        {
            ["del_threads"] = hide ? "[]" : entryJson,
            ["add_threads"] = hide ? entryJson : "[]",
            ["imei"] = session.Material.Imei,
        };

        string primaryHost = session.ServiceMap.TryGetValue("conversation", out string[]? cHosts) && cHosts.Length > 0
            ? cHosts[0]
            : ZaloConstants.Hosts.Conversation;

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString());
        string path = $"/api/hiddenconvers/add-remove?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "conversation",
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
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"SetHiddenConversation failed ({errorCode})", errorCode);
        }
    }

    /// <summary>
    /// Deletes conversation history for the current user (<c>POST /api/message/deleteconver</c> or <c>/api/group/deleteconver</c>, cmd 11778 / 11835).
    /// </summary>
    public static async Task DeleteConversationHistoryAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string? lastCliMsgId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        bool isGroup = threadType == ZaloThreadType.Group;
        string clean = UndoApis.CleanGroupId(threadId);
        string cliId = string.IsNullOrWhiteSpace(lastCliMsgId)
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            : lastCliMsgId;

        JsonObject body = new()
        {
            ["cliMsgId"] = cliId,
            ["conver"] = new JsonObject(),
            ["onlyMe"] = 1,
            ["imei"] = session.Material.Imei,
        };

        if (isGroup)
        {
            body["grid"] = clean;
        }
        else
        {
            body["toid"] = clean;
        }

        string serviceKey = isGroup ? "group" : "chat";
        string primaryHost = isGroup
            ? (session.ServiceMap.TryGetValue("group", out string[]? gHosts) && gHosts.Length > 0 ? gHosts[0] : ZaloConstants.Hosts.Group)
            : (session.ServiceMap.TryGetValue("chat", out string[]? cHosts) && cHosts.Length > 0 ? cHosts[0] : ZaloConstants.Hosts.Chat);

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, body.ToJsonString());
        string path = isGroup
            ? $"/api/group/deleteconver?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}"
            : $"/api/message/deleteconver?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

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
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"DeleteConversationHistory failed ({errorCode})", errorCode);
        }
    }

    private static IReadOnlyList<ZaloLabelItem> ParseLabelDataNode(JsonNode? labelDataNode)
    {
        JsonArray? arr = null;
        if (labelDataNode is JsonArray directArr)
        {
            arr = directArr;
        }
        else if (labelDataNode is JsonValue strVal && strVal.TryGetValue(out string? rawJson) && !string.IsNullOrWhiteSpace(rawJson))
        {
            try
            {
                arr = JsonNode.Parse(rawJson) as JsonArray;
            }
            catch
            {
                // Ignore malformed inner labelData string
            }
        }

        if (arr is null)
        {
            return [];
        }

        List<ZaloLabelItem> items = [];
        foreach (JsonNode? item in arr)
        {
            if (item is null)
            {
                continue;
            }

            int id = item["id"]?.GetValue<int>() ?? 0;
            string text = item["text"]?.GetValue<string>() ?? string.Empty;
            string textKey = item["textKey"]?.GetValue<string>() ?? string.Empty;
            string color = item["color"]?.GetValue<string>() ?? string.Empty;
            string emoji = item["emoji"]?.GetValue<string>() ?? string.Empty;
            long createTime = item["createTime"]?.GetValue<long>() ?? 0;

            List<string> convs = [];
            if (item["conversations"] is JsonArray convsArr)
            {
                foreach (JsonNode? c in convsArr)
                {
                    string? cId = c?.GetValue<string>();
                    if (!string.IsNullOrEmpty(cId))
                    {
                        convs.Add(cId);
                    }
                }
            }

            items.Add(new ZaloLabelItem(id, text, textKey, color, emoji, convs, createTime));
        }

        return items;
    }

    public static async Task<IReadOnlyList<ZaloLabelItem>> GetLabelsAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.GetServiceUrl("label") ?? ZaloConstants.Hosts.Label;
        JsonObject payload = new()
        {
            ["imei"] = session.Material.Imei
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/convlabel/get?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("label", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        JsonNode? data = root?["data"] ?? root;

        return ParseLabelDataNode(data?["labelData"]);
    }

    public static async Task<IReadOnlyList<ZaloLabelItem>> UpdateLabelsAsync(
        ZaloHttpClient http, ZaloSession session, IReadOnlyList<ZaloLabelItem> labels, int version, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(labels);

        string primaryHost = session.GetServiceUrl("label") ?? ZaloConstants.Hosts.Label;
        JsonArray labelArray = [];
        int offset = 0;
        foreach (ZaloLabelItem label in labels)
        {
            JsonArray convArr = [];
            if (label.Conversations is not null)
            {
                foreach (string c in label.Conversations)
                {
                    convArr.Add((JsonNode?)c);
                }
            }

            JsonObject lObj = new()
            {
                ["id"] = label.Id,
                ["text"] = label.Text,
                ["textKey"] = label.TextKey,
                ["conversations"] = convArr,
                ["color"] = label.Color,
                ["offset"] = offset++,
                ["emoji"] = label.Emoji,
                ["createTime"] = label.CreateTime
            };
            labelArray.Add((JsonNode?)lObj);
        }

        JsonObject payload = new()
        {
            ["labelData"] = labelArray.ToJsonString(),
            ["version"] = version,
            ["imei"] = session.Material.Imei
        };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = $"/api/convlabel/update?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "label",
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);

        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        JsonNode? data = root?["data"] ?? root;

        IReadOnlyList<ZaloLabelItem> parsed = ParseLabelDataNode(data?["labelData"]);
        return parsed.Count > 0 ? parsed : labels;
    }

    private static ZaloQuickMessage ParseQuickMessageItem(JsonNode? item, int fallbackId = 0, string fallbackKeyword = "", string fallbackTitle = "")
    {
        if (item is null)
        {
            return new ZaloQuickMessage(fallbackId, fallbackKeyword, fallbackTitle);
        }

        int id = item["id"]?.GetValue<int>() ?? fallbackId;
        string keyword = item["keyword"]?.GetValue<string>() ?? fallbackKeyword;
        int type = item["type"]?.GetValue<int>() ?? 0;
        long created = item["createdTime"]?.GetValue<long>() ?? 0;
        long modified = item["lastModified"]?.GetValue<long>() ?? created;
        string title = item["message"]?["title"]?.GetValue<string>() ?? fallbackTitle;

        return new ZaloQuickMessage(id, keyword, title, type, created, modified);
    }

    public static async Task<IReadOnlyList<ZaloQuickMessage>> GetQuickMessagesAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.GetServiceUrl("quick_message") ?? ZaloConstants.Hosts.QuickMessage;
        JsonObject payload = new()
        {
            ["version"] = 0,
            ["lang"] = 0,
            ["imei"] = session.Material.Imei
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/quickmessage/list?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("quick_message", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        JsonNode? data = root?["data"] ?? root;

        List<ZaloQuickMessage> results = [];
        if (data?["items"] is JsonArray itemsArr)
        {
            foreach (JsonNode? item in itemsArr)
            {
                if (item is not null)
                {
                    results.Add(ParseQuickMessageItem(item));
                }
            }
        }

        return results;
    }

    public static async Task<ZaloQuickMessage> AddQuickMessageAsync(
        ZaloHttpClient http, ZaloSession session, string keyword, string title, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        string primaryHost = session.GetServiceUrl("quick_message") ?? ZaloConstants.Hosts.QuickMessage;
        JsonObject payload = new()
        {
            ["keyword"] = keyword,
            ["message"] = new JsonObject
            {
                ["title"] = title,
                ["params"] = string.Empty
            },
            ["type"] = 0,
            ["imei"] = session.Material.Imei
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/quickmessage/create?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("quick_message", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        JsonNode? data = root?["data"] ?? root;

        return ParseQuickMessageItem(data?["item"] ?? data, 0, keyword, title);
    }

    public static async Task<ZaloQuickMessage> UpdateQuickMessageAsync(
        ZaloHttpClient http, ZaloSession session, int itemId, string keyword, string title, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        string primaryHost = session.GetServiceUrl("quick_message") ?? ZaloConstants.Hosts.QuickMessage;
        JsonObject payload = new()
        {
            ["itemId"] = itemId,
            ["keyword"] = keyword,
            ["message"] = new JsonObject
            {
                ["title"] = title,
                ["params"] = string.Empty
            },
            ["type"] = 0
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/quickmessage/update?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("quick_message", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        JsonNode? data = root?["data"] ?? root;

        return ParseQuickMessageItem(data?["item"] ?? data, itemId, keyword, title);
    }

    public static async Task RemoveQuickMessageAsync(
        ZaloHttpClient http, ZaloSession session, int[] itemIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Length == 0)
        {
            return;
        }

        string primaryHost = session.GetServiceUrl("quick_message") ?? ZaloConstants.Hosts.QuickMessage;
        JsonArray idsArr = [];
        foreach (int id in itemIds)
        {
            idsArr.Add((JsonNode?)id);
        }

        JsonObject payload = new()
        {
            ["itemIds"] = idsArr
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/quickmessage/delete?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("quick_message", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? json?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"RemoveQuickMessage failed ({errorCode})", errorCode);
        }
    }

    public static async Task UpdateAutoDeleteChatAsync(
        ZaloHttpClient http, ZaloSession session, string threadId, bool isGroup, long ttlMs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        string primaryHost = session.GetServiceUrl("conversation") ?? ZaloConstants.Hosts.Conversation;
        JsonObject payload = new()
        {
            ["threadId"] = UndoApis.CleanGroupId(threadId),
            ["isGroup"] = isGroup ? 1 : 0,
            ["ttl"] = ttlMs,
            ["clientLang"] = session.Material.Language
        };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = $"/api/conv/autodelete/updateConvers?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "conversation",
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
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"UpdateAutoDeleteChat failed ({errorCode})", errorCode);
        }
    }

    public static async Task UpdateProfileAsync(
        ZaloHttpClient http, ZaloSession session, ZaloProfileUpdateOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DateOfBirth);

        string primaryHost = session.GetServiceUrl("profile") ?? ZaloConstants.Hosts.Profile;

        JsonObject profileObj = new()
        {
            ["name"] = options.Name,
            ["dob"] = options.DateOfBirth,
            ["gender"] = options.Gender
        };

        JsonObject bizObj = new();
        if (options.BizDescription is not null)
        {
            bizObj["desc"] = options.BizDescription;
        }
        if (options.BizCategory.HasValue)
        {
            bizObj["cate"] = options.BizCategory.Value;
        }
        if (options.BizAddress is not null)
        {
            bizObj["addr"] = options.BizAddress;
        }
        if (options.BizWebsite is not null)
        {
            bizObj["website"] = options.BizWebsite;
        }
        if (options.BizEmail is not null)
        {
            bizObj["email"] = options.BizEmail;
        }

        JsonObject payload = new()
        {
            ["profile"] = profileObj.ToJsonString(),
            ["biz"] = bizObj.ToJsonString(),
            ["language"] = session.Material.Language
        };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = $"/api/social/profile/update?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "profile",
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
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"UpdateProfile failed ({errorCode})", errorCode);
        }
    }

    public static async Task UpdateSettingsAsync(
        ZaloHttpClient http, ZaloSession session, string settingKey, int value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);

        JsonObject payload = new()
        {
            [settingKey] = value
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/setting/update?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("login", ZaloConstants.Hosts.Login, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? json?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"UpdateSettings failed ({errorCode})", errorCode);
        }
    }

    public static async Task UpdateActiveStatusAsync(
        ZaloHttpClient http, ZaloSession session, bool isActive, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.GetServiceUrl("profile") ?? ZaloConstants.Hosts.Profile;
        JsonObject payload = new()
        {
            ["status"] = isActive ? 1 : 0,
            ["imei"] = session.Material.Imei
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string endpoint = isActive ? "/api/social/profile/ping" : "/api/social/profile/deactive";
        string path = $"{endpoint}?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("profile", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? json?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"UpdateActiveStatus failed ({errorCode})", errorCode);
        }
    }

    public static async Task UpdateHiddenConversPinAsync(
        ZaloHttpClient http, ZaloSession session, string fourDigitPin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(fourDigitPin);

        if (fourDigitPin.Length != 4 || !int.TryParse(fourDigitPin, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException("PIN must be a 4-digit number between 0000 and 9999.", nameof(fourDigitPin));
        }

        string primaryHost = session.GetServiceUrl("conversation") ?? ZaloConstants.Hosts.Conversation;
        JsonObject payload = new()
        {
            ["new_pin"] = Hashing.Md5Hex(fourDigitPin),
            ["imei"] = session.Material.Imei
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/hiddenconvers/update-pin?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("conversation", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? json?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"UpdateHiddenConversPin failed ({errorCode})", errorCode);
        }
    }

    public static async Task ResetHiddenConversPinAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.GetServiceUrl("conversation") ?? ZaloConstants.Hosts.Conversation;
        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, "{}"));
        string path = $"/api/hiddenconvers/reset?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("conversation", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = StickerApis.UnwrapDecrypt(json, session.Material.SecretKey);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? json?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"ResetHiddenConversPin failed ({errorCode})", errorCode);
        }
    }
}
