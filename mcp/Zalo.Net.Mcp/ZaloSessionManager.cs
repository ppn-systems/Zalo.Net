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
    private readonly MessageIngestPipeline _ingest;
    private readonly TaskCompletionSource _bootstrapCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ZaloSession? _activeSession;
    private CancellationTokenSource? _listenerCts;
    private int _bootstrapStarted;

    public ZaloSessionManager(MessageRepository repository, ILogger<ZaloSessionManager>? logger = null)
    {
        this._repository = repository;
        this._logger = logger;
        this._client = new ZaloWebClient();

        // Realtime messages go through the queue instead of being saved inline by the event handler.
        this._ingest = new MessageIngestPipeline(repository, logger);

        this._client.MessageReceived += this.OnMessageReceived;
        this._client.StatusChanged += this.OnStatusChanged;
    }

    public ZaloSession? ActiveSession => this._activeSession;
    public bool IsAuthenticated => this._activeSession != null;
    public MessageRepository Repository => this._repository;

    /// <summary>Ordered write pipeline for the messages pushed by the WebSocket listener.</summary>
    public MessageIngestPipeline Ingest => this._ingest;

    /// <summary>
    /// How long a tool call waits for the background bootstrap (see
    /// <see cref="StartBootstrapInBackground"/>) before it reports that Zalo is not connected yet.
    /// Ten seconds by default; override with the <c>ZALO_MCP_BOOTSTRAP_WAIT_MS</c> environment variable.
    /// </summary>
    public TimeSpan BootstrapWait { get; init; } = BootstrapWaitFromEnvironment();

    private static TimeSpan BootstrapWaitFromEnvironment()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("ZALO_MCP_BOOTSTRAP_WAIT_MS"), out int waitMs) && waitMs >= 0
            ? TimeSpan.FromMilliseconds(waitMs)
            : TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Loads the saved Zalo session and starts the listener without holding up the MCP server: the
    /// login is network bound, so waiting for it here would keep the stdio transport from answering
    /// <c>initialize</c> (clients time out and report "server did not respond"). Tool calls that need
    /// a session wait for this work in <see cref="EnsureAuthenticated"/> instead.
    /// </summary>
    public void StartBootstrapInBackground()
    {
        if (Interlocked.Exchange(ref this._bootstrapStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await this.InitializeFromDatabaseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this._logger?.LogError(ex, "Background Zalo session bootstrap failed.");
            }
            finally
            {
                _ = this._bootstrapCompletion.TrySetResult();
            }
        });
    }

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
        finally
        {
            // Release the tool calls waiting on the bootstrap, whether or not a session was restored.
            _ = this._bootstrapCompletion.TrySetResult();
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

    private void OnMessageReceived(object? sender, ZaloMessageEvent e)
    {
        this._logger?.LogInformation("Realtime Zalo message received from {Sender}: {Content}", e.DisplayName ?? e.UidFrom, e.Content);

        // Nothing is awaited here on purpose: the handler returns immediately and the queue writes the
        // messages one by one, in the order they arrived.
        _ = this._ingest.Enqueue(e);
    }

    private void OnStatusChanged(object? sender, ZaloSessionStatusChanged e)
    {
        this._logger?.LogInformation("Zalo session status changed for {Uid}: {Status} ({Reason})", e.Uid, e.Status, e.Reason);
    }

    public void EnsureAuthenticated()
    {
        if (this._activeSession != null)
        {
            return;
        }

        // The MCP transport answers `initialize` before the Zalo login finishes, so a tool call can
        // legitimately arrive while the bootstrap is still running. Wait for that work (bounded) rather
        // than answering with a misleading "not authenticated".
        if (Volatile.Read(ref this._bootstrapStarted) == 1
            && !this._bootstrapCompletion.Task.Wait(this.BootstrapWait))
        {
            throw new InvalidOperationException("Zalo session is still connecting (the background login has not finished yet). Retry in a moment, or authenticate with zalo_login_qr.");
        }

        if (this._activeSession == null)
        {
            throw new InvalidOperationException("Zalo session is not authenticated or has expired. Please perform QR code login first using zalo_login_qr or --login.");
        }
    }

    public void Dispose()
    {
        this._listenerCts?.Cancel();
        this._listenerCts?.Dispose();

        // Let the tool calls that are waiting in EnsureAuthenticated() finish instead of hanging.
        _ = this._bootstrapCompletion.TrySetResult();

        // Flush the messages that are already queued before the client goes away.
        this._ingest.DisposeAsync().AsTask().GetAwaiter().GetResult();

        this._client.Dispose();
    }
}
