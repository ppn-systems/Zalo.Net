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

internal static class FriendApis
{
    private const string DefaultProfileHost = ZaloConstants.Hosts.Profile;
    private const string DefaultFriendHost = ZaloConstants.Hosts.Friend;

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

    private static JsonNode? DecryptDataNode(ZaloSession session, JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        JsonNode? dataNode = node["data"];
        if (dataNode is null)
        {
            return node;
        }

        if (dataNode.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            string encStr = dataNode.GetValue<string>();
            if (string.IsNullOrWhiteSpace(encStr))
            {
                return null;
            }

            string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr)
                             ?? ZaloCipher.DecodeAesUtf8Key(session.Material.SecretKey, encStr);
            if (!string.IsNullOrWhiteSpace(decrypted))
            {
                try
                {
                    JsonNode? decodedJson = JsonNode.Parse(decrypted);
                    if (decodedJson is JsonObject obj && obj.ContainsKey("data"))
                    {
                        return obj["data"];
                    }
                    return decodedJson;
                }
                catch
                {
                    // Fallback to raw data node if parse fails
                }
            }
        }

        return dataNode;
    }

    public static async Task<IReadOnlyList<ZaloFriendInfo>> GetAllFriendsAsync(
        ZaloHttpClient http, ZaloSession session, int count, int page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string host = GetHost(session, "profile", DefaultProfileHost);
        string baseUrl = MakeUrl(host, "/api/social/friend/getfriends");

        JsonObject payload = new()
        {
            ["incInvalid"] = 1,
            ["page"] = page,
            ["count"] = count > 0 ? count : 200,
            ["avatar_size"] = 120,
            ["actiontime"] = 0,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt getfriends payload");
        }

        string requestUrl = $"{baseUrl}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from getfriends");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }

        JsonNode? dataNode = DecryptDataNode(session, node);
        if (dataNode is not JsonArray arr)
        {
            return Array.Empty<ZaloFriendInfo>();
        }

        List<ZaloFriendInfo> friends = [];
        foreach (JsonNode? item in arr)
        {
            if (item is null)
            {
                continue;
            }
            string userId = item["userId"]?.GetValue<string>()
                         ?? item["uid"]?.GetValue<string>()
                         ?? item["fId"]?.GetValue<string>()
                         ?? "";
            string displayName = item["displayName"]?.GetValue<string>()
                               ?? item["zaloName"]?.GetValue<string>()
                               ?? item["dName"]?.GetValue<string>()
                               ?? "";
            string? avatar = item["avatar"]?.GetValue<string>();
            string? phone = item["phoneNumber"]?.GetValue<string>();
            string? alias = item["alias"]?.GetValue<string>();

            if (!string.IsNullOrEmpty(userId))
            {
                friends.Add(new ZaloFriendInfo(userId, displayName, avatar, phone, alias));
            }
        }

        return friends;
    }

    public static async Task<ZaloUserProfile> FindUserByPhoneAsync(
        ZaloHttpClient http, ZaloSession session, string phoneNumber, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        string phone = phoneNumber.Trim();
        if (phone.StartsWith('0') && session.Material.Language.Equals("vi", StringComparison.OrdinalIgnoreCase))
        {
            phone = $"84{phone[1..]}";
        }

        string host = GetHost(session, "friend", DefaultFriendHost);
        string baseUrl = MakeUrl(host, "/api/friend/profile/get");

        JsonObject payload = new()
        {
            ["phone"] = phone,
            ["avatar_size"] = 240,
            ["language"] = session.Material.Language,
            ["imei"] = session.Material.Imei,
            ["reqSrc"] = 40
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt findUser payload");
        }

        string requestUrl = $"{baseUrl}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from findUser");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0 && errorCode != 216)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }

        JsonNode? data = DecryptDataNode(session, node);
        string uid = data?["uid"]?.GetValue<string>()
                  ?? data?["userId"]?.GetValue<string>()
                  ?? data?["fId"]?.GetValue<string>()
                  ?? "";
        string displayName = data?["zalo_name"]?.GetValue<string>()
                           ?? data?["display_name"]?.GetValue<string>()
                           ?? data?["displayName"]?.GetValue<string>()
                           ?? data?["zaloName"]?.GetValue<string>()
                           ?? data?["dpName"]?.GetValue<string>()
                           ?? data?["dName"]?.GetValue<string>()
                           ?? data?["name"]?.GetValue<string>()
                           ?? data?["username"]?.GetValue<string>()
                           ?? data?["user_name"]?.GetValue<string>()
                           ?? "";
        string? avatar = data?["avatar"]?.GetValue<string>();

        return new ZaloUserProfile(uid, displayName, avatar);
    }

    public static async Task SendFriendRequestAsync(
        ZaloHttpClient http, ZaloSession session, string userId, string? message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string host = GetHost(session, "friend", DefaultFriendHost);
        string url = MakeUrl(host, "/api/friend/sendreq");

        JsonObject payload = new()
        {
            ["toid"] = userId,
            ["msg"] = message ?? "Xin chào, mình kết bạn nhé!",
            ["imei"] = session.Material.Imei,
            ["language"] = session.Material.Language
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt sendFriendRequest payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from sendFriendRequest");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }

    public static async Task AcceptFriendRequestAsync(
        ZaloHttpClient http, ZaloSession session, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string host = GetHost(session, "friend", DefaultFriendHost);
        string url = MakeUrl(host, "/api/friend/accept");

        JsonObject payload = new()
        {
            ["fid"] = userId,
            ["imei"] = session.Material.Imei,
            ["language"] = session.Material.Language
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt acceptFriendRequest payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from acceptFriendRequest");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }

    public static async Task BlockUserAsync(
        ZaloHttpClient http, ZaloSession session, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string host = GetHost(session, "friend", DefaultFriendHost);
        string url = MakeUrl(host, "/api/friend/block");

        JsonObject payload = new()
        {
            ["fid"] = userId,
            ["imei"] = session.Material.Imei,
            ["language"] = session.Material.Language
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt blockUser payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from blockUser");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }

    public static async Task UnblockUserAsync(
        ZaloHttpClient http, ZaloSession session, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string host = GetHost(session, "friend", DefaultFriendHost);
        string url = MakeUrl(host, "/api/friend/unblock");

        JsonObject payload = new()
        {
            ["fid"] = userId,
            ["imei"] = session.Material.Imei,
            ["language"] = session.Material.Language
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt unblockUser payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from unblockUser");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }

    public static async Task ChangeFriendAliasAsync(
        ZaloHttpClient http, ZaloSession session, string userId, string alias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string host = GetHost(session, "alias", ZaloConstants.Hosts.Alias);
        string path = $"/api/alias/update?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        JsonObject payload = new()
        {
            ["friendId"] = userId,
            ["alias"] = alias ?? "",
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt changeFriendAlias payload");
        }

        string pathAndQuery = $"{path}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("alias", host, pathAndQuery, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from changeFriendAlias");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }

    public static async Task<ZaloOnlineStatus> GetUserLastOnlineAsync(
        ZaloHttpClient http, ZaloSession session, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string cleanUid = userId.Trim();
        string primaryHost = GetHost(session, "profile", DefaultProfileHost);

        JsonObject payload = new()
        {
            ["uid"] = cleanUid,
            ["conv_type"] = 1,
            ["imei"] = session.Material.Imei,
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/social/profile/lastOnline?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("profile", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? d = DecryptDataNode(session, node);
        if (d?["data"] is JsonObject innerData)
        {
            d = innerData;
        }

        bool showOnline = true;
        if (d?["settings"]?["show_online_status"] is JsonValue showVal)
        {
            if (showVal.TryGetValue(out bool b))
            {
                showOnline = b;
            }
            else if (showVal.TryGetValue(out int n))
            {
                showOnline = n != 0;
            }
        }

        if (!showOnline)
        {
            return new ZaloOnlineStatus(cleanUid, false, 0, string.Empty);
        }

        long lastOnlRaw = 0;
        if (d?["lastOnline"] is JsonValue loVal)
        {
            if (!loVal.TryGetValue(out lastOnlRaw) && loVal.TryGetValue(out string? loStr))
            {
                _ = long.TryParse(loStr?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out lastOnlRaw);
            }
        }

        if (lastOnlRaw <= 0)
        {
            return new ZaloOnlineStatus(cleanUid, false, 0, string.Empty);
        }

        long lastSec = lastOnlRaw > 10_000_000_000L ? lastOnlRaw / 1000 : lastOnlRaw;
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long diffSec = Math.Max(0, nowSec - lastSec);

        if (diffSec <= 300)
        {
            return new ZaloOnlineStatus(cleanUid, true, lastSec, "Vừa mới truy cập");
        }

        long mins = diffSec / 60;
        if (mins < 60)
        {
            return new ZaloOnlineStatus(cleanUid, false, lastSec, $"Truy cập {mins} phút trước");
        }

        long hours = diffSec / 3600;
        if (hours < 24)
        {
            return new ZaloOnlineStatus(cleanUid, false, lastSec, $"Truy cập {hours} giờ trước");
        }

        long days = Math.Min(7, diffSec / 86400);
        return new ZaloOnlineStatus(cleanUid, false, lastSec, $"Truy cập {days} ngày trước");
    }

    public static async Task<ZaloFriendshipStatus> GetFriendRequestStatusAsync(
        ZaloHttpClient http, ZaloSession session, string targetUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);

        string primaryHost = GetHost(session, "friend", DefaultFriendHost);
        JsonObject payload = new()
        {
            ["fid"] = targetUserId.Trim(),
            ["imei"] = session.Material.Imei,
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/friend/reqstatus?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("friend", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? d = DecryptDataNode(session, node);
        if (d?["data"] is JsonObject innerData)
        {
            d = innerData;
        }

        static bool IsTruthy(JsonNode? n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out int i))
                {
                    return i == 1;
                }
                if (v.TryGetValue(out bool b))
                {
                    return b;
                }
            }
            return false;
        }

        if (IsTruthy(d?["is_friend"] ?? d?["isFriend"]))
        {
            return ZaloFriendshipStatus.Friend;
        }
        if (IsTruthy(d?["is_requesting"] ?? d?["isRequesting"]))
        {
            return ZaloFriendshipStatus.IncomingPending;
        }
        if (IsTruthy(d?["is_requested"] ?? d?["isRequested"]))
        {
            return ZaloFriendshipStatus.OutgoingPending;
        }
        return ZaloFriendshipStatus.Stranger;
    }

    public static async Task RejectFriendRequestAsync(
        ZaloHttpClient http, ZaloSession session, string targetUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);

        string primaryHost = GetHost(session, "friend", DefaultFriendHost);
        JsonObject payload = new()
        {
            ["fid"] = targetUserId.Trim(),
            ["imei"] = session.Material.Imei,
        };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = $"/api/friend/reject?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "friend",
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);

        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = DecryptDataNode(session, node);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? node?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"RejectFriendRequest failed ({errorCode})", errorCode);
        }
    }

    public static async Task UndoFriendRequestAsync(
        ZaloHttpClient http, ZaloSession session, string targetUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);

        string primaryHost = GetHost(session, "friend", DefaultFriendHost);
        JsonObject payload = new()
        {
            ["fid"] = targetUserId.Trim(),
            ["imei"] = session.Material.Imei,
        };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = $"/api/friend/undo?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "friend",
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);

        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = DecryptDataNode(session, node);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? node?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"UndoFriendRequest failed ({errorCode})", errorCode);
        }
    }

    public static async Task<IReadOnlyList<ZaloUserProfile>> GetMiniProfilesAsync(
        ZaloHttpClient http, ZaloSession session, string[] userIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        if (userIds is null || userIds.Length == 0)
        {
            return [];
        }

        JsonArray versionedIds = new();
        foreach (string rawUid in userIds)
        {
            if (string.IsNullOrWhiteSpace(rawUid))
            {
                continue;
            }
            string clean = rawUid.Trim();
            versionedIds.Add((JsonNode?)(clean.Contains('_', StringComparison.Ordinal) ? clean : $"{clean}_0"));
        }

        if (versionedIds.Count == 0)
        {
            return [];
        }

        JsonObject payload = new()
        {
            ["friend_pversion_map"] = versionedIds,
        };

        string primaryHost = GetHost(session, "profile", DefaultProfileHost);
        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = $"/api/social/group/members?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "profile",
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);

        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? data = DecryptDataNode(session, node);
        if ((data?["profiles"] ?? data?["data"]?["profiles"] ?? data) is not JsonObject profilesObj)
        {
            return [];
        }

        List<ZaloUserProfile> profiles = new(profilesObj.Count);
        foreach (KeyValuePair<string, JsonNode?> kvp in profilesObj)
        {
            if (kvp.Value is not JsonObject p)
            {
                continue;
            }
            string uid = p["id"]?.ToString() ?? kvp.Key;
            string name = p["zaloName"]?.GetValue<string>()
                       ?? p["displayName"]?.GetValue<string>()
                       ?? p["name"]?.GetValue<string>()
                       ?? p["display_name"]?.GetValue<string>()
                       ?? uid;
            string? avatar = p["avatar"]?.GetValue<string>() ?? p["avt"]?.GetValue<string>();
            profiles.Add(new ZaloUserProfile(uid, name, avatar));
        }

        return profiles;
    }

    public static async Task RemoveFriendAsync(
        ZaloHttpClient http, ZaloSession session, string friendId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(friendId);

        string primaryHost = GetHost(session, "friend", DefaultFriendHost);
        JsonObject payload = new()
        {
            ["fid"] = friendId.Trim(),
            ["imei"] = session.Material.Imei
        };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = $"/api/friend/remove?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "friend",
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);

        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = DecryptDataNode(session, node);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? node?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"RemoveFriend failed ({errorCode})", errorCode);
        }
    }

    public static async Task<IReadOnlyList<ZaloFriendRequestItem>> GetSentFriendRequestsAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = GetHost(session, "friend", DefaultFriendHost);
        JsonObject payload = new()
        {
            ["imei"] = session.Material.Imei
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/friend/requested/list?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("friend", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        int topError = node?["error_code"]?.GetValue<int>() ?? 0;
        if (topError == 112)
        {
            return [];
        }

        JsonNode? data = DecryptDataNode(session, node);
        int innerErr = data?["error_code"]?.GetValue<int>() ?? 0;
        if (innerErr == 112)
        {
            return [];
        }

        JsonNode? target = data?["data"] ?? data;
        if (target is not JsonObject obj)
        {
            return [];
        }

        List<ZaloFriendRequestItem> results = [];
        foreach (KeyValuePair<string, JsonNode?> kvp in obj)
        {
            if (kvp.Value is not JsonObject item)
            {
                continue;
            }

            string uid = item["userId"]?.GetValue<string>() ?? kvp.Key;
            string displayName = item["displayName"]?.GetValue<string>() ?? string.Empty;
            string zaloName = item["zaloName"]?.GetValue<string>() ?? string.Empty;
            string avatar = item["avatar"]?.GetValue<string>() ?? string.Empty;
            string msg = item["fReqInfo"]?["message"]?.GetValue<string>() ?? string.Empty;
            long time = item["fReqInfo"]?["time"]?.GetValue<long>() ?? 0;

            results.Add(new ZaloFriendRequestItem(uid, displayName, zaloName, avatar, msg, time, IsIncoming: false));
        }

        return results;
    }

    public static async Task<IReadOnlyList<ZaloFriendRequestItem>> GetFriendRecommendationsAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = GetHost(session, "friend", DefaultFriendHost);
        JsonObject payload = new()
        {
            ["imei"] = session.Material.Imei
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/friend/recommendsv2/list?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("friend", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? data = DecryptDataNode(session, node);
        JsonNode? target = data?["data"] ?? data;

        List<ZaloFriendRequestItem> results = [];
        if (target?["recommItems"] is JsonArray arr)
        {
            foreach (JsonNode? entry in arr)
            {
                JsonNode? info = entry?["dataInfo"] ?? entry;
                if (info is null)
                {
                    continue;
                }

                string uid = info["userId"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrEmpty(uid))
                {
                    continue;
                }

                string displayName = info["displayName"]?.GetValue<string>() ?? string.Empty;
                string zaloName = info["zaloName"]?.GetValue<string>() ?? string.Empty;
                string avatar = info["avatar"]?.GetValue<string>() ?? string.Empty;
                string msg = info["recommInfo"]?["message"]?.GetValue<string>() ?? string.Empty;
                long time = info["recommTime"]?.GetValue<long>() ?? 0;
                int recommType = info["recommType"]?.GetValue<int>() ?? 1;

                results.Add(new ZaloFriendRequestItem(uid, displayName, zaloName, avatar, msg, time, IsIncoming: recommType == 2));
            }
        }

        return results;
    }

    public static async Task<IReadOnlyList<ZaloAliasEntry>> GetAliasListAsync(
        ZaloHttpClient http, ZaloSession session, int page, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = GetHost(session, "alias", ZaloConstants.Hosts.Alias);
        JsonObject payload = new()
        {
            ["page"] = page > 0 ? page : 1,
            ["count"] = count > 0 ? count : 100,
            ["imei"] = session.Material.Imei
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/alias/list?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("alias", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? data = DecryptDataNode(session, node);
        JsonNode? target = data?["data"] ?? data;

        List<ZaloAliasEntry> results = [];
        if (target?["items"] is JsonArray itemsArr)
        {
            foreach (JsonNode? item in itemsArr)
            {
                string? uid = item?["userId"]?.GetValue<string>();
                string? alias = item?["alias"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(uid) && alias is not null)
                {
                    results.Add(new ZaloAliasEntry(uid, alias));
                }
            }
        }

        return results;
    }

    public static async Task RemoveFriendAliasAsync(
        ZaloHttpClient http, ZaloSession session, string friendId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(friendId);

        string primaryHost = GetHost(session, "alias", ZaloConstants.Hosts.Alias);
        JsonObject payload = new()
        {
            ["friendId"] = friendId.Trim()
        };

        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/alias/remove?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("alias", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = DecryptDataNode(session, node);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? node?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"RemoveFriendAlias failed ({errorCode})", errorCode);
        }
    }

    public static async Task BlockViewFeedAsync(
        ZaloHttpClient http, ZaloSession session, string userId, bool isBlocked, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string primaryHost = GetHost(session, "friend", DefaultFriendHost);
        JsonObject payload = new()
        {
            ["fid"] = userId.Trim(),
            ["isBlockFeed"] = isBlocked ? 1 : 0,
            ["imei"] = session.Material.Imei
        };

        string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        string path = $"/api/friend/feed/block?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync(
            "friend",
            primaryHost,
            path,
            HttpMethod.Post,
            () => new FormUrlEncodedContent([new KeyValuePair<string, string>("params", encryptedParams)]),
            ct: ct).ConfigureAwait(false);

        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = DecryptDataNode(session, node);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? node?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(root?["error_message"]?.GetValue<string>() ?? $"BlockViewFeed failed ({errorCode})", errorCode);
        }
    }
}
