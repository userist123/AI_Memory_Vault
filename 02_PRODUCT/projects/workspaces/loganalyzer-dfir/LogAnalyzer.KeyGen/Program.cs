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
}
