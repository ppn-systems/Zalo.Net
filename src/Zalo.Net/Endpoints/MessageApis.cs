using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
    private const string SendDmUrl = "https://tt-chat2.zalo.me/api/message/sms";
    private const string SendGroupUrl = "https://tt-chat2.zalo.me/api/message/groupsms";
    private const string UserInfoUrl = "https://wpa.chat.zalo.me/api/social/profile/me";

    /// <summary>Sends a text message to a user or group thread.</summary>
    public static async Task<string> SendTextAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string text,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string cliMsg = Guid.NewGuid().ToString("N");

        Dictionary<string, object> data = new()
        {
            ["message"] = text,
            ["clientId"] = cliMsg,
            ["imei"] = session.Material.Imei,
            ["ttl"] = 0,
            ["toid"] = threadId,
            ["type"] = 1,
            ["msgType"] = "webchat",
        };

        if (threadType == ZaloThreadType.Group)
        {
            data["grid"] = threadId;
        }

        string url = threadType == ZaloThreadType.Group ? SendGroupUrl : SendDmUrl;
        (Dictionary<string, string> signedParams, HttpContent body) = BuildSignedRequest(data, session.Material.SecretKey, ts);
        string fullUrl = BuildUrl(url, signedParams);

        HttpResponseMessage resp = await http.RequestAsync(fullUrl, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        System.Text.Json.Nodes.JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "sendMessage failed", errorCode);
        }

        string msgId = json?["data"]?["msgId"]?.ToJsonString()?.Trim('"')
                 ?? json?["data"]?["message_id"]?.ToJsonString()?.Trim('"')
                 ?? cliMsg;
        return msgId;
    }

    /// <summary>Fetches profile information for a user.</summary>
    public static async Task<(string Uid, string DisplayName, string? AvatarUrl)> GetUserInfoAsync(
        ZaloHttpClient http, ZaloSession session, string userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Dictionary<string, object> data = new()
        {
            ["uid"] = userId,
            ["imei"] = session.Material.Imei,
        };
        (Dictionary<string, string> signedParams, _) = BuildSignedRequest(data, session.Material.SecretKey, ts);
        string url = BuildUrl(UserInfoUrl, signedParams);

        HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        System.Text.Json.Nodes.JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        if (json?["data"] is null)
        {
            throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "getUserInfo failed");
        }

        System.Text.Json.Nodes.JsonNode profile = json["data"]!;
        string uid = profile["uid"]?.GetValue<string>() ?? userId;
        string displayName = profile["zaloName"]?.GetValue<string>()
                       ?? profile["displayName"]?.GetValue<string>() ?? "";
        string? avatar = profile["avatar"]?.GetValue<string>()
                       ?? profile["avatarUrl"]?.GetValue<string>();

        return (uid, displayName, avatar);
    }

    private static (Dictionary<string, string> Params, HttpContent Body) BuildSignedRequest(
        Dictionary<string, object> data, string secretKey, long ts)
    {
        string dataJson = JsonSerializer.Serialize(data, EndpointJsonContext.Default.DictionaryStringObject);
        string encrypted = ZaloCipher.EncodeAes(secretKey, dataJson);

        Dictionary<string, object?> signDict = data.ToDictionary(k => k.Key, v => (object?)v.Value);
        signDict["ts"] = ts;
        string signKey = Hashing.GetSignKey("sendmessage", signDict);

        Dictionary<string, string> queryParams = new()
        {
            ["params"] = encrypted,
            ["ts"] = ts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["signkey"] = signKey,
            ["nretry"] = "0",
            ["type"] = ZaloHttpClient.ApiType.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        FormUrlEncodedContent formBody = new(queryParams);
        return (queryParams, formBody);
    }

    private static string BuildUrl(string baseUrl, Dictionary<string, string> @params)
    {
        StringBuilder sb = new StringBuilder(baseUrl).Append('?');
        foreach (KeyValuePair<string, string> kvp in @params)
        {
            _ = sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value)).Append('&');
        }
        return sb.ToString().TrimEnd('&');
    }
}
