// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Contracts.Exceptions;

namespace Zalo.Net.Auth;

/// <summary>
/// Statemachine helpers for Zalo QR code authentication flow.
/// </summary>
public static class LoginQrApis
{
    private static readonly Dictionary<string, string> s_chromeHeaders = new()
    {
        ["sec-ch-ua"] = "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\", \"Not?A_Brand\";v=\"99\"",
        ["sec-ch-ua-mobile"] = "?0",
        ["sec-ch-ua-platform"] = "\"Windows\"",
        ["sec-fetch-dest"] = "empty",
        ["sec-fetch-mode"] = "cors",
        ["sec-fetch-site"] = "same-origin",
        ["priority"] = "u=1, i",
    };

    /// <summary>Loads Zalo login page and extracts version.</summary>
    public static async Task<string> LoadLoginPageAsync(ZaloHttpClient http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        const string url = "https://id.zalo.me/account?continue=https%3A%2F%2Fchat.zalo.me%2F";
        Dictionary<string, string> headers = new()
        {
            ["accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
            ["accept-language"] = "vi-VN,vi;q=0.9,fr-FR;q=0.8,fr;q=0.7,en-US;q=0.6,en;q=0.5",
            ["cache-control"] = "max-age=0",
            ["sec-ch-ua"] = s_chromeHeaders["sec-ch-ua"],
            ["sec-ch-ua-mobile"] = s_chromeHeaders["sec-ch-ua-mobile"],
            ["sec-ch-ua-platform"] = s_chromeHeaders["sec-ch-ua-platform"],
            ["sec-fetch-dest"] = "document",
            ["sec-fetch-mode"] = "navigate",
            ["sec-fetch-site"] = "same-site",
            ["upgrade-insecure-requests"] = "1",
        };

        HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Get, extraHeaders: headers, origin: "https://chat.zalo.me", ct: ct).ConfigureAwait(false);
        string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        const string pattern = "https://stc-zlogin.zdn.vn/main-";
        int idx = html.IndexOf(pattern, StringComparison.Ordinal);
        if (idx < 0)
        {
            throw new ZaloApiException("Cannot extract login JS version from page");
        }

        int start = idx + pattern.Length;
        int end = html.IndexOf(".js", start, StringComparison.Ordinal);
        return end < 0 ? throw new ZaloApiException("Cannot extract login JS version from page") : html[start..end];
    }

    /// <summary>Fetches login info.</summary>
    public static async Task GetLoginInfoAsync(ZaloHttpClient http, string version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        using FormUrlEncodedContent form = new([
            new KeyValuePair<string, string>("continue", "https://zalo.me/pc"),
            new KeyValuePair<string, string>("v", version),
        ]);
        _ = await http.RequestAsync("https://id.zalo.me/account/logininfo", HttpMethod.Post,
            body: form, extraHeaders: WithChromeHeaders("https://id.zalo.me/account?continue=https%3A%2F%2Fzalo.me%2Fpc"),
            origin: "https://id.zalo.me", ct: ct).ConfigureAwait(false);
    }

    /// <summary>Verifies client session.</summary>
    public static async Task VerifyClientAsync(ZaloHttpClient http, string version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        using FormUrlEncodedContent form = new([
            new KeyValuePair<string, string>("type", "device"),
            new KeyValuePair<string, string>("continue", "https://zalo.me/pc"),
            new KeyValuePair<string, string>("v", version),
        ]);
        _ = await http.RequestAsync("https://id.zalo.me/account/verify-client", HttpMethod.Post,
            body: form, extraHeaders: WithChromeHeaders("https://id.zalo.me/account?continue=https%3A%2F%2Fzalo.me%2Fpc"),
            origin: "https://id.zalo.me", ct: ct).ConfigureAwait(false);
    }

    /// <summary>Generates QR code token.</summary>
    public static async Task<JsonNode> GenerateQrAsync(ZaloHttpClient http, string version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        using FormUrlEncodedContent form = new([
            new KeyValuePair<string, string>("continue", "https://zalo.me/pc"),
            new KeyValuePair<string, string>("v", version),
        ]);
        HttpResponseMessage resp = await http.RequestAsync("https://id.zalo.me/account/authen/qr/generate",
            HttpMethod.Post, body: form,
            extraHeaders: WithChromeHeaders("https://id.zalo.me/account?continue=https%3A%2F%2Fzalo.me%2Fpc"),
            origin: "https://id.zalo.me", ct: ct).ConfigureAwait(false);

        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        return json?["error_code"]?.GetValue<int>() != 0 || json["data"] is null
            ? throw new ZaloApiException($"QR generate failed: {json?["error_message"]}")
            : json["data"]!;
    }

