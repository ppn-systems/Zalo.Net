// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
    private const int ChunkSize = 512 * 1024; // 512 KB limit per Zalo Server Protocol
    private const string DefaultFileHost = "https://file-wpa.chat.zalo.me";

    private sealed record UploadResult(
        string PhotoId,
        string NormalUrl,
        string HdUrl,
        string ThumbUrl,
        string Checksum,
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
        return $"{baseClean}{path}{sep}zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";
    }

    private static bool IsImageExtension(string fileName)
    {
        string ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext is "jpg" or "jpeg" or "png" or "webp" or "gif" or "bmp";
    }

    private static string ComputeMd5Hex(byte[] bytes)
    {
        byte[] hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private static string? GetPropString(JsonNode? node, string propName)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(propName, out JsonNode? child))
        {
            return GetNodeString(child);
        }
        return null;
    }

    private static string? FindIdInJsonNode(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        if (node is JsonObject obj)
        {
            string[] preferredKeys = ["fileId", "photoId", "file_id", "photo_id", "clientFileId", "fId", "id"];
            foreach (string pk in preferredKeys)
            {
                if (obj.TryGetPropertyValue(pk, out JsonNode? child))
                {
                    string? s = GetNodeString(child);
                    if (!string.IsNullOrWhiteSpace(s) && s != "-1")
                    {
                        return s;
                    }
                }
            }

            foreach (KeyValuePair<string, JsonNode?> kvp in obj)
            {
                if (kvp.Key.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                {
                    string? s = GetNodeString(kvp.Value);
                    if (!string.IsNullOrWhiteSpace(s) && s != "-1")
                    {
                        return s;
                    }
                }
            }
        }
        return null;
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

    /// <summary>Uploads an image or document file attachment and sends an attachment message.</summary>
    public static async Task<ZaloSendResult> SendImageAttachmentAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, ZaloThreadType threadType, byte[] fileBytes, string fileName, string? caption,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(fileBytes);

        bool isImage = IsImageExtension(fileName);
        if (!isImage && !string.IsNullOrWhiteSpace(caption))
        {
            try
            {
                _ = await MessageApis.SendTextAsync(http, session, threadId, threadType, caption, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Internal.Warning))
                {
                    ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Internal.Warning, $"Send caption text failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

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
        string checksum = ComputeMd5Hex(fileBytes);

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

        Console.WriteLine($"[DEBUG LOG] Uploading file '{fileName}' ({totalSize} bytes, {totalChunk} chunks of 512KB) -> Host: {fileBaseUrl}, Path: {urlPath}");

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

            int errorCode = -1;
            if (json is JsonObject jObj && jObj.TryGetPropertyValue("error_code", out JsonNode? errNode) && errNode is JsonValue errVal)
            {
                if (!errVal.TryGetValue(out errorCode))
                {
                    errorCode = -1;
                }
            }

            if (errorCode != 0)
            {
                string errMsg = GetPropString(json, "error_message") ?? "upload file failed";
                Console.WriteLine($"[DEBUG LOG] Upload Error: code={errorCode}, msg='{errMsg}'");
                throw new ZaloApiException(errMsg, errorCode);
            }

            JsonNode? dataNode = DecryptDataNode(session, json);
            if (dataNode is JsonObject innerObj && innerObj.TryGetPropertyValue("error_code", out JsonNode? innerErrNode) && innerErrNode is JsonValue innerErrVal)
            {
                if (innerErrVal.TryGetValue(out int innerErr) && innerErr != 0)
                {
                    string errMsg = GetPropString(innerObj, "error_message") ?? "upload chunk failed";
                    Console.WriteLine($"[DEBUG LOG] Upload Chunk Error: code={innerErr}, msg='{errMsg}'");
                    throw new ZaloApiException(errMsg, innerErr);
                }
            }

            photoId = FindIdInJsonNode(dataNode)
                   ?? FindIdInJsonNode(json);

            normalUrl = GetPropString(dataNode, "normalUrl")
                     ?? GetPropString(dataNode, "fileUrl")
                     ?? GetPropString(dataNode, "url")
                     ?? GetPropString(dataNode, "downloadUrl")
                     ?? GetPropString(json, "normalUrl")
                     ?? GetPropString(json, "fileUrl")
                     ?? GetPropString(json, "url")
                     ?? "";

            hdUrl = GetPropString(dataNode, "hdUrl") ?? normalUrl;
            thumbUrl = GetPropString(dataNode, "thumbUrl") ?? normalUrl;
        }

        string finalId = photoId ?? clientId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!isImage && string.IsNullOrEmpty(normalUrl))
        {
            if (ZaloFileDoneRegistry.TryGet(finalId, out string? regUrl) && !string.IsNullOrEmpty(regUrl))
            {
                normalUrl = regUrl;
                Console.WriteLine($"[DEBUG LOG] Acquired fileUrl from WebSocket file_done: '{normalUrl}'");
            }
            else
            {
                Console.WriteLine($"[DEBUG LOG] Uploaded 100% data for fileId '{finalId}'. Waiting for WS file_done...");
                for (int wait = 0; wait < 150; wait++)
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    if (ZaloFileDoneRegistry.TryGet(finalId, out string? wUrl) && !string.IsNullOrEmpty(wUrl))
                    {
                        normalUrl = wUrl;
                        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Internal.Debug))
                        {
                            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Internal.Debug, $"Received file_done for fileId '{finalId}' after wait");
                        }
                        break;
                    }
                }
            }
        }

        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Internal.Debug))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Internal.Debug, $"Upload result: fileId='{finalId}', hasUrl={!string.IsNullOrEmpty(normalUrl)}");
        }
        return new UploadResult(finalId, normalUrl ?? "", hdUrl ?? "", thumbUrl ?? "", checksum, totalSize);
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
                    ["jcp"] = /*lang=json,strict*/ "{\"convertible\":\"jxl\"}"
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
                    ["jcp"] = /*lang=json,strict*/ "{\"convertible\":\"jxl\"}"
                };
        }
        else
        {
            string fileExt = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            payload = isGroup
                ? new JsonObject
                {
                    ["fileId"] = upload.PhotoId,
                    ["checksum"] = upload.Checksum,
                    ["checksumSha"] = "",
                    ["extention"] = fileExt,
                    ["totalSize"] = upload.TotalSize,
                    ["fileName"] = fileName,
                    ["clientId"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["fType"] = 1,
                    ["fileCount"] = 0,
                    ["fdata"] = "{}",
                    ["grid"] = threadId,
                    ["fileUrl"] = upload.NormalUrl,
                    ["zsource"] = -1,
                    ["ttl"] = 0
                }
                : new JsonObject
                {
                    ["fileId"] = upload.PhotoId,
                    ["checksum"] = upload.Checksum,
                    ["checksumSha"] = "",
                    ["extention"] = fileExt,
                    ["totalSize"] = upload.TotalSize,
                    ["fileName"] = fileName,
                    ["clientId"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["fType"] = 1,
                    ["fileCount"] = 0,
                    ["fdata"] = "{}",
                    ["toid"] = threadId,
                    ["fileUrl"] = upload.NormalUrl,
                    ["zsource"] = -1,
                    ["ttl"] = 0
                };
        }

        Console.WriteLine($"[DEBUG LOG] Sending file message via {path} (fileId='{upload.PhotoId}', ext='{Path.GetExtension(fileName)}')...");

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt sendFileMessage payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);

        int errorCode = -1;
        if (json is JsonObject jObj && jObj.TryGetPropertyValue("error_code", out JsonNode? errNode) && errNode is JsonValue errVal)
        {
            if (!errVal.TryGetValue(out errorCode))
            {
                errorCode = -1;
            }
        }

        if (errorCode != 0)
        {
            string errMsg = GetPropString(json, "error_message") ?? "sendFileMessage failed";
            Console.WriteLine($"[DEBUG LOG] Send File Error: code={errorCode}, msg='{errMsg}'");
            throw new ZaloApiException(errMsg, errorCode);
        }

        JsonNode? dataNode = DecryptDataNode(session, json) ?? json;
        string msgId = GetPropString(dataNode, "msgId")
                    ?? GetPropString(dataNode, "message_id")
                    ?? GetPropString(json, "msgId")
                    ?? GetPropString(json, "message_id")
                    ?? now.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Console.WriteLine($"[DEBUG LOG] Send File Success -> Zalo Server msgId='{msgId}'");
        return new ZaloSendResult(msgId);
    }
}
