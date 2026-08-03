using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zalo.Net.Auth;

/// <summary>
/// Wraps a <see cref="CookieContainer"/> with serialization compatible with tough-cookie JSON.
/// </summary>
public sealed class CookieStore
{
    private readonly CookieContainer _jar = new();

    public CookieContainer Container => _jar;

    public void AddCookies(string url, IEnumerable<string> setCookieHeaders)
    {
        foreach (var header in setCookieHeaders)
        {
            try
            {
                _jar.SetCookies(new Uri(url), header);
            }
            catch { /* ignore malformed set-cookie */ }
        }
    }

    public string GetCookieHeader(string url) => _jar.GetCookieHeader(new Uri(url));

    public string ToJson()
    {
        var list = new List<SerializedCookie>();
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
        return JsonSerializer.Serialize(list, SerializeOptions);
    }

    public static CookieStore FromJson(string json)
    {
        var store = new CookieStore();
        var cookies = JsonSerializer.Deserialize<List<SerializedCookie>>(json, SerializeOptions) ?? [];
        foreach (var c in cookies)
        {
            var name = c.Key ?? c.Name ?? "";
            var domain = c.Domain ?? "chat.zalo.me";
            if (domain.StartsWith('.')) domain = domain[1..];
            var url = $"https://{domain}";
            try
            {
                var cookie = new Cookie(name, c.Value ?? "", c.Path ?? "/", domain)
                {
                    Secure = c.Secure,
                    HttpOnly = c.HttpOnly,
                };
                if (c.Expires.HasValue) cookie.Expires = c.Expires.Value;
                store._jar.Add(new Uri(url), cookie);
            }
            catch { /* skip invalid cookies */ }
        }
        return store;
    }

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private sealed class SerializedCookie
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
}
