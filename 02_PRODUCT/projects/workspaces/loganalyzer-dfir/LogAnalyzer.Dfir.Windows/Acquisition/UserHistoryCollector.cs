using System.Runtime.CompilerServices;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Scripting;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// Planner #11 A6: the history left by the users and the name-resolution override of the station, collected live and read only:
/// <list type="bullet">
/// <item>browser history of every profile: Chromium family (Chrome, Edge, Brave: <c>User Data\Default</c> and <c>User Data\Profile N</c>, file <c>History</c> with its
/// <c>History-wal</c> / <c>History-journal</c>) and Firefox (<c>Profiles\*\places.sqlite</c> with <c>places.sqlite-wal</c>). The side files are copied FIRST and the database after
/// them, into the same folder under their own names, so the parsers find them beside the database; each is registered with its own type. They are copied one file at a time:
/// a database taken by shadow copy and its -wal taken directly can be from different moments (noted on the evidence);</item>
/// <item>the PowerShell PSReadLine history of every profile: every <c>*_history.txt</c> of <c>AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine</c> (<c>ConsoleHost_history.txt</c>, and <c>Visual Studio Code Host_history.txt</c> of the VS Code integrated terminal). PowerShell ISE has no PSReadLine and no history file, so there is nothing to collect for it;</item>
/// <item>the hosts file (<c>%SystemRoot%\System32\drivers\etc\hosts</c>).</item>
/// </list>
/// Profiles come from ProfileList (SIDs only, local drive paths, no reparse point on the way). A file is first read directly with shared access; only a sharing violation
/// (a running browser keeps its database open) falls back to <c>esentutl /y /vss</c>, the mechanism used for the hives of NTUSER.DAT, which needs elevation; an access
/// refusal is reported, never bypassed. A file over its cap, empty, a link, or that grew during the copy is not taken and the reason is reported (a gap).
/// The browser caps default to <see cref="MaxBrowserDbBytes"/>. Standard profile and above: the current user's files are readable without elevation, the others need
/// administrator rights and their refusal is reported, never hidden.
/// </summary>
public sealed class UserHistoryCollector : ICollector
{
    public string Name => "UserHistoryCollector";
    public string Version => "1.0";
    public bool RequiresElevation => false;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

    public const int MaxProfiles = 200, MaxBrowserProfilesPerBrowser = 50;
    /// <summary>Largest browser database (or side file) copied; a larger file is a recorded gap, not a partial copy.</summary>
    public const long MaxBrowserDbBytes = 512L * 1024 * 1024;
    public const string ChromiumWalSourceType = "chromium_history_wal", ChromiumJournalSourceType = "chromium_history_journal", FirefoxWalSourceType = "firefox_places_wal";

    private static readonly string[] ChromiumBrowsers = ["Chrome", "Edge", "Brave"];
    private static readonly string[][] ChromiumRoots =
    [
        ["AppData", "Local", "Google", "Chrome", "User Data"],
        ["AppData", "Local", "Microsoft", "Edge", "User Data"],
        ["AppData", "Local", "BraveSoftware", "Brave-Browser", "User Data"],
    ];
    private static readonly string[] FirefoxProfilesRoot = ["AppData", "Roaming", "Mozilla", "Firefox", "Profiles"];
    private static readonly string[] PsReadLineFolder = ["AppData", "Roaming", "Microsoft", "Windows", "PowerShell", "PSReadLine"];
    public const int MaxPsReadLineFilesPerProfile = 20;

    private readonly Func<IReadOnlyList<UserProfileEntry>> _profiles;
    private readonly Func<string, string, string, string, CancellationToken, VssCopyResult> _vssCopy;
    private readonly Func<IReadOnlySet<string>?> _shadowIds;
    private readonly string _hostsPath;
    private readonly long _maxBrowserBytes;

