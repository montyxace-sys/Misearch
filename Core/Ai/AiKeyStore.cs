using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Misearch.Core.Ai;

/// <summary>
/// Keeps the provider's API key on disk, encrypted with Windows DPAPI for the current
/// user, so it is never written in clear text next to the settings.
/// </summary>
public static class AiKeyStore
{
    private const string Entropy = "Misearch.AiKey.v1";

    private static byte[] EntropyBytes => Encoding.UTF8.GetBytes(Entropy);

    /// <summary>Encrypts a secret. Outside Windows (tests on CI) it is only base64.</summary>
    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return "";

        var data = Encoding.UTF8.GetBytes(secret);
        if (!OperatingSystem.IsWindows())
            return Convert.ToBase64String(data);

        var blob = ProtectedData.Protect(data, EntropyBytes, DataProtectionScope.CurrentUser);
        return "dpapi:" + Convert.ToBase64String(blob);
    }

    /// <summary>Decrypts a value written by <see cref="Protect"/>. Empty on failure.</summary>
    public static string Unprotect(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return "";

        try
        {
            if (stored.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsWindows())
                    return "";
                var blob = Convert.FromBase64String(stored["dpapi:".Length..]);
                var data = ProtectedData.Unprotect(blob, EntropyBytes, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(stored));
        }
        catch (Exception)
        {
            // A key copied from another user or machine cannot be decrypted: treat it as absent.
            return "";
        }
    }

    /// <summary>Hides all but the last four characters, for showing it back in the UI.</summary>
    public static string Mask(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return "";
        if (secret.Length <= 4)
            return new string('•', secret.Length);
        return "••••••" + secret[^4..];
    }
}
