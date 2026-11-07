// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Endpoints for Group Polls (<c>/api/poll/*</c>), Group Board Notes (<c>/api/board/*</c>), and Official Zalo Reminders.
/// </summary>
internal static class BoardApis
{
    private const int DefaultZaloBoardColor = -16777216;

    private static string GetGroupHost(ZaloSession session)
        => session.GetServiceUrl("group")
        ?? session.GetServiceUrl("group_poll")
        ?? ZaloConstants.Hosts.Group;

    private static string GetBoardHost(ZaloSession session)
        => session.GetServiceUrl("group_board")
        ?? session.GetServiceUrl("boards")
        ?? ZaloConstants.Hosts.GroupBoard;

    private static string MakeUrl(string host, string path)
        => $"{host.TrimEnd('/')}{path}?zpw_ver={ZaloConstants.Protocol.ApiVersion}&zpw_type={ZaloConstants.Protocol.ApiType}";

    private static JsonNode? DecryptDataNode(ZaloSession session, JsonNode? root)
    {
        if (root is null)
        {
            return null;
        }

        int topErr = root["error_code"]?.GetValue<int>() ?? 0;
        if (topErr != 0)
        {
            string msg = root["error_message"]?.GetValue<string>() ?? $"API error {topErr}";
            throw new ZaloApiException(msg, topErr);
        }

        JsonNode? dataField = root["data"];
        if (dataField is JsonValue val && val.TryGetValue(out string? encStr) && !string.IsNullOrEmpty(encStr))
        {
            string? decrypted = ZaloCipher.DecodeAes(session.Material.SecretKey, encStr);
            if (!string.IsNullOrEmpty(decrypted))
            {
                JsonNode? inner = JsonNode.Parse(decrypted);
                int innerErr = (inner as JsonObject)?["error_code"]?.GetValue<int>() ?? 0;
                if (innerErr != 0)
                {
                    string msg = (inner as JsonObject)?["error_message"]?.GetValue<string>() ?? $"API inner error {innerErr}";
                    throw new ZaloApiException(msg, innerErr);
                }
                JsonNode? innerData = (inner as JsonObject)?["data"] ?? inner;
                if (innerData is JsonValue strVal && strVal.TryGetValue(out string? nestedJson) && !string.IsNullOrWhiteSpace(nestedJson))
                {
                    string trimmed = nestedJson.Trim();
                    if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
                    {
                        try { innerData = JsonNode.Parse(trimmed); } catch { }
                    }
                }
                return innerData;
            }
        }

        return dataField;
    }

    private static ZaloPollDetail ParsePollDetail(JsonNode? dataNode, long fallbackPollId = 0, string fallbackQuestion = "")
    {
        if (dataNode is null)
        {
            return new ZaloPollDetail(fallbackPollId, "", fallbackQuestion, false, true, true, false, false, 0, 0, 0, []);
        }

        long pollId = dataNode["poll_id"]?.GetValue<long>() ?? fallbackPollId;
        string creator = dataNode["creator"]?.GetValue<string>() ?? string.Empty;
        string question = dataNode["question"]?.GetValue<string>() ?? fallbackQuestion;
        bool closed = dataNode["closed"]?.GetValue<bool>() ?? false;
        bool multi = dataNode["allow_multi_choices"]?.GetValue<bool>() ?? true;
        bool addOpt = dataNode["allow_add_new_option"]?.GetValue<bool>() ?? true;
        bool anon = dataNode["is_anonymous"]?.GetValue<bool>() ?? false;
        bool hidePreview = dataNode["is_hide_vote_preview"]?.GetValue<bool>() ?? false;
        int numVote = dataNode["num_vote"]?.GetValue<int>() ?? 0;
        long createdTime = dataNode["created_time"]?.GetValue<long>() ?? 0;
        long expiredTime = dataNode["expired_time"]?.GetValue<long>() ?? 0;

        List<ZaloPollOption> options = [];
        if (dataNode["options"] is JsonArray optsArr)
        {
            foreach (JsonNode? opt in optsArr)
            {
                if (opt is null)
                {
                    continue;
                }

                long optId = opt["option_id"]?.GetValue<long>() ?? 0;
                string content = opt["content"]?.GetValue<string>() ?? string.Empty;
                int votes = opt["votes"]?.GetValue<int>() ?? 0;
                bool voted = opt["voted"]?.GetValue<bool>() ?? false;
                List<string> voters = [];
                if (opt["voters"] is JsonArray votersArr)
                {
                    foreach (JsonNode? v in votersArr)
                    {
                        string? vId = v?.GetValue<string>();
                        if (!string.IsNullOrEmpty(vId))
                        {
                            voters.Add(vId);
                        }
                    }
                }

                options.Add(new ZaloPollOption(optId, content, votes, voted, voters));
            }
        }

        return new ZaloPollDetail(
            pollId,
            creator,
            question,
            closed,
            multi,
            addOpt,
            anon,
            hidePreview,
            numVote,
            createdTime,
            expiredTime,
            options);
    }

