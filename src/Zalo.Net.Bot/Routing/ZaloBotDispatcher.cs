// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Zalo.Net.Bot.Attributes;
using Zalo.Net.Bot.Context;

namespace Zalo.Net.Bot.Routing;

/// <summary>
/// High-performance handler registry, middleware pipeline, and message dispatcher
/// for routing Zalo events to handler methods or pure Native AOT delegates.
/// </summary>
public sealed class ZaloBotDispatcher
{
    private sealed record KeywordRegistration(IReadOnlyList<string> Keywords, Func<ZaloBotContext, CancellationToken, Task> Handler);
    private sealed record MessageRegistration(Func<ZaloBotContext, CancellationToken, Task> Handler);

    private readonly Dictionary<string, Func<ZaloBotContext, CancellationToken, Task>> _commandHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeywordRegistration> _keywordHandlers = [];
    private readonly List<MessageRegistration> _globalHandlers = [];
    private readonly List<Func<ZaloBotContext, Func<Task>, CancellationToken, Task>> _middlewares = [];

    /// <summary>
    /// Registers a middleware component into the execution pipeline.
    /// Middlewares execute in registration order around downstream handlers.
    /// </summary>
    public ZaloBotDispatcher Use(Func<ZaloBotContext, Func<Task>, CancellationToken, Task> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        this._middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Registers a middleware component into the execution pipeline.
    /// </summary>
    public ZaloBotDispatcher Use(Func<ZaloBotContext, Func<Task>, Task> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        return this.Use((ctx, next, _) => middleware(ctx, next));
    }

    /// <summary>
    /// Registers a pure Native AOT command handler delegate (e.g. <c>/ping</c>).
    /// </summary>
    public ZaloBotDispatcher OnCommand(string command, Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(handler);

        string normalized = command.StartsWith('/') || command.StartsWith('!') ? command : $"/{command}";
        if (this._commandHandlers.TryGetValue(normalized, out Func<ZaloBotContext, CancellationToken, Task>? existing))
        {
            this._commandHandlers[normalized] = async (ctx, ct) =>
            {
                await existing(ctx, ct).ConfigureAwait(false);
                await handler(ctx, ct).ConfigureAwait(false);
            };
        }
        else
        {
            this._commandHandlers[normalized] = handler;
        }

        return this;
    }

    /// <summary>
    /// Registers a pure Native AOT command handler delegate (e.g. <c>/ping</c>).
    /// </summary>
    public ZaloBotDispatcher OnCommand(string command, Func<ZaloBotContext, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.OnCommand(command, (ctx, _) => handler(ctx));
    }

    /// <summary>
    /// Registers a pure Native AOT keyword handler delegate.
    /// </summary>
    public ZaloBotDispatcher OnKeyword(IEnumerable<string> keywords, Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentNullException.ThrowIfNull(handler);

        this._keywordHandlers.Add(new KeywordRegistration([.. keywords], handler));
        return this;
    }

    /// <summary>
    /// Registers a pure Native AOT keyword handler delegate.
    /// </summary>
    public ZaloBotDispatcher OnKeyword(IEnumerable<string> keywords, Func<ZaloBotContext, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.OnKeyword(keywords, (ctx, _) => handler(ctx));
    }

    /// <summary>
    /// Registers a pure Native AOT message handler delegate for all events.
    /// </summary>
    public ZaloBotDispatcher OnMessage(Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        this._globalHandlers.Add(new MessageRegistration(handler));
        return this;
    }

    /// <summary>
    /// Registers a pure Native AOT message handler delegate for all events.
    /// </summary>
    public ZaloBotDispatcher OnMessage(Func<ZaloBotContext, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.OnMessage((ctx, _) => handler(ctx));
    }

    /// <summary>
    /// Registers all attribute-marked handler methods found on a target handler instance.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection fallback for attribute handlers")]
    public void RegisterHandlers(object handlerInstance)
    {
        ArgumentNullException.ThrowIfNull(handlerInstance);
        Type type = handlerInstance.GetType();

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            this.RegisterMethod(method, handlerInstance);
        }
    }

