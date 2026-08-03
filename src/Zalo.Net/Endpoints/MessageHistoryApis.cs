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

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1. Signed getoldmessages format (Nexus / Legacy Zalo Protocol)
        JsonObject signedObj = new()
        {
            ["imei"] = session.Material.Imei,
            ["count"] = count > 0 ? count : 50,
            ["grid"] = threadId
        };
        Dictionary<string, object?> dataDict = new()
        {
            ["imei"] = session.Material.Imei,
            ["count"] = count > 0 ? count : 50,
            ["grid"] = threadId
        };
        string dataJson = signedObj.ToJsonString();
        string? encryptedSigned = ZaloCipher.EncodeAes(session.Material.SecretKey, dataJson);
        string signKey = Hashing.GetSignKey("getoldmessages", dataDict);

        // 2. Simple getGroupChatHistory format (zca-js format)
        JsonObject simplePayload = new()
        {
            ["grid"] = threadId,
            ["count"] = count > 0 ? count : 50
        };
        string? encryptedSimple = ZaloCipher.EncodeAes(session.Material.SecretKey, simplePayload.ToJsonString());

        List<string> groupHosts = ["https://tt-group-wpa.chat.zalo.me", "https://tt-group2.zalo.me", "https://group2.zalo.me", "https://group-wpa.chat.zalo.me"];
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

        Exception? lastEx = null;

        foreach (string host in groupHosts.Distinct())
        {
            string hostClean = host.EndsWith('/') ? host[..^1] : host;

            // Pattern A: getmsglog with signkey & client_version (Nexus Protocol)
            if (!string.IsNullOrEmpty(encryptedSigned))
            {
                string urlA = $"{hostClean}/api/group/getmsglog?params={Uri.EscapeDataString(encryptedSigned)}&ts={ts}&signkey={signKey}&type={ZaloHttpClient.ApiType}&client_version={ZaloHttpClient.ApiVersion}";
                Console.WriteLine($"[DEBUG LOG] Pattern A (getmsglog): {urlA}");
                try
                {
                    using HttpResponseMessage resp = await http.RequestAsync(urlA, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
                        if (errorCode == 0)
                        {
                            JsonNode? dataNode = DecryptDataNode(session, json) ?? json?["data"];
                            Console.WriteLine($"[DEBUG LOG] Pattern A SUCCESS from {hostClean}");
                            return dataNode;
                        }
                        Console.WriteLine($"[DEBUG LOG] Pattern A -> code={errorCode}, msg='{json?["error_message"]?.GetValue<string>()}'");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG LOG] Pattern A -> HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG LOG] Pattern A -> Exception: {ex.Message}");
                    lastEx = ex;
                }
            }

            // Pattern B: history with zpw_ver & zpw_type (zca-js Protocol)
            if (!string.IsNullOrEmpty(encryptedSimple))
            {
                string urlB = $"{hostClean}/api/group/history?zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}&params={Uri.EscapeDataString(encryptedSimple)}";
                Console.WriteLine($"[DEBUG LOG] Pattern B (history): {urlB}");
                try
                {
                    using HttpResponseMessage resp = await http.RequestAsync(urlB, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
                        if (errorCode == 0)
                        {
                            JsonNode? dataNode = DecryptDataNode(session, json) ?? json?["data"];
                            Console.WriteLine($"[DEBUG LOG] Pattern B SUCCESS from {hostClean}");
                            return dataNode;
                        }
                        Console.WriteLine($"[DEBUG LOG] Pattern B -> code={errorCode}, msg='{json?["error_message"]?.GetValue<string>()}'");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG LOG] Pattern B -> HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG LOG] Pattern B -> Exception: {ex.Message}");
                    lastEx = ex;
                }
            }
        }

        throw lastEx ?? new ZaloApiException("Không thể tải lịch sử tin nhắn nhóm từ bất kỳ máy chủ Zalo nào.");
    }
}
