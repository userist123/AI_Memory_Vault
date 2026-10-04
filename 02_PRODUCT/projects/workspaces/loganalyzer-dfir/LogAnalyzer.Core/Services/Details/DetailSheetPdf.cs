using System;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Core.Services.Details
{
    /// <summary>PDF of one detail sheet: header, all fields by section, meaning, related items and raw form.</summary>
    public static class DetailSheetPdf
    {
        public static void Write(DetailSheet s, string path)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI").FontColor("#1e293b"));
                page.Header().Column(h =>
                {
                    h.Item().Text(s.Title).Bold().FontSize(13).FontColor("#0f172a");
                    h.Item().Text($"{s.Kind} · {s.TimeText}").FontSize(8).FontColor("#64748b");
                    h.Item().Text($"Generat de LogAnalyzer pe {Environment.MachineName}, {DateTime.Now:yyyy-MM-dd HH:mm:ss} (ora locală)").FontSize(7.5f).FontColor("#64748b");
                    h.Item().PaddingTop(4).LineHorizontal(1).LineColor("#0f172a");
                });
                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(8);
                    foreach (var g in s.Fields.GroupBy(f => f.Section))
                    {
                        col.Item().Text(g.Key).Bold().FontSize(10.5f);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.ConstantColumn(150); c.RelativeColumn(); });
                            foreach (var f in g)
                            {
                                t.Cell().BorderBottom(0.4f).BorderColor("#cbd5e1").Padding(2).Text(f.Name).Bold().FontSize(8);
                                t.Cell().BorderBottom(0.4f).BorderColor("#cbd5e1").Padding(2).Text(f.Value).FontSize(8);
                            }
                        });
                    }
                    if (s.Meaning.Count > 0)
                    {
                        col.Item().Text("Ce înseamnă").Bold().FontSize(10.5f);
                        foreach (var m in s.Meaning) col.Item().Text("• " + m);
                    }
                    if (s.Related.Count > 0)
                    {
                        col.Item().Text($"Corelate ({s.Related.Count})").Bold().FontSize(10.5f);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.ConstantColumn(95); c.ConstantColumn(55); c.ConstantColumn(120); c.RelativeColumn(); });
                            foreach (var r in s.Related.Take(300))
                            {
                                t.Cell().Padding(2).Text(r.TimeUtc.ToString("yyyy-MM-dd HH:mm:ss")).FontSize(7.5f);
                                t.Cell().Padding(2).Text(r.Kind).FontSize(7.5f);
                                t.Cell().Padding(2).Text(r.Link).FontSize(7.5f);
                                t.Cell().Padding(2).Text(r.Summary).FontSize(7.5f);
                            }
                        });
                    }
                    if (s.Raw.Length > 0)
                    {
                        col.Item().Text("Formă brută").Bold().FontSize(10.5f);
                        var raw = s.Raw.Length > 60_000 ? s.Raw[..60_000] + "\n… (trunchiat în PDF; forma completă este în aplicație)" : s.Raw;
                        col.Item().Background("#f1f5f9").Padding(6).Text(raw).FontFamily("Consolas").FontSize(7);
                    }
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("pagina ").FontSize(7.5f).FontColor("#64748b");
                    t.CurrentPageNumber().FontSize(7.5f).FontColor("#64748b");
                    t.Span(" / ").FontSize(7.5f).FontColor("#64748b");
                    t.TotalPages().FontSize(7.5f).FontColor("#64748b");
                });
            })).GeneratePdf(path);
        }
    }
}
