using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Zalo.Net.Cryptography;

/// <summary>
/// Decodes binary WebSocket frames sent by Zalo servers (header parsing, AES-GCM decryption via BouncyCastle, Deflate/GZip inflation, JSON parsing).
/// </summary>
public static class WsFrameCodec
{
    private static readonly JsonSerializerOptions EnvelopeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Parses the 4-byte Zalo WS frame header (Version, Cmd, SubCmd).
    /// </summary>
    public static (byte Version, int Cmd, byte SubCmd) ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4) throw new InvalidOperationException("Frame header must be at least 4 bytes");
        var version = header[0];
        var cmd = BinaryPrimitives.ReadUInt16LittleEndian(header[1..3]);
        var subCmd = header[3];
        return (version, cmd, subCmd);
    }

    /// <summary>
    /// Decodes frame body according to envelope encrypt type (0 = raw json, 1 = base64+zlib, 2 = AES-GCM+zlib, 3 = AES-GCM+utf8).
    /// </summary>
    public static async Task<JsonNode?> DecodeFrameBodyAsync(
        ReadOnlyMemory<byte> body, string? cipherKey, CancellationToken ct = default)
    {
        var envelope = JsonSerializer.Deserialize<WsEnvelope>(body.Span, EnvelopeOptions);
        if (envelope?.Data is null) return null;

        return envelope.Encrypt switch
        {
            0 => ParseJsonSafe(envelope.Data),
            1 => ParseJsonSafe(await InflateBase64Async(envelope.Data, ct)),
            2 => await DecryptGcmThenInflateAsync(envelope.Data, cipherKey, inflate: true, ct),
            3 => await DecryptGcmThenInflateAsync(envelope.Data, cipherKey, inflate: false, ct),
            _ => throw new InvalidOperationException($"Unknown encrypt type: {envelope.Encrypt}")
        };
    }

    private static async Task<JsonNode?> DecryptGcmThenInflateAsync(
        string encodedData, string? cipherKey, bool inflate, CancellationToken ct)
    {
        if (cipherKey is null)
            throw new InvalidOperationException("cipherKey required for encrypt type 2/3");

        var decoded = Uri.UnescapeDataString(encodedData);
        var buf = Convert.FromBase64String(decoded);

        if (buf.Length < 48)
            throw new InvalidOperationException("AES-GCM buffer too short (min 48 bytes: 16 IV + 16 AAD + 16 tag)");

        var iv = buf[..16];
        var aad = buf[16..32];
        var ctWithTag = buf[32..];

        var keyBytes = Convert.FromBase64String(cipherKey);
        var plaintext = AesGcmDecrypt(keyBytes, iv, aad, ctWithTag);

        if (!inflate) return ParseJsonSafe(Encoding.UTF8.GetString(plaintext));

        var inflated = await InflateAsync(plaintext, ct);
        return ParseJsonSafe(Encoding.UTF8.GetString(inflated));
    }

    private static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] aad, byte[] ctWithTag)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(
            new KeyParameter(key),
            macSize: 128,
            nonce: nonce,
            associatedText: aad);
        cipher.Init(forEncryption: false, parameters);

        var output = new byte[cipher.GetOutputSize(ctWithTag.Length)];
        var len = cipher.ProcessBytes(ctWithTag, 0, ctWithTag.Length, output, 0);
        _ = cipher.DoFinal(output, len);
        return output;
    }

    private static async Task<string> InflateBase64Async(string base64, CancellationToken ct)
    {
        var buf = Convert.FromBase64String(base64);
        var inflated = await InflateAsync(buf, ct);
        return Encoding.UTF8.GetString(inflated);
    }

    private static async Task<byte[]> InflateAsync(byte[] data, CancellationToken ct)
    {
        Stream MakeDecompressor(MemoryStream src)
        {
            if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
                return new GZipStream(src, CompressionMode.Decompress);
            if (data.Length >= 1 && (data[0] & 0x0f) == 0x08)
                return new ZLibStream(src, CompressionMode.Decompress);
            return new DeflateStream(src, CompressionMode.Decompress);
        }

        await using var ms = new MemoryStream(data);
        await using var dec = MakeDecompressor(ms);
        await using var outMs = new MemoryStream();
        await dec.CopyToAsync(outMs, ct);
        return outMs.ToArray();
    }

    private static JsonNode? ParseJsonSafe(string json)
        => JsonNode.Parse(json);

    private sealed class WsEnvelope
    {
        public string? Data { get; set; }
        public int Encrypt { get; set; }
    }
}
