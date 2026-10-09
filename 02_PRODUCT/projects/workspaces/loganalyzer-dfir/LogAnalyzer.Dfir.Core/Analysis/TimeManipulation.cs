using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>A change of the system clock found in Security 4616 or Kernel-General 1. Benign = time synchronization (W32Time runs in svchost.exe), the hardware clock or a time-zone change.</summary>
public sealed record ClockJump(string Source, string EventId, DateTimeOffset TimeUtc, DateTimeOffset? Previous, DateTimeOffset? New, TimeSpan? Delta,
    string Direction, string Actor, string Process, bool Benign, string BenignReason, EvidenceRef Evidence)
{
    /// <summary>"3h 12m înapoi"; empty when the event does not carry both times.</summary>
    public string Magnitude => Delta is not { } d || d == TimeSpan.Zero ? "" : $"{TimeManipulation.Human(d.Duration())} {Direction}";
}

/// <summary>A time-zone change: Kernel-General 22, Kernel-General 1 with Reason 3, or a 4616 that says so.</summary>
public sealed record ZoneChange(DateTimeOffset TimeUtc, string Source, string EventId, string Detail, EvidenceRef Evidence);

/// <summary>RecordID grew while the time went back by more than the tolerance, inside one channel of one file.</summary>
public sealed record RecordOrderInversion(string Source, string EvidenceId, long PreviousRecordId, long RecordId, DateTimeOffset PreviousTimeUtc, DateTimeOffset TimeUtc,
    TimeSpan Back, EvidenceRef Evidence);

/// <summary>An interval in which timestamps of the case cannot be trusted to order events (the WP4 TEMPORAL check reports UNKNOWN there, not CONTRADICTED).</summary>
public sealed record ManipulatedWindow(DateTimeOffset Start, DateTimeOffset End, string Kind, string Description, string EvidenceId, string Locator);

public sealed class TimeManipulationResult
{
    public List<ClockJump> Jumps { get; init; } = [];
    public List<ZoneChange> ZoneChanges { get; init; } = [];
    public List<RecordOrderInversion> Inversions { get; init; } = [];
    public List<ManipulatedWindow> Windows { get; init; } = [];

    public bool OverlapsAny(DateTimeOffset from, DateTimeOffset to) => Windows.Any(w => from <= w.End && to >= w.Start);
    public List<ManipulatedWindow> Overlapping(DateTimeOffset from, DateTimeOffset to) => Windows.Where(w => from <= w.End && to >= w.Start).ToList();
}

/// <summary>
/// WP15b: what the evidence shows about the system clock, without judging intent. Clock jumps keep their magnitude and direction; a
/// jump by time synchronization, the hardware clock or a zone change is recorded as benign and opens no window. Windows are
/// the intervals in which ordering by timestamp is unreliable. Extends AF05; it does not replace it.
/// </summary>
public static class TimeManipulation
{
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(1);
    /// <summary>Margin around a time-zone change in which local-time conversions of other sources are uncertain.</summary>
    public static readonly TimeSpan ZoneChangeMargin = TimeSpan.FromHours(1);

    private const string KernelGeneral = "Microsoft-Windows-Kernel-General";

