using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Detection;

public enum RuleLabKind { Sigma, Yara, Ioc }

public sealed record RuleLabMatch(string RuleId, string EvidenceId, string Locator, DateTimeOffset? TimeUtc, string Match)
{
    public string Key => $"{RuleId}|{EvidenceId}|{Locator}";
}

/// <summary>One test of a rule on a case: whether it loaded, why not, and what it matched (with the counts per evidence item).</summary>
public sealed class RuleLabRun
{
    public bool Valid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public List<RuleLabMatch> Matches { get; } = [];
    /// <summary>What the result cannot show, said next to it (for example rule fields the corpus does not carry).</summary>
    public List<string> Notes { get; } = [];
    public int CorpusEvents { get; init; }
    public int CorpusFiles { get; init; }
    public string Summary => !Valid ? "regula NU s-a încărcat: " + string.Join("; ", Errors)
        : $"{LogAnalyzer.Dfir.Language.RoText.Count(Matches.Count, "potrivire", "potriviri")} în {CorpusEvents:N0} evenimente și {CorpusFiles} fișiere" + (Notes.Count > 0 ? ". ATENȚIE: " + string.Join(" ", Notes) : "");
}

public sealed record RuleLabDiff(IReadOnlyList<RuleLabMatch> Added, IReadOnlyList<RuleLabMatch> Removed, int Kept)
{
    public string Summary => $"față de versiunea anterioară: {Added.Count} potriviri noi, {Removed.Count} pierdute, {Kept} păstrate";
}

/// <summary>
/// Master plan 6.8, the rule lab: a Sigma, YARA or IOC rule is loaded exactly as the detection engine loads it (same parser, same errors), run
/// on the timeline and files of a case, compared with an earlier version of itself (matches added and lost) and, once it loads, added to the
/// case's own Rules folder (audited, hashed into custody) so the next analysis of the case uses it. A rule is never changed or "fixed" here:
/// what does not load is refused with the parser's reason.
/// </summary>
public static class RuleLab
{
    public static string Folder(RuleLabKind k) => k switch { RuleLabKind.Sigma => "sigma", RuleLabKind.Yara => "yara", _ => "ioc" };
    public static string Extension(RuleLabKind k) => k switch { RuleLabKind.Sigma => ".yml", RuleLabKind.Yara => ".yar", _ => ".csv" };

