using System;
using System.Collections.Generic;
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
        var keys = @params.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var sb = new StringBuilder("zsecure").Append(type);
        foreach (var k in keys)
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
        var guid = Guid.NewGuid().ToString();
        var md5 = Md5Hex(userAgent);
        return $"{guid}-{md5}";
    }

    /// <summary>
    /// Computes streaming MD5 hash over a large stream using 2 MB buffer chunks.
    /// </summary>
    public static async Task<string> ComputeLargeFileMd5Async(Stream stream, CancellationToken ct = default)
    {
        const int chunkSize = 2 * 1024 * 1024;
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buf = new byte[chunkSize];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            md5.AppendData(buf, 0, read);
        }
        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    internal static string Md5Hex(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
