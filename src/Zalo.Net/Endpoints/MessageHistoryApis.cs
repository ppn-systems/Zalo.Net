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

        JsonObject simplePayload = new()
        {
            ["grid"] = threadId,
            ["count"] = count > 0 ? count : 50
        };
        string? encryptedSimple = ZaloCipher.EncodeAes(session.Material.SecretKey, simplePayload.ToJsonString());

        List<string> groupHosts = ["https://tt-group-wpa.chat.zalo.me", "https://group-wpa.chat.zalo.me"];
        if (session.ServiceMap.TryGetValue("group", out string[]? hosts))
        {
            foreach (string h in hosts)
            {
                if (!string.IsNullOrWhiteSpace(h))
                {
                    groupHosts.Insert(0, h.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? h : $"https://{h}");
                }
            }
        }

        string[] pathsToScan = [
            "/api/group/history",
            "/api/group/getmsg",
            "/api/group/getmsglog",
            "/api/group/listmsg",
            "/api/group/msg"
        ];

        Exception? lastEx = null;

        foreach (string host in groupHosts.Distinct())
        {
            string hostClean = host.EndsWith('/') ? host[..^1] : host;

            foreach (string path in pathsToScan)
            {
                // Try GET
                if (!string.IsNullOrEmpty(encryptedSimple))
                {
                    string urlB = $"{hostClean}{path}?zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}&params={Uri.EscapeDataString(encryptedSimple)}";
                    try
                    {
                        using HttpResponseMessage resp = await http.RequestAsync(urlB, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                        string rawContent = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        Console.WriteLine($"[DEBUG LOG] GET {hostClean}{path} -> HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                        Console.WriteLine($"[DEBUG LOG] Raw Body snippet: '{(rawContent.Length > 200 ? rawContent[..200] : rawContent)}'");

                        if (resp.IsSuccessStatusCode)
                        {
                            JsonNode? json = string.IsNullOrWhiteSpace(rawContent) ? null : JsonNode.Parse(rawContent);
                            int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
                            if (errorCode == 0)
                            {
                                JsonNode? dataNode = DecryptDataNode(session, json) ?? json?["data"];
                                Console.WriteLine($"[DEBUG LOG] SUCCESS GET {hostClean}{path}");
                                return dataNode;
                            }
                            Console.WriteLine($"[DEBUG LOG] Error Code: {errorCode}, Msg: '{json?["error_message"]?.GetValue<string>()}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DEBUG LOG] GET {hostClean}{path} -> Exception: {ex.Message}");
                        lastEx = ex;
                    }
                }
            }
        }

        throw lastEx ?? new ZaloApiException("Không thể tải lịch sử tin nhắn nhóm từ bất kỳ máy chủ Zalo nào.");
    }
}