    /// <param name="profiles">ProfileList rows (tests); by default read from the registry.</param>
    /// <param name="vssCopy">(source, destination, log folder, label, ct) → result (tests); by default esentutl /y /vss through <see cref="ToolRunner"/>.</param>
    /// <param name="shadowIds">Ids of the shadow copies that exist now, null when they cannot be listed (tests); by default <see cref="ShadowCopies.List"/>.</param>
    /// <param name="hostsPath">The hosts file (tests); by default System32\drivers\etc\hosts.</param>
    /// <param name="maxBrowserBytes">Cap of a browser database (tests); by default <see cref="MaxBrowserDbBytes"/>.</param>
    public UserHistoryCollector(Func<IReadOnlyList<UserProfileEntry>>? profiles = null, Func<string, string, string, string, CancellationToken, VssCopyResult>? vssCopy = null,
        Func<IReadOnlySet<string>?>? shadowIds = null, string? hostsPath = null, long maxBrowserBytes = MaxBrowserDbBytes)
    {
        _profiles = profiles ?? UserArtifactsCollector.ReadProfileList;
        _vssCopy = vssCopy ?? ((src, dest, logDir, label, ct) =>
        {
            var run = ToolRunner.Run(ToolRunner.System32("esentutl.exe"), ["/y", src, "/vss", "/d", dest], logDir, label, TimeSpan.FromMinutes(10), ct);
            return new VssCopyResult(run.ExitCode, run.CommandLine);
        });
        _shadowIds = shadowIds ?? CurrentShadowIds;
        _hostsPath = hostsPath ?? Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
        _maxBrowserBytes = maxBrowserBytes;
    }

