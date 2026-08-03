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
    private const string DefaultProfileHost = "https://profile-wpa.chat.zalo.me";
    private const string DefaultFriendHost = "https://friend-wpa.chat.zalo.me";

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
            ["count"] = count > 0 ? count : 20000,
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

        string host = GetHost(session, "alias", "https://alias-wpa.chat.zalo.me");
        string baseUrl = MakeUrl(host, "/api/alias/update");

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

        string requestUrl = $"{baseUrl}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from changeFriendAlias");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }
}
