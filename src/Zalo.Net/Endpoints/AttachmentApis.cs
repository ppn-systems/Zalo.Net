using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Endpoint helpers for image attachment and document file upload and sending.
/// </summary>
public static class AttachmentApis
{
    private const int ChunkSize = 5 * 1024 * 1024;
    private const string DefaultFileHost = "https://file-wpa.chat.zalo.me";

    private sealed record UploadResult(
        string PhotoId,
        string NormalUrl,
        string HdUrl,
        string ThumbUrl,
        int TotalSize);

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

    private static bool IsImageExtension(string fileName)
    {
        string ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext is "jpg" or "jpeg" or "png" or "webp" or "gif" or "bmp";
    }

    private static string? GetNodeString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        if (node is JsonValue val)
        {
            return val.ToString();
        }
        return node.GetValue<string>();
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

        if (dataNode is JsonValue)
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
                        return null;
                    }
                }
            }
            return null;
        }

        return dataNode;
    }

    /// <summary>Uploads an image or document file attachment and sends an attachment message.</summary>
    public static async Task<ZaloSendResult> SendImageAttachmentAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName, string? caption,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(fileBytes);

        UploadResult upload = await UploadFileAsync(http, session, threadId, threadType, fileBytes, fileName, ct).ConfigureAwait(false);
        return await SendFileMessageAsync(http, session, threadId, threadType, upload, fileName, caption ?? "", ct).ConfigureAwait(false);
    }

    private static async Task<UploadResult> UploadFileAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName,
        CancellationToken ct)
    {
        string fileBaseUrl = GetHost(session, "file", DefaultFileHost);
        bool isGroup = threadType == ZaloThreadType.Group;
        bool isImage = IsImageExtension(fileName);

        string urlPath = isImage
            ? (isGroup ? "/api/group/photo_original/upload" : "/api/message/photo_original/upload")
            : (isGroup ? "/api/group/asyncfile/upload" : "/api/message/asyncfile/upload");
        string typeParam = isGroup ? "11" : "2";

        int totalSize = fileBytes.Length;
        int totalChunk = (int)Math.Ceiling((double)totalSize / ChunkSize);
        if (totalChunk == 0)
        {
            totalChunk = 1;
        }
        long clientId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string baseUrl = MakeUrl(fileBaseUrl, urlPath);
        string? photoId = null;
        string? normalUrl = null;
        string? hdUrl = null;
        string? thumbUrl = null;

        for (int i = 0; i < totalChunk; i++)
        {
            JsonObject dataParams = new()
            {
                ["totalChunk"] = totalChunk,
                ["fileName"] = fileName,
                ["clientId"] = clientId,
                ["totalSize"] = totalSize,
                ["imei"] = session.Material.Imei,
                ["isE2EE"] = 0,
                ["jxl"] = 0,
                ["chunkId"] = i + 1
            };

            if (isGroup)
            {
                dataParams["grid"] = threadId;
            }
            else
            {
                dataParams["toid"] = threadId;
            }

            string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, dataParams.ToJsonString());
            if (string.IsNullOrEmpty(encryptedParams))
            {
                throw new ZaloApiException("Failed to encrypt upload file payload");
            }

            int chunkLen = Math.Min(ChunkSize, totalSize - (i * ChunkSize));
            byte[] chunkBytes = new byte[chunkLen];
            Array.Copy(fileBytes, i * ChunkSize, chunkBytes, 0, chunkLen);

            using MultipartFormDataContent content = new();
            using ByteArrayContent byteContent = new(chunkBytes);
            byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            content.Add(byteContent, "chunkContent", fileName);

            string requestUrl = $"{baseUrl}&type={typeParam}&params={Uri.EscapeDataString(encryptedParams)}";
            using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Post, body: content, ct: ct).ConfigureAwait(false);
            JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

            int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
            if (errorCode != 0)
            {
                throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "upload file failed", errorCode);
            }

            JsonNode? dataNode = DecryptDataNode(session, json) ?? (json?["data"] is JsonObject ? json["data"] : null);
            photoId = GetNodeString(dataNode?["photoId"]) ?? GetNodeString(dataNode?["fileId"]) ?? GetNodeString(dataNode?["photo_id"]);
            normalUrl = GetNodeString(dataNode?["normalUrl"]) ?? GetNodeString(dataNode?["fileUrl"]) ?? GetNodeString(dataNode?["url"]);
            hdUrl = GetNodeString(dataNode?["hdUrl"]) ?? normalUrl;
            thumbUrl = GetNodeString(dataNode?["thumbUrl"]) ?? normalUrl;
        }

        if (string.IsNullOrEmpty(photoId) || photoId == "-1")
        {
            throw new ZaloApiException("Failed to get fileId/photoId after upload");
        }

        return new UploadResult(photoId, normalUrl ?? "", hdUrl ?? "", thumbUrl ?? "", totalSize);
    }

    private static async Task<ZaloSendResult> SendFileMessageAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, UploadResult upload, string fileName, string text,
        CancellationToken ct)
    {
        bool isGroup = threadType == ZaloThreadType.Group;
        bool isImage = IsImageExtension(fileName);
        string fileHost = GetHost(session, "file", DefaultFileHost);

        string path = isImage
            ? (isGroup ? "/api/group/photo_original/send" : "/api/message/photo_original/send")
            : (isGroup ? "/api/group/asyncfile/msg" : "/api/message/asyncfile/msg");
        string url = MakeUrl(fileHost, path);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject payload;

        if (isImage)
        {
            payload = isGroup
                ? new JsonObject
                {
                    ["photoId"] = upload.PhotoId,
                    ["clientId"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["desc"] = text,
                    ["grid"] = threadId,
                    ["rawUrl"] = upload.NormalUrl,
                    ["hdUrl"] = upload.HdUrl,
                    ["thumbUrl"] = upload.ThumbUrl,
                    ["oriUrl"] = upload.NormalUrl,
                    ["hdSize"] = upload.TotalSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["zsource"] = -1,
                    ["ttl"] = 0,
                    ["jcp"] = "{\"convertible\":\"jxl\"}"
                }
                : new JsonObject
                {
                    ["photoId"] = upload.PhotoId,
                    ["clientId"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["desc"] = text,
                    ["toid"] = threadId,
                    ["rawUrl"] = upload.NormalUrl,
                    ["hdUrl"] = upload.HdUrl,
                    ["thumbUrl"] = upload.ThumbUrl,
                    ["normalUrl"] = upload.NormalUrl,
                    ["hdSize"] = upload.TotalSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["zsource"] = -1,
                    ["ttl"] = 0,
                    ["jcp"] = "{\"convertible\":\"jxl\"}"
                };
        }
        else
        {
            payload = isGroup
                ? new JsonObject
                {
                    ["fileId"] = upload.PhotoId,
                    ["clientId"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["desc"] = text,
                    ["grid"] = threadId,
                    ["fileUrl"] = upload.NormalUrl,
                    ["fileSize"] = upload.TotalSize,
                    ["fileName"] = fileName,
                    ["zsource"] = -1,
                    ["ttl"] = 0
                }
                : new JsonObject
                {
                    ["fileId"] = upload.PhotoId,
                    ["clientId"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["desc"] = text,
                    ["toid"] = threadId,
                    ["fileUrl"] = upload.NormalUrl,
                    ["fileSize"] = upload.TotalSize,
                    ["fileName"] = fileName,
                    ["zsource"] = -1,
                    ["ttl"] = 0
                };
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt sendFileMessage payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "sendFileMessage failed", errorCode);
        }

        JsonNode? dataNode = DecryptDataNode(session, json) ?? (json?["data"] is JsonObject ? json["data"] : null);
        string msgId = GetNodeString(dataNode?["msgId"])
                    ?? GetNodeString(dataNode?["message_id"])
                    ?? now.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new ZaloSendResult(msgId);
    }
}
