using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// The shortcuts and Jump Lists of every user profile, live, read only (research 2026-11-08: the live collection took none of them, so the link between
/// the files a user opened and the serial of the volume they were on was available only when the examiner imported them by hand):
/// <c>AppData\Roaming\Microsoft\Windows\Recent\*.lnk</c>, <c>Recent\AutomaticDestinations\*.automaticDestinations-ms</c> and
/// <c>Recent\CustomDestinations\*.customDestinations-ms</c>. Profiles come from ProfileList (SIDs only, never the service accounts); a profile path that is
/// not local, or a reparse point on the way, is refused and reported; files that are reparse points or larger than the caps are not copied. The current
/// user's profile is readable without elevation; the others need administrator rights, and their refusal is reported, never hidden.
/// </summary>
public sealed class RecentItemsCollector : ICollector
{
    public string Name => "RecentItemsCollector";
    public string Version => "1.0";
    public bool RequiresElevation => false;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

    public const int MaxProfiles = 200, MaxLinksPerProfile = 2_000, MaxJumpListsPerProfile = 500;
    private static readonly string[] ServiceSids = ["S-1-5-18", "S-1-5-19", "S-1-5-20"];

    /// <summary>What is copied from one Recent folder: (sub-folder, pattern, source type, size cap, count cap).</summary>
    public static readonly (string Folder, string Pattern, string SourceType, long MaxBytes, int MaxCount)[] Sets =
    [
        ("", "*.lnk", "lnk", 1L * 1024 * 1024, MaxLinksPerProfile),
        ("AutomaticDestinations", "*.automaticDestinations-ms", "jumplist_auto", 256L * 1024 * 1024, MaxJumpListsPerProfile),
        ("CustomDestinations", "*.customDestinations-ms", LogAnalyzer.Dfir.FileSystem.CustomJumpListParser.SourceTypeName, LogAnalyzer.Dfir.FileSystem.CustomJumpListParser.MaxFileBytes, MaxJumpListsPerProfile),
    ];

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var o = new CollectorOutcome { Source = @"%USERPROFILE%\AppData\Roaming\Microsoft\Windows\Recent (toate profilurile)", Tool = "copy" };
        IReadOnlyList<UserProfileEntry> profiles;
        try { profiles = UserArtifactsCollector.ReadProfileList(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { return CollectorOutcome.NotAvailable("Lista profilurilor (ProfileList) nu a putut fi citită: " + ex.Message, "ProfileList"); }

        foreach (var p in profiles.Where(p => UserArtifactsCollector.IsSid(p.KeyName) && !ServiceSids.Contains(p.KeyName, StringComparer.OrdinalIgnoreCase)).Take(MaxProfiles))
        {
            ct.ThrowIfCancellationRequested();
            CollectProfile(ctx.Case, p.KeyName, p.ProfilePath, o, ct);
        }
        o.Status = o.Evidence.Count == 0 ? (o.Errors.Count > 0 ? EvidenceStatus.Failed : EvidenceStatus.Empty) : o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }

    /// <summary>One profile's Recent folder and its two Jump List folders; refusals go to <paramref name="o"/>.Errors. Internal for the tests.</summary>
    internal void CollectProfile(LogAnalyzer.Dfir.Case.CaseWorkspace ws, string sid, string profilePath, CollectorOutcome o, CancellationToken ct)
    {
        if (UserArtifactsCollector.UnsafeProfilePath(profilePath) is { Length: > 0 } why) { o.Errors.Add($"{sid}: {why}"); return; }
        var recent = Path.Combine(profilePath, "AppData", "Roaming", "Microsoft", "Windows", "Recent");
        if (UserArtifactsCollector.ReparseOnPath(recent) is { Length: > 0 } link) { o.Errors.Add($"{sid}: legătura „{link}” pe drumul spre Recent nu este urmată"); return; }
        foreach (var (folder, pattern, sourceType, maxBytes, maxCount) in Sets)
        {
            var dir = folder.Length == 0 ? recent : Path.Combine(recent, folder);
            // review round 2: a user can make AutomaticDestinations a junction to another user's folder; the files would then be filed under the wrong SID
            if (folder.Length > 0 && UserArtifactsCollector.ReparseOnPath(dir) is { Length: > 0 } sub) { o.Errors.Add($"{sid}: legătura „{sub}” nu este urmată"); continue; }
            List<FileInfo> files;
            try
            {
                if (!Directory.Exists(dir)) continue;
                files = new DirectoryInfo(dir).EnumerateFiles(pattern, new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint })
                    .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                o.Errors.Add($"{sid}: {(folder.Length == 0 ? "Recent" : folder)} nu a putut fi citit ({(ex is UnauthorizedAccessException ? "necesită administrator" : ex.Message)})");
                continue;
            }
            if (files.Count > maxCount) o.Errors.Add($"{sid}: {(folder.Length == 0 ? "Recent" : folder)} are {files.Count} fișiere; s-au copiat cele mai noi {maxCount}");
            foreach (var f in files.Take(maxCount))
            {
                ct.ThrowIfCancellationRequested();
                if (f.Length == 0 || f.Length > maxBytes) { if (f.Length > maxBytes) o.Errors.Add($"{sid}: {f.Name} depășește limita de citire, necopiat"); continue; }
                try { o.Evidence.Add(ws.ImportFile(f.FullName, "Recent:" + sid, sourceType, TemporalType.Historical, Name, Version)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { o.Errors.Add($"{sid}: {f.Name}: {ex.Message}"); }
            }
        }
    }
}
