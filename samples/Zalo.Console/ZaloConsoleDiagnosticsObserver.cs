// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Zalo.Net.Contracts;

namespace Zalo.Console;

/// <summary>
/// Subscribes to ZaloDiagnosticsEvents DiagnosticSource stream and formats events for console debugging.
/// </summary>
internal sealed class ZaloConsoleDiagnosticsObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private static IDisposable? s_allListenersSubscription;
    private static IDisposable? s_zaloSourceSubscription;

    public static void EnableConsoleDiagnostics()
    {
        ZaloConsoleDiagnosticsObserver observer = new();
        s_zaloSourceSubscription = ZaloDiagnosticsEvents.Source.Subscribe(observer);
        s_allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(observer);
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == ZaloDiagnosticsEvents.ListenerName)
        {
            _ = value.Subscribe(this);
        }
    }

    public void OnNext(KeyValuePair<string, object?> pair)
    {
        ConsoleColor color = pair.Key.StartsWith("Http", StringComparison.Ordinal)
            ? ConsoleColor.DarkMagenta
            : (pair.Key.StartsWith("WebSocket", StringComparison.Ordinal)
                ? ConsoleColor.DarkYellow
                : ConsoleColor.DarkCyan);

        System.Console.ForegroundColor = color;
        System.Console.WriteLine($"[DIAGNOSTIC EVENT] {pair.Key} | Payload: {pair.Value}");
        System.Console.ResetColor();
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }

    public static void DisableConsoleDiagnostics()
    {
        s_zaloSourceSubscription?.Dispose();
        s_allListenersSubscription?.Dispose();
    }
}
