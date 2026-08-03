using System;
using System.Collections.Generic;
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

    public ParamsEncryptor(int type, string imei, long firstLaunchTime)
    {
        _zcid = CreateZcid(type, imei, firstLaunchTime);
        _zcidExt = RandomHexString();
        CreateEncryptKey();
    }

    internal ParamsEncryptor(int type, string imei, long firstLaunchTime, string zcidExt)
    {
        _zcid = CreateZcid(type, imei, firstLaunchTime);
        _zcidExt = zcidExt;
        CreateEncryptKey();
    }

    public string GetEncryptKey()
        => _encryptKey ?? throw new InvalidOperationException("encryptKey not derived");

    public (string Zcid, string ZcidExt, string EncVer) GetParams()
        => (_zcid, _zcidExt, "v2");

    public string EncryptData(string jsonData)
        => ZaloCipher.EncodeAesUtf8Key(GetEncryptKey(), jsonData, hex: false);

    private static string CreateZcid(int type, string imei, long firstLaunchTime)
    {
        if (string.IsNullOrEmpty(imei)) throw new ArgumentException("IMEI is required", nameof(imei));
        var msg = $"{type},{imei},{firstLaunchTime}";
        return ZaloCipher.EncodeAesUtf8Key(ZcidAesKey, msg, hex: true, uppercase: true);
    }

    private void CreateEncryptKey(int retry = 0)
    {
        try
        {
            var md5ExtHex = ComputeMd5Hex(_zcidExt).ToUpperInvariant();
            if (TryBuildKey(md5ExtHex, _zcid, out var key))
            {
                _encryptKey = key;
                return;
            }
        }
        catch { /* fall through to retry */ }

        if (retry < 3) CreateEncryptKey(retry + 1);
    }

    private static bool TryBuildKey(string n, string zcid, out string key)
    {
        key = string.Empty;
        var (nEven, _) = ProcessStr(n);
        var (zcidEven, zcidOdd) = ProcessStr(zcid);

        if (nEven is null || zcidEven is null || zcidOdd is null) return false;

        var sb = new StringBuilder(32);
        for (int i = 0; i < 8 && i < nEven.Count; i++) _ = sb.Append(nEven[i]);
        for (int i = 0; i < 12 && i < zcidEven.Count; i++) _ = sb.Append(zcidEven[i]);
        var zcidOddRev = new List<char>(zcidOdd);
        zcidOddRev.Reverse();
        for (int i = 0; i < 12 && i < zcidOddRev.Count; i++) _ = sb.Append(zcidOddRev[i]);

        key = sb.ToString();
        return key.Length == 32;
    }

    private static (List<char>? Even, List<char>? Odd) ProcessStr(string? s)
    {
        if (string.IsNullOrEmpty(s)) return (null, null);
        var even = new List<char>();
        var odd = new List<char>();
        for (int i = 0; i < s.Length; i++)
        {
            (i % 2 == 0 ? even : odd).Add(s[i]);
        }
        return (even, odd);
    }

    private static string RandomHexString()
    {
        var len = Random.Shared.Next(6, 13);
        return Convert.ToHexString(RandomNumberGenerator.GetBytes((len + 1) / 2))
                      .ToLowerInvariant()[..len];
    }

    internal static string ComputeMd5Hex(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
