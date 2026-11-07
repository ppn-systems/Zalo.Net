// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
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
    private const string FallbackLoginInfoUrl = "https://wpa.zaloapp.com/api/login/getLoginInfo";
    private const string ServerInfoUrl = "https://wpa.chat.zalo.me/api/login/getServerInfo";
    private const string LoginByPasswV2Url = "https://wpa.zaloapp.com/api/login/loginByPasswv2";
    private const string PasswordKey = "9fc5vEW3";

    /// <summary>
    /// Generates an 8-character random alphanumeric salt for password login encryption.
    /// </summary>
    public static string GenerateSalt()
    {
        const string Alpha = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> randomBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(randomBytes);
        Span<char> chars = stackalloc char[8];
        for (int i = 0; i < 8; i++)
        {
            chars[i] = Alpha[randomBytes[i] % Alpha.Length];
        }
        return new string(chars);
    }

    /// <summary>
    /// Encrypts a plaintext Zalo password using PasswordCipher (SHA-256 even/odd key derivation + AES-CBC).
    /// </summary>
    public static string EncryptPassword(string phone, string imei, string password, string salt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);
        ArgumentException.ThrowIfNullOrWhiteSpace(imei);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(salt);

        byte[] inputBytes = Encoding.UTF8.GetBytes($"{PasswordKey}-{phone}-{imei}-{salt}");
        byte[] hashBytes = SHA256.HashData(inputBytes);
        string hex = Convert.ToHexString(hashBytes); // 64 uppercase hex chars

        Span<char> enkChars = stackalloc char[32];
        for (int i = 0; i < 16; i++)
        {
            enkChars[i] = hex[i * 2]; // first 16 even chars
        }
        for (int i = 0; i < 16; i++)
        {
            // odd chars reversed (hex has 32 odd chars at indices 1, 3, ..., 63)
            enkChars[16 + i] = hex[63 - (i * 2)];
        }

        string enk = new(enkChars);

        char[] pwdChars = password.ToCharArray();
        Array.Reverse(pwdChars);
        string plain = string.Concat(salt, new string(pwdChars));

        return ZaloCipher.EncodeAesUtf8Key(enk, plain);
    }

    /// <summary>
    /// Authenticates via <c>POST /api/login/loginByPasswv2</c> using ParamCipher v2 and PasswordCipher.
    /// </summary>
    public static async Task<JsonNode?> LoginByPasswordAsync(
        ZaloHttpClient http,
        string phone,
        string password,
        string imei,
        string isoCountryCode = "VN",
        string language = "en",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(imei);

        string salt = GenerateSalt();
        string encryptedPwd = EncryptPassword(phone, imei, password, salt);
        long clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Dictionary<string, object?> payload = new()
        {
            ["phone_num"] = phone,
            ["iso_country_code"] = isoCountryCode,
            ["password"] = encryptedPwd,
            ["salt"] = salt,
            ["cptch_token"] = "",
            ["cptch_answer"] = "",
            ["make_answer"] = 0,
            ["language"] = language,
            ["logged_uids"] = null,
            ["computer_name"] = "Web",
            ["imei"] = imei,
        };

        ParamsEncryptor encryptor = new(ZaloConstants.Protocol.ApiType, imei, clientTime);
        string enk = encryptor.GetEncryptKey();
        string dataJson = JsonSerializer.Serialize(payload, AuthJsonContext.Default.DictionaryStringObject);
        string encrypted = encryptor.EncryptData(dataJson);
        (string zcid, string zcidExt, string encVer) = encryptor.GetParams();

        Dictionary<string, string> @params = new()
        {
            ["zcid"] = zcid,
            ["zcid_ext"] = zcidExt,
            ["enc_ver"] = encVer,
            ["params"] = encrypted,
            ["type"] = ZaloConstants.Protocol.ApiType.ToString(CultureInfo.InvariantCulture),
            ["client_version"] = ZaloConstants.Protocol.ApiVersion.ToString(CultureInfo.InvariantCulture),
        };

        @params["signkey"] = Hashing.GetSignKey("loginbypasswv2", @params.ToDictionary(k => k.Key, v => (object?)v.Value));

        string url = BuildUrl(LoginByPasswV2Url, @params, new Dictionary<string, string>
        {
            ["ref"] = ZaloConstants.Hosts.Origin,
        });

        using StringContent emptyForm = new(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded");
        HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: emptyForm, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        if (json?["data"] is JsonValue dataVal && dataVal.TryGetValue(out string? encResp) && !string.IsNullOrEmpty(encResp))
        {
            string? decrypted = ZaloCipher.DecodeAesUtf8Key(enk, encResp);
            if (!string.IsNullOrEmpty(decrypted))
            {
                json = JsonNode.Parse(decrypted);
            }
        }

        int errorCode = json?["error_code"]?.GetValue<int>() ?? 0;
        if (errorCode != 0)
        {
            throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? $"loginByPasswv2 failed with code {errorCode}", errorCode);
        }

        return json?["data"] ?? json;
    }

    /// <summary>Gets login info payload.</summary>
    public static async Task<JsonNode?> GetLoginInfoAsync(
        ZaloHttpClient http, string imei, string language, string encryptKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        (Dictionary<string, string> encParams, string enk) = await BuildEncryptedParams(imei, language, encryptKey, "getlogininfo").ConfigureAwait(false);

        string[] candidateUrls = [LoginInfoUrl, FallbackLoginInfoUrl];
        Exception? lastError = null;

        foreach (string baseUrl in candidateUrls)
        {
            try
            {
                string url = BuildUrl(baseUrl, encParams, new()
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new ZaloApiException("getLoginInfo failed on all candidate hosts");
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
