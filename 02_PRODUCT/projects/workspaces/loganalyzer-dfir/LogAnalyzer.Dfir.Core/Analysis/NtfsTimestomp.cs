using System.Globalization;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// The timestomp test over the $MFT events (source "MFT") and the USN events (source "USN"), shared by the finding NTFS-TIMESTOMP and the check AF08.
/// Calibrated on a real Windows 11 volume (Corpus/NtfsCalib, 2026-11-13): SetFileTime timestomps (A-E) against Copy-Item, robocopy, Move-Item,
/// Expand-Archive and tar (F-K). The order of USN operations comes from the Usn number, never from the time (fsutil prints local times that are
/// converted with a zone the evidence may not state); times are used only for the 24 h margin of the rename path.
/// HIGH, mismatch path: $SI Created is more than 60 s before $FN Created (the real creation time), without the copy signature (COPIED_CHANGE_TIME), and
/// the USN has, after the creation operation, a separate operation with only "Basic info change".
/// HIGH, rename path ($SI == $FN: Windows copied $SI into $FN on rename or move): USN creation, then a separate "Basic info change", then "Rename: new name",
/// and $SI Created more than 24 h before the creation operation (covers any zone error and the ambiguous DST hour).
/// Both High paths: the "Basic info change" operation holds no rename and is closed (lower Usn) before the "Rename: new name" operation; both are dropped
/// (counted as tunnelling, <see cref="Outcome.TunnelExplained"/>) when the final name was released by another file just before (see TunnelSignature).
/// MEDIUM: any other mismatch without COPIED_CHANGE_TIME, with or without USN, when the record has no short (8.3) name; LOW when it has one (tunnelling possible).
/// Not seen: tools that also change $FN or the MFT change time; without USN, a timestomp followed by a rename or a move.
/// </summary>
public static class NtfsTimestomp
{
    public const string RuleId = "NTFS-TIMESTOMP";
    public const string MftSource = "MFT", UsnSource = "USN";
    private static readonly long Tolerance = MftParser.CreatedToleranceTicks;
    private static readonly DateTime Min = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc), Max = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public enum Level { Low, Medium, High }
    public enum Kind { Mismatch, Rename }

    /// <param name="Mft">The $MFT event the $SI / $FN times come from.</param>
    /// <param name="UsnCreate">The USN event of the creation operation (High only).</param>
    /// <param name="UsnChange">The separate USN "Basic info change" operation after the creation (High only).</param>
    /// <param name="UsnRename">The USN "Rename: new name" operation after it (rename path only).</param>
    /// <param name="HasUsn">The journal has records for this file.</param>
    /// <param name="DosName">The $MFT record has a short (8.3) name: tunnelling can explain a mismatch.</param>
    public sealed record Hit(Level Level, Kind Kind, TimelineEvent Mft, TimelineEvent? UsnCreate, TimelineEvent? UsnChange, TimelineEvent? UsnRename, DateTime SiCreated, DateTime FnCreated, DateTime? CreateAt, bool HasUsn, bool DosName = false)
    {
        public string Path => Mft.Path;
        public string Key => Mft.Fields.TryGetValue("FileId", out var v) ? NormalizeId(v) : "";
    }

    /// <summary>The ID without "0x", lower case, without leading zeros: the same file number and sequence however each source wrote it.</summary>
    public static string NormalizeId(string id)
    {
        var s = id.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return s.TrimStart('0').ToLowerInvariant();
    }

    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";

    /// <summary>The ISO text MftParser writes (100 ns); null for "raw:n" and anything else.</summary>
    private static DateTime? ParseMft(string text)
        => DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) && t >= Min && t < Max ? t : null;

    private static DateTime? UsnTime(TimelineEvent e) => e.Time.Utc is { } u && u.UtcDateTime >= Min && u.UtcDateTime < Max ? u.UtcDateTime : null;

    private static string[] Reasons(TimelineEvent e) => F(e, "Reason").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when the timeline has an $MFT analysis (its summary event).</summary>
    public static bool HasMft(IEnumerable<TimelineEvent> events) => events.Any(e => e.Source == MftSource && F(e, "Category") == "summary");

    /// <param name="Hits">The findings.</param>
    /// <param name="TunnelExplained">Candidates (High by the other tests) whose final name was released a moment before by another file: NTFS tunnelling, not reported.</param>
    public sealed record Outcome(List<Hit> Hits, int TunnelExplained);

    public static List<Hit> Detect(IEnumerable<TimelineEvent> events) => Analyze(events).Hits;

    private const long TunnelWindowTicks = 15 * TimeSpan.TicksPerSecond;

    private static bool Has(TimelineEvent e, string reason) => Reasons(e).Contains(reason, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// NTFS tunnelling (only with 8.3 names on the volume): a file that takes the name another file released a moment before inherits that file's creation time.
    /// True when an operation that gave this file its final name (File create or Rename: new name) was preceded, in Usn order and, where both times are known,
    /// by at most 15 s (a difference between two times of the same journal, independent of the zone), by the release of that name (case-insensitive, same directory)
    /// by ANOTHER file. The release is seen (1) directly: "Rename: old name" or "File delete" of the same name; or (2) from the closed operations only
    /// (the parser keeps only Close records, and the record that says "Rename: old name" has no Close): the other file had an operation under that name, and later,
    /// before the candidate took the name, an operation "File delete" or "Rename: new name" to a different name.
    /// </summary>
    private static bool TunnelSignature(string key, List<(TimelineEvent E, long N)> ops, Dictionary<string, List<(TimelineEvent E, long N)>> byFile)
    {
        foreach (var op in ops)
        {
            if (!Has(op.E, "File create") && !Reasons(op.E).Any(x => x.StartsWith("Rename: new name", StringComparison.OrdinalIgnoreCase))) continue;
            string name = F(op.E, "FileName"), parent = NormalizeId(F(op.E, "ParentFileId"));
            if (name.Length == 0 || parent.Length == 0) continue;
            var t = UsnTime(op.E);
            bool Near(TimelineEvent release) => !(t is { } t1 && UsnTime(release) is { } t0 && t1.Ticks - t0.Ticks > TunnelWindowTicks);
            bool SameName(TimelineEvent e) => string.Equals(F(e, "FileName"), name, StringComparison.OrdinalIgnoreCase) && NormalizeId(F(e, "ParentFileId")) == parent;
            foreach (var (id, other) in byFile)
            {
                if (id == key) continue;
                foreach (var r in other)
                {
                    if (r.N >= op.N || !Near(r.E)) continue;
                    if (SameName(r.E) && (Has(r.E, "File delete") || Has(r.E, "Rename: old name"))) return true;   // (1)
                    bool releases = Has(r.E, "File delete") || Reasons(r.E).Any(x => x.StartsWith("Rename: new name", StringComparison.OrdinalIgnoreCase)) && !string.Equals(F(r.E, "FileName"), name, StringComparison.OrdinalIgnoreCase);
                    if (releases && other.Any(h => h.N < r.N && SameName(h.E))) return true;   // (2)
                }
            }
        }
        return false;
    }

    public static Outcome Analyze(IEnumerable<TimelineEvent> events)
    {
        var mftByFile = new Dictionary<string, List<TimelineEvent>>(StringComparer.Ordinal);
        var usnByFile = new Dictionary<string, List<TimelineEvent>>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (e.Source == MftSource && F(e, "Category") != "summary" && F(e, "FileId") is { Length: > 0 } mid && NormalizeId(mid) is { Length: > 0 } mk)
                (mftByFile.TryGetValue(mk, out var l) ? l : mftByFile[mk] = []).Add(e);
            else if (e.Source == UsnSource && F(e, "FileId") is { Length: > 0 } uid && NormalizeId(uid) is { Length: > 0 } uk)
                (usnByFile.TryGetValue(uk, out var l2) ? l2 : usnByFile[uk] = []).Add(e);
        }

        // every file's numbered operations (a record without a readable Usn cannot be ordered, so it proves nothing)
        var byFile = usnByFile.ToDictionary(kv => kv.Key, kv => kv.Value
            .Select(e => (E: e, N: long.TryParse(F(e, "Usn"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (long?)null))
            .Where(x => x.N is not null).Select(x => (x.E, N: x.N!.Value)).OrderBy(x => x.N).ToList(), StringComparer.Ordinal);

        var hits = new List<Hit>();
        int tunnel = 0;
        foreach (var (key, group) in mftByFile)
        {
            // the same record in two evidence items (or a hard link) is one file: the first event whose times can be read decides
            TimelineEvent? mft = null; DateTime si = default, fn = default, chg = default; bool changeKnown = false;
            foreach (var e in group.OrderBy(e => e.EvidenceId, StringComparer.Ordinal).ThenBy(e => e.Locator, StringComparer.Ordinal))
            {
                if (ParseMft(F(e, "SiCreated")) is { } s && ParseMft(F(e, "FnCreated")) is { } f) { mft = e; si = s; fn = f; if (ParseMft(F(e, "SiMftModified")) is { } c) { chg = c; changeKnown = true; } break; }
            }
            if (mft is null) continue;
            if (changeKnown && chg < fn) continue;   // COPIED_CHANGE_TIME: robocopy /COPY:DAT, Copy-Item (the source's change time was copied)

            bool mismatch = fn.Ticks - si.Ticks > Tolerance;
            bool sameTimes = Math.Abs(fn.Ticks - si.Ticks) <= TimeSpan.TicksPerSecond;   // Windows copied $SI into $FN (rename, move)
            usnByFile.TryGetValue(key, out var usn);
            // the operations of the file in journal order: the Usn number (a record without a readable number cannot be ordered, so it proves nothing)
            var numbered = (usn ?? []).Select(e => (E: e, N: long.TryParse(F(e, "Usn"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (long?)null))
                .Where(x => x.N is not null).Select(x => (x.E, N: x.N!.Value)).OrderBy(x => x.N).ToList();
            var ops = numbered.Select(x => x.E).ToList();
            bool dos = F(mft, "HasDosName") == "true";
            var create = ops.FirstOrDefault(e => Reasons(e).Contains("File create", StringComparer.OrdinalIgnoreCase));
            TimelineEvent? change = null, rename = null;
            if (create is not null)
            {
                int ci = ops.IndexOf(create);
                int bi = ops.FindIndex(ci + 1, IsBasicInfoOnly);
                if (bi >= 0)
                {
                    change = ops[bi];
                    int ri = ops.FindIndex(bi + 1, e => Reasons(e).Any(x => x.StartsWith("Rename: new name", StringComparison.OrdinalIgnoreCase)));
                    if (ri >= 0) rename = ops[ri];
                }
            }
            bool hasUsn = usn is { Count: > 0 };
            bool highMismatch = mismatch && change is not null;
            bool highRename = !mismatch && sameTimes && change is not null && rename is not null && UsnTime(create!) is { } at0 && si.Ticks < at0.Ticks - TimeSpan.TicksPerDay;
            if ((highMismatch || highRename) && TunnelSignature(key, numbered, byFile)) { tunnel++; continue; }
            if (highMismatch)
                hits.Add(new Hit(Level.High, Kind.Mismatch, mft, create, change, null, si, fn, UsnTime(create!), hasUsn, dos));
            else if (highRename)
                hits.Add(new Hit(Level.High, Kind.Rename, mft, create, change, rename, si, fn, UsnTime(create!), hasUsn, dos));
            else if (mismatch)
            {
                // Tunneling (8.3 names only, calibration L2 / N2) inherits the CREATION time alone: the saved file is written now, so its $SI
                // modification time is new. A modification time also far before $FN creation is not explained by tunneling (full stomp, A).
                bool modifiedAlsoOld = ParseMft(F(mft, "SiModified")) is { } siM && fn.Ticks - siM.Ticks > Tolerance;
                hits.Add(new Hit(dos && !modifiedAlsoOld ? Level.Low : Level.Medium, Kind.Mismatch, mft, create, null, null, si, fn, null, hasUsn, dos));
            }
        }
        return new Outcome(hits.OrderByDescending(h => h.Level).ThenBy(h => h.Path, StringComparer.Ordinal).ThenBy(h => h.Key, StringComparer.Ordinal).ToList(), tunnel);
    }

    /// <summary>An operation (one Close record) that only changed basic information: no data written, no creation, no rename (an operation that also renames is not a separate change).</summary>
    private static bool IsBasicInfoOnly(TimelineEvent e)
    {
        var r = Reasons(e);
        return r.Contains("Basic info change", StringComparer.OrdinalIgnoreCase)
            && !r.Any(x => x.StartsWith("Data", StringComparison.OrdinalIgnoreCase) || x.StartsWith("Rename", StringComparison.OrdinalIgnoreCase) || x.Equals("File create", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Sum of a numeric field of the MftParser summary events; (value, true) when every summary carried it.</summary>
    public static long Sum(IEnumerable<TimelineEvent> events, string field)
        => events.Where(e => e.Source == MftSource && F(e, "Category") == "summary").Sum(e => long.TryParse(F(e, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0);

    /// <summary>True when the MftParser caps left mismatch or recent records out of the events (nothing is known about those).</summary>
    public static bool Capped(IEnumerable<TimelineEvent> events)
        => Sum(events, "MismatchTotal") > Sum(events, "MismatchEmitted") || Sum(events, "RecentTotal") > Sum(events, "RecentEmitted");

    /// <summary>
    /// Why "nothing found" cannot be said (null = it can): the "recent" category was not emitted (no acquisition date), a cap cut mismatch or recent records,
    /// or the USN times are not inside the 30 days before acquisition (files whose $SI and $FN were made equal by a rename are reached only through "recent").
    /// </summary>
    public static string? Undetermined(IReadOnlyList<TimelineEvent> mftEvents, IReadOnlyList<TimelineEvent> usn)
    {
        var summaries = mftEvents.Where(e => F(e, "Category") == "summary").ToList();
        if (summaries.Any(e => F(e, "RecentCategory") != "emisă"))
            return "categoria fișierelor recente nu a fost emisă (proba nu are data achiziției), deci fișierele cu orele $SI și $FILE_NAME egale după o redenumire nu au fost verificate";
        if (Capped(mftEvents)) return "plafoanele de emitere au lăsat neemise nepotriviri sau fișiere recente, deci nu se poate spune că nu există";
        if (usn.Count > 0)
        {
            var times = usn.Select(UsnTime).Where(t => t is not null).Select(t => t!.Value).ToList();
            var acquired = summaries.Select(e => e.Time.Utc).Where(t => t is not null).Select(t => t!.Value.UtcDateTime).DefaultIfEmpty().Max();
            if (times.Count == 0) return "orele jurnalului USN sunt necunoscute, deci nu se poate verifica dacă fișierele lui sunt în fereastra de 30 de zile a fișierelor recente";
            if (acquired == default || times.Min() < acquired - MftParser.RecentWindow || times.Max() > acquired)
                return "jurnalul USN are ore în afara ferestrei de 30 de zile dinaintea achiziției $MFT, iar fișierele mai vechi nu sunt emise ca recente (redenumirile lor nu se pot verifica)";
        }
        return null;
    }

    /// <summary>Plain Romanian gap between two times, in the largest sensible unit.</summary>
    public static string Gap(TimeSpan d)
    {
        if (d.TotalDays >= 365) return $"{(int)(d.TotalDays / 365.25)} ani";
        if (d.TotalDays >= 2) return $"{(int)d.TotalDays} zile";
        if (d.TotalHours >= 2) return $"{(int)d.TotalHours} ore";
        return $"{(int)d.TotalMinutes} minute";
    }

    public static string Iso(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
}
