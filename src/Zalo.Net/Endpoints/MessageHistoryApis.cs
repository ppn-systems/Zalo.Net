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
            if (!string.IsNullOrWhiteSpace(encStr))
            {
                string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr)
                                 ?? ZaloCipher.DecodeAesUtf8Key(session.Material.SecretKey, encStr);
                if (!string.IsNullOrWhiteSpace(decrypted))
                {
                    try
                    {
                        JsonNode? decodedJson = JsonNode.Parse(decrypted);
                        if (decodedJson is JsonObject obj && obj.ContainsKey("data"))
                        {
                            return obj["data"];
                        }
                        return decodedJson;
                    }
                    catch
                    {
                        // Fallback to raw data node
                    }
                }
            }
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

        List<string> candidateHosts = [];
        if (session.ServiceMap.TryGetValue("group", out string[]? gh) && gh.Length > 0)
        {
            candidateHosts.Add(gh[0].StartsWith("http", StringComparison.OrdinalIgnoreCase) ? gh[0] : $"https://{gh[0]}");
        }
        if (session.ServiceMap.TryGetValue("group_poll", out string[]? gph) && gph.Length > 0)
        {
            candidateHosts.Add(gph[0].StartsWith("http", StringComparison.OrdinalIgnoreCase) ? gph[0] : $"https://{gph[0]}");
        }
        if (session.ServiceMap.TryGetValue("chat", out string[]? ch) && ch.Length > 0)
        {
            candidateHosts.Add(ch[0].StartsWith("http", StringComparison.OrdinalIgnoreCase) ? ch[0] : $"https://{ch[0]}");
        }
        candidateHosts.Add(DefaultGroupHost);
        candidateHosts.Add("https://tt-group2.zalo.me");
        candidateHosts.Add("https://groupms-chat2.zalo.me");

        string[] candidatePaths = ["/api/group/history", "/api/group/getmsglog", "/api/group/getmsg"];

        JsonObject payload1 = new()
        {
            ["grid"] = threadId,
            ["count"] = count > 0 ? count : 50
        };
        string? encParams1 = ZaloCipher.EncodeAes(session.Material.SecretKey, payload1.ToJsonString());

        JsonObject payload2 = new()
        {
            ["imei"] = session.Material.Imei,
            ["count"] = count > 0 ? count : 50,
            ["grid"] = threadId
        };
        string? encParams2 = ZaloCipher.EncodeAes(session.Material.SecretKey, payload2.ToJsonString());

        JsonNode? node = null;
        string? lastError = null;

        foreach (string host in candidateHosts.Distinct())
        {
            foreach (string path in candidatePaths)
            {
                string baseUrl = MakeUrl(host, path);

                if (!string.IsNullOrEmpty(encParams1))
                {
                    try
                    {
                        string reqUrl = $"{baseUrl}&params={Uri.EscapeDataString(encParams1)}";
                        using HttpResponseMessage resp = await http.RequestAsync(reqUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            JsonNode? candidate = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                            if (candidate?["error_code"]?.GetValue<int>() == 0)
                            {
                                node = candidate;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }

                if (node is null && !string.IsNullOrEmpty(encParams1))
                {
                    try
                    {
                        using FormUrlEncodedContent formBody = new([new KeyValuePair<string, string>("params", encParams1)]);
                        using HttpResponseMessage resp = await http.RequestAsync(baseUrl, HttpMethod.Post, body: formBody, ct: ct).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            JsonNode? candidate = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                            if (candidate?["error_code"]?.GetValue<int>() == 0)
                            {
                                node = candidate;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }

                if (node is null && !string.IsNullOrEmpty(encParams2))
                {
                    try
                    {
                        string reqUrl = $"{baseUrl}&params={Uri.EscapeDataString(encParams2)}";
                        using HttpResponseMessage resp = await http.RequestAsync(reqUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            JsonNode? candidate = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                            if (candidate?["error_code"]?.GetValue<int>() == 0)
                            {
                                node = candidate;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }

                if (node is not null)
                {
                    break;
                }
            }

            if (node is not null)
            {
                break;
            }
        }

        if (node is null)
        {
            throw new ZaloApiException($"Không thể tải lịch sử tin nhắn nhóm từ Zalo Server ({lastError ?? "404 Not Found"}).");
        }

        JsonNode? dataNode = DecryptDataNode(session, node) ?? node["data"];
        return dataNode;
    }
}
