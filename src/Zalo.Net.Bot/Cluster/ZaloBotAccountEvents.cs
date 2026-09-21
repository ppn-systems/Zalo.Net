// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Zalo.Net.Bot.Cluster;

/// <summary>
/// Event arguments for error occurrences within a specific bot account.
/// </summary>
public sealed class ZaloBotAccountErrorEventArgs(string uid, Exception exception) : EventArgs
{
    /// <summary>Gets the account UID where the error occurred.</summary>
    public string Uid { get; } = uid;

    /// <summary>Gets the exception that was thrown.</summary>
    public Exception Exception { get; } = exception;
}

/// <summary>
/// Event arguments for status transitions of a bot account in a cluster.
/// </summary>
public sealed class ZaloBotAccountStatusEventArgs(string uid, string status, string? message = null) : EventArgs
{
    /// <summary>Gets the account UID.</summary>
    public string Uid { get; } = uid;

    /// <summary>Gets the status description (e.g. Starting, Started, Stopped, Reconnecting).</summary>
    public string Status { get; } = status;

    /// <summary>Gets optional contextual details or reason for the status change.</summary>
    public string? Message { get; } = message;
}
