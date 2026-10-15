using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// The completeness verdict of one event-log channel: <see cref="Complete"/> true („complet”), false („incomplet”, with the reason and the missing interval),
/// or null („neevaluat”: it cannot be judged, and <see cref="Detail"/> says why, as „sursă lipsă: …” when a source is missing).
/// </summary>
public sealed record EvtxCompletenessResult(string Channel, bool? Complete, string Detail, IReadOnlyList<string> MissingIntervals)
{
    public string Text => Complete switch
    {
        true => $"Jurnalul {Channel}: complet ({Detail})",
        false => $"Jurnalul {Channel}: incomplet ({Detail})",
        _ => $"Jurnalul {Channel}: neevaluat ({Detail})",
    };
}

/// <summary>
/// EVTX completeness check (owner directive 2026-10-16, item 2d; tightened after the review of PR #288). For one channel, over the records of the case timeline:
/// <list type="bullet">
/// <item>EventRecordID continuity per evidence file: every hole is an interval of removed or lost records (incomplete, whatever the period);</item>
/// <item>clearing: Security 1102, or System 104 naming the channel, AT OR AFTER the start of the controlled period (a clear long before the period, with the period
/// fully covered after it, does not make the period incomplete);</item>
/// <item>start: the oldest record must be at or before the start of the period, otherwise the log wrapped or was cleared and the start is missing;</item>
/// <item>end: the newest record must reach the end of the period (or the collection time) within <see cref="TailTolerance"/>. For a channel that always writes
/// (Security, System, Application) a shorter tail means the channel was stopped or the log service killed: incomplete. A quiet channel may simply have had
/// nothing to write, so its tail cannot be confirmed: neevaluat;</item>
/// <item>Security 1100 (service shut down) is listed, not judged (it is normal at shutdown).</item>
/// </list>
/// „Complet” is said only when the period is known and every condition holds. Without a period the verdict is „neevaluat” (a gap still makes it „incomplet”).
/// The file header (truncation, corrupt chunks, repeated ids) is checked by the EVTX parser and reported as an evidence gap of the file.
/// </summary>
public static class EvtxCompleteness
{
    public const string Basis = "verificarea completitudinii jurnalului (numerotarea EventRecordID, golire 1102/104 în perioadă, început și sfârșit față de perioada controlată)";
    public static readonly TimeSpan TailTolerance = TimeSpan.FromDays(1);
    /// <summary>Channels Windows writes to all the time while it runs: a tail that stops early means the log was stopped.</summary>
    public static readonly IReadOnlyList<string> AlwaysActiveChannels = ["Security", "System", "Application"];

    private static long RecordId(TimelineEvent e)
    {
        if (!e.Locator.StartsWith("EventRecordID=", StringComparison.Ordinal)) return -1;
        var s = e.Locator.AsSpan(14);
        if (s.IndexOf(';') >= 0) return -1;                     // a repeated id (";occurrence=n") is not part of the sequence
        return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : -1;
    }

    private static string Time(DateTimeOffset? t) => t is { } x ? x.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "oră necunoscută";

