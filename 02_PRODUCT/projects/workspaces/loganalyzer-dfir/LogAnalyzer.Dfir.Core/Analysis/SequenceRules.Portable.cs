using System.Globalization;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class SequenceRules
{
    /// <summary>A program location that qualifies: first seen in the period (earliest row of Prefetch / BAM / Amcache for the path), with whether any row proves execution.</summary>
    private sealed record Program(string Path, string Name, DateTimeOffset FirstSeen, bool Executed, string Account, string Where, List<TimelineEvent> Rows);

    /// <summary>The executable path a row is about: for Prefetch, the image among its referenced files when the entry has no path of its own.</summary>
    private static string ExeOf(TimelineEvent e)
    {
        if (e.Source == "Prefetch" && LetterOfPath(e.Path).Length == 0)
        {
            var refs = F(e, "ReferencedFiles").Split('|');
            return refs.FirstOrDefault(r => r.EndsWith("\\" + e.Process, StringComparison.OrdinalIgnoreCase)) ?? e.Path;
        }
        return e.Path;
    }

    private static List<Program> NewPrograms(Ctx c, PortableUsbArchiveData d, HashSet<string> removable)
    {
        var tools = d.ArchiverTools.Concat(d.PortableTools).Select(Low).ToHashSet();
        var list = new List<Program>();
        var rows = c.Events.Where(e => e.Source is "Prefetch" or "BAM" or "Amcache" && T(e) is not null).Select(e => (E: e, Path: ExeOf(e))).Where(x => LetterOfPath(x.Path).Length > 0);
        foreach (var g in rows.GroupBy(x => Low(x.Path)))
        {
            var path = g.First().Path; var name = Leaf(path); var letter = LetterOfPath(path);
            string where;
            if (removable.Contains(letter)) where = $"unitate amovibilă {letter}:";
            else if (d.UserWritablePathParts.Any(p => p.Length > 0 && path.Contains(p, StringComparison.OrdinalIgnoreCase)) &&
                     (tools.Contains(Low(name)) || d.PortableNameWords.Any(w => w.Length > 0 && name.Contains(w, StringComparison.OrdinalIgnoreCase))))
                where = "cale scriabilă de utilizator";
            else continue;
            var first = g.Min(x => T(x.E))!.Value;
            // First seen in the case period: the earliest row for the path must fall inside it (when the scope gives a period).
            if (c.Scope.PeriodFromUtc is { } pf && first < pf || c.Scope.PeriodToUtc is { } pt && first > pt) continue;
            bool executed = g.Any(x => x.E.Source is "Prefetch" or "BAM");
            var accounts = g.Select(x => x.E.User).Where(u => u.Length > 0).Distinct().ToList();
            list.Add(new(path, name, first, executed, string.Join(", ", accounts), where, g.Select(x => x.E).ToList()));
        }
        return list;
    }

    private sealed record Archive(Hit Hit, long? Size);

    private static long? SizeOf(TimelineEvent e)
    {
        var s = F(e, "Size", "FileSize", "TargetSize", "Length");
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    /// <summary>
    /// Archive creation: a USN / 4663 write / Sysmon 11 row for a file with an archive extension. The size comes from the row itself or, where one exists, from a LNK / Amcache
    /// row for the same path; no size = unknown. Returns the archives that count and how many were below the threshold.
    /// </summary>
    private static (List<Archive> Counted, int Small) Archives(Ctx c, PortableUsbArchiveData d)
    {
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in c.Events.Where(e => e.Source is "LNK" or "Amcache"))
            if (SizeOf(e) is { } n && e.Path.Length > 0) sizes[e.Path] = n;
        var counted = new List<Archive>(); int small = 0;
        foreach (var h in WriteHits(c))
        {
            var ext = WinPath.GetExtension(h.Leaf);
            if (!d.ArchiveExtensions.Any(x => x.Equals(ext, StringComparison.OrdinalIgnoreCase))) continue;
            long? size = SizeOf(h.E) ?? (sizes.TryGetValue(h.Path, out var s0) ? s0 : null);
            if (size is { } sz && sz < d.ArchiveMinBytes) { small++; continue; }
            counted.Add(new(h, size));
        }
        return (counted, small);
    }

    /// <summary>
    /// SEQ-PORTABLE-USB-ARCHIVE (lessons-learned row 82): software run from a removable drive, or a portable / archiver tool run from a user-writable path, first seen in the case
    /// period (Prefetch / BAM; Amcache is presence only); creation of an archive (size at or above the data threshold where the size is known); and writes to a removable medium.
    /// Required: the writes and at least one of the other two steps, in the data windows. The archive step is "pas neobservat" when none qualifies. A path without a drive letter
    /// cannot be placed and is skipped.
    /// </summary>
    private static void PortableUsbArchive(Ctx c, List<Finding> o)
    {
        var d = c.Data.PortableUsbArchive;
        var media = MediaWithWrites(c);
        if (media.Count == 0) return;
        var removable = RemovableLetters(c);
        var programs = NewPrograms(c, d, removable);
        var (archives, small) = Archives(c, d);
        if (programs.Count == 0 && archives.Count == 0) return;
        bool execSrc = c.Has("Prefetch", "BAM", "Amcache"), archSrc = c.Has("USN", SecuritySource, SysmonSource);
        var win1 = Min(d.ExecToArchiveMinutes); var win2 = Min(d.ArchiveToMediaMinutes);

        foreach (var m in media)
        {
            var writes = m.Writes.OrderBy(w => T(w.Event)).ToList();
            var progs = programs.Where(p => writes.Any(w => SequenceEngine.Follows(p.FirstSeen, T(w.Event), win1 + win2))).OrderBy(p => p.FirstSeen).ToList();
            var arcs = archives.Where(a => writes.Any(w => SequenceEngine.Follows(a.Hit.Time, T(w.Event), win2))).OrderBy(a => a.Hit.Time).ToList();
            var cs = writes.Where(w => arcs.Any(a => SequenceEngine.Follows(a.Hit.Time, T(w.Event), win2)) || progs.Any(p => SequenceEngine.Follows(p.FirstSeen, T(w.Event), win1 + win2))).ToList();
            if (cs.Count == 0) continue;
            int observed = 1 + (progs.Count > 0 ? 1 : 0) + (arcs.Count > 0 ? 1 : 0);
            // The steps must rest on at least as many distinct records as the minimum: one archive written straight to the medium is one fact, not a sequence.
            var distinct = progs.Take(1).Select(p => p.Rows[0]).Concat(arcs.Take(1).Select(a => a.Hit.E)).Concat(cs.Take(1).Select(w => w.Event)).Distinct().Count();
            if (observed < d.MinObservedSteps || distinct < d.MinObservedSteps) continue;

            var arcNames = Names(arcs.Select(a => a.Hit.Leaf)); var wNames = Names(cs.Select(w => Leaf(PathOf(w))));
            var shared = arcNames.Intersect(wNames).ToList();
            bool sizeKnownBig = arcs.Any(a => a.Size is not null);
            bool full = observed == 3 && (sizeKnownBig || shared.Count > 0);
            var link = shared.Count > 0
                ? $"arhiva apare și pe mediu prin nume de fișier ({Fold(shared)}); numele egal nu dovedește același conținut (fără hash). Legătura dintre rularea programului și arhivă este doar în timp."
                : "corelare temporală, fără legătură dovedită între fișiere (nicio arhivă cu același nume pe mediu).";

            var steps = new List<SeqStep>();
            if (progs.Count > 0)
            {
                var p = progs[0];
                steps.Add(new(p.Executed ? "software nou rulat" : "software nou prezent (prezență, nu execuție)", string.Join(" / ", p.Rows.Select(r => r.Source).Distinct()), p.FirstSeen, p.Account, p.Path,
                    $"{p.Name} din {p.Where}, prima apariție în artefactele colectate" + (progs.Count > 1 ? $" (+{progs.Count - 1} alte programe)" : "") +
                    "; prima apariție în perioadă înseamnă prima în sursele colectate, nu dovada că programul nu exista înainte",
                    p.Rows.Take(6).Select(r => Ref(r, $"{r.Source} {p.Path}")).ToList(), []));
            }
            else steps.Add(SequenceEngine.Missing("software portabil sau de pe mediu amovibil", "Prefetch / BAM / Amcache", execSrc));
            if (arcs.Count > 0)
            {
                var a = arcs[0];
                var sizeText = a.Size is { } sz ? $"{SizeText(sz)} (prag {SizeText(d.ArchiveMinBytes)})" : "dimensiune necunoscută (sursa nu o conține)";
                steps.Add(new("arhivă creată", a.Hit.E.Source.StartsWith("EventLog:", StringComparison.Ordinal) ? a.Hit.E.Source.Replace("EventLog:", "") : a.Hit.E.Source, a.Hit.Time, a.Hit.Account, a.Hit.Path,
                    $"{a.Hit.Leaf}, {sizeText}" + (arcs.Count > 1 ? $" (+{arcs.Count - 1} alte arhive)" : ""), arcs.Take(6).Select(x => Ref(x.Hit.E, x.Hit.What)).ToList(), []));
            }
            else
            {
                var miss = SequenceEngine.Missing("arhivă mare creată", "USN / Security 4663 / Sysmon 11", archSrc);
                if (small > 0) miss = miss with { Note = $"pas neobservat ({small} arhive sub pragul de {SizeText(d.ArchiveMinBytes)} nu contează; sursa nu arată o arhivă mai mare în fereastră)" };
                steps.Add(miss);
            }
            steps.Add(new($"scriere pe mediul amovibil {m.Name}", "USN / Security 4663 pe volumul mediului", T(cs[0].Event), Fold(cs.Select(x => WriteAccount(m, x)), 3), $"mediu {m.Name} ({m.Letter}:)",
                $"{cs.Count} urme de scriere pe unitatea {m.Letter}: {Fold(cs.Select(x => Leaf(PathOf(x))), 6)}", cs.Take(8).Select(x => Ref(x.Event, x.What)).ToList(), m.FindingIds.ToList()));

            var t1 = progs.Count > 0 ? $"software {(progs[0].Executed ? "rulat" : "prezent")} la {Stamp(progs[0].FirstSeen)}" : "software portabil: pas neobservat";
            var t2 = arcs.Count > 0 ? $"arhivă creată la {Stamp(arcs[0].Hit.Time)}" : "arhivă: pas neobservat";
            var title = $"Secvență: {t1}; {t2}; scriere pe mediul {m.Name} la {Stamp(T(cs[0].Event))}";
            o.Add(Build(c, "SEQ-PORTABLE-USB-ARCHIVE", title, full ? c.Sev(Severity.High) : c.Sev(Severity.Medium), full ? Confidence.Medium : Confidence.Low, steps,
                $"program → arhivă ≤ {d.ExecToArchiveMinutes} min; arhivă → mediu ≤ {d.ArchiveToMediaMinutes} min; prag arhivă {SizeText(d.ArchiveMinBytes)}",
                link, "Combină software nou (Prefetch/BAM/Amcache), crearea unei arhive (USN/4663/Sysmon 11) și scrierile pe mediu (MEDIA-FILE-ACTIVITY).",
                new[] { m.Presence?.FindingId ?? "", m.Activity?.FindingId ?? "" }, (progs.Count > 0 ? progs[0].Rows[0].Host : arcs[0].Hit.E.Host),
                ["Un utilitar portabil sau o arhivă pot face parte din activitatea obișnuită a utilizatorului.", "Prezența în Amcache nu înseamnă execuție; Prefetch și BAM arată rulări, nu scopul lor.",
                 "Fără hash-uri, arhiva scrisă pe mediu nu poate fi identificată cu cea creată local."]));
        }
    }
}
