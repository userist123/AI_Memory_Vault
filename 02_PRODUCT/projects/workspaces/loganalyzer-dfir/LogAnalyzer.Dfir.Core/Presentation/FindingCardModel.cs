using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>
/// One of the four buttons of the finding card. The texts come from the resource layer (keys below), so they follow the language;
/// <see cref="AccessText"/> carries the WPF access-key underscore and <see cref="AccessKey"/> is the letter after it.
/// </summary>
public sealed record CardAction(string Key, string LabelKey, string AccessTextKey, string HelpKey)
{
    public string Label => Loc.T(LabelKey);
    public string AccessText => Loc.T(AccessTextKey);
    public string HelpText => Loc.T(HelpKey);
    public string AccessKey => AccessText.IndexOf('_') is var i and >= 0 && i + 1 < AccessText.Length ? AccessText[i + 1].ToString().ToUpperInvariant() : "";
}

public static class FindingCardActions
{
    public static IReadOnlyList<CardAction> All { get; } =
    [
        new("why", "card.de_ce_2", "card.de_ce", "card.arata_observatia_dovezile_rationamentul_limitele"),
        new("evidence", "card.arata_dovezile_2", "card.arata_dovezile", "card.arata_dovezile_constatarii_rezumat_lista"),
        new("verify", "card.verifica_2", "card.verifica", "card.arata_verdictul_verificarii_automate_pentru"),
        new("todo", "card.ce_trebuie_sa_fac_2", "card.ce_trebuie_sa_fac", "card.arata_pasii_recomandati_pentru_aceasta"),
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
    public static string NoNextStep => Loc.T("card.no_next_step");

    public static FindingCardModel Build(Finding f, EvidenceContext ctx, IReadOnlyList<string>? verificationChecks = null, LegacyScore? legacyScore = null)
    {
        var v = f.Verification;
        return new FindingCardModel(
            f.FindingId, f.RuleId, f.Title, f.HumanSummary.Length > 0 ? f.HumanSummary : f.Description,
            SeverityLabels.Text(f.Severity), f.Severity.ToSpec(), SeverityLabels.Icon(f.Severity),
            StateLabels.Label(f.Status), StateLabels.Meaning(f.Status),
            StateLabels.Label(v.State), $"{StateLabels.Meaning(v.State)} {v.Reason}".Trim(),
            WhyExplainer.Build(f, ctx, verificationChecks, legacyScore), EvidenceLevels.Build(f, ctx), KnowThinkDontKnow.Build(f, ctx),
            f.RecommendedNextSteps.Count > 0 ? f.RecommendedNextSteps.ToList() : [NoNextStep], f.TechnicalSummary);
    }
}
