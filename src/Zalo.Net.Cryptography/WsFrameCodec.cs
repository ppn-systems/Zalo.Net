// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

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
    public static async Task<JsonNode?> DecodeFrameBodyAsync(
        ReadOnlyMemory<byte> body, string? cipherKey, CancellationToken ct = default)
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
            2 => await DecryptGcmThenInflateAsync(envelope.Data, cipherKey, inflate: true, ct).ConfigureAwait(false),
            3 => await DecryptGcmThenInflateAsync(envelope.Data, cipherKey, inflate: false, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown encrypt type: {envelope.Encrypt}")
        };
    }

    private static async Task<JsonNode?> DecryptGcmThenInflateAsync(
        string encodedData, string? cipherKey, bool inflate, CancellationToken ct)
    {
        if (cipherKey is null)
        {
            throw new InvalidOperationException("cipherKey required for encrypt type 2/3");
        }

        string decoded = Uri.UnescapeDataString(encodedData);
        byte[] buf = Convert.FromBase64String(decoded);

        if (buf.Length < 48)
        {
            throw new InvalidOperationException("AES-GCM buffer too short (min 48 bytes: 16 IV + 16 AAD + 16 tag)");
        }

        byte[] iv = buf[..16];
        byte[] aad = buf[16..32];
        byte[] ctWithTag = buf[32..];

        byte[] keyBytes = Convert.FromBase64String(cipherKey);
        byte[] plaintext = AesGcmDecrypt(keyBytes, iv, aad, ctWithTag);

        if (!inflate)
        {
            return ParseJsonSafe(Encoding.UTF8.GetString(plaintext));
        }

        byte[] inflated = await InflateAsync(plaintext, ct).ConfigureAwait(false);
        return ParseJsonSafe(Encoding.UTF8.GetString(inflated));
    }

    private static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] aad, byte[] ctWithTag)
    {
        GcmBlockCipher cipher = new(new AesEngine());
        AeadParameters parameters = new(
            new KeyParameter(key),
            macSize: 128,
            nonce: nonce,
            associatedText: aad);
        cipher.Init(forEncryption: false, parameters);

        byte[] output = new byte[cipher.GetOutputSize(ctWithTag.Length)];
        int len = cipher.ProcessBytes(ctWithTag, 0, ctWithTag.Length, output, 0);
        _ = cipher.DoFinal(output, len);
        return output;
    }

    private static async Task<string> InflateBase64Async(string base64, CancellationToken ct)
    {
        byte[] buf = Convert.FromBase64String(base64);
        byte[] inflated = await InflateAsync(buf, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(inflated);
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

    private static JsonNode? ParseJsonSafe(string json)
        => JsonNode.Parse(json);
}
