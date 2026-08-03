// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;

namespace Zalo.Net.Contracts;

/// <summary>
/// Abstraction for Zalo Web client API operations, authentication flows, and real-time messaging.
/// Enables dependency injection and mocking for unit tests.
/// </summary>
public interface IZaloClient : IDisposable
{
    /// <summary>Gets the configured proxy instance for this client, if assigned.</summary>
    IWebProxy? Proxy { get; }

    /// <summary>Occurs when an inbound WebSocket message is received.</summary>
    event EventHandler<ZaloMessageEvent>? MessageReceived;

    /// <summary>Occurs when the session connection status changes.</summary>
    event EventHandler<ZaloSessionStatusChanged>? StatusChanged;

    /// <summary>Starts QR login flow.</summary>
    Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct = default);

    /// <summary>Polls for QR login status.</summary>
    Task<ZaloLoginState> PollQrStatusAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Consumes and retrieves session material after login confirmation.</summary>
    ZaloSessionMaterial? ConsumePendingMaterial(Guid sessionId);

    /// <summary>Starts real-time WebSocket listener using an active session.</summary>
    Task StartListenerAsync(ZaloSession session, CancellationToken ct = default);

    /// <summary>Runs WebSocket listener with automatic exponential backoff reconnects.</summary>
    Task RunWithReconnectAsync(ZaloSessionMaterial material, IWebProxy? proxy = null, CancellationToken ct = default);
}
