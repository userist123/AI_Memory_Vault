using LogAnalyzer.Dfir.AI;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>The case-level facts the AI claim validator compares against, built from the investigation result as it is now (evidence read from the case).</summary>
public static class AiCaseFactsFactory
{
    public static AiCaseFacts ForResult(InvestigationResult r) =>
        AiCaseFacts.Build(r.Case.Info.CaseId, r.Case.LoadEvidence(), r.Findings, r.Timeline, r.AntiForensics, r.Gaps);
}
