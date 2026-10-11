using System.Security.Cryptography;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Registers;

/// <summary>
/// The printable data transfer register (owner request 2026-10-11): organisation header from the report header profile (nothing hard-coded), one row
/// per transfer, the cross-check findings, the manual marking on every page and the three signature lines (operator, four-eyes witness, approval).
/// </summary>
public static class TransferReportPdf
{
    public static void Write(string path, TransferRegister register, IReadOnlyList<TransferFinding> findings, ReportHeaderProfile header, string marking, bool isolated,
                             DateTime? from = null, DateTime? to = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var rows = register.Rows.Where(r => !TransferRegister.TryDateTime(r.DateTime, out var d) || ((from is null || d >= from) && (to is null || d <= to)))
                                .OrderBy(r => r.DateTime, StringComparer.Ordinal).ToList();
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(22);
            page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Segoe UI").FontColor("#111827"));
            page.Header().Column(h =>
            {
                if (marking.Length > 0) h.Item().AlignCenter().Text(marking).Bold().FontSize(10).FontColor("#b91c1c");
                if (header.Unit.Length > 0) h.Item().AlignCenter().Text(header.Unit).Bold().FontSize(11);
                if (header.Structure.Length > 0) h.Item().AlignCenter().Text(header.Structure).FontSize(9);
                h.Item().AlignCenter().Text("REGISTRUL DE EVIDENȚĂ A TRANSFERURILOR DE DATE PE MEDII DE STOCARE").Bold().FontSize(12);
                h.Item().AlignCenter().Text($"Generat {DateTime.Now:yyyy-MM-dd HH:mm} pe {Environment.MachineName} · {LogAnalyzer.Dfir.Language.RoText.Count(rows.Count, "înregistrare", "înregistrări")}" +
                                            (from is not null || to is not null ? $" · perioada {from:yyyy-MM-dd} – {to:yyyy-MM-dd}" : "") +
                                            $" · {(isolated ? "stație izolată (fără rețea)" : "stație conectată")}").FontSize(7.5f).FontColor("#4b5563");
                foreach (var f in header.ExtraFields.Where(f => f.Label.Length > 0)) h.Item().AlignCenter().Text($"{f.Label}: {f.Value}").FontSize(7.5f);
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor("#111827");
            });
            page.Content().PaddingVertical(6).Column(col =>
            {
                col.Spacing(6);
                col.Item().Table(t =>
                {
                    float[] widths = [1.3f, 1f, 1.1f, 0.7f, 1.6f, 1.6f, 1.4f, 1.4f, 2f, 1.2f, 1.1f];
                    t.ColumnsDefinition(cd => { foreach (var w in widths) cd.RelativeColumn(w); });
                    foreach (var h in new[] { "Nr. registru", "Data și ora", "Clasificare", "Direcție", "Sursă", "Destinație", "Persoană", "Mediu de stocare", "Conținut și SHA-256", "Martor (4 ochi)", "Aprobare" })
                        t.Cell().Background("#e5e7eb").Border(0.5f).BorderColor("#6b7280").Padding(3).Text(h).Bold().FontSize(7.5f);
                    foreach (var r in rows)
                    {
                        void C(string main, string sub = "", bool bold = false, string? color = null)
                            => t.Cell().Border(0.5f).BorderColor("#9ca3af").Padding(3).Column(c =>
                            {
                                var tx = c.Item().Text(main); if (bold) tx.Bold(); if (color is not null) tx.FontColor(color);
                                if (sub.Length > 0) c.Item().Text(sub).FontSize(6.5f).FontColor("#4b5563");
                            });
                        C(r.RegistrationNumber, "", true);
                        C(r.DateTime);
                        C(r.Level?.Name ?? (r.Classification.Length > 0 ? r.Classification : "nemarcat"), "", r.Level is not null, r.Level is not null ? "#b91c1c" : null);
                        C(r.DirectionValue is { } d ? TransferRegister.DirectionText(d).ToUpperInvariant() : r.Direction, "", true);
                        C(r.Source, r.SourceStation.Length > 0 ? "Stație: " + r.SourceStation : "");
                        C(r.Destination, r.DestinationStation.Length > 0 ? "Stație: " + r.DestinationStation : "");
                        C(r.Person, r.PersonFunction);
                        C(r.MediumType, string.Join(" · ", new[] { r.MediumSerial.Length > 0 ? "S/N " + r.MediumSerial : "", r.MediumRegistration.Length > 0 ? "Nr. " + r.MediumRegistration : "" }.Where(s => s.Length > 0)));
                        C(r.Content, string.Join(" ", r.Sha256.Split([';', ',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries)));
                        C(r.Witness);
                        C(r.Approver.Length > 0 ? r.Approver : "—");
                    }
                });
                if (rows.Count == 0) col.Item().Text("Nu există transferuri în perioada aleasă.").FontColor("#4b5563");

                col.Item().PaddingTop(4).Text("Verificări automate").Bold().FontSize(9.5f);
                if (findings.Count == 0) col.Item().Text("Fiecare transfer a folosit un mediu din registrul de medii, activ și valabil, cu nivel cel puțin egal cu al conținutului.");
                foreach (var f in findings) col.Item().Text($"• {f.RegistrationNumber}: {f.Message}").FontColor(f.IsError ? "#b91c1c" : "#92400e");

                col.Item().PaddingTop(28).Row(rw =>
                {
                    foreach (var s in new[] { "Operator registru / executant", "Martor / ofițer de securitate (regula celor patru ochi)", "Șef structură — aprobare" })
                        rw.RelativeItem().PaddingHorizontal(10).Column(c =>
                        {
                            c.Item().LineHorizontal(0.7f);
                            c.Item().AlignCenter().Text(s).Bold().FontSize(8);
                            c.Item().AlignCenter().Text("Grad, nume, semnătură").FontSize(7).FontColor("#4b5563");
                        });
                });
            });
            ReportFooter.Compose(page.Footer(), "transfers", $"LogAnalyzer {DfirInfo.ApplicationVersion}", null, marking.Length > 0 ? marking : null);
        })).GeneratePdf(path);
    }

    /// <summary>SHA-256 of each file (hex, upper case) for the „Conținut și SHA-256” cell; a folder contributes all its files.</summary>
    public static List<(string Name, string Sha256, long Bytes)> HashFiles(IEnumerable<string> paths)
    {
        var list = new List<(string, string, long)>();
        foreach (var p in paths)
        {
            var files = Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories) : File.Exists(p) ? [p] : [];
            foreach (var f in files)
            {
                using var s = File.OpenRead(f);
                list.Add((Directory.Exists(p) ? Path.GetRelativePath(Path.GetDirectoryName(p.TrimEnd('\\'))!, f) : Path.GetFileName(f), Convert.ToHexString(SHA256.HashData(s)), new FileInfo(f).Length));
            }
        }
        return list;
    }
}
