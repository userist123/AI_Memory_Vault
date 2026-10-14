using LogAnalyzer.Dfir.Integrity;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Re-verifies every evidence item of the case when a report is produced. Results derived from a source whose hash no
/// longer matches acquisition (or whose file is gone) are invalid; the report says so instead of presenting them as current.
/// A source that is empty or of an unexpected format but still matches its acquisition hash is not invalid: it was simply
/// not interpreted, and the report says that as a separate notice.
/// </summary>
public static class ReportIntegrity
{
    /// <param name="AllIntact">No source is missing, changed or impossible to re-read.</param>
    /// <param name="HasNotices">At least one source matches its hash but was not interpreted (empty or unexpected format).</param>
    public sealed record Result(bool AllIntact, IReadOnlyList<PreflightResult> Items, string Banner, bool HasNotices = false);

    public static Result Check(InvestigationResult r)
    {
        // A stored path outside the case folder (audit #17) counts as missing evidence: it is never read.
        var items = r.Case.LoadEvidence().Select(e => r.Case.TryFullPath(e.StoredPath, out var full)
            ? EvidencePreflight.Check(e, full)
            : new PreflightResult(e.EvidenceId, PreflightStatus.Missing, 0, "", e.Sha256, "", $"{LogAnalyzer.Dfir.Case.CaseWorkspace.OutsideCaseReason}: {e.StoredPath}")).ToList();
        var invalid = items.Where(i => i.IsIntegrityFailure).ToList();
        var unreadable = items.Where(i => i.Status is PreflightStatus.Unreadable or PreflightStatus.Locked).ToList();
        var notices = items.Where(i => i.Status == PreflightStatus.FormatMismatch).ToList();

        var parts = new List<string>();
        if (invalid.Count > 0)
            parts.Add($"REZULTATE INVALIDE pentru {invalid.Count} din {LogAnalyzer.Dfir.Language.RoText.Count(items.Count, "element de probă", "elemente de probă")}: {string.Join(", ", invalid.Select(b => $"{b.EvidenceId} {b.SpecStatus}"))}. " +
                      "Constatările și evenimentele care provin din aceste elemente de probă nu mai pot fi atribuite sursei achiziționate.");
        if (unreadable.Count > 0)
            parts.Add($"Integritatea nu a putut fi verificată pentru {unreadable.Count} din {LogAnalyzer.Dfir.Language.RoText.Count(items.Count, "element de probă", "elemente de probă")} (fișier blocat sau ilizibil la generarea raportului): " +
                      string.Join(", ", unreadable.Select(b => $"{b.EvidenceId} {b.SpecStatus}")) + ".");
        if (invalid.Count == 0 && unreadable.Count == 0)
            parts.Add($"Integritatea elementelor de probă verificată la generarea raportului: {LogAnalyzer.Dfir.Language.RoText.Count(items.Count, "element de probă", "elemente de probă")}, SHA-256 identic cu cel de la achiziție.");
        if (notices.Count > 0)
            parts.Add("Elemente de probă neinterpretate: " + string.Join("; ", notices.Select(n => $"{n.EvidenceId}: {n.FormatNotice}")) + ".");

        var banner = string.Join(" ", parts);
        var intact = invalid.Count == 0 && unreadable.Count == 0;
        if (!intact) r.Case.Audit("report.integrity_failed", banner);
        return new Result(intact, items, banner, notices.Count > 0);
    }
}
