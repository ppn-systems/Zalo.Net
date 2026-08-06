// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp;

/// <summary>
/// CLI Command to import existing <c>session.json</c> from Zalo.Console into Zalo.Net.Mcp SQLite database.
/// </summary>
public static class ImportSessionCommand
{
    public static async Task RunAsync(string sessionFilePath)
    {
        Console.WriteLine($"=== Importing Session from {sessionFilePath} ===");

        if (!File.Exists(sessionFilePath))
        {
            Console.WriteLine($"[ERROR] Session file not found: {sessionFilePath}");
            return;
        }

        string json = await File.ReadAllTextAsync(sessionFilePath).ConfigureAwait(false);
        ZaloSessionMaterial? material = JsonSerializer.Deserialize<ZaloSessionMaterial>(json);

        if (material == null)
        {
            Console.WriteLine("[ERROR] Failed to deserialize ZaloSessionMaterial.");
            return;
        }

        ZaloDatabase db = new();
        db.Initialize();
        MessageRepository repo = new(db);

        string uid = string.IsNullOrWhiteSpace(material.Uid) ? "imported_user" : material.Uid;
        await repo.SaveSessionMaterialAsync(uid, material).ConfigureAwait(false);

        Console.WriteLine($"[SUCCESS] Session imported into SQLite database successfully for UID: {uid}!");
    }
}
