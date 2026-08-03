// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Zalo.Net.Contracts;

/// <summary>
/// Provides the diagnostic event source and event-name registry for <c>Zalo.Net</c>.
/// </summary>
/// <remarks>
/// Library components emit diagnostic events through <see cref="Source"/> instead of depending directly on a logging abstraction.
/// Applications and diagnostic tools can subscribe to <see cref="Source"/> to bridge events to logging, OpenTelemetry, or metrics systems.
/// </remarks>
public static class ZaloDiagnosticsEvents
{
    /// <summary>The diagnostic listener name used by Zalo.Net.</summary>
    public const string ListenerName = "Zalo.Net";

    /// <summary>The shared diagnostic listener used to publish Zalo.Net events.</summary>
    public static readonly DiagnosticListener Source = new(ListenerName);

    /// <summary>Writes a diagnostic event payload through <see cref="Source"/> in an AOT-safe manner.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DiagnosticSource payloads are observational only.")]
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "DiagnosticSource payloads are observational only.")]
    public static void Write<T>(string name, T payload) => Source.Write(name, payload);

    /// <summary>Diagnostic event names for WebSocket real-time operations.</summary>
    public static class WebSocket
    {
        /// <summary>Raised when WebSocket connects successfully.</summary>
        public const string Connected = "WebSocket.Connected";

        /// <summary>Raised when WebSocket disconnects.</summary>
        public const string Disconnected = "WebSocket.Disconnected";

        /// <summary>Raised when a WebSocket message is received.</summary>
        public const string MessageReceived = "WebSocket.MessageReceived";

        /// <summary>Raised when a WebSocket frame body is decoded.</summary>
        public const string FrameDecoded = "WebSocket.FrameDecoded";

        /// <summary>Raised when a WebSocket frame decoding fails.</summary>
        public const string FrameError = "WebSocket.FrameError";
    }

    /// <summary>Diagnostic event names for HTTP API calls.</summary>
    public static class Http
    {
        /// <summary>Raised when an HTTP request is sent.</summary>
        public const string RequestSent = "Http.RequestSent";

        /// <summary>Raised when an HTTP response is received.</summary>
        public const string ResponseReceived = "Http.ResponseReceived";

        /// <summary>Raised when an HTTP request fails.</summary>
        public const string RequestFailed = "Http.RequestFailed";
    }

    /// <summary>Diagnostic event names for authentication lifecycle.</summary>
    public static class Auth
    {
        /// <summary>Raised when a QR code login is generated.</summary>
        public const string QrGenerated = "Auth.QrGenerated";

        /// <summary>Raised when a user scans the QR code.</summary>
        public const string QrScanned = "Auth.QrScanned";

        /// <summary>Raised when an authenticated session is restored.</summary>
        public const string SessionLoaded = "Auth.SessionLoaded";

        /// <summary>Raised when an authenticated session expires.</summary>
        public const string SessionExpired = "Auth.SessionExpired";
    }

    /// <summary>Diagnostic event names for message interaction operations.</summary>
    public static class Message
    {
        /// <summary>Raised when a text message is sent.</summary>
        public const string TextSent = "Message.TextSent";

        /// <summary>Raised when an image message is sent.</summary>
        public const string ImageSent = "Message.ImageSent";

        /// <summary>Raised when a file attachment is sent.</summary>
        public const string FileSent = "Message.FileSent";

        /// <summary>Raised when a quote/reply message is sent.</summary>
        public const string QuoteSent = "Message.QuoteSent";

        /// <summary>Raised when a message recall/undo is sent.</summary>
        public const string UndoSent = "Message.UndoSent";

        /// <summary>Raised when a reaction is added.</summary>
        public const string ReactionSent = "Message.ReactionSent";

        /// <summary>Raised when a sticker is sent.</summary>
        public const string StickerSent = "Message.StickerSent";
    }

    /// <summary>Diagnostic event names for group management operations.</summary>
    public static class Group
    {
        /// <summary>Raised when a group chat is created.</summary>
        public const string Created = "Group.Created";

        /// <summary>Raised when a member is added to a group.</summary>
        public const string MemberAdded = "Group.MemberAdded";

        /// <summary>Raised when a member is removed from a group.</summary>
        public const string MemberRemoved = "Group.MemberRemoved";

        /// <summary>Raised when leaving a group.</summary>
        public const string Left = "Group.Left";
    }

    /// <summary>Diagnostic event names for internal diagnostics and traces.</summary>
    public static class Internal
    {
        /// <summary>Raised for internal trace-level diagnostic messages.</summary>
        public const string Trace = "Internal.Trace";

        /// <summary>Raised for internal debug-level diagnostic messages.</summary>
        public const string Debug = "Internal.Debug";

        /// <summary>Raised for internal informational diagnostic messages.</summary>
        public const string Information = "Internal.Information";

        /// <summary>Raised for internal warning-level diagnostic messages.</summary>
        public const string Warning = "Internal.Warning";

        /// <summary>Raised for internal error-level diagnostic messages.</summary>
        public const string Error = "Internal.Error";

        /// <summary>Raised for internal critical diagnostic messages.</summary>
        public const string Critical = "Internal.Critical";
    }
}
