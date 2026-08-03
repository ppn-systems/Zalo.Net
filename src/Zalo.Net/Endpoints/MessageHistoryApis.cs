using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Endpoint helpers for retrieving historical messages.
/// </summary>
public static class MessageHistoryApis
{
    private const string GetOldMsgsDmUrl = "https://tt-chat2.zalo.me/api/message/getoldmsg";
    private const string GetOldMsgsGroupUrl = "https://groupms-chat2.zalo.me/api/message/getoldmsg";

    /// <summary>Fetches old message history.</summary>
    public static async Task<JsonNode?> GetOldMessagesAsync(
        ZaloHttpClient http, ZaloSession session,
        ZaloThreadType type, string? lastMsgId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Dictionary<string, object> data = new()
        {
            ["imei"] = session.Material.Imei,
            ["count"] = 50,
            ["src"] = 1,
        };

        if (!string.IsNullOrEmpty(lastMsgId))
        {
            data["lastMsgId"] = lastMsgId;
        }

        string baseUrl = type == ZaloThreadType.Group ? GetOldMsgsGroupUrl : GetOldMsgsDmUrl;

        string dataJson = JsonSerializer.Serialize(data, EndpointJsonContext.Default.DictionaryStringObject);
        string encrypted = ZaloCipher.EncodeAes(session.Material.SecretKey, dataJson);

        Dictionary<string, object?> signDict = data.ToDictionary(k => k.Key, v => (object?)v.Value);
        signDict["ts"] = ts;
        string signKey = Hashing.GetSignKey("getoldmsg", signDict);

        Dictionary<string, string> queryParams = new()
        {
            ["params"] = encrypted,
            ["ts"] = ts.ToString(CultureInfo.InvariantCulture),
            ["signkey"] = signKey,
            ["nretry"] = "0",
            ["type"] = ZaloHttpClient.ApiType.ToString(CultureInfo.InvariantCulture),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(CultureInfo.InvariantCulture),
        };

        StringBuilder sb = new StringBuilder(baseUrl).Append('?');
        foreach (KeyValuePair<string, string> kvp in queryParams)
        {
            _ = sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value)).Append('&');
        }

        string fullUrl = sb.ToString().TrimEnd('&');
        HttpResponseMessage resp = await http.RequestAsync(fullUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        return (json?["error_code"]?.GetValue<int>() ?? -1) != 0
            ? throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "getOldMessages failed") : (json?["data"]);
    }
}
