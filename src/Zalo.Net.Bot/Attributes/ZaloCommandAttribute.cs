// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Zalo.Net.Bot.Attributes;

/// <summary>
/// Marks a bot handler method to trigger when an incoming message starts with a specific command (e.g., <c>/help</c>, <c>!ping</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ZaloCommandAttribute : Attribute
{
    /// <summary>
    /// Gets the command trigger name (e.g., <c>help</c>, <c>/ping</c>).
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Gets or sets an optional description for the command.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloCommandAttribute"/> class.
    /// </summary>
    /// <param name="command">The command string to trigger on.</param>
    public ZaloCommandAttribute(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        this.Command = command.StartsWith('/') || command.StartsWith('!') ? command : $"/{command}";
    }
}
