using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    private sealed record ToolHit(TimelineEvent Event, string Kind, string Path, string Hash);

    /// <summary>The executable path a row is about: the Prefetch entry's own image (from its referenced files), else the row's path.</summary>
    private static string ExePath(TimelineEvent e)
    {
        if (e.Source == "Prefetch")
        {
            var refs = F(e, "ReferencedFiles").Split('|');
            return refs.FirstOrDefault(r => r.EndsWith("\\" + e.Process, StringComparison.OrdinalIgnoreCase)) ?? e.Path;
        }
        return e.Path;
    }

    private static IEnumerable<string> Sha256sOf(TimelineEvent e)
    {
        if (e.Hash.Length == 64) yield return e.Hash;
        foreach (var part in F(e, "Hashes").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            if (part.StartsWith("SHA256=", StringComparison.OrdinalIgnoreCase) && part.Length == 71) yield return part[7..];
    }

    /// <summary>
    /// REMOTE-TOOL-EXECUTED (Prefetch / BAM / UserAssist / 4688 / Sysmon 1 for a known remote-access tool) and REMOTE-TOOL-PRESENT (Amcache /
    /// ShimCache, 7045/4697 service, MsiInstaller 11707 install, the tool's own log files): one finding per tool, execution wins. Present is not
    /// executed. Software approved in the procedure profile is Info with "aprobat în profil". The tool list is data (Data/remote_access_tools.json).
    /// </summary>
    private static void Tools(Ctx c, List<Finding> o)
    {
        var starts = ProcessStarts(c.Events).ToList();
        foreach (var tool in c.Data.Tools.Tools)
        {
            bool IsExe(string path) => path.Length > 0 && tool.Executables.Contains(WinPath.GetFileName(path.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
            var exec = new List<ToolHit>(); var present = new List<ToolHit>();
            foreach (var e in c.Events)
            {
                if (e.Source is "Prefetch" or "BAM" or "UserAssist")
                {
                    if (IsExe(e.Process) || IsExe(e.Path)) exec.Add(new(e, e.Source, ExePath(e), ""));
                }
                else if (e.Source is "Amcache" or "ShimCache")
                {
                    if (IsExe(e.Path) || IsExe(e.Process)) present.Add(new(e, e.Source, e.Path, e.Hash.Length == 64 ? e.Hash : ""));
                }
                else if (Ev(e, "System", 7045) || Ev(e, "Security", 4697))
                {
                    var name = F(e, "ServiceName"); var exe = ServiceExe(F(e, "ImagePath", "ServiceFileName"));
                    if (tool.ServiceNames.Contains(name, StringComparer.OrdinalIgnoreCase) || tool.DisplayContains.Any(d => name.Contains(d, StringComparison.OrdinalIgnoreCase)) || IsExe(exe))
                        present.Add(new(e, "serviciu " + e.EventId, exe, ""));
                }
                else if (Ev(e, "Application", 11707, 1033) && e.Provider.Contains("MsiInstaller", StringComparison.OrdinalIgnoreCase))
                {
                    if (tool.ProductContains.Any(p => AllText(e).Contains(p, StringComparison.OrdinalIgnoreCase))) present.Add(new(e, "instalare MSI", "", ""));
                }
                // The tool's own log files, seen as a path in any other collected source (file system, LNK, Jump Lists…).
                bool artifactAlreadyRead = e.Source is "Prefetch" or "BAM" or "UserAssist" or "Amcache" or "ShimCache" || e.Source.StartsWith("EventLog:", StringComparison.Ordinal);
                if (!artifactAlreadyRead && tool.LogMarkers.Any(m => e.Path.Replace('/', '\\').Contains(m, StringComparison.OrdinalIgnoreCase)))
                    present.Add(new(e, "fișier de log al uneltei", e.Path, ""));
            }
            foreach (var p in starts.Where(p => IsExe(p.Image)))
                exec.Add(new(p.Event, p.Event.EventId == "4688" ? "4688" : "Sysmon 1", p.Image, Sha256sOf(p.Event).FirstOrDefault() ?? ""));
            if (exec.Count == 0 && present.Count == 0) continue;

            bool executed = exec.Count > 0;
            var hits = (executed ? exec.Concat(present) : present).ToList();
            var paths = hits.Select(h => h.Path).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var aliases = tool.Aliases.Concat([tool.Name]).Concat(tool.Executables.Select(x => WinPath.GetFileNameWithoutExtension(x)));
            var hashes = hits.SelectMany(h => h.Hash.Length > 0 ? [h.Hash] : Sha256sOf(h.Event)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var approved = ApprovedSoftwareMatcher.Match(c.Profile, aliases, paths, hashes);
            var times = hits.Select(h => T(h.Event)).Where(t => t is not null).OrderBy(t => t).ToList();
            var runCounts = exec.Where(h => h.Event.Source == "Prefetch").Select(h => F(h.Event, "RunCount")).Where(r => r.Length > 0).ToList();
            string kinds = string.Join(", ", hits.GroupBy(h => h.Kind).Select(g => $"{g.Count()} × {g.Key}"));
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = executed ? "REMOTE-TOOL-EXECUTED" : "REMOTE-TOOL-PRESENT",
                Title = $"Unealtă de acces la distanță {(executed ? "rulată" : "prezentă")}: {tool.Name}" + (approved is not null ? " (aprobat în profil)" : ""),
                Severity = approved is not null ? Severity.Info : executed ? Severity.Medium : Severity.Low,
                Category = "Command and Control", Classification = Classification.Direct, Confidence = executed ? Confidence.High : Confidence.Medium, MitreTechniqueId = "T1219",
                FirstSeenUtc = times.FirstOrDefault(), LastSeenUtc = times.LastOrDefault(), Process = tool.Executables.FirstOrDefault() ?? "", File = paths.FirstOrDefault() ?? "",
                Description = $"{tool.Name}: {kinds}; {Time(times.FirstOrDefault())}" + (times.Count > 1 ? $" – {Time(times.LastOrDefault())}" : "") + "." +
                              (paths.Count > 0 ? $" Căi: {Join(paths, 4)}." : "") + (runCounts.Count > 0 ? $" Prefetch: număr de rulări {Join(runCounts)}." : "") +
                              (executed ? present.Count > 0 ? $" Alte dovezi de prezență: {present.Count} (instalare/serviciu/amcache/log)." : "" : " Prezența unui artefact nu dovedește execuția; nu există în caz un artefact de execuție pentru această unealtă.") +
                              (approved is not null ? $" Software aprobat în profil: {approved.Name} ({(approved.PathPattern.Length > 0 ? approved.PathPattern : approved.Publisher)}); aprobat în profil, deci informativ." : "") +
                              " Existența unei unelte de acces la distanță nu arată o sesiune sau cine s-a conectat.",
                ClassificationReason = executed ? "Artefact de execuție (Prefetch/BAM/UserAssist/4688/Sysmon 1) pentru un executabil din lista de unelte de acces la distanță." : "Artefact de prezență (Amcache/ShimCache/serviciu/instalare/log) pentru o unealtă din lista de acces la distanță.",
                SemanticType = executed ? SemanticType.Execution : SemanticType.Presence,
                SupportingEvidence = hits.Take(14).Select(h => Ref(h.Event, $"{h.Kind} {tool.Name}")).ToList(),
                AlternativeExplanations = ["Suport tehnic sau administrare legitimă; unealta poate fi aprobată de organizație dar necompletată în profil.", "O unealtă prezentă doar ca fișier descărcat sau instalată și nefolosită."],
            });
        }
    }
}
