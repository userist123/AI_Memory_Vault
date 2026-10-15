using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.KeyGen;

/// <summary>
/// F3 / F4 command line: report-keygen, report-sign, verify-report and verify-archive. Exit codes: 0 VALID / created, 1 usage or I/O error,
/// 2 MODIFIED (or archive not verified), 3 UNKNOWN KEY, 4 missing or unreadable signature, 5 archive intact but its manifest is not authenticated.
/// </summary>
public static class ReportCli
{
    public const string PassphraseEnvironmentVariable = "LOGANALYZER_KEY_PASSPHRASE";

    /// <summary>
    /// report-keygen &lt;private-key-file&gt; [&lt;public-key-file&gt;] [--passphrase] [--plain]: the operator's ECDSA P-256 report signing key. Never overwrites; never inside a repository.
    /// By default the private key is encrypted for the current Windows user (DPAPI); <c>--passphrase</c> encrypts it with a passphrase instead (taken from
    /// LOGANALYZER_KEY_PASSPHRASE or the first line of <paramref name="input"/>), so it can be moved to another machine; <c>--plain</c> writes the bare key (not recommended).
    /// The file gets an ACL granting access to the current user only.
    /// </summary>
    public static int Keygen(string[] args, TextWriter o, TextReader? input = null)
    {
        var flags = args.Skip(1).Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToList();
        var pos = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        bool usePass = flags.Contains("--passphrase"), plain = flags.Contains("--plain");
        if (pos.Count is < 1 or > 2 || flags.Except(["--passphrase", "--plain"]).Any() || usePass && plain)
        {
            o.WriteLine("Utilizare: report-keygen <fișier-cheie-privată> [<fișier-cheie-publică>] [--passphrase | --plain]");
            return 1;
        }
        var priv = Path.GetFullPath(pos[0]);
        if (File.Exists(priv)) { o.WriteLine("[-] Fișierul cheii există deja; nu se suprascrie."); return 1; }
        if (InsideGitRepository(priv)) { o.WriteLine("[-] Cheia privată nu se scrie într-un depozit git. Alegeți un mediu înregistrat, în afara depozitului."); return 1; }
        string? pass = null;
        if (usePass)
        {
            pass = Environment.GetEnvironmentVariable(PassphraseEnvironmentVariable);
            if (string.IsNullOrEmpty(pass))
            {
                o.WriteLine("Parola cheii (nu se afișează pe ecran și nu se scrie în jurnal):");
                pass = ReadSecret(input);
                if (string.IsNullOrEmpty(pass)) { o.WriteLine("[-] Parola nu poate fi goală."); return 1; }
                o.WriteLine("Repetați parola:");
                if (!string.Equals(pass, ReadSecret(input), StringComparison.Ordinal)) { o.WriteLine("[-] Cele două parole nu coincid; nu s-a scris nicio cheie."); return 1; }
            }
            if (string.IsNullOrEmpty(pass)) { o.WriteLine("[-] Parola nu poate fi goală."); return 1; }
        }
        try
        {
            var (privateKey, publicKey) = ReportSignature.GenerateKey();
            var stored = plain ? privateKey : usePass ? ReportKeyProtection.ProtectWithPassphrase(privateKey, pass!) : ReportKeyProtection.ProtectForCurrentUser(privateKey);
            Directory.CreateDirectory(Path.GetDirectoryName(priv)!);
            WriteOwnerOnly(priv, stored);
            if (pos.Count == 2) File.WriteAllText(pos[1], publicKey);
            o.WriteLine("[+] Cheia privată de semnare a rapoartelor a fost scrisă în " + priv + " (" + (plain ? "NECRIPTATĂ" : usePass ? "criptată cu parolă" : "criptată pentru utilizatorul Windows curent, DPAPI") + "; acces doar pentru utilizatorul curent).");
            o.WriteLine("    Păstrați-o pe un mediu înregistrat; nu o copiați în depozit sau în caz.");
            o.WriteLine("[+] Cheia publică (se configurează în aplicație: Setări semnătură rapoarte → chei de încredere):");
            o.WriteLine(publicKey);
            o.WriteLine("[+] Amprenta cheii: " + ReportSignature.KeyId(publicKey));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            o.WriteLine("[-] " + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// report-key-protect &lt;key-file&gt; [--passphrase] (security audit #20): rewrites a legacy bare PKCS#8 key file as a protected one holding the
    /// same key: DPAPI for the current Windows user by default, or a passphrase (LOGANALYZER_KEY_PASSPHRASE or typed twice). The protected copy is
    /// written next to the original with an owner-only ACL, then replaces it. A key that is already protected is left unchanged (exit 0);
    /// a file that is not a P-256 key is left unchanged (exit 1). The plaintext may still be recoverable from the disk: retire the medium if that matters.
    /// </summary>
    public static int Protect(string[] args, TextWriter o, TextReader? input = null)
    {
        var flags = args.Skip(1).Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToList();
        var pos = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        bool usePass = flags.Contains("--passphrase");
        if (pos.Count != 1 || flags.Except(["--passphrase"]).Any())
        {
            o.WriteLine("Utilizare: report-key-protect <fișier-cheie-privată> [--passphrase]");
            return 1;
        }
        var file = Path.GetFullPath(pos[0]);
        var tmp = file + ".protect-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var text = File.ReadAllText(file);
            if (ReportKeyProtection.IsProtected(text))
            {
                o.WriteLine("[=] Cheia este deja protejată (" + (ReportKeyProtection.NeedsPassphrase(text) ? "parolă" : "DPAPI") + "); nu s-a schimbat nimic.");
                return 0;
            }
            string pub;
            using (var key = ReportSignature.LoadPrivateKey(text)) pub = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            string? pass = null;
            if (usePass)
            {
                pass = Environment.GetEnvironmentVariable(PassphraseEnvironmentVariable);
                if (string.IsNullOrEmpty(pass))
                {
                    o.WriteLine("Parola cheii (nu se afișează pe ecran și nu se scrie în jurnal):");
                    pass = ReadSecret(input);
                    if (string.IsNullOrEmpty(pass)) { o.WriteLine("[-] Parola nu poate fi goală."); return 1; }
                    o.WriteLine("Repetați parola:");
                    if (!string.Equals(pass, ReadSecret(input), StringComparison.Ordinal)) { o.WriteLine("[-] Cele două parole nu coincid; cheia nu a fost schimbată."); return 1; }
                }
            }
            var stored = usePass ? ReportKeyProtection.ProtectWithPassphrase(text.Trim(), pass!) : ReportKeyProtection.ProtectForCurrentUser(text.Trim());
            WriteOwnerOnly(tmp, stored);
            File.Move(tmp, file, overwrite: true);
            o.WriteLine("[+] Cheia din " + file + " este acum " + (usePass ? "criptată cu parolă" : "criptată pentru utilizatorul Windows curent (DPAPI)") +
                        "; aceeași cheie, amprenta " + ReportSignature.KeyId(pub) + ". Semnăturile existente rămân valide.");
            o.WriteLine("    Copia necriptată poate rămâne recuperabilă de pe disc: dacă mediul a fost partajat, generați o cheie nouă și retrageți-o pe aceasta.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException) { }
            o.WriteLine("[-] Cheia nu a fost schimbată: " + ex.Message);
            return 1;
        }
    }

    /// <summary>Creates the file with an ACL that grants FullControl to the current user only (inheritance removed), then writes the content.</summary>
    internal static void WriteOwnerOnly(string path, string content)
    {
        var me = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Utilizatorul Windows curent nu poate fi determinat.");
        var sec = new FileSecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
        using var fs = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadAttributes, FileShare.None, 4096, FileOptions.None, sec);
        using var w = new StreamWriter(fs);
        w.Write(content);
    }

    /// <summary>report-sign &lt;private-key-file&gt; &lt;report.pdf&gt; &lt;signer&gt;: writes &lt;report.pdf&gt;.sig.</summary>
    public static int Sign(string[] args, TextWriter o, TextReader? input = null)
    {
        if (args.Length != 4) { o.WriteLine("Utilizare: report-sign <fișier-cheie-privată> <raport.pdf> <semnatar>"); return 1; }
        try
        {
            var text = File.ReadAllText(args[1]);
            string? pass = null;
            if (ReportKeyProtection.NeedsPassphrase(text))
            {
                pass = Environment.GetEnvironmentVariable(PassphraseEnvironmentVariable);
                if (string.IsNullOrEmpty(pass)) { o.WriteLine("Parola cheii (nu se afișează pe ecran):"); pass = ReadSecret(input); }
            }
            using var key = ReportSignature.LoadPrivateKey(text, pass);
            var s = ReportSignature.Sign(args[2], key, args[3]);
            o.WriteLine($"[+] Semnat: {ReportSignature.SigPath(args[2])} (SHA-256 {s.Sha256}, cheia {s.KeyId})");
            if (ReportKeyProtection.IsLegacyBareKey(text)) o.WriteLine("[!] " + ReportKeyProtection.LegacyBareKeyWarning);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException)
        {
            o.WriteLine("[-] " + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// verify-report &lt;signed.pdf&gt; --officer-fingerprint &lt;fp&gt; --report-sha256 &lt;sha&gt;... [--ca &lt;file&gt;]... [--crl &lt;file&gt;]...: the security officer's qualified
    /// signature inside the PDF (made in the application or in Adobe), checked exactly as the application does: fingerprint from the proces-verbal, the SHA-256 of
    /// the generated report (also on the proces-verbal), chain to the given qualified CA certificates and revocation in the given CRLs.
    /// Exit codes: 0 OFICIAL, 5 VALID, DAR NEOFICIAL, 2 INVALID, 1 usage or I/O error.
    /// </summary>
    public static int VerifyOfficerPdf(string[] args, TextWriter o)
    {
        var fps = new System.Collections.Generic.List<string>(); var shas = new System.Collections.Generic.List<string>();
        var cas = new System.Collections.Generic.List<string>(); var crls = new System.Collections.Generic.List<string>();
        string? pdf = null; bool bad = false;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--officer-fingerprint" when i + 1 < args.Length: fps.Add(args[++i]); break;
                case "--report-sha256" when i + 1 < args.Length: shas.Add(args[++i].Trim().ToUpperInvariant()); break;
                case "--ca" when i + 1 < args.Length: cas.Add(args[++i]); break;
                case "--crl" when i + 1 < args.Length: crls.Add(args[++i]); break;
                default: if (pdf is null && !args[i].StartsWith("--", StringComparison.Ordinal)) pdf = args[i]; else bad = true; break;
            }
        }
        if (bad || pdf is null || fps.Count != 1 || OfficerFingerprint.Normalize(fps[0]).Length == 0 || shas.Count == 0 || shas.Any(s => s.Length != 64 || !s.All(Uri.IsHexDigit)))
        {
            o.WriteLine("Utilizare: verify-report <raport-semnat.pdf> --officer-fingerprint <amprenta certificatului, de pe procesul-verbal> --report-sha256 <amprenta raportului> [--ca <CA calificat>]... [--crl <CRL>]...");
            return 1;
        }
        try
        {
            var trust = new QualifiedSignatureTrust(
                cas.Select(f => LogAnalyzer.Dfir.Auth.CertificateTrust.LoadCertificate(File.ReadAllBytes(f))).ToList(),
                crls.Select(f => LogAnalyzer.Dfir.Auth.ParsedCrl.Parse(File.ReadAllBytes(f))).ToList());
            // verifying away from the station: the officer is the one whose fingerprint is on the paper
            var bytes = File.ReadAllBytes(pdf);
            var signer = PdfSignatureCheck.Check(bytes, LogAnalyzer.UI.Services.SignedCmsVerifier.Instance).Signer;
            var paperOfficer = signer is not null && OfficerFingerprint.Same(OfficerFingerprint.Of(signer), fps[0])
                ? new[] { new SecurityOfficerRow { Name = OfficerFingerprint.CommonName(signer), Fingerprint = OfficerFingerprint.Of(signer), Certificate = Convert.ToBase64String(signer.RawData), Basis = OfficerRegistry.BasisFirst } }
                : [];
            var v = OfficialReportVerifier.Verify(bytes, LogAnalyzer.UI.Services.SignedCmsVerifier.Instance, shas, paperOfficer, fps[0], trust);
            o.WriteLine(v.ToPlainText());
            return v.Verdict switch { OfficialVerdict.Oficial => 0, OfficialVerdict.ValidNeoficial => 5, _ => 2 };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException)
        {
            o.WriteLine("[-] Fișierele nu au putut fi citite: " + ex.Message);
            return 1;
        }
    }

    /// <summary>verify-report &lt;report.pdf&gt; &lt;public key base64 or file&gt;...: prints VALID / MODIFIED / UNKNOWN KEY and the reason.</summary>
    public static int Verify(string[] args, TextWriter o)
    {
        if (args.Contains("--officer-fingerprint") || args.Contains("--report-sha256")) return VerifyOfficerPdf(args, o);
        if (args.Length < 3) { o.WriteLine("Utilizare: verify-report <raport.pdf> <cheie-publică-base64 | fișier-cheie-publică>..."); return 1; }
        try
        {
            var keys = args.Skip(2).Select(k => File.Exists(k) ? File.ReadAllText(k).Trim() : k.Trim()).ToList();
            var r = ReportSignature.Verify(args[1], keys);
            o.WriteLine(r.Word);
            o.WriteLine(r.Message);
            return r.Status switch
            {
                ReportSignatureStatus.Valid => 0, ReportSignatureStatus.Modified => 2, ReportSignatureStatus.UnknownKey => 3, _ => 4,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            o.WriteLine("INVALID");
            o.WriteLine("Verificarea nu s-a putut face: " + ex.Message);
            return 4;
        }
    }

    /// <summary>
    /// verify-archive &lt;case.zip&gt; [&lt;manifest-sha256 | public key base64 | public key file&gt;...]: re-hashes every file against manifest.json without importing anything.
    /// Prints VERIFIED only when the manifest is also authenticated (valid signature by a given public key, or the given manifest SHA-256); an intact archive nobody
    /// vouches for prints UNAUTHENTICATED (exit 5).
    /// </summary>
    public static int VerifyArchive(string[] args, TextWriter o)
    {
        if (args.Length < 2) { o.WriteLine("Utilizare: verify-archive <arhivă-caz.zip> [<sha256-manifest> | <cheie-publică-base64 | fișier>]..."); return 1; }
        try
        {
            string? expected = null; var keys = new System.Collections.Generic.List<string>();
            foreach (var a in args.Skip(2))
            {
                var t = File.Exists(a) ? File.ReadAllText(a).Trim() : a.Trim();
                if (t.Length == 64 && t.All(Uri.IsHexDigit))
                {
                    if (expected is not null && !expected.Equals(t, StringComparison.OrdinalIgnoreCase)) { o.WriteLine("Se acceptă o singură valoare SHA-256 așteptată pentru manifest; s-au dat mai multe valori diferite. Nu se verifică nimic."); return 1; }
                    expected = t;
                }
                else keys.Add(t);
            }
            var r = LogAnalyzer.Dfir.Case.CaseArchive.Verify(args[1], expected, keys);
            o.WriteLine(!r.Ok ? "NOT VERIFIED" : r.Authenticated ? "VERIFIED" : "UNAUTHENTICATED");
            o.WriteLine(r.Summary);
            foreach (var p in r.Problems.Take(50)) o.WriteLine(" - " + p);
            return !r.Ok ? 2 : r.Authenticated ? 0 : 5;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            o.WriteLine("NOT VERIFIED");
            o.WriteLine("Arhiva nu poate fi citită: " + ex.Message);
            return 2;
        }
    }

    /// <summary>One line of secret input. From a supplied reader (tests, piped input) it is read as is; from an interactive console the typed characters are not echoed.</summary>
    internal static string? ReadSecret(TextReader? input)
    {
        if (input is not null) return input.ReadLine();
        if (Console.IsInputRedirected) return Console.In.ReadLine();
        var sb = new System.Text.StringBuilder();
        for (var k = Console.ReadKey(intercept: true); k.Key != ConsoleKey.Enter; k = Console.ReadKey(intercept: true))
        {
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
            else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }

    internal static bool InsideGitRepository(string path)
    {
        for (var d = new DirectoryInfo(Path.GetDirectoryName(path)!); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git"))) return true;
        return false;
    }
}
