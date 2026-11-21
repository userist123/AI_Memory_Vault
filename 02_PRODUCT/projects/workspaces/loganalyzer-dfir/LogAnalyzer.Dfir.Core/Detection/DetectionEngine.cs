using LogAnalyzer.Dfir.HashSets;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Detection;

/// <summary>
/// Loads detection rules from one or more folders (ioc/*.csv, yara/*.yar, sigma/*.yml) and runs them over a case:
/// Sigma and IOC on the timeline, hash IOC on the evidence files, YARA on evidence of type "file". A rule file that does
/// not load is reported as a gap with its error; it never stops the analysis and is never silently skipped.
/// </summary>
public sealed class DetectionEngine
{
    public const long MaxYaraFileBytes = 256L * 1024 * 1024;

    private readonly List<IocMatcher> _iocs = [];
    private readonly List<YaraLite> _yara = [];
    private readonly List<SigmaLite> _sigma = [];
    private readonly List<SigmaCorrelation> _correlations = [];
    /// <summary>Fingerprint sets imported by the operator (HashSetLibrary). Known-bad matches become hash detections in <see cref="Run"/>; known-good ones are labels, kept out of the detections.</summary>
    public IReadOnlyList<KnownHashSet> KnownHashSets { get; set; } = [];
    public List<EvidenceGap> LoadErrors { get; } = [];
    /// <summary>Rule evaluations that hit a regex match timeout in <see cref="Run"/>. Each is also in <see cref="LoadErrors"/> as a FAILED gap.</summary>
    public List<RuleTimeout> Timeouts { get; } = [];

    public IReadOnlyList<DetectionRule> Rules =>
        _iocs.Select(i => i.RuleSet)
            .Concat(KnownHashSets.Where(h => h.Info.Category == HashSetCategory.KnownBad).Select(h => new DetectionRule($"HASHSET:{h.Info.Id}", h.Info.Version, DetectionKind.Hash,
                $"Set de amprente {h.Info.Name} ({h.Info.EntryCount} intrări)", h.Info.SourceSha256, h.Info.SourceFileName)))
            .Concat(_yara.SelectMany(y => y.Rules.Select(r => r.Identity)))
            .Concat(_sigma.SelectMany(s => s.Rules.Select(r => r.Identity)))
            .Concat(_correlations.Select(c => c.Identity)).ToList();

    public static DetectionEngine Load(params string[] ruleRoots) => Load(null, ruleRoots);

    /// <param name="matchTimeout">Regex match timeout for every Sigma and YARA pattern; null keeps each engine's default.</param>
    /// <param name="ruleRoots">Folders holding ioc/, yara/ and sigma/ subfolders.</param>
    public static DetectionEngine Load(TimeSpan? matchTimeout, params string[] ruleRoots)
    {
        var e = new DetectionEngine();
        foreach (var root in ruleRoots.Where(Directory.Exists))
        {
            foreach (var f in Files(root, "ioc", "*.csv")) e.Try(f, () => e._iocs.Add(IocMatcher.LoadCsv(f)));
            foreach (var f in Files(root, "yara", "*.yar")) e.Try(f, () => e._yara.Add(YaraLite.Parse(File.ReadAllText(f), f, matchTimeout)));
            foreach (var f in Files(root, "sigma", "*.yml")) e.Try(f, () => e._sigma.Add(SigmaLite.Load(matchTimeout ?? SigmaLite.DefaultMatchTimeout, (Path.GetFileName(f), File.ReadAllText(f)))));
        }
        e.ResolveCorrelations();
        return e;
    }

    /// <summary>Correlation documents refer to base rules of any loaded file, so they are resolved once every file is loaded. A document that does not resolve is a load error (gap), never skipped.</summary>
    private void ResolveCorrelations()
    {
        var baseRules = _sigma.SelectMany(s => s.Rules).ToList();
        var pending = _sigma.SelectMany(s => s.Correlations).ToList();
        var corrRefs = pending.SelectMany(p => new[] { p.Id, p.Name }).Where(x => x.Length > 0).ToList();
        foreach (var p in pending) Try(p.File, () => _correlations.Add(SigmaCorrelation.Parse(p.File, p.Yaml, baseRules, corrRefs)));
    }

    private static IEnumerable<string> Files(string root, string sub, string pattern) =>
        Directory.Exists(Path.Combine(root, sub)) ? Directory.GetFiles(Path.Combine(root, sub), pattern).Order(StringComparer.Ordinal) : [];

