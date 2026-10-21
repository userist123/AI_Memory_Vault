using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>One row of HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList: the key name (a SID, or not) and its expanded ProfileImagePath.</summary>
public sealed record UserProfileEntry(string KeyName, string ProfilePath);

/// <summary>Result of one VSS copy (esentutl /y src /vss /d dest): its exit code and the exact command line.</summary>
public sealed record VssCopyResult(int ExitCode, string CommandLine);

/// <summary>
/// E7 slice 2: per-user activity artifacts, live, read only, collection Completă (FullForensic) only, administrator only.
/// <list type="bullet">
/// <item>NTUSER.DAT and AppData\Local\Microsoft\Windows\UsrClass.dat of every profile in ProfileList, copied from a VSS snapshot by
/// <c>esentutl /y &lt;src&gt; /vss /d &lt;dest&gt;</c>, the same mechanism as Amcache and SRUM (esentutl makes a non-persistent snapshot and releases it
/// itself). The snapshot does not change during the copy; the copy is hashed (SHA-256) when it is registered.</item>
/// <item><c>$Recycle.Bin\&lt;SID&gt;\$I*</c> of every fixed volume, copied directly (the files are not locked), read twice: when the second read
/// differs the first read is kept and the change is reported. For every $I only the existence and size of its $R are recorded; $R is never opened
/// or copied.</item>
/// </list>
/// Reparse points (junctions, symbolic links) on the way to a hive or inside $Recycle.Bin are never followed; folder names that are not SIDs are skipped
/// with a note; profiles, $I files per SID, $I files in total and the $I size (<see cref="RecycleBinInfoParser.MaxInfoFileBytes"/>) are capped and every
/// cap reached is reported. Shadow copies listed before and after are compared: one that stays after the collector is reported and audited.
/// </summary>
public sealed class UserArtifactsCollector : ICollector
{
    public const int DefaultMaxProfiles = 200;
    public const int DefaultMaxInfoPerSid = 10_000;
    public const int DefaultMaxInfoTotal = 20_000;
    /// <summary>Entries looked at in one SID folder ($I and $R together) before stopping: bounds the work on a folder with millions of entries.</summary>
    public const int MaxEntriesPerSidFolder = 200_000;
    /// <summary>SID folders looked at under one $Recycle.Bin.</summary>
    public const int MaxSidFoldersPerVolume = 1_000;

    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private static readonly string[] ServiceSids = ["S-1-5-18", "S-1-5-19", "S-1-5-20"];
    private static readonly Regex SidRx = new(@"^S-1-\d{1,15}(-\d{1,10}){1,15}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private readonly Func<IReadOnlyList<UserProfileEntry>> _profiles;
    private readonly Func<IReadOnlyList<string>> _fixedVolumes;
    private readonly Func<string, string, string, string, CancellationToken, VssCopyResult> _vssCopy;
    private readonly Func<IReadOnlySet<string>?> _shadowIds;
    private readonly Func<string, byte[]> _readInfo;
    private readonly int _maxProfiles, _maxInfoPerSid, _maxInfoTotal;

    /// <param name="profiles">ProfileList rows (tests); by default read from the registry.</param>
    /// <param name="fixedVolumes">Roots of the fixed volumes (tests); by default the ready fixed drives.</param>
    /// <param name="vssCopy">(source, destination, log folder, label, ct) → result (tests); by default esentutl /vss through <see cref="ToolRunner"/>.</param>
    /// <param name="shadowIds">Ids of the shadow copies that exist now, null when they cannot be listed (tests); by default <see cref="ShadowCopies.List"/>.</param>
    /// <param name="readInfo">Reads one $I file (tests); by default a shared read of at most <see cref="RecycleBinInfoParser.MaxInfoFileBytes"/> + 1 bytes.</param>
    public UserArtifactsCollector(Func<IReadOnlyList<UserProfileEntry>>? profiles = null, Func<IReadOnlyList<string>>? fixedVolumes = null,
        Func<string, string, string, string, CancellationToken, VssCopyResult>? vssCopy = null, Func<IReadOnlySet<string>?>? shadowIds = null,
        Func<string, byte[]>? readInfo = null, int maxProfiles = DefaultMaxProfiles, int maxInfoPerSid = DefaultMaxInfoPerSid, int maxInfoTotal = DefaultMaxInfoTotal)
    {
        _profiles = profiles ?? ReadProfileList;
        _fixedVolumes = fixedVolumes ?? FixedVolumeRoots;
        _vssCopy = vssCopy ?? EsentutlVssCopy;
        _shadowIds = shadowIds ?? CurrentShadowIds;
        _readInfo = readInfo ?? ReadInfoFile;
        _maxProfiles = maxProfiles; _maxInfoPerSid = maxInfoPerSid; _maxInfoTotal = maxInfoTotal;
    }

    public string Name => RecycleBinInfoParser.CollectedBy;
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.FullForensic;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        if (!ctx.IsElevated)
            return CollectorOutcome.NotAvailable("Activitatea utilizatorilor (NTUSER.DAT, UsrClass.dat, Coșul de reciclare $I): necesită drepturi de administrator; nimic nu a fost colectat.", "profiluri și $Recycle.Bin");
        var o = new CollectorOutcome
        {
            Source = "profilurile din ProfileList și $Recycle.Bin de pe volumele fixe",
            Tool = "esentutl.exe /vss (hive-uri), copiere directă ($I)", ToolVersion = ToolRunner.FileVersion(ToolRunner.System32("esentutl.exe")),
        };
        var before = _shadowIds();
        var vssUsed = new System.Runtime.CompilerServices.StrongBox<bool>(false);
        try
        {
            CollectHives(ctx, o, vssUsed, ct);
            CollectRecycleBin(ctx, o, ct);
        }
        finally
        {
            // also on failure and cancellation: a shadow copy esentutl did not release is reported (and audited, the outcome may be lost on cancellation)
            if (vssUsed.Value) CheckLeftoverShadows(ctx, o, before);
        }
        o.Status = o.Evidence.Count > 0 ? (o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success)
                 : o.Errors.Count > 0 ? EvidenceStatus.Failed : EvidenceStatus.Empty;
        return o;
    }

