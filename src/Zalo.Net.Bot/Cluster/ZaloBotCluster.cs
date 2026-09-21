// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Zalo.Net.Bot.Builder;
using Zalo.Net.Bot.Engine;
using Zalo.Net.Bot.Routing;
using Zalo.Net.Contracts;

namespace Zalo.Net.Bot.Cluster;

/// <summary>
/// Coordinates multiple independent <see cref="ZaloBotEngine"/> instances for concurrent multi-account bot execution.
/// Ensures complete data and failure isolation between accounts.
/// </summary>
public sealed class ZaloBotCluster : IDisposable
{
    private sealed class AccountEntry(
        string uid,
        ZaloSession session,
        IZaloClient client,
        ZaloBotEngine engine,
        IWebProxy? proxy,
        CancellationTokenSource cts)
    {
        public string Uid { get; } = uid;
        public ZaloSession Session { get; } = session;
        public IZaloClient Client { get; } = client;
        public ZaloBotEngine Engine { get; } = engine;
        public IWebProxy? Proxy { get; } = proxy;
        public CancellationTokenSource Cts { get; } = cts;
        public Task? ExecutionTask { get; set; }
    }

    private readonly ConcurrentDictionary<string, AccountEntry> _accounts = new(StringComparer.Ordinal);
    private readonly Func<ZaloBotDispatcher> _dispatcherFactory;
    private readonly Lock _syncLock = new();
    private bool _disposed;

    /// <summary>Gets or sets the maximum concurrency allowed for each individual bot account.</summary>
    public int MaxConcurrencyPerAccount { get; set; } = 50;

    /// <summary>Gets or sets optional logger factory for producing structured loggers for each account.</summary>
    public Microsoft.Extensions.Logging.ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>Gets or sets optional service provider for dependency injection across accounts.</summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>Occurs when an unhandled exception is encountered in any bot account.</summary>
    public event EventHandler<ZaloBotAccountErrorEventArgs>? OnAccountError;

    /// <summary>Occurs when a bot account changes its operational status.</summary>
    public event EventHandler<ZaloBotAccountStatusEventArgs>? OnAccountStatusChanged;

    /// <summary>Gets the list of all registered account UIDs in this cluster.</summary>
    public IReadOnlyCollection<string> AccountUids => [.. this._accounts.Keys];

    /// <summary>Gets the total count of registered bot accounts.</summary>
    public int AccountCount => this._accounts.Count;

    /// <summary>
    /// Initializes a new instance of <see cref="ZaloBotCluster"/>.
    /// </summary>
    /// <param name="dispatcherFactory">Factory producing shared or cloned dispatchers for registered accounts.</param>
    public ZaloBotCluster(Func<ZaloBotDispatcher>? dispatcherFactory = null) =>
        this._dispatcherFactory = dispatcherFactory ?? (() => new ZaloBotDispatcher());

    /// <summary>
    /// Registers a new Zalo account to this cluster.
    /// </summary>
    /// <param name="session">The authenticated Zalo session for this account.</param>
    /// <param name="client">Optional custom Zalo client. If null, a new <see cref="ZaloWebClient"/> is created.</param>
    /// <param name="proxy">Optional proxy dedicated to this account.</param>
    /// <param name="configureAccount">Optional callback to register account-specific handlers.</param>
    /// <returns>The created <see cref="ZaloBotEngine"/> for this account.</returns>
    public ZaloBotEngine AddAccount(
        ZaloSession session,
        IZaloClient? client = null,
        IWebProxy? proxy = null,
        Action<ZaloBotBuilder>? configureAccount = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ObjectDisposedException.ThrowIf(this._disposed, this);

        string uid = session.Uid;
        IZaloClient botClient = client ?? new ZaloWebClient(proxy ?? session.Proxy);

        ZaloBotBuilder builder = ZaloBotBuilder.Create()
            .UseSession(session)
            .UseClient(botClient);

        if (proxy != null || session.Proxy != null)
        {
            _ = builder.UseProxy(proxy ?? session.Proxy!);
        }

        if (this.LoggerFactory != null)
        {
            _ = builder.UseLogger(this.LoggerFactory.CreateLogger<ZaloBotEngine>());
        }

        if (this.Services != null)
        {
            _ = builder.UseServices(this.Services);
        }

        _ = builder.WithConcurrencyLimit(this.MaxConcurrencyPerAccount);

        // Apply shared dispatcher
        ZaloBotDispatcher dispatcher = this._dispatcherFactory();
        _ = builder.UseDispatcher(dispatcher);

        // Apply account-level customizations if provided
        configureAccount?.Invoke(builder);

        ZaloBotEngine engine = builder.Build();
        CancellationTokenSource cts = new();
        AccountEntry entry = new(uid, session, botClient, engine, proxy ?? session.Proxy, cts);

        // Forward per-account errors to cluster-level isolated event
        engine.OnError += (_, ex) =>
        {
            this.OnAccountError?.Invoke(this, new ZaloBotAccountErrorEventArgs(uid, ex));
        };

        if (!this._accounts.TryAdd(uid, entry))
        {
            cts.Dispose();
            throw new InvalidOperationException($"Account UID '{uid}' is already registered in this cluster.");
        }

        this.OnAccountStatusChanged?.Invoke(this, new ZaloBotAccountStatusEventArgs(uid, "Registered"));
        return engine;
    }