    /// <summary>The channel of a timeline row ("" when the row is not from an event log).</summary>
    public static string ChannelOf(TimelineEvent e) => e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) ? e.Source[9..] : "";

    /// <param name="periodToUtc">End of the controlled period, or the collection time when the period is open; null = unknown (the tail is then not judged).</param>
    public static EvtxCompletenessResult Assess(string channel, IReadOnlyList<TimelineEvent> timeline, DateTimeOffset? periodFromUtc, DateTimeOffset? periodToUtc = null)
    {
        var evs = timeline.Where(e => ChannelOf(e).Equals(channel, StringComparison.OrdinalIgnoreCase)).ToList();
        if (evs.Count == 0)
            return new(channel, null, $"sursă lipsă: jurnalul {channel}, de ce lipsește: nu are nicio înregistrare în caz (canal necolectat, oprit sau gol), " +
                                      $"cum se pornește: colectarea cu drepturi de administrator și activarea canalului (wevtutil sl \"{channel}\" /e:true)", []);
        var reasons = new List<string>();
        var missing = new List<string>();
        bool incomplete = false, undetermined = false;

        // 1. EventRecordID continuity, per file: a hole is missing records whatever the period.
        int numbered = 0;
        foreach (var file in evs.GroupBy(e => e.EvidenceId))
        {
            var ordered = file.Select(e => (e, id: RecordId(e))).Where(x => x.id > 0).OrderBy(x => x.id).ToList();
            numbered += ordered.Count;
            for (int i = 1; i < ordered.Count; i++)
            {
                long diff = ordered[i].id - ordered[i - 1].id;
                if (diff <= 1) continue;
                incomplete = true;
                missing.Add($"RecordID {ordered[i - 1].id + 1}–{ordered[i].id - 1} ({diff - 1} înregistrări) între {Time(ordered[i - 1].e.Time.Utc)} și {Time(ordered[i].e.Time.Utc)}");
            }
        }
        if (numbered == 0)
            return new(channel, null, $"sursă lipsă: numerele EventRecordID ale jurnalului {channel}, de ce lipsește: rândurile din caz nu poartă numărul înregistrării (export fără număr), " +
                                      "cum se pornește: colectarea fișierului .evtx original cu aplicația (EvtExportLog)", []);
        if (missing.Count > 0) reasons.Add($"{missing.Count} goluri în numerotarea înregistrărilor (înregistrări șterse sau pierdute)");

        // 2. Clearing of this log, judged only at or after the period start (without a period: listed, the verdict stays neevaluat).
        bool security = channel.Equals("Security", StringComparison.OrdinalIgnoreCase);
        var clears = timeline.Where(e => security && ChannelOf(e).Equals("Security", StringComparison.OrdinalIgnoreCase) && e.EventId == "1102"
                                         || ChannelOf(e).Equals("System", StringComparison.OrdinalIgnoreCase) && e.EventId == "104" &&
                                            (e.Fields.TryGetValue("Channel", out var ch) || e.Fields.TryGetValue("LogName", out ch)) && ch.Equals(channel, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(e => e.Time.Utc).ToList();
        var relevantClears = periodFromUtc is { } ps ? clears.Where(c => c.Time.Utc is not { } t || t >= ps).ToList() : clears;
        foreach (var c in relevantClears)
        {
            if (periodFromUtc is not null) incomplete = true;
            missing.Add($"tot ce era în jurnal înainte de golirea din {Time(c.Time.Utc)} ({(c.EventId == "1102" ? "Security 1102" : "System 104")})");
        }
        if (relevantClears.Count > 0) reasons.Add($"jurnalul a fost golit de {relevantClears.Count} {(relevantClears.Count == 1 ? "dată" : "ori")}{(periodFromUtc is null ? "" : " în perioada controlată")}");
        if (clears.Count > relevantClears.Count) reasons.Add($"{clears.Count - relevantClears.Count} goliri înainte de perioada controlată (nu afectează perioada)");

        // 3. Event-log service shut down (Security 1100): listed, not judged.
        var stops = security ? evs.Count(e => e.EventId == "1100") : 0;
        if (stops > 0) reasons.Add($"serviciul de jurnalizare s-a oprit de {stops} {(stops == 1 ? "dată" : "ori")} (1100; normal la închiderea sistemului, cât timp e oprit nu se scrie nimic)");

        // 4. Start of the period.
        var timed = evs.Where(e => e.Time.Utc is not null).OrderBy(e => e.Time.Utc).ToList();
        var oldest = timed.FirstOrDefault()?.Time.Utc;
        var newest = timed.LastOrDefault()?.Time.Utc;
        long firstId = evs.Select(RecordId).Where(x => x > 0).DefaultIfEmpty(0).Min();
        if (periodFromUtc is not { } p)
        {
            undetermined = true;
            reasons.Add("perioada controlată nu este stabilită, deci acoperirea ei nu se poate judeca");
        }
        else if (oldest is { } o && o > p)
        {
            incomplete = true;
            missing.Add($"{Time(p)} – {Time(o)} (începutul perioadei controlate până la cea mai veche înregistrare păstrată)");
            reasons.Add(firstId > 1
                ? $"cea mai veche înregistrare păstrată (RecordID {firstId}, {Time(o)}) este după începutul perioadei: jurnalul a fost suprascris (circular) sau golit"
                : $"cea mai veche înregistrare ({Time(o)}) este după începutul perioadei: jurnalul nu acoperă începutul perioadei");
        }
        else reasons.Add($"cea mai veche înregistrare ({Time(oldest)}) acoperă începutul perioadei");

        // 5. End of the period.
        bool active = AlwaysActiveChannels.Contains(channel, StringComparer.OrdinalIgnoreCase);
        if (periodToUtc is not { } end)
        {
            undetermined = true;
            reasons.Add("sfârșitul perioadei (sau ora colectării) nu este cunoscut, deci nu se poate confirma că jurnalul ajunge până la el");
        }
        else if (newest is { } n && n < end - TailTolerance)
        {
            if (active)
            {
                incomplete = true;
                missing.Add($"{Time(n)} – {Time(end)} (de la ultima înregistrare până la sfârșitul perioadei)");
                reasons.Add($"ultima înregistrare ({Time(n)}) este cu mult înainte de sfârșitul perioadei ({Time(end)}) pe un jurnal în care Windows scrie permanent: jurnalul a fost oprit sau serviciul de jurnalizare a fost oprit");
            }
            else
            {
                undetermined = true;
                reasons.Add($"ultima înregistrare este din {Time(n)}, înainte de sfârșitul perioadei ({Time(end)}): canalul poate să nu fi avut nimic de scris sau poate fi fost oprit, deci sfârșitul nu se poate confirma");
            }
        }
        else reasons.Add($"ultima înregistrare ({Time(newest)}) ajunge la sfârșitul perioadei");

        bool? verdict = incomplete ? false : undetermined ? null : true;
        if (verdict == true) reasons.Insert(0, $"{numbered} înregistrări numerotate continuu, nicio golire în perioadă");
        var detail = string.Join("; ", reasons) + (missing.Count > 0 ? "; interval lipsă: " + string.Join("; ", missing.Take(10)) + (missing.Count > 10 ? $"; încă {missing.Count - 10}" : "") : "");
        return new(channel, verdict, detail, missing);
    }
}
