using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Contracts.Errors;

namespace Zalo.Net.Auth;

/// <summary>
/// Managed HTTP client wrapper for executing Zalo web protocol HTTP requests.
/// Handles cookie jar synchronization, custom header injection, and manual redirect handling.
/// </summary>
public sealed class ZaloHttpClient : IDisposable
{
    public const int ApiType = 30;
    public const int ApiVersion = 671;

    private readonly HttpClient _http;
    private readonly CookieStore _cookies;
    private readonly string _userAgent;

    public CookieStore Cookies => _cookies;

    public ZaloHttpClient(string userAgent, CookieStore? cookies = null)
    {
        _userAgent = userAgent;
        _cookies = cookies ?? new CookieStore();

        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip
                                   | DecompressionMethods.Deflate
                                   | DecompressionMethods.Brotli,
        };
        _http = new HttpClient(handler, disposeHandler: true);
    }

    public async Task<HttpResponseMessage> RequestAsync(
        string url, HttpMethod method, HttpContent? body = null,
        Dictionary<string, string>? extraHeaders = null,
        string origin = "https://chat.zalo.me",
        CancellationToken ct = default)
    {
        const int maxRedirects = 5;
        var currentUrl = url;

        for (int i = 0; i < maxRedirects; i++)
        {
            var req = new HttpRequestMessage(method, currentUrl);
            AddDefaultHeaders(req, origin);
            if (extraHeaders != null)
            {
                foreach (var (k, v) in extraHeaders)
                {
                    _ = req.Headers.TryAddWithoutValidation(k, v);
                }
            }
            if (body != null && method != HttpMethod.Get) req.Content = body;

            var resp = await _http.SendAsync(req, ct);

            var uri = new Uri(currentUrl);
            if (resp.Headers.TryGetValues("Set-Cookie", out var setCookieValues))
            {
                _cookies.AddCookies($"{uri.Scheme}://{uri.Host}", setCookieValues);
            }

            if (resp.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently
                                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect)
            {
                var location = resp.Headers.Location?.ToString();
                if (location is null) return resp;
                currentUrl = location.StartsWith("http", StringComparison.Ordinal) ? location : new Uri(new Uri(currentUrl), location).ToString();
                method = HttpMethod.Get;
                body = null;
                origin = $"{new Uri(currentUrl).Scheme}://id.zalo.me";
                continue;
            }

            return resp;
        }

        throw new ZaloApiError("Too many redirects");
    }

    public static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage resp, CancellationToken ct = default)
    {
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.Found)
            throw new ZaloApiError($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        var content = await resp.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    private void AddDefaultHeaders(HttpRequestMessage req, string origin)
    {
        _ = req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        _ = req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        _ = req.Headers.TryAddWithoutValidation("Origin", origin);
        _ = req.Headers.TryAddWithoutValidation("Referer", origin + "/");
        _ = req.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        _ = req.Headers.TryAddWithoutValidation("Cookie", _cookies.GetCookieHeader(origin));
    }

    public void Dispose() => _http.Dispose();
}
