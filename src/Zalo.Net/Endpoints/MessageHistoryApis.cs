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
        string[] targetKeys = ["group", "group_cloud_message", "conversation", "chat"];
        foreach (string key in targetKeys)
        {
            if (session.ServiceMap.TryGetValue(key, out string[]? hosts))
            {
                foreach (string h in hosts)
                {
                    if (!string.IsNullOrWhiteSpace(h))
                    {
                        hostsToTry.Add(h.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? h : $"https://{h}");
                    }
                }
            }
        }
        hostsToTry.Add(DefaultGroupHost);

        string[] candidatePaths = [
            "/api/group/history",
            "/api/group/getmsg"
        ];

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
            foreach (string path in candidatePaths)
            {
                string baseUrl = MakeUrl(host, path);
                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));

                // Try GET
                try
                {
                    string reqUrlGet = $"{baseUrl}&params={Uri.EscapeDataString(encryptedParams)}";
                    Console.WriteLine($"[DEBUG LOG] GET {host}{path} ...");
                    using HttpResponseMessage respGet = await http.RequestAsync(reqUrlGet, HttpMethod.Get, ct: timeoutCts.Token).ConfigureAwait(false);
                    if (respGet.IsSuccessStatusCode)
                    {
                        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(respGet, timeoutCts.Token).ConfigureAwait(false);
                        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
                        if (errorCode == 0)
                        {
                            JsonNode? dataNode = DecryptDataNode(session, json) ?? json?["data"];
                            Console.WriteLine($"[DEBUG LOG] GetGroupHistory SUCCESS via GET {host}{path}");
                            return dataNode;
                        }
                        string errMsg = json?["error_message"]?.GetValue<string>() ?? $"ErrorCode {errorCode}";
                        Console.WriteLine($"[DEBUG LOG] GET {host}{path} -> Error: code={errorCode}, msg='{errMsg}'");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG LOG] GET {host}{path} -> HTTP {(int)respGet.StatusCode} {respGet.ReasonPhrase}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG LOG] GET {host}{path} -> {ex.Message}");
                    lastEx = ex;
                }

                // Try POST
                try
                {
                    Console.WriteLine($"[DEBUG LOG] POST {host}{path} ...");
                    using FormUrlEncodedContent postBody = new([new KeyValuePair<string, string>("params", encryptedParams)]);
                    using HttpResponseMessage respPost = await http.RequestAsync(baseUrl, HttpMethod.Post, body: postBody, ct: timeoutCts.Token).ConfigureAwait(false);
                    if (respPost.IsSuccessStatusCode)
                    {
                        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(respPost, timeoutCts.Token).ConfigureAwait(false);
                        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
                        if (errorCode == 0)
                        {
                            JsonNode? dataNode = DecryptDataNode(session, json) ?? json?["data"];
                            Console.WriteLine($"[DEBUG LOG] GetGroupHistory SUCCESS via POST {host}{path}");
                            return dataNode;
                        }
                        string errMsg = json?["error_message"]?.GetValue<string>() ?? $"ErrorCode {errorCode}";
                        Console.WriteLine($"[DEBUG LOG] POST {host}{path} -> Error: code={errorCode}, msg='{errMsg}'");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG LOG] POST {host}{path} -> HTTP {(int)respPost.StatusCode} {respPost.ReasonPhrase}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG LOG] POST {host}{path} -> {ex.Message}");
                    lastEx = ex;
                }
            }
        }

        throw lastEx ?? new ZaloApiException("Không thể tải lịch sử tin nhắn nhóm từ bất kỳ máy chủ Zalo nào.");
    }
}
