using System;
using System.Globalization;
using System.IO;
using System.Text;
using LogAnalyzer.Core.Services;

namespace LogAnalyzer.KeyGen;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Offline detection data packs (docs/dfir/ANALYSIS_2026-10-11_APPLICATION_UPDATES.md): run on the owner's offline machine only.
        if (args.Length >= 1 && args[0] == "datapack-keygen") { DataPackKeygen(args); return; }
        if (args.Length >= 1 && args[0] == "datapack-sign") { DataPackSign(args); return; }
        // F3: report signing key of the operator / organisation, and the independent verifier of a signed report.
        if (args.Length >= 1 && args[0] == "report-keygen") { Environment.ExitCode = ReportCli.Keygen(args, Console.Out); return; }
        if (args.Length >= 1 && args[0] == "report-key-protect") { Environment.ExitCode = ReportCli.Protect(args, Console.Out); return; }
        if (args.Length >= 1 && args[0] == "report-sign") { Environment.ExitCode = ReportCli.Sign(args, Console.Out); return; }
        if (args.Length >= 1 && args[0] == "officer-keygen") { Environment.ExitCode = ReportCli.OfficerKeygen(args, Console.Out); return; }
        if (args.Length >= 1 && args[0] == "verify-report") { Environment.ExitCode = ReportCli.Verify(args, Console.Out); return; }
        if (args.Length >= 1 && args[0] == "verify-archive") { Environment.ExitCode = ReportCli.VerifyArchive(args, Console.Out); return; }
        // Security audit #3: licences are ECDSA signatures made with an offline key (the former salted-hash scheme is gone).
        if (args.Length >= 1 && args[0] == "license-keygen") { Environment.ExitCode = LicenseCli.Keygen(args, Console.Out); return; }
        if (args.Length >= 1 && args[0] == "license-sign") { Environment.ExitCode = LicenseCli.Sign(args, Console.Out); return; }
        if (args.Length >= 1 && args[0] == "hardware-id")
        {
            try { Console.WriteLine(new LicenseService().GetHardwareId()); }
            catch (HardwareIdUnavailableException ex) { Console.WriteLine("[-] " + ex.Message); Environment.ExitCode = 2; }
            return;
        }

        ShowUsage();
        if (!(args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))) Environment.ExitCode = 1;
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Utilizare:");
        Console.WriteLine("Licențe (pe mașina offline a celui care emite licențele):");
        Console.WriteLine("  LogAnalyzer.KeyGen license-keygen <fișier-cheie-privată> [<fișier-cheie-publică>] [--passphrase | --plain]");
        Console.WriteLine("  LogAnalyzer.KeyGen license-sign <fișier-cheie-privată> --hardware-id <16 hex> --expires <AAAA-LL-ZZ> [--id <id>] [--out <license.lic>]");
        Console.WriteLine("  LogAnalyzer.KeyGen hardware-id");
        Console.WriteLine("  Cheile din schema veche (\"CHEIE|AAAA-LL-ZZ\") nu mai sunt acceptate de aplicație.");
        Console.WriteLine();
        Console.WriteLine("Rapoarte semnate digital:");
        Console.WriteLine("  LogAnalyzer.KeyGen report-keygen <fișier-cheie-privată> [<fișier-cheie-publică>] [--passphrase | --plain]");
        Console.WriteLine("  LogAnalyzer.KeyGen report-key-protect <fișier-cheie-privată> [--passphrase]   (criptează o cheie veche necriptată)");
        Console.WriteLine("  LogAnalyzer.KeyGen report-sign <fișier-cheie-privată> <raport.pdf> <semnatar>");
        Console.WriteLine("  LogAnalyzer.KeyGen verify-report <raport.pdf> <cheie-publică-base64 | fișier-cheie-publică>...");
        Console.WriteLine("  LogAnalyzer.KeyGen verify-report <raport.pdf> --officer-fingerprint <amprenta de pe procesul-verbal>");
        Console.WriteLine();
        Console.WriteLine("Ofițerul de securitate (cheia stă pe mediul ofițerului, nu pe stație):");
        Console.WriteLine("  LogAnalyzer.KeyGen officer-keygen <fișier-cheie-privată> [<fișier-cheie-publică>]");
        Console.WriteLine("  LogAnalyzer.KeyGen verify-archive <arhivă-caz.zip> [<sha256-manifest> | <cheie-publică>]...");
        Console.WriteLine();
        Console.WriteLine("Pachete de date de detecție:");
        Console.WriteLine("  LogAnalyzer.KeyGen datapack-keygen <fișier-cheie-privată>");
        Console.WriteLine("  LogAnalyzer.KeyGen datapack-sign <fișier-cheie-privată> <id-pachet> <versiune> <ieșire.zip> <fișier.json>...");
    }

    /// <summary>datapack-keygen &lt;private-key-file&gt;: creates the ECDSA P-256 signing key (PKCS#8, keep offline) and prints the public key to compile into the application.</summary>
    private static void DataPackKeygen(string[] args)
    {
        if (args.Length != 2) { Console.WriteLine("Utilizare: datapack-keygen <fișier-cheie-privată>"); return; }
        if (File.Exists(args[1])) { Console.WriteLine("[-] Fișierul cheii există deja; nu se suprascrie."); return; }
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        File.WriteAllText(args[1], Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
        Console.WriteLine("[+] Cheia privată a fost scrisă în " + args[1] + " (păstrați-o offline, pe un mediu înregistrat, sigilat).");
        Console.WriteLine("[+] Cheia publică (se trece în LogAnalyzer.App/Services/DataPackTrust.cs):");
        Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>datapack-sign &lt;private-key-file&gt; &lt;pack-id&gt; &lt;version&gt; &lt;out.zip&gt; &lt;file.json&gt;...: builds a signed pack from the given data files.</summary>
    private static void DataPackSign(string[] args)
    {
        if (args.Length < 6 || !long.TryParse(args[3], out var version)) { Console.WriteLine("Utilizare: datapack-sign <fișier-cheie-privată> <id-pachet> <versiune> <ieșire.zip> <fișier.json>..."); return; }
        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(args[1]).Trim()), out _);
        var files = args.Skip(5).ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f));
        var unknown = files.Keys.Where(k => !LogAnalyzer.Dfir.Analysis.DataPack.AllowedFiles.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0) { Console.WriteLine("[-] Fișiere neacceptate într-un pachet: " + string.Join(", ", unknown)); return; }
        LogAnalyzer.Dfir.Analysis.DataPack.Create(args[4], args[2], version, files, key);
        Console.WriteLine($"[+] Pachetul {args[2]} versiunea {version} a fost semnat: {args[4]} (SHA-256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[4])))})");
    }
}

