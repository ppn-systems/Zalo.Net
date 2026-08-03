using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Contracts.Exceptions;

namespace Zalo.Net.Auth;

/// <summary>
/// Managed HTTP client wrapper for executing Zalo web protocol HTTP requests.
/// Handles cookie jar synchronization, custom header injection, and manual redirect handling.
/// </summary>
public sealed class ZaloHttpClient : IDisposable
{
    /// <summary>Default Zalo Web API type constant (30).</summary>
    public const int ApiType = 30;

    /// <summary>Default Zalo Web API version constant (671).</summary>
    public const int ApiVersion = 671;

    private readonly HttpClient _http;
    private readonly CookieStore _cookies;
    private readonly string _userAgent;

    /// <summary>
    /// Gets the associated <see cref="CookieStore"/>.
    /// </summary>
    public CookieStore Cookies => _cookies;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloHttpClient"/> class.
    /// </summary>
    public ZaloHttpClient(string userAgent, CookieStore? cookies = null)
    {
        _userAgent = userAgent;
        _cookies = cookies ?? new CookieStore();

        HttpClientHandler handler = new()
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip
                                   | DecompressionMethods.Deflate
                                   | DecompressionMethods.Brotli,
        };
        _http = new HttpClient(handler, disposeHandler: true);
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

            HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

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
        string content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    private void AddDefaultHeaders(HttpRequestMessage req, string currentUrl, string origin)
    {
        _ = req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        _ = req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        _ = req.Headers.TryAddWithoutValidation("Origin", origin);
        _ = req.Headers.TryAddWithoutValidation("Referer", origin + "/");
        _ = req.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

        string cookieHeader = _cookies.GetCookieHeader(currentUrl);
        string allCookies = _cookies.GetAllCookiesHeader();

        if (string.IsNullOrEmpty(cookieHeader))
        {
            cookieHeader = allCookies;
        }
        else if (!string.IsNullOrEmpty(allCookies))
        {
            cookieHeader = $"{cookieHeader}; {allCookies}";
        }

        _ = req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
