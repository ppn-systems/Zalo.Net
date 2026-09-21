// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Zalo.Net.Cryptography;

internal sealed class WsEnvelope
{
    public string? Data { get; set; }
    public int Encrypt { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WsEnvelope))]
internal partial class CryptoJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Decodes binary WebSocket frames sent by Zalo servers (header parsing, AES-GCM decryption via BouncyCastle, Deflate/GZip inflation, JSON parsing).
/// </summary>
public static class WsFrameCodec
{
    /// <summary>
    /// Parses the 4-byte Zalo WS frame header (Version, Cmd, SubCmd).
    /// </summary>
    public static (byte Version, int Cmd, byte SubCmd) ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
        {
            throw new InvalidOperationException("Frame header must be at least 4 bytes");
        }
        byte version = header[0];
        ushort cmd = BinaryPrimitives.ReadUInt16LittleEndian(header[1..3]);
        byte subCmd = header[3];
        return (version, cmd, subCmd);
    }

    /// <summary>
    /// Decodes frame body according to envelope encrypt type (0 = raw json, 1 = base64+zlib, 2 = AES-GCM+zlib, 3 = AES-GCM+utf8).
    /// </summary>
    public static Task<JsonNode?> DecodeFrameBodyAsync(
        ReadOnlyMemory<byte> body, string? cipherKey, CancellationToken ct = default)
    {
        byte[]? keyBytes = cipherKey is not null ? Convert.FromBase64String(cipherKey) : null;
        return DecodeFrameBodyAsync(body, keyBytes, ct);
    }

    /// <summary>
    /// Decodes frame body with pre-decoded cipher key bytes to eliminate Base64 decoding allocations per message.
    /// </summary>
    public static async Task<JsonNode?> DecodeFrameBodyAsync(
        ReadOnlyMemory<byte> body, byte[]? cipherKeyBytes, CancellationToken ct = default)
    {
        WsEnvelope? envelope = JsonSerializer.Deserialize(body.Span, CryptoJsonContext.Default.WsEnvelope);
        if (envelope?.Data is null)
        {
            return null;
        }

        return envelope.Encrypt switch
        {
            0 => ParseJsonSafe(envelope.Data),
            1 => ParseJsonSafe(await InflateBase64Async(envelope.Data, ct).ConfigureAwait(false)),
            2 => await DecryptGcmThenInflateAsync(envelope.Data, cipherKeyBytes, inflate: true, ct).ConfigureAwait(false),
            3 => await DecryptGcmThenInflateAsync(envelope.Data, cipherKeyBytes, inflate: false, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown encrypt type: {envelope.Encrypt}")
        };
    }

    private static async Task<JsonNode?> DecryptGcmThenInflateAsync(
        string encodedData, byte[]? cipherKeyBytes, bool inflate, CancellationToken ct)
    {
        if (cipherKeyBytes is null)
        {
            throw new InvalidOperationException("cipherKey required for encrypt type 2/3");
        }

        ReadOnlySpan<char> charSpan = encodedData.Contains('%', StringComparison.Ordinal)
            ? Uri.UnescapeDataString(encodedData).AsSpan()
            : encodedData.AsSpan();

        int maxByteCount = ((charSpan.Length * 3) + 3) / 4;
        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maxByteCount);
        try
        {
            if (!Convert.TryFromBase64Chars(charSpan, rented, out int bytesWritten) || bytesWritten < 48)
            {
                throw new InvalidOperationException("AES-GCM buffer too short (min 48 bytes: 16 IV + 16 AAD + 16 tag)");
            }

            ReadOnlySpan<byte> buf = rented.AsSpan(0, bytesWritten);
            ReadOnlySpan<byte> iv = buf[..16];
            ReadOnlySpan<byte> aad = buf.Slice(16, 16);
            ReadOnlySpan<byte> ctWithTag = buf[32..];

            byte[] plaintext = AesGcmAnyNonce.Decrypt(cipherKeyBytes, iv, aad, ctWithTag);

            if (!inflate)
            {
                return ParseJsonSafe(plaintext);
            }

            byte[] inflated = await InflateAsync(plaintext, ct).ConfigureAwait(false);
            return ParseJsonSafe(inflated);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static async Task<byte[]> InflateBase64Async(string base64, CancellationToken ct)
    {
        byte[] buf = Convert.FromBase64String(base64);
        return await InflateAsync(buf, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> InflateAsync(byte[] data, CancellationToken ct)
    {
        static Stream MakeDecompressor(MemoryStream src, byte[] headerData)
        {
            if (headerData.Length >= 2 && headerData[0] == 0x1f && headerData[1] == 0x8b)
            {
                return new GZipStream(src, CompressionMode.Decompress);
            }
            if (headerData.Length >= 1 && (headerData[0] & 0x0f) == 0x08)
            {
                return new ZLibStream(src, CompressionMode.Decompress);
            }
            return new DeflateStream(src, CompressionMode.Decompress);
        }

        await using MemoryStream ms = new(data);
        await using Stream dec = MakeDecompressor(ms, data);
        await using MemoryStream outMs = new();
        await dec.CopyToAsync(outMs, ct).ConfigureAwait(false);
        return outMs.ToArray();
    }

    private static JsonNode? ParseJsonSafe(ReadOnlySpan<byte> utf8Json)
        => JsonNode.Parse(utf8Json);

    private static JsonNode? ParseJsonSafe(string json)
        => JsonNode.Parse(json);
}
