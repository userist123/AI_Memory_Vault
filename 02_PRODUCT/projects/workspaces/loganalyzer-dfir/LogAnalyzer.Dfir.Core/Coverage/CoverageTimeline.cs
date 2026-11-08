using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Coverage;

/// <summary>One source's events per day over the shown period, and the empty days inside the period the source was writing.</summary>
public sealed class CoverageTimelineRow
{
    public string Source { get; init; } = "";
    public int[] PerDay { get; init; } = [];
    public int Total => PerDay.Sum();
    public DateTimeOffset? FirstUtc { get; init; }
    public DateTimeOffset? LastUtc { get; init; }
    /// <summary>Runs of empty days strictly between the first and the last day with events (start day index, length).</summary>
    public List<(int Start, int Days)> Gaps { get; } = [];
}

/// <summary>
/// Master plan 6.4, coverage in time: for each source of the timeline, how many events fall on each day of the period, and where a source that
/// was writing before and after has silent days. A silent day is a CANDIDATE gap, never proof of deletion: the station may have been off, the
/// channel quiet, or the log rolled over (the first day of a source is often where retention cut it). Events without a UTC time are counted apart.
/// </summary>
public sealed class CoverageTimeline
{
    public const int MaxDays = 400;

    public DateOnly FirstDay { get; init; }
    public int Days { get; init; }
    public List<CoverageTimelineRow> Rows { get; } = [];
    public int WithoutTime { get; init; }
    public bool Truncated { get; init; }

    public DateOnly Day(int i) => FirstDay.AddDays(i);

    /// <summary>Gaps shorter than <paramref name="minGapDays"/> days are not listed (a quiet weekend is normal).</summary>
    public static CoverageTimeline Build(IReadOnlyList<TimelineEvent> timeline, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, int minGapDays = 2)
    {
        var timed = timeline.Where(e => e.Time.Utc is not null).ToList();
        if (timed.Count == 0) return new CoverageTimeline { FirstDay = DateOnly.FromDateTime(DateTime.UtcNow), Days = 0, WithoutTime = timeline.Count };
        var lo = fromUtc ?? timed.Min(e => e.Time.Utc!.Value);
        var hi = toUtc ?? timed.Max(e => e.Time.Utc!.Value);
        if (hi < lo) (lo, hi) = (hi, lo);
        var first = DateOnly.FromDateTime(lo.UtcDateTime);
        int days = DateOnly.FromDateTime(hi.UtcDateTime).DayNumber - first.DayNumber + 1;
        bool truncated = days > MaxDays;
        if (truncated) { first = DateOnly.FromDateTime(hi.UtcDateTime).AddDays(-(MaxDays - 1)); days = MaxDays; }   // the most recent part is kept
        var c = new CoverageTimeline { FirstDay = first, Days = days, WithoutTime = timeline.Count - timed.Count, Truncated = truncated };
        foreach (var g in timed.GroupBy(e => e.Source, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()))
        {
            var per = new int[days];
            foreach (var e in g)
            {
                int i = DateOnly.FromDateTime(e.Time.Utc!.Value.UtcDateTime).DayNumber - first.DayNumber;
                if (i >= 0 && i < days) per[i]++;
            }
            var row = new CoverageTimelineRow { Source = g.Key, PerDay = per, FirstUtc = g.Min(e => e.Time.Utc), LastUtc = g.Max(e => e.Time.Utc) };
            int firstActive = Array.FindIndex(per, x => x > 0), lastActive = Array.FindLastIndex(per, x => x > 0);
            for (int i = firstActive + 1; firstActive >= 0 && i < lastActive; i++)
            {
                if (per[i] > 0) continue;
                int j = i;
                while (j < lastActive && per[j] == 0) j++;
                if (j - i >= minGapDays) row.Gaps.Add((i, j - i));
                i = j;
            }
            c.Rows.Add(row);
        }
        return c;
    }

    /// <summary>The candidate gaps in words, with the reasons that are not tampering.</summary>
    public IEnumerable<string> GapLines() =>
        Rows.SelectMany(r => r.Gaps.Select(g =>
            $"{r.Source}: nicio înregistrare între {Day(g.Start):yyyy-MM-dd} și {Day(g.Start + g.Days - 1):yyyy-MM-dd} ({g.Days} zile), deși sursa scria înainte și după — " +
            "lacună POSIBILĂ (CANDIDATE): stația oprită, canal fără activitate, jurnal golit sau suprascris; se confirmă cu alte surse."));
}
