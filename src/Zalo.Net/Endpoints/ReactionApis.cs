using System;
using System.Collections.Generic;
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
/// Preset reaction icons and types for Zalo messages.
/// </summary>
public enum ZaloReactionType
{
    /// <summary>Haha 😄</summary>
    Haha = 0,
    /// <summary>Like 👍</summary>
    Like = 3,
    /// <summary>Heart ❤️</summary>
    Heart = 5,
    /// <summary>Wow 😮</summary>
    Wow = 32,
    /// <summary>Cry 😢</summary>
    Cry = 2,
    /// <summary>Angry 😡</summary>
    Angry = 20,
    /// <summary>Kiss 😗</summary>
    Kiss = 8,
    /// <summary>Tears of Joy 😂</summary>
    TearsOfJoy = 7,
    /// <summary>Poop 💩</summary>
    Shit = 66,
    /// <summary>Rose 🌹</summary>
    Rose = 120,
    /// <summary>Broken Heart 💔</summary>
    BrokenHeart = 65,
    /// <summary>Dislike 👎</summary>
    Dislike = 4,
    /// <summary>Love 😍</summary>
    Love = 29,
    /// <summary>Confused 🤔</summary>
    Confused = 51
}

/// <summary>
/// Endpoint helpers for adding message reactions.
/// </summary>
public static class ReactionApis
{
    private static (int rType, string icon) GetReactionDetails(ZaloReactionType reaction) => reaction switch
    {
        ZaloReactionType.Haha => (0, "/:-))"),
        ZaloReactionType.Like => (3, ":-b"),
        ZaloReactionType.Heart => (5, "<3"),
        ZaloReactionType.Wow => (32, ":-O"),
        ZaloReactionType.Cry => (2, ";-("),
        ZaloReactionType.Angry => (20, ":-||"),
        ZaloReactionType.Kiss => (8, ":-*"),
        ZaloReactionType.TearsOfJoy => (7, ":-D"),
        ZaloReactionType.Shit => (66, "💩"),
        ZaloReactionType.Rose => (120, "🌹"),
        ZaloReactionType.BrokenHeart => (65, "💔"),
        ZaloReactionType.Dislike => (4, "👎"),
        ZaloReactionType.Love => (29, "😍"),
        ZaloReactionType.Confused => (51, "🤔"),
        _ => ((int)reaction, "👍")
    };

    /// <summary>Adds a reaction (emoji) to a message.</summary>
    public static async Task AddReactionAsync(
        ZaloHttpClient http, ZaloSession session,
        string threadId, string msgId, string cliMsgId, ZaloThreadType type,
        ZaloReactionType reaction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(msgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliMsgId);

        bool isGroup = type == ZaloThreadType.Group;
        string endpointHost = session.ServiceMap.TryGetValue("reaction", out string[]? rHosts) && rHosts.Length > 0
            ? rHosts[0]
            : "https://tt-react-wpa.chat.zalo.me";

        if (!endpointHost.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            endpointHost = $"https://{endpointHost}";
        }

        string path = isGroup ? "/api/group/reaction" : "/api/message/reaction";
        string url = $"{endpointHost.TrimEnd('/')}{path}?zpw_ver={ZaloHttpClient.ApiVersion}&zpw_type={ZaloHttpClient.ApiType}";

        (int rType, string icon) = GetReactionDetails(reaction);

        _ = long.TryParse(msgId, out long numericMsgId);
        _ = long.TryParse(cliMsgId, out long numericCliMsgId);

        JsonObject msgObj = new()
        {
            ["gMsgID"] = numericMsgId,
            ["cMsgID"] = numericCliMsgId,
            ["msgType"] = 1
        };
        JsonArray msgList = [];
        msgList.Add((JsonNode)msgObj);

        JsonObject innerReact = new()
        {
            ["react_list"] = msgList,
            ["rIcon"] = icon,
            ["rType"] = rType,
            ["source"] = 6
        };

        JsonObject reactItem = new()
        {
            ["react"] = innerReact.ToJsonString(),
            ["clientId"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        JsonArray reactList = [];
        reactList.Add((JsonNode)reactItem);

        JsonObject payload = new()
        {
            ["react_list"] = reactList
        };

        if (isGroup)
        {
            payload["grid"] = threadId;
            payload["imei"] = session.Material.Imei;
        }
        else
        {
            payload["toid"] = threadId;
        }

        string? encryptedParams = ZaloCipher.EncodeAes(session.Material.SecretKey, payload.ToJsonString());
        if (string.IsNullOrEmpty(encryptedParams))
        {
            throw new ZaloApiException("Mã hóa tham số thả cảm xúc thất bại.");
        }

        Dictionary<string, string> formBody = new()
        {
            ["params"] = encryptedParams
        };

        using HttpResponseMessage resp = await http.RequestAsync(url, HttpMethod.Post, new FormUrlEncodedContent(formBody), ct: ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new ZaloApiException($"Thả cảm xúc thất bại với mã HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }

        JsonNode? json = await ZaloHttpClient.ReadJsonAsync(resp, ct).ConfigureAwait(false);
        int errorCode = json?["error_code"]?.GetValue<int>() ?? -1;
        if (errorCode != 0)
        {
            string msg = json?["error_message"]?.GetValue<string>() ?? "Không rõ nguyên nhân";
            throw new ZaloApiException($"Zalo Server báo lỗi thả cảm xúc ({errorCode}): {msg}");
        }
    }
}
