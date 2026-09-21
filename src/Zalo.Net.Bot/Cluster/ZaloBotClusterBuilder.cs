// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Logging;
using Zalo.Net.Bot.Builder;
using Zalo.Net.Bot.Context;
using Zalo.Net.Bot.Routing;
using Zalo.Net.Contracts;

namespace Zalo.Net.Bot.Cluster;

/// <summary>
/// Fluent builder for constructing a <see cref="ZaloBotCluster"/> with shared command/keyword routing and middlewares.
/// </summary>
public sealed class ZaloBotClusterBuilder
{
    private sealed record AccountConfig(
        ZaloSession Session,
        IZaloClient? Client,
        IWebProxy? Proxy,
        Action<ZaloBotBuilder>? Configure);

    private readonly List<AccountConfig> _accountConfigs = [];
    private readonly ZaloBotDispatcher _sharedDispatcher = new();
    private ILoggerFactory? _loggerFactory;
    private IServiceProvider? _services;
    private int _maxConcurrencyPerAccount = 50;

    /// <summary>Creates a new instance of <see cref="ZaloBotClusterBuilder"/>.</summary>
    public static ZaloBotClusterBuilder Create() => new();

    /// <summary>Sets a shared logger factory for all cluster accounts.</summary>
    public ZaloBotClusterBuilder UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        this._loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        return this;
    }

    /// <summary>Sets a shared service provider for dependency injection across all cluster accounts.</summary>
    public ZaloBotClusterBuilder UseServices(IServiceProvider services)
    {
        this._services = services ?? throw new ArgumentNullException(nameof(services));
        return this;
    }

    /// <summary>Sets the maximum concurrency limit for each individual account in the cluster.</summary>
    public ZaloBotClusterBuilder WithConcurrencyLimit(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        this._maxConcurrencyPerAccount = maxConcurrency;
        return this;
    }

    /// <summary>Registers a middleware component shared across all accounts in the cluster.</summary>
    public ZaloBotClusterBuilder UseMiddleware(Func<ZaloBotContext, Func<Task>, CancellationToken, Task> middleware)
    {
        _ = this._sharedDispatcher.Use(middleware);
        return this;
    }

    /// <summary>Registers a middleware component shared across all accounts in the cluster.</summary>
    public ZaloBotClusterBuilder UseMiddleware(Func<ZaloBotContext, Func<Task>, Task> middleware)
    {
        _ = this._sharedDispatcher.Use(middleware);
        return this;
    }

    /// <summary>Registers an account to be included in the cluster.</summary>
    public ZaloBotClusterBuilder AddAccount(
        ZaloSession session,
        IWebProxy? proxy = null,
        Action<ZaloBotBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        this._accountConfigs.Add(new AccountConfig(session, null, proxy, configure));
        return this;
    }

    /// <summary>Registers an account with a custom <see cref="IZaloClient"/> instance.</summary>
    public ZaloBotClusterBuilder AddAccount(
        ZaloSession session,
        IZaloClient client,
        IWebProxy? proxy = null,
        Action<ZaloBotBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(client);
        this._accountConfigs.Add(new AccountConfig(session, client, proxy, configure));
        return this;
    }

    /// <summary>Registers multiple accounts to the cluster.</summary>
    public ZaloBotClusterBuilder AddAccounts(IEnumerable<ZaloSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        foreach (ZaloSession session in sessions)
        {
            this._accountConfigs.Add(new AccountConfig(session, null, null, null));
        }
        return this;
    }

    /// <summary>Registers a command handler shared across all cluster accounts.</summary>
    public ZaloBotClusterBuilder OnCommand(string command, Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        _ = this._sharedDispatcher.OnCommand(command, handler);
        return this;
    }

    /// <summary>Registers a command handler shared across all cluster accounts.</summary>
    public ZaloBotClusterBuilder OnCommand(string command, Func<ZaloBotContext, Task> handler)
    {
        _ = this._sharedDispatcher.OnCommand(command, handler);
        return this;
    }

    /// <summary>Registers a keyword handler shared across all cluster accounts.</summary>
    public ZaloBotClusterBuilder OnKeyword(IEnumerable<string> keywords, Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        _ = this._sharedDispatcher.OnKeyword(keywords, handler);
        return this;
    }

    /// <summary>Registers a keyword handler shared across all cluster accounts.</summary>
    public ZaloBotClusterBuilder OnKeyword(IEnumerable<string> keywords, Func<ZaloBotContext, Task> handler)
    {
        _ = this._sharedDispatcher.OnKeyword(keywords, handler);
        return this;
    }

    /// <summary>Registers a global message handler shared across all cluster accounts.</summary>
    public ZaloBotClusterBuilder OnMessage(Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        _ = this._sharedDispatcher.OnMessage(handler);
        return this;
    }

    /// <summary>Registers a global message handler shared across all cluster accounts.</summary>
    public ZaloBotClusterBuilder OnMessage(Func<ZaloBotContext, Task> handler)
    {
        _ = this._sharedDispatcher.OnMessage(handler);
        return this;
    }

    /// <summary>Registers attribute-marked handler methods from a target class instance.</summary>
    public ZaloBotClusterBuilder RegisterHandlers(object handlerInstance)
    {
        this._sharedDispatcher.RegisterHandlers(handlerInstance);
        return this;
    }

    /// <summary>Registers static attribute-marked handler methods from a target class type.</summary>
    public ZaloBotClusterBuilder RegisterHandlers<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] T>()
    {
        this._sharedDispatcher.RegisterHandlers<T>();
        return this;
    }

    /// <summary>Builds the configured <see cref="ZaloBotCluster"/>.</summary>
    public ZaloBotCluster Build()
    {
        ZaloBotCluster cluster = new(() => this._sharedDispatcher)
        {
            LoggerFactory = this._loggerFactory,
            Services = this._services,
            MaxConcurrencyPerAccount = this._maxConcurrencyPerAccount
        };

        foreach (AccountConfig config in this._accountConfigs)
        {
            _ = cluster.AddAccount(config.Session, config.Client, config.Proxy, config.Configure);
        }

        return cluster;
    }
}