    /// <summary>
    /// Registers static attribute-marked handler methods from a target type.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "Reflection fallback for attribute handlers")]
    public void RegisterHandlers<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] T>()
    {
        Type type = typeof(T);
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            this.RegisterMethod(method, null);
        }
    }

    private void RegisterMethod(MethodInfo method, object? instance)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 0 || parameters[0].ParameterType != typeof(ZaloBotContext))
        {
            return;
        }

        Func<ZaloBotContext, CancellationToken, Task> delegateHandler = CreateDelegateFromMethod(method, instance);

        foreach (ZaloCommandAttribute cmdAttr in method.GetCustomAttributes<ZaloCommandAttribute>())
        {
            _ = this.OnCommand(cmdAttr.Command, delegateHandler);
        }

        foreach (ZaloKeywordAttribute kwAttr in method.GetCustomAttributes<ZaloKeywordAttribute>())
        {
            this._keywordHandlers.Add(new KeywordRegistration(kwAttr.Keywords, delegateHandler));
        }

        if (method.GetCustomAttribute<ZaloOnMessageAttribute>() != null)
        {
            this._globalHandlers.Add(new MessageRegistration(delegateHandler));
        }
    }

    private static Func<ZaloBotContext, CancellationToken, Task> CreateDelegateFromMethod(MethodInfo method, object? instance)
    {
        ParameterInfo[] parameters = method.GetParameters();
        bool hasCt = parameters.Length > 1 && parameters[1].ParameterType == typeof(CancellationToken);
        bool returnsTask = typeof(Task).IsAssignableFrom(method.ReturnType);

        if (returnsTask)
        {
            if (hasCt)
            {
                try
                {
                    Func<ZaloBotContext, CancellationToken, Task> d = (Func<ZaloBotContext, CancellationToken, Task>)Delegate.CreateDelegate(
                        typeof(Func<ZaloBotContext, CancellationToken, Task>), instance, method);
                    return d;
                }
                catch { }

                return (ctx, ct) => (Task)method.Invoke(instance, [ctx, ct])!;
            }
            else
            {
                try
                {
                    Func<ZaloBotContext, Task> d = (Func<ZaloBotContext, Task>)Delegate.CreateDelegate(
                        typeof(Func<ZaloBotContext, Task>), instance, method);
                    return (ctx, ct) => d(ctx);
                }
                catch { }

                return (ctx, ct) => (Task)method.Invoke(instance, [ctx])!;
            }
        }
        else
        {
            if (hasCt)
            {
                try
                {
                    Action<ZaloBotContext, CancellationToken> d = (Action<ZaloBotContext, CancellationToken>)Delegate.CreateDelegate(
                        typeof(Action<ZaloBotContext, CancellationToken>), instance, method);
                    return (ctx, ct) =>
                    {
                        d(ctx, ct);
                        return Task.CompletedTask;
                    };
                }
                catch { }

                return (ctx, ct) =>
                {
                    _ = method.Invoke(instance, [ctx, ct]);
                    return Task.CompletedTask;
                };
            }
            else
            {
                try
                {
                    Action<ZaloBotContext> d = (Action<ZaloBotContext>)Delegate.CreateDelegate(
                        typeof(Action<ZaloBotContext>), instance, method);
                    return (ctx, ct) =>
                    {
                        d(ctx);
                        return Task.CompletedTask;
                    };
                }
                catch { }

                return (ctx, ct) =>
                {
                    _ = method.Invoke(instance, [ctx]);
                    return Task.CompletedTask;
                };
            }
        }
    }

    /// <summary>
    /// Dispatches an incoming context through the middleware pipeline and matching registered handler methods.
    /// </summary>
    public async Task DispatchAsync(ZaloBotContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (this._middlewares.Count == 0)
        {
            await this.ExecuteRoutingAsync(ctx, ct).ConfigureAwait(false);
            return;
        }

        int index = -1;
        async Task NextAsync()
        {
            index++;
            if (index < this._middlewares.Count)
            {
                await this._middlewares[index](ctx, NextAsync, ct).ConfigureAwait(false);
            }
            else if (index == this._middlewares.Count)
            {
                await this.ExecuteRoutingAsync(ctx, ct).ConfigureAwait(false);
            }
        }

        await NextAsync().ConfigureAwait(false);
    }

    private async Task ExecuteRoutingAsync(ZaloBotContext ctx, CancellationToken ct)
    {
        string? raw = ctx.Content;
        if (string.IsNullOrWhiteSpace(raw))
        {
            foreach (MessageRegistration reg in this._globalHandlers)
            {
                await reg.Handler(ctx, ct).ConfigureAwait(false);
            }
            return;
        }

        ReadOnlySpan<char> text = raw.AsSpan().Trim();

        // 1. Check Command match with O(1) AlternateLookup and zero string allocations
        if (ctx.Command != null)
        {
            Dictionary<string, Func<ZaloBotContext, CancellationToken, Task>>.AlternateLookup<ReadOnlySpan<char>> lookup =
                this._commandHandlers.GetAlternateLookup<ReadOnlySpan<char>>();

            if (lookup.TryGetValue(ctx.Command.AsSpan(), out Func<ZaloBotContext, CancellationToken, Task>? handler))
            {
                await handler(ctx, ct).ConfigureAwait(false);
                return;
            }
        }

        // 2. Check Keyword match
        foreach (KeywordRegistration reg in this._keywordHandlers)
        {
            foreach (string kw in reg.Keywords)
            {
                if (text.Contains(kw.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    await reg.Handler(ctx, ct).ConfigureAwait(false);
                    return;
                }
            }
        }

        // 3. Fallback to Global Message Handlers
        foreach (MessageRegistration reg in this._globalHandlers)
        {
            await reg.Handler(ctx, ct).ConfigureAwait(false);
        }
    }
}
