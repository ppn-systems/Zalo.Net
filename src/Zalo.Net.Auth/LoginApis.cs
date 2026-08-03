// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

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
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Auth;

/// <summary>
/// Session login and server info endpoints.
/// </summary>
public static class LoginApis
{
    private const string LoginInfoUrl = "https://wpa.chat.zalo.me/api/login/getLoginInfo";
    private const string ServerInfoUrl = "https://wpa.chat.zalo.me/api/login/getServerInfo";

    /// <summary>Gets login info payload.</summary>
    public static async Task<JsonNode?> GetLoginInfoAsync(
        ZaloHttpClient http, string imei, string language, string encryptKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        (Dictionary<string, string> encParams, string enk) = await BuildEncryptedParams(imei, language, encryptKey, "getlogininfo").ConfigureAwait(false);

        string url = BuildUrl(LoginInfoUrl, encParams, new()
        {
            ["nretry"] = "0",
            ["type"] = ZaloConstants.Protocol.ApiType.ToString(CultureInfo.InvariantCulture),
            ["client_version"] = ZaloConstants.Protocol.ApiVersion.ToString(CultureInfo.InvariantCulture),
        });

        HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "getLoginInfo failed", errorCode);
        }

        string encryptedData = json?["data"]?.GetValue<string>()
                             ?? throw new ZaloApiException("getLoginInfo: missing data field");

        string decrypted = ZaloCipher.DecodeAesUtf8Key(enk, encryptedData)
                        ?? throw new ZaloApiException("getLoginInfo: failed to decrypt response");

        JsonNode? parsed = JsonNode.Parse(decrypted);

        try
        {
            if (parsed is JsonObject o)
            {
                if (o.ContainsKey("data"))
                {
                    return o["data"];
                }
                if (o.ContainsKey("zpw_enk"))
                {
                    return o;
                }
            }
        }
        catch { /* fallback to parsed */ }

        return parsed;
    }

    /// <summary>Gets server info payload.</summary>
    public static async Task<JsonNode?> GetServerInfoAsync(
        ZaloHttpClient http, string imei,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        string signKey = Hashing.GetSignKey("getserverinfo", new Dictionary<string, object?>
        {
            ["imei"] = imei,
            ["type"] = ZaloConstants.Protocol.ApiType,
            ["client_version"] = ZaloConstants.Protocol.ApiVersion,
            ["computer_name"] = "Web",
        });

        string url = BuildUrl(ServerInfoUrl, new Dictionary<string, string>
        {
            ["imei"] = imei,
            ["type"] = ZaloConstants.Protocol.ApiType.ToString(CultureInfo.InvariantCulture),
            ["client_version"] = ZaloConstants.Protocol.ApiVersion.ToString(CultureInfo.InvariantCulture),
            ["computer_name"] = "Web",
            ["signkey"] = signKey,
        }, null);

        HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        return json?["data"] is null
            ? throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "getServerInfo failed")
            : json["data"];
    }

    private static Task<(Dictionary<string, string> Params, string Enk)> BuildEncryptedParams(
        string imei, string language, string? encryptKey, string signType)
    {
        _ = encryptKey;
        Dictionary<string, object?> data = new()
        {
            ["computer_name"] = "Web",
            ["imei"] = imei,
            ["language"] = language,
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        ParamsEncryptor encryptor = new(ZaloConstants.Protocol.ApiType, imei, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        string enk = encryptor.GetEncryptKey();
        string dataJson = JsonSerializer.Serialize(data, AuthJsonContext.Default.DictionaryStringObject);
        string encrypted = encryptor.EncryptData(dataJson);
        (string zcid, string zcidExt, string encVer) = encryptor.GetParams();

        Dictionary<string, string> @params = new()
        {
            ["params"] = encrypted,
            ["zcid"] = zcid,
            ["zcid_ext"] = zcidExt,
            ["enc_ver"] = encVer,
            ["type"] = ZaloConstants.Protocol.ApiType.ToString(CultureInfo.InvariantCulture),
            ["client_version"] = ZaloConstants.Protocol.ApiVersion.ToString(CultureInfo.InvariantCulture),
        };

        @params["signkey"] = Hashing.GetSignKey(signType, @params.ToDictionary(k => k.Key, v => (object?)v.Value));

        return Task.FromResult((@params, enk));
    }

    private static string BuildUrl(string baseUrl, Dictionary<string, string> @params,
        Dictionary<string, string>? extra)
    {
        StringBuilder sb = new StringBuilder(baseUrl).Append('?');
        foreach (KeyValuePair<string, string> kvp in @params)
        {
            _ = sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value)).Append('&');
        }
        if (extra != null)
        {
            foreach (KeyValuePair<string, string> kvp in extra)
            {
                _ = sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value)).Append('&');
            }
        }
        return sb.ToString().TrimEnd('&');
    }
}
