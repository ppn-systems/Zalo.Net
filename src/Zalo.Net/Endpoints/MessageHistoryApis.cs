using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Errors;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Endpoint helper for requesting historical message backfills.
/// </summary>
public static class MessageHistoryApis
{
    private const string OldDmUrl = "https://tt-chat2.zalo.me/api/message/lastmessages";
    private const string OldGroupUrl = "https://tt-group2.zalo.me/api/group/getmsglog";

    public static async Task<JsonNode?> GetOldMessagesAsync(
        ZaloHttpClient http, ZaloSession session,
        ZaloThreadType type, string? lastMsgId,
        CancellationToken ct)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = new Dictionary<string, object?>
        {
            ["imei"] = session.Material.Imei,
            ["count"] = 50,
            ["lastMsgId"] = lastMsgId ?? "0",
        };

        var url = type == ZaloThreadType.Group ? OldGroupUrl : OldDmUrl;

        var dataJson = JsonSerializer.Serialize(data);
        var encrypted = ZaloCipher.EncodeAes(session.Material.SecretKey, dataJson);
        var signKey = Hashing.GetSignKey("getoldmessages", data);

        var queryParams = new Dictionary<string, string>
        {
            ["params"] = encrypted,
            ["ts"] = ts.ToString(),
            ["signkey"] = signKey,
            ["type"] = ZaloHttpClient.ApiType.ToString(),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(),
        };

        var sb = new StringBuilder(url).Append('?');
        foreach (var (k, v) in queryParams)
            _ = sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v)).Append('&');

        var resp = await http.RequestAsync(sb.ToString().TrimEnd('&'), HttpMethod.Get, ct: ct);
        var json = await ZaloHttpClient.ReadJsonAsync(resp, ct);

        return (json?["error_code"]?.GetValue<int>() ?? -1) != 0
            ? throw new ZaloApiError(json?["error_message"]?.GetValue<string>() ?? "getOldMessages failed")
            : (json?["data"]);
    }
}
