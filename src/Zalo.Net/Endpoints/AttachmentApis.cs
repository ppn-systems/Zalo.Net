using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Endpoint helpers for image attachment upload and image message sending.
/// </summary>
public static class AttachmentApis
{
    private const int ChunkSize = 5 * 1024 * 1024;

    /// <summary>Uploads an image attachment and sends an image message.</summary>
    public static async Task<ZaloSendResult> SendImageAttachmentAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName, string? caption,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(fileBytes);

        string photoId = await UploadImageAsync(http, session, threadId, threadType, fileBytes, fileName, ct).ConfigureAwait(false);
        return await SendImageMessageAsync(http, session, threadId, threadType, photoId, caption ?? "", ct).ConfigureAwait(false);
    }

    private static async Task<string> UploadImageAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName,
        CancellationToken ct)
    {
        string fileBaseUrl = session.ServiceMap["file"][0];
        bool isGroup = threadType == ZaloThreadType.Group;
        string urlPath = isGroup ? "api/group/photo_original/upload" : "api/message/photo_original/upload";
        string typeParam = isGroup ? "11" : "2";

        int totalSize = fileBytes.Length;
        int totalChunk = (int)Math.Ceiling((double)totalSize / ChunkSize);
        if (totalChunk == 0)
        {
            totalChunk = 1;
        }
        long clientId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string baseUrl = $"{fileBaseUrl.TrimEnd('/')}/{urlPath}";
        string? lastPhotoId = null;

        for (int i = 0; i < totalChunk; i++)
        {
            Dictionary<string, object> dataParams = new()
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

            string paramsJson = JsonSerializer.Serialize(dataParams, EndpointJsonContext.Default.DictionaryStringObject);
            string encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, paramsJson);

            int chunkLen = Math.Min(ChunkSize, totalSize - (i * ChunkSize));
            byte[] chunkBytes = new byte[chunkLen];
            Array.Copy(fileBytes, i * ChunkSize, chunkBytes, 0, chunkLen);

            using MultipartFormDataContent content = new();
            using ByteArrayContent byteContent = new(chunkBytes);
            byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            content.Add(byteContent, "chunkContent", fileName);

            string requestUrl = $"{baseUrl}?type={typeParam}&params={Uri.EscapeDataString(encryptedParams)}";
            HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Post, body: content, ct: ct).ConfigureAwait(false);
            System.Text.Json.Nodes.JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

            int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
            if (errorCode != 0)
            {
                throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "uploadImage failed", errorCode);
            }

            lastPhotoId = json?["data"]?["photoId"]?.GetValue<string>();
        }

        if (string.IsNullOrEmpty(lastPhotoId) || lastPhotoId == "-1")
        {
            throw new ZaloApiException("Failed to get photoId after upload");
        }

        return lastPhotoId;
    }

    private static async Task<ZaloSendResult> SendImageMessageAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string photoId, string text,
        CancellationToken ct)
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string cliMsg = Guid.NewGuid().ToString("N");

        Dictionary<string, object> data = new()
        {
            ["clientId"] = cliMsg,
            ["imei"] = session.Material.Imei,
            ["toid"] = threadId,
            ["msgType"] = "chat.photo",
            ["message"] = text,
            ["photoId"] = photoId
        };

        if (threadType == ZaloThreadType.Group)
        {
            data["grid"] = threadId;
        }

        string url = threadType == ZaloThreadType.Group
            ? $"{session.ServiceMap["group"][0]}/api/group"
            : $"{session.ServiceMap["chat"][0]}/api/message";

        string dataJson = JsonSerializer.Serialize(data, EndpointJsonContext.Default.DictionaryStringObject);
        string encrypted = ZaloCipher.EncodeAes(session.Material.SecretKey, dataJson);

        Dictionary<string, object?> signDict = data.ToDictionary(k => k.Key, v => (object?)v.Value);
        signDict["ts"] = ts;
        string signKey = Hashing.GetSignKey("sendmessage", signDict);

        Dictionary<string, string> queryParams = new()
        {
            ["params"] = encrypted,
            ["ts"] = ts.ToString(CultureInfo.InvariantCulture),
            ["signkey"] = signKey,
            ["nretry"] = "0",
            ["type"] = ZaloHttpClient.ApiType.ToString(CultureInfo.InvariantCulture),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(CultureInfo.InvariantCulture),
        };

        using FormUrlEncodedContent formBody = new(queryParams);
        StringBuilder sb = new StringBuilder(url).Append('?');
        foreach (KeyValuePair<string, string> kvp in queryParams)
        {
            _ = sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value)).Append('&');
        }

        string fullUrl = sb.ToString().TrimEnd('&');

        HttpResponseMessage resp = await http.RequestAsync(fullUrl, HttpMethod.Post, body: formBody, ct: ct).ConfigureAwait(false);
        System.Text.Json.Nodes.JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            throw new ZaloApiException(json?["error_message"]?.GetValue<string>() ?? "sendPhoto failed", errorCode);
        }

        string msgId = json?["data"]?["msgId"]?.ToJsonString()?.Trim('"')
                 ?? json?["data"]?["message_id"]?.ToJsonString()?.Trim('"')
                 ?? cliMsg;

        return new ZaloSendResult(msgId);
    }
}
