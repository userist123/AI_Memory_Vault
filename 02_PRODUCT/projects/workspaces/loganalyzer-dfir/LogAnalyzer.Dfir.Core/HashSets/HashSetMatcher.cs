using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.HashSets;

/// <summary>One hash of the case found in one set. <see cref="Source"/> is "probă" (SHA-256 recorded at acquisition) or "cronologie" (hash field of a parsed row).</summary>
public sealed record HashSetMatch(string SetId, string SetName, string Category, string SetSourceFile, string SetSourceSha256,
    string EvidenceId, string Path, string Locator, string Algorithm, string Hash, string Source, string EvidenceSha256)
{
    public bool IsKnownBad => Category == HashSetCategory.KnownBad;
    /// <summary>"cunoscut-bun după setul X" is a label, not a verdict: the item stays in the case and is not "curat".</summary>
    public string Label => IsKnownBad
        ? $"cunoscut-rău după setul „{SetName}”"
        : $"cunoscut-bun după setul „{SetName}” (etichetă informativă: proba rămâne în caz și nu este declarată curată)";
}

/// <summary>Matches the hashes of a case against the sets. A hash in both a known-good and a known-bad set yields both matches (visible conflict).</summary>
public static class HashSetMatcher
{
    public static List<HashSetMatch> Match(IReadOnlyList<KnownHashSet> sets, IEnumerable<EvidenceItem> evidence, IEnumerable<TimelineEvent>? timeline = null)
    {
        var res = new List<HashSetMatch>();
        if (sets.Count == 0) return res;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Try(string hash, string evidenceId, string path, string locator, string source, string evSha)
        {
            if (hash.Length is not (32 or 40 or 64)) return;
            foreach (var s in sets)
                if (s.Contains(hash) && seen.Add($"{s.Info.Id}|{evidenceId}|{hash.ToUpperInvariant()}|{(source == "probă" ? "" : locator)}"))
                    res.Add(new HashSetMatch(s.Info.Id, s.Info.Name, s.Info.Category, s.Info.SourceFileName, s.Info.SourceSha256, evidenceId, path, locator,
                        KnownHashSet.AlgorithmOf(hash)!, hash.ToUpperInvariant(), source, evSha));
        }
        foreach (var e in evidence)
            Try(e.Sha256, e.EvidenceId, string.IsNullOrEmpty(e.OriginalPath) ? e.OriginalName : e.OriginalPath, "file", "probă", e.Sha256);
        if (timeline is not null)
            foreach (var ev in timeline)
                foreach (var h in new[] { ev.Hash, ev.Fields.GetValueOrDefault("SHA256", ""), ev.Fields.GetValueOrDefault("SHA1", ""), ev.Fields.GetValueOrDefault("MD5", "") }
                    .Concat(ParseHashesField(ev.Fields.GetValueOrDefault("Hashes", ""))))
                    if (!string.IsNullOrEmpty(h)) Try(h.Trim(), ev.EvidenceId, ev.Path, ev.Locator, "cronologie", ev.SourceSha256);
        return res;
    }

    /// <summary>What a set can and cannot match, shown in the card and in the investigation result.</summary>
    public const string CoverageNote = "Probele din caz se compară după SHA-256; seturile doar MD5 / SHA-1 potrivesc doar câmpurile de eveniment care au acele amprente (Sysmon, Amcache).";

    /// <summary>
    /// The Sysmon <c>Hashes</c> field (<c>SHA1=…,MD5=…,SHA256=…,IMPHASH=…</c>, same separators as the parser of the remote-tool rules): each value is taken by its
    /// algorithm name and length; IMPHASH (also 32 hex characters) is not an MD5 and is left out.
    /// </summary>
    public static IEnumerable<string> ParseHashesField(string hashes)
    {
        foreach (var part in hashes.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var value = part[(eq + 1)..];
            var ok = part[..eq].ToUpperInvariant().Replace("-", "") switch { "SHA256" => value.Length == 64, "SHA1" => value.Length == 40, "MD5" => value.Length == 32, _ => false };
            if (ok) yield return value;
        }
    }

    /// <summary>Detection results for the known-bad matches only (same <see cref="DetectionKind.Hash"/> kind as the hash IOCs).</summary>
    public static IEnumerable<DetectionResult> ToDetections(IEnumerable<HashSetMatch> matches) =>
        matches.Where(m => m.IsKnownBad).Select(m => new DetectionResult(
            $"HASHSET:{m.SetId}", m.SetSourceSha256.Length >= 12 ? m.SetSourceSha256[..12] : m.SetSourceSha256, m.SetSourceSha256, DetectionKind.Hash,
            $"Amprentă din setul cunoscut-rău „{m.SetName}”", m.EvidenceId, m.Locator, m.EvidenceSha256,
            $"{m.Algorithm} {m.Hash} ({m.Path})", Classification.Direct, Confidence.High,
            $"Amprenta {m.Algorithm} {m.Hash} a {m.Path} apare în setul „{m.SetName}” (categoria cunoscut-rău, sursa {m.SetSourceFile}, SHA-256 al sursei {m.SetSourceSha256}). " +
            "Potrivirea arată că amprenta este în listă; nu dovedește singură cum a ajuns fișierul pe stație."));

    /// <summary>One high-severity finding per (set, evidence, hash) for the known-bad matches.</summary>
    public static IEnumerable<Finding> ToFindings(IEnumerable<HashSetMatch> matches, Func<string> nextId) =>
        matches.Where(m => m.IsKnownBad).GroupBy(m => (m.SetId, m.EvidenceId, m.Hash)).Select(g =>
        {
            var m = g.First();
            return new Finding
            {
                FindingId = nextId(), RuleId = "HASHSET-KNOWN-BAD", Title = $"Amprentă cunoscut-rău (set „{m.SetName}”): {System.IO.Path.GetFileName(m.Path)}",
                Severity = Severity.High, Category = "KnownBadHash", Classification = Classification.Direct, Confidence = Confidence.High, File = m.Path,
                PlainWhat = $"Există în probe fișierul {LogAnalyzer.Dfir.Analysis.PlainPhrases.Q(LogAnalyzer.Dfir.Analysis.PlainPhrases.Leaf(m.Path))}, a cărui amprentă (identificatorul unic al conținutului) se găsește în lista de fișiere periculoase „{m.SetName}”, importată de operator; lista poate conține și un fișier legitim, deci sursa ei trebuie verificată",
                Description = $"{m.Algorithm} {m.Hash} apare în setul „{m.SetName}” (categoria cunoscut-rău; sursa {m.SetSourceFile}, SHA-256 {m.SetSourceSha256}). Proba: {m.EvidenceId}, {m.Path}.",
                ClassificationReason = "Potrivire exactă de amprentă cu o listă importată de operator.",
                SupportingEvidence = [new EvidenceRef(m.EvidenceId, m.Locator, $"{m.Algorithm} {m.Hash} în setul „{m.SetName}” ({m.SetSourceFile})", m.EvidenceSha256)],
                AlternativeExplanations = ["Lista poate conține o amprentă a unui fișier legitim; verificați sursa listei."],
                RecommendedNextSteps = ["Confirmați amprenta în sursa listei și analizați fișierul într-un mediu izolat."],
            };
        });
}
