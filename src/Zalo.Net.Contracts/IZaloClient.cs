// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>Sends a bank account card for quick transfer via Zalo API.</summary>
    Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloBankCard bankCard, CancellationToken ct = default);

    /// <summary>Sends a bank account card for quick transfer via Zalo API.</summary>
    Task SendBankCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string binBank, string accountNumber, string accountName, CancellationToken ct = default);

    /// <summary>Sends a user contact card recommendation via Zalo API.</summary>
    Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, ZaloContactCard contactCard, CancellationToken ct = default);

    /// <summary>Sends a user contact card recommendation via Zalo API.</summary>
    Task SendContactCardAsync(ZaloSession session, string threadId, ZaloThreadType threadType, string userId, string? phoneNumber = null, string? qrCodeUrl = null, CancellationToken ct = default);

    /// <summary>Joins a group via invite link (e.g. https://zalo.me/g/XXXXXXXXX).</summary>
    Task JoinGroupViaLinkAsync(ZaloSession session, string inviteUrl, CancellationToken ct = default);

    /// <summary>Reviews pending group join requests (approves or rejects).</summary>
    Task ReviewJoinRequestsAsync(ZaloSession session, string groupId, string[] memberUids, bool approve, CancellationToken ct = default);

    /// <summary>Leaves a group silently without broadcasting a leave message.</summary>
    Task LeaveGroupSilentlyAsync(ZaloSession session, string groupId, CancellationToken ct = default);

    /// <summary>Removes/Kicks a member from a group chat.</summary>
    Task KickGroupMemberAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default);

    /// <summary>Promotes a group member to co-admin / admin.</summary>
    Task PromoteGroupAdminAsync(ZaloSession session, string groupId, string memberUid, CancellationToken ct = default);

    /// <summary>Pins an important announcement message in a group chat.</summary>
    Task PinGroupMessageAsync(ZaloSession session, string groupId, string msgId, CancellationToken ct = default);

    /// <summary>Sends an image payload to a user or group thread.</summary>
    Task SendImageAsync(ZaloSession session, string threadId, ZaloThreadType threadType, byte[] imageBytes, string fileName, string? caption = null, CancellationToken ct = default);
}
