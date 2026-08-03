// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;

namespace Zalo.Net.Contracts;

/// <summary>
/// Thread-safe registry mapping fileId to fileUrl returned asynchronously by Zalo WS file_done events.
/// </summary>
public static class ZaloFileDoneRegistry
{
    private static readonly ConcurrentDictionary<string, string> s_map = new();

    /// <summary>Registers a completed file upload URL for a fileId.</summary>
    public static void Set(string fileId, string fileUrl)
    {
        if (!string.IsNullOrWhiteSpace(fileId) && !string.IsNullOrWhiteSpace(fileUrl))
        {
            s_map[fileId] = fileUrl;
        }
    }

    /// <summary>Tries to retrieve the completed file URL for a fileId.</summary>
    public static bool TryGet(string fileId, out string? fileUrl)
    {
        if (!string.IsNullOrWhiteSpace(fileId))
        {
            return s_map.TryGetValue(fileId, out fileUrl);
        }
        fileUrl = null;
        return false;
    }
}
