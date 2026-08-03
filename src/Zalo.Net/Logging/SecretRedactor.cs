using System;

namespace Zalo.Net.Logging;

/// <summary>
/// Helper to prevent sensitive secrets from being output into application logs.
/// </summary>
public static class SecretRedactor
{
    public const string RedactedValue = "[REDACTED]";

    public static string Redacted => RedactedValue;

    public static bool ContainsPotentialSecret(string logMessage)
    {
        if (logMessage == null)
        {
            return false;
        }

        return logMessage.Contains("imei", StringComparison.OrdinalIgnoreCase)
            || logMessage.Contains("zpsid", StringComparison.OrdinalIgnoreCase)
            || logMessage.Contains("zpw_sek", StringComparison.OrdinalIgnoreCase)
            || logMessage.Contains("secretKey", StringComparison.OrdinalIgnoreCase)
            || logMessage.Contains("cipherKey", StringComparison.OrdinalIgnoreCase);
    }
}
