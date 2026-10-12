using System.Security.Cryptography;
using System.Text;

namespace Yptv;

// Playlist sources (URLs, logins) are stored encrypted for the current Windows user (DPAPI), never as plain text.
static class Dpapi
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("iptv-playlist-source");

    public static string Seal(string text) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy, DataProtectionScope.CurrentUser));

    // Null when the value can't be read (another Windows user, damaged file).
    public static string? Open(string? sealedText)
    {
        if (string.IsNullOrEmpty(sealedText)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(sealedText), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
