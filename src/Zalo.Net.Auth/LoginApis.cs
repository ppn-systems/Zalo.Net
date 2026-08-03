using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Contracts.Errors;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Auth;

/// <summary>
/// Session login and server info endpoints.
/// </summary>
public static class LoginApis
{
    private const string LoginInfoUrl = "https://wpa.chat.zalo.me/api/login/getLoginInfo";
    private const string ServerInfoUrl = "https://wpa.chat.zalo.me/api/login/getServerInfo";

    public static async Task<JsonNode?> GetLoginInfoAsync(
        ZaloHttpClient http, string imei, string language, string encryptKey,
        CancellationToken ct = default)
    {
        var (encParams, enk) = await BuildEncryptedParams(imei, language, encryptKey, "getlogininfo");

        var url = BuildUrl(LoginInfoUrl, encParams, new()
        {
            ["nretry"] = "0",
            ["type"] = ZaloHttpClient.ApiType.ToString(),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(),
        });

        var resp = await http.RequestAsync(url, HttpMethod.Get, ct: ct);
        var json = await ZaloHttpClient.ReadJsonAsync(resp, ct);

        var errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
            throw new ZaloApiError(json?["error_message"]?.GetValue<string>() ?? "getLoginInfo failed", errorCode);

        var encryptedData = json?["data"]?.GetValue<string>();
        if (encryptedData is null) throw new ZaloApiError("getLoginInfo: missing data field");

        var decrypted = ZaloCipher.DecodeAesUtf8Key(enk, encryptedData)
                        ?? throw new ZaloApiError("getLoginInfo: failed to decrypt response");

        var parsed = JsonNode.Parse(decrypted);

        try
        {
            if (parsed is JsonObject o)
            {
                if (o.ContainsKey("data")) return o["data"];
                if (o.ContainsKey("zpw_enk")) return o;
            }
        }
        catch { /* fallback to parsed */ }

        return parsed;
    }

    public static async Task<JsonNode?> GetServerInfoAsync(
        ZaloHttpClient http, string imei,
        CancellationToken ct = default)
    {
        var signKey = Hashing.GetSignKey("getserverinfo", new Dictionary<string, object?>
        {
            ["imei"] = imei,
            ["type"] = ZaloHttpClient.ApiType,
            ["client_version"] = ZaloHttpClient.ApiVersion,
            ["computer_name"] = "Web",
        });

        var url = BuildUrl(ServerInfoUrl, new Dictionary<string, string>
        {
            ["imei"] = imei,
            ["type"] = ZaloHttpClient.ApiType.ToString(),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(),
            ["computer_name"] = "Web",
            ["signkey"] = signKey,
        }, null);

        var resp = await http.RequestAsync(url, HttpMethod.Get, ct: ct);
        var json = await ZaloHttpClient.ReadJsonAsync(resp, ct);

        return json?["data"] is null
            ? throw new ZaloApiError(json?["error_message"]?.GetValue<string>() ?? "getServerInfo failed")
            : json["data"];
    }

    private static Task<(Dictionary<string, string> Params, string Enk)> BuildEncryptedParams(
        string imei, string language, string? existingEnk, string signType)
    {
        var data = new Dictionary<string, object?>
        {
            ["computer_name"] = "Web",
            ["imei"] = imei,
            ["language"] = language,
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        var encryptor = new ParamsEncryptor(ZaloHttpClient.ApiType, imei, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var enk = encryptor.GetEncryptKey();
        var dataJson = JsonSerializer.Serialize(data);
        var encrypted = encryptor.EncryptData(dataJson);
        var (zcid, zcidExt, encVer) = encryptor.GetParams();

        var @params = new Dictionary<string, string>
        {
            ["params"] = encrypted,
            ["zcid"] = zcid,
            ["zcid_ext"] = zcidExt,
            ["enc_ver"] = encVer,
            ["type"] = ZaloHttpClient.ApiType.ToString(),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(),
        };

        @params["signkey"] = Hashing.GetSignKey(signType, @params.ToDictionary(k => k.Key, v => (object?)v.Value));

        return Task.FromResult((@params, enk));
    }

    private static string BuildUrl(string baseUrl, Dictionary<string, string> @params,
        Dictionary<string, string>? extra)
    {
        var sb = new StringBuilder(baseUrl).Append('?');
        foreach (var (k, v) in @params)
            _ = sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v)).Append('&');
        if (extra != null)
            foreach (var (k, v) in extra)
                _ = sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v)).Append('&');
        return sb.ToString().TrimEnd('&');
    }
}
