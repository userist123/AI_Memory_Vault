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

    /// <summary>
    /// What produced the report, on every page (legal research G2/G3, docs/dfir/RESEARCH_2026-10-11_PROBE_VALOARE_JURIDICA.md): the program file and its
    /// SHA-256, the detection lists used (shipped or a signed data pack) and the station's time zone with its UTC offset, so times can be read correctly.
    /// </summary>
    public static string Provenance => string.Join("\n", ProvenanceLines);

    /// <summary>The provenance as short lines (each well under the page width at the footer's font size), so no line is cut at the page edge.</summary>
    public static IReadOnlyList<string> ProvenanceLines => ProvenanceValue.Value;

    /// <summary>Longest footer line the layout is designed for; the test keeps every line under it.</summary>
    public const int MaxLineLength = 120;

    private static readonly Lazy<IReadOnlyList<string>> ProvenanceValue = new(() =>
    {
        string program;
        try
        {
            var exe = Environment.ProcessPath ?? "";
            program = exe.Length > 0 && File.Exists(exe)
                ? $"{Path.GetFileName(exe)} SHA-256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(exe)))[..16]}…"
                : "program necunoscut";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { program = "amprenta programului nu a putut fi citită"; }
        var tz = TimeZoneInfo.Local;
        return (IReadOnlyList<string>)
        [
            $"Generat de {program}",
            $"liste de detecție: {LogAnalyzer.Dfir.Analysis.ShippedData.Description}",
            $"fus orar al stației: {LogAnalyzer.Dfir.Language.RoText.ZoneName(tz, DateTimeOffset.Now)}",
            "orele cu „UTC” sunt în timp universal, restul în ora stației",
        ];
    });

    /// <param name="marking">WP18 S5: the marking line entered by hand ("Marcaj: … (marcaj introdus manual, neverificat de aplicație)"), printed first on every page.</param>
    public static void Compose(IContainer footer, string kind, string appLine, ReportSeal? seal, string? marking = null)
    {
        var lines = (seal ?? new ReportSeal(null, null)).Lines().ToList();
        if (!string.IsNullOrWhiteSpace(marking)) lines.Insert(0, marking);
        lines.AddRange(ProvenanceLines);
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
