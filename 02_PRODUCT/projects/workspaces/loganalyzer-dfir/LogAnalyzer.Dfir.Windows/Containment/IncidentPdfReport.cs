using LogAnalyzer.Dfir.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Containment;

/// <summary>
/// PDF report for one contained program: identity, where it went or tried to go (each destination with the source
/// that shows it), persistence, scan verdict, containment actions, findings and evidence gaps.
/// </summary>
public static class IncidentPdfReport
{
    private const string Ink = "#0f172a", Muted = "#64748b", Line = "#cbd5e1", Danger = "#b91c1c", Ok = "#15803d";

    public static void Write(ProcessIncident i, string outputPath, string caseName, string host)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var s = i.Scan;
        var zone = TimeZoneInfo.Local;
        string Local(DateTimeOffset? t) => t is { } v ? TimeZoneInfo.ConvertTime(v, zone).ToString("yyyy-MM-dd HH:mm:ss") : "—";

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI").FontColor("#1e293b"));

            page.Header().Column(h =>
            {
                h.Item().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"Raport incident {i.IncidentId} — {Path.GetFileName(i.ProgramPath)}").Bold().FontSize(14).FontColor(Ink);
                        c.Item().Text($"Caz {i.CaseId} ({caseName}) · stația {host} · generat {Local(DateTimeOffset.UtcNow)} ({zone.Id})").FontSize(8).FontColor(Muted);
                    });
                    r.ConstantItem(130).AlignRight().Container().Background(i.State == ContainmentState.Contained ? Danger : Ink)
                        .PaddingVertical(4).PaddingHorizontal(8).Text(StateText(i.State)).Bold().FontSize(8).FontColor(Colors.White);
                });
                h.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Ink);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(10);

                Section(col, "1. Rezumat");
                col.Item().Text(t =>
                {
                    t.Span("Declanșator: ").Bold(); t.Span(i.Trigger + ". ");
                    t.Span("Izolat la: ").Bold(); t.Span(Local(i.CreatedUtc) + ". ");
                    t.Span("Proces suspendat: ").Bold(); t.Span(i.ProcessSuspended ? "da" : "nu");
                });
                if (s is { SuspicionReasons.Count: > 0 })
                    foreach (var reason in s.SuspicionReasons) col.Item().Text("• " + reason).FontColor(Danger);
                else col.Item().Text("Scanarea nu a găsit motive de suspiciune.").FontColor(Ok);

                Section(col, "2. Identitatea programului");
                KeyValues(col, new (string, string)[]
                {
                    ("Cale", i.ProgramPath),
                    ("PID / proces", s is null ? "—" : $"{s.Process.Pid?.ToString() ?? "—"} / {s.Process.Name}"),
                    ("Linie de comandă", s?.Process.CommandLine is { Length: > 0 } cl ? cl : "—"),
                    ("Proces părinte", s is null ? "—" : $"{s.Process.ParentPid?.ToString() ?? "—"} {s.Process.ParentName} {s.Process.ParentPath}"),
                    ("Utilizator", s?.Process.User is { Length: > 0 } u ? u : "—"),
                    ("Pornit la", Local(s?.Process.StartedUtc)),
                    ("SHA-256", s?.File?.Sha256 ?? "—"),
                    ("Dimensiune", s?.File is { } f0 ? $"{f0.Size:N0} B" : "—"),
                    ("Creat / modificat", s?.File is { } f1 ? $"{Local(f1.CreatedUtc)} / {Local(f1.ModifiedUtc)}" : "—"),
                    ("Semnătură", s?.File is { } f2 ? $"{f2.Signature.Status} ({f2.Signature.Kind}) {f2.Signature.Signer}" : "—"),
                    ("Producător / produs", s?.File is { } f3 ? $"{f3.CompanyName} / {f3.ProductName} / {f3.FileDescription}" : "—"),
                    ("Nume original", s?.File?.OriginalFileName is { Length: > 0 } on ? on : "—"),
                    ("PE", s?.Pe is { } pe ? (pe.Error.Length > 0 ? pe.Error : $"{pe.Machine}, {pe.Subsystem}{(pe.IsDll ? ", DLL" : "")}, compilat {Local(pe.CompileTimeUtc)}, secțiuni {string.Join(" ", pe.Sections)}") : "—"),
                    ("DLL-uri importate", s?.Pe is { ImportedDlls.Count: > 0 } pe2 ? string.Join(", ", pe2.ImportedDlls) : "—"),
                    ("Microsoft Defender", s?.Defender is { } d ? DefenderText(d) : "nescanat"),
                });

                Section(col, "3. Unde a mers / unde a încercat să meargă");
                var intents = i.AllNetworkIntents.ToList();
                if (intents.Count == 0)
                    col.Item().Text("Nicio conexiune observată. Dacă auditul „Filtering Platform Connection” nu era activ, încercările blocate nu au fost înregistrate de Windows (vezi golurile de probă).").FontColor(Muted);
                else
                    Table(col, new[] { "Ora", "Tip", "Destinație", "Port", "Protocol", "Sursa probei" },
                        intents.OrderBy(n => n.Time.Utc).Take(300).Select(n => new[]
                        {
                            Local(n.Time.Utc), n.Kind, n.Destination, n.Port?.ToString() ?? "", n.Protocol, n.Source,
                        }));
                if (s is { Iocs.Count: > 0 })
                {
                    col.Item().Text("Adrese, domenii și URL-uri găsite în conținutul fișierului (intenții, nu conexiuni dovedite):").Bold();
                    Table(col, new[] { "Tip", "Valoare" }, s.Iocs.Where(x => x.Type is "url" or "domain" or "ipv4" or "ipv6").Take(150).Select(x => new[] { x.Type, x.Value }));
                }
                if (s is { NetworkApiReferences.Count: > 0 })
                    col.Item().Text("Funcții de rețea referite: " + string.Join(", ", s.NetworkApiReferences));

                Section(col, "4. Persistență și istoric de rulare");
                if (s is null || (s.Persistence.Count == 0 && s.Prefetch.Count == 0))
                    col.Item().Text("Nu au fost găsite intrări de autostart sau fișiere Prefetch.").FontColor(Muted);
                else
                {
                    if (s.Persistence.Count > 0)
                        Table(col, new[] { "Mecanism", "Locație", "Valoare" }, s.Persistence.Select(p => new[] { p.Mechanism, p.Location, p.Value }));
                    foreach (var pf in s.Prefetch)
                        col.Item().Text($"Prefetch {Path.GetFileName(pf.PrefetchFile)}: {pf.RunCount} rulări; ultimele: {string.Join(", ", pf.RunTimesUtc.Take(8).Select(t => Local(t)))}");
                }
                if (s is { SiblingFilesRecentlyChanged.Count: > 0 })
                {
                    col.Item().Text("Fișiere din același folder modificate în ultimele 30 de zile:").Bold();
                    foreach (var f in s.SiblingFilesRecentlyChanged.Take(40)) col.Item().Text("• " + f).FontSize(8);
                }

                Section(col, "5. Constatări");
                if (i.Findings.Count == 0) col.Item().Text("Nicio constatare.").FontColor(Muted);
                foreach (var f in i.Findings)
                    col.Item().Border(0.6f).BorderColor(Line).Padding(6).Column(c =>
                    {
                        c.Item().Text($"{f.Title}").Bold();
                        c.Item().Text($"Severitate {f.Severity.ToSpec()} · Clasificare {f.Classification.ToSpec()} · Încredere {f.Confidence.ToSpec()}").FontSize(8).FontColor(Muted);
                        c.Item().Text(f.Description);
                        if (f.ClassificationReason.Length > 0) c.Item().Text("De ce: " + f.ClassificationReason).FontSize(8);
                        foreach (var e in f.SupportingEvidence.Take(10)) c.Item().Text($"Probă: {e.EvidenceId} · {e.Locator} · {e.Description}").FontSize(8);
                        foreach (var a in f.AlternativeExplanations) c.Item().Text("Alternativă: " + a).FontSize(8);
                        foreach (var n in f.RecommendedNextSteps) c.Item().Text("Pas următor: " + n).FontSize(8);
                    });

                Section(col, "6. Acțiuni de izolare (jurnal)");
                Table(col, new[] { "Ora", "Acțiune", "Țintă", "Rezultat", "Detaliu", "Operator" },
                    i.Actions.Select(a => new[] { Local(a.TimeUtc), a.Action, a.Target, a.Success ? "OK" : "EȘUAT", a.Detail, a.Operator }));
                if (i.FirewallRules.Count > 0)
                    col.Item().Text("Reguli de firewall active: " + string.Join(", ", i.FirewallRules)).FontSize(8);

                Section(col, "7. Goluri de probă");
                if (s is null || s.Gaps.Count == 0) col.Item().Text("Niciun gol raportat de scanare.").FontColor(Muted);
                else Table(col, new[] { "Artefact", "Status", "Motiv", "Impact", "Sursă alternativă" },
                    s.Gaps.Select(g => new[] { g.Artifact, g.Status.ToSpec(), g.Reason, g.Impact, g.AlternativeSource }));
                if (i.EvidenceIds.Count > 0)
                    col.Item().Text("Probe în caz: " + string.Join(", ", i.EvidenceIds) + " (SHA-256 și lanțul de custodie în Logs/chain_of_custody.csv).").FontSize(8);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span($"LogAnalyzer {DfirInfo.ApplicationVersion} · {i.IncidentId} · pagina ").FontSize(7.5f).FontColor(Muted);
                t.CurrentPageNumber().FontSize(7.5f).FontColor(Muted);
                t.Span(" / ").FontSize(7.5f).FontColor(Muted);
                t.TotalPages().FontSize(7.5f).FontColor(Muted);
            });
        })).GeneratePdf(outputPath);
    }

    private static string StateText(ContainmentState s) => s switch
    {
        ContainmentState.Contained => "IZOLAT",
        ContainmentState.Released => "IZOLARE RIDICATĂ",
        _ => "IZOLARE EȘUATĂ",
    };

    private static string DefenderText(DefenderVerdict d) => d.ThreatFound switch
    {
        true => "AMENINȚARE DETECTATĂ: " + Shorten(d.Output, 400),
        false => "nicio amenințare detectată",
        null => $"rezultat nedeterminat ({d.Status.ToSpec()}): {Shorten(d.Output, 300)}",
    };

    private static string Shorten(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private static void Section(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(4).Text(title).Bold().FontSize(11).FontColor(Ink);

    private static void KeyValues(ColumnDescriptor col, IEnumerable<(string Key, string Value)> rows) =>
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { c.ConstantColumn(120); c.RelativeColumn(); });
            foreach (var (k, v) in rows)
            {
                t.Cell().BorderBottom(0.4f).BorderColor(Line).PaddingVertical(2).Text(k).Bold().FontSize(8.5f);
                t.Cell().BorderBottom(0.4f).BorderColor(Line).PaddingVertical(2).Text(v).FontSize(8.5f);
            }
        });

    private static void Table(ColumnDescriptor col, string[] headers, IEnumerable<string[]> rows) =>
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { foreach (var _ in headers) c.RelativeColumn(); });
            t.Header(h =>
            {
                foreach (var head in headers)
                    h.Cell().Background("#e2e8f0").Padding(3).Text(head).Bold().FontSize(8);
            });
            foreach (var r in rows)
                foreach (var cell in r)
                    t.Cell().BorderBottom(0.4f).BorderColor(Line).Padding(3).Text(cell ?? "").FontSize(7.5f);
        });
}