    /// <summary>
    /// Removes and terminates an account from this cluster.
    /// </summary>
    /// <param name="uid">The account UID to remove.</param>
    /// <returns><see langword="true"/> if removed; otherwise <see langword="false"/>.</returns>
    public bool RemoveAccount(string uid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        if (this._accounts.TryRemove(uid, out AccountEntry? entry))
        {
            try
            {
                entry.Cts.Cancel();
            }
            catch (ObjectDisposedException) { }

            entry.Cts.Dispose();
            entry.Client.Dispose();

            this.OnAccountStatusChanged?.Invoke(this, new ZaloBotAccountStatusEventArgs(uid, "Removed"));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Retrieves the <see cref="ZaloBotEngine"/> for a given account UID.
    /// </summary>
    public ZaloBotEngine? GetEngine(string uid) =>
        this._accounts.TryGetValue(uid, out AccountEntry? entry) ? entry.Engine : null;

    /// <summary>
    /// Retrieves the active <see cref="ZaloSession"/> for a given account UID.
    /// </summary>
    public ZaloSession? GetSession(string uid) =>
        this._accounts.TryGetValue(uid, out AccountEntry? entry) ? entry.Session : null;

    /// <summary>
    /// Starts all registered accounts concurrently with complete fault isolation.
    /// </summary>
    public Task StartAllAsync(CancellationToken ct) => this.StartAllAsync(rampUpDelay: null, ct);

    /// <summary>
    /// Starts all registered accounts with controlled ramp-up rate and complete fault isolation.
    /// A failure or disconnect in one account will not terminate other accounts.
    /// </summary>
    /// <param name="rampUpDelay">Optional delay between launching each account to smooth CPU/TLS handshakes and avoid server-side rate limits during mass startup (e.g. 1,000+ accounts). Default is null (launch concurrently).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task StartAllAsync(TimeSpan? rampUpDelay = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);

        List<Task> tasks = [];
        foreach (AccountEntry entry in this._accounts.Values)
        {
            tasks.Add(this.StartAccountInternalAsync(entry, ct));
            if (rampUpDelay.HasValue && rampUpDelay.Value > TimeSpan.Zero)
            {
                await Task.Delay(rampUpDelay.Value, ct).ConfigureAwait(false);
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a specific account's bot engine asynchronously.
    /// </summary>
    public Task StartAccountAsync(string uid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        if (!this._accounts.TryGetValue(uid, out AccountEntry? entry))
        {
            throw new KeyNotFoundException($"Account UID '{uid}' is not registered.");
        }

        return this.StartAccountInternalAsync(entry, ct);
    }

    /// <summary>
    /// Stops a specific account's bot engine.
    /// </summary>
    public void StopAccount(string uid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        if (this._accounts.TryGetValue(uid, out AccountEntry? entry))
        {
            entry.Cts.Cancel();
            this.OnAccountStatusChanged?.Invoke(this, new ZaloBotAccountStatusEventArgs(uid, "Stopped"));
        }
    }

    private Task StartAccountInternalAsync(AccountEntry entry, CancellationToken ct)
    {
        lock (this._syncLock)
        {
            if (entry.ExecutionTask is { IsCompleted: false })
            {
                return entry.ExecutionTask;
            }

            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(entry.Cts.Token, ct);

            entry.ExecutionTask = Task.Run(async () =>
            {
                using (linkedCts)
                {
                    this.OnAccountStatusChanged?.Invoke(this, new ZaloBotAccountStatusEventArgs(entry.Uid, "Started"));
                    try
                    {
                        await entry.Engine.StartAsync(linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        this.OnAccountStatusChanged?.Invoke(this, new ZaloBotAccountStatusEventArgs(entry.Uid, "Stopped", "Operation canceled"));
                    }
                    catch (Exception ex)
                    {
                        this.OnAccountError?.Invoke(this, new ZaloBotAccountErrorEventArgs(entry.Uid, ex));
                        this.OnAccountStatusChanged?.Invoke(this, new ZaloBotAccountStatusEventArgs(entry.Uid, "Faulted", ex.Message));
                    }
                }
            }, ct);

            return entry.ExecutionTask;
        }
    }

    /// <summary>
    /// Stops all running bot accounts.
    /// </summary>
    public void StopAll()
    {
        foreach (AccountEntry entry in this._accounts.Values)
        {
            try
            {
                entry.Cts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this.StopAll();

        foreach (AccountEntry entry in this._accounts.Values)
        {
            entry.Cts.Dispose();
            entry.Client.Dispose();
        }

        this._accounts.Clear();
    }
}