    /// <summary>Polls waiting scan state.</summary>
    public static async Task<JsonNode?> WaitingScanAsync(
        ZaloHttpClient http, string version, string code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        while (!ct.IsCancellationRequested)
        {
            using FormUrlEncodedContent form = new([
                new KeyValuePair<string, string>("code",     code),
                new KeyValuePair<string, string>("continue", "https://chat.zalo.me/"),
                new KeyValuePair<string, string>("v",        version),
            ]);
            try
            {
                HttpResponseMessage resp = await http.RequestAsync("https://id.zalo.me/account/authen/qr/waiting-scan",
                    HttpMethod.Post, body: form,
                    extraHeaders: WithChromeHeaders("https://id.zalo.me/account?continue=https%3A%2F%2Fchat.zalo.me%2F"),
                    origin: "https://id.zalo.me", ct: ct).ConfigureAwait(false);

                JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
                if (errorCode == 8)
                {
                    continue;
                }
                return json;
            }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    /// <summary>Polls waiting confirm state.</summary>
    public static async Task<JsonNode?> WaitingConfirmAsync(
        ZaloHttpClient http, string version, string code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        while (!ct.IsCancellationRequested)
        {
            using FormUrlEncodedContent form = new([
                new KeyValuePair<string, string>("code",      code),
                new KeyValuePair<string, string>("gToken",    ""),
                new KeyValuePair<string, string>("gAction",   "CONFIRM_QR"),
                new KeyValuePair<string, string>("continue",  "https://chat.zalo.me/"),
                new KeyValuePair<string, string>("v",         version),
            ]);
            try
            {
                HttpResponseMessage resp = await http.RequestAsync("https://id.zalo.me/account/authen/qr/waiting-confirm",
                    HttpMethod.Post, body: form,
                    extraHeaders: WithChromeHeaders("https://id.zalo.me/account?continue=https%3A%2F%2Fchat.zalo.me%2F"),
                    origin: "https://id.zalo.me", ct: ct).ConfigureAwait(false);

                JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
                if (errorCode == 8)
                {
                    continue;
                }
                return json;
            }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    /// <summary>Checks session after confirm.</summary>
    public static async Task CheckSessionAsync(ZaloHttpClient http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        _ = await http.RequestAsync(
            "https://id.zalo.me/account/checksession?continue=https%3A%2F%2Fchat.zalo.me%2Findex.html",
            HttpMethod.Get,
            extraHeaders: new Dictionary<string, string>
            {
                ["accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                ["sec-fetch-dest"] = "document",
                ["sec-fetch-mode"] = "navigate",
                ["sec-fetch-site"] = "same-origin",
                ["upgrade-insecure-requests"] = "1",
            },
            origin: "https://id.zalo.me", ct: ct).ConfigureAwait(false);
    }

    /// <summary>Gets authenticated user profile info.</summary>
    public static async Task<JsonNode?> GetUserInfoAsync(ZaloHttpClient http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        HttpResponseMessage resp = await http.RequestAsync("https://jr.chat.zalo.me/jr/userinfo", HttpMethod.Get,
            extraHeaders: new Dictionary<string, string>
            {
                ["accept"] = "*/*",
                ["accept-language"] = "vi-VN,vi;q=0.9",
                ["sec-fetch-dest"] = "empty",
                ["sec-fetch-mode"] = "cors",
                ["sec-fetch-site"] = "same-site",
            },
            origin: "https://chat.zalo.me", ct: ct).ConfigureAwait(false);

        return await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
    }

    private static Dictionary<string, string> WithChromeHeaders(string referer)
    {
        Dictionary<string, string> d = new(s_chromeHeaders)
        {
            ["Referer"] = referer,
            ["Referrer-Policy"] = "strict-origin-when-cross-origin",
            ["accept-language"] = "vi-VN,vi;q=0.9,fr-FR;q=0.8,fr;q=0.7,en-US;q=0.6,en;q=0.5",
        };
        return d;
    }
}
