using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>Inputs the contract step needs from the run: which parsers produced rows for an evidence item, and the clock.</summary>
public sealed class FindingContractContext
{
    public Func<string, IEnumerable<string>> ParsersOf { get; init; } = _ => [];
    public DateTimeOffset? NowUtc { get; init; }
    public string ApplicationVersion { get; init; } = DfirInfo.ApplicationVersion;
}

/// <summary>
/// Applies the finding contract (program requirements §5/§19/§20) to findings the pipeline produced: semantic type, standard
/// state, verification (NOT_ASSESSED), limitations, provenance, audit trail, message keys, summaries, and the rule's default
/// missing evidence and next steps. The standard state is DERIVED from <see cref="Classification"/> and added beside it.
/// Severity and Confidence are never read or changed here (lessons learned §99).
/// </summary>
public static class FindingContract
{
    public const string Version = "1.0";
    private const string Actor = "FindingContract/" + Version;

    /// <summary>Classification → standard state. Deterministic and conservative: nothing maps to SUPPORTED or VERIFIED (the verifier decides those).</summary>
    public static StandardState StatusFor(Classification c) => c switch
    {
        Classification.Direct => StandardState.Observed,
        Classification.Correlated => StandardState.Correlated,
        Classification.Candidate => StandardState.Inferred,
        Classification.Unproven => StandardState.Unproven,
        Classification.BenignKnown => StandardState.Observed,
        _ => StandardState.Unknown,
    };

    public static string KeyBase(string ruleId) => "finding." + ruleId.ToLowerInvariant().Replace('-', '_');

    public static IEnumerable<Finding> Enrich(IEnumerable<Finding> findings, FindingContractContext? ctx = null)
    {
        ctx ??= new FindingContractContext();
        var list = findings.ToList();
        foreach (var f in list) Apply(f, ctx);
        // A chain inherits the contradictions of its steps: a step that is weakened weakens the story built on it.
        var byId = list.Where(f => f.FindingId.Length > 0).GroupBy(f => f.FindingId).ToDictionary(g => g.Key, g => g.First());
        foreach (var chain in list.Where(f => f.RelatedFindingIds.Count > 0))
            foreach (var id in chain.RelatedFindingIds)
                if (byId.TryGetValue(id, out var step))
                    foreach (var c in step.ContradictingEvidence)
                    {
                        var line = $"{id}: {c}";
                        if (!chain.ContradictingEvidence.Contains(line)) chain.ContradictingEvidence.Add(line);
                    }
        foreach (var f in list) Finish(f, ctx);
        return list;
    }

