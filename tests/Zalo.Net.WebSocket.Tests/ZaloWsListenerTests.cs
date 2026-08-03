// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zalo.Net.Contracts;

namespace Zalo.Net.WebSocket.Tests;

public sealed class ZaloWsListenerTests
{
    [Fact]
    public async Task Cmd501Frame_RaisesMessageEvent_WithCorrectFields()
    {
        List<ZaloMessageEvent> events = [];
        ZaloSession session = MakeSession();
        ZaloWsListener listener = new(session, events.Add, _ => { });

        const string innerJson = """
            {"data":{"msgs":[{"msgId":"9007199254740993","cliMsgId":"9007199254740994",
             "msgType":"webchat","uidFrom":"111","idTo":"222","dName":"Alice",
             "ts":"1700000000000","content":"hello","isSelf":0}]}}
            """;
        byte[] frame = BuildFrame(cmd: 501, subCmd: 0, bodyJson: innerJson, encrypt: 0, keyBytes: null);
        await InvokeDispatch(listener, frame);

        _ = Assert.Single(events);
        ZaloMessageEvent e = events[0];
        Assert.Equal("9007199254740993", e.MsgId);
        Assert.Equal("9007199254740994", e.CliMsgId);
        Assert.Equal("111", e.UidFrom);
        Assert.Equal("Alice", e.DisplayName);
        Assert.False(e.IsSelf);
        Assert.Equal(ZaloThreadType.User, e.ThreadType);
    }

    [Fact]
    public async Task Cmd501Frame_IsSelf_True_WhenValueIs1()
    {
        List<ZaloMessageEvent> events = [];
        ZaloWsListener listener = new(MakeSession(), events.Add, _ => { });

        const string innerJson = """{"data":{"msgs":[{"msgId":"1","cliMsgId":"2","uidFrom":"me","idTo":"you","isSelf":1}]}}""";
        byte[] frame = BuildFrame(501, 0, innerJson, encrypt: 0, keyBytes: null);
        await InvokeDispatch(listener, frame);

        _ = Assert.Single(events);
        Assert.True(events[0].IsSelf);
    }

    [Fact]
    public async Task Cmd501Frame_IsSelf_True_WhenUidFromIsZero()
    {
        List<ZaloMessageEvent> events = [];
        ZaloWsListener listener = new(MakeSession(), events.Add, _ => { });

        const string innerJson = """{"data":{"msgs":[{"msgId":"1","uidFrom":"0","isSelf":0,"cliMsgId":"2","idTo":"other"}]}}""";
        byte[] frame = BuildFrame(501, 0, innerJson, encrypt: 0, keyBytes: null);
        await InvokeDispatch(listener, frame);

        _ = Assert.Single(events);
        Assert.True(events[0].IsSelf);
    }

    [Fact]
    public async Task Cmd501Frame_IsSelf_True_WhenUidFromEqualsSessionUid()
    {
        List<ZaloMessageEvent> events = [];
        ZaloWsListener listener = new(MakeSession(), events.Add, _ => { });

        const string innerJson = """{"data":{"msgs":[{"msgId":"11","cliMsgId":"12","uidFrom":"uid","idTo":"other"}]}}""";
        byte[] frame = BuildFrame(501, 0, innerJson, encrypt: 0, keyBytes: null);
        await InvokeDispatch(listener, frame);

        _ = Assert.Single(events);
        Assert.True(events[0].IsSelf);
    }

    [Fact]
    public async Task Cmd521Frame_ThreadType_IsGroup()
    {
        List<ZaloMessageEvent> events = [];
        ZaloWsListener listener = new(MakeSession(), events.Add, _ => { });

        const string innerJson = """{"data":{"groupMsgs":[{"msgId":"42","cliMsgId":"43","uidFrom":"u1","idTo":"g1","threadId":"g1"}]}}""";
        byte[] frame = BuildFrame(521, 0, innerJson, encrypt: 0, keyBytes: null);
        await InvokeDispatch(listener, frame);

        _ = Assert.Single(events);
        Assert.Equal(ZaloThreadType.Group, events[0].ThreadType);
        Assert.Equal("g1", events[0].ThreadId);
    }

    [Fact]
    public async Task UnknownCmd_NoEvents_Raised()
    {
        List<ZaloMessageEvent> events = [];
        ZaloWsListener listener = new(MakeSession(), events.Add, _ => { });

        byte[] frame = BuildFrame(999, 0, """{"data":"irrelevant","encrypt":0}""", encrypt: 0, keyBytes: null);
        await InvokeDispatch(listener, frame);

        Assert.Empty(events);
    }

    [Fact]
    public async Task Cmd501Frame_Encrypt1_InflatedPayload_ParsedCorrectly()
    {
        List<ZaloMessageEvent> events = [];
        ZaloWsListener listener = new(MakeSession(), events.Add, _ => { });

        const string innerJson = """{"data":{"msgs":[{"msgId":"555","cliMsgId":"556","uidFrom":"a","idTo":"b"}]}}""";
        byte[] frame = BuildFrame(501, 0, innerJson, encrypt: 1, keyBytes: null);
        await InvokeDispatch(listener, frame);

        _ = Assert.Single(events);
        Assert.Equal("555", events[0].MsgId);
    }

