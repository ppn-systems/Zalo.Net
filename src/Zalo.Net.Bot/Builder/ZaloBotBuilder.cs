// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Zalo.Net.Bot.Context;
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

    /// <summary>Registers a pure Native AOT command handler (e.g. <c>/ping</c>).</summary>
    public ZaloBotBuilder OnCommand(string command, Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        _ = this._dispatcher.OnCommand(command, handler);
        return this;
    }

    /// <summary>Registers a pure Native AOT command handler (e.g. <c>/ping</c>).</summary>
    public ZaloBotBuilder OnCommand(string command, Func<ZaloBotContext, Task> handler)
    {
        _ = this._dispatcher.OnCommand(command, handler);
        return this;
    }

    /// <summary>Registers a pure Native AOT keyword handler.</summary>
    public ZaloBotBuilder OnKeyword(IEnumerable<string> keywords, Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        _ = this._dispatcher.OnKeyword(keywords, handler);
        return this;
    }

    /// <summary>Registers a pure Native AOT keyword handler.</summary>
    public ZaloBotBuilder OnKeyword(IEnumerable<string> keywords, Func<ZaloBotContext, Task> handler)
    {
        _ = this._dispatcher.OnKeyword(keywords, handler);
        return this;
    }

    /// <summary>Registers a pure Native AOT global message handler.</summary>
    public ZaloBotBuilder OnMessage(Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        _ = this._dispatcher.OnMessage(handler);
        return this;
    }

    /// <summary>Registers a pure Native AOT global message handler.</summary>
    public ZaloBotBuilder OnMessage(Func<ZaloBotContext, Task> handler)
    {
        _ = this._dispatcher.OnMessage(handler);
        return this;
    }

    /// <summary>Registers attribute-marked handler methods from a target class instance.</summary>
    public ZaloBotBuilder RegisterHandlers(object handlerInstance)
    {
        this._dispatcher.RegisterHandlers(handlerInstance);
        return this;
    }

    /// <summary>Registers static attribute-marked handler methods from a target class type.</summary>
    public ZaloBotBuilder RegisterHandlers<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] T>()
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
