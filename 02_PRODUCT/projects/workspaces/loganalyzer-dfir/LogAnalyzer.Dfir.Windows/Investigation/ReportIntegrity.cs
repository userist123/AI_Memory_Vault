using LogAnalyzer.Dfir.Integrity;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Re-verifies every evidence item of the case when a report is produced. Results derived from a source whose hash no
/// longer matches acquisition are invalid; the report says so instead of presenting them as current.
/// </summary>
public static class ReportIntegrity
{
    public sealed record Result(bool AllIntact, IReadOnlyList<PreflightResult> Items, string Banner);

    public static Result Check(InvestigationResult r)
    {
        var items = r.Case.LoadEvidence().Select(e => EvidencePreflight.Check(e, r.Case.FullPath(e.StoredPath))).ToList();
        var bad = items.Where(i => i.Status is not (PreflightStatus.Ok or PreflightStatus.Unverified)).ToList();
        var banner = bad.Count == 0
            ? $"Integritatea probelor verificată la generarea raportului: {items.Count} probe, SHA-256 identic cu cel de la achiziție."
            : $"REZULTATE INVALIDE pentru {bad.Count} din {items.Count} probe: {string.Join(", ", bad.Select(b => $"{b.EvidenceId} {b.SpecStatus}"))}. " +
              "Constatările și evenimentele care provin din aceste probe nu mai pot fi atribuite sursei achiziționate.";
        if (bad.Count > 0) r.Case.Audit("report.integrity_failed", banner);
        return new Result(bad.Count == 0, items, banner);
    }
}
