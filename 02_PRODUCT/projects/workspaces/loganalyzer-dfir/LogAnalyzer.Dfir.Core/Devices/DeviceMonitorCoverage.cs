namespace LogAnalyzer.Dfir.Devices;

/// <summary>A stretch of the controlled period in which the monitor was not running.</summary>
public sealed record CoverageGap(DateTimeOffset FromUtc, DateTimeOffset ToUtc);

/// <summary>An interval the monitor itself recorded as lost (a watcher error or overflow, the Windows service stopped, the station slept): "device.monitor_gap" in the sealed audit.</summary>
public sealed record SealedGap(DateTimeOffset FromUtc, DateTimeOffset ToUtc, string Reason);

/// <summary>
/// The coverage of a period: the intervals the monitor did not watch, and whether it watched any part of the period at all (no sealed run covering any part =
/// the control cannot say anything about the period).
/// </summary>
public sealed record CoverageResult(IReadOnlyList<CoverageGap> Gaps, bool AnyCoverage)
{
    public bool Complete => AnyCoverage && Gaps.Count == 0;
}

/// <summary>Which part of a period the monitor's running windows cover (pure). The windows must come from the SEALED audit; the journal's own copy is only compared with them.</summary>
public static class DeviceMonitorCoverage
{
    /// <summary>A gap between two runs shorter than this (an application restart) is not reported; an interval the monitor recorded as lost always is.</summary>
    public static readonly TimeSpan IgnoredGap = TimeSpan.FromMinutes(2);

    /// <summary>How far an end time may differ between the journal and the sealed audit before it is a disagreement.</summary>
    public static readonly TimeSpan CrossCheckTolerance = TimeSpan.FromMinutes(2);

    private static DateTimeOffset EffectiveEnd(MonitorRun r, bool latest, DateTimeOffset toUtc)
    {
        // a window still open (no clean stop) ends at its last heartbeat; but when it is the latest one and the period ends within two heartbeats of it, the monitor
        // is taken as running up to the end of the period (it is the application doing the control)
        var end = r.EndUtc ?? (latest && toUtc - r.LastBeatUtc <= DeviceJournalMerge.HeartbeatInterval * 2 ? (toUtc > r.LastBeatUtc ? toUtc : r.LastBeatUtc) : r.LastBeatUtc);
        return end < r.StartUtc ? r.StartUtc : end;
    }

