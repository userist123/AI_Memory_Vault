using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.IO;

public sealed record NativeManifestCheck(bool Ok, IReadOnlyList<string> Problems, bool Checked = true, bool ManifestMissing = false)
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

public enum NativeStartupVerdict { Start, Warn, Refuse }

/// <summary>What the application does at startup: start, start after a warning, or refuse (exit code 3 = libraries, 4 = install folder).</summary>
public sealed record NativeStartupDecision(NativeStartupVerdict Verdict, string Message, int ExitCode, string Caption);

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
    /// The library check. The embedded manifest, when present, is always used (single-file or not). Without one: a Debug build may use the
    /// file next to the exe, and a Debug build without any manifest has nothing to check against (<see cref="NativeManifestCheck.Checked"/> =
    /// false). A non-Debug build without the embedded manifest is a failure, single-file or framework-dependent (review of the remediations #4:
    /// both publish modes embed it, so its absence means an incomplete or altered build).
    /// </summary>
    public static NativeManifestCheck VerifyInstall(string directory, string? embeddedManifest, bool debugBuild, bool singleFile, string? selfFileName = null)
    {
        if (embeddedManifest is not null) return VerifyText(directory, embeddedManifest, selfFileName);
        if (debugBuild && File.Exists(Path.Combine(directory, FileName))) return Verify(directory);
        if (!debugBuild)
            return new(false, [$"manifestul bibliotecilor native nu este încorporat în executabil (publicare incompletă{(singleFile ? "" : ", build fără manifest")})"],
                ManifestMissing: true);
        return new(true, ["build de dezvoltare rulat din folderul bin: fără manifest de publicare, bibliotecile nu au fost verificate"], Checked: false);
    }

    public const string UnverifiedStandardStartWarning =
        "Atenție: bibliotecile native ale aplicației nu au putut fi verificate, pentru că manifestul lor nu este încorporat în executabil " +
        "(publicare incompletă). Aplicația pornește fără drepturi de administrator, dar NU trebuie pornită ca administrator: o pornire ca " +
        "administrator este refuzată. Reinstalați aplicația din pachetul original.";

    /// <summary>
    /// The whole startup decision (review of the remediations #4):
    /// <list type="bullet">
    /// <item>libraries that differ from the manifest: refused, elevated or not;</item>
    /// <item>a non-Debug build without the embedded manifest: an elevated start is refused with the library message, a standard start warns;</item>
    /// <item>an elevated process: the install folder must be writable only by Administrators / SYSTEM, whether a manifest was checked or not;</item>
    /// <item>a standard process: a writable folder warns when a manifest was checked (a Debug build run from bin is not checked).</item>
    /// </list>
    /// <paramref name="installFolderAcl"/> is called only when the folder decision is needed.
    /// </summary>
    public static NativeStartupDecision DecideStartup(string directory, string? embeddedManifest, bool debugBuild, bool singleFile, bool elevated,
        Func<LogAnalyzer.Dfir.Auth.StoreAclResult> installFolderAcl, string? selfFileName = null)
    {
        const string libCaption = "LogAnalyzer – biblioteci native neverificate";
        const string folderCaption = "LogAnalyzer – folder de instalare neprotejat";
        var check = VerifyInstall(directory, embeddedManifest, debugBuild, singleFile, selfFileName);
        string? warning = null;
        if (!check.Ok)
        {
            if (!check.ManifestMissing || elevated) return new(NativeStartupVerdict.Refuse, check.Message, 3, libCaption);
            warning = UnverifiedStandardStartWarning;
        }
        if (elevated || check.Checked)
        {
            var folder = DecideInstallFolder(installFolderAcl(), elevated);
            if (folder.Verdict == InstallFolderVerdict.RefuseElevated) return new(NativeStartupVerdict.Refuse, folder.Message, 4, folderCaption);
            if (folder.Verdict == InstallFolderVerdict.Warn)
                return new(NativeStartupVerdict.Warn, warning is null ? folder.Message : warning + "\n\n" + folder.Message, 0, warning is null ? folderCaption : libCaption);
        }
        return warning is null ? new(NativeStartupVerdict.Start, "", 0, "") : new(NativeStartupVerdict.Warn, warning, 0, libCaption);
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

    /// <param name="selfFileName">
    /// Framework-dependent publish: the file name of the main assembly, which carries the embedded manifest and so cannot be listed in it.
    /// Exactly that one name is exempt from "not in the manifest"; null in a single-file bundle.
    /// </param>
    public static NativeManifestCheck VerifyText(string directory, string manifestText, string? selfFileName = null)
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
                if (!expected.ContainsKey(Path.GetFileName(dll)) && !string.Equals(Path.GetFileName(dll), selfFileName, StringComparison.OrdinalIgnoreCase)) problems.Add($"{Path.GetFileName(dll)} nu este în manifest");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add("folderul aplicației nu poate fi citit: " + ex.Message);
        }
        return new(problems.Count == 0, problems);
    }
}
