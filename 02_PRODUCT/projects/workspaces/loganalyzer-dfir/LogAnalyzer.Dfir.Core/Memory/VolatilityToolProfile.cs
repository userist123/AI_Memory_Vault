using System.Security.Cryptography;
using System.Text.Json;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Memory;

/// <summary>
/// Master plan 6.6, the Volatility 3 the application may start: the executable and its SHA-256, the SHA-256 of the manifest of the whole tool folder,
/// the symbol folder and the SHA-256 of its manifest. Kept in its own folder, %PROGRAMDATA%\LogAnalyzer\tools, which only Administrators and SYSTEM may
/// change (LogAnalyzer.KeyGen volatility-profile creates it so); the application only reads it and refuses it when anyone else can write there.
/// A missing file means "no Volatility configured": the analyst keeps importing JSON outputs by hand, as before.
/// </summary>
public sealed class VolatilityToolProfile
{
    public const string FileName = "tools.json";
    public const string ToolManifestName = "tool_manifest.json";
    public const string SymbolsManifestName = "symbols_manifest.json";

    public string Executable { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ToolManifestSha256 { get; set; } = "";
    public string SymbolsDir { get; set; } = "";
    public string SymbolsManifestSha256 { get; set; } = "";
    public int TimeoutMinutes { get; set; } = 30;

    /// <summary>The protected folder that holds the profile and both manifests.</summary>
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "tools");

    private sealed class Root { public VolatilityToolProfile? Volatility { get; set; } }

