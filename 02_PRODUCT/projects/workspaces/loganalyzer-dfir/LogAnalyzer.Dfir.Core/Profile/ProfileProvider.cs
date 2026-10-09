using LogAnalyzer.Dfir.Analysis;

namespace LogAnalyzer.Dfir.Profile;

/// <summary>
/// The profile as the running application sees it: loaded once from the profile file, replaced when the operator saves a new one.
/// The engines never read the file themselves; they take what this returns. No profile = <c>null</c> everywhere = unchanged behaviour.
/// </summary>
public sealed class ProfileProvider(string? path = null)
{
    public static ProfileProvider Shared { get; } = new();

    private readonly object _gate = new();
    private ProfileImportResult? _loaded;
    private int _revision;

    public string Path { get; } = path ?? ProfileStore.DefaultPath;

    private ProfileImportResult Loaded { get { lock (_gate) return _loaded ??= ProfileStore.Load(Path); } }   // (re-entrant: Monitor locks are)

    /// <summary>The profile in force; null when there is no profile file (or it cannot be read - see <see cref="Issues"/>).</summary>
    public ProcedureProfile? Current => Loaded.Profile;
    public IReadOnlyList<ProfileIssue> Issues => Loaded.Issues;
    public int Revision { get { lock (_gate) return _revision; } }

    /// <summary>Re-reads the file (e.g. after another tool changed it).</summary>
    public void Reload() { lock (_gate) { _loaded = null; _revision++; } }

    /// <summary>Makes <paramref name="saved"/> the profile in force (the view calls this after a successful save).</summary>
    public void Replace(ProcedureProfile saved) { lock (_gate) { _loaded = new ProfileImportResult { Profile = saved, Issues = ProfileOps.Validate(saved) }; _revision++; } }

    private (int Revision, DateTime FirstDay, DateTime LastDay, LogMaintenancePolicy? Policy)? _memo;

    /// <summary>Maintenance policy around <paramref name="timesUtc"/> (recurring windows expanded over their span ± 1 day); null without a profile or maintenance section.</summary>
    public LogMaintenancePolicy? MaintenancePolicyFor(IEnumerable<DateTimeOffset> timesUtc)
    {
        var times = timesUtc.ToList();
        if (times.Count == 0) times.Add(DateTimeOffset.UtcNow);
        var first = times.Min().UtcDateTime.Date; var last = times.Max().UtcDateTime.Date;
        lock (_gate)
        {
            if (_memo is { } m && m.Revision == _revision && m.FirstDay == first && m.LastDay == last) return m.Policy;   // live events arrive one by one on the same day
            var policy = ProfileSnapshot.MaintenancePolicyFor(Current, [new DateTimeOffset(first, TimeSpan.Zero), new DateTimeOffset(last, TimeSpan.Zero).AddDays(1)], TimeZoneInfo.Local.Id);
            _memo = (_revision, first, last, policy);
            return policy;
        }
    }

    /// <summary>Working hours for the engines that compare with them; null = not defined.</summary>
    public WorkingHours? WorkingHours => ProfileOps.ToWorkingHours(Current);
}
