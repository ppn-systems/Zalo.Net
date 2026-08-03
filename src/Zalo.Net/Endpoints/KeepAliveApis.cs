using System;
using System.Collections.Generic;
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
/// Endpoint helper for sending Zalo HTTP keep-alive requests.
/// </summary>
public static class KeepAliveApis
{
    private const string KeepAliveUrl = "https://tt-chat2.zalo.me/api/chat/keepalive";

    public static async Task SendAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = new Dictionary<string, object?>
        {
            ["imei"] = session.Material.Imei,
            ["type"] = ZaloHttpClient.ApiType,
            ["client_version"] = ZaloHttpClient.ApiVersion,
            ["ts"] = ts,
        };

        var dataJson = JsonSerializer.Serialize(data);
        var encrypted = ZaloCipher.EncodeAes(session.Material.SecretKey, dataJson);
        var signKey = Hashing.GetSignKey("keepalive", data);

        var queryParams = new Dictionary<string, string>
        {
            ["params"] = encrypted,
            ["ts"] = ts.ToString(),
            ["signkey"] = signKey,
            ["type"] = ZaloHttpClient.ApiType.ToString(),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(),
        };

        var sb = new StringBuilder(KeepAliveUrl).Append('?');
        foreach (var (k, v) in queryParams)
            _ = sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v)).Append('&');

        try
        {
            _ = await http.RequestAsync(sb.ToString().TrimEnd('&'), HttpMethod.Get, ct: ct);
        }
        catch { /* non-fatal */ }
    }
}
