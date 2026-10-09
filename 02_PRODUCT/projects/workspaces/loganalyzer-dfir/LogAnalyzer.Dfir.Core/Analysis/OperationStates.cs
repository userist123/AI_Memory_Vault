using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// State of a whole run (UX contract §20), computed from what actually happened. It says how far the ANALYSIS got; it says
/// nothing about whether any finding is verified (that is <see cref="Finding.Verification"/>).
/// </summary>
public static class OperationStates
{
    public static (OperationState State, string Reason) Summarize(IReadOnlyCollection<EvidenceStatus> collection, IReadOnlyCollection<ParseResult> parsing)
    {
        int cFail = collection.Count(s => s is EvidenceStatus.Failed or EvidenceStatus.NotAvailable);
        int cPart = collection.Count(s => s == EvidenceStatus.Partial);
        var real = parsing.Where(p => p.Status != EvidenceStatus.SkippedByDesign).ToList();
        int pFail = real.Count(p => p.Status is EvidenceStatus.Failed or EvidenceStatus.NotAvailable);
        int pPart = real.Count(p => p.Status == EvidenceStatus.Partial);

        if (collection.Count > 0 && cFail == collection.Count && real.Count == 0)
            return (OperationState.Failed, $"Nicio sursă nu a putut fi colectată ({cFail}/{collection.Count}).");
        if (real.Count > 0 && pFail == real.Count)
        {
            bool preflight = real.All(p => p.Error.Contains("EVIDENCE_", StringComparison.Ordinal));
            return preflight
                ? (OperationState.Blocked, $"Toate probele au fost refuzate de verificarea de integritate ({pFail}); nimic nu a fost parsat.")
                : (OperationState.Failed, $"Toate parsările au eșuat ({pFail}/{real.Count}).");
        }
        if (cFail + cPart + pFail + pPart > 0)
            return (OperationState.Partial, $"Colectare: {cFail} eșuate/indisponibile, {cPart} parțiale; parsare: {pFail} eșuate, {pPart} parțiale din {real.Count}. Rezultatele nu acoperă sursele lipsă.");
        return (OperationState.Completed, $"Analiza s-a încheiat ({real.Count} parsări). Aceasta nu verifică constatările.");
    }
}

/// <summary>Analysis/run_state.json: the operation state of the run, written even when the run is cancelled or fails.</summary>
public sealed record RunState(string CaseId, OperationState State, string Reason, DateTimeOffset UpdatedUtc);
