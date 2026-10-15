using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Verification;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>"Raport de control al stației": summary, every check with its evidence, user activity, action timeline, gaps.</summary>
public static class ControlReportPdf
{
    private const string Ink = "#0f172a", Muted = "#64748b", Line = "#cbd5e1";

    /// <summary>Tests: receives the F2 pre-report line printed on page 1 (async-local).</summary>
    public static readonly AsyncLocal<Action<string>?> PrintedLines = new();

    /// <summary>Tests: receives the texts of the page-1 "Concluzie pe scurt" box and the section headings, in print order (async-local).</summary>
    public static readonly AsyncLocal<Action<string>?> TextObserver = new();

    /// <param name="header">WP18 S5: the "proces-verbal" header (unit, station, Hardware ID, period, inspector, role, registration number, marking, signatures).</param>
    /// <param name="result">WP18 S5: the five answers, printed on the first page before the checks, together with the evidence gaps.</param>
    public static void Write(ControlReport r, string path, string inspector, string notes = "", VerificationReport? verification = null, LogAnalyzer.Dfir.Case.ReportSeal? seal = null,
        ControlReportHeader? header = null, ControlResultScreen? result = null, IReadOnlyList<LogAnalyzer.Dfir.Flow.ManualCheckResult>? manual = null, string authorizingAct = "",
        LogAnalyzer.Dfir.Case.PreReportCheck? preCheck = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var f = r.Facts;
        // F2: without a case (preCheck null) there is no evidence to re-verify and the line says so; SaveToCase passes the case re-check.
        var pre = preCheck ?? LogAnalyzer.Dfir.Case.PreReportCheck.ForCase(null);
        string W(string s) => pre.Unconfirm(s);
        string L(DateTimeOffset? t) => t is { } v ? TimeZoneInfo.ConvertTime(v, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm") : "—";
        // every text printed through P is also given to TextObserver (tests read the page-1 box without parsing the PDF)
        TextBlockDescriptor P(ColumnDescriptor c, string text) { TextObserver.Value?.Invoke(text); return c.Item().Text(text); }

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("Segoe UI").FontColor("#1e293b"));
            page.Header().Column(h =>
            {
                if (header is not null) h.Item().AlignCenter().Text(header.MarkingLine).Bold().FontSize(8).FontColor("#b91c1c");
                if (r.Officer is null) h.Item().AlignCenter().Text(LogAnalyzer.Dfir.Reporting.OfficerSignature.UnsignedLabel).Bold().FontSize(9).FontColor("#b91c1c");
                h.Item().Text(header is null ? $"Raport de control — stația {f.Host}" : $"Raport de control pentru proces-verbal — stația {f.Host}").Bold().FontSize(14).FontColor(Ink);
                h.Item().Text($"Perioada controlată: {L(f.PeriodStartUtc)} – {L(f.PeriodEndUtc)} (ora locală) · colectat {L(f.CollectedUtc)} · " +
                              $"Ediția: {LogAnalyzer.Core.Services.Edition.StationPostures.EditionLabel(f.Posture)} · drepturi de administrator: {(f.IsAdministrator ? "da" : "NU (rezultate incomplete)")}")
                    .FontSize(7.5f).FontColor(Muted);
                h.Item().Text($"Verificator: {inspector}").FontSize(7.5f).FontColor(Muted);
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor(Ink);
            });

            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(8);
                col.Item().Text(pre.Line).FontSize(8).Bold().FontColor(pre.BlocksConfirmed ? "#b91c1c" : pre.Applicable ? "#065f46" : "#78350f");
                PrintedLines.Value?.Invoke(pre.Line);
                // page 1: "Concluzie pe scurt", as in the investigation report (review 2026-10-12)
                var box = ControlConclusion.Build(r);
                col.Item().Background("#f8fafc").Border(1.2f).BorderColor(Ink).Padding(8).Column(sc =>
                {
                    sc.Spacing(3);
                    P(sc, "Concluzie pe scurt").Bold().FontSize(12).FontColor(Ink);
                    P(sc, W(box.Verdict)).Bold().FontSize(9);
                    if (box.Serious.Count > 0)
                    {
                        P(sc, "Ce nu este conform (pe domenii)").Bold().FontSize(8.5f);
                        foreach (var s in box.Serious) P(sc, "• " + W(s)).FontSize(8);
                    }
                    P(sc, "Ce înseamnă pentru dumneavoastră").Bold().FontSize(8.5f);
                    P(sc, box.Meaning).FontSize(8);
                    P(sc, "Pași următori").Bold().FontSize(8.5f);
                    foreach (var s in box.NextSteps) P(sc, "• " + W(s)).FontSize(8);
                    P(sc, "Ce nu s-a putut verifica").Bold().FontSize(8.5f);
                    foreach (var s in box.NotChecked) P(sc, "• " + W(s)).FontSize(8);
                });
                col.Item().Row(row =>
                {
                    Badge(row, "CONFORM", r.Count(ControlStatus.Conform), "#15803d");
                    Badge(row, "NECONFORM", r.Count(ControlStatus.Neconform), "#b91c1c");
                    Badge(row, "DE VERIFICAT", r.Count(ControlStatus.DeVerificat), "#b45309");
                    Badge(row, "NEDETERMINAT", r.Count(ControlStatus.Nedeterminat), "#475569");
                    if (r.Count(ControlStatus.NuSeAplica) > 0) Badge(row, "NU SE APLICĂ", r.Count(ControlStatus.NuSeAplica), "#94a3b8");
                });
                if (header is not null)
                    col.Item().Border(0.8f).BorderColor(Ink).Padding(6).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(170); c.RelativeColumn(); });
                        foreach (var (label, value) in header.Lines())
                        {
                            t.Cell().Padding(2).Text(label).Bold().FontSize(8);
                            t.Cell().Padding(2).Text(value).FontSize(8);
                        }
                    });
                if (result is not null)
                    col.Item().Border(0.8f).BorderColor(Line).Padding(6).Column(rc =>
                    {
                        rc.Item().Text(W(result.Headline)).Bold().FontSize(11).FontColor(Ink);
                        rc.Item().Text(W("Există o problemă? " + result.Problem)).FontSize(8.5f);
                        rc.Item().Text(W("Cât de grav? " + result.Seriousness)).FontSize(8.5f);
                        rc.Item().Text(W("Sunt probele de încredere? " + (pre.BlocksConfirmed ? "NU — " + pre.Line + " " : "") + result.Trust)).FontSize(8.5f);
                        rc.Item().Text(W("Ce s-a găsit? " + result.Found)).FontSize(8.5f);
                        rc.Item().Text("Ce urmează?").Bold().FontSize(8.5f);
                        foreach (var st in result.NextSteps) rc.Item().Text(W("• " + st)).FontSize(8);
                    });
                if (header is not null || result is not null)
                {
                    col.Item().Text("Surse lipsă sau incomplete și surse necitite (înaintea verificărilor, pentru că limitează ce se poate afirma)").Bold().FontSize(9.5f).FontColor("#b45309");
                    if (f.Gaps.Count == 0 && f.Coverage.All(c => c.Readable)) col.Item().Text("Niciun gol raportat; toate sursele au fost citite.").FontSize(8).FontColor(Muted);
                    foreach (var c in f.Coverage.Where(c => !c.Readable)) col.Item().Text($"Sursă necitită: {c.Channel} {c.Note}").FontSize(8).FontColor("#b45309");
                    foreach (var g in f.Gaps) col.Item().Text($"Gol: {g.Artifact} — {g.Status.ToSpec()}: {g.Reason}. Impact: {g.Impact}").FontSize(8).FontColor("#b45309");
                }
                if (notes.Length > 0) col.Item().Text("Observații: " + notes);
                // WP4: when the case holds a verification run of its investigation findings, one line (and the warning, if any) is shown here too.
                if (verification is not null)
                    col.Item().Border(0.8f).BorderColor(Line).Padding(5).Column(vc =>
                    {
                        vc.Item().Text(verification.Banner + " (constatările investigației din acest caz; verificare automată, nu externă)").FontSize(8).Bold().FontColor(Ink);
                        if (verification.Warning is { } warn)
                            vc.Item().PaddingTop(3).Background("#fef2f2").Border(1).BorderColor("#b91c1c").Padding(4).Text(warn).FontSize(8).Bold().FontColor("#7f1d1d");
                    });

                P(col, "1. Verificări").Bold().FontSize(11).FontColor(Ink);
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
                            cc.Item().Text(W(c.Detail));
                            foreach (var e in c.Evidence.Take(25)) cc.Item().Text("– " + W(e)).FontSize(7).FontColor(Muted);
                            if (c.Evidence.Count > 25) cc.Item().Text($"… încă {LogAnalyzer.Dfir.Language.RoText.Count(c.Evidence.Count - 25, "intrare", "intrări")} (în fișierul JSON al raportului)").FontSize(7).FontColor(Muted);
                            if (c.Recommendation.Length > 0) cc.Item().Text(W("Recomandare: " + c.Recommendation)).FontSize(7.5f).Italic();
                        });
                }

                // checks only the controller can do (physical, documents): every question is printed; unanswered ones as „Neverificat”
                col.Item().Text("1b. Verificări făcute de controlor (fizic și pe documente)").Bold().FontSize(11).FontColor(Ink);
                var answers = (manual ?? []).ToDictionary(m => m.Id);
                col.Item().Text(LogAnalyzer.Dfir.Flow.ManualChecks.Summary(answers.Values)).FontSize(8.5f);
                foreach (var q in LogAnalyzer.Dfir.Flow.ManualChecks.All)
                {
                    var a = answers.GetValueOrDefault(q.Id);
                    var text = $"{q.Id} · {q.Question} — {LogAnalyzer.Dfir.Flow.ManualChecks.AnswerText(a?.Answer ?? LogAnalyzer.Dfir.Flow.ManualAnswer.NotAnswered)}";
                    col.Item().Text(text + (a?.Note is { Length: > 0 } n ? $". Observație: {n}" : "")).FontSize(8)
                        .FontColor(a?.Answer == LogAnalyzer.Dfir.Flow.ManualAnswer.No ? "#b91c1c" : Ink);
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

                col.Item().Text("4. Acoperirea jurnalelor și surse lipsă sau incomplete").Bold().FontSize(11).FontColor(Ink);
                foreach (var c in f.Coverage)
                    col.Item().Text($"{c.Channel}: {(c.Readable ? $"{LogAnalyzer.Dfir.Language.RoText.Count(c.Records, "eveniment", "evenimente")} citite, cel mai vechi {L(c.OldestUtc)}" : "NECITIT")} {c.Note}").FontSize(7.5f);
                foreach (var g in f.Gaps)
                    col.Item().Text($"Gol: {g.Artifact} — {g.Status.ToSpec()}: {g.Reason}. Impact: {g.Impact}").FontSize(7.5f).FontColor("#b45309");
                col.Item().PaddingTop(6).Text("„DE VERIFICAT” înseamnă că faptele sunt prezentate, dar numai organizația poate spune dacă au fost autorizate. " +
                                              "„NEDETERMINAT” înseamnă că sursa necesară nu a putut fi citită; nu înseamnă că activitatea nu a avut loc.").FontSize(7).Italic().FontColor(Muted);

                // mandatory "Limitări" (legal research G7): what this report can and cannot prove, stated every time
                col.Item().PaddingTop(6).Text("5. Limitări").Bold().FontSize(11).FontColor(Ink);
                foreach (var lim in Limitations(f))
                    col.Item().Text("• " + lim).FontSize(7.5f);
                // basis and declaration (legal research G1, G4)
                col.Item().PaddingTop(8).Text("Actul care autorizează controlul: " + (authorizingAct.Length > 0 ? authorizingAct : "nespecificat în scopul cazului")).FontSize(8)
                    .FontColor(authorizingAct.Length > 0 ? Ink : "#b45309");
                col.Item().Text($"Declar că am efectuat controlul stației {f.Host} folosind aplicația indicată în subsolul paginii, doar prin citire, fără a modifica stația, " +
                                "și că rezultatele de mai sus reproduc fidel ce a constatat aplicația și ce am verificat personal.").FontSize(8);
                if (header is not null)
                    col.Item().PaddingTop(28).Row(row =>
                    {
                        row.RelativeItem().Column(c => { c.Item().LineHorizontal(0.8f).LineColor(Ink); c.Item().PaddingTop(3).Text(header.SignatureLeft).FontSize(8); c.Item().Text("Nume, semnătura, data").FontSize(7).FontColor(Muted); });
                        row.ConstantItem(40);
                        row.RelativeItem().Column(c => { c.Item().LineHorizontal(0.8f).LineColor(Ink); c.Item().PaddingTop(3).Text(header.SignatureRight).FontSize(8); c.Item().Text("Nume, semnătura, data").FontSize(7).FontColor(Muted); });
                    });
                // owner decision 2026-10-16: anchor code for the next control and the security officer's signature (last section, so it is on the last page)
                P(col, "6. Ancorarea jurnalului aplicației și semnătura ofițerului de securitate").Bold().FontSize(11).FontColor(Ink);
                foreach (var line in OfficerSectionLines(r)) P(col, line).FontSize(8.5f);
            });

            ReportFooter.Compose(page.Footer(), "control", $"LogAnalyzer {DfirInfo.ApplicationVersion} · control {f.Host}", seal, header?.MarkingLine);
        })).GeneratePdf(path);
    }

    /// <summary>The lines of section 6: the anchor code to copy on the proces-verbal, and either the officer who signed or the unsigned label with the registered fingerprints.</summary>
    public static IReadOnlyList<string> OfficerSectionLines(ControlReport r)
    {
        var l = new List<string>();
        l.Add(r.Anchor is { } a
            ? a.Line + ". Treceți codul pe procesul-verbal; la controlul următor se introduce în aplicație ca să se verifice că jurnalul aplicației nu a fost rescris (verificarea A11)."
            : "Cod de ancorare: indisponibil (jurnalul aplicației nu a putut fi citit).");
        if (r.Officer is { } o)
        {
            l.Add($"Semnat digital de ofițerul de securitate: {o.Officer}, la {TimeZoneInfo.ConvertTime(o.SignedUtc, TimeZoneInfo.Local):yyyy-MM-dd HH:mm} (ora stației).");
            l.Add($"Amprenta cheii ofițerului: {o.Fingerprint}. Semnătura este în fișierul alăturat cu același nume și terminația „{LogAnalyzer.Dfir.Reporting.OfficerSignature.Extension}”; se verifică în aplicație („Verifică raport”) sau cu LogAnalyzer.KeyGen verify-report <raport.pdf> --officer-fingerprint {o.Fingerprint}.");
        }
        else
        {
            l.Add(LogAnalyzer.Dfir.Reporting.OfficerSignature.UnsignedLabel + ". Raportul devine oficial doar după ce ofițerul de securitate îl semnează cu cheia sa („Semnează ca ofițer de securitate”).");
            if (r.RegisteredOfficers.Count == 0) l.Add("Ofițeri de securitate înregistrați: niciunul.");
            foreach (var o2 in r.RegisteredOfficers) l.Add($"Ofițer de securitate înregistrat: {o2.Name}, amprenta {o2.Fingerprint} ({o2.Basis}).");
        }
        return l;
    }

    /// <summary>The limits of a control report, always printed (legal research G7, docs/dfir/RESEARCH_2026-10-11_PROBE_VALOARE_JURIDICA.md).</summary>
    public static IReadOnlyList<string> Limitations(StationFacts f)
    {
        var l = new List<string>
        {
            "Controlul a citit stația; nu a modificat nimic pe ea. Concluziile se sprijină doar pe sursele enumerate la punctul 4.",
            "Lipsa unei urme nu dovedește că un fapt nu a avut loc: jurnalele au o capacitate limitată, se suprascriu și pot fi șterse.",
            "Windows păstrează pentru fiecare dispozitiv doar ultima conectare; istoricul complet depinde de jurnale.",
            "Faptele „DE VERIFICAT” trebuie confirmate cu documentele organizației (aprobări, registre); aplicația nu le poate califica singură.",
            "Orele cu „UTC” sunt în timp universal, restul în ora stației; ora locală a stației și fusul ei orar sunt trecute în subsolul fiecărei pagini.",
        };
        if (!f.IsAdministrator) l.Insert(0, "Controlul a rulat FĂRĂ drepturi de administrator: jurnalul Security, politica de audit și o parte din setări nu au putut fi citite.");
        if (f.Gaps.Count > 0) l.Add($"{(f.Gaps.Count == 1 ? "Există o sursă lipsă sau incompletă" : "Există " + LogAnalyzer.Dfir.Language.RoText.Count(f.Gaps.Count, "sursă lipsă sau incompletă", "surse lipsă sau incomplete"))} (punctul 4); verificările care depind de {(f.Gaps.Count == 1 ? "ea" : "ele")} sunt NEDETERMINATE.");
        return l;
    }

    /// <summary>Saves the report (JSON) and its PDF in the case, registered as evidence with SHA-256.</summary>
    public static (string Json, string Pdf) SaveToCase(ControlReport r, CaseWorkspace ws, string inspector, string notes = "", ControlReportHeader? header = null, ControlResultScreen? result = null,
        IReadOnlyList<LogAnalyzer.Dfir.Flow.ManualCheckResult>? manual = null)
    {
        var dir = Path.Combine(ws.Root, "Control", $"CONTROL_{r.Facts.CollectedUtc:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var json = Path.Combine(dir, "control_report.json");
        File.WriteAllText(json, JsonSerializer.Serialize(new { r.Facts, r.Checks, r.Users, r.Actions, ManualChecks = manual ?? [] },
            new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
        var pdf = Path.Combine(dir, $"Raport_control_{r.Facts.Host}_{r.Facts.CollectedUtc:yyyyMMdd}.pdf");
        var pre = LogAnalyzer.Dfir.Case.PreReportCheck.ForCase(ws);   // F2: re-verify the case evidence first; its audit entry is inside the printed chain heads
        Write(r, pdf, inspector, notes, Investigation.InvestigationReportPdf.ReadVerification(ws.Root), LogAnalyzer.Dfir.Case.ReportSeal.For(ws), header, result, manual, ws.Info.Scope.AuthorizingAct, pre);
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

    /// <summary>
    /// The official copy, rendered with the officer's signature section filled in (<see cref="ControlReport.Officer"/> must be set) so the signature covers it.
    /// Written next to the draft as "…_OFICIAL.pdf" and registered in the case; the draft and its JSON are not touched.
    /// </summary>
    public static string SaveOfficialToCase(ControlReport r, CaseWorkspace ws, string inspector, string notes = "", ControlReportHeader? header = null, ControlResultScreen? result = null,
        IReadOnlyList<LogAnalyzer.Dfir.Flow.ManualCheckResult>? manual = null)
    {
        if (r.Officer is null) throw new InvalidOperationException("Copia oficială se generează doar cu datele ofițerului care semnează.");
        var dir = Path.Combine(ws.Root, "Control", $"CONTROL_{r.Facts.CollectedUtc:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var pdf = Path.Combine(dir, $"Raport_control_{r.Facts.Host}_{r.Facts.CollectedUtc:yyyyMMdd}_OFICIAL_{r.Officer.SignedUtc:HHmmss}.pdf");
        var pre = LogAnalyzer.Dfir.Case.PreReportCheck.ForCase(ws);
        Write(r, pdf, inspector, notes, Investigation.InvestigationReportPdf.ReadVerification(ws.Root), LogAnalyzer.Dfir.Case.ReportSeal.For(ws), header, result, manual, ws.Info.Scope.AuthorizingAct, pre);
        ws.RegisterStored(pdf, "live:" + r.Facts.Host, "control", "control_report_pdf_official", TemporalType.Derived, "ControlReportPdf", DfirInfo.ApplicationVersion);
        ws.RecordOutput(Path.GetRelativePath(ws.Root, pdf), "ControlReportPdf", DfirInfo.ApplicationVersion);
        ws.Audit("control.report_official", $"{r.Facts.Host} officer {r.Officer.Fingerprint}, {pdf}");
        return pdf;
    }

    public static string StatusText(ControlStatus s) => s switch
    {
        ControlStatus.Conform => "CONFORM", ControlStatus.Neconform => "NECONFORM", ControlStatus.DeVerificat => "DE VERIFICAT", ControlStatus.NuSeAplica => "NU SE APLICĂ", _ => "NEDETERMINAT",
    };

    private static string StatusColor(ControlStatus s) => s switch
    {
        ControlStatus.Conform => "#15803d", ControlStatus.Neconform => "#b91c1c", ControlStatus.DeVerificat => "#b45309", ControlStatus.NuSeAplica => "#94a3b8", _ => "#475569",
    };

    private static void Badge(RowDescriptor row, string label, int n, string color) =>
        row.RelativeItem().Padding(2).Background(color).Padding(6).Column(c =>
        {
            c.Item().AlignCenter().Text(n.ToString()).Bold().FontSize(14).FontColor(Colors.White);
            c.Item().AlignCenter().Text(label).Bold().FontSize(7.5f).FontColor(Colors.White);
        });
}
