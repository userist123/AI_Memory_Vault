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

/// <summary>Romanian UI labels of the ten states (UX contract §7), as a table the UI can bind to. The full localisation layer is a later package.</summary>
public static class StateLabels
{
    public static IReadOnlyDictionary<StandardState, string> RomanianTable { get; } = new Dictionary<StandardState, string>
    {
        [StandardState.Observed] = "Observat",
        [StandardState.Correlated] = "Corelat",
        [StandardState.Supported] = "Susținut de dovezi",
        [StandardState.Verified] = "Verificat",
        [StandardState.Inferred] = "Deducție",
        [StandardState.Unproven] = "Nedemonstrat",
        [StandardState.Contradicted] = "Contrazis",
        [StandardState.Rejected] = "Respins",
        [StandardState.Unknown] = "Necunoscut",
        [StandardState.NotAssessed] = "Neevaluat",
    };

    public static string Romanian(StandardState s) => RomanianTable[s];

    /// <summary>
    /// The label of a classification, through the same mapping the finding contract uses (<see cref="FindingContract.StatusFor"/>) and the same table
    /// as <see cref="Romanian"/>. The grids, the graph view and the finding card use this instead of any wording of their own.
    /// </summary>
    public static string ForClassification(Classification c) => Romanian(FindingContract.StatusFor(c));

    /// <summary>One sentence on what a state means and does not mean, for tooltips and the finding card; so a state is never only a word or a colour.</summary>
    public static string Meaning(StandardState s) => MeaningTable[s];

    public static IReadOnlyDictionary<StandardState, string> MeaningTable { get; } = new Dictionary<StandardState, string>
    {
        [StandardState.Observed] = "Faptul apare direct într-o probă din caz.",
        [StandardState.Correlated] = "Mai multe fapte observate se potrivesc (în timp, în cale sau în sursă); corelația nu dovedește cauzalitate.",
        [StandardState.Supported] = "Verificarea a găsit în caz dovezi care susțin constatarea.",
        [StandardState.Verified] = "Verificarea automată a confirmat constatarea față de probele din caz (nu este o verificare externă sau umană).",
        [StandardState.Inferred] = "Este o deducție din fapte observate, nu o observație directă.",
        [StandardState.Unproven] = "Probele din caz nu sunt suficiente pentru a o demonstra.",
        [StandardState.Contradicted] = "Există dovezi în caz care contrazic constatarea.",
        [StandardState.Rejected] = "Verificarea a respins constatarea.",
        [StandardState.Unknown] = "Nu se poate spune cu probele din caz.",
        [StandardState.NotAssessed] = "Nicio verificare nu a evaluat încă această constatare.",
    };

    public static IReadOnlyDictionary<OperationState, string> RomanianOperation { get; } = new Dictionary<OperationState, string>
    {
        [OperationState.NotStarted] = "Neînceput",
        [OperationState.Running] = "În desfășurare",
        [OperationState.Completed] = "Finalizat",
        [OperationState.Partial] = "Parțial",
        [OperationState.Failed] = "Eșuat",
        [OperationState.Cancelled] = "Anulat",
        [OperationState.Blocked] = "Blocat",
    };
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
