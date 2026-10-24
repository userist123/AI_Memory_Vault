using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.KeyGen;

/// <summary>
/// Licence tooling (security audit #3), run on the owner's offline machine only. Exit codes: 0 created, 1 usage / I/O / validation error.
/// <list type="bullet">
/// <item><c>license-keygen &lt;private-key-file&gt; [&lt;public-key-file&gt;] [--passphrase | --plain]</c>: ECDSA P-256 licence key, protected like the
/// report key (DPAPI for the current user by default, or a passphrase); never overwrites, never inside a git repository. The printed public key
/// goes into <c>LogAnalyzer.Core/Licensing/license-public-keys.txt</c> (compiled into the editions).</item>
/// <item><c>license-sign &lt;private-key-file&gt; --hardware-id &lt;16 hex&gt; --expires &lt;yyyy-MM-dd&gt; [--id &lt;licence id&gt;] [--out &lt;license.lic&gt;]</c>:
/// prints the signed licence (LA1.…), after checking it with the same verification the application performs.</item>
/// </list>
/// </summary>
public static class LicenseCli
{
    public static int Keygen(string[] args, TextWriter o, TextReader? input = null)
    {
        var flags = args.Skip(1).Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToList();
        var pos = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        bool usePass = flags.Contains("--passphrase"), plain = flags.Contains("--plain");
        if (pos.Count is < 1 or > 2 || flags.Except(["--passphrase", "--plain"]).Any() || usePass && plain)
        {
            o.WriteLine("Utilizare: license-keygen <fișier-cheie-privată> [<fișier-cheie-publică>] [--passphrase | --plain]");
            return 1;
        }
        var priv = Path.GetFullPath(pos[0]);
        if (File.Exists(priv)) { o.WriteLine("[-] Fișierul cheii există deja; nu se suprascrie."); return 1; }
        if (ReportCli.InsideGitRepository(priv)) { o.WriteLine("[-] Cheia privată de licență nu se scrie într-un depozit git. Alegeți un mediu înregistrat, offline, în afara depozitului."); return 1; }
        string? pass = null;
        if (usePass)
        {
            pass = Environment.GetEnvironmentVariable(ReportCli.PassphraseEnvironmentVariable);
            if (string.IsNullOrEmpty(pass))
            {
                o.WriteLine("Parola cheii (nu se afișează pe ecran și nu se scrie în jurnal):");
                pass = ReportCli.ReadSecret(input);
                if (string.IsNullOrEmpty(pass)) { o.WriteLine("[-] Parola nu poate fi goală."); return 1; }
                o.WriteLine("Repetați parola:");
                if (!string.Equals(pass, ReportCli.ReadSecret(input), StringComparison.Ordinal)) { o.WriteLine("[-] Cele două parole nu coincid; nu s-a scris nicio cheie."); return 1; }
            }
        }
        try
        {
            var (privateKey, publicKey) = ReportSignature.GenerateKey();
            var stored = plain ? privateKey : usePass ? ReportKeyProtection.ProtectWithPassphrase(privateKey, pass!) : ReportKeyProtection.ProtectForCurrentUser(privateKey);
            Directory.CreateDirectory(Path.GetDirectoryName(priv)!);
            ReportCli.WriteOwnerOnly(priv, stored);
            if (pos.Count == 2) File.WriteAllText(pos[1], publicKey + Environment.NewLine);
            o.WriteLine("[+] Cheia privată de licență a fost scrisă în " + priv + " (" + (plain ? "NECRIPTATĂ" : usePass ? "criptată cu parolă" : "criptată pentru utilizatorul Windows curent, DPAPI") + "; acces doar pentru utilizatorul curent).");
            o.WriteLine("    Păstrați-o offline, pe un mediu înregistrat; nu o copiați în depozit, în aplicație sau pe stațiile clienților.");
            o.WriteLine("[+] Cheia publică (o linie în LogAnalyzer.Core/Licensing/license-public-keys.txt, apoi se reconstruiește aplicația):");
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

    public static int Sign(string[] args, TextWriter o, TextReader? input = null)
    {
        const string usage = "Utilizare: license-sign <fișier-cheie-privată> --hardware-id <16 hex> --expires <AAAA-LL-ZZ> [--id <id licență>] [--out <license.lic>]";
        if (args.Length < 2) { o.WriteLine(usage); return 1; }
        string? hw = null, expires = null, id = null, outFile = null;
        for (int i = 2; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--hardware-id": hw = Next(); break;
                case "--expires": expires = Next(); break;
                case "--id": id = Next(); break;
                case "--out": outFile = Next(); break;
                default: o.WriteLine(usage); return 1;
            }
        }
        if (hw is null || expires is null) { o.WriteLine(usage); return 1; }
        hw = hw.Trim().ToUpperInvariant();
        if (!LicenseService.IsHardwareId(hw)) { o.WriteLine("[-] Hardware ID invalid: exact 16 caractere hexazecimale, cum apare în fereastra de activare a aplicației."); return 1; }
        if (!DateTime.TryParseExact(expires.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
        { o.WriteLine("[-] Data de expirare trebuie să fie în formatul AAAA-LL-ZZ (de exemplu 2027-12-31)."); return 1; }
        if (expiry.Date < DateTime.UtcNow.Date) { o.WriteLine("[-] Data de expirare este în trecut; licența nu ar fi acceptată."); return 1; }
        try
        {
            var text = File.ReadAllText(args[1]);
            if (!ReportKeyProtection.IsProtected(text))
                o.WriteLine("[!] Atenție: cheia privată de licență este stocată NECRIPTAT. Generați una protejată (license-keygen fără --plain) și retrageți-o pe aceasta.");
            string? pass = null;
            if (ReportKeyProtection.NeedsPassphrase(text))
            {
                pass = Environment.GetEnvironmentVariable(ReportCli.PassphraseEnvironmentVariable);
                if (string.IsNullOrEmpty(pass)) { o.WriteLine("Parola cheii (nu se afișează pe ecran):"); pass = ReportCli.ReadSecret(input); }
            }
            using var key = ReportSignature.LoadPrivateKey(text, pass);
            var licence = LicenseService.Sign(key, hw, expiry.Date, id);
            // Self-check with exactly the verification the application performs, against this key's public half.
            var self = new LicenseService([Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())]).Check(hw, licence, DateTime.UtcNow, out _);
            if (self != LicenseCheck.Valid) { o.WriteLine("[-] Auto-verificarea a eșuat (" + self + "): licența nu se distribuie."); return 1; }
            if (outFile is not null) File.WriteAllText(outFile, licence);
            o.WriteLine($"[+] Licență semnată pentru {hw}, valabilă până la {expiry:yyyy-MM-dd} inclusiv (UTC), cheia {ReportSignature.KeyId(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))}:");
            o.WriteLine(licence);
            if (outFile is not null) o.WriteLine("[+] Scrisă și în " + Path.GetFullPath(outFile));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException or ArgumentException)
        {
            o.WriteLine("[-] " + ex.Message);
            return 1;
        }
    }
}
