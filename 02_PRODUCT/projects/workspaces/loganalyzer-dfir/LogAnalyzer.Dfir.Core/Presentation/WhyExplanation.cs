using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>A score computed by the legacy heuristic engine, with the factors it was computed from. A score without factors is never shown.</summary>
public sealed record LegacyScore(int Score, IReadOnlyList<string> Factors);

/// <summary>
/// The universal „De ce?” (UX contract §5, U5): what was observed, the evidence, the reasoning, the limits and the verification verdict. It is built from
/// fields the finding already has (<c>Description</c>, <c>SupportingEvidence</c>, <c>ClassificationReason</c>, <c>MissingEvidence</c>, <c>AlternativeExplanations</c>,
/// <c>Limitations</c>, <c>Verification</c>) and never reduces to a percentage or a score.
/// </summary>
public sealed record WhyExplanation(
    string Observation, string EvidenceSummary, IReadOnlyList<string> EvidenceLines, string Reasoning, IReadOnlyList<string> Limitations,
    string Verification, IReadOnlyList<string> VerificationChecks, string? LegacyScoreNote)
{
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ce am observat").AppendLine("  " + Observation).AppendLine();
        sb.AppendLine("Dovezi").AppendLine("  " + EvidenceSummary);
        foreach (var l in EvidenceLines) sb.AppendLine("  • " + l);
        sb.AppendLine().AppendLine("Raționament").AppendLine("  " + Reasoning).AppendLine();
        sb.AppendLine("Limite");
        if (Limitations.Count == 0) sb.AppendLine("  Nicio limită înregistrată (asta nu înseamnă că nu există).");
        foreach (var l in Limitations) sb.AppendLine("  • " + l);
        sb.AppendLine().AppendLine("Verificare").AppendLine("  " + Verification);
        foreach (var c in VerificationChecks) sb.AppendLine("  • " + c);
        if (LegacyScoreNote is not null) sb.AppendLine().AppendLine(LegacyScoreNote);
        return sb.ToString().TrimEnd();
    }
}

public static class WhyExplainer
{
    public const string NoReasoning = "Motivul clasificării nu este înregistrat pentru această constatare.";

    public static WhyExplanation Build(Finding f, EvidenceContext ctx, IReadOnlyList<string>? verificationChecks = null, LegacyScore? legacyScore = null)
    {
        var ev = EvidenceLevels.Build(f, ctx);
        var lines = ev.List.Zip(f.SupportingEvidence, (l, r) => $"{l.Source} · {l.TimeText} · {r.Description} ({r.EvidenceId}, {(r.Locator.Length > 0 ? r.Locator : EvidenceLevels.Unknown)})").ToList();

        var lim = new List<string>();
        lim.AddRange(f.MissingEvidence.Select(m => "Lipsește: " + m));
        lim.AddRange(f.AlternativeExplanations.Select(a => "Alternativă care nu poate fi exclusă: " + a));
        lim.AddRange(f.ContradictingEvidence.Select(c => "Contrazis de: " + c));
        lim.AddRange(f.Limitations);

        var v = f.Verification;
        string verification = $"{StateLabels.Label(v.State)}: {v.Reason}" + (v.Verifier.Length > 0 ? $" (verificator: {v.Verifier})" : "");

        string? note = legacyScore is { Factors.Count: > 0 } s
            ? $"Scor euristic vechi: {s.Score}/100, calculat din: {string.Join("; ", s.Factors)}. Scorul nu este o verificare și nu înlocuiește dovezile de mai sus."
            : null;

        return new WhyExplanation(
            f.Description, ev.Summary, lines, f.ClassificationReason.Length > 0 ? f.ClassificationReason : NoReasoning,
            lim.Distinct(StringComparer.Ordinal).ToList(), verification, verificationChecks ?? [], note);
    }
}
