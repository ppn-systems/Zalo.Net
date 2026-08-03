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
using Zalo.Net.Contracts.Errors;
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

    public static async Task<string> SendTextAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string text,
        CancellationToken ct)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cliMsg = Guid.NewGuid().ToString("N");

        var data = new Dictionary<string, object>
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
            data["grid"] = threadId;

        var url = threadType == ZaloThreadType.Group ? SendGroupUrl : SendDmUrl;
        var (signedParams, body) = BuildSignedRequest(data, session.Material.SecretKey, ts);
        var fullUrl = BuildUrl(url, signedParams);

        var resp = await http.RequestAsync(fullUrl, HttpMethod.Post, body: body, ct: ct);
        var json = await ZaloHttpClient.ReadJsonAsync(resp, ct);

        var errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
            throw new ZaloApiError(json?["error_message"]?.GetValue<string>() ?? "sendMessage failed", errorCode);

        var msgId = json?["data"]?["msgId"]?.ToJsonString()?.Trim('"')
                 ?? json?["data"]?["message_id"]?.ToJsonString()?.Trim('"')
                 ?? cliMsg;
        return msgId;
    }

    public static async Task<(string Uid, string DisplayName, string? AvatarUrl)> GetUserInfoAsync(
        ZaloHttpClient http, ZaloSession session, string userId, CancellationToken ct)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = new Dictionary<string, object>
        {
            ["uid"] = userId,
            ["imei"] = session.Material.Imei,
        };
        var (signedParams, _) = BuildSignedRequest(data, session.Material.SecretKey, ts);
        var url = BuildUrl(UserInfoUrl, signedParams);

        var resp = await http.RequestAsync(url, HttpMethod.Get, ct: ct);
        var json = await ZaloHttpClient.ReadJsonAsync(resp, ct);

        if (json?["data"] is null)
            throw new ZaloApiError(json?["error_message"]?.GetValue<string>() ?? "getUserInfo failed");

        var profile = json["data"]!;
        var uid = profile["uid"]?.GetValue<string>() ?? userId;
        var displayName = profile["zaloName"]?.GetValue<string>()
                       ?? profile["displayName"]?.GetValue<string>() ?? "";
        var avatar = profile["avatar"]?.GetValue<string>()
                       ?? profile["avatarUrl"]?.GetValue<string>();

        return (uid, displayName, avatar);
    }

    private static (Dictionary<string, string> Params, HttpContent Body) BuildSignedRequest(
        Dictionary<string, object> data, string secretKey, long ts)
    {
        var dataJson = JsonSerializer.Serialize(data);
        var encrypted = ZaloCipher.EncodeAes(secretKey, dataJson);

        var signDict = data.ToDictionary(k => k.Key, v => (object?)v.Value);
        signDict["ts"] = ts;
        var signKey = Hashing.GetSignKey("sendmessage", signDict);

        var queryParams = new Dictionary<string, string>
        {
            ["params"] = encrypted,
            ["ts"] = ts.ToString(),
            ["signkey"] = signKey,
            ["nretry"] = "0",
            ["type"] = ZaloHttpClient.ApiType.ToString(),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(),
        };

        var formBody = new FormUrlEncodedContent(queryParams);
        return (queryParams, formBody);
    }

    private static string BuildUrl(string baseUrl, Dictionary<string, string> @params)
    {
        var sb = new StringBuilder(baseUrl).Append('?');
        foreach (var (k, v) in @params)
            _ = sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v)).Append('&');
        return sb.ToString().TrimEnd('&');
    }
}
