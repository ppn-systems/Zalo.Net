// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Zalo.Net.Bot.Context;
using Zalo.Net.Bot.Routing;
using Zalo.Net.Contracts;
using Zalo.Net.Contracts.Exceptions;

namespace Zalo.Net.Bot.Engine;

/// <summary>
/// Execution engine managing background WebSocket listener and dispatching events to handlers.
/// </summary>
[SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters")]
public sealed class ZaloBotEngine
{
    private readonly ZaloSession _session;
    private readonly IZaloClient _client;
    private readonly ZaloBotDispatcher _dispatcher;
    private readonly IWebProxy? _proxy;

    /// <summary>
    /// Occurs when an unhandled exception occurs inside a bot event handler.
    /// </summary>
    public event EventHandler<Exception>? OnError;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloBotEngine"/> class.
    /// </summary>
    public ZaloBotEngine(ZaloSession session, IZaloClient client, ZaloBotDispatcher dispatcher, IWebProxy? proxy = null)
    {
        this._session = session ?? throw new ArgumentNullException(nameof(session));
        this._client = client ?? throw new ArgumentNullException(nameof(client));
        this._dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this._proxy = proxy;
    }

    /// <summary>
    /// Starts the background Zalo WebSocket listener and runs the Bot handler loop until cancelled.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"=== Zalo.Net.Bot Engine Started for Session UID {this._session.Uid} ===");

        async void handler(object? sender, ZaloMessageEvent msg)
        {
            if (msg.IsSelf || msg.UidFrom == this._session.Uid)
            {
                return;
            }

            ZaloBotContext ctx = new(msg, this._session, this._client);
            try
            {
                await this._dispatcher.DispatchAsync(ctx, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.OnError?.Invoke(this, ex);
            }
        }

        this._client.MessageReceived += handler;
        try
        {
            await this._client.RunWithReconnectAsync(this._session.Material, proxy: this._proxy, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Zalo.Net.Bot Engine stopped gracefully.");
        }
        catch (ZaloApiException ex)
        {
            Console.WriteLine($"[ERROR] Zalo.Net.Bot API Exception: {ex.Message}");
            throw;
        }
        finally
        {
            this._client.MessageReceived -= handler;
        }
    }
}
