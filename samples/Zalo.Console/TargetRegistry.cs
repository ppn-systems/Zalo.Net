// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Zalo.Net.Contracts;

namespace Zalo.Console;

/// <summary>
/// Represents a cached chat target for numerical CLI menu selection.
/// </summary>
internal sealed record QuickTarget(int Index, string Name, string TargetId, ZaloThreadType ThreadType);

/// <summary>
/// Thread-safe in-memory target registry for quick contact selection.
/// </summary>
internal sealed class TargetRegistry
{
    private readonly List<QuickTarget> _targets = [];
    private readonly Lock _lock = new();

    public void AddOrUpdate(string name, string targetId, ZaloThreadType threadType)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return;
        }

        lock (_lock)
        {
            int existingIdx = _targets.FindIndex(t => t.TargetId == targetId);
            if (existingIdx >= 0)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _targets[existingIdx] = _targets[existingIdx] with { Name = name };
                }
                return;
            }

            int newIndex = _targets.Count + 1;
            string displayName = !string.IsNullOrWhiteSpace(name)
                ? name
                : (threadType == ZaloThreadType.Group ? $"Group {targetId}" : $"User {targetId}");

            _targets.Add(new QuickTarget(newIndex, displayName, targetId, threadType));
        }
    }

    public IReadOnlyList<QuickTarget> GetAll()
    {
        lock (_lock)
        {
            return [.. _targets];
        }
    }

    public QuickTarget? FindByIndex(int index)
    {
        lock (_lock)
        {
            return _targets.FirstOrDefault(t => t.Index == index);
        }
    }
}
