using LogAnalyzer.Dfir.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The last page of a document that the security officer signs: a heading and an EMPTY framed box at the fixed place of <see cref="SignatureField"/>, and nothing
/// else on that page, so the signature (whose drawing is limited to that box) cannot cover any content of the report.
/// </summary>
public static class SignatureBoxPage
{
    private const float TitleHeight = 60;

    public static void Add(IDocumentContainer doc, string? markingLine, string what)
    {
        doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI").FontColor("#1e293b"));
            page.Content().PaddingLeft((float)SignatureField.BoxLeft).PaddingTop((float)SignatureField.BoxTop - TitleHeight - 10).Column(c =>
            {
                c.Item().Width((float)SignatureField.BoxWidth).Height(TitleHeight).ScaleToFit().Column(t =>
                {
                    if (!string.IsNullOrEmpty(markingLine)) t.Item().Text(markingLine).Bold().FontSize(8).FontColor("#b91c1c");
                    t.Item().Text("Semnătura ofițerului de securitate").Bold().FontSize(11);
                    t.Item().Text(what).FontSize(7.5f).FontColor("#64748b");
                });
                c.Item().PaddingTop(10).Width((float)SignatureField.BoxWidth).Height((float)SignatureField.BoxHeight).Border(1).BorderColor("#0f172a");
            });
        });
    }
}