    // ---- hives ----

    private void CollectHives(CollectorContext ctx, CollectorOutcome o, System.Runtime.CompilerServices.StrongBox<bool> vssUsed, CancellationToken ct)
    {
        IReadOnlyList<UserProfileEntry> profiles;
        try { profiles = _profiles(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            o.Errors.Add("Lista profilurilor (ProfileList) nu a putut fi citită: " + ex.Message + "; NTUSER.DAT și UsrClass.dat nu au fost colectate.");
            return;
        }
        int taken = 0;
        var logDir = Path.Combine(ctx.Case.Root, "Logs", "tools");
        var ordered = profiles.OrderBy(p => p.KeyName, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = ordered[i];
            var sid = p.KeyName;
            if (!IsSid(sid)) { o.Errors.Add($"ProfileList: cheia „{Shorten(sid)}” nu este un SID și a fost omisă (de exemplu copia .bak a unui profil deteriorat)."); continue; }
            if (taken >= _maxProfiles)
            {
                o.Errors.Add($"Limita profilurilor colectate ({_maxProfiles}) a fost atinsă; intrări din ProfileList rămase necolectate: {ordered.Count - i}.");
                break;
            }
            bool service = ServiceSids.Contains(sid, StringComparer.OrdinalIgnoreCase);
            var why = UnsafeProfilePath(p.ProfilePath);
            if (why.Length > 0)
            {
                if (service) o.Commands.Add($"{sid}: profil de serviciu omis ({why})");
                else o.Errors.Add($"Profilul {sid}: {why}; hive-urile lui nu au fost colectate.");
                continue;
            }
            var folder = Probe(p.ProfilePath);
            if (folder.State != ProbeState.Exists)
            {
                if (service) o.Commands.Add($"{sid}: profil de serviciu fără folder, omis");
                else o.Errors.Add(folder.State == ProbeState.Missing
                    ? $"Profilul {sid}: folderul profilului lipsește ({p.ProfilePath}); hive-urile lui nu au fost colectate."
                    : $"Profilul {sid}: folderul profilului nu poate fi citit ({p.ProfilePath}: {folder.Detail}); hive-urile lui nu au fost colectate.");
                continue;
            }
            var ntuser = Path.Combine(p.ProfilePath, "NTUSER.DAT");
            var usrclass = Path.Combine(p.ProfilePath, "AppData", "Local", "Microsoft", "Windows", "UsrClass.dat");
            if (service && Probe(ntuser).State != ProbeState.Exists && Probe(usrclass).State != ProbeState.Exists)
            {
                o.Commands.Add($"{sid}: profil de serviciu fără hive, omis");
                continue;
            }
            taken++;
            CopyHive(ctx, o, sid, p.ProfilePath, ntuser, "NTUSER.DAT", "ntuser_hive", "NTUSER", logDir, vssUsed, ct);
            CopyHive(ctx, o, sid, p.ProfilePath, usrclass, "UsrClass.dat", "usrclass_hive", "UsrClass", logDir, vssUsed, ct);
        }
    }

    private void CopyHive(CollectorContext ctx, CollectorOutcome o, string sid, string profilePath, string src, string fileName, string sourceType, string source,
                          string logDir, System.Runtime.CompilerServices.StrongBox<bool> vssUsed, CancellationToken ct)
    {
        var link = ReparseOnPath(src);
        if (link.Length > 0) { o.Errors.Add($"{fileName} ({sid}): calea trece printr-o legătură (reparse point: {link}), care nu este urmată; hive-ul nu a fost colectat."); return; }
        var probe = Probe(src);
        if (probe.State == ProbeState.Missing) { o.Errors.Add($"{fileName} ({sid}): hive-ul lipsește ({src})."); return; }
        if (probe.State == ProbeState.Unreadable) { o.Errors.Add($"{fileName} ({sid}): hive-ul nu poate fi citit ({probe.Detail})."); return; }
        if (probe.IsDirectory) { o.Errors.Add($"{fileName} ({sid}): în locul hive-ului este un folder ({src}); nu a fost colectat."); return; }

        var dir = Path.Combine(ctx.Case.RawDir("UserActivity"), "Hives", sid);
        Directory.CreateDirectory(dir);
        var dest = Unique(Path.Combine(dir, fileName));
        vssUsed.Value = true;   // set before the copy: a cancelled esentutl must still trigger the leftover check
        var run = _vssCopy(src, dest, logDir, $"esentutl_{source.ToLowerInvariant()}_{sid}", ct);
        o.Commands.Add(run.CommandLine);
        if (run.ExitCode != 0 || !File.Exists(dest))
        {
            TryDelete(dest);
            o.Errors.Add($"{fileName} ({sid}): copierea din copia de umbră a eșuat (esentutl /vss, cod {run.ExitCode}); serviciul Volume Shadow Copy poate fi oprit sau indisponibil.");
            return;
        }
        o.Evidence.Add(ctx.Case.RegisterStored(dest, src, $"{source}:{sid}", sourceType, TemporalType.Historical, Name, Version,
            notes: $"Profil {sid} ({profilePath}). Copiat din copia de umbră VSS cu esentutl /vss (copia nu se schimbă în timpul citirii); SHA-256 calculat la colectare. Jurnalele de tranzacții (.LOG1/.LOG2) nu sunt copiate."));
    }

    // ---- Recycle Bin ----

    private void CollectRecycleBin(CollectorContext ctx, CollectorOutcome o, CancellationToken ct)
    {
        IReadOnlyList<string> volumes;
        try { volumes = _fixedVolumes(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            o.Errors.Add("Volumele fixe nu au putut fi listate: " + ex.Message + "; Coșul de reciclare nu a fost colectat.");
            return;
        }
        int total = 0;
        foreach (var root in volumes)
        {
            ct.ThrowIfCancellationRequested();
            var bin = Path.Combine(root, "$Recycle.Bin");
            var probe = Probe(bin);
            if (probe.State == ProbeState.Missing) { o.Commands.Add($"{root}: fără $Recycle.Bin"); continue; }
            if (probe.State == ProbeState.Unreadable) { o.Errors.Add($"Coșul de reciclare de pe {root} nu poate fi citit ({probe.Detail})."); continue; }
            if (probe.IsReparse) { o.Errors.Add($"{bin} este o legătură (reparse point) și nu este urmată; Coșul acestui volum nu a fost colectat."); continue; }
            if (!probe.IsDirectory) { o.Errors.Add($"{bin} nu este un folder; Coșul acestui volum nu a fost colectat."); continue; }

            List<FileSystemInfo> sids;
            try { sids = new DirectoryInfo(bin).EnumerateFileSystemInfos().Take(MaxSidFoldersPerVolume + 1).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { o.Errors.Add($"Coșul de reciclare de pe {root} nu poate fi citit ({ex.Message})."); continue; }
            if (sids.Count > MaxSidFoldersPerVolume)
            {
                o.Errors.Add($"{bin}: peste {MaxSidFoldersPerVolume} de intrări; restul nu au fost cercetate.");
                sids.RemoveAt(sids.Count - 1);
            }
            foreach (var s in sids.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (s.Attributes.HasFlag(FileAttributes.ReparsePoint)) { o.Errors.Add($"{bin}\\{Shorten(s.Name)}: legătură (reparse point), nu este urmată."); continue; }
                if (s is not DirectoryInfo d) continue;   // desktop.ini and the like
                if (!IsSid(d.Name)) { o.Errors.Add($"{bin}\\{Shorten(d.Name)}: numele folderului nu este un SID; folderul a fost omis."); continue; }
                if (total >= _maxInfoTotal) { o.Errors.Add($"Limita totală de fișiere $I ({_maxInfoTotal}) a fost atinsă; {bin}\\{d.Name} și ce urmează nu au fost colectate."); return; }
                total = CollectSid(ctx, o, root, d, total, ct);
            }
        }
    }

    private int CollectSid(CollectorContext ctx, CollectorOutcome o, string root, DirectoryInfo sidDir, int total, CancellationToken ct)
    {
        var sid = sidDir.Name;
        int perSid = 0, examined = 0;
        string? destDir = null;
        try
        {
            foreach (var e in sidDir.EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if (++examined > MaxEntriesPerSidFolder) { o.Errors.Add($"{sidDir.FullName}: peste {MaxEntriesPerSidFolder} de intrări; restul nu au fost cercetate."); break; }
                if (!e.Name.StartsWith("$I", StringComparison.OrdinalIgnoreCase)) continue;
                if (e.Attributes.HasFlag(FileAttributes.ReparsePoint)) { o.Errors.Add($"{sidDir.FullName}\\{Shorten(e.Name)}: legătură (reparse point), nu este urmată."); continue; }
                if (e is DirectoryInfo) { o.Errors.Add($"{sidDir.FullName}\\{Shorten(e.Name)}: un folder cu nume de $I, omis (un $I este întotdeauna un fișier)."); continue; }
                if (!IsSafeChildName(e.Name)) { o.Errors.Add($"{sidDir.FullName}: numele „{Shorten(e.Name)}” nu este un nume de fișier permis; omis."); continue; }
                if (perSid >= _maxInfoPerSid) { o.Errors.Add($"{sidDir.FullName}: limita de fișiere $I pentru un SID ({_maxInfoPerSid}) a fost atinsă; restul nu au fost colectate."); break; }
                if (total >= _maxInfoTotal) { o.Errors.Add($"Limita totală de fișiere $I ({_maxInfoTotal}) a fost atinsă; restul nu au fost colectate."); break; }
                var fi = (FileInfo)e;
                if (fi.Length > RecycleBinInfoParser.MaxInfoFileBytes)
                { o.Errors.Add($"{fi.FullName}: {fi.Length} octeți, peste limita unui $I ({RecycleBinInfoParser.MaxInfoFileBytes} octeți); necopiat."); continue; }

                byte[] first;
                try { first = _readInfo(fi.FullName); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { o.Errors.Add($"{fi.FullName}: nu poate fi citit ({ex.Message})."); continue; }
                if (first.Length > RecycleBinInfoParser.MaxInfoFileBytes)
                { o.Errors.Add($"{fi.FullName}: a crescut peste limita unui $I ({RecycleBinInfoParser.MaxInfoFileBytes} octeți); necopiat."); continue; }

                destDir ??= CreateDir(Path.Combine(ctx.Case.RawDir("UserActivity"), "RecycleBin", VolumeLabel(root), sid));
                var dest = Unique(Path.Combine(destDir, CaseWorkspaceSafe(e.Name)));
                if (!Path.GetFullPath(dest).StartsWith(destDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                { o.Errors.Add($"{sidDir.FullName}: numele „{Shorten(e.Name)}” ar ieși din folderul probei; omis."); continue; }
                using (var w = new FileStream(dest, FileMode.CreateNew, FileAccess.Write)) w.Write(first);
                try { File.SetLastWriteTimeUtc(dest, fi.LastWriteTimeUtc); } catch (IOException) { }

                byte[]? second = null;
                try { second = _readInfo(fi.FullName); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                // the entry was checked before the open; a $I swapped for a link meanwhile is dropped, never registered
                if (ReparseOnPath(fi.FullName).Length > 0)
                {
                    TryDelete(dest);
                    o.Errors.Add($"{fi.FullName}: a devenit o legătură (reparse point) în timpul copierii; copia a fost eliminată, nu este urmată.");
                    continue;
                }
                bool changed = second is null || !second.AsSpan().SequenceEqual(first);

                var notes = $"Coșul de reciclare, SID {sid}, volumul {root}. {RNoteFor(fi)}. Copiat direct (fișierul nu este blocat); SHA-256 calculat pe copie la colectare."
                          + (changed ? (second is null ? " A doua citire a sursei nu a reușit: nu se poate confirma că $I nu s-a schimbat în timpul copierii."
                                                       : " Fișierul $I s-a schimbat în timpul copierii (a doua citire diferă): s-a păstrat prima citire.") : " A doua citire a sursei este identică.");
                o.Evidence.Add(ctx.Case.RegisterStored(dest, fi.FullName, $"RecycleBin:{sid}", RecycleBinInfoParser.SourceTypeName, TemporalType.Historical, Name, Version, notes: notes));
                if (changed) o.Errors.Add($"{fi.FullName}: " + (second is null ? "a doua citire nu a reușit, schimbarea în timpul copierii nu poate fi exclusă." : "s-a schimbat în timpul copierii; s-a păstrat prima citire."));
                perSid++; total++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { o.Errors.Add($"{sidDir.FullName}: folderul Coșului nu poate fi citit ({ex.Message})."); }
        return total;
    }

    /// <summary>Existence and size of the $R next to a $I, from the directory entry only: $R is never opened or copied.</summary>
    public static string RNoteFor(FileInfo info)
    {
        try
        {
            var r = Path.Combine(info.DirectoryName ?? "", "$R" + info.Name[2..]);
            var fi = new FileInfo(r);
            if (fi.Exists) return RecycleBinInfoParser.RNote("present", fi.Attributes.HasFlag(FileAttributes.ReparsePoint) ? null : fi.Length);
            if (Directory.Exists(r)) return RecycleBinInfoParser.RNote("present_folder");
            return RecycleBinInfoParser.RNote("absent");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { return RecycleBinInfoParser.RNote("not_checked"); }
    }

    // ---- snapshots ----

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

    // ---- checks ----

    public static bool IsSid(string name) => name.Length <= 184 && SidRx.IsMatch(name);

    /// <summary>A name that can only be one child of the folder: no separators, no "." / "..", no invalid characters, no ADS colon.</summary>
    public static bool IsSafeChildName(string name) =>
        name.Length is > 0 and <= 255 && name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.IndexOfAny(['\\', '/', ':']) < 0 && name.Trim(' ', '.').Length == name.Length;

    /// <summary>Why a ProfileImagePath is refused ("" = allowed): it must be a fully qualified local drive path without "..".</summary>
    public static string UnsafeProfilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "calea profilului lipsește din ProfileList";
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return $"calea profilului nu este locală ({Shorten(path)})";
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':') return $"calea profilului nu este o cale completă pe un volum local ({Shorten(path)})";
        if (path.Split('\\', '/').Any(s => s == "..")) return $"calea profilului conține „..” ({Shorten(path)})";
        if (path.Contains('%')) return $"calea profilului conține o variabilă neexpandată ({Shorten(path)})";
        return "";
    }

    /// <summary>The first existing component below the volume root that is a reparse point, "" when none.</summary>
    public static string ReparseOnPath(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var cur = root;
        foreach (var part in path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            cur = Path.Combine(cur, part);
            try
            {
                var a = File.GetAttributes(cur);
                if (a.HasFlag(FileAttributes.ReparsePoint)) return cur;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
        }
        return "";
    }

    private enum ProbeState { Exists, Missing, Unreadable }
    private readonly record struct ProbeResult(ProbeState State, bool IsDirectory, bool IsReparse, string Detail);

    private static ProbeResult Probe(string path)
    {
        try
        {
            var a = File.GetAttributes(path);
            return new(ProbeState.Exists, a.HasFlag(FileAttributes.Directory), a.HasFlag(FileAttributes.ReparsePoint), "");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(ProbeState.Missing, false, false, ""); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return new(ProbeState.Unreadable, false, false, ex.Message); }
    }

    private static string Shorten(string s) => s.Length <= 120 ? s : s[..120] + "…";
    private static string CaseWorkspaceSafe(string name) => LogAnalyzer.Dfir.Case.CaseWorkspace.SafeName(name);
    private static string VolumeLabel(string root) => root.Length >= 2 && root[1] == ':' && char.IsAsciiLetter(root[0]) ? root[0].ToString().ToUpperInvariant() : "Vol_" + CaseWorkspaceSafe(root);
    private static string CreateDir(string d) { Directory.CreateDirectory(d); return Path.GetFullPath(d); }

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!; var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        for (int i = 1; ; i++) { var p = Path.Combine(dir, $"{stem}_{i}{ext}"); if (!File.Exists(p)) return p; }
    }

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }

    // ---- production defaults ----

    private static IReadOnlyList<UserProfileEntry> ReadProfileList()
    {
        var list = new List<UserProfileEntry>();
        using var key = Registry.LocalMachine.OpenSubKey(ProfileListKey);
        if (key is null) return list;
        foreach (var name in key.GetSubKeyNames())
        {
            using var k = key.OpenSubKey(name);
            var path = k?.GetValue("ProfileImagePath") as string ?? "";   // REG_EXPAND_SZ is expanded by GetValue
            list.Add(new UserProfileEntry(name, path));
        }
        return list;
    }

    private static IReadOnlyList<string> FixedVolumeRoots() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList();

    private static VssCopyResult EsentutlVssCopy(string src, string dest, string logDir, string label, CancellationToken ct)
    {
        var run = ToolRunner.Run(ToolRunner.System32("esentutl.exe"), ["/y", src, "/vss", "/d", dest], logDir, label, TimeSpan.FromMinutes(10), ct);
        return new VssCopyResult(run.ExitCode, run.CommandLine);
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

    private static byte[] ReadInfoFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[RecycleBinInfoParser.MaxInfoFileBytes + 1];
        int n = 0, r;
        while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
        return buf.AsSpan(0, n).ToArray();
    }
}
