namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// What a case report or export prints about the state of its case (owner decisions 28 and 30): the head hashes of the custody and audit
/// chains, so a copy held outside the case (custody register, paper or electronic) makes truncation or rewriting detectable, and the label
/// "scop provizoriu, neconfirmat" while the case scope has not been confirmed.
/// </summary>
public sealed record ReportSeal(ChainAnchor? Anchor, string? ScopeNote)
{
    public static ReportSeal For(CaseWorkspace ws) => new(ws.Anchor(), ws.ScopeNote);

    /// <summary>Footer lines. Without a case the anchor is said to be unavailable, never left out silently.</summary>
    public IReadOnlyList<string> Lines()
    {
        var l = new List<string>();
        if (Anchor is null) l.Add("Ancoră lanț de custodie / audit: indisponibilă (raport generat fără caz)");
        else
        {
            l.Add($"Ancoră custodie (Seq {Anchor.CustodySeq}): {Anchor.CustodyHead}");
            l.Add($"Ancoră audit (Seq {Anchor.AuditSeq}): {Anchor.AuditHead}");
        }
        if (!string.IsNullOrEmpty(ScopeNote)) l.Add(ScopeNote);
        return l;
    }
}

/// <summary>What the operator sees at case close (decision 30): the final chain heads, to record in the custody register with signature and date.</summary>
public sealed record CaseClosure(string CaseId, DateTimeOffset ClosedUtc, ChainAnchor Anchor, string? ScopeNote, string RegisterText)
{
    /// <summary>
    /// Audits <c>case.closed</c>, THEN reads the chain heads, so they include the closing entry and nothing is written after them. The case is not
    /// locked or changed: closing is the moment to copy the heads out of the case.
    /// </summary>
    public static CaseClosure Close(CaseWorkspace ws, string operatorName)
    {
        var closedUtc = DateTimeOffset.UtcNow;
        ws.Audit("case.closed", $"by={operatorName}");
        var anchor = ws.Anchor();
        var note = ws.ScopeNote;
        var text = string.Join(Environment.NewLine,
        [
            $"ÎNCHIDERE CAZ {ws.Info.CaseId} ({ws.Info.Name})",
            $"Data (UTC): {closedUtc:yyyy-MM-dd HH:mm}   Operator: {operatorName}",
            $"Custodie: Seq {anchor.CustodySeq}  {anchor.CustodyHead}",
            $"Audit:    Seq {anchor.AuditSeq}  {anchor.AuditHead}",
            note is null ? "Scop: confirmat" : note,
            "Se înscriu în registrul de custodie (pe hârtie sau electronic) cu semnătură și dată.",
        ]);
        return new CaseClosure(ws.Info.CaseId, closedUtc, anchor, note, text);
    }
}
