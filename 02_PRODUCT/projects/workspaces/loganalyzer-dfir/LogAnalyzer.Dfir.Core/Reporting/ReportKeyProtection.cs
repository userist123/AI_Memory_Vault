using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>
/// Protected storage of the report signing key. A key file is either the bare PKCS#8 base64 (legacy, readable by anyone who can read the file) or one of
/// <c>LAPK1-DPAPI:&lt;base64&gt;</c> (Windows DPAPI, current user scope: only this Windows account on this machine can decrypt it) and
/// <c>LAPK1-PASS:&lt;base64&gt;</c> (PBKDF2-SHA256 600000 iterations + AES-256-GCM, for keys that must move between machines).
/// </summary>
public static class ReportKeyProtection
{
    public const string DpapiPrefix = "LAPK1-DPAPI:", PassphrasePrefix = "LAPK1-PASS:";
    private const int Iterations = 600_000;

    public static bool IsProtected(string fileText) => fileText.TrimStart().StartsWith("LAPK1-", StringComparison.Ordinal);
    /// <summary>True for the legacy bare PKCS#8 key file (no LAPK1 envelope): usable, but readable by anyone who can read the file (security audit #20).</summary>
    public static bool IsLegacyBareKey(string fileText) => !IsProtected(fileText);

    /// <summary>Shown every time a legacy bare key is used; names the command that re-protects it in place.</summary>
    public const string LegacyBareKeyWarning =
        "ATENȚIE: cheia de semnare este stocată NECRIPTAT (format vechi PKCS#8): oricine poate citi fișierul poate semna în numele dumneavoastră. " +
        "Protejați-o acum cu: LogAnalyzer.KeyGen report-key-protect <fișier-cheie> [--passphrase].";

    public static bool NeedsPassphrase(string fileText) => fileText.TrimStart().StartsWith(PassphrasePrefix, StringComparison.Ordinal);

    /// <summary>Encrypts the PKCS#8 base64 key for the current Windows user (DPAPI).</summary>
    public static string ProtectForCurrentUser(string pkcs8Base64)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI există doar pe Windows; folosiți o parolă.");
        var blob = ProtectedData.Protect(Convert.FromBase64String(pkcs8Base64.Trim()), Entropy, DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(blob);
    }

    public static string ProtectWithPassphrase(string pkcs8Base64, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Parola nu poate fi goală.");
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);
        var plain = Convert.FromBase64String(pkcs8Base64.Trim());
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag, Encoding.ASCII.GetBytes("LAPK1"));
        return PassphrasePrefix + Convert.ToBase64String([.. salt, .. nonce, .. tag, .. cipher]);
    }

    /// <summary>The PKCS#8 base64 key out of a key file's text. Throws <see cref="CryptographicException"/> when it cannot be decrypted (other user, wrong passphrase, damaged).</summary>
    public static string Unprotect(string fileText, string? passphrase = null)
    {
        var t = fileText.Trim();
        if (!IsProtected(t)) return t;
        try
        {
            if (t.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsWindows()) throw new CryptographicException("Cheia este protejată cu DPAPI și poate fi deschisă doar pe Windows.");
                return Convert.ToBase64String(ProtectedData.Unprotect(Convert.FromBase64String(t[DpapiPrefix.Length..]), Entropy, DataProtectionScope.CurrentUser));
            }
            if (t.StartsWith(PassphrasePrefix, StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(passphrase)) throw new CryptographicException("Cheia este protejată cu parolă: parola lipsește.");
                var raw = Convert.FromBase64String(t[PassphrasePrefix.Length..]);
                if (raw.Length < 16 + 12 + 16 + 1) throw new CryptographicException("Fișierul cheii este deteriorat.");
                var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), raw[..16], Iterations, HashAlgorithmName.SHA256, 32);
                var plain = new byte[raw.Length - 44];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(raw[16..28], raw[44..], raw[28..44], plain, Encoding.ASCII.GetBytes("LAPK1"));
                return Convert.ToBase64String(plain);
            }
        }
        catch (FormatException ex) { throw new CryptographicException("Fișierul cheii este deteriorat: " + ex.Message); }
        throw new CryptographicException("Format de cheie necunoscut.");
    }

    private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("LogAnalyzer.report-signing-key/1");
}
