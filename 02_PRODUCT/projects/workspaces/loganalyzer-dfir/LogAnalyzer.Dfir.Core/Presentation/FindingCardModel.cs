using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>One of the four buttons of the finding card. <see cref="AccessText"/> carries the WPF access-key underscore.</summary>
public sealed record CardAction(string Key, string Label, string AccessKey, string AccessText, string HelpText);

public static class FindingCardActions
{
    public static IReadOnlyList<CardAction> All { get; } =
    [
        new("why", "De ce?", "D", "_De ce?", "Arată observația, dovezile, raționamentul, limitele și verificarea acestei constatări."),
        new("evidence", "Arată dovezile", "A", "_Arată dovezile", "Arată dovezile constatării: rezumat, listă și detalii tehnice (SHA-256, parser, locator)."),
        new("verify", "Verifică", "V", "_Verifică", "Arată verdictul verificării automate pentru această constatare și controalele pe care se sprijină."),
        new("todo", "Ce trebuie să fac", "C", "_Ce trebuie să fac", "Arată pașii recomandați pentru această constatare."),
    ];
}

/// <summary>
/// The finding card (UX contract §4, U4) as data: title, human summary, severity (text and icon), state and verification state (text), and the parts behind the four
/// actions. Everything is read from the <see cref="Finding"/> and the case data; the WPF control only binds to it.
/// </summary>
public sealed record FindingCardModel(
    string FindingId, string RuleId, string Title, string HumanSummary, string SeverityText, string SeveritySpec, string SeverityIcon,
    string StateLabel, string StateMeaning, string VerificationLabel, string VerificationText,
    WhyExplanation Why, EvidenceLevels Evidence, KnowThinkDontKnow Know, IReadOnlyList<string> NextSteps, string TechnicalSummary)
{
    public const string NoNextStep = "Niciun pas recomandat nu este înregistrat pentru această constatare.";

    public static FindingCardModel Build(Finding f, EvidenceContext ctx, IReadOnlyList<string>? verificationChecks = null, LegacyScore? legacyScore = null)
    {
        var v = f.Verification;
        return new FindingCardModel(
            f.FindingId, f.RuleId, f.Title, f.HumanSummary.Length > 0 ? f.HumanSummary : f.Description,
            SeverityLabels.Romanian(f.Severity), f.Severity.ToSpec(), SeverityLabels.Icon(f.Severity),
            StateLabels.Romanian(f.Status), StateLabels.Meaning(f.Status),
            StateLabels.Romanian(v.State), $"{StateLabels.Meaning(v.State)} {v.Reason}".Trim(),
            WhyExplainer.Build(f, ctx, verificationChecks, legacyScore), EvidenceLevels.Build(f, ctx), KnowThinkDontKnow.Build(f, ctx),
            f.RecommendedNextSteps.Count > 0 ? f.RecommendedNextSteps.ToList() : [NoNextStep], f.TechnicalSummary);
    }
}
