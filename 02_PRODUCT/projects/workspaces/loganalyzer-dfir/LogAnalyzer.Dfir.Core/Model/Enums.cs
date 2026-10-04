namespace LogAnalyzer.Dfir.Model;

/// <summary>Outcome of collecting or parsing one source (spec §8). EMPTY != FAILED, FAILED != NOT_AVAILABLE.</summary>
public enum EvidenceStatus { Success, Empty, Failed, NotAvailable, Partial, SkippedByDesign }

/// <summary>When the data describes the system (spec §112).</summary>
public enum TemporalType { Historical, Live, CurrentSnapshot, Derived, Unknown }

/// <summary>Strength of a claim (spec §37/§52). Never upgraded by inference alone.</summary>
public enum Classification { Direct, Correlated, Candidate, Unproven, BenignKnown }

public enum Confidence { Low, Medium, High }

public enum Severity { Info, Low, Medium, High, Critical }

public enum Sensitivity { Public, Internal, Confidential, HighlySensitive }

public static class SpecNames
{
    public static string ToSpec(this EvidenceStatus s) => s switch
    {
        EvidenceStatus.Success => "SUCCESS",
        EvidenceStatus.Empty => "EMPTY",
        EvidenceStatus.Failed => "FAILED",
        EvidenceStatus.NotAvailable => "NOT_AVAILABLE",
        EvidenceStatus.Partial => "PARTIAL",
        EvidenceStatus.SkippedByDesign => "SKIPPED_BY_DESIGN",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static string ToSpec(this TemporalType t) => t switch
    {
        TemporalType.Historical => "HISTORICAL",
        TemporalType.Live => "LIVE",
        TemporalType.CurrentSnapshot => "CURRENT_SNAPSHOT",
        TemporalType.Derived => "DERIVED",
        _ => "UNKNOWN",
    };

    public static string ToSpec(this Classification c) => c switch
    {
        Classification.Direct => "DIRECT",
        Classification.Correlated => "CORRELATED",
        Classification.Candidate => "CANDIDATE",
        Classification.Unproven => "UNPROVEN",
        Classification.BenignKnown => "BENIGN/KNOWN",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    public static string ToSpec(this Confidence c) => c.ToString().ToUpperInvariant();
    public static string ToSpec(this Severity s) => s.ToString().ToUpperInvariant();

    public static string ToSpec(this Sensitivity s) => s switch
    {
        Sensitivity.HighlySensitive => "HIGHLY_SENSITIVE",
        _ => s.ToString().ToUpperInvariant(),
    };
}
