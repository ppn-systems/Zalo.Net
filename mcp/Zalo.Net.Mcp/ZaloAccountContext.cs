// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// DTO representing an authenticated Zalo account summary.
/// </summary>
public sealed record ZaloAccountSummary(
    string Uid,
    string DisplayName,
    string? AvatarUrl,
    bool IsActive,
    bool IsConnected);

/// <summary>
/// Fully isolated runtime context for an individual Zalo account.
/// Contains its own WebClient, dedicated SQLite database, repository, and ingest queue.
/// </summary>
public sealed class ZaloAccountContext : IAsyncDisposable, IDisposable
{
    private bool _disposed;

    /// <summary>Gets the account user ID.</summary>
    public string Uid { get; }

    /// <summary>Gets or sets the user display name.</summary>
    public string DisplayName { get; set; }

    /// <summary>Gets or sets the user avatar URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Gets or sets the active Zalo session.</summary>
    public ZaloSession Session { get; set; }

    /// <summary>Gets the isolated ZaloWebClient instance for this account.</summary>
    public ZaloWebClient Client { get; }

    /// <summary>Gets the account's physically isolated SQLite database.</summary>
    public ZaloDatabase Database { get; }

    /// <summary>Gets the account's isolated message repository.</summary>
    public MessageRepository Repository { get; }

    /// <summary>Gets the ordered ingest pipeline for this account's messages.</summary>
    public MessageIngestPipeline Ingest { get; }

    /// <summary>Gets or sets the background WebSocket listener cancellation token source.</summary>
    public CancellationTokenSource? ListenerCts { get; set; }

    /// <summary>Gets or sets whether the WebSocket connection is active.</summary>
    public bool IsConnected { get; set; }

    public ZaloAccountContext(
        string uid,
        string displayName,
        string? avatarUrl,
        ZaloSession session,
        ZaloWebClient client,
        ZaloDatabase database,
        MessageRepository repository,
        MessageIngestPipeline ingest)
    {
        this.Uid = uid ?? throw new ArgumentNullException(nameof(uid));
        this.DisplayName = displayName ?? uid;
        this.AvatarUrl = avatarUrl;
        this.Session = session ?? throw new ArgumentNullException(nameof(session));
        this.Client = client ?? throw new ArgumentNullException(nameof(client));
        this.Database = database ?? throw new ArgumentNullException(nameof(database));
        this.Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.Ingest = ingest ?? throw new ArgumentNullException(nameof(ingest));
    }

    /// <summary>Stops the background listener and cancels ongoing network tasks.</summary>
    public void StopListener()
    {
        if (this.ListenerCts != null)
        {
            try
            {
                this.ListenerCts.Cancel();
            }
            catch (ObjectDisposedException) { }

            this.ListenerCts.Dispose();
            this.ListenerCts = null;
        }

        this.IsConnected = false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this.StopListener();
        await this.Ingest.DisposeAsync().ConfigureAwait(false);
        this.Client.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this.StopListener();
        this.Ingest.DisposeAsync().AsTask().GetAwaiter().GetResult();
        this.Client.Dispose();
    }
}