    private enum Probe { Exists, Missing, Unreadable }

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var o = new CollectorOutcome
        {
            Source = @"istoricul browserelor, *_history.txt PSReadLine (ConsoleHost, VS Code; toate profilurile) și drivers\etc\hosts",
            Tool = "copiere directă (acces partajat); esentutl.exe /vss doar pentru un fișier blocat",
            ToolVersion = ToolRunner.FileVersion(ToolRunner.System32("esentutl.exe")),
        };
        var before = _shadowIds();
        var vssUsed = new StrongBox<bool>(false);
        var logDir = Path.Combine(ctx.Case.Root, "Logs", "tools");
        try
        {
            CollectHosts(ctx, o, vssUsed, logDir, ct);
            IReadOnlyList<UserProfileEntry> profiles = [];
            try { profiles = _profiles().OrderBy(p => p.KeyName, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { o.Errors.Add("Istoricul browserelor și PSReadLine: lista profilurilor (ProfileList) nu a putut fi citită (" + ex.Message + "); profilurile nu au fost cercetate."); }
            int taken = 0;
            foreach (var p in profiles)
            {
                ct.ThrowIfCancellationRequested();
                if (!UserArtifactsCollector.IsSid(p.KeyName)) continue;
                if (UserArtifactsCollector.UnsafeProfilePath(p.ProfilePath) is { Length: > 0 } why) { o.Errors.Add($"Profilul {p.KeyName} nu a fost cercetat pentru istoric: {why}"); continue; }
                if (++taken > MaxProfiles) { o.Errors.Add($"Istoric utilizatori: limita de {MaxProfiles} de profiluri a fost atinsă; restul nu au fost cercetate."); break; }
                CollectPsReadLine(ctx, o, p, vssUsed, logDir, ct);
                CollectChromium(ctx, o, p, vssUsed, logDir, ct);
                CollectFirefox(ctx, o, p, vssUsed, logDir, ct);
            }
        }
        finally
        {
            // also on failure and cancellation: a shadow copy esentutl did not release is reported
            if (vssUsed.Value) CheckLeftoverShadows(ctx, o, before);
        }
        o.Status = o.Evidence.Count > 0 ? (o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success)
                 : o.Errors.Count > 0 ? EvidenceStatus.Failed : EvidenceStatus.Empty;
        return o;
    }

    // ---- hosts and PSReadLine: one text file ----

    private void CollectHosts(CollectorContext ctx, CollectorOutcome o, StrongBox<bool> vssUsed, string logDir, CancellationToken ct)
    {
        if (!File.Exists(_hostsPath) && !Directory.Exists(_hostsPath)) { o.Errors.Add("Fișierul hosts lipsește."); return; }
        var dir = Path.Combine(ctx.Case.RawDir("UserHistory"), "Hosts");
        var dest = NewDest(dir, "hosts");
        if (!CopyOne(o, "hosts", _hostsPath, dest, HostsFileParser.MaxFileBytes, vssUsed, logDir, "esentutl_hosts", ct, out var how)) return;
        o.Evidence.Add(ctx.Case.RegisterStored(dest, _hostsPath, "Hosts", HostsFileParser.SourceTypeName, TemporalType.Historical, Name, Version,
            notes: $"Fișierul hosts al stației ({how}); SHA-256 calculat la colectare. Ora fișierului este ora ultimei modificări, nu a unei intrări."));
    }

    /// <summary>Every <c>*_history.txt</c> of the profile's PSReadLine folder, not only ConsoleHost: the VS Code integrated terminal writes <c>Visual Studio Code Host_history.txt</c> there. Same copy, same parser, same rules.</summary>
    private void CollectPsReadLine(CollectorContext ctx, CollectorOutcome o, UserProfileEntry p, StrongBox<bool> vssUsed, string logDir, CancellationToken ct)
    {
        var folder = Path.Combine([p.ProfilePath, .. PsReadLineFolder]);
        List<string> names;
        try
        {
            if (!Directory.Exists(folder)) return;
            names = [.. Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().Where(PsReadLineHistoryParser.IsHistoryFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { o.Errors.Add($"PSReadLine ({p.KeyName}): folderul nu poate fi citit ({ex.Message})."); return; }
        if (names.Count > MaxPsReadLineFilesPerProfile) { o.Errors.Add($"PSReadLine ({p.KeyName}): {names.Count} fișiere *_history.txt; se iau primele {MaxPsReadLineFilesPerProfile}."); names = names[..MaxPsReadLineFilesPerProfile]; }
        int n = 0;
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            n++;
            var src = Path.Combine(folder, name);
            if (!Present(o, $"PSReadLine ({p.KeyName})", src, name, p.ProfilePath)) continue;
            var dir = Path.Combine(ctx.Case.RawDir("UserHistory"), "PSReadLine", Safe(p.KeyName));
            var dest = NewDest(dir, name);
            if (!CopyOne(o, $"PSReadLine ({p.KeyName}) {name}", src, dest, PsReadLineHistoryParser.MaxFileBytes, vssUsed, logDir, $"esentutl_psreadline_{Safe(p.KeyName)}_{n}", ct, out var how)) continue;
            o.Evidence.Add(ctx.Case.RegisterStored(dest, src, "PSReadLine:" + p.KeyName, PsReadLineHistoryParser.SourceTypeName, TemporalType.Historical, Name, Version,
                notes: $"Profil {p.KeyName} ({p.ProfilePath}). Istoricul PSReadLine, fișierul {name} ({how}); SHA-256 calculat la colectare. Fișierul nu are ore."));
        }
    }

    // ---- browsers ----

    private void CollectChromium(CollectorContext ctx, CollectorOutcome o, UserProfileEntry p, StrongBox<bool> vssUsed, string logDir, CancellationToken ct)
    {
        for (int b = 0; b < ChromiumBrowsers.Length; b++)
        {
            var root = Path.Combine([p.ProfilePath, .. ChromiumRoots[b]]);
            var label = $"{ChromiumBrowsers[b]} ({p.KeyName})";
            if (!DirectoryUsable(o, label, root, p.ProfilePath)) continue;
            List<string> profileDirs;
            try
            {
                profileDirs = new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint })
                    .Select(d => d.Name).Where(n => n == "Default" || n.StartsWith("Profile ", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { o.Errors.Add($"{label}: folderul de profiluri nu poate fi citit ({ex.Message})."); continue; }
            if (profileDirs.Count > MaxBrowserProfilesPerBrowser)
            {
                o.Errors.Add($"{label}: {profileDirs.Count} profiluri de browser; s-au luat primele {MaxBrowserProfilesPerBrowser}.");
                profileDirs = profileDirs.Take(MaxBrowserProfilesPerBrowser).ToList();
            }
            foreach (var pd in profileDirs)
            {
                ct.ThrowIfCancellationRequested();
                var folder = Path.Combine(root, pd);
                CopyDatabase(ctx, o, p, $"{label}/{pd}", folder, "History", ["History-wal", "History-journal"], [ChromiumWalSourceType, ChromiumJournalSourceType],
                    "chromium_history", $"{ChromiumBrowsers[b]}_{Safe(pd)}", vssUsed, logDir, ct);
            }
        }
    }

    private void CollectFirefox(CollectorContext ctx, CollectorOutcome o, UserProfileEntry p, StrongBox<bool> vssUsed, string logDir, CancellationToken ct)
    {
        var root = Path.Combine([p.ProfilePath, .. FirefoxProfilesRoot]);
        var label = $"Firefox ({p.KeyName})";
        if (!DirectoryUsable(o, label, root, p.ProfilePath)) return;
        List<string> profileDirs;
        try
        {
            profileDirs = new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint })
                .Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { o.Errors.Add($"{label}: folderul de profiluri nu poate fi citit ({ex.Message})."); return; }
        if (profileDirs.Count > MaxBrowserProfilesPerBrowser)
        {
            o.Errors.Add($"{label}: {profileDirs.Count} profiluri de browser; s-au luat primele {MaxBrowserProfilesPerBrowser}.");
            profileDirs = profileDirs.Take(MaxBrowserProfilesPerBrowser).ToList();
        }
        foreach (var pd in profileDirs)
        {
            ct.ThrowIfCancellationRequested();
            CopyDatabase(ctx, o, p, $"{label}/{pd}", Path.Combine(root, pd), "places.sqlite", ["places.sqlite-wal"], [FirefoxWalSourceType],
                "firefox_places", "Firefox_" + Safe(pd), vssUsed, logDir, ct);
        }
    }

    /// <summary>One database and its side files into one folder: the side files first, the database after; nothing is registered when the database was not copied.</summary>
    private void CopyDatabase(CollectorContext ctx, CollectorOutcome o, UserProfileEntry p, string label, string folder, string dbName, string[] sideNames, string[] sideTypes,
                              string dbType, string place, StrongBox<bool> vssUsed, string logDir, CancellationToken ct)
    {
        var dbSrc = Path.Combine(folder, dbName);
        if (!Present(o, label, dbSrc, dbName, p.ProfilePath, quietWhenMissing: true)) return;
        var dir = Path.Combine(ctx.Case.RawDir("UserHistory"), "Browsers", Safe(p.KeyName), place);
        for (int i = 1; File.Exists(Path.Combine(dir, dbName)); i++) dir = Path.Combine(ctx.Case.RawDir("UserHistory"), "Browsers", Safe(p.KeyName), place + "_" + i);
        Directory.CreateDirectory(dir);
        var sides = new List<(string Dest, string Src, string Type, string Name, string How)>();
        for (int i = 0; i < sideNames.Length; i++)
        {
            var sideSrc = Path.Combine(folder, sideNames[i]);
            if (!Present(o, label, sideSrc, sideNames[i], p.ProfilePath, quietWhenMissing: true)) continue;
            var sideDest = Path.Combine(dir, sideNames[i]);
            // a side file is useful only beside its database: it can be empty (a checkpointed -wal); an empty one is not an error, only not taken
            if (CopyOne(o, $"{label}: {sideNames[i]}", sideSrc, sideDest, _maxBrowserBytes, vssUsed, logDir, $"esentutl_browser_{Safe(p.KeyName)}_{sideNames[i]}", ct, out var sideHow))
                sides.Add((sideDest, sideSrc, sideTypes[i], sideNames[i], sideHow));
        }
        var dbDest = Path.Combine(dir, dbName);
        if (!CopyOne(o, $"{label}: {dbName}", dbSrc, dbDest, _maxBrowserBytes, vssUsed, logDir, $"esentutl_browser_{Safe(p.KeyName)}_{place}", ct, out var how))
        {
            foreach (var s in sides) TryDelete(s.Dest);
            if (sides.Count > 0) o.Errors.Add($"{label}: baza nu a fost copiată, deci nici fișierele ei însoțitoare ({string.Join(", ", sides.Select(s => s.Name))}) nu au fost păstrate.");
            return;
        }
        var source = $"BrowserHistory:{p.KeyName}:{place}";
        var sideNote = sides.Count > 0 ? $" Copiat împreună cu {string.Join(", ", sides.Select(s => s.Name))} (fișiere copiate pe rând, pot fi din momente diferite)." : " Fără fișier -wal / -journal: ultimele tranzacții pot lipsi.";
        foreach (var s in sides)
            o.Evidence.Add(ctx.Case.RegisterStored(s.Dest, s.Src, source, s.Type, TemporalType.Historical, Name, Version,
                notes: $"Profil {p.KeyName} ({p.ProfilePath}). Fișier însoțitor {s.Name} al {dbName} ({s.How}); parserul îl pune lângă copia de lucru a bazei."));
        o.Evidence.Add(ctx.Case.RegisterStored(dbDest, dbSrc, source, dbType, TemporalType.Historical, Name, Version,
            notes: $"Profil {p.KeyName} ({p.ProfilePath}). Istoric browser {place} ({how}); SHA-256 calculat la colectare. Parserul lucrează pe o copie de lucru." + sideNote));
    }

    // ---- shared copy rules ----

    private static bool DirectoryUsable(CollectorOutcome o, string label, string dir, string profilePath)
    {
        var p = ProbePath(dir, out var detail);
        if (p == Probe.Missing) return false;
        if (p == Probe.Unreadable) { o.Errors.Add($"{label}: folderul nu poate fi citit ({detail})."); return false; }
        if (!Directory.Exists(dir)) { o.Errors.Add($"{label}: {Shorten(dir)} nu este un folder; omis."); return false; }
        if (UserArtifactsCollector.ReparseOnPath(dir) is { Length: > 0 } link)
        { o.Errors.Add($"{label}: calea trece printr-o legătură (reparse point: {Shorten(link)}), care nu este urmată; nu a fost colectat."); return false; }
        return true;
    }

    /// <summary>True when the file exists, is a file and no component of its path is a link; a missing file is not an error (unless <paramref name="quietWhenMissing"/> is false, still not an error).</summary>
    private static bool Present(CollectorOutcome o, string label, string path, string name, string profilePath, bool quietWhenMissing = true)
    {
        var p = ProbePath(path, out var detail);
        if (p == Probe.Missing) return false;
        if (p == Probe.Unreadable) { o.Errors.Add($"{label}: {name} nu poate fi citit ({detail})."); return false; }
        if (Directory.Exists(path)) { o.Errors.Add($"{label}: {name} este un folder, nu un fișier; omis."); return false; }
        if (UserArtifactsCollector.ReparseOnPath(path) is { Length: > 0 } link)
        { o.Errors.Add($"{label}: {name}: calea trece printr-o legătură (reparse point: {Shorten(link)}), care nu este urmată; nu a fost colectat."); return false; }
        return true;
    }

    private static Probe ProbePath(string path, out string detail)
    {
        detail = "";
        try { File.GetAttributes(path); return Probe.Exists; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return Probe.Missing; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { detail = ex.Message; return Probe.Unreadable; }
    }

    /// <summary>
    /// Copies <paramref name="src"/> to <paramref name="dest"/> (a new file) with shared read access; a sharing violation falls back to the shadow copy. Every refusal is
    /// reported in <paramref name="o"/>.Errors and the partial copy removed. Returns false when nothing was copied (an empty file is "omitted", not an error).
    /// </summary>
    private bool CopyOne(CollectorOutcome o, string label, string src, string dest, long cap, StrongBox<bool> vssUsed, string logDir, string vssLabel, CancellationToken ct, out string how)
    {
        how = "copiat direct";
        long len;
        try { len = new FileInfo(src).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { o.Errors.Add($"{label}: nu poate fi citit ({ex.Message})."); return false; }
        if (len == 0) { o.Commands.Add($"{label}: este gol, omis"); return false; }
        if (len > cap) { o.Errors.Add($"{label}: are {len} octeți, peste limita de {cap}; necopiat (lacună: conținutul lui nu este în caz)."); return false; }
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try
        {
            bool tooBig;
            using (var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var d = new FileStream(dest, FileMode.CreateNew, FileAccess.Write))
            {
                var buf = new byte[81920]; long total = 0; int n;
                while ((n = s.Read(buf, 0, (int)Math.Min(buf.Length, cap + 1 - total))) > 0) { d.Write(buf, 0, n); total += n; if (total > cap) break; if ((total & 0xFFFFF) == 0) ct.ThrowIfCancellationRequested(); }
                tooBig = total > cap;
            }
            if (tooBig) { TryDelete(dest); o.Errors.Add($"{label}: a crescut peste limita de {cap} octeți în timpul copierii; necopiat."); return false; }
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDelete(dest);
            o.Errors.Add($"{label}: nu poate fi citit ({ex.Message}); accesul refuzat nu este ocolit prin copia de umbră.");
            return false;
        }
        catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException && (ex.HResult & 0xFFFF) is 32 or 33)
        {
            TryDelete(dest);
            vssUsed.Value = true;   // set before the copy: a cancelled esentutl must still trigger the leftover check
            var run = _vssCopy(src, dest, logDir, vssLabel, ct);
            o.Commands.Add(run.CommandLine);
            if (run.ExitCode != 0 || !File.Exists(dest))
            {
                TryDelete(dest);
                o.Errors.Add($"{label}: nu a putut fi citit direct ({ex.Message}) și copierea din copia de umbră a eșuat (cod {run.ExitCode}); fișierul este deschis de un program (închide browserul sau rulează cu drepturi de administrator).");
                return false;
            }
            if (new FileInfo(dest).Length > cap) { TryDelete(dest); o.Errors.Add($"{label}: din copia de umbră depășește limita de {cap} octeți; necopiat."); return false; }
            how = "copiat din copia de umbră VSS (citirea directă a eșuat, fișier deschis de alt program)";
        }
        catch (IOException ex)
        {
            TryDelete(dest);
            o.Errors.Add($"{label}: nu a putut fi copiat ({ex.Message}).");
            return false;
        }
        if (UserArtifactsCollector.ReparseOnPath(src).Length > 0)
        {
            TryDelete(dest);
            o.Errors.Add($"{label}: a devenit o legătură (reparse point) în timpul copierii; copia a fost eliminată.");
            return false;
        }
        try { File.SetLastWriteTimeUtc(dest, new FileInfo(src).LastWriteTimeUtc); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return true;
    }

    private static string NewDest(string dir, string name)
    {
        var d = dir;
        for (int i = 1; File.Exists(Path.Combine(d, name)); i++) d = dir + "_" + i;
        Directory.CreateDirectory(d);
        return Path.Combine(d, name);
    }

    private void CheckLeftoverShadows(CollectorContext ctx, CollectorOutcome o, IReadOnlySet<string>? before)
    {
        var after = _shadowIds();
        if (before is null || after is null)
        {
            o.Errors.Add("Nu s-a putut verifica dacă după copiere a rămas o copie de umbră (lista copiilor de umbră nu a putut fi citită).");
            return;
        }
        foreach (var id in after.Except(before, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal))
        {
            o.Errors.Add($"După colectare a rămas o copie de umbră nouă ({id}); esentutl /vss trebuia să o elimine. Verificați dacă aparține altui program, altfel ștergeți-o.");
            try { ctx.Case.Audit("vss.leftover", $"{Name} {id}"); } catch (IOException) { }
        }
    }

    private static IReadOnlySet<string>? CurrentShadowIds()
    {
        var r = ShadowCopies.List();
        return r.Status switch
        {
            ShadowListStatus.Listed => r.Items.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ShadowListStatus.None => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            _ => null,
        };
    }

    private static string Safe(string name) => LogAnalyzer.Dfir.Case.CaseWorkspace.SafeName(name);
    private static string Shorten(string s) => s.Length <= 120 ? s : s[..120] + "…";
    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
}
