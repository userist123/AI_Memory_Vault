using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The report of the officer's "Confruntare între stații" (owner decision 2026-10-16): findings sorted by severity, the exports used and refused, and the honest
/// limits; the last page is the reserved signature box and the file already holds the empty signature field over it, so the officer signs it with the card
/// exactly like a control report.
/// </summary>
public static class CrossStationReportPdf
{
    public const string SignHere = "Se semnează doar în acest câmp, cu certificatul calificat de pe card: în LogAnalyzer („Exportă raportul semnat”) sau în Adobe, alegând câmpul existent „Semnatura ofiterului de securitate”.";

    /// <summary>Tests: every text printed, in order.</summary>
    public static readonly AsyncLocal<Action<string>?> TextObserver = new();

    private static string U(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    private static string Color(CrossSeverity s) => s switch
    {
        CrossSeverity.Grav => "#b91c1c", CrossSeverity.Ridicat => "#c2410c", CrossSeverity.DeVerificat => "#b45309", CrossSeverity.Nedeterminat => "#475569", _ => "#065f46",
    };

    /// <summary>The PDF bytes (with the reserved, empty signature field).</summary>
    public static byte[] Generate(CrossStationResult r, CrossStationSettings s, string officerStation, string registerLine, DateTimeOffset generatedUtc)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        void Say(string t) => TextObserver.Value?.Invoke(t);
        var generated = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(26);
                page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("Segoe UI").FontColor("#1e293b"));
                page.Header().Column(h =>
                {
                    h.Item().AlignCenter().Text(ControlReportPdf.OfficialLabel).Bold().FontSize(9).FontColor("#b91c1c");
                    h.Item().Text("Confruntarea istoricului mediilor de stocare între stații").Bold().FontSize(14);
                    h.Item().Text($"Perioada: {U(s.FromUtc)} – {U(s.ToUtc)} · generat {U(generatedUtc)} pe stația ofițerului {officerStation}").FontSize(7.5f).FontColor("#64748b");
                    h.Item().PaddingTop(4).LineHorizontal(1);
                });
                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(5);
                    void Line(string text, float size = 8.5f, bool bold = false, string color = "#1e293b")
                    {
                        Say(text);
                        var t = col.Item().Text(text).FontSize(size).FontColor(color);
                        if (bold) t.Bold();
                    }
                    Line("Pe scurt: " + r.Summary, 9, true);
                    Line(registerLine);
                    Line("1. Constatări, în ordinea gravității", 11, true);
                    if (r.Findings.Count == 0) Line("Nicio constatare.");
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(62); c.ConstantColumn(78); c.ConstantColumn(54); c.ConstantColumn(62); c.RelativeColumn(); });
                        foreach (var h in new[] { "Gravitate", "Stația", "Mediul", "Transferul", "Constatarea" }) t.Cell().BorderBottom(0.8f).Padding(2).Text(h).Bold().FontSize(7.5f);
                        foreach (var f in r.Ordered)
                        {
                            Say($"{f.SeverityText} | {f.Station} | {f.Medium} | {f.Transfer} | {f.Text}");
                            t.Cell().BorderBottom(0.3f).Padding(2).Text(f.SeverityText).Bold().FontSize(7.5f).FontColor(Color(f.Severity));
                            t.Cell().BorderBottom(0.3f).Padding(2).Text(f.Station).FontSize(7.5f);
                            t.Cell().BorderBottom(0.3f).Padding(2).Text(f.Medium).FontSize(7.5f);
                            t.Cell().BorderBottom(0.3f).Padding(2).Text(f.Transfer).FontSize(7.5f);
                            t.Cell().BorderBottom(0.3f).Padding(2).Text(f.Text).FontSize(7.5f);
                        }
                    });
                    Line("2. Exporturile folosite și cele respinse", 11, true);
                    foreach (var c in r.Accepted)
                        Line($"ACCEPTAT · {c.StationLabel} (Hardware ID {c.Doc!.StationId}, ediția {c.Doc.Edition}) · exportat {c.Doc.ExportedUtc} · registru v{c.Doc.RegisterVersion} · ancorare {(c.Doc.AnchorCode.Length > 0 ? c.Doc.AnchorCode + " (seq " + c.Doc.AnchorSeq + ")" : "lipsă")} · semnat de {c.SignerName} · SHA-256 {c.Sha256}", 7.5f);
                    foreach (var c in r.Rejected)
                        Line($"RESPINS · {c.FileName} · {c.Headline} · SHA-256 {c.Sha256}", 7.5f, false, "#b91c1c");
                    Line("3. Limitări (ce această confruntare nu poate arăta)", 11, true);
                    foreach (var l in r.Limits) Line("• " + l, 7.5f);
                });
                ReportFooter.Compose(page.Footer(), "confruntare", $"LogAnalyzer {DfirInfo.ApplicationVersion} · confruntare între stații", null);
            });
            SignatureBoxPage.Add(doc, null, SignHere);
        }).GeneratePdf();
        return LogAnalyzer.Dfir.Reporting.SignatureField.Reserve(generated);
    }

    /// <summary>The same table as CSV (UTF-8 with BOM so Excel reads the diacritics; ';' separated).</summary>
    public static string Csv(CrossStationResult r)
    {
        static string Q(string v) => "\"" + (v ?? "").Replace("\"", "\"\"").ReplaceLineEndings(" ") + "\"";
        var sb = new StringBuilder();
        sb.AppendLine("Gravitate;Tip;Stația;Mediul;Transferul;Constatarea");
        foreach (var f in r.Ordered) sb.AppendLine(string.Join(";", new[] { f.SeverityText, f.Kind, f.Station, f.Medium, f.Transfer, f.Text }.Select(Q)));
        return sb.ToString();
    }
}
