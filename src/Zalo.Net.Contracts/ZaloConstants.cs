// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

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

        /// <summary>Default Zalo Web API version constant (671).</summary>
        public const int ApiVersion = 671;

        /// <summary>Default User-Agent string mimicking modern Chrome on Windows.</summary>
        public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    }

    /// <summary>Default Zalo Web Gateway endpoint hosts.</summary>
    public static class Hosts
    {
        /// <summary>Default 1-on-1 direct message gateway host.</summary>
        public const string Chat = "https://chat-wpa.chat.zalo.me";

        /// <summary>Default group chat gateway host.</summary>
        public const string Group = "https://group-wpa.chat.zalo.me";

        /// <summary>Default user profile gateway host.</summary>
        public const string Profile = "https://profile-wpa.chat.zalo.me";

        /// <summary>Default message reaction gateway host.</summary>
        public const string Reaction = "https://tt-react-wpa.chat.zalo.me";

        /// <summary>Default authentication gateway host.</summary>
        public const string Login = "https://wpa.chat.zalo.me";

        /// <summary>Default Origin header value.</summary>
        public const string Origin = "https://chat.zalo.me";
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
        /// Default ping interval in milliseconds (20 seconds).
        /// </summary>
        public const int DefaultPingIntervalMs = 20_000;
    }
}
