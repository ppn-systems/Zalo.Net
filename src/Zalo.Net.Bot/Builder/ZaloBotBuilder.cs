// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Zalo.Net.Bot.Engine;
using Zalo.Net.Bot.Routing;
using Zalo.Net.Contracts;

namespace Zalo.Net.Bot.Builder;

/// <summary>
/// Fluent builder pattern for constructing a <see cref="ZaloBotEngine"/> instance.
/// </summary>
public sealed class ZaloBotBuilder
{
    private ZaloSession? _session;
    private IZaloClient? _client;
    private IWebProxy? _proxy;
    private readonly ZaloBotDispatcher _dispatcher = new();

    /// <summary>Creates a new instance of <see cref="ZaloBotBuilder"/>.</summary>
    public static ZaloBotBuilder Create() => new();

    /// <summary>Sets the active Zalo session material.</summary>
    public ZaloBotBuilder UseSession(ZaloSession session)
    {
        this._session = session ?? throw new ArgumentNullException(nameof(session));
        return this;
    }

    /// <summary>Sets a custom <see cref="IZaloClient"/> instance.</summary>
    public ZaloBotBuilder UseClient(IZaloClient client)
    {
        this._client = client ?? throw new ArgumentNullException(nameof(client));
        return this;
    }

    /// <summary>Sets an optional HTTP/SOCKS5 proxy.</summary>
    public ZaloBotBuilder UseProxy(IWebProxy proxy)
    {
        this._proxy = proxy;
        return this;
    }

    /// <summary>Registers handler methods from a target class instance.</summary>
    public ZaloBotBuilder RegisterHandlers(object handlerInstance)
    {
        this._dispatcher.RegisterHandlers(handlerInstance);
        return this;
    }

    /// <summary>Registers static handler methods from a target class type.</summary>
    public ZaloBotBuilder RegisterHandlers<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods)] T>()
    {
        this._dispatcher.RegisterHandlers<T>();
        return this;
    }

    /// <summary>Builds and configures the <see cref="ZaloBotEngine"/>.</summary>
    public ZaloBotEngine Build()
    {
        if (this._session == null)
        {
            throw new InvalidOperationException("ZaloSession must be provided via UseSession().");
        }

        IZaloClient client = this._client ?? new ZaloWebClient(this._proxy);
        return new ZaloBotEngine(this._session, client, this._dispatcher, this._proxy);
    }
}
