// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;

namespace Zalo.Net.Auth;

/// <summary>
/// Managed HTTP client wrapper for executing Zalo web protocol HTTP requests.
/// Handles cookie jar synchronization, custom header injection, and manual redirect handling.
/// </summary>
public sealed class ZaloHttpClient : IDisposable
{
    private static readonly SocketsHttpHandler s_sharedHandler = new()
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip
                               | DecompressionMethods.Deflate
                               | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        EnableMultipleHttp2Connections = true
    };

    private static readonly HttpClient s_sharedClient = new(s_sharedHandler, disposeHandler: false);

    // Keyed by proxy instance identity; entries are reclaimed automatically once the caller's
    // IWebProxy is no longer referenced, instead of pinning a fresh handler forever.
    private static readonly ConditionalWeakTable<IWebProxy, HttpClient> s_proxyClients = new();

    private readonly HttpClient _http;
    private readonly bool _disposeClient;
    private readonly CookieStore _cookies;
    private readonly string _userAgent;

    /// <summary>
    /// Gets the associated <see cref="CookieStore"/>.
    /// </summary>
    public CookieStore Cookies => _cookies;

    /// <summary>
    /// Gets the configured <see cref="IWebProxy"/> if assigned.
    /// </summary>
    public IWebProxy? Proxy { get; }

    /// <summary>
    /// Gets or sets client-side privacy guard options (block typing, delivered, seen).
    /// </summary>
    public ZaloPrivacyOptions PrivacyOptions { get; set; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloHttpClient"/> class.
    /// </summary>
    public ZaloHttpClient(string userAgent, CookieStore? cookies = null, IWebProxy? proxy = null)
    {
        _userAgent = userAgent;
        _cookies = cookies ?? new CookieStore();
        this.Proxy = proxy;

        if (proxy is null)
        {
            _http = s_sharedClient;
            _disposeClient = false;
        }
        else
        {
            _http = GetOrCreateProxyClient(proxy);
            _disposeClient = false;
        }
    }

    private static HttpClient GetOrCreateProxyClient(IWebProxy proxy)
    {
        return s_proxyClients.GetValue(proxy, static p =>
        {
            SocketsHttpHandler handler = new()
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip
                                       | DecompressionMethods.Deflate
                                       | DecompressionMethods.Brotli,
                Proxy = p,
                UseProxy = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                EnableMultipleHttp2Connections = true
            };
            return new HttpClient(handler, disposeHandler: true);
        });
    }

    /// <summary>
    /// Checks whether an outbound URL should be intercepted by the client-side privacy guard.
    /// </summary>
    public bool IsBlockedByPrivacyGuard(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        ZaloPrivacyOptions opts = this.PrivacyOptions;
        if (!opts.BlockTyping && !opts.BlockDelivered && !opts.BlockSeen)
        {
            return false;
        }

        if (opts.BlockTyping &&
            (url.Contains("/api/message/typing", StringComparison.OrdinalIgnoreCase) ||
             url.Contains("/api/group/typing", StringComparison.OrdinalIgnoreCase) ||
             url.Contains("act=typing", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (opts.BlockDelivered &&
            (url.Contains("/api/message/delivered", StringComparison.OrdinalIgnoreCase) ||
             url.Contains("/api/group/delivered", StringComparison.OrdinalIgnoreCase) ||
             url.Contains("deliveredv2", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (opts.BlockSeen &&
            (url.Contains("/api/message/seen", StringComparison.OrdinalIgnoreCase) ||
             url.Contains("/api/group/seen", StringComparison.OrdinalIgnoreCase) ||
             url.Contains("seenv2", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sends an HTTP request across ordered candidate gateway hosts with automatic failover.
    /// </summary>
    public async Task<HttpResponseMessage> RequestWithFailoverAsync(
        string serviceKey,
        string? primaryHost,
        string pathAndQuery,
        HttpMethod method,
        Func<HttpContent?>? contentFactory = null,
        Dictionary<string, string>? extraHeaders = null,
        string origin = "https://chat.zalo.me",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pathAndQuery);

        string[] candidates = ZaloConstants.Hosts.GetCandidates(serviceKey, primaryHost);
        Exception? lastException = null;

        for (int i = 0; i < candidates.Length; i++)
        {
            string host = candidates[i].TrimEnd('/');
            string fullUrl = pathAndQuery.StartsWith('/')
                ? string.Concat(host, pathAndQuery)
                : string.Concat(host, "/", pathAndQuery);

            try
            {
                using HttpContent? body = contentFactory?.Invoke();
                HttpResponseMessage resp = await this.RequestAsync(fullUrl, method, body, extraHeaders, origin, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.Found || i == candidates.Length - 1)
                {
                    return resp;
                }
                resp.Dispose();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (i == candidates.Length - 1)
                {
                    throw;
                }
            }
        }

        throw lastException ?? new ZaloApiException($"All candidate hosts failed for service '{serviceKey}'.");
    }

    /// <summary>
    /// Sends an HTTP request with default Zalo headers and manual redirect handling.
    /// </summary>
    public async Task<HttpResponseMessage> RequestAsync(
        string url, HttpMethod method, HttpContent? body = null,
        Dictionary<string, string>? extraHeaders = null,
        string origin = "https://chat.zalo.me",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(method);

        if (this.IsBlockedByPrivacyGuard(url))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"error_code\":0,\"data\":{}}", System.Text.Encoding.UTF8, "application/json")
            };
        }

        const int maxRedirects = 5;
        string currentUrl = url;

        for (int i = 0; i < maxRedirects; i++)
        {
            using HttpRequestMessage req = new(method, currentUrl);
            this.AddDefaultHeaders(req, currentUrl, origin);
            if (extraHeaders != null)
            {
                foreach (KeyValuePair<string, string> kvp in extraHeaders)
                {
                    _ = req.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                }
            }
            if (body != null && method != HttpMethod.Get)
            {
                req.Content = body;
            }

            if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Http.RequestSent))
            {
                ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Http.RequestSent, new { Url = currentUrl, MethodName = method.Method });
            }

            HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

            if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Http.ResponseReceived))
            {
                ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Http.ResponseReceived, new { Url = currentUrl, StatusCode = (int)resp.StatusCode });
            }

            Uri uri = new(currentUrl);
            if (resp.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookieValues))
            {
                _cookies.AddCookies($"{uri.Scheme}://{uri.Host}", setCookieValues);
            }

            if (resp.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently
                                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect)
            {
                string? location = resp.Headers.Location?.ToString();
                if (location is null)
                {
                    return resp;
                }
                currentUrl = location.StartsWith("http", StringComparison.Ordinal) ? location : new Uri(new Uri(currentUrl), location).ToString();
                method = HttpMethod.Get;
                body = null;
                origin = $"{new Uri(currentUrl).Scheme}://id.zalo.me";
                continue;
            }

            return resp;
        }

        throw new ZaloApiException("Too many redirects");
    }

    /// <summary>
    /// Reads and parses JSON payload from response.
    /// </summary>
    public static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage resp, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resp);

        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.Found)
        {
            throw new ZaloApiException(string.Format(CultureInfo.InvariantCulture, "HTTP {0} {1}", (int)resp.StatusCode, resp.ReasonPhrase));
        }
        await using System.IO.Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private void AddDefaultHeaders(HttpRequestMessage req, string currentUrl, string origin)
    {
        _ = req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        _ = req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        _ = req.Headers.TryAddWithoutValidation("Origin", origin);
        _ = req.Headers.TryAddWithoutValidation("Referer", origin + "/");
        _ = req.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

        string cookieHeader = _cookies.GetCookieHeader(currentUrl);
        if (string.IsNullOrEmpty(cookieHeader) && !string.IsNullOrEmpty(origin))
        {
            cookieHeader = _cookies.GetCookieHeader(origin);
        }

        if (!string.IsNullOrEmpty(cookieHeader))
        {
            _ = req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposeClient)
        {
            _http.Dispose();
        }
    }
}