    private static void Apply(Finding f, FindingContractContext ctx)
    {
        var rule = RuleContracts.For(f.RuleId);
        var semBasis = f.HasSemanticType ? "producer" : rule is not null ? "rule catalog" : "default (rule not in catalog)";
        if (!f.HasSemanticType && rule is not null) f.SemanticType = rule.Semantic(f);
        f.Status = AntiOverclaim.Constrain(StatusFor(f.Classification), f.SemanticType);
        f.ContractVersion = Version;
        f.TitleKey = KeyBase(f.RuleId) + ".title";
        f.SummaryKey = KeyBase(f.RuleId) + ".summary";
        if (f.Verification.State == StandardState.NotAssessed) f.Verification = FindingVerification.NotAssessed();

        if (rule is not null)
        {
            if (f.MissingEvidence.Count == 0) f.MissingEvidence.AddRange(rule.MissingEvidence);
            if (f.RecommendedNextSteps.Count == 0) f.RecommendedNextSteps.AddRange(rule.NextSteps);
        }

        var lim = new List<string>();
        if (rule is not null) lim.AddRange(rule.Limitations);
        else lim.Add($"Regula {f.RuleId} nu are intrare în catalogul contractului: limitările, probele lipsă și pașii următori nu au fost evaluați pentru ea.");
        lim.Add(SemanticLimitation(f.SemanticType));
        var refs = f.SupportingEvidence;
        var parsers = refs.SelectMany(r => ctx.ParsersOf(r.EvidenceId)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (var p in parsers)
            if (ParserCapabilities.For(p) is { } cap)
                foreach (var c in cap.CannotProve) lim.Add($"{p} nu poate dovedi: {c}");
        if (rule is not null && rule.ContradictionCheck is null)
            lim.Add("Dovezi contradictorii: regula nu le evaluează; lipsa lor în listă nu înseamnă că nu există.");
        lim.Add("Verificare independentă: neefectuată (NOT_ASSESSED).");
        foreach (var l in lim.Where(l => l.Length > 0))
            if (!f.Limitations.Contains(l)) f.Limitations.Add(l);

        f.Provenance = new FindingProvenance(
            f.RuleId, RuleContract.RuleVersion, rule?.Producer ?? "unknown", ctx.ApplicationVersion,
            refs.Select(r => r.EvidenceId).Distinct(StringComparer.Ordinal).ToList(),
            refs.Select(r => r.Sha256).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
            parsers, Version);

        f.AuditTrail.RemoveAll(a => a.Actor.StartsWith("FindingContract/", StringComparison.Ordinal) || a.Step == "rule.fired");
        f.AuditTrail.Add(new("rule.fired", (rule?.Producer ?? "unknown") + "/" + f.RuleId, f.ClassificationReason));
        f.AuditTrail.Add(new("classification.mapped", Actor,
            $"Classification {f.Classification.ToSpec()} → stare {f.Status.ToSpec()}; tip semantic {f.SemanticType.ToSpec()} ({semBasis})", ctx.NowUtc));
        f.AuditTrail.Add(new("contradiction.check", Actor,
            rule?.ContradictionCheck is { } chk ? $"{chk}: {f.ContradictingEvidence.Count} găsite" : "neevaluat de regulă"));
        if (f.Verification.State == StandardState.NotAssessed) f.AuditTrail.Add(new("verification.default", Actor, f.Verification.Reason));
    }

    private static void Finish(Finding f, FindingContractContext ctx)
    {
        var rule = RuleContracts.For(f.RuleId);
        var viol = AntiOverclaim.Violations(f);
        f.AuditTrail.Add(new("anti_overclaim.check", Actor, viol.Count == 0 ? "fără încălcări" : string.Join("; ", viol)));
        f.HumanSummary = $"{f.Title}. {rule?.Meaning ?? "Regulă fără descriere în catalog."} Stare: {StateLabels.Romanian(f.Status)}.";
        f.TechnicalSummary = $"{f.RuleId} v{RuleContract.RuleVersion} · severitate {f.Severity.ToSpec()} · încredere {f.Confidence.ToSpec()} · " +
                             $"{f.Classification.ToSpec()} → {f.Status.ToSpec()} · {f.SemanticType.ToSpec()} · {f.SupportingEvidence.Count} referințe de probă" +
                             (f.MitreTechniqueId.Length > 0 ? $" · {f.MitreTechniqueId}" : "");
    }

    public static string SemanticLimitation(SemanticType t) => t switch
    {
        SemanticType.Presence => "Prezența unui artefact nu dovedește execuția sau o acțiune a utilizatorului.",
        SemanticType.Execution => "Execuția unui program nu arată cine l-a pornit și nici intenția.",
        SemanticType.Configuration => "Configurația arată ce este setat, nu că a fost folosit.",
        SemanticType.Correlation => "Corelația (în timp, în cale sau între surse) nu dovedește cauzalitate.",
        SemanticType.Inference => "Este o deducție, nu o observație directă.",
        SemanticType.Attribution => "Atribuirea cere dovezi separate, care nu există în acest caz.",
        _ => "",
    };
}

/// <summary>
/// UI labels of the ten states (UX contract §7) and of the seven operation states, from the resource layer (<c>state.*</c>, <c>op.*</c>): one table, two languages.
/// <see cref="Label(StandardState)"/> follows the current UI language; <c>Romanian</c> pins Romanian (stored-case checks, tests).
/// </summary>
public static class StateLabels
{
    private static string Key(StandardState s) => "state." + s.ToSpec().ToLowerInvariant();

