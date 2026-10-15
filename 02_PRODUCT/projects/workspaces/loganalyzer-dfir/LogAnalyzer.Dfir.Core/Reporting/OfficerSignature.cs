using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>
/// The security officer's own signing key (owner decision 2026-10-16): ECDSA P-256, PKCS#8 encrypted with a passphrase (PBKDF2-SHA256 600000 + AES-256-GCM),
/// kept on the officer's medium (removable drive), never stored on the station. The envelope prefix and the authenticated data differ from the station's
/// report key (<see cref="ReportKeyProtection"/>) so one cannot be passed off as the other. There is no DPAPI or unencrypted variant: the key must not
/// be bound to, or readable on, the station it controls.
/// </summary>
public static class OfficerKey
{
    public const string Prefix = "LAOK1-PASS:";
    private const int Iterations = 600_000;
    private static readonly byte[] Aad = "LAOK1-officer"u8.ToArray();

    public static bool IsOfficerKey(string fileText) => fileText.TrimStart().StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>A new officer key: (file text to write on the officer's medium, public key SubjectPublicKeyInfo base64).</summary>
    public static (string FileText, string PublicKey) Generate(ReadOnlySpan<char> passphrase)
    {
        if (passphrase.Length < 8) throw new ArgumentException("Parola cheii de ofițer trebuie să aibă cel puțin 8 caractere.");
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pkcs8 = k.ExportPkcs8PrivateKey();
        try { return (Protect(pkcs8, passphrase), Convert.ToBase64String(k.ExportSubjectPublicKeyInfo())); }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    private static string Protect(byte[] pkcs8, ReadOnlySpan<char> passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Derive(passphrase, salt);
        try
        {
            var cipher = new byte[pkcs8.Length]; var tag = new byte[16];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, pkcs8, cipher, tag, Aad);
            return Prefix + Convert.ToBase64String([.. salt, .. nonce, .. tag, .. cipher]);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] Derive(ReadOnlySpan<char> passphrase, byte[] salt)
    {
        var pass = new byte[Encoding.UTF8.GetMaxByteCount(passphrase.Length)];
        try
        {
            int n = Encoding.UTF8.GetBytes(passphrase, pass);
            return Rfc2898DeriveBytes.Pbkdf2(pass.AsSpan(0, n), salt, Iterations, HashAlgorithmName.SHA256, 32);
        }
        finally { CryptographicOperations.ZeroMemory(pass); }
    }

    /// <summary>
    /// Opens the officer key for the duration of one signing. The decrypted PKCS#8 bytes and the derived AES key are zeroed before returning; the caller
    /// disposes the returned key right after signing. Throws <see cref="CryptographicException"/> for a wrong passphrase, a damaged file or a key that is not an officer key.
    /// </summary>
    public static ECDsa Load(string fileText, ReadOnlySpan<char> passphrase)
    {
        var t = fileText.Trim();
        if (!t.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Fișierul nu este o cheie de ofițer de securitate (creați-o cu LogAnalyzer.KeyGen officer-keygen).");
        if (passphrase.IsEmpty) throw new CryptographicException("Parola cheii de ofițer lipsește.");
        byte[] raw;
        try { raw = Convert.FromBase64String(t[Prefix.Length..]); }
        catch (FormatException ex) { throw new CryptographicException("Fișierul cheii de ofițer este deteriorat: " + ex.Message); }
        if (raw.Length < 16 + 12 + 16 + 1) throw new CryptographicException("Fișierul cheii de ofițer este deteriorat.");
        var key = Derive(passphrase, raw[..16]);
        var plain = new byte[raw.Length - 44];
        try
        {
            try { using var aes = new AesGcm(key, 16); aes.Decrypt(raw[16..28], raw[44..], raw[28..44], plain, Aad); }
            catch (AuthenticationTagMismatchException) { throw new CryptographicException("Parola cheii de ofițer este greșită sau fișierul a fost modificat."); }
            var ec = ECDsa.Create();
            try
            {
                ec.ImportPkcs8PrivateKey(plain, out _);
                if (ec.KeySize != 256) throw new CryptographicException("Cheia de ofițer trebuie să fie ECDSA P-256.");
                return ec;
            }
            catch { ec.Dispose(); throw; }
        }
        finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(raw); }
    }

    public static string PublicKeyOf(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>The fingerprint printed on paper and pinned for verification: first 128 bits of SHA-256(SubjectPublicKeyInfo), as 8 groups of 4 hex characters.</summary>
    public static string Fingerprint(string publicKeyBase64)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyBase64.Trim())))[..32];
        return string.Join("-", Enumerable.Range(0, 8).Select(i => hex.Substring(i * 4, 4)));
    }

    /// <summary>Upper-case hex without separators; empty when the text is not 32 hex characters.</summary>
    public static string Normalize(string? fingerprint)
    {
        var s = new string((fingerprint ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-' && c != ':').Select(char.ToUpperInvariant).ToArray());
        return s.Length == 32 && s.All(Uri.IsHexDigit) ? s : "";
    }

    public static bool Same(string? a, string? b) => Normalize(a) is { Length: > 0 } x && x == Normalize(b);
}

/// <summary>The detached officer signature: "&lt;report&gt;.pdf.officer.sig" (JSON).</summary>
public sealed class OfficerSignatureFile
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = OfficerSignature.Schema;
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("officer")] public string Officer { get; set; } = "";
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("public_key")] public string PublicKey { get; set; } = "";
    [JsonPropertyName("anchor_code")] public string AnchorCode { get; set; } = "";
    [JsonPropertyName("signed_utc")] public DateTimeOffset SignedUtc { get; set; }
    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = "ECDSA-P256-SHA256";
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
}

