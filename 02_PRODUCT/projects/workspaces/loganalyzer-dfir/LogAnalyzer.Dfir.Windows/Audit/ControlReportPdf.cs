using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>"Raport de control al stației": summary, every check with its evidence, user activity, action timeline, gaps.</summary>
public static class ControlReportPdf
{
    private const string Ink = "#0f172a", Muted = "#64748b", Line = "#cbd5e1";

    public static void Write(ControlReport r, string path, string inspector, string notes = "", LogAnalyzer.Dfir.Case.ReportSeal? seal = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var f = r.Facts;
        string L(DateTimeOffset? t) => t is { } v ? TimeZoneInfo.ConvertTime(v, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm") : "—";

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("Segoe UI").FontColor("#1e293b"));
            page.Header().Column(h =>
            {
                h.Item().Text($"Raport de control — stația {f.Host}").Bold().FontSize(14).FontColor(Ink);
                h.Item().Text($"Perioada controlată: {L(f.PeriodStartUtc)} – {L(f.PeriodEndUtc)} (ora locală) · colectat {L(f.CollectedUtc)} · " +
                              $"{(f.StationShouldBeIsolated ? "stație izolată (AirGapped)" : "stație conectată")} · drepturi de administrator: {(f.IsAdministrator ? "da" : "NU (rezultate incomplete)")}")
                    .FontSize(7.5f).FontColor(Muted);
                h.Item().Text($"Verificator: {inspector}").FontSize(7.5f).FontColor(Muted);
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor(Ink);
            });

            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(8);
                col.Item().Row(row =>
                {
                    Badge(row, "CONFORM", r.Count(ControlStatus.Conform), "#15803d");
                    Badge(row, "NECONFORM", r.Count(ControlStatus.Neconform), "#b91c1c");
                    Badge(row, "DE VERIFICAT", r.Count(ControlStatus.DeVerificat), "#b45309");
                    Badge(row, "NEDETERMINAT", r.Count(ControlStatus.Nedeterminat), "#475569");
                });
                if (notes.Length > 0) col.Item().Text("Observații: " + notes);

                col.Item().Text("1. Verificări").Bold().FontSize(11).FontColor(Ink);
                foreach (var area in r.Checks.GroupBy(c => c.Area))
                {
                    col.Item().Text(area.Key).Bold().FontSize(9.5f);
                    foreach (var c in area)
                        col.Item().Border(0.5f).BorderColor(Line).Padding(5).Column(cc =>
                        {
                            cc.Item().Row(rr =>
                            {
                                rr.ConstantItem(78).Text(StatusText(c.Status)).Bold().FontColor(StatusColor(c.Status));
                                rr.RelativeItem().Text($"{c.Id} · {c.Title}").Bold();
                            });
                            cc.Item().Text(c.Detail);
                            foreach (var e in c.Evidence.Take(25)) cc.Item().Text("– " + e).FontSize(7).FontColor(Muted);
                            if (c.Evidence.Count > 25) cc.Item().Text($"… încă {c.Evidence.Count - 25} intrări (în fișierul JSON al raportului)").FontSize(7).FontColor(Muted);
                            if (c.Recommendation.Length > 0) cc.Item().Text("Recomandare: " + c.Recommendation).FontSize(7.5f).Italic();
                        });
                }

                col.Item().Text("2. Activitatea utilizatorilor").Bold().FontSize(11).FontColor(Ink);
                if (r.Users.Count == 0) col.Item().Text("Nu există date (jurnalul Security necitit sau fără autentificări în perioadă).").FontColor(Muted);
                else col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(1.6f); c.RelativeColumn(1.6f); });
                    foreach (var head in new[] { "Utilizator", "Local", "RDP", "Eșuate", "Privilegiat", "Prima", "Ultima" })
                        t.Cell().Background("#e2e8f0").Padding(2).Text(head).Bold().FontSize(7.5f);
                    foreach (var u in r.Users)
                        foreach (var v in new[] { u.User, u.InteractiveLogons.ToString(), u.RemoteLogons.ToString(), u.FailedLogons.ToString(), u.PrivilegedSessions.ToString(), L(u.FirstLogonUtc), L(u.LastLogonUtc) })
                            t.Cell().BorderBottom(0.4f).BorderColor(Line).Padding(2).Text(v).FontSize(7.5f);
                });

                col.Item().Text($"3. Cronologia acțiunilor importante ({r.Actions.Count})").Bold().FontSize(11).FontColor(Ink);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(70); c.RelativeColumn(1.3f); c.RelativeColumn(1.4f); c.RelativeColumn(3); });
                    foreach (var head in new[] { "Ora locală", "Cine", "Ce", "Detalii / probă" })
                        t.Cell().Background("#e2e8f0").Padding(2).Text(head).Bold().FontSize(7.5f);
                    foreach (var a in r.Actions.Take(1500))
                    {
                        t.Cell().BorderBottom(0.3f).BorderColor(Line).Padding(2).Text(L(a.TimeUtc)).FontSize(7);
                        t.Cell().BorderBottom(0.3f).BorderColor(Line).Padding(2).Text(a.Who).FontSize(7);
                        t.Cell().BorderBottom(0.3f).BorderColor(Line).Padding(2).Text(a.Action).FontSize(7);
                        t.Cell().BorderBottom(0.3f).BorderColor(Line).Padding(2).Text($"{a.Detail}  [{a.Source}]").FontSize(7);
                    }
                });

                col.Item().Text("4. Acoperirea jurnalelor și goluri de probă").Bold().FontSize(11).FontColor(Ink);
                foreach (var c in f.Coverage)
                    col.Item().Text($"{c.Channel}: {(c.Readable ? $"{c.Records} evenimente citite, cel mai vechi {L(c.OldestUtc)}" : "NECITIT")} {c.Note}").FontSize(7.5f);
                foreach (var g in f.Gaps)
                    col.Item().Text($"Gol: {g.Artifact} — {g.Status.ToSpec()}: {g.Reason}. Impact: {g.Impact}").FontSize(7.5f).FontColor("#b45309");
                col.Item().PaddingTop(6).Text("„DE VERIFICAT” înseamnă că faptele sunt prezentate, dar numai organizația poate spune dacă au fost autorizate. " +
                                              "„NEDETERMINAT” înseamnă că sursa necesară nu a putut fi citită; nu înseamnă că activitatea nu a avut loc.").FontSize(7).Italic().FontColor(Muted);
            });

            ReportFooter.Compose(page.Footer(), "control", $"LogAnalyzer {DfirInfo.ApplicationVersion} · control {f.Host}", seal);
        })).GeneratePdf(path);
    }

    /// <summary>Saves the report (JSON) and its PDF in the case, registered as evidence with SHA-256.</summary>
    public static (string Json, string Pdf) SaveToCase(ControlReport r, CaseWorkspace ws, string inspector, string notes = "")
    {
        var dir = Path.Combine(ws.Root, "Control", $"CONTROL_{r.Facts.CollectedUtc:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var json = Path.Combine(dir, "control_report.json");
        File.WriteAllText(json, JsonSerializer.Serialize(new { r.Facts, r.Checks, r.Users, r.Actions },
            new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
        var pdf = Path.Combine(dir, $"Raport_control_{r.Facts.Host}_{r.Facts.CollectedUtc:yyyyMMdd}.pdf");
        Write(r, pdf, inspector, notes, LogAnalyzer.Dfir.Case.ReportSeal.For(ws));
        ws.RegisterStored(json, "live:" + r.Facts.Host, "control", "control_report", TemporalType.CurrentSnapshot, "StationFactCollector", DfirInfo.ApplicationVersion);
        ws.RegisterStored(pdf, "live:" + r.Facts.Host, "control", "control_report_pdf", TemporalType.Derived, "ControlReportPdf", DfirInfo.ApplicationVersion);
        // WP3b: the report files are also outputs in the custody chain, listed with the chain heads in a manifest next to them.
        var relJson = Path.GetRelativePath(ws.Root, json); var relPdf = Path.GetRelativePath(ws.Root, pdf);
        ws.RecordOutput(relJson, "ControlReportPdf", DfirInfo.ApplicationVersion);
        ws.RecordOutput(relPdf, "ControlReportPdf", DfirInfo.ApplicationVersion);
        ws.WriteManifest(Path.Combine(Path.GetDirectoryName(relJson)!, "report_manifest.json"), [relJson, relPdf], "ControlReportPdf", DfirInfo.ApplicationVersion);
        ws.Audit("control.report", $"{r.Facts.Host} {r.Checks.Count} checks, NECONFORM {r.Count(ControlStatus.Neconform)}, {pdf}");
        return (json, pdf);
    }

    public static string StatusText(ControlStatus s) => s switch
    {
        ControlStatus.Conform => "CONFORM", ControlStatus.Neconform => "NECONFORM", ControlStatus.DeVerificat => "DE VERIFICAT", _ => "NEDETERMINAT",
    };

    private static string StatusColor(ControlStatus s) => s switch
    {
        ControlStatus.Conform => "#15803d", ControlStatus.Neconform => "#b91c1c", ControlStatus.DeVerificat => "#b45309", _ => "#475569",
    };

    private static void Badge(RowDescriptor row, string label, int n, string color) =>
        row.RelativeItem().Padding(2).Background(color).Padding(6).Column(c =>
        {
            c.Item().AlignCenter().Text(n.ToString()).Bold().FontSize(14).FontColor(Colors.White);
            c.Item().AlignCenter().Text(label).Bold().FontSize(7.5f).FontColor(Colors.White);
        });
}
