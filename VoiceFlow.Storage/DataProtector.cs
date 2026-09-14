using System.Security.Cryptography;
using System.Text;

namespace VoiceFlow.Storage;

/// <summary>
/// DPAPI (CurrentUser scope) protection for the API key. The plain value never touches
/// settings.json or the logs.
/// </summary>
public static class DataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VoiceFlow.ApiKey.v1");

    public static string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        Array.Clear(bytes);
        return Convert.ToBase64String(encrypted);
    }

    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
        {
            return null;
        }

        try
        {
            var encrypted = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Written by a different Windows user, or corrupted: treat it as "no key set".
            return null;
        }
    }
}
