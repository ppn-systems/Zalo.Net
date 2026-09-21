// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// State manager owning multiple isolated Zalo account contexts, their dedicated SQLite databases,
/// WebSocket listeners, and the active session dispatching.
/// </summary>
public sealed class ZaloSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ZaloAccountContext> _accounts = new(StringComparer.Ordinal);
    private readonly MessageRepository _masterRepository;
    private readonly MessageIngestPipeline _masterIngest;
    private readonly ZaloWebClient _loginClient;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<ZaloSessionManager>? _logger;
    private readonly TaskCompletionSource _bootstrapCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string? _activeAccountUid;
    private int _bootstrapStarted;
    private bool _disposed;

    public ZaloSessionManager(
        MessageRepository repository,
        ILoggerFactory? loggerFactory = null,
        ILogger<ZaloSessionManager>? logger = null)
    {
        this._masterRepository = repository ?? throw new ArgumentNullException(nameof(repository));
        this._loggerFactory = loggerFactory;
        this._logger = logger;
        this._loginClient = new ZaloWebClient();

        // Fallback ingest queue for backward compatibility
        this._masterIngest = new MessageIngestPipeline(repository, logger);
    }

    /// <summary>Gets the UID of the currently active account context.</summary>
    public string? ActiveAccountUid => this._activeAccountUid;

    /// <summary>Gets the active account's session, or null if unauthenticated.</summary>
    public ZaloSession? ActiveSession => this.GetAccount()?.Session;

    /// <summary>Gets whether at least one account is currently authenticated.</summary>
    public bool IsAuthenticated => this.ActiveSession != null;

    /// <summary>Gets the message repository for the active account (or fallback master repository).</summary>
    public MessageRepository Repository => this.GetAccount()?.Repository ?? this._masterRepository;

    /// <summary>Gets the ingest pipeline for the active account (or fallback master ingest).</summary>
    public MessageIngestPipeline Ingest => this.GetAccount()?.Ingest ?? this._masterIngest;

    /// <summary>Gets all registered account contexts.</summary>
    public IReadOnlyCollection<ZaloAccountContext> Accounts => [.. this._accounts.Values];

    /// <summary>How long a tool call waits for background bootstrap before reporting timeout.</summary>
    public TimeSpan BootstrapWait { get; init; } = BootstrapWaitFromEnvironment();

    private static TimeSpan BootstrapWaitFromEnvironment()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("ZALO_MCP_BOOTSTRAP_WAIT_MS"), out int waitMs) && waitMs >= 0
            ? TimeSpan.FromMilliseconds(waitMs)
            : TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Starts background bootstrap to restore all saved sessions without blocking MCP transport.
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
                this._logger?.LogError(ex, "Background Zalo session bootstrap encountered an error.");
            }
            finally
            {
                _ = this._bootstrapCompletion.TrySetResult();
            }
        });
    }

    /// <summary>
    /// Restores all saved active accounts from database with full physical data isolation.
    /// </summary>
    public async Task InitializeFromDatabaseAsync(CancellationToken ct = default)
    {
        try
        {
            IReadOnlyList<(string Uid, ZaloSessionMaterial Material)> sessions =
                await this._masterRepository.GetAllActiveSessionsAsync(ct).ConfigureAwait(false);

            if (sessions.Count == 0)
            {
                ZaloSessionMaterial? single = await this._masterRepository.GetActiveSessionMaterialAsync(ct).ConfigureAwait(false);
                if (single != null)
                {
                    sessions = [(single.Uid, single)];
                }
            }

            foreach ((string uid, ZaloSessionMaterial material) in sessions)
            {
                try
                {
                    this._logger?.LogInformation("Attempting auto-login for account UID {Uid}", uid);
                    _ = await this.RegisterOrUpdateAccountAsync(material, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this._logger?.LogWarning(ex, "Saved Zalo session for UID {Uid} is expired or could not connect: {Message}", uid, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            this._logger?.LogError(ex, "Failed to read sessions from database.");
        }
        finally
        {
            _ = this._bootstrapCompletion.TrySetResult();
        }
    }

    /// <summary>
    /// Registers or updates an account with complete data isolation (own SQLite file, repository, and ingest queue).
    /// </summary>
    public async Task<ZaloAccountContext> RegisterOrUpdateAccountAsync(ZaloSessionMaterial material, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(material);

        string uid = material.Uid;
        ZaloSession session = await ZaloWebClient.LoginWithSessionAsync(material, ct).ConfigureAwait(false);
        await this._masterRepository.SaveSessionMaterialAsync(uid, material, ct).ConfigureAwait(false);

        // If account already exists, stop its existing listener
        if (this._accounts.TryGetValue(uid, out ZaloAccountContext? existing))
        {
            existing.StopListener();
        }

        // Establish 100% physically isolated SQLite database for this account
        ZaloDatabase accountDb = ZaloDatabase.ForAccount(uid);
        accountDb.Initialize();

        MessageRepository accountRepo = new(accountDb);
        MessageIngestPipeline accountIngest = new(accountRepo, this._loggerFactory?.CreateLogger<MessageIngestPipeline>());
        ZaloWebClient accountClient = new(session.Proxy);

        (string displayName, string? avatarUrl) = await FetchProfileSafeAsync(accountClient, session, uid, ct).ConfigureAwait(false);

        ZaloAccountContext accountContext = new(
            uid: uid,
            displayName: displayName,
            avatarUrl: avatarUrl,
            session: session,
            client: accountClient,
            database: accountDb,
            repository: accountRepo,
            ingest: accountIngest);

        // Wire realtime message ingest specifically into this account's isolated queue
        accountClient.MessageReceived += (_, e) =>
        {
            this._logger?.LogInformation("Realtime Zalo message received for account {Uid} from {Sender}: {Content}", uid, e.DisplayName ?? e.UidFrom, e.Content);
            _ = accountIngest.Enqueue(e);
        };

        accountClient.StatusChanged += (_, e) =>
        {
            this._logger?.LogInformation("Zalo status changed for account {Uid}: {Status} ({Reason})", uid, e.Status, e.Reason);
            accountContext.IsConnected = e.Status == ZaloConnectionStatus.Connected;
        };

        // Start isolated WebSocket listener
        this.StartAccountListener(accountContext, material, session);

        this._accounts[uid] = accountContext;
        this._activeAccountUid ??= uid;

        return accountContext;
    }

    private void StartAccountListener(ZaloAccountContext context, ZaloSessionMaterial material, ZaloSession session)
    {
        context.StopListener();
        context.ListenerCts = new CancellationTokenSource();
        CancellationToken token = context.ListenerCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                context.IsConnected = true;
                await context.Client.RunWithReconnectAsync(material, proxy: session.Proxy, ct: token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                this._logger?.LogWarning(ex, "Background listener stopped for account {Uid}", context.Uid);
                context.IsConnected = false;
            }
        }, token);
    }

    private static async Task<(string DisplayName, string? AvatarUrl)> FetchProfileSafeAsync(
        ZaloWebClient client, ZaloSession session, string uid, CancellationToken ct)
    {
        try
        {
            ZaloUserProfile profile = await ZaloWebClient.GetUserInfoAsync(session, uid, ct).ConfigureAwait(false);
            return (profile.DisplayName, profile.AvatarUrl);
        }
        catch
        {
            return (uid, null);
        }
    }

    /// <summary>Starts QR login flow using the login client.</summary>
    public async Task<ZaloQrSession> StartQrLoginAsync(CancellationToken ct = default) =>
        await this._loginClient.StartQrLoginAsync(ct).ConfigureAwait(false);

    /// <summary>Registers an initialized account context directly into the manager.</summary>
    public void AddAccountContext(ZaloAccountContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this._accounts[context.Uid] = context;
        this._activeAccountUid ??= context.Uid;
    }

    /// <summary>
    /// Polls QR status and registers the account upon successful login.
    /// </summary>
    public async Task<ZaloLoginState> PollQrStatusAsync(Guid sessionId, CancellationToken ct = default)
    {
        ZaloLoginState state = await this._loginClient.PollQrStatusAsync(sessionId, ct).ConfigureAwait(false);
        if (state.Status == ZaloLoginStatus.Connected)
        {
            ZaloSessionMaterial? material = this._loginClient.ConsumePendingMaterial(sessionId);
            if (material != null)
            {
                ZaloAccountContext context = await this.RegisterOrUpdateAccountAsync(material, ct).ConfigureAwait(false);
                this._activeAccountUid = context.Uid;
            }
        }

        return state;
    }

    /// <summary>Retrieves metadata summary of all loaded accounts.</summary>
    public IReadOnlyList<ZaloAccountSummary> ListAccounts()
    {
        return [.. this._accounts.Values.Select(a => new ZaloAccountSummary(
            Uid: a.Uid,
            DisplayName: a.DisplayName,
            AvatarUrl: a.AvatarUrl,
            IsActive: a.Uid == this._activeAccountUid,
            IsConnected: a.IsConnected))];
    }

    /// <summary>
    /// Switches the active account context to the specified UID.
    /// </summary>
    public bool SwitchActiveAccount(string accountUid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);

        if (this._accounts.ContainsKey(accountUid))
        {
            this._activeAccountUid = accountUid;
            _ = Task.Run(async () =>
            {
                try
                {
                    await this._masterRepository.SetActiveSessionAsync(accountUid).ConfigureAwait(false);
                }
                catch { }
            });
            return true;
        }

        return false;
    }

    /// <summary>
    /// Disconnects and unloads an account context.
    /// </summary>
    public async Task<bool> DisconnectAccountAsync(string accountUid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);

        if (this._accounts.TryRemove(accountUid, out ZaloAccountContext? context))
        {
            await context.DisposeAsync().ConfigureAwait(false);
            await this._masterRepository.DeactivateSessionAsync(accountUid, ct).ConfigureAwait(false);

            if (this._activeAccountUid == accountUid)
            {
                this._activeAccountUid = this._accounts.Keys.FirstOrDefault();
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Retrieves the isolated account context for a specific account UID, or the active account if unspecified.
    /// </summary>
    public ZaloAccountContext? GetAccount(string? accountUid = null)
    {
        // Strict Isolation: If a specific account UID is requested, never fall back to another account
        if (!string.IsNullOrWhiteSpace(accountUid))
        {
            return this._accounts.TryGetValue(accountUid, out ZaloAccountContext? specific) ? specific : null;
        }

        if (this._activeAccountUid != null && this._accounts.TryGetValue(this._activeAccountUid, out ZaloAccountContext? active))
        {
            return active;
        }

        return this._accounts.Values.FirstOrDefault();
    }

    /// <summary>
    /// Ensures that the specified account (or the active account) is authenticated and ready for tool execution.
    /// </summary>
    public void EnsureAuthenticated(string? accountUid = null)
    {
        if (Volatile.Read(ref this._bootstrapStarted) == 1
            && !this._bootstrapCompletion.Task.Wait(this.BootstrapWait))
        {
            throw new InvalidOperationException("Zalo sessions are still connecting in background. Retry in a moment, or authenticate with zalo_login_qr.");
        }

        ZaloAccountContext? account = this.GetAccount(accountUid);
        if (account == null || account.Session == null)
        {
            if (!string.IsNullOrWhiteSpace(accountUid))
            {
                throw new InvalidOperationException($"Zalo account '{accountUid}' is not authenticated or not registered.");
            }

            throw new InvalidOperationException("Zalo session is not authenticated or has expired. Please perform QR code login first using zalo_login_qr or --login.");
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
        _ = this._bootstrapCompletion.TrySetResult();

        foreach (ZaloAccountContext account in this._accounts.Values)
        {
            account.Dispose();
        }

        this._accounts.Clear();
        this._masterIngest.DisposeAsync().AsTask().GetAwaiter().GetResult();
        this._loginClient.Dispose();
    }
}