    public static TimeManipulationResult Analyze(IReadOnlyList<TimelineEvent> events, TimeSpan? tolerance = null)
    {
        var tol = tolerance ?? DefaultTolerance;
        var r = new TimeManipulationResult();
        static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";
        static EvidenceRef Ref(TimelineEvent e, string d) => new(e.EvidenceId, e.Locator, d, e.SourceSha256);
        static bool Svchost(TimelineEvent e) => F(e, "ProcessName").EndsWith(@"\svchost.exe", StringComparison.OrdinalIgnoreCase);
        static DateTimeOffset? Parse(string s) =>
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

        foreach (var e in events.Where(x => x.Time.Utc is not null))
        {
            var at = e.Time.Utc!.Value;
            bool sec4616 = e.Source.Equals("EventLog:Security", StringComparison.OrdinalIgnoreCase) && e.EventId == "4616";
            bool kg = e.Source.Equals("EventLog:System", StringComparison.OrdinalIgnoreCase) && e.Provider.Equals(KernelGeneral, StringComparison.OrdinalIgnoreCase);
            bool kg1 = kg && e.EventId == "1";
            string reason = F(e, "Reason");
            if (kg && e.EventId == "22" || (kg1 || sec4616) && reason == "3")
            {
                r.ZoneChanges.Add(new(at, e.Source, e.EventId, kg && e.EventId == "22" ? "Kernel-General 22" : $"{(kg1 ? "Kernel-General 1" : "Security 4616")}, motiv 3 (fus orar)", Ref(e, "schimbare de fus orar")));
                r.Windows.Add(new(at - ZoneChangeMargin, at + ZoneChangeMargin, "TIMEZONE_CHANGE",
                        $"fusul orar a fost schimbat la {at:yyyy-MM-dd HH:mm} UTC ({e.Source["EventLog:".Length..]} {e.EventId}); conversiile orelor locale din jur sunt nesigure", e.EvidenceId, e.Locator));
                continue;
            }
            if (!(sec4616 || kg1)) continue;
            var prev = Parse(F(e, sec4616 ? "PreviousTime" : "OldTime"));
            var now = Parse(F(e, "NewTime"));
            TimeSpan? delta = prev is { } p && now is { } n ? n - p : null;
            bool benign; string why;
            if (Svchost(e)) { benign = true; why = "sincronizare de timp (svchost/W32Time)"; }
            else if (kg1 && reason == "2") { benign = true; why = "ceasul hardware"; }
            else if (kg1 && reason != "1") { benign = true; why = "motivul schimbării nu este „aplicație” (Reason 1)"; }
            else { benign = false; why = ""; }
            string dir = delta is not { } d ? "" : d < TimeSpan.Zero ? "înapoi" : d > TimeSpan.Zero ? "înainte" : "";
            var jump = new ClockJump(e.Source, e.EventId, at, prev, now, delta, dir, F(e, "SubjectUserName"), F(e, "ProcessName"), benign, why, Ref(e, "schimbare a orei sistemului"));
            r.Jumps.Add(jump);
            if (!benign && prev is { } a && now is { } b && a != b)
            {
                var (lo, hi) = a < b ? (a, b) : (b, a);
                r.Windows.Add(new(lo, hi, "CLOCK_JUMP",
                    $"ceasul a fost mutat {jump.Magnitude} ({a:yyyy-MM-dd HH:mm:ss} → {b:yyyy-MM-dd HH:mm:ss} UTC) de {(jump.Actor.Length > 0 ? jump.Actor : "cont necunoscut")}; orele din acest interval nu ordonează fiabil evenimentele",
                    e.EvidenceId, e.Locator));
            }
        }

        static long RecordId(TimelineEvent e) =>
            !e.Locator.StartsWith("EventRecordID=", StringComparison.Ordinal) || e.Locator.Contains("occurrence", StringComparison.Ordinal) ? -1
            : long.TryParse(e.Locator["EventRecordID=".Length..].Split(';')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
        foreach (var g in events.Where(e => e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) && e.Time.Utc is not null && RecordId(e) >= 0)
                                .GroupBy(e => (e.EvidenceId, e.Source)))
        {
            var ordered = g.OrderBy(RecordId).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                var back = ordered[i - 1].Time.Utc!.Value - ordered[i].Time.Utc!.Value;
                if (RecordId(ordered[i]) <= RecordId(ordered[i - 1]) || back <= tol) continue;
                var inv = new RecordOrderInversion(g.Key.Source, g.Key.EvidenceId, RecordId(ordered[i - 1]), RecordId(ordered[i]), ordered[i - 1].Time.Utc!.Value, ordered[i].Time.Utc!.Value, back,
                    Ref(ordered[i], "înregistrare cu oră mai veche decât cea precedentă"));
                r.Inversions.Add(inv);
                r.Windows.Add(new(inv.TimeUtc, inv.PreviousTimeUtc, "RECORD_ORDER_INVERSION",
                    $"{g.Key.Source["EventLog:".Length..]}: RecordID {inv.PreviousRecordId} → {inv.RecordId} dar ora a coborât cu {Human(back)} ({inv.PreviousTimeUtc:yyyy-MM-dd HH:mm:ss} → {inv.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC)",
                    inv.EvidenceId, ordered[i].Locator));
            }
        }
        return r;
    }

    public static string Human(TimeSpan d)
    {
        var parts = new List<string>();
        if (d.Days > 0) parts.Add($"{d.Days}d");
        if (d.Hours > 0) parts.Add($"{d.Hours}h");
        if (d.Minutes > 0) parts.Add($"{d.Minutes}m");
        if (parts.Count == 0 || d.Days == 0 && d.Hours == 0 && d.Minutes < 5 && d.Seconds > 0) parts.Add($"{d.Seconds}s");
        return string.Join(" ", parts);
    }
}
