using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Reporting;

public enum ReportSignatureStatus { Valid, Modified, UnknownKey, Missing, Invalid }

/// <summary>The detached signature written next to a report: "&lt;report&gt;.pdf.sig" (JSON). Signs the SHA-256 of the PDF, its name, size, signer and time.</summary>
public sealed class ReportSignatureFile
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = ReportSignature.Schema;
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("signer")] public string Signer { get; set; } = "";
    [JsonPropertyName("key_id")] public string KeyId { get; set; } = "";
    [JsonPropertyName("public_key")] public string PublicKey { get; set; } = "";
    [JsonPropertyName("signed_utc")] public DateTimeOffset SignedUtc { get; set; }
    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = "ECDSA-P256-SHA256";
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
}

public sealed record ReportSignatureResult(ReportSignatureStatus Status, string Message, ReportSignatureFile? Signature, string ActualSha256)
{
    /// <summary>VALID / MODIFIED / UNKNOWN KEY / MISSING / INVALID, as the CLI prints it.</summary>
    public string Word => Status switch
    {
        ReportSignatureStatus.Valid => "VALID", ReportSignatureStatus.Modified => "MODIFIED", ReportSignatureStatus.UnknownKey => "UNKNOWN KEY",
        ReportSignatureStatus.Missing => "MISSING", _ => "INVALID",
    };
}

/// <summary>
/// F3: digital signature of generated PDF reports with an ECDSA P-256 key held by the operator or the organisation (same scheme as the signed edition
/// policy and the data packs: PKCS#8 private key kept outside the application and the repository, SubjectPublicKeyInfo public key configured in the app).
/// The signature is detached (<c>.sig</c> next to the PDF) so the PDF bytes stay exactly what was generated, and anyone can recompute its SHA-256.
/// </summary>
public static class ReportSignature
{
    public const string Schema = "loganalyzer.report-signature/1";
    public const string Extension = ".sig";

    public static string SigPath(string reportPath) => reportPath + Extension;

