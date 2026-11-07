// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;
using Zalo.Net.Cryptography;

namespace Zalo.Net.Endpoints;

/// <summary>
/// Endpoint helpers for sending, searching, suggesting, and browsing Zalo stickers.
/// </summary>
public static class StickerApis
{
    private static readonly long[] s_legacyNonCharPacks = [0, 50, 10788, 11190, 11984, 11998, 12002, 12003, 12024, 12658];

    /// <summary>Sends a Zalo sticker to a thread. Returns the server msgId and the client-generated cliMsgId (for de-duplication).</summary>
    public static async Task<ZaloSendResult> SendStickerAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, int stickerId, int cateId, int stickerType, ZaloThreadType type,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        bool isGroup = type == ZaloThreadType.Group;
        string endpointHost = isGroup
            ? (session.ServiceMap.TryGetValue("group", out string[]? gHosts) && gHosts.Length > 0 ? gHosts[0] : ZaloConstants.Hosts.Group)
            : (session.ServiceMap.TryGetValue("chat", out string[]? cHosts) && cHosts.Length > 0 ? cHosts[0] : ZaloConstants.Hosts.Chat);

        if (!endpointHost.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            endpointHost = $"https://{endpointHost}";
        }

        string path = isGroup ? "/api/group/sticker" : "/api/message/sticker";
        string url = $"{endpointHost.TrimEnd('/')}{path}?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&nretry=0";

        long clientId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject payload = new()
        {
            ["stickerId"] = stickerId,
            ["cateId"] = cateId,
            ["type"] = stickerType,
            ["clientId"] = clientId,
            ["imei"] = session.Material.Imei,
            ["zsource"] = 101
        };

        if (isGroup)
        {
            payload["grid"] = threadId;
        }
        else
        {
            payload["toid"] = threadId;
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Mã hóa tham số gửi sticker thất bại.");
        }

        Dictionary<string, string> formBody = new()
        {
            ["params"] = encryptedParams
        };

        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, new FormUrlEncodedContent(formBody), ct: ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new ZaloApiException($"Gửi sticker thất bại với mã HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }

        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = UnwrapDecrypt(json, session.Material.SecretKey);
        int errorCode = root?["error_code"]?.GetValue<int>() ?? json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = root?["error_message"]?.GetValue<string>() ?? json?["error_message"]?.GetValue<string>() ?? "Không rõ nguyên nhân";
            throw new ZaloApiException($"Zalo Server báo lỗi gửi sticker ({errorCode}): {msg}");
        }

        if (ZaloDiagnosticsEvents.Source.IsEnabled(ZaloDiagnosticsEvents.Message.StickerSent))
        {
            ZaloDiagnosticsEvents.Write(ZaloDiagnosticsEvents.Message.StickerSent, new { TargetThreadId = threadId, TargetThreadType = type.ToString(), StickerId = stickerId });
        }