    /// <summary>A file name the rule can be stored under: letters, digits, - and _, with the kind's extension.</summary>
    public static string? SafeName(string name, RuleLabKind kind)
    {
        name = (name ?? "").Trim();
        if (name.IndexOfAny(['/', '\\', ':']) >= 0) return null;   // a path is refused, never silently cut to its last part
        var stem = name.EndsWith(Extension(kind), StringComparison.OrdinalIgnoreCase) ? name[..^Extension(kind).Length] : name;
        if (!System.Text.RegularExpressions.Regex.IsMatch(stem, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")) return null;
        return stem + Extension(kind);
    }

    public static RuleLabRun Test(RuleLabKind kind, string name, string ruleText, IReadOnlyList<TimelineEvent> timeline, IReadOnlyList<EvidenceItem> files, Func<EvidenceItem, byte[]> read)
    {
        var file = SafeName(name, kind);
        var run = new RuleLabRun { CorpusEvents = timeline.Count, CorpusFiles = kind == RuleLabKind.Yara ? files.Count : 0 };
        if (file is null) { run.Errors.Add("numele regulii: litere, cifre, - și _, cel mult 64 de caractere"); return run; }
        if (string.IsNullOrWhiteSpace(ruleText)) { run.Errors.Add("regula este goală"); return run; }
        if (kind == RuleLabKind.Sigma && SigmaCorrelation(ruleText)) { run.Errors.Add("regulile de corelare Sigma (correlation) nu sunt acceptate de motorul de reguli; secvențele se descriu în regulile de secvență"); return run; }

        var root = Path.Combine(Path.GetTempPath(), "LogAnalyzer-rulelab", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, Folder(kind)));
            File.WriteAllText(Path.Combine(root, Folder(kind), file), ruleText);
            var engine = DetectionEngine.Load(root);
            foreach (var g in engine.LoadErrors) run.Errors.Add(g.Reason);
            if (!run.Valid) return run;
            if (engine.Rules.Count == 0) { run.Errors.Add("fișierul nu conține nicio regulă"); return run; }
            foreach (var d in engine.Run(timeline, kind == RuleLabKind.Yara ? files : [], read))
                run.Matches.Add(new RuleLabMatch(d.RuleId, d.EvidenceId, d.Locator, d.TimeUtc, d.Match));
            foreach (var g in engine.LoadErrors) run.Errors.Add(g.Reason);   // a regex timeout during the run is an error of the rule too
            if (kind == RuleLabKind.Sigma)
            {
                // lab 2026-11-08: a reopened case reads its timeline from timeline.csv, which keeps no parser fields, so a rule on TargetUserName
                // matched nothing and only "0" was shown. Fields no corpus event carries are named.
                var missing = SigmaFields(ruleText).Where(f => !timeline.Any(e => e.Fields.ContainsKey(f))).ToList();
                if (missing.Count > 0)
                    run.Notes.Add($"niciun eveniment al cazului nu are câmpurile {string.Join(", ", missing)}, deci regula nu le poate potrivi; un caz redeschis are cronologia din timeline.csv, fără câmpurile parserelor: reanalizați cazul (tab „Rulări și diferențe”) și testați din nou.");
            }
            return run;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static readonly HashSet<string> ColumnFields = new(StringComparer.OrdinalIgnoreCase) { "EventID", "Channel", "Provider_Name", "Computer", "Image", "CommandLine", "User" };

    /// <summary>Field names a Sigma rule tests (the keys inside its selections), minus those the engine reads from the event columns.</summary>
    private static IEnumerable<string> SigmaFields(string yaml) =>
        System.Text.RegularExpressions.Regex.Matches(yaml, @"^\s{2,}(?:-\s*)?([A-Za-z][A-Za-z0-9_]*)(?:\|[^:\s]*)?\s*:", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).Where(f => f is not ("condition" or "sel" or "selection" or "filter") && !ColumnFields.Contains(f) && !f.StartsWith("sel", StringComparison.OrdinalIgnoreCase) && !f.StartsWith("filter", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sigma correlation rules carry a top-level "correlation:" key and no "detection:".</summary>
    private static bool SigmaCorrelation(string yaml) =>
        System.Text.RegularExpressions.Regex.IsMatch(yaml, @"^correlation\s*:", System.Text.RegularExpressions.RegexOptions.Multiline);

    public static RuleLabDiff Diff(RuleLabRun before, RuleLabRun after)
    {
        var b = before.Matches.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var a = after.Matches.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        return new RuleLabDiff(after.Matches.Where(m => !b.Contains(m.Key)).ToList(), before.Matches.Where(m => !a.Contains(m.Key)).ToList(), a.Count(b.Contains));
    }

    /// <summary>
    /// Adds a rule that loads to the case's Rules folder (case-specific rules, used by the next analysis). Refused on a read-only case, for a rule
    /// that does not load, or over an existing file (a new version gets a new name, so earlier analyses stay explainable).
    /// </summary>
    public static StateResult AddToCase(CaseWorkspace ws, RuleLabKind kind, string name, string ruleText, RuleLabRun tested)
    {
        if (ws.IsReadOnly) return StateResult.Refuse("caz doar pentru citire: " + ws.ReadOnlyReason);
        if (!tested.Valid) return StateResult.Refuse("regula nu s-a încărcat la test; nu se adaugă");
        var file = SafeName(name, kind);
        if (file is null) return StateResult.Refuse("numele regulii nu este valid");
        var rel = $"Rules/{Folder(kind)}/{file}";
        var full = Path.Combine(ws.Root, "Rules", Folder(kind), file);
        if (File.Exists(full)) return StateResult.Refuse($"există deja {rel}; o versiune nouă primește alt nume, ca analizele anterioare să rămână explicabile");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false))) w.Write(ruleText);
        ws.RecordOutput(rel, "RuleLab", "1");
        ws.Audit("rule.added_to_case", $"{rel} {tested.Summary}");
        return StateResult.Success;
    }
}
