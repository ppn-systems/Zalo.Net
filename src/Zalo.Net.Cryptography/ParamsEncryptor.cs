// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Zalo.Net.Cryptography;

/// <summary>
/// Handles zcid creation and encryptKey derivation for encrypted Zalo API request parameters.
/// </summary>
public sealed class ParamsEncryptor
{
    private const string ZcidAesKey = "3FC4F0D2AB50057BCE0D90D9187A22B1";

    private readonly string _zcid;
    private readonly string _zcidExt;
    private string? _encryptKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParamsEncryptor"/> class.
    /// </summary>
    public ParamsEncryptor(int type, string imei, long firstLaunchTime)
    {
        _zcid = CreateZcid(type, imei, firstLaunchTime);
        _zcidExt = RandomHexString();
        this.CreateEncryptKey();
    }

    internal ParamsEncryptor(int type, string imei, long firstLaunchTime, string zcidExt)
    {
        _zcid = CreateZcid(type, imei, firstLaunchTime);
        _zcidExt = zcidExt;
        this.CreateEncryptKey();
    }

    /// <summary>
    /// Gets the derived 32-character encryption key.
    /// </summary>
    public string GetEncryptKey()
        => _encryptKey ?? throw new InvalidOperationException("encryptKey not derived");

    /// <summary>
    /// Gets the parameter tuple (zcid, zcid_ext, enc_ver).
    /// </summary>
    public (string Zcid, string ZcidExt, string EncVer) GetParams()
        => (_zcid, _zcidExt, "v2");

    /// <summary>
    /// Encrypts JSON parameter payload with the derived key.
    /// </summary>
    public string EncryptData(string jsonData)
        => ZaloCipher.EncodeAesUtf8Key(this.GetEncryptKey(), jsonData, hex: false);

    private static string CreateZcid(int type, string imei, long firstLaunchTime)
    {
        if (string.IsNullOrEmpty(imei))
        {
            throw new ArgumentException("IMEI is required", nameof(imei));
        }
        string msg = $"{type},{imei},{firstLaunchTime}";
        return ZaloCipher.EncodeAesUtf8Key(ZcidAesKey, msg, hex: true, uppercase: true);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Retry mechanism handles generation failures up to 3 times")]
    private void CreateEncryptKey(int retry = 0)
    {
        try
        {
            string md5ExtHex = ComputeMd5Hex(_zcidExt).ToUpperInvariant();
            if (TryBuildKey(md5ExtHex, _zcid, out string? key))
            {
                _encryptKey = key;
                return;
            }
        }
        catch { /* fall through to retry */ }

        if (retry < 3)
        {
            this.CreateEncryptKey(retry + 1);
        }
    }

    private static bool TryBuildKey(string n, string zcid, [NotNullWhen(true)] out string? key)
    {
        key = string.Empty;
        (List<char>? nEven, _) = ProcessStr(n);
        (List<char>? zcidEven, List<char>? zcidOdd) = ProcessStr(zcid);

        if (nEven is null || zcidEven is null || zcidOdd is null)
        {
            return false;
        }

        StringBuilder sb = new(32);
        for (int i = 0; i < 8 && i < nEven.Count; i++)
        {
            _ = sb.Append(nEven[i]);
        }
        for (int i = 0; i < 12 && i < zcidEven.Count; i++)
        {
            _ = sb.Append(zcidEven[i]);
        }
        List<char> zcidOddRev = [.. zcidOdd];
        zcidOddRev.Reverse();
        for (int i = 0; i < 12 && i < zcidOddRev.Count; i++)
        {
            _ = sb.Append(zcidOddRev[i]);
        }

        key = sb.ToString();
        return key.Length == 32;
    }

    private static (List<char>? Even, List<char>? Odd) ProcessStr(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return (null, null);
        }
        List<char> even = [];
        List<char> odd = [];
        for (int i = 0; i < s.Length; i++)
        {
            (i % 2 == 0 ? even : odd).Add(s[i]);
        }
        return (even, odd);
    }

    private static string RandomHexString()
    {
        int len = Random.Shared.Next(6, 13);
        return Convert.ToHexString(RandomNumberGenerator.GetBytes((len + 1) / 2))
                      .ToLowerInvariant()[..len];
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 required by Zalo protocol specification")]
    internal static string ComputeMd5Hex(string input)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