    public static async Task<ZaloPollDetail> CreatePollAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, ZaloPollCreateOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Question);

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/poll/create");

        JsonArray optsArray = [];
        if (options.Options is not null)
        {
            foreach (string optText in options.Options)
            {
                optsArray.Add((JsonNode?)optText);
            }
        }

        JsonObject payload = new()
        {
            ["group_id"] = groupId,
            ["question"] = options.Question,
            ["options"] = optsArray,
            ["expired_time"] = options.ExpiredTime,
            ["pinAct"] = false,
            ["allow_multi_choices"] = options.AllowMultiChoices,
            ["allow_add_new_option"] = options.AllowAddNewOption,
            ["is_hide_vote_preview"] = options.HideVotePreview,
            ["is_anonymous"] = options.IsAnonymous,
            ["poll_type"] = 0,
            ["src"] = 1,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt CreatePoll payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode = DecryptDataNode(session, node) ?? node?["data"];

        return ParsePollDetail(dataNode, 0, options.Question);
    }

    public static async Task<ZaloPollDetail> GetPollDetailAsync(
        ZaloHttpClient http, ZaloSession session, long pollId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/poll/detail");

        JsonObject payload = new()
        {
            ["poll_id"] = pollId,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt GetPollDetail payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode = DecryptDataNode(session, node) ?? node?["data"];

        return ParsePollDetail(dataNode, pollId);
    }

    public static async Task<ZaloPollDetail> VotePollAsync(
        ZaloHttpClient http, ZaloSession session, long pollId, long[] optionIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(optionIds);

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/poll/vote");

        JsonArray optIdsArr = [];
        foreach (long id in optionIds)
        {
            optIdsArr.Add((JsonNode?)id);
        }

        JsonObject payload = new()
        {
            ["poll_id"] = pollId,
            ["option_ids"] = optIdsArr,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt VotePoll payload");
        }

        string requestUrl = $"{url}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode = DecryptDataNode(session, node) ?? node?["data"];

        return ParsePollDetail(dataNode, pollId);
    }

    public static async Task LockPollAsync(
        ZaloHttpClient http, ZaloSession session, long pollId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/poll/end");

        JsonObject payload = new()
        {
            ["poll_id"] = pollId,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt LockPoll payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        _ = DecryptDataNode(session, node);
    }

    public static async Task SharePollAsync(
        ZaloHttpClient http, ZaloSession session, long pollId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);

        string host = GetGroupHost(session);
        string url = MakeUrl(host, "/api/poll/share");

        JsonObject payload = new()
        {
            ["poll_id"] = pollId,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt SharePoll payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        _ = DecryptDataNode(session, node);
    }

    public static async Task<ZaloBoardItem> CreateGroupNoteAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, string title, bool pinToTop, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        string host = GetBoardHost(session);
        string url = MakeUrl(host, "/api/board/topic/createv2");

        JsonObject titleParams = new()
        {
            ["title"] = title
        };

        JsonObject payload = new()
        {
            ["grid"] = groupId,
            ["type"] = 0,
            ["color"] = DefaultZaloBoardColor,
            ["emoji"] = string.Empty,
            ["startTime"] = -1,
            ["duration"] = -1,
            ["params"] = titleParams.ToJsonString(),
            ["repeat"] = 0,
            ["src"] = 1,
            ["imei"] = session.Material.Imei,
            ["pinAct"] = pinToTop ? 1 : 0
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt CreateGroupNote payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode = DecryptDataNode(session, node) ?? node?["data"];

        string id = dataNode?["id"]?.GetValue<string>() ?? string.Empty;
        string creatorId = dataNode?["creatorId"]?.GetValue<string>() ?? session.Material.Uid;
        long createTime = dataNode?["createTime"]?.GetValue<long>() ?? 0;
        long editTime = dataNode?["editTime"]?.GetValue<long>() ?? createTime;

        return new ZaloBoardItem(id, 1, title, creatorId, createTime, editTime);
    }

    public static async Task<ZaloBoardItem> EditGroupNoteAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, string topicId, string title, bool pinToTop, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        string host = GetBoardHost(session);
        string url = MakeUrl(host, "/api/board/topic/updatev2");

        JsonObject titleParams = new()
        {
            ["title"] = title
        };

        JsonObject payload = new()
        {
            ["grid"] = groupId,
            ["type"] = 0,
            ["color"] = DefaultZaloBoardColor,
            ["emoji"] = string.Empty,
            ["startTime"] = -1,
            ["duration"] = -1,
            ["params"] = titleParams.ToJsonString(),
            ["topicId"] = topicId,
            ["repeat"] = 0,
            ["imei"] = session.Material.Imei,
            ["pinAct"] = pinToTop ? 1 : 2
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt EditGroupNote payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode = DecryptDataNode(session, node) ?? node?["data"];

        string id = dataNode?["id"]?.GetValue<string>() ?? topicId;
        string creatorId = dataNode?["creatorId"]?.GetValue<string>() ?? session.Material.Uid;
        long createTime = dataNode?["createTime"]?.GetValue<long>() ?? 0;
        long editTime = dataNode?["editTime"]?.GetValue<long>() ?? createTime;

        return new ZaloBoardItem(id, 1, title, creatorId, createTime, editTime);
    }

    public static async Task<IReadOnlyList<ZaloBoardItem>> GetGroupBoardListAsync(
        ZaloHttpClient http, ZaloSession session, string groupId, int page, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        string host = GetBoardHost(session);
        string url = MakeUrl(host, "/api/board/list");

        JsonObject payload = new()
        {
            ["group_id"] = groupId,
            ["board_type"] = 0,
            ["page"] = page > 0 ? page : 1,
            ["count"] = count > 0 ? count : 20,
            ["last_id"] = 0,
            ["last_type"] = 0,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt GetGroupBoardList payload");
        }

        string requestUrl = $"{url}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode;
        try
        {
            dataNode = DecryptDataNode(session, node) ?? node?["data"];
        }
        catch (ZaloApiException ex) when (ex.Code is -1 or 211)
        {
            return [];
        }

        List<ZaloBoardItem> results = [];
        if (dataNode?["items"] is JsonArray itemsArr)
        {
            foreach (JsonNode? item in itemsArr)
            {
                if (item is null)
                {
                    continue;
                }

                int boardType = item["board_type"]?.GetValue<int>() ?? 1;
                JsonNode? detail = item["data"];
                if (detail is null)
                {
                    continue;
                }

                if (boardType == 3)
                {
                    string pollId = (detail["poll_id"]?.GetValue<long>() ?? 0).ToString(CultureInfo.InvariantCulture);
                    string question = detail["question"]?.GetValue<string>() ?? string.Empty;
                    string creator = detail["creator"]?.GetValue<string>() ?? string.Empty;
                    long created = detail["created_time"]?.GetValue<long>() ?? 0;
                    long updated = detail["updated_time"]?.GetValue<long>() ?? created;
                    results.Add(new ZaloBoardItem(pollId, boardType, question, creator, created, updated));
                }
                else
                {
                    string id = detail["id"]?.ToString() ?? string.Empty;
                    string creator = detail["creatorId"]?.ToString() ?? string.Empty;
                    long created = detail["createTime"]?.GetValue<long>() ?? 0;
                    long updated = detail["editTime"]?.GetValue<long>() ?? created;
                    string title = string.Empty;

                    JsonNode? paramsField = detail["params"];
                    if (paramsField is JsonValue pVal && pVal.TryGetValue(out string? pStr) && !string.IsNullOrWhiteSpace(pStr))
                    {
                        try
                        {
                            JsonNode? parsedP = JsonNode.Parse(pStr);
                            title = parsedP?["title"]?.GetValue<string>() ?? pStr;
                        }
                        catch
                        {
                            title = pStr;
                        }
                    }
                    else if (paramsField is JsonObject pObj)
                    {
                        title = pObj["title"]?.GetValue<string>() ?? string.Empty;
                    }

                    results.Add(new ZaloBoardItem(id, boardType, title, creator, created, updated));
                }
            }
        }

        return results;
    }

    private static ZaloReminderItem ParseReminderItem(JsonNode? node, string fallbackThreadId, bool isGroup, string fallbackId = "", string fallbackTitle = "")
    {
        if (node is null)
        {
            return new ZaloReminderItem(fallbackId, fallbackThreadId, isGroup, fallbackTitle);
        }

        string id = node["reminderId"]?.ToString() ?? node["id"]?.ToString() ?? fallbackId;
        string creatorId = node["creatorUid"]?.ToString() ?? node["creatorId"]?.ToString() ?? string.Empty;
        string emoji = node["emoji"]?.GetValue<string>() ?? "⏰";
        long startTime = node["startTime"]?.GetValue<long>() ?? 0;
        long createTime = node["createTime"]?.GetValue<long>() ?? 0;
        int repeatInt = node["repeat"]?.GetValue<int>() ?? 0;
        ZaloReminderRepeatMode repeat = Enum.IsDefined(typeof(ZaloReminderRepeatMode), repeatInt)
            ? (ZaloReminderRepeatMode)repeatInt
            : ZaloReminderRepeatMode.None;

        string title = fallbackTitle;
        JsonNode? paramsNode = node["params"];
        if (paramsNode is JsonObject pObj)
        {
            title = pObj["title"]?.GetValue<string>() ?? fallbackTitle;
        }
        else if (paramsNode is JsonValue pVal && pVal.TryGetValue(out string? pStr) && !string.IsNullOrWhiteSpace(pStr))
        {
            try
            {
                JsonNode? parsed = JsonNode.Parse(pStr);
                title = parsed?["title"]?.GetValue<string>() ?? fallbackTitle;
            }
            catch
            {
                title = pStr;
            }
        }

        return new ZaloReminderItem(id, fallbackThreadId, isGroup, title, creatorId, emoji, startTime, repeat, createTime);
    }

    public static async Task<ZaloReminderItem> CreateReminderAsync(
        ZaloHttpClient http, ZaloSession session, string threadId, bool isGroup, ZaloReminderOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);

        string host = GetBoardHost(session);
        string path = isGroup ? "/api/board/topic/createv2" : "/api/board/oneone/create";
        string url = MakeUrl(host, path);

        long startTime = options.StartTimeMs > 0 ? options.StartTimeMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string emoji = options.Emoji ?? "⏰";
        int repeat = (int)options.Repeat;

        JsonObject payload;
        if (!isGroup)
        {
            JsonObject objectData = new()
            {
                ["toUid"] = threadId,
                ["type"] = 0,
                ["color"] = -16245706,
                ["emoji"] = emoji,
                ["startTime"] = startTime,
                ["duration"] = -1,
                ["params"] = new JsonObject { ["title"] = options.Title },
                ["needPin"] = false,
                ["repeat"] = repeat,
                ["creatorUid"] = session.Uid,
                ["src"] = 1
            };

            payload = new JsonObject
            {
                ["objectData"] = objectData.ToJsonString(),
                ["imei"] = session.Material.Imei
            };
        }
        else
        {
            JsonObject titleObj = new() { ["title"] = options.Title };
            payload = new JsonObject
            {
                ["grid"] = threadId,
                ["type"] = 0,
                ["color"] = DefaultZaloBoardColor,
                ["emoji"] = emoji,
                ["startTime"] = startTime,
                ["duration"] = -1,
                ["params"] = titleObj.ToJsonString(),
                ["repeat"] = repeat,
                ["src"] = 1,
                ["imei"] = session.Material.Imei,
                ["pinAct"] = options.PinAct ? 1 : 0
            };
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt CreateReminder payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode = DecryptDataNode(session, node) ?? node?["data"];

        return ParseReminderItem(dataNode, threadId, isGroup, "", options.Title);
    }

    public static async Task<ZaloReminderItem> EditReminderAsync(
        ZaloHttpClient http, ZaloSession session, string threadId, bool isGroup, string reminderId, ZaloReminderOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);

        string host = GetBoardHost(session);
        string path = isGroup ? "/api/board/topic/updatev2" : "/api/board/oneone/update";
        string url = MakeUrl(host, path);

        long startTime = options.StartTimeMs > 0 ? options.StartTimeMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string emoji = options.Emoji ?? "⏰";
        int repeat = (int)options.Repeat;

        JsonObject payload;
        if (!isGroup)
        {
            JsonObject objectData = new()
            {
                ["toUid"] = threadId,
                ["type"] = 0,
                ["color"] = DefaultZaloBoardColor,
                ["emoji"] = emoji,
                ["startTime"] = startTime,
                ["duration"] = -1,
                ["params"] = new JsonObject { ["title"] = options.Title },
                ["needPin"] = false,
                ["reminderId"] = reminderId,
                ["repeat"] = repeat
            };

            payload = new JsonObject
            {
                ["objectData"] = objectData.ToJsonString()
            };
        }
        else
        {
            JsonObject titleObj = new() { ["title"] = options.Title };
            payload = new JsonObject
            {
                ["grid"] = threadId,
                ["type"] = 0,
                ["color"] = DefaultZaloBoardColor,
                ["emoji"] = emoji,
                ["startTime"] = startTime,
                ["duration"] = -1,
                ["params"] = titleObj.ToJsonString(),
                ["topicId"] = reminderId,
                ["repeat"] = repeat,
                ["imei"] = session.Material.Imei,
                ["pinAct"] = options.PinAct ? 1 : 2
            };
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt EditReminder payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode = DecryptDataNode(session, node) ?? node?["data"];

        return ParseReminderItem(dataNode, threadId, isGroup, reminderId, options.Title);
    }

    public static async Task RemoveReminderAsync(
        ZaloHttpClient http, ZaloSession session, string threadId, bool isGroup, string reminderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderId);

        string host = GetBoardHost(session);
        string path = isGroup ? "/api/board/topic/remove" : "/api/board/oneone/remove";
        string url = MakeUrl(host, path);

        JsonObject payload = isGroup
            ? new JsonObject
            {
                ["grid"] = threadId,
                ["topicId"] = reminderId,
                ["imei"] = session.Material.Imei
            }
            : new JsonObject
            {
                ["uid"] = threadId,
                ["reminderId"] = reminderId
            };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt RemoveReminder payload");
        }

        using FormUrlEncodedContent body = new([new KeyValuePair<string, string>("params", encryptedParams)]);
        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, body: body, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        _ = DecryptDataNode(session, node);
    }

    public static async Task<ZaloReminderItem> GetReminderAsync(
        ZaloHttpClient http, ZaloSession session, string reminderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderId);

        string host = GetBoardHost(session);
        string url = MakeUrl(host, "/api/board/topic/getReminder");

        JsonObject payload = new()
        {
            ["eventId"] = reminderId,
            ["imei"] = session.Material.Imei
        };

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt GetReminder payload");
        }

        string requestUrl = $"{url}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode = DecryptDataNode(session, node) ?? node?["data"];

        string groupId = dataNode?["groupId"]?.GetValue<string>() ?? string.Empty;
        return ParseReminderItem(dataNode, groupId, true, reminderId);
    }

    public static async Task<IReadOnlyList<ZaloReminderItem>> GetListReminderAsync(
        ZaloHttpClient http, ZaloSession session, string threadId, bool isGroup, int page, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        string host = GetBoardHost(session);
        string path = isGroup ? "/api/board/listReminder" : "/api/board/oneone/list";
        string url = MakeUrl(host, path);

        JsonObject objectData = new()
        {
            [isGroup ? "group_id" : "uid"] = threadId,
            ["board_type"] = 1,
            ["page"] = page > 0 ? page : 1,
            ["count"] = count > 0 ? count : 20,
            ["last_id"] = 0,
            ["last_type"] = 0
        };

        JsonObject payload = new()
        {
            ["objectData"] = objectData.ToJsonString()
        };
        if (isGroup)
        {
            payload["imei"] = session.Material.Imei;
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Failed to encrypt GetListReminder payload");
        }

        string requestUrl = $"{url}&params={Uri.EscapeDataString(encryptedParams)}";
        using HttpResponseMessage resp = await http.RequestAsync(requestUrl, HttpMethod.Get, ct: ct).ConfigureAwait(false);
        JsonNode? node = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        JsonNode? dataNode;
        try
        {
            dataNode = DecryptDataNode(session, node) ?? node?["data"];
        }
        catch (ZaloApiException ex) when (ex.Code is -1 or 211)
        {
            return [];
        }

        List<ZaloReminderItem> results = [];
        JsonArray? arr = dataNode as JsonArray ?? (dataNode as JsonObject)?["items"] as JsonArray;
        if (arr is not null)
        {
            foreach (JsonNode? item in arr)
            {
                if (item is not null)
                {
                    results.Add(ParseReminderItem(item, threadId, isGroup));
                }
            }
        }

        return results;
    }
}
