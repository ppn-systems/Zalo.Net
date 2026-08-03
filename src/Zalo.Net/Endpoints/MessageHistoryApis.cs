using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Endpoint helpers for retrieving historical messages.
/// </summary>
public static class MessageHistoryApis
{
    private const string DefaultGroupHost = "https://group-wpa.chat.zalo.me";

    private static string GetHost(ZaloSession session, string serviceKey, string defaultHost)
    {
        if (session.ServiceMap.TryGetValue(serviceKey, out string[]? hosts) && hosts.Length > 0)
        {
            return hosts[0].StartsWith("http", StringComparison.OrdinalIgnoreCase) ? hosts[0] : $"https://{hosts[0]}";
        }
        return defaultHost;
    }

    private static string MakeUrl(string baseUrl, string path)
    {
        string baseClean = baseUrl.EndsWith('/') ? baseUrl[..^1] : baseUrl;
        string sep = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseClean}{path}{sep}zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}";
    }

    /// <summary>Fetches old message history for a group thread.</summary>
    public static async Task<JsonNode?> GetOldMessagesAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType type, int count = 50,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        if (type != ZaloThreadType.Group)
        {
            throw new ZaloApiException("Zalo Web API chỉ hỗ trợ tải lịch sử tin nhắn đối với Nhóm (Group). Tin nhắn cá nhân (DM) được đồng bộ qua WebSocket thời gian thực.");
        }

        string host = GetHost(session, "group", DefaultGroupHost);
        string[] paths = ["/api/group/history", "/api/group/getmsglog", "/api/group/getmsg"];

        JsonObject payload = new()
        {
            ["grid"] = threadId,
            ["count"] = count > 0 ? count : 50,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt getOldMessages payload");
        }

        HttpResponseMessage? lastResp = null;
        JsonNode? node = null;

        foreach (string path in paths)
        {
            string baseUrl = MakeUrl(host, path);
            string requestUrl = $"{baseUrl}&params={Uri.EscapeDataString(encryptedParams)}";
            try
            {
                HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                    if (node is not null)
                    {
                        break;
                    }
                }
                lastResp = resp;
            }
            catch (ZaloApiException ex) when (ex.Code == 404 || ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase))
            {
                // Try next fallback path
            }
        }

        if (node is null && lastResp is not null)
        {
            node = await ZaloHttpClient.ReadJsonAsync(lastResp, ct).ConfigureAwait(false);
        }

        if (node is null)
        {
            throw new ZaloApiException("Không thể tải lịch sử tin nhắn nhóm từ máy chủ Zalo.");
        }

        int errorCode = node["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = node["error_message"]?.GetValue<string>() ?? $"Error {errorCode}";
            throw new ZaloApiException(msg, errorCode);
        }

        JsonNode? dataNode = node["data"];
        if (dataNode?.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            string encStr = dataNode.GetValue<string>();
            string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr);
            if (!string.IsNullOrWhiteSpace(decrypted))
            {
                try { dataNode = JsonNode.Parse(decrypted); } catch { }
            }
        }

        return dataNode;
    }
}
