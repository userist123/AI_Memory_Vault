using LogAnalyzer.Dfir.Case;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The footer shared by every case PDF (owner decisions 28 and 30): the chain head hashes, the "scop provizoriu, neconfirmat" label while the scope is
/// unconfirmed, then the application line with the page numbers.
/// </summary>
public static class ReportFooter
{
    /// <summary>Test hook: called with (report kind, footer text) when a footer is composed. Per async flow, so parallel tests do not see each other's reports.</summary>
    public static readonly AsyncLocal<Action<string, string>?> Observer = new();

    /// <param name="marking">WP18 S5: the marking line entered by hand ("Marcaj: … (marcaj introdus manual, neverificat de aplicație)"), printed first on every page.</param>
    public static void Compose(IContainer footer, string kind, string appLine, ReportSeal? seal, string? marking = null)
    {
        var lines = (seal ?? new ReportSeal(null, null)).Lines().ToList();
        if (!string.IsNullOrWhiteSpace(marking)) lines.Insert(0, marking);
        Observer.Value?.Invoke(kind, string.Join("\n", lines));
        footer.Column(col =>
        {
            for (int i = 0; i < lines.Count; i++)
            {
                bool scope = seal?.ScopeNote is { Length: > 0 } n && lines[i] == n;
                bool mark = !string.IsNullOrWhiteSpace(marking) && i == 0;
                var t = col.Item().AlignCenter().Text(lines[i]).FontSize(mark ? 7.5f : 5.8f);
                if (scope || mark) t.Bold().FontColor("#b91c1c"); else t.FontColor("#64748b");
            }
            col.Item().AlignCenter().Text(t =>
            {
                t.Span(appLine + " · pagina ").FontSize(7).FontColor("#64748b");
                t.CurrentPageNumber().FontSize(7).FontColor("#64748b");
                t.Span(" / ").FontSize(7).FontColor("#64748b");
                t.TotalPages().FontSize(7).FontColor("#64748b");
            });
        });
    }
}
