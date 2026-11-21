using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Registers;

/// <summary>What the printout says about the register file: its SHA-256 and whether the register audit chain confirms it (planner #10 item 2).</summary>
public sealed record RegisterIntegrity(string Sha256, string AuditLine);

/// <summary>
/// The shared printed register (planner #10 item 2; ORNISS model, e.g. Anexa 14 for interventions): any <see cref="IRegisterData"/> printed from its title,
/// column headers and rows. Landscape A4, organisation header from the report header profile, the manual marking in the header and on every footer, an optional
/// period on the "Date" column, the SHA-256 of the register file with its audit state, and the signature lines. Long cells wrap; every row is printed.
/// </summary>
public static class RegisterReportPdf
{
    /// <summary>Footer kind observed by <see cref="ReportFooter.Observer"/>.</summary>
    public static string FooterKind(IRegisterData register) => "register-" + register.Kind;

    /// <summary>Tests: receives every printed line (title, period, SHA-256 line, headers, one line per row with the cells joined by " | ") (async-local).</summary>
    public static readonly AsyncLocal<Action<string>?> TextObserver = new();

    /// <summary>The column a period applies to: the one called "Date", when the register has it.</summary>
    public static int DateColumn(IRegisterData register)
    {
        for (int i = 0; i < register.Columns.Count; i++) if (string.Equals(register.Columns[i], "Date", StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>SHA-256 of the file and whether the last audit line of this register names the same hash (and the chain verifies). Never throws on a missing file.</summary>
    public static RegisterIntegrity Integrity(string registerPath, IRegisterData register)
    {
        try
        {
            if (!File.Exists(registerPath)) return new("", "Registrul nu are încă un fișier salvat; nu există SHA-256 și nici intrare în jurnalul de audit.");
            var sha = Hashing.Sha256File(registerPath);
            var auditPath = RegisterStore.AuditPathFor(registerPath);
            if (!File.Exists(auditPath)) return new(sha, "Jurnalul de audit al registrelor nu există lângă fișier: potrivirea nu poate fi confirmată.");
            var verification = RegisterStore.VerifyAudit(registerPath);
            var lines = Encoding.UTF8.GetString(LogAnalyzer.Dfir.Case.HashChain.ReadAllShared(auditPath)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var last = lines.LastOrDefault(l => l.Contains($"\"{register.Kind}\"", StringComparison.Ordinal));
            var matches = last is not null && last.Contains(sha, StringComparison.OrdinalIgnoreCase);
            var chain = verification.Status == LogAnalyzer.Dfir.Case.ChainStatus.Valid ? "lanțul jurnalului este valid" : "lanțul jurnalului NU este confirmat valid (" + verification.Message + ")";
            return new(sha, (matches ? "SHA-256 coincide cu ultima intrare din jurnalul de audit al registrelor" : "SHA-256 NU coincide cu ultima intrare din jurnalul de audit al registrelor (fișier modificat în afara aplicației sau nesalvat)") + "; " + chain + ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("", "Fișierul registrului nu a putut fi citit pentru SHA-256: " + ex.Message);
        }
    }

    public static void Write(string path, IRegisterData register, ReportHeaderProfile header, string marking, string? title = null, Func<string, string>? label = null,
                             DateTime? from = null, DateTime? to = null, RegisterIntegrity? integrity = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        label ??= c => c;
        var columns = register.Columns.Select(label).ToList();
        var dateCol = DateColumn(register);
        var rows = register.Cells().Where(r =>
        {
            if (dateCol < 0 || (from is null && to is null) || dateCol >= r.Length || ParseDate(r[dateCol]) is not { } d) return true;   // an unreadable date is kept, never dropped silently
            return (from is null || d >= from) && (to is null || d <= to);
        }).ToList();
        var printTitle = title ?? register.Title.ToUpperInvariant();
        var markingLine = MarkingText.Line(marking);
        var period = from is not null || to is not null ? $" · perioada {(from is null ? "…" : from.Value.ToString("yyyy-MM-dd"))} – {(to is null ? "…" : to.Value.ToString("yyyy-MM-dd"))}" : "";
        var shaLine = integrity is null ? "SHA-256 al fișierului registrului: neinclus în această tipăritură." :
            (integrity.Sha256.Length > 0 ? $"SHA-256 al fișierului registrului: {integrity.Sha256}. " : "") + integrity.AuditLine;
        var obs = TextObserver.Value;
        obs?.Invoke(printTitle); obs?.Invoke(period); obs?.Invoke(shaLine); obs?.Invoke(string.Join(" | ", columns));
        foreach (var r in rows) obs?.Invoke(string.Join(" | ", columns.Select((_, i) => i < r.Length ? r[i] : "")));

        // column weights from header and content length, bounded so no column vanishes and none takes the page
        var weights = columns.Select((h, i) => (float)Math.Clamp(Math.Max(h.Length * 0.6, rows.Count == 0 ? 0 : rows.Max(r => i < r.Length ? Math.Min(r[i].Length, 40) : 0) * 0.8), 5, 30)).ToArray();

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(22);
            page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Segoe UI").FontColor("#111827"));
            page.Header().Column(h =>
            {
                h.Item().AlignCenter().Text(markingLine).Bold().FontSize(10).FontColor("#b91c1c");
                if (header.Unit.Length > 0) h.Item().AlignCenter().Text(header.Unit).Bold().FontSize(11);
                if (header.Structure.Length > 0) h.Item().AlignCenter().Text(header.Structure).FontSize(9);
                h.Item().AlignCenter().Text(printTitle).Bold().FontSize(12);
                h.Item().AlignCenter().Text($"Generat {DateTime.Now:yyyy-MM-dd HH:mm} pe {Environment.MachineName} · {LogAnalyzer.Dfir.Language.RoText.Count(rows.Count, "înregistrare", "înregistrări")}{period}").FontSize(7.5f).FontColor("#4b5563");
                foreach (var f in header.ExtraFields.Where(f => f.Label.Length > 0)) h.Item().AlignCenter().Text($"{f.Label}: {f.Value}").FontSize(7.5f);
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor("#111827");
            });
            page.Content().PaddingVertical(6).Column(col =>
            {
                col.Spacing(6);
                col.Item().Text(shaLine).FontSize(7.5f).FontColor("#4b5563");
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { foreach (var w in weights) cd.RelativeColumn(w); });
                    t.Header(hd => { foreach (var h in columns) hd.Cell().Background("#e5e7eb").Border(0.5f).BorderColor("#6b7280").Padding(3).Text(h).Bold().FontSize(7.5f); });
                    foreach (var r in rows)
                        for (int i = 0; i < columns.Count; i++)
                            t.Cell().Border(0.5f).BorderColor("#9ca3af").Padding(3).Text(i < r.Length ? r[i] : "");
                });
                if (rows.Count == 0) col.Item().Text("Nu există înregistrări" + (period.Length > 0 ? " în perioada aleasă." : ".")).FontColor("#4b5563");
                col.Item().PaddingTop(28).ShowEntire().Row(rw =>
                {
                    foreach (var s in new[] { "Întocmit", "Verificat", "Aprobat" })
                        rw.RelativeItem().PaddingHorizontal(10).Column(c =>
                        {
                            c.Item().LineHorizontal(0.7f);
                            c.Item().AlignCenter().Text(s).Bold().FontSize(8);
                            c.Item().AlignCenter().Text("Grad, nume, semnătură, data").FontSize(7).FontColor("#4b5563");
                        });
                });
            });
            ReportFooter.Compose(page.Footer(), FooterKind(register), $"LogAnalyzer {DfirInfo.ApplicationVersion}", null, markingLine);
        })).GeneratePdf(path);
    }

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
