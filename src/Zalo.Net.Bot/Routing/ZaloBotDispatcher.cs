// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using Zalo.Net.Bot.Attributes;
using Zalo.Net.Bot.Context;

namespace Zalo.Net.Bot.Routing;

/// <summary>
/// High-performance handler registry and message dispatcher for routing Zalo events to handler methods.
/// </summary>
public sealed class ZaloBotDispatcher
{
    private sealed record CommandRegistration(string Command, MethodInfo Method, object? Instance);
    private sealed record KeywordRegistration(IReadOnlyList<string> Keywords, MethodInfo Method, object? Instance);
    private sealed record MessageRegistration(MethodInfo Method, object? Instance);

    private readonly List<CommandRegistration> _commandHandlers = [];
    private readonly List<KeywordRegistration> _keywordHandlers = [];
    private readonly List<MessageRegistration> _globalHandlers = [];

    /// <summary>
    /// Registers all handler methods found on a target handler instance or type.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075")]
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
    /// Registers static handler methods from a target type.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2090")]
    public void RegisterHandlers<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods)] T>()
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

        foreach (ZaloCommandAttribute cmdAttr in method.GetCustomAttributes<ZaloCommandAttribute>())
        {
            this._commandHandlers.Add(new CommandRegistration(cmdAttr.Command, method, instance));
        }

        foreach (ZaloKeywordAttribute kwAttr in method.GetCustomAttributes<ZaloKeywordAttribute>())
        {
            this._keywordHandlers.Add(new KeywordRegistration(kwAttr.Keywords, method, instance));
        }

        if (method.GetCustomAttribute<ZaloOnMessageAttribute>() != null)
        {
            this._globalHandlers.Add(new MessageRegistration(method, instance));
        }
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
                    await InvokeHandlerAsync(reg.Method, reg.Instance, ctx, ct).ConfigureAwait(false);
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
                    await InvokeHandlerAsync(reg.Method, reg.Instance, ctx, ct).ConfigureAwait(false);
                    return;
                }
            }
        }

        // 3. Fallback to Global Message Handlers
        foreach (MessageRegistration reg in this._globalHandlers)
        {
            await InvokeHandlerAsync(reg.Method, reg.Instance, ctx, ct).ConfigureAwait(false);
        }
    }

    private static async Task InvokeHandlerAsync(MethodInfo method, object? instance, ZaloBotContext ctx, CancellationToken ct)
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
            else
            {
                args[i] = null;
            }
        }

        object? result = method.Invoke(instance, args);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }
}
