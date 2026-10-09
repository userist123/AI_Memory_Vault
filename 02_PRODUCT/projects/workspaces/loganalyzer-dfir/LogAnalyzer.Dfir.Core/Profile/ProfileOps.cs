using LogAnalyzer.Dfir.Analysis;

namespace LogAnalyzer.Dfir.Profile;

/// <summary>One section of the profile as it is reported: defined or "nedefinit".</summary>
public sealed record SectionStatus(string Section, bool Defined, int Rows)
{
    public const string UndefinedText = "nedefinit";
    public string Text => Defined ? $"definit ({Rows} rânduri)" : UndefinedText;
    public override string ToString() => $"{Section}: {Text}";
}

public static class ProfileOps
{
    /// <summary>Validates every row of every table. An empty profile is valid (it is just undefined).</summary>
    public static List<ProfileIssue> Validate(ProcedureProfile p)
    {
        var issues = new List<ProfileIssue>();
        var zones = p.Zones.Select(z => z.Name.Trim()).Where(z => z.Length > 0).ToList();
        foreach (var t in ProfileTables.All)
        {
            var rows = ProfileTables.Cells(p, t);
            for (int i = 0; i < rows.Count; i++)
                foreach (var (msg, isErr) in ProfileTables.ValidateRow(t, rows[i], zones))
                    issues.Add(new ProfileIssue(t, i + 1, msg, isErr));
        }
        foreach (var dup in zones.GroupBy(z => z, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(new ProfileIssue(ProfileTable.Zones, 0, $"zona „{dup.Key}” este definită de {dup.Count()} ori"));
        // Rotation order: EXPORT → HASH → ARCHIVE → VERIFY → CLEAR. A different order is reported, not refused: it is the operator's procedure.
        var steps = p.RotationProcedure.Select(r => r.Step.Trim().ToUpperInvariant()).Where(s => ProfileTables.RotationOrder.Contains(s)).ToList();
        if (steps.Count > 0)
        {
            var expected = ProfileTables.RotationOrder.Where(steps.Contains).ToList();
            if (!steps.SequenceEqual(expected))
                issues.Add(new ProfileIssue(ProfileTable.RotationProcedure, 0, $"ordinea pașilor diferă de cea așteptată ({string.Join(" → ", ProfileTables.RotationOrder)})", false));
            var missing = ProfileTables.RotationOrder.Where(s => !steps.Contains(s)).ToList();
            if (missing.Count > 0)
                issues.Add(new ProfileIssue(ProfileTable.RotationProcedure, 0, "lipsesc pașii " + string.Join(", ", missing), false));
        }
        return issues;
    }

    public static IReadOnlyList<SectionStatus> Status(ProcedureProfile? p)
    {
        int N(params ProfileTable[] ts) => p is null ? 0 : ts.Sum(t => ProfileTables.Count(p, t));
        SectionStatus S(string name, params ProfileTable[] ts) => new(name, N(ts) > 0, N(ts));
        return
        [
            S("Program de lucru", ProfileTable.WorkingHours, ProfileTable.Shifts),
            S("Mentenanța jurnalelor", ProfileTable.ApprovedAccounts, ProfileTable.MaintenanceWindows, ProfileTable.RotationProcedure),
            S("Software aprobat", ProfileTable.ApprovedSoftware),
            S("Politică GPO așteptată", ProfileTable.ExpectedPolicies),
            S("Zone și transferuri", ProfileTable.Zones, ProfileTable.TransferChannels, ProfileTable.NetworkDestinations),
        ];
    }

    /// <summary>"Program de lucru: nedefinit; Mentenanța jurnalelor: definit (3 rânduri); …". Without a profile every section is nedefinit.</summary>
    public static string Describe(ProcedureProfile? p) => string.Join("; ", Status(p).Select(s => s.ToString()));

    /// <summary>
    /// Approved accounts and maintenance windows expanded over [<paramref name="fromUtc"/>, <paramref name="toUtc"/>] (recurring windows are
    /// turned into concrete UTC intervals using <paramref name="zone"/>, the local clock of the system). <c>null</c> = the maintenance section
    /// defines neither accounts nor windows ("not defined"), which keeps the log clear NotAssessed. Rows that fail validation are not used.
    /// </summary>
    public static LogMaintenancePolicy? ToMaintenancePolicy(ProcedureProfile? p, DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeZoneInfo? zone = null)
    {
        if (p is null) return null;
        var accounts = p.ApprovedAccounts.Where(a => ProfileTables.ValidateRow(ProfileTable.ApprovedAccounts, [a.Account, a.Note]).Count == 0)
                                         .Select(a => a.Account.Trim()).ToList();
        var windows = MaintenanceSchedule.Expand(p.MaintenanceWindows, fromUtc, toUtc, zone ?? TimeZoneInfo.Local);
        if (accounts.Count == 0 && windows.Count == 0 && p.MaintenanceWindows.Count == 0) return null;
        return new LogMaintenancePolicy(accounts, windows);
    }

    /// <summary>
    /// The profile's working hours as the single range the existing engines compare against: the smallest range that covers every weekday
    /// interval and every shift (it errs towards "inside working hours", so it claims fewer off-hours events). <c>null</c> = not defined.
    /// Off-hours stays a comparison with the profile, never a penalty.
    /// </summary>
    public static WorkingHours? ToWorkingHours(ProcedureProfile? p)
    {
        if (p is null) return null;
        var covered = new bool[1440];
        bool any = false;
        void Mark(string s, string e)
        {
            if (!ProfileTables.TryParseTime(s, out var a) || !ProfileTables.TryParseTime(e, out var b) || a == b) return;
            int i = a.Hour * 60 + a.Minute, j = b.Hour * 60 + b.Minute;
            for (int m = i; m != j; m = (m + 1) % 1440) covered[m] = true;
            covered[j] = true;
            any = true;
        }
        foreach (var w in p.WorkingHours.Where(w => ProfileTables.TryParseDay(w.Day, out _))) Mark(w.Start, w.End);
        foreach (var s in p.Shifts) Mark(s.Start, s.End);
        if (!any) return null;
        // The longest run of uncovered minutes (cyclic) is outside working hours; the rest is the covering range.
        int bestStart = -1, bestLen = 0;
        for (int m = 0; m < 1440; m++)
        {
            if (covered[m] || !covered[(m + 1439) % 1440]) continue;
            int len = 0;
            while (len < 1440 && !covered[(m + len) % 1440]) len++;
            if (len > bestLen) { bestLen = len; bestStart = m; }
        }
        if (bestStart < 0) return new WorkingHours(new TimeOnly(0, 0), new TimeOnly(23, 59));
        int from = (bestStart + bestLen) % 1440, to = (bestStart + 1439) % 1440;
        return new WorkingHours(new TimeOnly(from / 60, from % 60), new TimeOnly(to / 60, to % 60));
    }
}

/// <summary>Expansion of recurring maintenance windows (e.g. "first Monday of the month 08:00-10:00") into concrete UTC intervals.</summary>
public static class MaintenanceSchedule
{
    /// <summary>Largest span expanded, to keep a profile with a wrong date range from producing an unbounded list.</summary>
    public static readonly TimeSpan MaxSpan = TimeSpan.FromDays(366 * 10);

    public static List<MaintenanceWindow> Expand(IEnumerable<MaintenanceWindowRow> rows, DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeZoneInfo zone)
    {
        var result = new List<MaintenanceWindow>();
        if (toUtc < fromUtc) return result;
        if (toUtc - fromUtc > MaxSpan) toUtc = fromUtc + MaxSpan;
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromUtc, zone).Date).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(toUtc, zone).Date).AddDays(1);
        foreach (var r in rows)
        {
            if (ProfileTables.ValidateRow(ProfileTable.MaintenanceWindows, [r.Kind, r.Date, r.Day, r.Ordinal, r.Start, r.End, r.Note]).Count > 0) continue;
            var kind = ProfileTables.NormalizeKind(r.Kind)!;
            ProfileTables.TryParseTime(r.Start, out var start); ProfileTables.TryParseTime(r.End, out var end);
            IEnumerable<DateOnly> days;
            if (kind == "once") { ProfileTables.TryParseDate(r.Date, out var d); days = [d]; }
            else
            {
                ProfileTables.TryParseDay(r.Day, out var dow);
                int ord = -1;
                if (kind == "monthly") ProfileTables.TryParseOrdinal(r.Ordinal, out ord);
                days = Enumerable.Range(0, lastDay.DayNumber - firstDay.DayNumber + 1).Select(i => firstDay.AddDays(i))
                    .Where(d => d.DayOfWeek == dow && (kind == "weekly" || (ord >= 1 ? (d.Day - 1) / 7 + 1 == ord : d.AddDays(7).Month != d.Month)));
            }
            foreach (var d in days)
            {
                var s = ToUtc(d, start, zone);
                var e = ToUtc(end > start ? d : d.AddDays(1), end, zone);
                if (e >= fromUtc && s <= toUtc) result.Add(new MaintenanceWindow(s, e));
            }
        }
        return result.OrderBy(w => w.StartUtc).ToList();
    }

    private static DateTimeOffset ToUtc(DateOnly d, TimeOnly t, TimeZoneInfo zone)
    {
        var local = d.ToDateTime(t, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);   // spring-forward gap: the hour does not exist, use the next one
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
