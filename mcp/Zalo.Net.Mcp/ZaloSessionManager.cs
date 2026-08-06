// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// State manager owning the ZaloWebClient, active session, WebSocket listener, and SQLite persistence.
/// </summary>
public sealed class ZaloSessionManager : IDisposable
{
    private readonly ZaloWebClient _client;
    private readonly MessageRepository _repository;
    private readonly ILogger<ZaloSessionManager>? _logger;
    private ZaloSession? _activeSession;
    private CancellationTokenSource? _listenerCts;

    public ZaloSessionManager(MessageRepository repository, ILogger<ZaloSessionManager>? logger = null)
    {
        this._repository = repository;
        this._logger = logger;
        this._client = new ZaloWebClient();

        this._client.MessageReceived += this.OnMessageReceived;
        this._client.StatusChanged += this.OnStatusChanged;
    }

    public ZaloSession? ActiveSession => this._activeSession;
    public bool IsAuthenticated => this._activeSession != null;
    public MessageRepository Repository => this._repository;

    public async Task InitializeFromDatabaseAsync(CancellationToken ct = default)
    {
        try
        {
            ZaloSessionMaterial? material = await this._repository.GetActiveSessionMaterialAsync(ct).ConfigureAwait(false);
            if (material != null)
            {
                this._logger?.LogInformation("Attempting auto-login using saved session for UID {Uid}", material.Uid);
                this._activeSession = await ZaloWebClient.LoginWithSessionAsync(material, ct).ConfigureAwait(false);
                this.StartBackgroundListener(material);
            }
        }
        catch (Exception ex)
        {
            this._logger?.LogWarning("Saved Zalo session is expired or invalid ({Message}). Resetting active session.", ex.Message);
            await this._repository.DeactivateSessionAsync(ct: ct).ConfigureAwait(false);
            this._activeSession = null;
        }
    }

    public async Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct = default)
    {
        return await this._client.StartQrLoginAsync(ct).ConfigureAwait(false);
    }

    public async Task<ZaloLoginState> PollQrStatusAsync(Guid sessionId, CancellationToken ct = default)
    {
        ZaloLoginState state = await this._client.PollQrStatusAsync(sessionId, ct).ConfigureAwait(false);
        if (state.Status == ZaloLoginStatus.Connected)
        {
            ZaloSessionMaterial? material = this._client.ConsumePendingMaterial(sessionId);
            if (material != null)
            {
                this._activeSession = await ZaloWebClient.LoginWithSessionAsync(material, ct).ConfigureAwait(false);
                await this._repository.SaveSessionMaterialAsync(material.Uid, material, ct).ConfigureAwait(false);
                this.StartBackgroundListener(material);
            }
        }
        return state;
    }

    private void StartBackgroundListener(ZaloSessionMaterial material)
    {
        this._listenerCts?.Cancel();
        this._listenerCts?.Dispose();
        this._listenerCts = new CancellationTokenSource();

        CancellationToken token = this._listenerCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await this._client.RunWithReconnectAsync(material, ct: token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                this._logger?.LogWarning("Zalo background listener stopped ({Message}). Session expired — please authenticate via zalo_login_qr or --login.", ex.Message);
                await this._repository.DeactivateSessionAsync(material.Uid, token).ConfigureAwait(false);
                this._activeSession = null;
            }
        }, token);
    }

    private async void OnMessageReceived(object? sender, ZaloMessageEvent e)
    {
        this._logger?.LogInformation("Realtime Zalo message received from {Sender}: {Content}", e.DisplayName ?? e.UidFrom, e.Content);
        try
        {
            await this._repository.SaveMessageAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this._logger?.LogError(ex, "Failed to save realtime Zalo message to SQLite database.");
        }
    }

    private void OnStatusChanged(object? sender, ZaloSessionStatusChanged e)
    {
        this._logger?.LogInformation("Zalo session status changed for {Uid}: {Status} ({Reason})", e.Uid, e.Status, e.Reason);
    }

    public void EnsureAuthenticated()
    {
        if (this._activeSession == null)
        {
            throw new InvalidOperationException("Zalo session is not authenticated or has expired. Please perform QR code login first using zalo_login_qr or --login.");
        }
    }

    public void Dispose()
    {
        this._listenerCts?.Cancel();
        this._listenerCts?.Dispose();
        this._client.Dispose();
    }
}
