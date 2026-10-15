using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>The completeness verdict of one event-log channel: <see cref="Complete"/> true / false, or null when it cannot be judged (then <see cref="Detail"/> is a „sursă lipsă: …”).</summary>
public sealed record EvtxCompletenessResult(string Channel, bool? Complete, string Detail, IReadOnlyList<string> MissingIntervals)
{
    public string Text => Complete switch
    {
        true => $"Jurnalul {Channel}: complet ({Detail})",
        false => $"Jurnalul {Channel}: incomplet ({Detail})",
        _ => $"Jurnalul {Channel}: completitudine nedeterminată ({Detail})",
    };
}

/// <summary>
/// EVTX completeness check (owner directive 2026-10-16, item 2d), instead of the blanket „jurnalele nu pot dovedi că jurnalul e complet”. For one channel, over the
/// records of the case timeline:
/// <list type="bullet">
/// <item>EventRecordID continuity per evidence file: every hole is an interval of removed or lost records (with the times around it);</item>
/// <item>clearing: Security 1102 (Security cleared), System 104 naming the channel (another log cleared): the records before it are gone from this file;</item>
/// <item>Security 1100 (event-log service shut down): no record is written while the service is down (normal at system shutdown, so listed, not judged);</item>
/// <item>overwrite: when the oldest record is newer than the start of the controlled period, the log wrapped (or was cleared) and the period is only partly covered.</item>
/// </list>
/// The file header (record count, dirty / full flags, truncation, corrupt chunks) is checked by the EVTX parser and reported as an evidence gap of the file.
/// Output: „complet / incomplet (motiv, interval lipsă)”. Nothing is guessed: with no record of the channel in the case the verdict is undetermined, with the source named.
/// </summary>
public static class EvtxCompleteness
{
    public const string Basis = "verificarea completitudinii jurnalului (numerotarea EventRecordID, golire 1102/104, oprire 1100, suprascriere față de începutul perioadei)";

    private static long RecordId(TimelineEvent e)
    {
        if (!e.Locator.StartsWith("EventRecordID=", StringComparison.Ordinal)) return -1;
        var s = e.Locator.AsSpan(14);
        int semi = s.IndexOf(';');
        if (semi >= 0) return -1;                     // a repeated id (";occurrence=n") is not part of the sequence
        return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : -1;
    }

    private static string Time(DateTimeOffset? t) => t is { } x ? x.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "oră necunoscută";

    /// <summary>The channel of a timeline row ("" when the row is not from an event log).</summary>
    public static string ChannelOf(TimelineEvent e) => e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) ? e.Source[9..] : "";

    public static EvtxCompletenessResult Assess(string channel, IReadOnlyList<TimelineEvent> timeline, DateTimeOffset? periodFromUtc)
    {
        var evs = timeline.Where(e => ChannelOf(e).Equals(channel, StringComparison.OrdinalIgnoreCase)).ToList();
        if (evs.Count == 0)
            return new(channel, null, $"sursă lipsă: jurnalul {channel}, de ce lipsește: nu are nicio înregistrare în caz (canal necolectat, oprit sau gol), " +
                                      $"cum se pornește: colectarea cu drepturi de administrator și activarea canalului (wevtutil sl \"{channel}\" /e:true)", []);
        var reasons = new List<string>();
        var missing = new List<string>();
        bool incomplete = false;

        // 1. EventRecordID continuity, per file.
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

        // 2. Clearing of this log.
        bool security = channel.Equals("Security", StringComparison.OrdinalIgnoreCase);
        var clears = timeline.Where(e => security && ChannelOf(e).Equals("Security", StringComparison.OrdinalIgnoreCase) && e.EventId == "1102"
                                         || ChannelOf(e).Equals("System", StringComparison.OrdinalIgnoreCase) && e.EventId == "104" &&
                                            (e.Fields.TryGetValue("Channel", out var ch) || e.Fields.TryGetValue("LogName", out ch)) && ch.Equals(channel, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(e => e.Time.Utc).ToList();
        foreach (var c in clears)
        {
            incomplete = true;
            missing.Add($"tot ce era în jurnal înainte de golirea din {Time(c.Time.Utc)} ({(c.EventId == "1102" ? "Security 1102" : "System 104")})");
        }
        if (clears.Count > 0) reasons.Add($"jurnalul a fost golit de {clears.Count} {(clears.Count == 1 ? "dată" : "ori")}");

        // 3. Event-log service shut down (Security 1100): listed, not judged.
        var stops = security ? evs.Count(e => e.EventId == "1100") : 0;
        if (stops > 0) reasons.Add($"serviciul de jurnalizare s-a oprit de {stops} {(stops == 1 ? "dată" : "ori")} (1100; normal la închiderea sistemului, cât timp e oprit nu se scrie nimic)");

        // 4. Overwrite against the controlled period.
        var oldest = evs.Where(e => e.Time.Utc is not null).OrderBy(e => e.Time.Utc).FirstOrDefault();
        long firstId = evs.Select(RecordId).Where(x => x > 0).DefaultIfEmpty(0).Min();
        if (periodFromUtc is { } p && oldest?.Time.Utc is { } o && o > p)
        {
            incomplete = true;
            missing.Add($"{Time(p)} – {Time(o)} (începutul perioadei controlate până la cea mai veche înregistrare păstrată)");
            reasons.Add(firstId > 1
                ? $"cea mai veche înregistrare păstrată (RecordID {firstId}, {Time(o)}) este după începutul perioadei: jurnalul a fost suprascris (circular) sau golit, perioada este acoperită parțial"
                : $"cea mai veche înregistrare ({Time(o)}) este după începutul perioadei: jurnalul nu acoperă începutul perioadei");
        }
        else if (periodFromUtc is null)
            reasons.Add("perioada controlată nu este stabilită, deci acoperirea ei nu se poate judeca; s-au verificat doar numerotarea și golirile");
        else reasons.Add($"cea mai veche înregistrare ({Time(oldest?.Time.Utc)}) acoperă începutul perioadei");

        if (!incomplete) reasons.Insert(0, $"{numbered} înregistrări numerotate continuu, nicio golire");
        var detail = string.Join("; ", reasons) + (missing.Count > 0 ? "; interval lipsă: " + string.Join("; ", missing.Take(10)) + (missing.Count > 10 ? $"; încă {missing.Count - 10}" : "") : "");
        return new(channel, !incomplete, detail, missing);
    }
}
