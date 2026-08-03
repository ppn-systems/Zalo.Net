using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Errors;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Endpoint helpers for image attachment upload and image message sending.
/// </summary>
public static class AttachmentApis
{
    private const int ChunkSize = 5 * 1024 * 1024;

    public static async Task<ZaloSendResult> SendImageAttachmentAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName, string? caption,
        CancellationToken ct)
    {
        var photoId = await UploadImageAsync(http, session, threadId, threadType, fileBytes, fileName, ct);
        return await SendImageMessageAsync(http, session, threadId, threadType, photoId, caption ?? "", ct);
    }

    private static async Task<string> UploadImageAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName,
        CancellationToken ct)
    {
        var fileBaseUrl = session.ServiceMap["file"][0];
        var isGroup = threadType == ZaloThreadType.Group;
        var urlPath = isGroup ? "api/group/photo_original/upload" : "api/message/photo_original/upload";
        var typeParam = isGroup ? "11" : "2";

        var totalSize = fileBytes.Length;
        var totalChunk = (int)Math.Ceiling((double)totalSize / ChunkSize);
        if (totalChunk == 0) totalChunk = 1;
        var clientId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var baseUrl = $"{fileBaseUrl.TrimEnd('/')}/{urlPath}";
        string? lastPhotoId = null;

        for (int i = 0; i < totalChunk; i++)
        {
            var dataParams = new Dictionary<string, object>
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

            if (isGroup) dataParams["grid"] = threadId;
            else dataParams["toid"] = threadId;

            var paramsJson = JsonSerializer.Serialize(dataParams);
            var encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, paramsJson);

            var chunkLen = Math.Min(ChunkSize, totalSize - (i * ChunkSize));
            var chunkBytes = new byte[chunkLen];
            Array.Copy(fileBytes, i * ChunkSize, chunkBytes, 0, chunkLen);

            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(chunkBytes);
            byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            content.Add(byteContent, "chunkContent", fileName);

            var requestUrl = $"{baseUrl}?type={typeParam}&params={Uri.EscapeDataString(encryptedParams)}";
            var resp = await http.RequestAsync(requestUrl, HttpMethod.Post, body: content, ct: ct);
            var json = await ZaloHttpClient.ReadJsonAsync(resp, ct);

            var errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
            if (errorCode != 0)
                throw new ZaloApiError(json?["error_message"]?.GetValue<string>() ?? "uploadImage failed", errorCode);

            lastPhotoId = json?["data"]?["photoId"]?.GetValue<string>();
        }

        if (string.IsNullOrEmpty(lastPhotoId) || lastPhotoId == "-1")
            throw new ZaloApiError("Failed to get photoId after upload");

        return lastPhotoId;
    }

    private static async Task<ZaloSendResult> SendImageMessageAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, string photoId, string text,
        CancellationToken ct)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cliMsg = Guid.NewGuid().ToString("N");

        var data = new Dictionary<string, object>
        {
            ["clientId"] = cliMsg,
            ["imei"] = session.Material.Imei,
            ["toid"] = threadId,
            ["msgType"] = "chat.photo",
            ["message"] = text,
            ["photoId"] = photoId
        };

        if (threadType == ZaloThreadType.Group)
            data["grid"] = threadId;

        var url = threadType == ZaloThreadType.Group
            ? $"{session.ServiceMap["group"][0]}/api/group"
            : $"{session.ServiceMap["chat"][0]}/api/message";

        var dataJson = JsonSerializer.Serialize(data);
        var encrypted = ZaloCipher.EncodeAes(session.Material.SecretKey, dataJson);

        var signDict = data.ToDictionary(k => k.Key, v => (object?)v.Value);
        signDict["ts"] = ts;
        var signKey = Hashing.GetSignKey("sendmessage", signDict);

        var queryParams = new Dictionary<string, string>
        {
            ["params"] = encrypted,
            ["ts"] = ts.ToString(),
            ["signkey"] = signKey,
            ["nretry"] = "0",
            ["type"] = ZaloHttpClient.ApiType.ToString(),
            ["client_version"] = ZaloHttpClient.ApiVersion.ToString(),
        };

        var formBody = new FormUrlEncodedContent(queryParams);
        var sb = new StringBuilder(url).Append('?');
        foreach (var (k, v) in queryParams)
            _ = sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v)).Append('&');

        var fullUrl = sb.ToString().TrimEnd('&');

        var resp = await http.RequestAsync(fullUrl, HttpMethod.Post, body: formBody, ct: ct);
        var json = await ZaloHttpClient.ReadJsonAsync(resp, ct);

        var errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
            throw new ZaloApiError(json?["error_message"]?.GetValue<string>() ?? "sendPhoto failed", errorCode);

        var msgId = json?["data"]?["msgId"]?.ToJsonString()?.Trim('"')
                 ?? json?["data"]?["message_id"]?.ToJsonString()?.Trim('"')
                 ?? cliMsg;

        return new ZaloSendResult(msgId);
    }
}
