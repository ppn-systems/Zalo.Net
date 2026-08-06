// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Zalo.Net.Bot.Attributes;
using Zalo.Net.Bot.Context;

namespace Zalo.Net.Bot.Routing;

/// <summary>
/// High-performance handler registry and message dispatcher for routing Zalo events to handler methods or pure AOT delegates.
/// </summary>
public sealed class ZaloBotDispatcher
{
    private sealed record CommandRegistration(string Command, Func<ZaloBotContext, CancellationToken, Task> Handler);
    private sealed record KeywordRegistration(IReadOnlyList<string> Keywords, Func<ZaloBotContext, CancellationToken, Task> Handler);
    private sealed record MessageRegistration(Func<ZaloBotContext, CancellationToken, Task> Handler);

    private readonly List<CommandRegistration> _commandHandlers = [];
    private readonly List<KeywordRegistration> _keywordHandlers = [];
    private readonly List<MessageRegistration> _globalHandlers = [];

    /// <summary>
    /// Registers a pure Native AOT command handler delegate (e.g. <c>/ping</c>).
    /// </summary>
    public ZaloBotDispatcher OnCommand(string command, Func<ZaloBotContext, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(handler);

        string normalized = command.StartsWith('/') || command.StartsWith('!') ? command : $"/{command}";
        this._commandHandlers.Add(new CommandRegistration(normalized, handler));
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
            this._commandHandlers.Add(new CommandRegistration(cmdAttr.Command, delegateHandler));
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
        return async (ctx, ct) =>
        {
            ParameterInfo[] parameters = method.GetParameters();
            object?[] args = new object?[parameters.Length];
            args[0] = ctx;

            for (int i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(CancellationToken))
                {
                    args[i] = ct;
                }
            }

            object? result = method.Invoke(instance, args);
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
            }
        };
    }

    /// <summary>
    /// Dispatches an incoming context to matching registered handler methods.
    /// </summary>
    public async Task DispatchAsync(ZaloBotContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string text = ctx.Content?.Trim() ?? string.Empty;

        // 1. Check Command match
        if (text.StartsWith('/') || text.StartsWith('!'))
        {
            string cmdName = text.Split(' ', 2)[0].ToLowerInvariant();
            foreach (CommandRegistration reg in this._commandHandlers)
            {
                if (reg.Command.Equals(cmdName, StringComparison.OrdinalIgnoreCase))
                {
                    await reg.Handler(ctx, ct).ConfigureAwait(false);
                    return;
                }
            }
        }

        // 2. Check Keyword match
        foreach (KeywordRegistration reg in this._keywordHandlers)
        {
            foreach (string kw in reg.Keywords)
            {
                if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
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
