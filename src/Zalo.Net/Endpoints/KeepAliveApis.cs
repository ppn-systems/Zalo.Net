using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Keep-alive ping endpoint helper.
/// </summary>
public static class KeepAliveApis
{
    private const string PingUrl = "https://wpa.chat.zalo.me/api/login/ping";

    /// <summary>Sends keep-alive ping request.</summary>
    public static async Task SendAsync(ZaloHttpClient http, ZaloSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Dictionary<string, object> data = new()
        {
            ["imei"] = session.Material.Imei,
            ["computer_name"] = "Web",
        };

        string dataJson = JsonSerializer.Serialize(data, EndpointJsonContext.Default.DictionaryStringObject);
        string encrypted = ZaloCipher.EncodeAes(session.Material.SecretKey, dataJson);

        Dictionary<string, object?> signDict = data.ToDictionary(k => k.Key, v => (object?)v.Value);
        signDict["ts"] = ts;
        string signKey = Hashing.GetSignKey("ping", signDict);

        Dictionary<string, string> queryParams = new()
        {
            ["params"] = encrypted,
            ["ts"] = ts.ToString(CultureInfo.InvariantCulture),
            ["signkey"] = signKey,
            ["nretry"] = "0",
            ["type"] = ZaloHttpClient.ApiType.ToString(CultureInfo.InvariantCulture),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(CultureInfo.InvariantCulture),
        };

        StringBuilder sb = new StringBuilder(PingUrl).Append('?');
        foreach (KeyValuePair<string, string> kvp in queryParams)
        {
            _ = sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value)).Append('&');
        }

        string fullUrl = sb.ToString().TrimEnd('&');
        HttpResponseMessage resp = await http.RequestAsync(fullUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        _ = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
    }
}
