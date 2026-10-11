using System;
using System.Globalization;
using System.IO;
using System.Text;
using LogAnalyzer.Core.Services;

namespace LogAnalyzer.KeyGen;

public static class Program
{
    // Single source of truth: the same LicenseService the editions use to show the Hardware ID and verify keys.
    private static readonly LicenseService License = new();

    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Offline detection data packs (docs/dfir/ANALYSIS_2026-10-11_APPLICATION_UPDATES.md): run on the owner's offline machine only.
        if (args.Length >= 1 && args[0] == "datapack-keygen") { DataPackKeygen(args); return; }
        if (args.Length >= 1 && args[0] == "datapack-sign") { DataPackSign(args); return; }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("===================================================================");
        Console.WriteLine(" 🛡️ LOGANALYZER DFIR ENTERPRISE — KEY GENERATOR & LICENSE MANAGER");
        Console.WriteLine("===================================================================");
        Console.ResetColor();

        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            ShowUsage();
            return;
        }

        string hardwareId;
        string expiryInput;

        if (args.Length >= 2)
        {
            hardwareId = Clean(args[0]).ToUpperInvariant();
            expiryInput = Clean(args[1]);
        }
        else
        {
            var localHwId = GetLocalHardwareId();
            Console.WriteLine($"[+] Hardware ID detectat pe stația locală: {localHwId}");
            Console.Write($"Introduceți Hardware ID [Apăsați ENTER pentru cel local '{localHwId}']: ");
            var inputHw = Clean(Console.ReadLine());
            hardwareId = string.IsNullOrWhiteSpace(inputHw) ? localHwId : inputHw.ToUpperInvariant();

            Console.WriteLine();
            Console.WriteLine("Selectați perioada de valabilitate:");
            Console.WriteLine("  1. 1 An (Recomandat Standard)");
            Console.WriteLine("  2. 3 Ani (Enterprise Multi-Year)");
            Console.WriteLine("  3. 10 Ani (Long-Term Air-Gapped Station)");
            Console.WriteLine("  4. Dată Personalizată (Format: YYYY-MM-DD)");
            Console.Write("Alegere [1]: ");
            var choice = Clean(Console.ReadLine());

            expiryInput = choice switch
            {
                "2" => DateTime.UtcNow.AddYears(3).ToString("yyyy-MM-dd"),
                "3" => DateTime.UtcNow.AddYears(10).ToString("yyyy-MM-dd"),
                "4" => PromptCustomDate(),
                _ => DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd")
            };
        }

        if (hardwareId.Length < 8 || !hardwareId.All(Uri.IsHexDigit))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[-] Eroare: Hardware ID invalid (trebuie să conțină minim 8 caractere hexazecimale).");
            Console.ResetColor();
            Environment.ExitCode = 1;
            return;
        }

        if (!DateTime.TryParseExact(expiryInput.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiryDate))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[-] Eroare: Formatul datei trebuie să fie strict YYYY-MM-DD (ex: 2027-12-31).");
            Console.ResetColor();
            Environment.ExitCode = 1;
            return;
        }

        var fullLicenseString = License.BuildLicenseString(hardwareId, expiryDate);
        var key = fullLicenseString.Split('|')[0];
        if (!License.VerifyLicenseString(hardwareId, fullLicenseString, DateTime.UtcNow))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[-] Eroare: licența generată nu trece verificarea aplicației (dată expirată?).");
            Console.ResetColor();
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("===================================================================");
        Console.WriteLine(" ✅ LICENȚĂ GENERATĂ CU SUCCES:");
        Console.WriteLine("===================================================================");
        Console.WriteLine($" Target Hardware ID: {hardwareId}");
        Console.WriteLine($" Valabilitate până:  {expiryDate:yyyy-MM-dd}");
        Console.WriteLine($" Cheie Criptografică:{key}");
        Console.WriteLine($" Șir Licență:        {fullLicenseString}");
        Console.WriteLine("===================================================================");
        Console.ResetColor();

        Console.WriteLine();
        Console.Write("Doriți să salvați licența în fișierul 'license.lic'? [D/n]: ");
        var saveChoice = Clean(Console.ReadLine()).ToUpperInvariant();
        if (string.IsNullOrEmpty(saveChoice) || saveChoice == "D" || saveChoice == "Y" || saveChoice == "DA")
        {
            File.WriteAllText("license.lic", fullLicenseString, Encoding.UTF8);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[+] Fișierul '{Path.GetFullPath("license.lic")}' a fost salvat cu succes!");
            Console.ResetColor();
        }
    }

    private static string PromptCustomDate()
    {
        Console.Write("Introduceți data de expirare (YYYY-MM-DD): ");
        var d = Clean(Console.ReadLine());
        return d.Length > 0 ? d : DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
    }

    private static string GetLocalHardwareId() => License.GetHardwareId();

    // Piped/redirected stdin (e.g. from PowerShell) can start with a BOM or carry control characters.
    private static string Clean(string? s) =>
        s is null ? string.Empty : new string(s.Where(ch => ch != '\uFEFF' && !char.IsControl(ch)).ToArray()).Trim();

    private static void ShowUsage()
    {
        Console.WriteLine("Utilizare:");
        Console.WriteLine("  LogAnalyzer.KeyGen [<HARDWARE_ID>] [<YYYY-MM-DD>]");
        Console.WriteLine();
        Console.WriteLine("Exemple:");
        Console.WriteLine("  LogAnalyzer.KeyGen");
        Console.WriteLine("  LogAnalyzer.KeyGen ABCD1234EFGH5678 2027-12-31");
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

