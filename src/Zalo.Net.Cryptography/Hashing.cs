// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Zalo.Net.Cryptography;

/// <summary>
/// Provides hashing functions required by the Zalo Web protocol (getSignKey, generateImei, MD5 helpers).
/// </summary>
public static class Hashing
{
    /// <summary>
    /// Computes Zalo API signKey: MD5("zsecure" + type + sorted-param-values-concatenated).
    /// </summary>
    public static string GetSignKey(string type, IReadOnlyDictionary<string, object?> @params)
    {
        ArgumentNullException.ThrowIfNull(@params);

        string[] keys = [.. @params.Keys.OrderBy(k => k, StringComparer.Ordinal)];
        StringBuilder sb = new StringBuilder("zsecure").Append(type);
        foreach (string k in keys)
        {
            _ = sb.Append(@params[k]);
        }
        return Md5Hex(sb.ToString());
    }

    /// <summary>
    /// Generates a deterministic-format IMEI for Zalo Web API requests: "{guid}-{md5(userAgent)}".
    /// </summary>
    public static string GenerateImei(string userAgent)
    {
        string guid = Guid.NewGuid().ToString();
        string md5 = Md5Hex(userAgent);
        return $"{guid}-{md5}";
    }

    /// <summary>
    /// Computes streaming MD5 hash over a large stream using 2 MB buffer chunks.
    /// </summary>
    public static async Task<string> ComputeLargeFileMd5Async(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        const int chunkSize = 2 * 1024 * 1024;
        using IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        byte[] buf = new byte[chunkSize];
        int read;
        while ((read = await stream.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            md5.AppendData(buf, 0, read);
        }
        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 required by Zalo protocol specification")]
    internal static string Md5Hex(string input)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
