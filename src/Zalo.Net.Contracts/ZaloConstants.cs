// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace Zalo.Net.Contracts;

/// <summary>
/// Central registry for Zalo protocol constants, default hosts, API versions, and timeouts.
/// </summary>
public static class ZaloConstants
{
    /// <summary>Protocol version constants.</summary>
    public static class Protocol
    {
        /// <summary>Default Zalo Web API type constant (30).</summary>
        public const int ApiType = 30;

        /// <summary>Default Zalo Web API version constant (694).</summary>
        public const int ApiVersion = 694;

        /// <summary>Verified MENTION_ALL_ID ("-1") for @All group mentions.</summary>
        public const string MentionAllUid = "-1";

        /// <summary>Default User-Agent string mimicking modern Chrome on Windows.</summary>
        public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    }

    /// <summary>Default Zalo Web Gateway endpoint hosts (verified zpw_service_map_v3).</summary>
    public static class Hosts
    {
        /// <summary>Default 1-on-1 direct message gateway host.</summary>
        public const string Chat = "https://tt-chat3-wpa.chat.zalo.me";

        /// <summary>Default group chat gateway host.</summary>
        public const string Group = "https://tt-group-wpa.chat.zalo.me";

        /// <summary>Default user profile gateway host.</summary>
        public const string Profile = "https://tt-profile-wpa.chat.zalo.me";

        /// <summary>Default friend management gateway host.</summary>
        public const string Friend = "https://tt-friend-wpa.chat.zalo.me";

        /// <summary>Default conversation state gateway host (pin, unread, hidden, preload).</summary>
        public const string Conversation = "https://tt-convers-wpa.chat.zalo.me";

        /// <summary>Default sticker gateway host.</summary>
        public const string Sticker = "https://tt-sticker-wpa.chat.zalo.me";

        /// <summary>Default message reaction gateway host.</summary>
        public const string Reaction = "https://reaction.chat.zalo.me";

        /// <summary>Default alias gateway host.</summary>
        public const string Alias = "https://tt-alias-wpa.chat.zalo.me";

        /// <summary>Default group board/note/reminder gateway host.</summary>
        public const string GroupBoard = "https://groupboard-wpa.chat.zalo.me";

        /// <summary>Default conversation label gateway host.</summary>
        public const string Label = "https://label-wpa.chat.zalo.me";

        /// <summary>Default quick message template gateway host.</summary>
        public const string QuickMessage = "https://quickmessage.chat.zalo.me";

        /// <summary>Default cloud message history gateway host.</summary>
        public const string CloudMsg = "https://tt-group-cm.chat.zalo.me";

        /// <summary>Default authentication gateway host for web sessions.</summary>
        public const string Login = "https://wpa.chat.zalo.me";

        /// <summary>Default authentication gateway host for QR/password login.</summary>
        public const string AuthLogin = "https://wpa.zaloapp.com";

        /// <summary>Default file attachment gateway host.</summary>
        public const string File = "https://tt-files-wpa.chat.zalo.me";

        /// <summary>Default Origin header value.</summary>
        public const string Origin = "https://chat.zalo.me";

        /// <summary>
        /// Returns an ordered, deduplicated list of candidate gateway hosts for automatic failover.
        /// </summary>
        public static string[] GetCandidates(string serviceKey, string? primaryHost = null)
        {
            ArgumentNullException.ThrowIfNull(serviceKey);
            List<string> list = new(6);

            static void AddUnique(List<string> target, string? host)
            {
                if (string.IsNullOrWhiteSpace(host))
                {
                    return;
                }

                string normalized = host.Trim().TrimEnd('/');
                if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = "https://" + normalized;
                }

                foreach (string existing in target)
                {
                    if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                target.Add(normalized);
            }

            AddUnique(list, primaryHost);

            switch (serviceKey.ToLowerInvariant())
            {
                case "chat":
                    AddUnique(list, Chat);
                    AddUnique(list, "https://chat-wpa.chat.zalo.me");
                    AddUnique(list, "https://tt-chat3-wpa.chat.zalo.me");
                    AddUnique(list, "https://tt-chat1-wpa.chat.zalo.me");
                    break;
                case "group":
                case "group_poll":
                    AddUnique(list, Group);
                    AddUnique(list, "https://group-wpa.chat.zalo.me");
                    AddUnique(list, "https://tt-group-wpa.chat.zalo.me");
                    break;
                case "profile":
                    AddUnique(list, Profile);
                    AddUnique(list, "https://profile-wpa.chat.zalo.me");
                    AddUnique(list, "https://tt-profile-wpa.chat.zalo.me");
                    break;
                case "friend":
                    AddUnique(list, Friend);
                    AddUnique(list, Profile);
                    AddUnique(list, "https://tt-friend-wpa.chat.zalo.me");
                    break;
                case "conversation":
                    AddUnique(list, Conversation);
                    AddUnique(list, "https://tt-conversation-wpa.zaloapp.com");
                    break;
                case "alias":
                    AddUnique(list, Alias);
                    AddUnique(list, "https://alias-wpa.chat.zalo.me");
                    AddUnique(list, Friend);
                    break;
                case "group_board":
                case "boards":
                    AddUnique(list, GroupBoard);
                    AddUnique(list, "https://board-wpa.chat.zalo.me");
                    AddUnique(list, "https://tt-groupboard-wpa.chat.zalo.me");
                    AddUnique(list, Group);
                    break;
                case "label":
                    AddUnique(list, Label);
                    AddUnique(list, "https://tt-label-wpa.chat.zalo.me");
                    AddUnique(list, Conversation);
                    break;
                case "quick_message":
                    AddUnique(list, QuickMessage);
                    AddUnique(list, "https://quickmsg-wpa.chat.zalo.me");
                    AddUnique(list, "https://tt-quickmsg-wpa.chat.zalo.me");
                    break;
                case "sticker":
                    AddUnique(list, Sticker);
                    AddUnique(list, "https://sticker-wpa.chat.zalo.me");
                    break;
                case "reaction":
                    AddUnique(list, Reaction);
                    AddUnique(list, "https://tt-react-wpa.chat.zalo.me");
                    break;
                case "file":
                case "files":
                    AddUnique(list, File);
                    AddUnique(list, "https://file-wpa.chat.zalo.me");
                    AddUnique(list, "https://tt-files-wpa.chat.zalo.me");
                    break;
                case "cloud_msg":
                case "zcloud":
                    AddUnique(list, CloudMsg);
                    AddUnique(list, "https://zcld.chat.zalo.me");
                    break;
                case "login":
                case "auth":
                    AddUnique(list, Login);
                    AddUnique(list, AuthLogin);
                    break;
                default:
                    AddUnique(list, Chat);
                    break;
            }

            return [.. list];
        }
    }

    /// <summary>WebSocket listener configuration constants.</summary>
    public static class WebSocket
    {
        /// <summary>
        /// Close code emitted when session is kicked or revoked (3003).
        /// </summary>
        public const int CloseCodeKicked = 3003;

        /// <summary>
        /// Close code emitted when session is logged in elsewhere (3000).
        /// </summary>
        public const int CloseCodeDuplicate = 3000;

        /// <summary>
        /// Close code emitted when shard worker does not host the session (3002).
        /// </summary>
        public const int CloseCodeNotFoundWorker = 3002;

        /// <summary>
        /// Default ping interval in milliseconds (20 seconds).
        /// </summary>
        public const int DefaultPingIntervalMs = 20_000;
    }
}
