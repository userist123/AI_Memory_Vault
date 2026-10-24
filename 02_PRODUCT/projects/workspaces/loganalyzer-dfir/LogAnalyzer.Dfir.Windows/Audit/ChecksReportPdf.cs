using LogAnalyzer.Dfir.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>Generic PDF for a list of checks plus optional tables (domain inventory, user investigation, e-mail analysis).</summary>
public static class ChecksReportPdf
{
    public sealed record Table(string Title, string[] Headers, IReadOnlyList<string[]> Rows);

    /// <summary>Without a case there is no chain to anchor: the footer says so.</summary>
    public static void Write(string path, string title, string subtitle, IReadOnlyList<ControlCheck> checks, IReadOnlyList<string> notes, params Table[] tables) =>
        Write(path, title, subtitle, null, checks, notes, tables);

    /// <param name="seal">Chain head hashes and scope label of the case the report belongs to (decisions 28, 30).</param>
    public static void Write(string path, string title, string subtitle, LogAnalyzer.Dfir.Case.ReportSeal? seal, IReadOnlyList<ControlCheck> checks, IReadOnlyList<string> notes, params Table[] tables)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("Segoe UI").FontColor("#1e293b"));
            page.Header().Column(h =>
            {
                h.Item().Text(title).Bold().FontSize(14).FontColor("#0f172a");
                h.Item().Text($"{subtitle} · generat {DateTime.Now:yyyy-MM-dd HH:mm} pe {Environment.MachineName}").FontSize(7.5f).FontColor("#64748b");
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor("#0f172a");
            });
            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(6);
                foreach (var n in notes) col.Item().Text("• " + n);
                foreach (var c in checks)
                    col.Item().Border(0.5f).BorderColor("#cbd5e1").Padding(5).Column(cc =>
                    {
                        cc.Item().Text($"{ControlReportPdf.StatusText(c.Status)} · {c.Id} · {c.Title}").Bold();
                        cc.Item().Text(c.Detail);
                        foreach (var e in c.Evidence.Take(40)) cc.Item().Text("– " + e).FontSize(7).FontColor("#64748b");
                        if (c.Recommendation.Length > 0) cc.Item().Text("Recomandare: " + c.Recommendation).FontSize(7.5f).Italic();
                    });
                foreach (var t in tables.Where(t => t.Rows.Count > 0))
                {
                    col.Item().Text($"{t.Title} ({t.Rows.Count})").Bold().FontSize(10.5f);
                    col.Item().Table(tb =>
                    {
                        tb.ColumnsDefinition(cd => { foreach (var _ in t.Headers) cd.RelativeColumn(); });
                        foreach (var h in t.Headers) tb.Cell().Background("#e2e8f0").Padding(2).Text(h).Bold().FontSize(7.5f);
                        foreach (var row in t.Rows.Take(3000))
                            foreach (var cell in row) tb.Cell().BorderBottom(0.3f).BorderColor("#cbd5e1").Padding(2).Text(cell ?? "").FontSize(7);
                    });
                }
            });
            ReportFooter.Compose(page.Footer(), "checks", $"LogAnalyzer {DfirInfo.ApplicationVersion}", seal);
        })).GeneratePdf(path);
    }
}