        string cliMsgId = clientId.ToString(CultureInfo.InvariantCulture);
        string msgId = root?["data"]?["msgId"]?.GetValue<string>()
                    ?? root?["data"]?["message_id"]?.GetValue<string>()
                    ?? root?["msgId"]?.GetValue<string>()
                    ?? cliMsgId;
        return new ZaloSendResult(msgId, cliMsgId);
    }

    /// <summary>
    /// Normalizes a sticker keyword by collapsing whitespace and converting to lowercase.
    /// </summary>
    public static string NormalizeStickerKeyword(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return string.Empty;
        }

        string[] parts = keyword.Replace('\u00A0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    /// <summary>
    /// Strips Vietnamese diacritics to lowercase ASCII.
    /// </summary>
    public static string StripVietnameseDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder sb = new(text.Length);
        foreach (char raw in text)
        {
            char c = char.ToLowerInvariant(raw);
            char mapped = c switch
            {
                'à' or 'á' or 'ạ' or 'ả' or 'ã' or 'â' or 'ầ' or 'ấ' or 'ậ' or 'ẩ' or 'ẫ' or 'ă' or 'ằ' or 'ắ' or 'ặ' or 'ẳ' or 'ẵ' => 'a',
                'è' or 'é' or 'ẹ' or 'ẻ' or 'ẽ' or 'ê' or 'ề' or 'ế' or 'ệ' or 'ể' or 'ễ' => 'e',
                'ì' or 'í' or 'ị' or 'ỉ' or 'ĩ' => 'i',
                'ò' or 'ó' or 'ọ' or 'ỏ' or 'õ' or 'ô' or 'ồ' or 'ố' or 'ộ' or 'ổ' or 'ỗ' or 'ơ' or 'ờ' or 'ớ' or 'ợ' or 'ở' or 'ỡ' => 'o',
                'ù' or 'ú' or 'ụ' or 'ủ' or 'ũ' or 'ư' or 'ừ' or 'ứ' or 'ự' or 'ử' or 'ữ' => 'u',
                'ỳ' or 'ý' or 'ỵ' or 'ỷ' or 'ỹ' => 'y',
                'đ' => 'd',
                '\u00A0' => ' ',
                _ => c,
            };
            _ = sb.Append(mapped);
        }

        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Maps common unaccented Vietnamese keywords, English terms, or chat slang to the accented Vietnamese form indexed by Zalo Sticker Search.
    /// </summary>
    public static string? GetCanonicalAccentedSynonym(string stripped) => stripped switch
    {
        "nho" or "nho em" or "nho anh" or "nho qua" or "miss" or "miss u" or "miss you" => "nhớ",
        "khoc" or "huhu" or "huhuhu" or "hic" or "cry" => "khóc",
        "buon" or "buon qua" or "sad" or "tui than" => "buồn",
        "vui" or "vui ve" or "vui qua" or "happy" => "vui",
        "cuoi" or "mac cuoi" or "buon cuoi" => "cười",
        "yeu" or "iu" or "yeu em" or "yeu anh" or "iu em" or "iu anh" or "love" or "love u" or "love you" or "tha tim" or "tim" => "yêu",
        "thuong" or "thuong em" or "thuong anh" or "thuong qua" => "thương",
        "gian" or "gian doi" or "doi hon" or "tuc" or "buc" or "buc minh" or "cay" or "angry" => "giận",
        "met" or "met qua" or "met moi" or "duoi" or "duoi qua" or "tired" => "mệt",
        "doi" or "doi bung" or "doi qua" or "hungry" => "đói",
        "an" or "an com" or "di an" => "ăn cơm",
        "ca phe" or "cf" or "cafe" or "coffee" => "cà phê",
        "tra sua" or "trasua" => "trà sữa",
        "nhau" or "di nhau" or "bia" or "dzoo" or "dzo" => "nhậu",
        "ngu" or "di ngu" or "buon ngu" or "ngu ngon" or "ngu ngon nhe" or "ngu ngon nha" or "good night" or "goodnight" or "gn" or "g9" => "ngủ",
        "chao" or "xin chao" or "chao ban" or "chao anh" or "chao chi" or "chao em" or "he lo" or "helo" or "hello" or "hi" => "xin chào",
        "cam on" or "cam on ban" or "cam on anh" or "cam on chi" or "cam on em" or "cam on nhieu" or "thank" or "thanks" or "thank you" or "tks" or "tkss" or "thank kiu" => "cảm ơn",
        "xin loi" or "xin loi nha" or "xin loi nhe" or "sorry" or "sr" or "so ry" => "xin lỗi",
        "tam biet" or "tam biet nhe" or "bye" or "byebye" or "good bye" or "goodbye" or "bibi" or "pp" => "tạm biệt",
        "chuc mung" or "happy birthday" or "hpbd" or "sinh nhat" or "cmsn" => "chúc mừng",
        "tet" or "chuc mung nam moi" or "nam moi" => "tết",
        "co len" or "co gang" or "fighting" => "cố lên",
        "tuyet" or "tuyet voi" or "xuat sac" or "dinh" or "dinh qua" or "chuan" or "duoc" or "ok" => "tuyệt vời",
        "ngai" or "ngai qua" or "ngai nhi" or "xau ho" => "ngại",
        "so" or "so qua" or "scared" => "sợ",
        "lanh" or "lanh qua" or "lanh khong" or "ret" or "ret qua" => "lạnh",
        "nong" or "nong qua" or "nong buc" => "nóng",
        "tien" or "luong" => "tiền",
        "het tien" or "ngheo" or "ngheo qua" or "vi rong" => "hết tiền",
        "quy" or "lay" or "xin" or "van xin" => "lạy",
        "ban" or "ban qua" or "busy" => "bận",
        "chan" or "chan qua" or "chan doi" or "boring" => "chán",
        "cho" or "cho chut" or "cho xiu" or "doi chut" or "doi xiu" or "wait" => "chờ chút",
        "di choi" or "choi" or "dao pho" => "đi chơi",
        "di lam" or "lam viec" or "deadline" or "code" => "làm",
        "hong" or "hong chuyen" or "ngac nhien" => "ngạc nhiên",
        _ => null,
    };

    /// <summary>
    /// Builds deduplicated query variants (accented, unaccented, canonical synonym) for sticker search.
    /// </summary>
    public static string[] GetCanonicalStickerQueries(string keyword)
    {
        string norm = NormalizeStickerKeyword(keyword);
        if (string.IsNullOrEmpty(norm))
        {
            return [];
        }

        string stripped = StripVietnameseDiacritics(norm);
        List<string> queries = new(4) { norm };
        if (!string.IsNullOrEmpty(stripped) && stripped != norm && !queries.Contains(stripped))
        {
            queries.Add(stripped);
        }

        string? accented = GetCanonicalAccentedSynonym(stripped);
        if (!string.IsNullOrEmpty(accented))
        {
            if (!queries.Contains(accented))
            {
                queries.Add(accented);
            }
            string accStripped = StripVietnameseDiacritics(accented);
            if (!string.IsNullOrEmpty(accStripped) && !queries.Contains(accStripped))
            {
                queries.Add(accStripped);
            }
        }

        if (queries.Count > 4)
        {
            queries.RemoveRange(4, queries.Count - 4);
        }

        return [.. queries];
    }

    /// <summary>
    /// Fetches official Zalo contextual sticker trigger keywords (<c>GET /api/message/sticker/suggest/keywords</c>, cmd 11740).
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetStickerSuggestKeywordsAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.ServiceMap.TryGetValue("sticker", out string[]? hosts) && hosts.Length > 0
            ? hosts[0]
            : ZaloConstants.Hosts.Sticker;

        JsonObject payload = new() { ["imei"] = session.Material.Imei };
        string encParams = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string pathAndQuery = $"/api/message/sticker/suggest/keywords?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={encParams}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("sticker", primaryHost, pathAndQuery, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = UnwrapDecrypt(json, session.Material.SecretKey);

        if ((root?["data"]?["keywords"] ?? root?["keywords"]) is not JsonArray keywordsArr)
        {
            return [];
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> result = new(keywordsArr.Count * 2);
        foreach (JsonNode? item in keywordsArr)
        {
            string? raw = item?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string norm = NormalizeStickerKeyword(raw);
            if (string.IsNullOrEmpty(norm) || norm.StartsWith("zkwd_", StringComparison.Ordinal))
            {
                continue;
            }

            if (seen.Add(norm))
            {
                result.Add(norm);
            }

            string stripped = StripVietnameseDiacritics(norm);
            if (!string.IsNullOrEmpty(stripped) && seen.Add(stripped))
            {
                result.Add(stripped);
            }
        }

        return result;
    }

    /// <summary>
    /// Searches Zalo stickers by keyword across both <c>/api/message/sticker/suggest/stickers</c> and <c>/api/message/sticker/search</c>.
    /// </summary>
    public static async Task<IReadOnlyList<ZaloStickerItem>> SearchStickersAsync(
        ZaloHttpClient http, ZaloSession session, string keyword, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string kw = NormalizeStickerKeyword(keyword);
        if (string.IsNullOrEmpty(kw))
        {
            return [];
        }

        string primaryHost = session.ServiceMap.TryGetValue("sticker", out string[]? hosts) && hosts.Length > 0
            ? hosts[0]
            : ZaloConstants.Hosts.Sticker;

        string[] queries = GetCanonicalStickerQueries(kw);
        List<(int CateId, int StickerId, int Type, int Score)> rawItems = await QueryStickerEndpointsAsync(http, session, primaryHost, queries, ct).ConfigureAwait(false);

        if (rawItems.Count == 0)
        {
            string[] words = kw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is >= 2 and <= 5)
            {
                List<string> fallbacks = new(2);
                if (words.Length >= 3)
                {
                    fallbacks.Add($"{words[^2]} {words[^1]}");
                }
                if (words[^1].Length >= 2)
                {
                    fallbacks.Add(words[^1]);
                }

                foreach (string fb in fallbacks)
                {
                    string[] fbQueries = GetCanonicalStickerQueries(fb);
                    rawItems = await QueryStickerEndpointsAsync(http, session, primaryHost, fbQueries, ct).ConfigureAwait(false);
                    if (rawItems.Count > 0)
                    {
                        break;
                    }
                }
            }
        }

        bool hasModernChar = rawItems.Any(x => x.CateId >= 10000 && !s_legacyNonCharPacks.Contains(x.CateId));
        if (hasModernChar)
        {
            _ = rawItems.RemoveAll(x => x.CateId < 10000 || s_legacyNonCharPacks.Contains(x.CateId));
        }

        rawItems.Sort((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            return cmp != 0 ? cmp : b.CateId.CompareTo(a.CateId);
        });

        Dictionary<int, int> packCounts = new();
        List<ZaloStickerItem> output = new(24);
        List<(int CateId, int StickerId, int Type)> deferred = new();

        foreach ((int cid, int sid, int stype, _) in rawItems)
        {
            _ = packCounts.TryGetValue(cid, out int cnt);
            if (cnt >= 2)
            {
                deferred.Add((cid, sid, stype));
                continue;
            }
            packCounts[cid] = cnt + 1;
            output.Add(new ZaloStickerItem(sid, cid, stype, $"{kw} #{output.Count + 1}"));
            if (output.Count >= 24)
            {
                break;
            }
        }

        foreach ((int cid, int sid, int stype) in deferred)
        {
            if (output.Count >= 24)
            {
                break;
            }
            output.Add(new ZaloStickerItem(sid, cid, stype, $"{kw} #{output.Count + 1}"));
        }

        return output;
    }

    private static async Task<List<(int CateId, int StickerId, int Type, int Score)>> QueryStickerEndpointsAsync(
        ZaloHttpClient http, ZaloSession session, string primaryHost, string[] queries, CancellationToken ct)
    {
        Dictionary<int, (int CateId, int Type, int Score)> scoreMap = new();

        for (int i = 0; i < queries.Length; i++)
        {
            string q = queries[i];
            int weight = i == 0 ? 3 : 2;

            // 1) Contextual suggest endpoint (/api/message/sticker/suggest/stickers)
            try
            {
                JsonObject suggBody = new()
                {
                    ["keyword"] = q,
                    ["gif"] = 1,
                    ["guggy"] = 0,
                    ["imei"] = session.Material.Imei,
                };
                string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, suggBody.ToJsonString()));
                string path = $"/api/message/sticker/suggest/stickers?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";
                using HttpResponseMessage resp = await http.RequestWithFailoverAsync("sticker", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                JsonNode? root = UnwrapDecrypt(json, session.Material.SecretKey);
                JsonArray? arr = (root?["data"]?["sugg_sticker"] ?? root?["sugg_sticker"]) as JsonArray;
                MergeStickerArray(arr, weight, scoreMap);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* continue */ }

            // 2) Full tag search endpoint (/api/message/sticker/search)
            try
            {
                JsonObject searchBody = new()
                {
                    ["keyword"] = q,
                    ["limit"] = 30,
                    ["srcType"] = 0,
                    ["imei"] = session.Material.Imei,
                };
                string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, searchBody.ToJsonString()));
                string path = $"/api/message/sticker/search?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";
                using HttpResponseMessage resp = await http.RequestWithFailoverAsync("sticker", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
                JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
                JsonNode? root = UnwrapDecrypt(json, session.Material.SecretKey);
                JsonArray? arr = (root?["data"] ?? root) as JsonArray;
                MergeStickerArray(arr, weight, scoreMap);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* continue */ }
        }

        List<(int CateId, int StickerId, int Type, int Score)> list = new(scoreMap.Count);
        foreach (KeyValuePair<int, (int CateId, int Type, int Score)> kvp in scoreMap)
        {
            list.Add((kvp.Value.CateId, kvp.Key, kvp.Value.Type, kvp.Value.Score));
        }
        return list;
    }

    private static void MergeStickerArray(
        JsonArray? arr,
        int weight,
        Dictionary<int, (int CateId, int Type, int Score)> scoreMap)
    {
        if (arr is null)
        {
            return;
        }

        foreach (JsonNode? it in arr)
        {
            if (it is null)
            {
                continue;
            }

            int sid = ParseInt(it["sticker_id"] ?? it["id"]);
            if (sid <= 0)
            {
                continue;
            }
            int cid = ParseInt(it["cate_id"] ?? it["cateId"]);
            int stype = ParseInt(it["type"], 7);

            if (scoreMap.TryGetValue(sid, out (int CateId, int Type, int Score) existing))
            {
                scoreMap[sid] = (existing.CateId == 0 && cid > 0 ? cid : existing.CateId, existing.Type, existing.Score + weight);
            }
            else
            {
                scoreMap[sid] = (cid, stype, weight);
            }
        }
    }

    /// <summary>
    /// Fetches the Zalo Sticker Store catalog (<c>GET /api/message/sticker/category/list/v2</c> + CDN <c>getAllCate</c>).
    /// </summary>
    public static async Task<IReadOnlyList<ZaloStickerPack>> GetStickerStorePacksAsync(
        ZaloHttpClient http, ZaloSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string primaryHost = session.ServiceMap.TryGetValue("sticker", out string[]? hosts) && hosts.Length > 0
            ? hosts[0]
            : ZaloConstants.Hosts.Sticker;

        string encEmpty = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, ""));
        string path = $"/api/message/sticker/category/list/v2?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={encEmpty}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("sticker", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = UnwrapDecrypt(json, session.Material.SecretKey);
        JsonNode? data = root?["data"] ?? root;

        HashSet<long> excludedIds = new();
        if (data?["exCateIds"] is JsonArray exArr)
        {
            foreach (JsonNode? idNode in exArr)
            {
                long exId = ParseLong(idNode);
                if (exId > 0)
                {
                    _ = excludedIds.Add(exId);
                }
            }
        }

        string dataUrl = data?["dataUrl"]?.GetValue<string>() ?? "https://zalo-api.zadn.vn/api/emoticon/pcweb/getAllCate";
        if (dataUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            dataUrl = string.Concat("https://", dataUrl.AsSpan(7));
        }

        using HttpResponseMessage cdnResp = await http.RequestAsync(dataUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        byte[] rawBytes = await cdnResp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        string jsonText;
        if (rawBytes.Length >= 2 && rawBytes[0] == 0x1F && rawBytes[1] == 0x8B)
        {
            await using MemoryStream ms = new(rawBytes);
            await using GZipStream gz = new(ms, CompressionMode.Decompress);
            using StreamReader sr = new(gz, Encoding.UTF8);
            jsonText = await sr.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        else
        {
            jsonText = Encoding.UTF8.GetString(rawBytes);
        }

        JsonNode? cdnRoot = JsonNode.Parse(jsonText);
        if (cdnRoot is not JsonArray packArray)
        {
            return [];
        }

        List<ZaloStickerPack> packs = new(packArray.Count);
        foreach (JsonNode? item in packArray)
        {
            if (item is null)
            {
                continue;
            }
            long id = ParseLong(item["id"]);
            if (id <= 0 || excludedIds.Contains(id))
            {
                continue;
            }
            string name = (item["name"]?.GetValue<string>() ?? "").Trim();
            if (string.IsNullOrEmpty(name) || name.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            int totalImage = ParseInt(item["totalImage"], 16);
            string iconUrl = (item["iconUrl"]?.GetValue<string>() ?? item["thumbImg"]?.GetValue<string>() ?? "")
                .Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

            packs.Add(new ZaloStickerPack(id, name, totalImage, iconUrl));
        }

        packs.Sort((a, b) => b.Id.CompareTo(a.Id));
        return packs;
    }

    /// <summary>
    /// Fetches all sticker items belonging to a specific category (<c>GET /api/message/sticker/category/sticker_detail</c>, cmd 11284).
    /// </summary>
    public static async Task<IReadOnlyList<ZaloStickerItem>> GetStickerPackItemsAsync(
        ZaloHttpClient http, ZaloSession session, int cateId, string packName = "", CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        if (cateId == 0)
        {
            List<ZaloStickerItem> defaults = new(30);
            for (int i = 1; i <= 30; i++)
            {
                defaults.Add(new ZaloStickerItem(i, 0, 7, $"Củ hành #{i}"));
            }
            return defaults;
        }

        string primaryHost = session.ServiceMap.TryGetValue("sticker", out string[]? hosts) && hosts.Length > 0
            ? hosts[0]
            : ZaloConstants.Hosts.Sticker;

        JsonObject payload = new() { ["cid"] = cateId.ToString(CultureInfo.InvariantCulture) };
        string enc = Uri.EscapeDataString(ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString()));
        string path = $"/api/message/sticker/category/sticker_detail?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}&params={enc}";

        using HttpResponseMessage resp = await http.RequestWithFailoverAsync("sticker", primaryHost, path, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? root = UnwrapDecrypt(json, session.Material.SecretKey);
        if ((root?["data"] ?? root) is not JsonArray arr)
        {
            return [];
        }

        string prefix = string.IsNullOrWhiteSpace(packName) ? $"Pack {cateId}" : packName.Trim();
        List<ZaloStickerItem> items = new(arr.Count);
        for (int i = 0; i < arr.Count; i++)
        {
            JsonNode? it = arr[i];
            if (it is null)
            {
                continue;
            }
            int sid = ParseInt(it["id"] ?? it["stickerId"]);
            if (sid <= 0)
            {
                continue;
            }
            int cid = ParseInt(it["cateId"], cateId);
            int stype = ParseInt(it["type"], 7);
            items.Add(new ZaloStickerItem(sid, cid, stype, $"{prefix} #{i + 1}"));
        }

        return items;
    }

    internal static JsonNode? UnwrapDecrypt(JsonNode? node, string secretKey)
    {
        if (node is null)
        {
            return null;
        }

        if (node["data"] is JsonValue dataVal && dataVal.TryGetValue(out string? encStr) && !string.IsNullOrWhiteSpace(encStr))
        {
            string trimmed = encStr.Trim();
            if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
            {
                try { return JsonNode.Parse(trimmed); } catch { /* decrypt below */ }
            }

            if (!string.IsNullOrEmpty(secretKey))
            {
                try
                {
                    string? dec = ZaloCipher.DecodeAes(secretKey, trimmed);
                    if (!string.IsNullOrWhiteSpace(dec))
                    {
                        return JsonNode.Parse(dec);
                    }
                }
                catch { /* ignore */ }
            }
        }

        return node;
    }

    private static int ParseInt(JsonNode? node, int fallback = 0)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue(out int i))
            {
                return i;
            }
            if (v.TryGetValue(out long l))
            {
                return (int)l;
            }
            if (v.TryGetValue(out string? s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
        }
        return fallback;
    }

    private static long ParseLong(JsonNode? node, long fallback = 0)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue(out long l))
            {
                return l;
            }
            if (v.TryGetValue(out int i))
            {
                return i;
            }
            if (v.TryGetValue(out string? s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }
        }
        return fallback;
    }
}