    [Fact]
    public async Task Cmd501Frame_Encrypt2_AesGcm_ParsedCorrectly()
    {
        byte[] keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        string cipherKey = Convert.ToBase64String(keyBytes);

        List<ZaloMessageEvent> events = [];
        ZaloWsListener listener = new(MakeSession(cipherKey), events.Add, _ => { });

        const string innerJson = """{"data":{"msgs":[{"msgId":"777","cliMsgId":"778","uidFrom":"x","idTo":"y"}]}}""";
        byte[] frame = BuildFrame(501, 0, innerJson, encrypt: 2, keyBytes);
        await InvokeDispatch(listener, frame);

        _ = Assert.Single(events);
        Assert.Equal("777", events[0].MsgId);
    }

    [Fact]
    public async Task Cmd501Frame_MultipleMessages_RaisesMultipleEvents()
    {
        List<ZaloMessageEvent> events = [];
        ZaloWsListener listener = new(MakeSession(), events.Add, _ => { });

        const string innerJson = """
            {"data":{"msgs":[
                {"msgId":"101","cliMsgId":"102","uidFrom":"u1","idTo":"u2","content":"msg1"},
                {"msgId":"201","cliMsgId":"202","uidFrom":"u3","idTo":"u4","content":"msg2"}
            ]}}
            """;
        byte[] frame = BuildFrame(501, 0, innerJson, encrypt: 0, keyBytes: null);
        await InvokeDispatch(listener, frame);

        Assert.Equal(2, events.Count);
        Assert.Equal("101", events[0].MsgId);
        Assert.Equal("201", events[1].MsgId);
    }

    private static ZaloSession MakeSession(string? cipherKey = null) =>
        new(new ZaloSessionMaterial("[]", cipherKey ?? "", "imei", "uid", "ua"),
            "uid", ["wss://ws.zalo.me"], new Dictionary<string, string[]>(), 20_000);

    [SuppressMessage("Trimming", "IL2075", Justification = "Reflection test helper")]
    private static async Task InvokeDispatch(ZaloWsListener listener, byte[] frame)
    {
        MethodInfo method = typeof(ZaloWsListener).GetMethod(
            "DispatchFrameAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DispatchFrameAsync not found via reflection");

        object state = Activator.CreateInstance(
            typeof(ZaloWsListener).GetNestedType("CipherState", BindingFlags.NonPublic)!)!;

        FieldInfo sessionField = typeof(ZaloWsListener).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ZaloSession session = (ZaloSession)sessionField.GetValue(listener)!;
        if (!string.IsNullOrEmpty(session.Material.SecretKey))
        {
            FieldInfo keyProp = state.GetType().GetField("Key", BindingFlags.Public | BindingFlags.Instance)!;
            keyProp.SetValue(state, session.Material.SecretKey);
        }

        Task task = (Task)method.Invoke(listener, [frame, state, CancellationToken.None])!;
        await task;
    }

    [SuppressMessage("Trimming", "IL2026", Justification = "Test helper")]
    [SuppressMessage("AOT", "IL3050", Justification = "Test helper")]
    private static byte[] BuildFrame(int cmd, int subCmd, string bodyJson, int encrypt, byte[]? keyBytes = null)
    {
        string data;
        if (encrypt == 0)
        {
            data = System.Text.Json.JsonSerializer.Serialize(bodyJson);
        }
        else if (encrypt == 1)
        {
            string b64 = Convert.ToBase64String(DeflateZlib(Encoding.UTF8.GetBytes(bodyJson)));
            data = $"\"{b64}\"";
        }
        else
        {
            string b64 = Convert.ToBase64String(EncryptGcm(bodyJson, keyBytes!, inflate: encrypt == 2));
            data = $"\"{b64}\"";
        }

        string envelope = $$$"""{"data":{{{data}}},"encrypt":{{{encrypt}}}}""";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(envelope);

        byte[] header =
        [
            0x01,
            (byte)(cmd & 0xFF),
            (byte)((cmd >> 8) & 0xFF),
            (byte)subCmd,
        ];

        byte[] frame = new byte[4 + bodyBytes.Length];
        header.CopyTo(frame, 0);
        bodyBytes.CopyTo(frame, 4);
        return frame;
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

    private static byte[] EncryptGcm(string json, byte[] keyBytes, bool inflate)
    {
        byte[] plain = inflate
            ? DeflateZlib(Encoding.UTF8.GetBytes(json))
            : Encoding.UTF8.GetBytes(json);

        byte[] iv = new byte[16];
        byte[] aad = new byte[16];
        RandomNumberGenerator.Fill(iv);
        RandomNumberGenerator.Fill(aad);

        GcmBlockCipher cipher = new(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(keyBytes), 128, iv, aad));
        byte[] ctTag = new byte[cipher.GetOutputSize(plain.Length)];
        int len = cipher.ProcessBytes(plain, 0, plain.Length, ctTag, 0);
        _ = cipher.DoFinal(ctTag, len);

        byte[] buf = new byte[16 + 16 + ctTag.Length];
        iv.CopyTo(buf, 0);
        aad.CopyTo(buf, 16);
        ctTag.CopyTo(buf, 32);
        return buf;
    }
}