    /// <summary>Null when the file or its "Volatility" section is absent; an unreadable file is reported through <paramref name="problem"/> and also gives null.</summary>
    public static VolatilityToolProfile? Load(string? dir, out string? problem)
    {
        problem = null;
        var p = Path.Combine(dir ?? DefaultDir, FileName);
        try
        {
            if (!File.Exists(p)) return null;
            return JsonSerializer.Deserialize<Root>(File.ReadAllText(p))?.Volatility;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { problem = ex.Message; return null; }
    }

    private static readonly object RecognisedGate = new();
    private static (string Key, string? Exe) _recognised = ("", null);

    /// <summary>
    /// The approved executable for recognising its earlier runs in the sealed audit (a later control, after the application restarted): the profile folder and
    /// the tool folder can be changed only by administrators, and the file has the pinned SHA-256 now. Null otherwise. The answer is kept until the profile
    /// or the executable changes on disk. A run itself is always checked in full (VolatilityRunner.CheckTool); this only keeps a run already made from being
    /// reported as a foreign program.
    /// </summary>
    public static string? ApprovedExecutable(string? dir = null, Func<string, Auth.StoreAclResult>? aclCheck = null)
    {
        if (!OperatingSystem.IsWindows() && aclCheck is null) return null;
        // the caller is the station control: anything unexpected in a file it does not trust yet means "not recognised", never an exception (review 2026-11-08)
        try
        {
            var d = dir ?? DefaultDir;
            var profile = Load(d, out var problem);
            if (problem is not null || profile is null || string.IsNullOrEmpty(profile.Executable) || profile.Sha256 is null
                || !Path.IsPathFullyQualified(profile.Executable) || profile.Executable.StartsWith(@"\\", StringComparison.Ordinal)
                || profile.Sha256.Trim().Length != 64 || !File.Exists(profile.Executable)) return null;
            var exe = new FileInfo(profile.Executable);
            if (exe.LinkTarget is not null || exe.DirectoryName is not { Length: > 3 } toolDir) return null;   // a drive root is never a tool folder
            if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(exe.FullName)!).DriveType != DriveType.Fixed) return null;   // mapped or removable
            var key = string.Join("|", d, File.GetLastWriteTimeUtc(Path.Combine(d, FileName)).Ticks, exe.FullName, exe.Length, exe.LastWriteTimeUtc.Ticks, profile.Sha256.Trim());
            bool cache = aclCheck is null;   // only the production rights check is remembered
            if (cache) lock (RecognisedGate) if (_recognised.Key == key) return _recognised.Exe;
            Func<string, Auth.StoreAclResult> acl = aclCheck ?? (OperatingSystem.IsWindows() ? Auth.AuthStoreProtection.CheckDirectory : _ => new Auth.StoreAclResult(false, []));
            string? ok = acl(d).Protected && acl(toolDir).Protected
                         && Hashing.Sha256File(exe.FullName).Equals(profile.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) ? exe.FullName : null;
            if (cache && ok is not null) lock (RecognisedGate) _recognised = (key, ok);   // a refusal is checked again next time
            return ok;
        }
        catch (Exception) { return null; }
    }

    public static void Save(VolatilityToolProfile v, string? dir)
    {
        var d = dir ?? DefaultDir;
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, FileName), JsonSerializer.Serialize(new Root { Volatility = v }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>One file of an approved folder: its path relative to the folder (forward slashes) and its SHA-256.</summary>
public sealed record ManifestFile(string Path, string Sha256);

/// <summary>
/// The manifest of an approved folder (the tool folder, the symbol folder), kept outside that folder in the protected profile folder. Its own SHA-256 is
/// pinned in the profile, so a file that is added, removed or changed after the administrator approved the folder is refused before the tool runs.
/// </summary>
public static class FolderManifest
{
    public const int MaxFiles = 20_000;

    private static string Rel(string root, string file) => System.IO.Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>Writes the manifest of every file under <paramref name="dir"/> to <paramref name="manifestPath"/> and returns the manifest's SHA-256.</summary>
    public static string Create(string dir, string manifestPath)
    {
        var root = System.IO.Path.GetFullPath(dir);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(f => Rel(root, f), StringComparer.Ordinal)
            .Select(f => new ManifestFile(Rel(root, f), Hashing.Sha256File(f))).ToList();
        if (files.Count > MaxFiles) throw new InvalidOperationException($"Folderul are peste {MaxFiles} fișiere.");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(files, new JsonSerializerOptions { WriteIndented = true }));
        return Hashing.Sha256File(manifestPath);
    }

    /// <summary>
    /// Checks the folder against its pinned manifest: the manifest's hash, every listed file present with its hash, no file that is not listed, and no
    /// link or junction anywhere (a link would let the folder point outside what was hashed). Returns the problems; empty means the folder is the approved one.
    /// </summary>
    public static IReadOnlyList<string> Verify(string dir, string manifestPath, string pinnedManifestSha256, string what)
    {
        var problems = new List<string>();
        if (dir.Length == 0) return [$"Profilul nu are folderul pentru {what}."];
        var root = System.IO.Path.GetFullPath(dir);
        if (!Directory.Exists(root)) return [$"Folderul pentru {what} nu există: {dir}."];
        if (new DirectoryInfo(root).LinkTarget is not null) return [$"Folderul pentru {what} este o legătură (link sau joncțiune); este refuzat."];
        if (!File.Exists(manifestPath)) return [$"Manifestul pentru {what} lipsește ({manifestPath}); administratorul reface aprobarea cu LogAnalyzer.KeyGen volatility-profile."];
        if (pinnedManifestSha256.Trim().Length != 64) return [$"Profilul nu are amprenta manifestului pentru {what}."];
        var bytes = File.ReadAllBytes(manifestPath);   // read once: the bytes hashed are the bytes parsed
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!actual.Equals(pinnedManifestSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            return [$"Manifestul pentru {what} nu este cel aprobat: amprenta {actual.ToLowerInvariant()} diferă de cea din profil."];
        List<ManifestFile> listed;
        try { listed = JsonSerializer.Deserialize<List<ManifestFile>>(bytes) ?? []; }
        catch (JsonException ex) { return [$"Manifestul pentru {what} nu poate fi citit: " + ex.Message]; }
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in listed)
            if (f.Path.Length == 0 || f.Path.Split('/').Contains("..") || System.IO.Path.IsPathRooted(f.Path) || !byPath.TryAdd(f.Path, f.Sha256))
                problems.Add($"Intrare nevalidă în manifestul pentru {what}: „{f.Path}”.");
        int seen = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            if (++seen > MaxFiles) { problems.Add($"Folderul pentru {what} are peste {MaxFiles} intrări."); break; }
            var info = new FileInfo(entry);
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) { problems.Add($"Legătură în folderul pentru {what}: {Rel(root, entry)}."); continue; }
            if (info.Attributes.HasFlag(FileAttributes.Directory)) continue;
            var rel = Rel(root, entry);
            if (!byPath.Remove(rel, out var sha)) { problems.Add($"Fișier care nu este în manifestul pentru {what}: {rel}."); continue; }
            if (!Hashing.Sha256File(entry).Equals(sha, StringComparison.OrdinalIgnoreCase)) problems.Add($"Fișier modificat după aprobare ({what}): {rel}.");
        }
        foreach (var missing in byPath.Keys.OrderBy(k => k, StringComparer.Ordinal)) problems.Add($"Fișier din manifestul pentru {what} care lipsește: {missing}.");
        return problems;
    }
}
