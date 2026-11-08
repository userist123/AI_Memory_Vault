using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Memory;

/// <summary>Findings and gaps produced from Volatility rows in the case timeline.</summary>
public sealed record MemoryFindingsResult(IReadOnlyList<Finding> Findings, IReadOnlyList<EvidenceGap> Gaps);

/// <summary>
/// Master plan 6.6: rules over the rows Volatility 3 printed (MEM-HIDDEN, MEM-INJECT, MEM-NETPROC, MEM-PATH) and the memory/disk correlation (MEM-NODISK).
/// Every finding is a candidate read from tool output, never a verdict: the application does not read the memory image. Lists live in
/// Analysis/Data/memory_rules.json. The result depends only on the timeline: running it twice gives the same findings.
/// </summary>
public static class MemoryFindings
{
    public const int MaxPerRule = 200;
    private const int MaxEvidencePerFinding = 20;
    private const string Reason = "Candidat citit din ieșirea unei unelte (Volatility 3), nu din imaginea de memorie; aplicația nu a verificat imaginea și nu a rulat unealta.";

    private sealed record Rules(Regex[] Paths, HashSet<string> Core, string[] DiskSources, (string Source, string EventId)[] ProcessCreation);

    private static readonly Lazy<Rules> Data = new(() =>
    {
        using var doc = JsonDocument.Parse(ShippedData.Read("memory_rules.json"));
        var root = doc.RootElement;
        string[] Strs(string k) => root.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : [];
        var creation = root.TryGetProperty("process_creation_event_log", out var pc) && pc.ValueKind == JsonValueKind.Array
            ? pc.EnumerateArray().Select(x => (x.GetProperty("source").GetString() ?? "", x.GetProperty("event_id").GetString() ?? "")).ToArray() : [];
        return new Rules(Strs("user_writable_path_regexes").Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).ToArray(),
            Strs("core_processes").Select(x => x.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal), Strs("disk_sources"), creation);
    });

    private static readonly Regex ImagePath = new(@"^\s*(?:\\\?\?\\)?(?<p>(?:[A-Za-z]:|\\SystemRoot)\\.*?\.(?:exe|com|scr|dll))(?=\s|$|"")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Token = new(@"[^\s\\/""'<>|:*?]+\.[A-Za-z0-9]{2,4}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool IsMem(TimelineEvent e, string plugin) => e.Source == "Memory:" + plugin;
    private static bool IsMemory(TimelineEvent e) => e.Source.StartsWith("Memory:", StringComparison.Ordinal);
    private static string Key(TimelineEvent e) => $"{e.Pid}|{e.Process.ToLowerInvariant()}";
    private static EvidenceRef Ref(TimelineEvent e, string why) => new(e.EvidenceId, e.Locator, why, e.SourceSha256);
    private static bool Ended(TimelineEvent e) => e.Fields.GetValueOrDefault("ExitTime", "") is not ("" or "N/A");

    /// <summary>The executable path at the start of a command line, or "" when there is none.</summary>
    public static string ImagePathOf(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return "";
        var a = args.TrimStart();
        if (a.StartsWith('"')) { var end = a.IndexOf('"', 1); return end > 1 ? a[1..end] : ""; }
        var m = ImagePath.Match(a);
        return m.Success ? m.Groups["p"].Value : "";
    }

    private static string LeafName(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        return (i >= 0 ? path[(i + 1)..] : path).Trim().ToLowerInvariant();
    }

    /// <summary>True when the timeline holds a source the correlation reads as "disk" (Prefetch, Amcache, ShimCache, services, process-creation events).</summary>
    public static bool IsDiskSource(TimelineEvent e)
    {
        var r = Data.Value;
        if (r.DiskSources.Any(s => e.Source.Equals(s, StringComparison.OrdinalIgnoreCase) || e.Source.StartsWith(s + ":", StringComparison.OrdinalIgnoreCase))) return true;
        return r.ProcessCreation.Any(c => e.Source.Equals(c.Source, StringComparison.OrdinalIgnoreCase) && e.EventId == c.EventId);
    }

    public static MemoryFindingsResult Run(IReadOnlyList<TimelineEvent> timeline, Func<string> nextId)
    {
        var mem = timeline.Where(IsMemory).ToList();
        if (mem.Count == 0) return new MemoryFindingsResult([], []);
        var rules = Data.Value;
        var findings = new List<Finding>();
        var gaps = new List<EvidenceGap>();

        var cmdByPid = mem.Where(e => IsMem(e, "cmdline") && e.Pid is not null).GroupBy(e => e.Pid!.Value).ToDictionary(g => g.Key, g => g.First());
        string CmdPath(int? pid) => pid is { } p && cmdByPid.TryGetValue(p, out var c) ? ImagePathOf(c.Fields.GetValueOrDefault("CommandLine", "")) : "";

        // ---- MEM-HIDDEN ----
        var hidden = VolatilityJsonParser.PsscanOnly(timeline).GroupBy(Key).Select(g => g.OrderBy(x => x.Locator, StringComparer.Ordinal).First()).OrderBy(e => e.Pid).Take(MaxPerRule).ToList();
        var hiddenByPid = new Dictionary<int, (Finding F, TimelineEvent E)>();
        foreach (var e in hidden)
        {
            var f = Make(nextId, "MEM-HIDDEN", $"Proces găsit de psscan și negăsit de pslist: {e.Process} (PID {e.Pid})", Severity.Medium, Classification.Unproven, Confidence.Low, e,
                $"Procesul {e.Process} (PID {e.Pid}) apare în rezultatul psscan și nu apare în pslist, și nu este marcat ca încheiat. Poate fi un proces scos din lista legată a sistemului (tehnică de ascundere), dar psscan găsește și structuri rămase de la procese încheiate sau încheiate între cele două rulări, deci rezultatul este un candidat de verificat, nu o dovadă.",
                [Ref(e, "rând psscan (procesul lipsește din pslist)")], "T1014",
                ["Structură rămasă în memorie de la un proces încheiat", "Procesul a pornit sau s-a încheiat între rulările pluginurilor", "Simboluri sau versiune de Volatility nepotrivite"],
                ["Rulați din nou pslist și psscan pe aceeași imagine și comparați", "Căutați procesul în Prefetch, Amcache și jurnalul 4688"]);
            findings.Add(f);
            if (e.Pid is { } p) hiddenByPid[p] = (f, e);
        }

        // ---- MEM-PATH (computed before MEM-INJECT, which uses it as "another memory signal") ----
        var pathHits = new List<(TimelineEvent Cmd, string Image)>();
        foreach (var c in mem.Where(e => IsMem(e, "cmdline") && e.Pid is not null).GroupBy(Key).Select(g => g.First()).OrderBy(e => e.Pid))
        {
            var img = ImagePathOf(c.Fields.GetValueOrDefault("CommandLine", ""));
            if (img.Length == 0) continue;
            var norm = img.Replace('/', '\\');
            if (rules.Paths.Any(r => SafeMatch(r, norm))) pathHits.Add((c, img));
        }
        var pathPids = pathHits.Where(h => h.Cmd.Pid is not null).Select(h => h.Cmd.Pid!.Value).ToHashSet();

        // ---- MEM-INJECT ----
        var injectHigh = new Dictionary<int, (Finding F, List<TimelineEvent> Rows)>();
        var inject = new List<Finding>();
        foreach (var g in mem.Where(e => IsMem(e, "malfind") && e.Pid is not null && e.Fields.GetValueOrDefault("Protection", "").Contains("EXECUTE_READWRITE", StringComparison.OrdinalIgnoreCase))
                             .GroupBy(Key).OrderBy(g => g.First().Pid))
        {
            if (inject.Count >= MaxPerRule) break;
            var rows = g.OrderBy(x => x.Locator, StringComparer.Ordinal).ToList();
            var first = rows[0];
            var mzRows = rows.Where(x => x.Fields.GetValueOrDefault("MzHeader", "") == "da").ToList();
            int pid = first.Pid!.Value;
            if (mzRows.Count > 0)
            {
                var f = Make(nextId, "MEM-INJECT", $"Regiune executabilă și inscriptibilă cu antet MZ în {first.Process} (PID {pid})", Severity.High, Classification.Candidate, Confidence.Medium, first,
                    $"malfind a semnalat {mzRows.Count} regiune(i) PAGE_EXECUTE_READWRITE al căror conținut începe cu antetul unui executabil (MZ) în procesul {first.Process} (PID {pid}). Aceasta este o urmă tipică de cod încărcat în memorie fără fișier pe disc, dar și unele programe legitime (încărcătoare, protecții de licență) o produc.",
                    mzRows.Take(MaxEvidencePerFinding).Select(x => Ref(x, $"regiune malfind {x.Fields.GetValueOrDefault("Protection", "")} la {x.Fields.GetValueOrDefault("StartVpn", "")} cu antet MZ")).ToList(), "T1055",
                    ["Program legitim care încarcă module în memorie (protecții, încărcătoare)", "Fals pozitiv malfind"],
                    ["Extrageți regiunea cu malfind --dump și analizați-o în mediu izolat", "Comparați cu fișierele procesului de pe disc"]);
                inject.Add(f); injectHigh[pid] = (f, mzRows);
            }
            else if (hiddenByPid.ContainsKey(pid) || pathPids.Contains(pid))
            {
                inject.Add(Make(nextId, "MEM-INJECT", $"Regiune executabilă și inscriptibilă în {first.Process} (PID {pid}), cu alt semnal de memorie", Severity.Low, Classification.Unproven, Confidence.Low, first,
                    $"malfind a semnalat {rows.Count} regiune(i) PAGE_EXECUTE_READWRITE fără antet MZ în procesul {first.Process} (PID {pid}). Singură, o astfel de regiune apare des la programe legitime (JIT, .NET, browsere); este raportată doar pentru că același proces are și un alt semnal în memorie (proces ascuns sau cale într-o locație inscriptibilă).",
                    rows.Take(MaxEvidencePerFinding).Select(x => Ref(x, $"regiune malfind {x.Fields.GetValueOrDefault("Protection", "")} la {x.Fields.GetValueOrDefault("StartVpn", "")} fără antet MZ")).ToList(), "T1055",
                    ["Compilare JIT sau runtime .NET/Java", "Fals pozitiv malfind"], ["Analizați regiunea împreună cu celălalt semnal al procesului"]));
            }
        }
        findings.AddRange(inject);

        // ---- MEM-NETPROC ----
        int netCount = 0;
        foreach (var pid in hiddenByPid.Keys.Union(injectHigh.Keys).OrderBy(x => x))
        {
            if (netCount >= MaxPerRule) break;
            var conns = mem.Where(e => IsMem(e, "netscan") && e.Pid == pid && e.RemoteIp.Length > 0).OrderBy(e => e.Locator, StringComparer.Ordinal).ToList();
            if (conns.Count == 0) continue;
            var proc = hiddenByPid.TryGetValue(pid, out var h) ? h.E.Process : injectHigh[pid].Rows[0].Process;
            var ev = new List<EvidenceRef>();
            var related = new List<string>();
            if (hiddenByPid.TryGetValue(pid, out var hh)) { ev.AddRange(hh.F.SupportingEvidence); related.Add(hh.F.FindingId); }
            if (injectHigh.TryGetValue(pid, out var ih)) { ev.AddRange(ih.F.SupportingEvidence); related.Add(ih.F.FindingId); }
            ev.AddRange(conns.Take(MaxEvidencePerFinding).Select(c => Ref(c, $"conexiune netscan către {c.RemoteIp}:{c.RemotePort}")));
            var why = string.Join(" și ", new[] { hiddenByPid.ContainsKey(pid) ? "găsit doar de psscan (MEM-HIDDEN)" : null, injectHigh.ContainsKey(pid) ? "cu regiune injectată MZ (MEM-INJECT)" : null }.Where(x => x is not null));
            var f = Make(nextId, "MEM-NETPROC", $"Conexiune de rețea a unui proces suspect: {proc} (PID {pid}) → {conns[0].RemoteIp}", Severity.High, Classification.Candidate, Confidence.Medium, conns[0],
                $"netscan arată {conns.Count} conexiune(i) cu adresă la distanță deținute de procesul {proc} (PID {pid}), care este {why}. Combinația este mai puternică decât fiecare semnal separat, dar rămâne un candidat din ieșirea uneltei: netscan poate arăta și structuri rămase de conexiuni închise.",
                ev, "T1071", ["Structură rămasă de la o conexiune închisă", "Fals pozitiv al semnalului de bază"],
                ["Căutați adresa la distanță în jurnalele de rețea (Zeek, PCAP, firewall)", "Verificați procesul pe disc și în Prefetch"], related);
            findings.Add(f); netCount++;
        }

        // ---- MEM-PATH findings ----
        foreach (var (cmd, img) in pathHits.Take(MaxPerRule))
            findings.Add(Make(nextId, "MEM-PATH", $"Proces pornit dintr-o locație în care utilizatorii pot scrie: {cmd.Process} (PID {cmd.Pid})", Severity.Medium, Classification.Unproven, Confidence.Low, cmd,
                $"Linia de comandă din memorie a procesului {cmd.Process} (PID {cmd.Pid}) arată imaginea {img}, aflată într-o locație în care utilizatorii obișnuiți pot scrie (profil public, Temp, ProgramData, Descărcări, Coșul de gunoi). Programele legitime se instalează de regulă în alte locuri, dar există și programe portabile sau actualizatoare care rulează de aici.",
                [Ref(cmd, "linie de comandă (cmdline)")], "T1036.005", ["Program portabil sau actualizator legitim"], ["Verificați semnătura și prezența fișierului în Prefetch, Amcache sau ShimCache"]));

        // ---- MEM-NODISK ----
        var disk = timeline.Where(e => !IsMemory(e) && IsDiskSource(e)).ToList();
        if (disk.Count == 0)
        {
            gaps.Add(new EvidenceGap("Corelare memorie–disc", EvidenceStatus.NotAvailable,
                "Cazul conține rânduri din memorie, dar nicio sursă de pe disc care arată ce programe au rulat (Prefetch, Amcache, ShimCache, servicii, evenimente 4688 sau Sysmon 1); regula MEM-NODISK nu poate fi evaluată",
                "Nu se poate spune care procese din memorie nu au urmă pe disc în probele cazului", "Importați Prefetch, Amcache, SYSTEM/ShimCache sau jurnalele Security/Sysmon ale aceleiași mașini", "Da, dacă sursele există"));
        }
        else
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in disk)
            {
                foreach (var s in new[] { d.Process, d.Path, d.Service }) if (s.Length > 0) names.Add(LeafName(s));
                foreach (Match m in Token.Matches(d.Summary)) names.Add(m.Value.ToLowerInvariant());
            }
            var sortedNames = names.OrderBy(x => x, StringComparer.Ordinal).ToList();
            bool Seen(string name) => names.Contains(name) || (name.Length >= 14 && sortedNames.Any(n => n.StartsWith(name, StringComparison.Ordinal)));   // Volatility cuts ImageFileName at 14 characters
            int count = 0;
            var candidates = mem.Where(e => (IsMem(e, "pslist") || IsMem(e, "psscan")) && e.Pid is not null && !Ended(e) && e.Process.Length > 0)
                                .GroupBy(Key).Select(g => g.OrderBy(x => x.Source, StringComparer.Ordinal).ThenBy(x => x.Locator, StringComparer.Ordinal).First()).OrderBy(e => e.Pid);
            foreach (var e in candidates)
            {
                if (count >= MaxPerRule) break;
                var cmdPath = CmdPath(e.Pid);
                var name = cmdPath.Length > 0 ? LeafName(cmdPath) : e.Process.Trim().ToLowerInvariant();
                if (name.Length == 0 || rules.Core.Contains(name) || rules.Core.Contains(e.Process.Trim().ToLowerInvariant()) && cmdPath.Length == 0) continue;
                if (Seen(name)) continue;
                var ev = new List<EvidenceRef> { Ref(e, $"proces în memorie ({e.Source["Memory:".Length..]})") };
                if (cmdPath.Length > 0 && cmdByPid.TryGetValue(e.Pid!.Value, out var cmd)) ev.Add(Ref(cmd, "linie de comandă (cmdline)"));
                findings.Add(Make(nextId, "MEM-NODISK", $"Proces din memorie fără urmă pe disc în probele cazului: {name} (PID {e.Pid})", Severity.Low, Classification.Unproven, Confidence.Low, e,
                    $"Programul {name} (PID {e.Pid}) rulează în memorie, dar numele lui nu apare în nicio sursă de pe disc a cazului (Prefetch, Amcache, ShimCache, servicii, evenimente 4688 sau Sysmon 1). Acesta este doar un indiciu informativ: înseamnă „fără urmă pe disc în probele cazului”, nu „fără fișier”. Sursele de pe disc pot fi incomplete (Prefetch dezactivat, jurnale rotite, alt moment de captură).",
                    ev, "", ["Sursele de pe disc nu acoperă momentul pornirii procesului", "Prefetch dezactivat sau jurnale rotite", "Probele de pe disc provin de la altă mașină"],
                    ["Importați sursele de pe disc ale aceleiași mașini și rulați din nou analiza"]));
                count++;
            }
        }
        return new MemoryFindingsResult(findings, gaps);
    }

    private static bool SafeMatch(Regex r, string s) { try { return r.IsMatch(s); } catch (RegexMatchTimeoutException) { return false; } }

    private static Finding Make(Func<string> nextId, string rule, string title, Severity sev, Classification cls, Confidence conf, TimelineEvent e, string description,
        List<EvidenceRef> evidence, string mitre, string[] alternatives, string[] next, List<string>? related = null) => new()
    {
        FindingId = nextId(), RuleId = rule, Title = title, Severity = sev, Category = "Memorie", Classification = cls, Confidence = conf,
        Process = e.Process, Pid = e.Pid, Ip = e.RemoteIp, Description = description, PlainWhat = description,
        ClassificationReason = Reason, SupportingEvidence = evidence, MitreTechniqueId = mitre,
        AlternativeExplanations = [.. alternatives], RecommendedNextSteps = [.. next], RelatedFindingIds = related ?? [],
        Limitations = [Reason],
    };
}