    /// <summary>The coverage of [fromUtc, toUtc] by the runs, minus the intervals the monitor recorded as lost.</summary>
    public static CoverageResult Analyze(IEnumerable<MonitorRun> runs, IEnumerable<SealedGap>? lost, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc) return new CoverageResult([], true);
        var list = runs.OrderBy(r => r.StartUtc).ToList();
        var covered = new List<(DateTimeOffset S, DateTimeOffset E)>();
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var end = EffectiveEnd(r, i == list.Count - 1, toUtc);
            var s = r.StartUtc < fromUtc ? fromUtc : r.StartUtc;
            var e = end > toUtc ? toUtc : end;
            if (e > s) covered.Add((s, e));
        }
        // subtract the intervals the monitor recorded as lost
        foreach (var g in lost ?? [])
        {
            var next = new List<(DateTimeOffset S, DateTimeOffset E)>();
            foreach (var (s, e) in covered)
            {
                if (g.ToUtc <= s || g.FromUtc >= e) { next.Add((s, e)); continue; }
                if (g.FromUtc > s) next.Add((s, g.FromUtc));
                if (g.ToUtc < e) next.Add((g.ToUtc, e));
            }
            covered = next;
        }
        bool any = covered.Any(c => c.E > c.S);
        var gaps = new List<CoverageGap>();
        var cursor = fromUtc;
        foreach (var (s, e) in covered.OrderBy(x => x.S))
        {
            if (s - cursor > IgnoredGap) gaps.Add(new CoverageGap(cursor, s));
            if (e > cursor) cursor = e;
        }
        if (toUtc - cursor > IgnoredGap) gaps.Add(new CoverageGap(cursor, toUtc));
        // an interval the monitor itself recorded as lost always counts, however short (only a restart between two runs is ignored)
        foreach (var g in lost ?? [])
        {
            var s = g.FromUtc < fromUtc ? fromUtc : g.FromUtc;
            var e = g.ToUtc > toUtc ? toUtc : g.ToUtc;
            if (e > s) gaps.Add(new CoverageGap(s, e));
        }
        // merge what overlaps (a lost interval inside or across a gap between runs is one gap)
        var merged = new List<CoverageGap>();
        foreach (var g in gaps.OrderBy(x => x.FromUtc))
        {
            if (merged.Count > 0 && g.FromUtc <= merged[^1].ToUtc)
            {
                if (g.ToUtc > merged[^1].ToUtc) merged[^1] = merged[^1] with { ToUtc = g.ToUtc };
            }
            else merged.Add(g);
        }
        return new CoverageResult(merged, any);
    }

    /// <summary>The uncovered intervals only (see <see cref="Analyze"/>).</summary>
    public static IReadOnlyList<CoverageGap> Gaps(IEnumerable<MonitorRun> runs, DateTimeOffset fromUtc, DateTimeOffset toUtc, IEnumerable<SealedGap>? lost = null) =>
        Analyze(runs, lost, fromUtc, toUtc).Gaps;

    /// <summary>
    /// The disagreements between the journal's own run windows and the sealed ones inside the period (the journal is a plain file, so an edit that fakes coverage,
    /// or removes a window, shows up here): a journal window with no sealed counterpart, a sealed window the journal does not have, or a journal window that
    /// reaches further than the sealed records prove. Each finding is a sentence for the evidence list.
    /// </summary>
    public static IReadOnlyList<string> CrossCheck(IEnumerable<MonitorRun> journalRuns, IEnumerable<MonitorRun> sealedRuns, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var findings = new List<string>();
        var jr = journalRuns.OrderBy(r => r.StartUtc).ToList();
        var sr = sealedRuns.OrderBy(r => r.StartUtc).ToList();
        bool Overlaps(MonitorRun r, bool latest) => r.StartUtc <= toUtc && EffectiveEnd(r, latest, toUtc) >= fromUtc;
        static string T(DateTimeOffset t) => DeviceMonitorReport.UtcText(t);
        static MonitorRun? Match(MonitorRun r, List<MonitorRun> others) =>
            others.FirstOrDefault(o => r.RunId.Length > 0 && o.RunId.Length > 0 && o.RunId == r.RunId)
            ?? others.FirstOrDefault(o => (o.StartUtc - r.StartUtc).Duration() <= CrossCheckTolerance);
        for (int i = 0; i < jr.Count; i++)
        {
            var j = jr[i];
            if (!Overlaps(j, i == jr.Count - 1)) continue;
            var s = Match(j, sr);
            var jEnd = EffectiveEnd(j, false, toUtc);
            if (s is null)
            {
                findings.Add($"jurnalul monitorului arată o rulare ({T(j.StartUtc)} – {T(jEnd)}) care nu are înregistrare în jurnalul sigilat");
                continue;
            }
            var sEnd = EffectiveEnd(s, false, toUtc);
            if (jEnd - sEnd > DeviceJournalMerge.HeartbeatInterval * 2 + CrossCheckTolerance)
                findings.Add($"jurnalul monitorului arată rularea începută la {T(j.StartUtc)} până la {T(jEnd)}, jurnalul sigilat doar până la {T(sEnd)}");
        }
        for (int i = 0; i < sr.Count; i++)
        {
            var s = sr[i];
            if (!Overlaps(s, i == sr.Count - 1)) continue;
            if (Match(s, jr) is null)
                findings.Add($"jurnalul sigilat arată o rulare a monitorului începută la {T(s.StartUtc)} care lipsește din jurnalul monitorului (rând șters?)");
        }
        return findings;
    }
}