    public static IReadOnlyDictionary<StandardState, string> RomanianTable { get; } = new RomanianView<StandardState>(s => Loc.T(Key(s), AppLanguage.Romanian));

    /// <summary>The label in the current UI language.</summary>
    public static string Label(StandardState s) => Loc.T(Key(s));
    public static string Label(StandardState s, AppLanguage lang) => Loc.T(Key(s), lang);
    public static string Romanian(StandardState s) => Loc.T(Key(s), AppLanguage.Romanian);

    /// <summary>
    /// The label of a classification, through the same mapping the finding contract uses (<see cref="FindingContract.StatusFor"/>) and the same table
    /// as <see cref="Label(StandardState)"/>. The grids, the graph view and the finding card use this instead of any wording of their own.
    /// </summary>
    public static string ForClassification(Classification c) => Label(FindingContract.StatusFor(c));

    /// <summary>One sentence on what a state means and does not mean, for tooltips and the finding card; so a state is never only a word or a colour.</summary>
    public static string Meaning(StandardState s) => Loc.T(Key(s) + ".meaning");
    public static string Meaning(StandardState s, AppLanguage lang) => Loc.T(Key(s) + ".meaning", lang);

    public static IReadOnlyDictionary<StandardState, string> MeaningTable { get; } = new RomanianView<StandardState>(s => Loc.T(Key(s) + ".meaning", AppLanguage.Romanian));

    private static string OpKey(OperationState s) => "op." + s.ToSpec().ToLowerInvariant();

    public static string Operation(OperationState s) => Loc.T(OpKey(s));
    public static IReadOnlyDictionary<OperationState, string> RomanianOperation { get; } = new RomanianView<OperationState>(s => Loc.T(OpKey(s), AppLanguage.Romanian));

    /// <summary>Read-only view over every value of an enum, with the text produced on demand from the resource layer in Romanian.</summary>
    private sealed class RomanianView<T>(Func<T, string> text) : IReadOnlyDictionary<T, string> where T : struct, Enum
    {
        private static readonly T[] All = Enum.GetValues<T>();
        public string this[T key] => text(key);
        public IEnumerable<T> Keys => All;
        public IEnumerable<string> Values => All.Select(text);
        public int Count => All.Length;
        public bool ContainsKey(T key) => true;
        public bool TryGetValue(T key, out string value) { value = text(key); return true; }
        public IEnumerator<KeyValuePair<T, string>> GetEnumerator() => All.Select(k => new KeyValuePair<T, string>(k, text(k))).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>
/// Maps the older vocabularies onto the standard ones. Conservative: presence never becomes execution, and "possible"
/// never becomes "proven". The legacy <c>EvidenceStrength</c> (LogAnalyzer.Core, unwired) is mapped by name so this
/// assembly does not depend on it; a test checks every legacy value is covered.
/// </summary>
public static class LegacyVocabulary
{
    /// <param name="strengthName">Name of a legacy <c>EvidenceStrength</c> value.</param>
    /// <param name="artifactType">Legacy artifact type ("Prefetch", "Amcache", "BAM", ...). ExecutionProven covered Amcache too, which is only presence.</param>
    public static (SemanticType Type, StandardState State) FromEvidenceStrength(string strengthName, string artifactType = "") => strengthName switch
    {
        "ExecutionProven" => AntiOverclaim.SemanticForArtifact(artifactType) == SemanticType.Execution
            ? (SemanticType.Execution, StandardState.Observed)
            : (SemanticType.Presence, StandardState.Observed),
        "ExecutionPossible" => (SemanticType.Inference, StandardState.Inferred),
        "FileExistenceOnly" => (SemanticType.Presence, StandardState.Observed),
        "ConfigurationOnly" => (SemanticType.Configuration, StandardState.Observed),
        "ContextOnly" => (SemanticType.Observation, StandardState.Observed),
        _ => (SemanticType.Observation, StandardState.NotAssessed),
    };

    public static readonly IReadOnlyList<string> KnownStrengths = ["ExecutionProven", "ExecutionPossible", "FileExistenceOnly", "ConfigurationOnly", "ContextOnly"];
}
