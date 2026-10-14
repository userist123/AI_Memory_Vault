using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.IO;

public sealed record NativeManifestCheck(bool Ok, IReadOnlyList<string> Problems, bool Checked = true)
{
    public string Message => Ok
        ? "Bibliotecile native corespund manifestului de publicare."
        : "Aplicația nu pornește: bibliotecile native din folderul aplicației nu corespund manifestului scris la publicare " +
          $"({NativeLibraryManifest.FileName}). Cineva ar fi putut înlocui sau adăuga o bibliotecă pe care aplicația, pornită ca administrator, " +
          "ar fi încărcat-o. Reinstalați aplicația din pachetul original, într-un folder în care doar administratorii pot scrie. Detalii: " +
          string.Join("; ", Problems.Take(8));
}

/// <summary>
/// Security audit #5. The published executable is single-file WITHOUT native self-extraction: SQLCipher, WPF and QuestPDF native libraries sit next to
/// the exe, in the (administrator-only) install folder. At publish, <c>NativeLibraryManifest.targets</c> writes <see cref="FileName"/>
/// ("SHA256  name.dll" per line, every *.dll in the publish folder). At startup the published app checks that every listed DLL is present and
/// unchanged and that no other DLL was added (a planted DLL would be found first by the loader); any difference fails closed.
/// </summary>
public enum InstallFolderVerdict { Ok, Warn, RefuseElevated }

public sealed record InstallFolderDecision(InstallFolderVerdict Verdict, string Message);

public static class NativeLibraryManifest
{
    public const string FileName = "native-libraries.sha256";

    /// <summary>
    /// Logical name of the manifest embedded in the main assembly at publish (independent review, finding 3): the text file next to the exe is
    /// unsigned and could be rewritten together with a planted DLL; the copy inside the executable is the one a Release build trusts.
    /// </summary>
    public const string ResourceName = "native-libraries.sha256";

    private static readonly Regex Line = new(@"^(?<h>[0-9A-Fa-f]{64})\s+(?<n>[^\\/:*?""<>|]+\.dll)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The startup decision. The embedded manifest, when present, is always used (single-file or not). Without one: a Debug build may use the
    /// file next to the exe; a Release single-file executable fails closed (it was published without the embedded manifest); a build run from
    /// its bin folder (not single-file, no manifest) has nothing to check against and returns <see cref="NativeManifestCheck.Checked"/> = false.
    /// </summary>
    public static NativeManifestCheck VerifyInstall(string directory, string? embeddedManifest, bool debugBuild, bool singleFile)
    {
        if (embeddedManifest is not null) return VerifyText(directory, embeddedManifest);
        if (debugBuild && File.Exists(Path.Combine(directory, FileName))) return Verify(directory);
        if (singleFile) return new(false, ["manifestul bibliotecilor native nu este încorporat în executabil (publicare incompletă)"]);
        return new(true, ["build de dezvoltare rulat din folderul bin: fără manifest de publicare, bibliotecile nu au fost verificate"], Checked: false);
    }

    /// <summary>
    /// The install folder must be writable only by Administrators / SYSTEM / TrustedInstaller (same rule as the auth store). Otherwise a standard
    /// user could plant a library that an elevated start would load: an elevated start is refused, a standard start warns loudly.
    /// </summary>
    public static InstallFolderDecision DecideInstallFolder(LogAnalyzer.Dfir.Auth.StoreAclResult acl, bool elevated)
    {
        if (acl.Protected) return new(InstallFolderVerdict.Ok, "Folderul aplicației poate fi modificat doar de Administratori și SYSTEM.");
        var details = string.Join("; ", acl.Problems.Take(5));
        return elevated
            ? new(InstallFolderVerdict.RefuseElevated,
                "Aplicația nu pornește ca administrator: folderul ei poate fi modificat și de alte conturi decât Administratorii și SYSTEM, deci cineva " +
                "ar putea pune acolo o bibliotecă pe care aplicația, pornită cu drepturi de administrator, ar încărca-o. Instalați aplicația într-un folder " +
                "în care doar administratorii pot scrie (de exemplu Program Files) sau corectați drepturile folderului. Detalii: " + details)
            : new(InstallFolderVerdict.Warn,
                "Atenție: folderul aplicației poate fi modificat și de alte conturi decât Administratorii și SYSTEM. Aplicația pornește fără drepturi de " +
                "administrator, dar nu trebuie pornită ca administrator din acest folder. Instalați-o într-un folder în care doar administratorii pot scrie. Detalii: " + details);
    }

    public static NativeManifestCheck Verify(string directory)
    {
        var manifestPath = Path.Combine(directory, FileName);
        if (!File.Exists(manifestPath)) return new(false, [$"{FileName} lipsește"]);
        try { return VerifyText(directory, File.ReadAllText(manifestPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(false, ["manifestul nu poate fi citit: " + ex.Message]); }
    }

    public static NativeManifestCheck VerifyText(string directory, string manifestText)
    {
        var problems = new List<string>();
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var raw in manifestText.Split('\n'))
            {
                var l = raw.Trim().TrimStart('﻿');
                if (l.Length == 0) continue;
                var m = Line.Match(l);
                if (!m.Success) { problems.Add($"linie de manifest invalidă: „{(l.Length > 80 ? l[..80] : l)}”"); continue; }
                if (!expected.TryAdd(m.Groups["n"].Value, m.Groups["h"].Value)) problems.Add($"{m.Groups["n"].Value} apare de două ori în manifest");
            }
            if (expected.Count == 0 && problems.Count == 0) problems.Add("manifestul nu listează nicio bibliotecă");
            foreach (var (name, hash) in expected)
            {
                var p = Path.Combine(directory, name);
                if (!File.Exists(p)) { problems.Add($"{name} lipsește"); continue; }
                using var fs = File.OpenRead(p);
                if (!Convert.ToHexString(SHA256.HashData(fs)).Equals(hash, StringComparison.OrdinalIgnoreCase)) problems.Add($"{name} a fost modificat (SHA-256 diferit)");
            }
            foreach (var dll in Directory.EnumerateFiles(directory, "*.dll"))
                if (!expected.ContainsKey(Path.GetFileName(dll))) problems.Add($"{Path.GetFileName(dll)} nu este în manifest");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add("folderul aplicației nu poate fi citit: " + ex.Message);
        }
        return new(problems.Count == 0, problems);
    }
}
