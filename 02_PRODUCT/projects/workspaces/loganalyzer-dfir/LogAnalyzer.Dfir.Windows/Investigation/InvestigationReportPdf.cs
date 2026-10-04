using LogAnalyzer.Dfir.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Final investigation report: incident chains first (the story), then findings by severity with their evidence,
/// collection and parsing status, evidence gaps and custody pointers. Every statement points to evidence.
/// </summary>
public static class InvestigationReportPdf
{
    private const string Ink = "#0f172a", Muted = "#64748b", Line = "#cbd5e1";

    public static void Write(InvestigationResult r, string path, string investigator, string notes = "")
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var info = r.Case.Info;
        var zone = TimeZoneInfo.Local;
        string L(DateTimeOffset? t) => t is { } v ? TimeZoneInfo.ConvertTime(v, zone).ToString("yyyy-MM-dd HH:mm") : "—";
        var chains = r.Findings.Where(f => f.RuleId == "INCIDENT-CHAIN").ToList();
        var others = r.Findings.Where(f => f.RuleId != "INCIDENT-CHAIN").ToList();

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("Segoe UI").FontColor("#1e293b"));
            page.Header().Column(h =>
            {
                h.Item().Text($"Raport de investigație — {info.Name}").Bold().FontSize(14).FontColor(Ink);
                h.Item().Text($"Caz {info.CaseId} · stația {info.Host} · investigator {investigator} · ore în {zone.Id}").FontSize(7.5f).FontColor(Muted);
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor(Ink);
            });
            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(7);
                col.Item().Row(row =>
                {
                    Box(row, r.Findings.Count(f => f.Severity == Severity.Critical).ToString(), "CRITICE", "#7f1d1d");
                    Box(row, r.Findings.Count(f => f.Severity == Severity.High).ToString(), "RIDICATE", "#b91c1c");
                    Box(row, r.Findings.Count(f => f.Severity == Severity.Medium).ToString(), "MEDII", "#b45309");
                    Box(row, r.Timeline.Count.ToString("N0"), "EVENIMENTE", "#334155");
                    Box(row, r.Gaps.Count.ToString(), "GOLURI", "#475569");
                });
                if (notes.Length > 0) col.Item().Text("Observații: " + notes);

                col.Item().Text("1. Ce s-a întâmplat (lanțuri de incident)").Bold().FontSize(11).FontColor(Ink);
                if (chains.Count == 0) col.Item().Text("Nu s-au găsit constatări grave grupate în timp.").FontColor(Muted);
                foreach (var c in chains)
                    col.Item().Border(0.8f).BorderColor("#7f1d1d").Padding(6).Column(cc =>
                    {
                        cc.Item().Text($"{c.Title}  [{c.Severity.ToSpec()} · {c.Classification.ToSpec()}]").Bold();
                        foreach (var step in c.Description.Split(" → ")) cc.Item().Text("→ " + step).FontSize(8);
                        cc.Item().Text("Atenție: " + string.Join(" ", c.AlternativeExplanations)).FontSize(7).Italic().FontColor(Muted);
                    });

                col.Item().Text("2. Constatări").Bold().FontSize(11).FontColor(Ink);
                foreach (var f in others.OrderByDescending(f => f.Severity).ThenBy(f => f.FirstSeenUtc ?? f.LastSeenUtc))
                    col.Item().Border(0.5f).BorderColor(Line).Padding(5).Column(cc =>
                    {
                        cc.Item().Text($"{f.FindingId} · {f.Title}").Bold();
                        cc.Item().Text($"{f.Severity.ToSpec()} · {f.Classification.ToSpec()} · încredere {f.Confidence.ToSpec()} · {f.Category}{(f.MitreTechniqueId.Length > 0 ? " · MITRE " + f.MitreTechniqueId : "")} · {L(f.FirstSeenUtc)} – {L(f.LastSeenUtc)}")
                            .FontSize(7.5f).FontColor(Muted);
                        cc.Item().Text(f.Description);
                        if (f.ClassificationReason.Length > 0) cc.Item().Text("De ce: " + f.ClassificationReason).FontSize(7.5f);
                        foreach (var e in f.SupportingEvidence.Take(8)) cc.Item().Text($"Probă {e.EvidenceId} · {e.Locator} · {e.Description}").FontSize(7).FontColor(Muted);
                        foreach (var a in f.AlternativeExplanations) cc.Item().Text("Alternativă: " + a).FontSize(7).Italic();
                        foreach (var m in f.MissingEvidence) cc.Item().Text("Lipsește: " + m).FontSize(7).Italic();
                    });

                col.Item().Text("3. Probe colectate și parsate").Bold().FontSize(11).FontColor(Ink);
                foreach (var c in r.Collection)
                    col.Item().Text($"{c.Collector}: {c.Status.ToSpec()}, {c.EvidenceCount} probe ({L(c.StartUtc)}–{L(c.EndUtc)}) {c.Errors}").FontSize(7.5f);
                foreach (var g in r.Parsing.GroupBy(p => p.Parser))
                    col.Item().Text($"{g.Key}: {g.Count()} fișiere, {g.Sum(p => p.Records):N0} înregistrări, statusuri {string.Join(", ", g.Select(p => p.Status.ToSpec()).Distinct())}").FontSize(7.5f);

                col.Item().Text("4. Goluri de probă").Bold().FontSize(11).FontColor(Ink);
                if (r.Gaps.Count == 0) col.Item().Text("Niciun gol raportat.").FontColor(Muted);
                foreach (var g in r.Gaps)
                    col.Item().Text($"{g.Artifact} — {g.Status.ToSpec()}: {g.Reason}. Impact: {g.Impact}. Alternativă: {g.AlternativeSource}").FontSize(7.5f);

                col.Item().PaddingTop(6).Text($"Cronologia completă: Analysis/timeline.csv · constatări: Analysis/findings.json · lanțul de custodie: Logs/chain_of_custody.csv (în {r.Case.Root}). " +
                                              "„NOT_AVAILABLE” înseamnă că sursa nu a putut fi citită, nu că activitatea nu a avut loc. Clasificarea CANDIDATE cere confirmarea analistului.")
                    .FontSize(7).Italic().FontColor(Muted);
            });
            page.Footer().AlignCenter().Text(t =>
            {
                t.Span($"LogAnalyzer {DfirInfo.ApplicationVersion} · {info.CaseId} · pagina ").FontSize(7).FontColor(Muted);
                t.CurrentPageNumber().FontSize(7).FontColor(Muted);
                t.Span(" / ").FontSize(7).FontColor(Muted);
                t.TotalPages().FontSize(7).FontColor(Muted);
            });
        })).GeneratePdf(path);
    }

    private static void Box(RowDescriptor row, string value, string label, string color) =>
        row.RelativeItem().Padding(2).Background(color).Padding(6).Column(c =>
        {
            c.Item().AlignCenter().Text(value).Bold().FontSize(13).FontColor(Colors.White);
            c.Item().AlignCenter().Text(label).Bold().FontSize(7).FontColor(Colors.White);
        });
}
