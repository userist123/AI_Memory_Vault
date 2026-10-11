using System;
using System.IO;
using System.Linq;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.KeyGen;

/// <summary>
/// F3 / F4 command line: report-keygen, report-sign, verify-report and verify-archive. Exit codes: 0 VALID / created, 1 usage or I/O error,
/// 2 MODIFIED (or archive not verified), 3 UNKNOWN KEY, 4 missing or unreadable signature.
/// </summary>
public static class ReportCli
{
    /// <summary>report-keygen &lt;private-key-file&gt; [&lt;public-key-file&gt;]: the operator's ECDSA P-256 report signing key. Never overwrites; never inside a repository.</summary>
    public static int Keygen(string[] args, TextWriter o)
    {
        if (args.Length is < 2 or > 3) { o.WriteLine("Utilizare: report-keygen <fișier-cheie-privată> [<fișier-cheie-publică>]"); return 1; }
        var priv = Path.GetFullPath(args[1]);
        if (File.Exists(priv)) { o.WriteLine("[-] Fișierul cheii există deja; nu se suprascrie."); return 1; }
        if (InsideGitRepository(priv)) { o.WriteLine("[-] Cheia privată nu se scrie într-un depozit git. Alegeți un mediu înregistrat, în afara depozitului."); return 1; }
        var (privateKey, publicKey) = ReportSignature.GenerateKey();
        Directory.CreateDirectory(Path.GetDirectoryName(priv)!);
        File.WriteAllText(priv, privateKey);
        if (args.Length == 3) File.WriteAllText(args[2], publicKey);
        o.WriteLine("[+] Cheia privată de semnare a rapoartelor a fost scrisă în " + priv + " (păstrați-o pe un mediu înregistrat; nu o copiați în depozit sau în caz).");
        o.WriteLine("[+] Cheia publică (se configurează în aplicație: Setări semnătură rapoarte → chei de încredere):");
        o.WriteLine(publicKey);
        o.WriteLine("[+] Amprenta cheii: " + ReportSignature.KeyId(publicKey));
        return 0;
    }

    /// <summary>report-sign &lt;private-key-file&gt; &lt;report.pdf&gt; &lt;signer&gt;: writes &lt;report.pdf&gt;.sig.</summary>
    public static int Sign(string[] args, TextWriter o)
    {
        if (args.Length != 4) { o.WriteLine("Utilizare: report-sign <fișier-cheie-privată> <raport.pdf> <semnatar>"); return 1; }
        try
        {
            using var key = ReportSignature.LoadPrivateKey(File.ReadAllText(args[1]));
            var s = ReportSignature.Sign(args[2], key, args[3]);
            o.WriteLine($"[+] Semnat: {ReportSignature.SigPath(args[2])} (SHA-256 {s.Sha256}, cheia {s.KeyId})");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            o.WriteLine("[-] " + ex.Message);
            return 1;
        }
    }

    /// <summary>verify-report &lt;report.pdf&gt; &lt;public key base64 or file&gt;...: prints VALID / MODIFIED / UNKNOWN KEY and the reason.</summary>
    public static int Verify(string[] args, TextWriter o)
    {
        if (args.Length < 3) { o.WriteLine("Utilizare: verify-report <raport.pdf> <cheie-publică-base64 | fișier-cheie-publică>..."); return 1; }
        var keys = args.Skip(2).Select(k => File.Exists(k) ? File.ReadAllText(k).Trim() : k.Trim()).ToList();
        var r = ReportSignature.Verify(args[1], keys);
        o.WriteLine(r.Word);
        o.WriteLine(r.Message);
        return r.Status switch
        {
            ReportSignatureStatus.Valid => 0, ReportSignatureStatus.Modified => 2, ReportSignatureStatus.UnknownKey => 3, _ => 4,
        };
    }

    /// <summary>verify-archive &lt;case.zip&gt;: re-hashes every file against manifest.json without importing anything.</summary>
    public static int VerifyArchive(string[] args, TextWriter o)
    {
        if (args.Length != 2) { o.WriteLine("Utilizare: verify-archive <arhivă-caz.zip>"); return 1; }
        var r = LogAnalyzer.Dfir.Case.CaseArchive.Verify(args[1]);
        o.WriteLine(r.Ok ? "VERIFIED" : "NOT VERIFIED");
        o.WriteLine(r.Summary);
        foreach (var p in r.Problems.Take(50)) o.WriteLine(" - " + p);
        return r.Ok ? 0 : 2;
    }

    private static bool InsideGitRepository(string path)
    {
        for (var d = new DirectoryInfo(Path.GetDirectoryName(path)!); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git"))) return true;
        return false;
    }
}