/// <summary>What the official report prints in its signature section (filled in before the PDF is signed, so the signature covers it).</summary>
public sealed record OfficerSignatureBlock(string Officer, string Fingerprint, DateTimeOffset SignedUtc);

/// <summary>
/// Signature of the official control report by the security officer. Verification is against PINNED fingerprints (from the signed paper or the
/// registered officers), never against the key carried in the signature file: a key made by anyone else gives UNKNOWN KEY.
/// </summary>
public static class OfficerSignature
{
    public const string Schema = "loganalyzer.officer-signature/1";
    public const string Extension = ".officer.sig";
    public const string UnsignedLabel = "NESEMNAT DE OFIȚERUL DE SECURITATE — nu este raport oficial";

    public static string SigPath(string reportPath) => reportPath + Extension;

    private static byte[] Payload(OfficerSignatureFile s) => Encoding.UTF8.GetBytes(string.Join("\n",
        Schema, s.File, s.Sha256.ToUpperInvariant(), s.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Officer, s.Fingerprint, s.PublicKey,
        s.AnchorCode, s.SignedUtc.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture), s.Algorithm));

    public static OfficerSignatureFile Sign(string reportPath, ECDsa key, string officer, string anchorCode, DateTimeOffset signedUtc)
    {
        var pub = OfficerKey.PublicKeyOf(key);
        var s = new OfficerSignatureFile
        {
            File = Path.GetFileName(reportPath), Sha256 = ReportSignature.Sha256File(reportPath), Size = new FileInfo(reportPath).Length, Officer = officer.Trim(),
            Fingerprint = OfficerKey.Fingerprint(pub), PublicKey = pub, AnchorCode = anchorCode, SignedUtc = signedUtc.ToUniversalTime(),
        };
        s.Signature = Convert.ToBase64String(key.SignData(Payload(s), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        File.WriteAllText(SigPath(reportPath), JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return s;
    }

    /// <summary>VALID only when the signing key's fingerprint is one of <paramref name="pinnedFingerprints"/>, the signature verifies and the PDF is unchanged.</summary>
    public static ReportSignatureResult Verify(string reportPath, IEnumerable<string> pinnedFingerprints, string? sigPath = null)
    {
        sigPath ??= SigPath(reportPath);
        var pinned = pinnedFingerprints.Select(OfficerKey.Normalize).Where(x => x.Length > 0).ToHashSet();
        string actual = "";
        OfficerSignatureFile? s;
        try
        {
            if (!File.Exists(reportPath)) return new(ReportSignatureStatus.Missing, "Raportul nu există: " + Path.GetFileName(reportPath), null, "");
            actual = ReportSignature.Sha256File(reportPath);
            if (!File.Exists(sigPath)) return new(ReportSignatureStatus.Missing, $"{OfficerSignature.UnsignedLabel}: lipsește semnătura ofițerului ({Path.GetFileName(sigPath)}).", null, actual);
            try { s = JsonSerializer.Deserialize<OfficerSignatureFile>(File.ReadAllText(sigPath)); }
            catch (JsonException) { s = null; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(ReportSignatureStatus.Invalid, "Fișierul nu poate fi citit (" + ex.Message + "): verificarea nu s-a putut face.", null, actual);
        }
        if (s is not null) { s.Schema ??= ""; s.File ??= ""; s.Sha256 ??= ""; s.Officer ??= ""; s.Fingerprint ??= ""; s.PublicKey ??= ""; s.AnchorCode ??= ""; s.Algorithm ??= ""; s.Signature ??= ""; }
        if (s is null || s.Schema != Schema || s.Signature.Length == 0 || s.PublicKey.Length == 0 || s.Sha256.Length == 0)
            return new(ReportSignatureStatus.Invalid, "Fișierul de semnătură al ofițerului nu poate fi citit (format necunoscut).", null, actual);
        string realFp;
        try { realFp = OfficerKey.Fingerprint(s.PublicKey); }
        catch (FormatException) { return new(ReportSignatureStatus.Invalid, "Cheia publică din semnătură este deteriorată.", null, actual); }
        if (pinned.Count == 0)
            return new(ReportSignatureStatus.UnknownKey, "Nu s-a dat nicio amprentă de ofițer cu care să se compare (de pe procesul-verbal semnat sau din lista „Ofițeri de securitate”). Semnătura nu dovedește nimic fără ea.", null, actual);
        if (!pinned.Contains(OfficerKey.Normalize(realFp)))
            return new(ReportSignatureStatus.UnknownKey, $"CHEIE NECUNOSCUTĂ — raportul este semnat cu cheia cu amprenta {realFp}, care NU este amprenta ofițerului de securitate dată pentru comparare. Raportul nu este oficial.", null, actual);
        bool ok;
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(s.PublicKey), out _);
            ok = OfficerKey.Same(s.Fingerprint, realFp) &&
                 ec.VerifyData(Payload(s), Convert.FromBase64String(s.Signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { ok = false; }
        if (!ok) return new(ReportSignatureStatus.Modified, "SEMNĂTURĂ INVALIDĂ — fișierul de semnătură al ofițerului a fost modificat sau nu aparține acestui raport.", null, actual);
        if (!string.Equals(actual, s.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(ReportSignatureStatus.Modified, $"RAPORT MODIFICAT după semnarea de către ofițer — SHA-256 actual {actual} diferă de cel semnat {s.Sha256}.", null, actual);
        return new(ReportSignatureStatus.Valid,
            $"SEMNĂTURĂ VALIDĂ A OFIȚERULUI DE SECURITATE — „{s.Officer}”, amprenta {realFp}, la {s.SignedUtc:yyyy-MM-dd HH:mm} UTC" +
            (s.AnchorCode.Length > 0 ? $", cod de ancorare {s.AnchorCode}" : "") + $". Raportul nu a fost modificat de la semnare (SHA-256 {actual}).", null, actual);
    }
}

/// <summary>
/// The "Ofițeri de securitate" list of the procedure profile. The first officer is registered on trust and that trust rests on the fingerprint printed on the
/// proces-verbal; every later officer must carry an approval signed by an officer already trusted. A row that is not reachable through valid approvals
/// from the first one is not trusted, whatever the profile file says.
/// </summary>
public static class OfficerRegistry
{
    public const string BasisFirst = "primul ofițer (amprentă pe procesul-verbal)", BasisApproved = "aprobat de un ofițer înregistrat";

    private static byte[] ApprovalPayload(string name, string fingerprint, string publicKey, DateTimeOffset registeredUtc) => Encoding.UTF8.GetBytes(string.Join("\n",
        "LogAnalyzer-officer-registration-v1", name.Trim(), OfficerKey.Normalize(fingerprint), publicKey.Trim(),
        registeredUtc.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Registers the first officer; refused when any officer is already listed (the next ones need an approval).</summary>
    public static SecurityOfficerRow RegisterFirst(ProcedureProfile p, string name, string publicKey, DateTimeOffset nowUtc)
    {
        p.SecurityOfficers ??= [];
        if (p.SecurityOfficers.Count > 0) throw new InvalidOperationException("Există deja ofițeri de securitate înregistrați: un ofițer nou se înregistrează doar cu aprobarea (cheia și parola) unui ofițer existent.");
        Require(name);
        var row = new SecurityOfficerRow { Name = name.Trim(), PublicKey = publicKey.Trim(), Fingerprint = OfficerKey.Fingerprint(publicKey), RegisteredUtc = nowUtc.ToUniversalTime(), Basis = BasisFirst };
        p.SecurityOfficers.Add(row);
        return row;
    }

    /// <summary>Registers another officer with the approval signature of an already trusted officer (<paramref name="approver"/>, opened for this call only).</summary>
    public static SecurityOfficerRow RegisterApproved(ProcedureProfile p, string name, string publicKey, ECDsa approver, DateTimeOffset nowUtc)
    {
        p.SecurityOfficers ??= [];
        Require(name);
        var approverFp = OfficerKey.Fingerprint(OfficerKey.PublicKeyOf(approver));
        if (!Trusted(p, out _).Any(r => OfficerKey.Same(r.Fingerprint, approverFp)))
            throw new InvalidOperationException($"Cheia care aprobă (amprenta {approverFp}) nu aparține unui ofițer de securitate înregistrat.");
        var fp = OfficerKey.Fingerprint(publicKey);
        if (p.SecurityOfficers.Any(r => OfficerKey.Same(r.Fingerprint, fp))) throw new InvalidOperationException($"Ofițerul cu amprenta {fp} este deja înregistrat.");
        var now = nowUtc.ToUniversalTime();
        var row = new SecurityOfficerRow
        {
            Name = name.Trim(), PublicKey = publicKey.Trim(), Fingerprint = fp, RegisteredUtc = now, Basis = BasisApproved, ApprovedBy = approverFp,
            Approval = Convert.ToBase64String(approver.SignData(ApprovalPayload(name, fp, publicKey, now), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
        };
        p.SecurityOfficers.Add(row);
        return row;
    }

    private static void Require(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Numele ofițerului de securitate este obligatoriu.");
    }

    /// <summary>The rows that are trusted: the first row (registered as first) and every row with a valid approval by a row trusted before it. Problems name the others.</summary>
    public static IReadOnlyList<SecurityOfficerRow> Trusted(ProcedureProfile? p, out List<string> problems)
    {
        problems = [];
        var trusted = new List<SecurityOfficerRow>();
        var rows = p?.SecurityOfficers ?? [];
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string realFp;
            try { realFp = OfficerKey.Fingerprint(r.PublicKey ?? ""); }
            catch (FormatException) { problems.Add($"„{r.Name}”: cheia publică este deteriorată."); continue; }
            if (!OfficerKey.Same(realFp, r.Fingerprint)) { problems.Add($"„{r.Name}”: amprenta scrisă nu corespunde cheii publice."); continue; }
            if (i == 0 && r.Basis == BasisFirst) { trusted.Add(r); continue; }
            var approver = trusted.FirstOrDefault(t => OfficerKey.Same(t.Fingerprint, r.ApprovedBy));
            bool ok = false;
            if (approver is not null && r.Basis == BasisApproved)
            {
                try
                {
                    using var ec = ECDsa.Create();
                    ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(approver.PublicKey ?? ""), out _);
                    ok = ec.VerifyData(ApprovalPayload(r.Name, r.Fingerprint, r.PublicKey!, r.RegisteredUtc), Convert.FromBase64String(r.Approval ?? ""),
                                       HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                }
                catch (Exception ex) when (ex is FormatException or CryptographicException) { ok = false; }
            }
            if (ok) trusted.Add(r);
            else problems.Add($"„{r.Name}” (amprenta {r.Fingerprint}) nu are o aprobare validă a unui ofițer înregistrat: nu este de încredere.");
        }
        return trusted;
    }

    public static bool IsTrusted(ProcedureProfile? p, string fingerprint) => Trusted(p, out _).Any(r => OfficerKey.Same(r.Fingerprint, fingerprint));
}