    /// <summary>A new P-256 key pair: (private PKCS#8 base64, public SubjectPublicKeyInfo base64).</summary>
    public static (string PrivateKey, string PublicKey) GenerateKey()
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(k.ExportPkcs8PrivateKey()), Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Short fingerprint of a public key: first 16 hex characters of the SHA-256 of its SubjectPublicKeyInfo.</summary>
    public static string KeyId(string publicKeyBase64) => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyBase64.Trim())))[..16];

    /// <summary>Loads the key from a key file's text: bare PKCS#8 base64, or a DPAPI / passphrase protected file (see <see cref="ReportKeyProtection"/>).</summary>
    public static ECDsa LoadPrivateKey(string pkcs8Base64, string? passphrase = null)
    {
        var k = ECDsa.Create();
        k.ImportPkcs8PrivateKey(Convert.FromBase64String(ReportKeyProtection.Unprotect(pkcs8Base64, passphrase)), out _);
        if (k.KeySize != 256) { k.Dispose(); throw new CryptographicException("Cheia de semnare trebuie să fie ECDSA P-256."); }
        return k;
    }

    private static byte[] Payload(ReportSignatureFile s) => Encoding.UTF8.GetBytes(string.Join("\n",
        Schema, s.File, s.Sha256.ToUpperInvariant(), s.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Signer, s.KeyId, s.PublicKey,
        s.SignedUtc.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture), s.Algorithm));

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    /// <summary>Signs <paramref name="reportPath"/> and writes "&lt;report&gt;.sig". Returns the signature written.</summary>
    public static ReportSignatureFile Sign(string reportPath, ECDsa privateKey, string signer, DateTimeOffset? nowUtc = null)
    {
        var pub = Convert.ToBase64String(privateKey.ExportSubjectPublicKeyInfo());
        var s = new ReportSignatureFile
        {
            File = Path.GetFileName(reportPath), Sha256 = Sha256File(reportPath), Size = new FileInfo(reportPath).Length, Signer = signer.Trim(),
            KeyId = KeyId(pub), PublicKey = pub, SignedUtc = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
        };
        s.Signature = Convert.ToBase64String(privateKey.SignData(Payload(s), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        File.WriteAllText(SigPath(reportPath), JsonSerializer.Serialize(s, Opts), new UTF8Encoding(false));
        return s;
    }

    /// <summary>
    /// Verifies a report against its detached signature. UNKNOWN KEY when the signing key is not among <paramref name="trustedPublicKeys"/> (the signature
    /// proves nothing then); MODIFIED when the PDF's SHA-256 differs from the signed one or the signature file was altered; VALID otherwise.
    /// </summary>
    public static ReportSignatureResult Verify(string reportPath, IReadOnlyList<string> trustedPublicKeys, string? sigPath = null)
    {
        sigPath ??= SigPath(reportPath);
        string actual = "";
        ReportSignatureFile? s;
        try
        {
            if (!File.Exists(reportPath)) return new(ReportSignatureStatus.Missing, "Raportul nu există: " + Path.GetFileName(reportPath), null, "");
            actual = Sha256File(reportPath);
            if (!File.Exists(sigPath)) return new(ReportSignatureStatus.Missing, "Raportul nu are fișier de semnătură (" + Path.GetFileName(sigPath) + ").", null, actual);
            try { s = JsonSerializer.Deserialize<ReportSignatureFile>(File.ReadAllText(sigPath)); }
            catch (JsonException) { s = null; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(ReportSignatureStatus.Invalid, "Fișierul nu poate fi citit (" + ex.Message + "): verificarea nu s-a putut face.", null, actual);
        }
        if (s is not null)
        {
            // "field":null in the JSON assigns null to a non-nullable string: treat it as empty, never dereference it
            s.Schema ??= ""; s.File ??= ""; s.Sha256 ??= ""; s.Signer ??= ""; s.KeyId ??= ""; s.PublicKey ??= ""; s.Algorithm ??= ""; s.Signature ??= "";
        }
        if (s is null || s.Schema != Schema || s.Signature.Length == 0 || s.PublicKey.Length == 0 || s.Sha256.Length == 0)
            return new(ReportSignatureStatus.Invalid, "Fișierul de semnătură nu poate fi citit (format necunoscut).", s, actual);

        string? trusted = null;
        foreach (var k in trustedPublicKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            try { if (KeyId(k) == s.KeyId && Convert.FromBase64String(k.Trim()).AsSpan().SequenceEqual(Convert.FromBase64String(s.PublicKey))) { trusted = k.Trim(); break; } }
            catch (FormatException) { }
        }
        if (trusted is null)
            return new(ReportSignatureStatus.UnknownKey, $"CHEIE NECUNOSCUTĂ — raportul este semnat de „{s.Signer}” cu cheia {s.KeyId}, care nu este printre cheile de încredere configurate. Semnătura nu dovedește nimic până nu confirmați cheia la emitent.", s, actual);

        bool sigOk;
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(trusted), out _);
            sigOk = ec.VerifyData(Payload(s), Convert.FromBase64String(s.Signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { sigOk = false; }
        if (!sigOk)
            return new(ReportSignatureStatus.Modified, "SEMNĂTURĂ INVALIDĂ — fișierul de semnătură a fost modificat sau nu aparține acestui raport.", s, actual);
        if (!string.Equals(actual, s.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(ReportSignatureStatus.Modified, $"RAPORT MODIFICAT — SHA-256 actual {actual} diferă de cel semnat {s.Sha256} ({s.Signer}, {s.SignedUtc:yyyy-MM-dd HH:mm} UTC).", s, actual);
        return new(ReportSignatureStatus.Valid, $"SEMNĂTURĂ VALIDĂ — fișierul nu a fost modificat de la semnare. Semnat de „{s.Signer}” la {s.SignedUtc:yyyy-MM-dd HH:mm} UTC, cheia {s.KeyId}, SHA-256 {actual}.", s, actual);
    }

    public static string Sha256File(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(f));
    }
}

/// <summary>
/// Where the operator's signing key and the trusted public keys are configured (per Windows user, outside the repository and outside the case):
/// %APPDATA%\LogAnalyzer\report_signing.json. The private key itself stays in the file the operator chose (e.g. on a sealed medium); only its path is stored.
/// </summary>
public sealed class ReportSigningSettings
{
    public string PrivateKeyPath { get; set; } = "";
    public string Signer { get; set; } = "";
    public List<string> TrustedPublicKeys { get; set; } = [];

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogAnalyzer", "report_signing.json");

    public static ReportSigningSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ReportSigningSettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public bool CanSign => PrivateKeyPath.Length > 0 && File.Exists(PrivateKeyPath);

    /// <summary>Signs a just-generated report when a key is configured; returns the line to show (never throws for a missing or bad key: it says so).</summary>
    /// <param name="signerOverride">When given, written as the signer instead of the configured label or the Windows user name (masked exports pass the pseudonym).</param>
    public string SignIfConfigured(string reportPath, string? signerOverride = null)
    {
        if (PrivateKeyPath.Length == 0) return "Raportul NU este semnat digital: nu este configurată o cheie de semnare (Setări semnătură rapoarte).";
        if (!File.Exists(PrivateKeyPath)) return "Raportul NU este semnat digital: fișierul cheii de semnare nu este accesibil (mediul cu cheia nu este conectat?).";
        try
        {
            var keyText = File.ReadAllText(PrivateKeyPath);
            using var key = ReportSignature.LoadPrivateKey(keyText);
            var s = ReportSignature.Sign(reportPath, key, !string.IsNullOrWhiteSpace(signerOverride) ? signerOverride : Signer.Length > 0 ? Signer : Environment.UserName);
            var line = $"Raport semnat digital ({s.Signer}, cheia {s.KeyId}); semnătura: {Path.GetFileName(ReportSignature.SigPath(reportPath))}.";
            return ReportKeyProtection.IsLegacyBareKey(keyText) ? line + " " + ReportKeyProtection.LegacyBareKeyWarning : line;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException or UnauthorizedAccessException)
        {
            return "Raportul NU este semnat digital: cheia de semnare nu a putut fi citită (" + ex.Message + ").";
        }
    }
}