    private void Try(string file, Action load)
    {
        try { load(); }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or YamlDotNet.Core.YamlException or OverflowException or ArgumentException)
        {
            LoadErrors.Add(new EvidenceGap($"Regulă {Path.GetFileName(file)}", EvidenceStatus.Failed, $"{ex.GetType().Name}: {ex.Message}",
                "Regula nu a fost aplicată", "Corectați fișierul regulii", "Da"));
        }
    }

    /// <param name="readEvidence">Returns the bytes of a stored evidence file (the caller controls access and size).</param>
    public List<DetectionResult> Run(IReadOnlyList<TimelineEvent> timeline, IReadOnlyList<EvidenceItem> evidence, Func<EvidenceItem, byte[]> readEvidence)
    {
        var results = new List<DetectionResult>();
        var timedOut = new List<RuleTimeout>();
        var baseMatches = new List<(DetectionRule Rule, TimelineEvent Event)>();
        foreach (var s in _sigma)
            foreach (var m in s.Match(timeline, timedOut))
            {
                results.Add(SigmaLite.ToResult(m.Rule, m.Event));      // base rules always report their own matches (generate has no effect)
                if (_correlations.Count > 0) baseMatches.Add((m.Rule.Identity, m.Event));
            }
        foreach (var c in _correlations)
        {
            var cr = c.Evaluate(baseMatches);
            results.AddRange(cr.Hits.Select(SigmaCorrelation.ToResult));
            foreach (var gap in cr.Gaps)
                LoadErrors.Add(new EvidenceGap($"Corelare {c.Title}", EvidenceStatus.Partial, gap, "Corelarea s-a calculat fără aceste evenimente: lipsa unei corelări nu înseamnă că activitatea nu a existat",
                    "Completați ora sau câmpul în sursă ori reanalizați cu parserul potrivit", "Parțial"));
            foreach (var t in timedOut.Where(t => c.Referenced.Any(r => r.RuleId == t.RuleId)))
                LoadErrors.Add(new EvidenceGap($"Corelare {c.Title}", EvidenceStatus.Partial, $"regula referită {t.RuleId} a depășit timpul la evaluare; corelarea poate fi incompletă",
                    "Corelarea nu a văzut toate potrivirile regulii", "Simplificați expresia regulată a regulii", "Da"));
        }
        foreach (var i in _iocs)
        {
            results.AddRange(i.MatchEvents(timeline));
            results.AddRange(i.MatchEvidence(evidence));
        }
        if (KnownHashSets.Count > 0) results.AddRange(HashSetMatcher.ToDetections(HashSetMatcher.Match(KnownHashSets, evidence, timeline)));
        if (_yara.Count > 0)
            foreach (var e in evidence.Where(e => e.SourceType == "file"))
            {
                if (e.Size > MaxYaraFileBytes)
                {
                    LoadErrors.Add(new EvidenceGap(e.OriginalName, EvidenceStatus.SkippedByDesign, $"fișier de {e.Size:N0} octeți, peste limita YARA de {MaxYaraFileBytes:N0}",
                        "Fișierul nu a fost scanat cu YARA", "Scanare separată", "Da"));
                    continue;
                }
                var data = readEvidence(e);
                foreach (var y in _yara)
                    foreach (var m in y.Scan(data, timedOut, e.EvidenceId))
                        results.Add(new DetectionResult(m.Rule.Identity.RuleId, m.Rule.Identity.Version, m.Rule.Identity.Sha256, DetectionKind.Yara,
                            m.Rule.Identity.Title, e.EvidenceId, "file", e.Sha256,
                            $"{e.OriginalName}: " + string.Join(", ", m.Counts.Where(c => c.Value > 0).Select(c => $"{c.Key}×{c.Value} (prima la 0x{m.FirstOffsets[c.Key]:X})")),
                            Classification.Direct, m.Rule.Meta.GetValueOrDefault("severity") is "high" or "critical" ? Confidence.High : Confidence.Medium,
                            $"Fișierul îndeplinește regula YARA {m.Rule.Name}: {m.Rule.Identity.Title}"));
            }
        foreach (var t in timedOut)
        {
            Timeouts.Add(t);
            LoadErrors.Add(new EvidenceGap($"Regulă {t.Title}", EvidenceStatus.Failed, $"Timeout la evaluarea regulii {t.RuleId}: {t.Detail}",
                "Regula nu a fost evaluată pe intrarea afectată: lipsa unei potriviri nu înseamnă că intrarea e curată", "Simplificați expresia regulată a regulii sau evaluați regula cu alt instrument", "Da"));
        }
        return results;
    }
}
