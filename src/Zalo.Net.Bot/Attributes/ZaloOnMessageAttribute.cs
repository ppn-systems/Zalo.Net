// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Zalo.Net.Bot.Attributes;

/// <summary>
/// Marks a bot handler method to trigger on all incoming Zalo messages.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ZaloOnMessageAttribute : Attribute
{
}
