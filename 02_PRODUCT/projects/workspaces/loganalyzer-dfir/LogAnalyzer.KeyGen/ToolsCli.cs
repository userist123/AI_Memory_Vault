using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Memory;

namespace LogAnalyzer.KeyGen;

/// <summary>
/// Master plan 6.6: approves one Volatility 3 tool folder and one symbol folder. Writes both manifests and the profile into %PROGRAMDATA%\LogAnalyzer\tools.
/// Runs only as administrator; refuses tool or symbol folders that anyone else can change; never reuses a profile folder it cannot trust (a folder a
/// non-administrator prepared beforehand is moved aside, and the new one gets its protected ACL at creation); ends with the same rights check as the application.
/// </summary>
public static class ToolsCli
{
    public static int VolatilityProfile(string[] args, TextWriter o)
    {
        const string usage = "Utilizare: LogAnalyzer.KeyGen volatility-profile <vol.exe> <folder-simboluri> [--timeout <minute>] [--profile-dir <folder>]";
        if (args.Length < 3) { o.WriteLine(usage); return 1; }
        string exe = Path.GetFullPath(args[1]), symbols = Path.GetFullPath(args[2]);
        string dir = VolatilityToolProfile.DefaultDir; int timeout = 30;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--profile-dir" && i + 1 < args.Length) dir = Path.GetFullPath(args[++i]);
            else if (args[i] == "--timeout" && i + 1 < args.Length && int.TryParse(args[++i], out var m) && m is >= 1 and <= 1440) timeout = m;
            else { o.WriteLine(usage); return 1; }
        }
        if (!OperatingSystem.IsWindows()) { o.WriteLine("[-] Comanda este doar pentru Windows."); return 2; }
        if (!IsAdministrator()) { o.WriteLine("[-] Rulați comanda dintr-o consolă pornită ca administrator."); return 3; }
        if (!File.Exists(exe)) { o.WriteLine($"[-] Executabilul nu există: {exe}"); return 2; }
        if (!Directory.Exists(symbols)) { o.WriteLine($"[-] Folderul de simboluri nu există: {symbols}"); return 2; }
        var toolDir = Path.GetDirectoryName(exe)!;
        if (toolDir.Equals(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase) || toolDir.Length <= 3)
        { o.WriteLine("[-] Executabilul trebuie să stea în folderul lui propriu (de exemplu C:\\Program Files\\Volatility3), nu în System32 sau la rădăcina unui disc."); return 2; }
        // the application refuses folders anyone else can change: say so now, not at the first run
        foreach (var (folder, what) in new[] { (toolDir, "folderul uneltei"), (symbols, "folderul simbolurilor") })
        {
            var r = AuthStoreProtection.CheckDirectory(folder);
            if (!r.Protected)
            {
                o.WriteLine($"[-] Și alte conturi decât administratorii pot modifica {what} ({folder}): {string.Join("; ", r.Problems.Take(4))}.");
                o.WriteLine("    Mutați unealta sub C:\\Program Files (sau dați folderului proprietar Administrators și scriere doar pentru Administrators și SYSTEM).");
                return 4;
            }
        }
        try
        {
            FreshProtectedFolder(dir, o);
            var profile = new VolatilityToolProfile
            {
                Executable = exe, Sha256 = Hashing.Sha256File(exe),
                ToolManifestSha256 = FolderManifest.Create(toolDir, Path.Combine(dir, VolatilityToolProfile.ToolManifestName)),
                SymbolsDir = symbols, SymbolsManifestSha256 = FolderManifest.Create(symbols, Path.Combine(dir, VolatilityToolProfile.SymbolsManifestName)),
                TimeoutMinutes = timeout,
            };
            VolatilityToolProfile.Save(profile, dir);
            var final = AuthStoreProtection.CheckDirectory(dir);
            if (!final.Protected) { o.WriteLine("[-] Folderul profilului nu a rămas protejat: " + string.Join("; ", final.Problems.Take(4))); return 4; }
            o.WriteLine($"[+] Profil scris în {dir} (doar Administrators și SYSTEM îl pot modifica).");
            o.WriteLine($"    executabil {exe} SHA-256 {profile.Sha256}");
            o.WriteLine($"    folderul uneltei {toolDir}, manifest SHA-256 {profile.ToolManifestSha256}");
            o.WriteLine($"    simboluri {symbols}, manifest SHA-256 {profile.SymbolsManifestSha256}");
            o.WriteLine("    Orice fișier schimbat, adăugat sau șters după aprobare oprește rularea până la o nouă aprobare.");
            return 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PrivilegeNotHeldException or IOException or InvalidOperationException)
        { o.WriteLine("[-] Profilul nu poate fi scris: " + ex.Message); return 3; }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// A profile folder this command can trust: an existing one is kept only if it already passes the rights check (owner and writers are administrators,
    /// so no one else ever held a handle on it); otherwise it is moved aside and a new folder is created with the protected ACL applied at creation.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void FreshProtectedFolder(string dir, TextWriter o)
    {
        if (Directory.Exists(dir) && !AuthStoreProtection.CheckDirectory(dir).Protected)
        {
            var aside = dir + ".neincredere-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            Directory.Move(dir, aside);
            o.WriteLine($"[!] Folderul existent {dir} putea fi modificat de alte conturi; a fost mutat în {aside} și nu este folosit.");
        }
        if (Directory.Exists(dir)) return;
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var sec = new DirectorySecurity();
        sec.SetOwner(admins);
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
        sec.CreateDirectory(dir);
    }
}
