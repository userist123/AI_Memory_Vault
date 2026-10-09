using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class SequenceRules
{
    private sealed record Access(TimelineEvent E, DateTimeOffset Time, string Account, string Share, string Leaf, string Ip, string What);
    private sealed record Hit(TimelineEvent E, DateTimeOffset Time, string Account, string Path, string Leaf, string What, bool Weak);

    private static string ShareTail(string share) => share[(share.LastIndexOf('\\') + 1)..].Trim();

    /// <summary>Remote share access: Security 5140 / 5145 from another host (not IPC$), or 4663 on a network path. The account is the one in the event.</summary>
    private static List<Access> SmbAccesses(Ctx c, SmbStagingUsbData d)
    {
        var list = new List<Access>();
        foreach (var e in c.Events)
        {
            if (T(e) is not { } t) continue;
            if (Ev(e, "Security", 5140, 5145))
            {
                var share = F(e, "ShareName");
                if (d.IgnoredShares.Any(s => s.Equals(ShareTail(share), StringComparison.OrdinalIgnoreCase))) continue;
                var ip = F(e, "IpAddress");
                var acc = SubjectAccount(e);
                if (acc.Length == 0 || !IsRemoteAddress(ip)) continue;
                var target = F(e, "RelativeTargetName");
                list.Add(new(e, t, acc, share, Leaf(target), ip.Trim(), $"{e.EventId} {share}{(target.Length > 0 ? "\\" + target : "")}"));
            }
            else if (Ev(e, "Security", 4663) && IsUnc(F(e, "ObjectName")))
            {
                var acc = SubjectAccount(e);
                if (acc.Length == 0) continue;
                var obj = F(e, "ObjectName");
                list.Add(new(e, t, acc, obj, Leaf(obj), "", $"4663 {obj}"));
            }
        }
        return list;
    }

    private static bool IgnoredStaging(SmbStagingUsbData d, string path) =>
        d.IgnoredStagingPathParts.Any(p => p.Length > 0 && path.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static bool UsnIsWrite(TimelineEvent e) => Wp14Rules.UsnWrite.Any(x => F(e, "Reason").Contains(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>A file written or created by one of the file-system sources: USN (create / extend / overwrite), Security 4663 with a write mask, Sysmon 11. <c>Weak</c> is never set here.</summary>
    private static IEnumerable<Hit> WriteHits(Ctx c)
    {
        foreach (var e in c.Events)
        {
            if (T(e) is not { } t) continue;
            if (e.Source == "USN" && UsnIsWrite(e))
            {
                var path = e.Path; var name = F(e, "FileName");
                yield return new(e, t, "", path, name.Length > 0 ? name : Leaf(path), $"USN {path} ({F(e, "Reason")})", false);
            }
            else if (Ev(e, "Security", 4663) && Wp14Rules.Write4663(e))
            {
                var path = F(e, "ObjectName");
                yield return new(e, t, SubjectAccount(e), path, Leaf(path), $"4663 {path} (acces de scriere)", false);
            }
            else if (Ev(e, Sysmon, 11))
            {
                var path = F(e, "TargetFilename");
                var u = F(e, "User");
                yield return new(e, t, u.Contains('\\') ? u : "", path, Leaf(path), $"Sysmon 11 {path}", false);
            }
        }
    }

    /// <summary>Local staging: files written under a local, non-removable folder with a known drive letter, outside the ignored system paths; plus LNK references (weak: a link shows a file was referenced, not written).</summary>
    private static List<Hit> StagingHits(Ctx c, SmbStagingUsbData d, HashSet<string> removable)
    {
        bool Local(string path) => LetterOfPath(path) is { Length: > 0 } l && !removable.Contains(l) && !IsUnc(path) && !IgnoredStaging(d, path);
        var list = WriteHits(c).Where(h => Local(h.Path)).ToList();
        foreach (var e in c.Events.Where(e => e.Source == "LNK" && T(e) is not null))
        {
            var dt = F(e, "DriveType");
            var path = e.Path.Length > 0 ? e.Path : F(e, "LnkTarget");
            if (dt.Equals("REMOVABLE", StringComparison.OrdinalIgnoreCase) || dt.Equals("NETWORK", StringComparison.OrdinalIgnoreCase) || dt.Equals("REMOTE", StringComparison.OrdinalIgnoreCase)) continue;
            if (Local(path)) list.Add(new(e, T(e)!.Value, "", path, Leaf(path), $"LNK către {path} (referire, nu scriere)", true));
        }
        return list;
    }

    private static HashSet<string> Names(IEnumerable<string> leaves) => leaves.Where(l => l.Length > 0).Select(Low).ToHashSet();

    private static string Fold(IEnumerable<string> names, int max = 6)
    {
        var l = names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return string.Join(", ", l.Take(max)) + (l.Count > max ? $" … (+{l.Count - max})" : "");
    }

    /// <summary>
    /// SEQ-SMB-STAGING-USB (lessons-learned row 69, without a NAS): remote share access, local staging, write to a removable medium. The steps are joined by account (where both
    /// are known), by the time windows in the data and, where the evidence allows, by file name; without a file-name link the sequence says "corelare temporală, fără legătură
    /// dovedită între fișiere". Staging is by drive-letter paths only: a USN row without a letter cannot be told from the removable volume. Minimum: three observed steps, or two
    /// with a proven file-name link between the share access and the medium.
    /// </summary>
    private static void SmbStagingUsb(Ctx c, List<Finding> o)
    {
        var d = c.Data.SmbStagingUsb;
        var access = SmbAccesses(c, d);
        if (access.Count == 0) return;
        var media = MediaWithWrites(c);
        if (media.Count == 0) return;
        var staging = StagingHits(c, d, RemovableLetters(c));
        bool smbSrc = c.Has(SecuritySource), stagingSrc = c.Has("USN", "LNK", SecuritySource, SysmonSource);

        foreach (var group in access.GroupBy(a => Low(a.Account)))
        {
            var As = group.OrderBy(a => a.Time).ToList();
            var acc = As[0].Account;
            var aFirst = As[0].Time; var aLast = As[^1].Time;
            foreach (var m in media)
            {
                var Bs = staging.Where(b => b.Time >= aFirst && b.Time <= aLast + Min(d.SmbToStagingMinutes) && AccountCompatibleHit(acc, b)).OrderBy(b => b.Time).ToList();
                var stageFrom = Bs.Count > 0 ? Bs[0].Time : aFirst;
                var stageTo = Bs.Count > 0 ? Bs[^1].Time : aLast + Min(d.SmbToStagingMinutes);
                var Cs = m.Writes.Where(w => T(w.Event) is { } wt && wt >= stageFrom && wt <= stageTo + Min(d.StagingToMediaMinutes) && WriteCompatible(acc, m, w))
                                 .OrderBy(w => T(w.Event)).ToList();
                if (Cs.Count == 0) continue;

                var a = Names(As.Select(x => x.Leaf)); var b = Names(Bs.Select(x => x.Leaf)); var w2 = Names(Cs.Select(x => Leaf(PathOf(x))));
                var ab = a.Intersect(b).ToList(); var bc = b.Intersect(w2).ToList(); var ac = a.Intersect(w2).ToList();
                bool proven = ac.Count > 0 || (ab.Count > 0 && bc.Count > 0);
                int observed = 2 + (Bs.Count > 0 ? 1 : 0);
                if (observed < (proven ? d.MinObservedStepsWithFileLink : d.MinObservedSteps)) continue;

                string link;
                if (proven)
                    link = "legătură dovedită prin nume de fișier: " + string.Join("; ", new[]
                    {
                        ab.Count > 0 ? $"partajare → staging ({Fold(ab)})" : "", bc.Count > 0 ? $"staging → mediu ({Fold(bc)})" : "", ac.Count > 0 ? $"partajare → mediu ({Fold(ac)})" : "",
                    }.Where(x => x.Length > 0)) + ". Numele egale arată același nume de fișier, nu același conținut (fără hash).";
                else if (ab.Count > 0 || bc.Count > 0)
                    link = "legătură parțială prin nume de fișier: " + string.Join("; ", new[] { ab.Count > 0 ? $"partajare → staging ({Fold(ab)})" : "", bc.Count > 0 ? $"staging → mediu ({Fold(bc)})" : "" }.Where(x => x.Length > 0)) +
                           "; lipsește legătura dintre capetele secvenței. Corelare temporală, fără legătură dovedită între fișiere pentru restul pașilor.";
                else
                    link = "corelare temporală, fără legătură dovedită între fișiere (niciun nume de fișier comun între partajare, staging și mediu).";

                var folders = Bs.Select(x => WinPath.GetDirectoryName(x.Path) ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var ips = As.Select(x => x.Ip).Where(x => x.Length > 0).Distinct().ToList();
                var steps = new List<SeqStep>
                {
                    new("acces la partajare de la distanță", "Security 5140/5145/4663", aFirst, acc, As[0].Share,
                        $"{As.Count} evenimente" + (ips.Count > 0 ? $" de la {string.Join(", ", ips)}" : "") + $": {Fold(As.Select(x => x.What), 4)}",
                        As.Take(8).Select(x => Ref(x.E, x.What)).ToList(), []),
                    Bs.Count > 0
                        ? new("staging local", "USN / Security 4663 / Sysmon 11 / LNK", Bs[0].Time, Fold(Bs.Select(x => x.Account), 3), Fold(folders, 3),
                              $"{Bs.Count} fișiere create sau referite în {Fold(folders, 3)}: {Fold(Bs.Select(x => x.Leaf), 6)}" + (Bs.All(x => x.Weak) ? " (doar referiri LNK)" : ""),
                              Bs.Take(8).Select(x => Ref(x.E, x.What)).ToList(), [])
                        : SequenceEngine.Missing("staging local", "USN / Security 4663 / Sysmon 11", stagingSrc),
                    new($"scriere pe mediul amovibil {m.Name}", "USN / Security 4663 pe volumul mediului", T(Cs[0].Event), Fold(Cs.Select(x => WriteAccount(m, x)), 3), $"mediu {m.Name} ({m.Letter}:)",
                        $"{Cs.Count} urme de scriere pe unitatea {m.Letter}: {Fold(Cs.Select(x => Leaf(PathOf(x))), 6)}", Cs.Take(8).Select(x => Ref(x.Event, x.What)).ToList(), m.FindingIds.ToList()),
                };
                var title = $"Secvență: acces la partajare la {Stamp(aFirst)}; staging local " + (Bs.Count > 0 ? $"la {Stamp(Bs[0].Time)}" : "pas neobservat") +
                            $"; scriere pe mediul {m.Name} la {Stamp(T(Cs[0].Event))} ({(proven ? "legătură de nume de fișier dovedită" : "corelare temporală")})";
                var sev = proven ? c.Sev(Severity.High) : c.Sev(Severity.Medium);
                o.Add(Build(c, "SEQ-SMB-STAGING-USB", title, sev, proven ? Confidence.Medium : Confidence.Low, steps,
                    $"acces → staging ≤ {d.SmbToStagingMinutes} min; staging → mediu ≤ {d.StagingToMediaMinutes} min",
                    link, "Combină accesul la partajări (Security 5140/5145/4663), urmele de fișiere locale (USN/4663/Sysmon/LNK) și scrierile pe mediu (MEDIA-FILE-ACTIVITY).",
                    new[] { m.Presence?.FindingId ?? "", m.Activity?.FindingId ?? "" }, As[0].E.Host,
                    ["Accesul la partajări și scrierea pe un mediu pot fi activități independente ale aceluiași utilizator.", "Un nume de fișier egal nu dovedește același conținut.",
                     "Un fișier poate fi scris pe mediu dintr-o sursă care nu apare în sursele colectate."]));
            }
        }
    }

    private static string PathOf(Wp14Rules.Activity a) => a.Event.Path.Length > 0 && !a.Event.Source.StartsWith("EventLog:", StringComparison.Ordinal) ? a.Event.Path : F(a.Event, "ObjectName");

    private static bool AccountCompatibleHit(string account, Hit h) => h.Account.Length == 0 || SequenceEngine.JoinAccount(account, h.Account) != JoinResult.Different;
}
