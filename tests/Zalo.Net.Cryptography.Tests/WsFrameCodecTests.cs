using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Xunit;

namespace Zalo.Net.Cryptography.Tests;

public sealed class WsFrameCodecTests
{
    [Fact]
    public void ParseHeader_Cmd501_CorrectlyDecoded()
    {
        byte[] header = [0x01, 0xF5, 0x01, 0x00];
        (byte version, int cmd, byte subCmd) = WsFrameCodec.ParseHeader(header);
        Assert.Equal(1, version);
        Assert.Equal(501, cmd);
        Assert.Equal(0, subCmd);
    }

    [Fact]
    public void ParseHeader_CipherKeyFrame_CorrectlyDecoded()
    {
        byte[] header = [0x01, 0x01, 0x00, 0x01];
        (byte version, int cmd, byte subCmd) = WsFrameCodec.ParseHeader(header);
        Assert.Equal(1, version);
        Assert.Equal(1, cmd);
        Assert.Equal(1, subCmd);
    }

    [Fact]
    public void ParseHeader_TooShort_Throws()
        => _ = Assert.Throws<InvalidOperationException>(() => WsFrameCodec.ParseHeader([0x01, 0x01]));

    [Fact]
    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming", Justification = "Test helper")]
    [SuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break in AOT", Justification = "Test helper")]
    public async Task DecodeFrameBody_Encrypt0_ParsesPlainJson()
    {
        const string innerJson = /*lang=json,strict*/ """{"msgs":[{"msgId":"1234567890123456789","content":"hello"}]}""";
        string envelope = $$"""{"data":{{System.Text.Json.JsonSerializer.Serialize(innerJson)}},"encrypt":0}""";
        ReadOnlyMemory<byte> body = new(Encoding.UTF8.GetBytes(envelope));

        JsonNode? result = await WsFrameCodec.DecodeFrameBodyAsync(body, cipherKey: null);
        Assert.NotNull(result);

        string? msgId = result!["msgs"]?[0]?["msgId"]?.ToJsonString();
        Assert.Contains("1234567890123456789", msgId!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecodeFrameBody_BigIntMsgId_PreservedAsString()
    {
        const string innerJson = /*lang=json,strict*/ """{"msgId":"9007199254740993","cliMsgId":"9007199254740994"}""";
        byte[] deflated = DeflateZlib(Encoding.UTF8.GetBytes(innerJson));
        string b64 = Convert.ToBase64String(deflated);
        string envelope = $$"""{"data":"{{b64}}","encrypt":1}""";
        ReadOnlyMemory<byte> body = new(Encoding.UTF8.GetBytes(envelope));

        JsonNode? result = await WsFrameCodec.DecodeFrameBodyAsync(body, cipherKey: null);
        Assert.NotNull(result);

        JsonNode? msgIdNode = result!["msgId"];
        Assert.NotNull(msgIdNode);
        string msgIdStr = msgIdNode!.GetValue<string>();
        Assert.Equal("9007199254740993", msgIdStr);
    }

    [Fact]
    public async Task DecodeFrameBody_Encrypt2_AesGcmRoundTrip()
    {
        const string innerJson = /*lang=json,strict*/ """{"msgs":[{"msgId":"1111222233334444","content":"test"}]}""";
        byte[] keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        string cipherKey = Convert.ToBase64String(keyBytes);

        (byte[] encryptedFrame, _) = CreateSyntheticGcmFrame(innerJson, keyBytes, inflate: true);
        string b64 = Convert.ToBase64String(encryptedFrame);
        string envelope = $$"""{"data":"{{b64}}","encrypt":2}""";
        ReadOnlyMemory<byte> body = new(Encoding.UTF8.GetBytes(envelope));

        JsonNode? result = await WsFrameCodec.DecodeFrameBodyAsync(body, cipherKey);
        Assert.NotNull(result);
        string? msgId = result!["msgs"]?[0]?["msgId"]?.GetValue<string>()
                    ?? result["msgs"]?[0]?["msgId"]?.ToJsonString();
        Assert.Equal("1111222233334444", msgId);
    }

    [Fact]
    public async Task DecodeFrameBody_Encrypt3_AesGcmNoInflate()
    {
        const string innerJson = /*lang=json,strict*/ """{"key":"somekey"}""";
        byte[] keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        string cipherKey = Convert.ToBase64String(keyBytes);

        (byte[] encryptedFrame, _) = CreateSyntheticGcmFrame(innerJson, keyBytes, inflate: false);
        string b64 = Convert.ToBase64String(encryptedFrame);
        string envelope = $$"""{"data":"{{b64}}","encrypt":3}""";
        ReadOnlyMemory<byte> body = new(Encoding.UTF8.GetBytes(envelope));

        JsonNode? result = await WsFrameCodec.DecodeFrameBodyAsync(body, cipherKey);
        Assert.NotNull(result);
        Assert.Equal("somekey", result!["key"]?.GetValue<string>());
    }

    [Fact]
    public async Task DecodeFrameBody_Encrypt2_MissingCipherKey_Throws()
    {
        string fakeData = Convert.ToBase64String(new byte[64]);
        string envelope = $$"""{"data":"{{fakeData}}","encrypt":2}""";
        ReadOnlyMemory<byte> body = new(Encoding.UTF8.GetBytes(envelope));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WsFrameCodec.DecodeFrameBodyAsync(body, cipherKey: null));
    }

    [Fact]
    public async Task DecodeFrameBody_UnknownEncryptType_ThrowsInvalidOperationException()
    {
        string envelope = /*lang=json,strict*/ """{"data":"hello","encrypt":99}""";
        ReadOnlyMemory<byte> body = new(Encoding.UTF8.GetBytes(envelope));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WsFrameCodec.DecodeFrameBodyAsync(body, cipherKey: null));
    }

    private static byte[] DeflateZlib(byte[] data)
    {
        using MemoryStream ms = new();
        using ZLibStream zlib = new(ms, CompressionLevel.Optimal);
        zlib.Write(data);
        zlib.Flush();
        zlib.Dispose();
        return ms.ToArray();
    }

    private static (byte[] Frame, byte[] KeyBytes) CreateSyntheticGcmFrame(
        string json, byte[] keyBytes, bool inflate)
    {
        byte[] plainBytes = inflate
            ? DeflateZlib(Encoding.UTF8.GetBytes(json))
            : Encoding.UTF8.GetBytes(json);

        byte[] iv = new byte[16];
        byte[] aad = new byte[16];
        RandomNumberGenerator.Fill(iv);
        RandomNumberGenerator.Fill(aad);

        GcmBlockCipher cipher = new(new AesEngine());
        AeadParameters parameters = new(new KeyParameter(keyBytes), 128, iv, aad);
        cipher.Init(true, parameters);

        byte[] ctWithTag = new byte[cipher.GetOutputSize(plainBytes.Length)];
        int len = cipher.ProcessBytes(plainBytes, 0, plainBytes.Length, ctWithTag, 0);
        _ = cipher.DoFinal(ctWithTag, len);

        byte[] frame = new byte[16 + 16 + ctWithTag.Length];
        iv.CopyTo(frame, 0);
        aad.CopyTo(frame, 16);
        ctWithTag.CopyTo(frame, 32);

        return (frame, keyBytes);
    }
}
