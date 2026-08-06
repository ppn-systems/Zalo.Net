// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Zalo.Net.Bot.Attributes;

/// <summary>
/// Marks a bot handler method to trigger when an incoming message contains one or more specified keywords.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ZaloKeywordAttribute : Attribute
{
    /// <summary>
    /// Gets the list of keywords that trigger this handler.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloKeywordAttribute"/> class.
    /// </summary>
    /// <param name="keywords">One or more keywords to trigger on.</param>
    public ZaloKeywordAttribute(params string[] keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        this.Keywords = keywords;
    }
}
