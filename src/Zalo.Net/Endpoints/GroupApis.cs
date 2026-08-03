using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

internal static class GroupApis
{
    private const string DefaultGroupHost = "https://group-wpa.chat.zalo.me";

    private static string GetGroupHost(ZaloSession session)
    {
        if (session.ServiceMap.TryGetValue("group", out string[]? hosts) && hosts.Length > 0)
        {
            return hosts[0].StartsWith("http", StringComparison.OrdinalIgnoreCase) ? hosts[0] : $"https://{hosts[0]}";
        }
        return DefaultGroupHost;
    }

    private static string MakeUrl(string baseUrl, string path)
    {
        string baseClean = baseUrl.EndsWith('/') ? baseUrl[..^1] : baseUrl;
        string sep = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseClean}{path}{sep}zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}";
    }

    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming", Justification = "JsonArray Add primitive string")]
    [SuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break in AOT", Justification = "JsonArray Add primitive string")]
    private static JsonArray ToJsonArray(IEnumerable<string> items)
    {
        JsonArray arr = [];
        foreach (string item in items)
        {
            arr.Add((JsonNode?)item);
        }
        return arr;
    }

    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming", Justification = "JsonArray Add primitive int")]
    [SuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break in AOT", Justification = "JsonArray Add primitive int")]
    private static JsonArray ToJsonArray(IEnumerable<int> items)
    {
        JsonArray arr = [];
        foreach (int item in items)
        {
            arr.Add((JsonNode?)item);
        }
        return arr;
    }

    private static string[] ToStringArray(JsonNode? node)
    {
        if (node is JsonArray arr)
        {
            List<string> list = [];
            foreach (JsonNode? item in arr)
            {
                if (item?.GetValue<string>() is { Length: > 0 } str)
                {
                    list.Add(str);
                }
            }
            return [.. list];
        }
        return [];
    }

    private static JsonNode? DecryptDataNode(ZaloSession session, JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        JsonNode? dataNode = node["data"];
        if (dataNode is null)
        {
            return null;
        }

        if (dataNode.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            string encStr = dataNode.GetValue<string>();
            if (string.IsNullOrWhiteSpace(encStr))
            {
                return null;
            }

            string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr)
                             ?? ZaloCipher.DecodeAesUtf8Key(session.Material.SecretKey, encStr);
            if (!string.IsNullOrWhiteSpace(decrypted))
            {
                try
                {
                    return JsonNode.Parse(decrypted);
                }
                catch
                {
                    // Fallback to raw data node
                }
            }
        }

        return dataNode;
    }

    public static async Task<ZaloGroupCreateResult> CreateGroupAsync(
        ZaloHttpClient http, ZaloSession session, string groupName, IEnumerable<string> memberIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        ArgumentNullException.ThrowIfNull(memberIds);

        string[] members = [.. memberIds];
        if (members.Length == 0)
        {
            throw new ZaloApiException("Group must have at least one member");
        }

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/group/create/v2");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject payload = new()
        {
            ["clientId"] = now,
            ["gname"] = groupName,
            ["gdesc"] = null,
            ["members"] = ToJsonArray(members),
            ["membersTypes"] = ToJsonArray(Enumerable.Repeat(-1, members.Length)),
            ["nameChanged"] = 1,
            ["createLink"] = 1,
            ["clientLang"] = session.Material.Language,
            ["imei"] = session.Material.Imei,
            ["zsource"] = 601
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt group creation payload");
        }

        string requestUrl = $"{url}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Post, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from createGroup");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? node["message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }

        JsonNode? data = DecryptDataNode(session, node);
        string groupId = data?["groupId"]?.GetValue<string>()
                      ?? data?["grid"]?.GetValue<string>()
                      ?? "";

        string[] successMembers = ToStringArray(data?["sucessMembers"] ?? data?["successMembers"]);
        string[] errorMembers = ToStringArray(data?["errorMembers"]);

        return new ZaloGroupCreateResult(groupId, successMembers, errorMembers);
    }

    public static async Task LeaveGroupAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, bool silent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/group/leave");

        JsonObject payload = new()
        {
            ["grids"] = new JsonArray { (JsonNode?)groupId },
            ["imei"] = session.Material.Imei,
            ["silent"] = silent ? 1 : 0,
            ["language"] = session.Material.Language
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt leaveGroup payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from leaveGroup");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }

    public static async Task AddUserToGroupAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, IEnumerable<string> memberIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(memberIds);

        string[] members = [.. memberIds];
        if (members.Length == 0)
        {
            return;
        }

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/group/invite/v2");

        JsonObject payload = new()
        {
            ["grid"] = groupId,
            ["members"] = ToJsonArray(members),
            ["memberTypes"] = ToJsonArray(Enumerable.Repeat(-1, members.Length)),
            ["imei"] = session.Material.Imei,
            ["clientLang"] = session.Material.Language
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt addUserToGroup payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from addUserToGroup");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }

    public static async Task RemoveUserFromGroupAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, IEnumerable<string> memberIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(memberIds);

        string[] members = [.. memberIds];
        if (members.Length == 0)
        {
            return;
        }

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/group/kick/v2");

        JsonObject payload = new()
        {
            ["grid"] = groupId,
            ["members"] = ToJsonArray(members),
            ["imei"] = session.Material.Imei,
            ["clientLang"] = session.Material.Language
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt removeUserFromGroup payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from removeUserFromGroup");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }

    public static async Task ChangeGroupNameAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, string newName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/group/updateinfo");

        JsonObject payload = new()
        {
            ["grid"] = groupId,
            ["gname"] = newName,
            ["imei"] = session.Material.Imei,
            ["clientLang"] = session.Material.Language
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt changeGroupName payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false)
                      ?? throw new ZaloApiException("Invalid JSON response from changeGroupName");

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }
    }
}
