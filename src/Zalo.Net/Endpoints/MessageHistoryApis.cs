using System;
using System.Collections.Generic;
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

/// <summary>
/// Endpoint helpers for retrieving historical messages.
/// </summary>
public static class MessageHistoryApis
{
    private const string DefaultGroupHost = "https://group-wpa.chat.zalo.me";

    private static string MakeUrl(string baseUrl, string path)
    {
        string baseClean = baseUrl.EndsWith('/') ? baseUrl[..^1] : baseUrl;
        string sep = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseClean}{path}{sep}zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}";
    }

    private static JsonNode? DecryptDataNode(ZaloSession session, JsonNode? node)
    {
        if (node is not JsonObject root)
        {
            return null;
        }

        if (!root.TryGetPropertyValue("data", out JsonNode? dataNode) || dataNode is null)
        {
            return root;
        }

        if (dataNode is JsonValue val)
        {
            string encStr = val.ToString();
            if (!string.IsNullOrWhiteSpace(encStr))
            {
                string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr)
                                 ?? ZaloCipher.DecodeAesUtf8Key(session.Material.SecretKey, encStr);
                if (!string.IsNullOrWhiteSpace(decrypted))
                {
                    try
                    {
                        JsonNode? decodedJson = JsonNode.Parse(decrypted);
                        if (decodedJson is JsonObject obj)
                        {
                            if (obj.TryGetPropertyValue("data", out JsonNode? innerData) && innerData is JsonObject)
                            {
                                return innerData;
                            }
                            return obj;
                        }
                    }
                    catch
                    {
                        return root;
                    }
                }
            }
            return root;
        }

        return dataNode;
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

        List<string> hostsToTry = [];
        if (session.ServiceMap.TryGetValue("group", out string[]? mapHosts) && mapHosts.Length > 0)
        {
            foreach (string h in mapHosts)
            {
                if (!string.IsNullOrWhiteSpace(h))
                {
                    hostsToTry.Add(h.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? h : $"https://{h}");
                }
            }
        }
        hostsToTry.Add(DefaultGroupHost);

        JsonObject payload = new()
        {
            ["grid"] = threadId,
            ["count"] = count > 0 ? count : 50
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt getGroupChatHistory payload");
        }

        Exception? lastEx = null;

        foreach (string host in hostsToTry.Distinct())
        {
            string baseUrl = MakeUrl(host, "/api/group/history");
            string requestUrl = $"{baseUrl}&params={Uri.EscapeDataString(encryptedParams)}";

            Console.WriteLine($"[DEBUG LOG] Fetching group history from {host} for group {threadId}...");

            try
            {
                using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                    int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
                    if (errorCode == 0)
                    {
                        JsonNode? dataNode = DecryptDataNode(session, json) ?? json?["data"];
                        Console.WriteLine($"[DEBUG LOG] GetGroupHistory Success from {host}");
                        return dataNode;
                    }
                    string errMsg = json?["error_message"]?.GetValue<string>() ?? $"ErrorCode {errorCode}";
                    Console.WriteLine($"[DEBUG LOG] GetGroupHistory Response Error ({host}): code={errorCode}, msg='{errMsg}'");
                    lastEx = new ZaloApiException($"Không thể tải lịch sử tin nhắn nhóm từ Zalo Server ({errMsg}).", errorCode);
                }
                else
                {
                    Console.WriteLine($"[DEBUG LOG] GetGroupHistory HTTP Status ({host}): {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    lastEx = new ZaloApiException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG LOG] GetGroupHistory Exception ({host}): {ex.Message}");
                lastEx = ex;
            }
        }

        throw lastEx ?? new ZaloApiException("Không thể tải lịch sử tin nhắn nhóm từ bất kỳ máy chủ Zalo nào.");
    }
}
