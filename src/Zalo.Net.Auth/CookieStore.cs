using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zalo.Net.Auth;

internal sealed class SerializedCookie
{
    public string? Key { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Domain { get; set; }
    public string? Path { get; set; }
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public DateTime? Expires { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<SerializedCookie>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class AuthJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Wraps a <see cref="CookieContainer"/> with serialization compatible with tough-cookie JSON.
/// </summary>
public sealed class CookieStore
{
    private readonly CookieContainer _jar = new();

    /// <summary>
    /// Gets the underlying <see cref="CookieContainer"/>.
    /// </summary>
    public CookieContainer Container => _jar;

    /// <summary>
    /// Adds Set-Cookie response header strings to the cookie container.
    /// </summary>
    public void AddCookies(string url, IEnumerable<string> setCookieHeaders)
    {
        ArgumentNullException.ThrowIfNull(setCookieHeaders);

        foreach (string header in setCookieHeaders)
        {
            try
            {
                _jar.SetCookies(new Uri(url), header);
            }
            catch { /* ignore malformed set-cookie */ }
        }
    }

    /// <summary>
    /// Gets the Cookie header string for the given URL.
    /// </summary>
    public string GetCookieHeader(string url) => _jar.GetCookieHeader(new Uri(url));

    /// <summary>
    /// Serializes cookies to tough-cookie compatible JSON format.
    /// </summary>
    public string ToJson()
    {
        List<SerializedCookie> list = [];
        foreach (Cookie c in _jar.GetAllCookies())
        {
            list.Add(new SerializedCookie
            {
                Key = c.Name,
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Path = c.Path,
                Secure = c.Secure,
                HttpOnly = c.HttpOnly,
                Expires = c.Expires == DateTime.MinValue ? null : c.Expires,
            });
        }
        return JsonSerializer.Serialize(list, AuthJsonContext.Default.ListSerializedCookie);
    }

    /// <summary>
    /// Restores a CookieStore from a tough-cookie JSON string.
    /// </summary>
    public static CookieStore FromJson(string json)
    {
        CookieStore store = new();
        List<SerializedCookie> cookies = JsonSerializer.Deserialize(json, AuthJsonContext.Default.ListSerializedCookie) ?? [];
        foreach (SerializedCookie c in cookies)
        {
            string name = c.Key ?? c.Name ?? "";
            string domain = c.Domain ?? "chat.zalo.me";
            if (domain.StartsWith('.'))
            {
                domain = domain[1..];
            }
            string url = $"https://{domain}";
            try
            {
                Cookie cookie = new(name, c.Value ?? "", c.Path ?? "/", domain)
                {
                    Secure = c.Secure,
                    HttpOnly = c.HttpOnly,
                };
                if (c.Expires.HasValue)
                {
                    cookie.Expires = c.Expires.Value;
                }
                store._jar.Add(new Uri(url), cookie);
            }
            catch { /* skip invalid cookies */ }
        }
        return store;
    }
}
