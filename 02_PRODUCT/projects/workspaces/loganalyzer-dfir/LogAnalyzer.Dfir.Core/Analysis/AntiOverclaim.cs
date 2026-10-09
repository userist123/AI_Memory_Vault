using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// The hard "never" rules of lessons learned §100, as functions the mapping uses and tests enforce:
/// artifact/presence never becomes execution or user action; correlation is never proof; missing evidence is never a clean
/// result; AI output is never verified; a configured policy is never "effective". Severity and Confidence are separate (§99):
/// nothing here reads one to compute the other.
/// </summary>
public static class AntiOverclaim
{
    /// <summary>Artifacts that record that something ran. Everything else is presence, configuration or observation.</summary>
    private static readonly HashSet<string> ExecutionArtifacts = new(StringComparer.OrdinalIgnoreCase) { "Prefetch", "BAM", "UserAssist" };

    /// <summary>Artifact → what it may be called. Amcache, ShimCache, LNK, Jump List, USB, USN are presence/observation, never execution or user action.</summary>
    public static SemanticType SemanticForArtifact(string artifact) => artifact switch
    {
        _ when ExecutionArtifacts.Contains(artifact) => SemanticType.Execution,
        "Amcache" or "ShimCache" or "LNK" or "JumpList" => SemanticType.Presence,
        "Service" or "ScheduledTask" or "RunKey" or "Winlogon" or "IFEO" or "SystemConfig" => SemanticType.Configuration,
        _ => SemanticType.Observation,
    };

    /// <summary>
    /// The highest state a statement may reach. Correlation and inference stop below VERIFIED/SUPPORTED-as-proof; an AI origin
    /// can never exceed INFERRED; nothing here ever returns VERIFIED, because only the verifier (WP4) sets that.
    /// </summary>
    public static StandardState Constrain(StandardState requested, SemanticType type, bool fromAi = false)
    {
        if (requested == StandardState.Verified) requested = StandardState.Supported; // verification is granted by the verifier, not by mapping
        if (fromAi && requested is StandardState.Supported or StandardState.Observed or StandardState.Correlated) requested = StandardState.Inferred;
        if (type is SemanticType.Correlation && requested is StandardState.Supported) requested = StandardState.Correlated;
        if (type is SemanticType.Inference or SemanticType.Attribution && requested is StandardState.Observed or StandardState.Supported or StandardState.Correlated)
            requested = StandardState.Inferred;
        return requested;
    }

    /// <summary>What a run with no findings may say. Never "clean": gaps → UNKNOWN; no gaps → still UNKNOWN (no detection is not no compromise).</summary>
    public static StandardState NoFindingsConclusion(IReadOnlyCollection<EvidenceGap> gaps) => StandardState.Unknown;

    /// <summary>A configured policy is "effective" only with separate proof; configuration alone is NOT_ASSESSED.</summary>
    public static StandardState PolicyEffectiveness(bool configured, bool provenEffective) =>
        provenEffective ? StandardState.Supported : configured ? StandardState.NotAssessed : StandardState.Unknown;

    /// <summary>Breaches of the contract on one finding; empty = none. Used by the pipeline (audit entry) and the tests.</summary>
    public static List<string> Violations(Finding f)
    {
        var v = new List<string>();
        if (f.Status == StandardState.Verified && (f.Verification.State != StandardState.Verified || f.Verification.Verifier.Length == 0))
            v.Add("VERIFIED fără verificator");
        if (f.SemanticType == SemanticType.Correlation && f.Status is StandardState.Verified or StandardState.Supported)
            v.Add("corelația tratată ca dovadă");
        if (f.SemanticType is SemanticType.Presence or SemanticType.Configuration && f.Status == StandardState.Verified)
            v.Add("prezența/configurația tratată ca verificată");
        if (f.SemanticType == SemanticType.Attribution && f.Status is not (StandardState.Inferred or StandardState.Unproven or StandardState.Unknown or StandardState.NotAssessed))
            v.Add("atribuire fără dovezi separate");
        return v;
    }
}
