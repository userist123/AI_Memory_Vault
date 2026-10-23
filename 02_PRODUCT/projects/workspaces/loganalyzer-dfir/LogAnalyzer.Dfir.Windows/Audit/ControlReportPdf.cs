using LogAnalyzer.Dfir.Analysis;
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

        var generated = Document.Create(doc => { doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("Segoe UI").FontColor("#1e293b"));
            page.Header().Column(h =>
            {
                if (header is not null) h.Item().AlignCenter().Text(header.MarkingLine).Bold().FontSize(8).FontColor("#b91c1c");
                h.Item().AlignCenter().Text(OfficialLabel).Bold().FontSize(9).FontColor("#b91c1c");
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

                // classified information: per file, accesses and exits (approved / unjustified / to verify), worst first
                P(col, ClassifiedSectionTitle).Bold().FontSize(11).FontColor(Ink);
                foreach (var (text, head, color) in ClassifiedSectionLines(r))
                {
                    var t = P(col, W(text)).FontSize(head ? 8.5f : 7.5f).FontColor(color);
                    if (head) t.Bold();
                }

                // owner decision 2026-10-16: every device the real-time monitor saw in the period (check D05), with state, registration number and alerts
                P(col, DeviceSectionTitle).Bold().FontSize(11).FontColor(Ink);
                foreach (var (text, head, color) in DeviceSectionLines(r))
                {
                    var t = P(col, W(text)).FontSize(head ? 8.5f : 7.5f).FontColor(color);
                    if (head) t.Bold();
                }

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
                // usability 2026-10-16: every technical word, result and check id printed in this report, explained in plain Romanian (only those that appear)
                var terms = TermCatalog.AppendixLines(Corpus(r, inspector, notes, header, result, manual));
                if (terms.Count > 0)
                {
                    P(col, TermCatalog.AppendixTitle).Bold().FontSize(11).FontColor(Ink);
                    foreach (var line in terms) P(col, "• " + line).FontSize(7.5f);
                }
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
        });
        // the officer's signature place: an empty box on a last page of its own, and the empty signature field over it (owner decision, review round 3)
        SignatureBoxPage.Add(doc, header?.MarkingLine, SignHere); }).GeneratePdf();
        File.WriteAllBytes(path, LogAnalyzer.Dfir.Reporting.SignatureField.Reserve(generated));
    }

    /// <summary>
    /// The texts this report prints (header, conclusion, results, checks, users, actions, classified files, coverage, limitations, the officer section and
    /// the signature page): what the „Ce înseamnă termenii” appendix is built from, so it lists only the terms that appear in this report.
    /// </summary>
    public static string Corpus(ControlReport r, string inspector = "", string notes = "", ControlReportHeader? header = null, ControlResultScreen? result = null,
        IReadOnlyList<LogAnalyzer.Dfir.Flow.ManualCheckResult>? manual = null)
    {
        var f = r.Facts;
        var sb = new System.Text.StringBuilder();
        void A(string? s) { if (!string.IsNullOrEmpty(s)) sb.Append(s).Append('\n'); }
        A(OfficialLabel); A(SignHere); A(inspector); A(notes);
        A(StatusText(ControlStatus.Conform)); A(StatusText(ControlStatus.Neconform)); A(StatusText(ControlStatus.DeVerificat)); A(StatusText(ControlStatus.Nedeterminat));
        if (r.Count(ControlStatus.NuSeAplica) > 0) A(StatusText(ControlStatus.NuSeAplica));
        if (header is not null) { A(header.MarkingLine); A(header.SignatureLeft); A(header.SignatureRight); foreach (var (l, v) in header.Lines()) { A(l); A(v); } }
        var box = ControlConclusion.Build(r);
        A(box.Verdict); A(box.Meaning); foreach (var s in box.Serious.Concat(box.NextSteps).Concat(box.NotChecked)) A(s);
        if (result is not null) { A(result.Headline); A(result.Problem); A(result.Seriousness); A(result.Trust); A(result.Found); foreach (var s in result.NextSteps) A(s); }
        foreach (var c in f.Coverage) { A(c.Channel); A(c.Note); }
        foreach (var g in f.Gaps) { A(g.Artifact); A(g.Reason); A(g.Impact); }
        foreach (var c in r.Checks)
        {
            A(StatusText(c.Status)); A(c.Id); A(c.Area); A(c.Title); A(c.Detail); A(c.Recommendation);
            foreach (var e in c.Evidence.Take(25)) A(e);
        }
        foreach (var q in LogAnalyzer.Dfir.Flow.ManualChecks.All) { A(q.Id); A(q.Question); }
        foreach (var m in manual ?? []) A(m.Note);
        if (r.Users.Count > 0) { A("RDP"); foreach (var u in r.Users) A(u.User); }
        foreach (var a in r.Actions.Take(1500)) { A(a.Action); A(a.Detail); A(a.Source); }
        foreach (var (text, _, _) in ClassifiedSectionLines(r)) A(text);
        foreach (var (text, _, _) in DeviceSectionLines(r)) A(text);
        foreach (var lim in Limitations(f)) A(lim);
        foreach (var line in OfficerSectionLines(r)) A(line);
        return sb.ToString();
    }

    /// <summary>Printed on every page: the report is official only with the security officer's qualified electronic signature (owner decision 2026-10-16).</summary>
    /// <summary>The line under "Semnătura ofițerului de securitate" on the signature page.</summary>
    public const string SignHere = "Se semnează doar în acest câmp, cu certificatul calificat de pe card: în LogAnalyzer („Semnează ca ofițer de securitate”) sau în Adobe, alegând câmpul existent „Semnatura ofiterului de securitate”.";

    public const string OfficialLabel = "Fără semnătura electronică calificată a ofițerului de securitate acesta NU este raport oficial";

    /// <summary>The lines of section 6: the anchor code to copy on the proces-verbal, how the report becomes official, and the registered officers' fingerprints.</summary>
    public static IReadOnlyList<string> OfficerSectionLines(ControlReport r)
    {
        var l = new List<string>();
        l.Add(r.Anchor is { } a
            ? a.Line + ". Treceți codul pe procesul-verbal; la controlul următor se introduce în aplicație ca să se verifice că jurnalul aplicației nu a fost rescris (verificarea A11)."
            : "Cod de ancorare: indisponibil (jurnalul aplicației nu a putut fi citit).");
        // the install date is recorded with the anchor and compared at the next control (A16): it goes on the paper next to the code
        // the values STORED in the anchored state entry (read back from the log), never a fresh computation
        l.Add(r.AnchoredState is { } st
            ? "Data instalării aplicației (din starea ancorată): " + (st.Utc is { } inst ? inst.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC" : "necunoscută") +
              (st.Basis.Length > 0 ? $" ({st.Basis})" : "") +
              (st.Chain is { } c ? $"; identitatea jurnalului: prima înregistrare {c.FirstKind} la {c.FirstWhenUtc}, amprenta {(c.FirstHash.Length > 16 ? c.FirstHash[..16] : c.FirstHash)}" : "") +
              ". Treceți-le pe procesul-verbal lângă cod."
            : "Data instalării aplicației: indisponibilă (starea ancorată nu a fost scrisă).");
        // review of PR #295: the register in force goes on the paper, so the officer compares it across stations
        if (r.Facts.SignedMedia is { } sm)
            l.Add((sm.Usable
                    ? $"Registrul de medii semnat în vigoare: versiunea {sm.Doc!.Version}, SHA-256 {sm.Sha256} (emis de „{sm.Doc.StationId}” la {sm.Doc.IssuedUtc})"
                    : "Registrul de medii semnat: niciunul în vigoare (" + sm.Problem + ")") +
                  ". Treceți versiunea și amprenta pe procesul-verbal; o stație care nu are ultima versiune se vede doar comparând procesele-verbale ale stațiilor.");
        l.Add(OfficialLabel + ". Ofițerul de securitate semnează fișierul cu certificatul calificat de pe cardul său („Semnează ca ofițer de securitate” în aplicație, sau în Adobe); semnătura se verifică cu „Verifică raport semnat”, comparând amprenta certificatului cu cea de pe procesul-verbal.");
        if (r.RegisteredOfficers.Count == 0) l.Add("Ofițeri de securitate declarați pe stație: niciunul.");
        foreach (var o in r.RegisteredOfficers)
            l.Add($"Ofițer de securitate declarat pe stație (neconfirmat): {o.Name}, amprenta certificatului {o.Fingerprint} ({o.Basis}). Se confirmă doar prin amprenta de pe procesul-verbal semnat.");
        return l;
    }

    /// <summary>The classified section for the JSON report (the origins are runtime objects and are left out).</summary>
    private static object? ClassifiedJson(ControlReport r) => r.Classified is not { } c ? null : new
    {
        c.Unjustified, c.ToVerify, c.Approved, c.Deleted, c.AuditRecords, c.Notes,
        SecurityChanges = c.SecurityChanges.Select(s => new { s.TimeUtc, s.Who, s.Process, s.Object, s.AuditRules, s.Evidence }),
        Files = c.Ordered.Select(f => new
        {
            f.Path, f.StatusText,
            Exits = f.Exits.Select(x => new { Channel = x.ChannelText, Verdict = x.VerdictText, x.TimeUtc, x.Who, x.Target, x.Detail, x.Chain, x.RegisterEntry, x.Weak }),
            Accesses = f.Accesses.Select(a => new { a.TimeUtc, a.Who, a.Process, a.Access, a.Weak, a.Evidence }),
        }),
    };

    /// <summary>The user-activity part for the JSON report: the summary and the rows that are in classified folders or on removable volumes (null = not evaluated by this control).</summary>
    private static object? UserActivityJson(ControlReport r) => r.UserActivity is not { } u ? null : new
    {
        u.Summary,
        Rows = u.Items.Where(i => i.IsRelevant).Select(i => new { i.Source, i.Path, i.TimeUtc, i.TimeMeaning, i.Who, i.Relevance, i.RContent }),
    };

    public const string ClassifiedSectionTitle = "3b. Informații clasificate — acces și ieșiri";

    public const string DeviceSectionTitle = "3c. Dispozitive văzute de monitorul în timp real (verificarea D05)";

    /// <summary>
    /// The lines of the device section: a summary, the alerts raised in the period, every device seen (those that break the rule first, in red) with class,
    /// state (connected, disconnected, not working, disabled) and the registration number or „fără număr de înregistrare”. Pure, so the tests read it without
    /// parsing the PDF.
    /// </summary>
    public static List<(string Text, bool Head, string Color)> DeviceSectionLines(ControlReport r, int maxDevices = 1000)
    {
        const string Red = "#b91c1c", Amber = "#b45309";
        var l = new List<(string, bool, string)>();
        var f = r.Facts;
        if (f.DeviceMonitorProblem.Length > 0) l.Add((f.DeviceMonitorProblem, false, Amber));
        if (f.DeviceMonitor is not { } journal)
        {
            l.Add(("Jurnalul monitorului de dispozitive lipsește: monitorul nu a rulat pe această stație, deci dispozitivele nu pot fi listate (vezi istoricul Windows, D01).", false, Amber));
            return l;
        }
        bool isolated = f.Posture != LogAnalyzer.Core.Services.Edition.StationPosture.Internet;
        var from = f.PeriodStartUtc;
        var to = f.PeriodEndUtc == default ? DateTimeOffset.MaxValue : f.PeriodEndUtc;
        var s = LogAnalyzer.Dfir.Devices.DeviceMonitorReport.Build(journal, from, to, isolated);
        l.Add(($"Medii de stocare și dispozitive conectate extern, văzute în perioadă: {s.Devices.Count} ({s.Devices.Count(d => d.Present)} conectate acum) · încalcă regula: {s.Offending.Count} · alerte: {s.Alerts.Count} ({s.Critical} critice). " +
               "Se listează doar mediile de stocare și dispozitivele conectate extern (aceeași listă ca în pagina monitorului și în verificarea D05); componentele interne nu se numără. " +
               "Monitorul vede doar cât timp aplicația este pornită; orele sunt cele la care a observat schimbarea.", false, Ink));
        if (f.DeviceJournalUntrusted.Length > 0) l.Add((f.DeviceJournalUntrusted, false, Amber));
        if (f.DeviceAudit is { ChainValid: true } audit && f.PeriodEndUtc != default)
        {
            var cov = LogAnalyzer.Dfir.Devices.DeviceMonitorCoverage.Analyze(audit.Runs, audit.Gaps, from, to);
            l.Add((!cov.AnyCoverage ? "Acoperire: nicio rulare sigilată a monitorului nu acoperă perioada."
                : cov.Gaps.Count == 0 ? "Acoperire: monitorul a rulat pe toată perioada (din înregistrările sigilate)."
                : "Acoperire incompletă: " + string.Join("; ", cov.Gaps.Take(8).Select(g => $"{LogAnalyzer.Dfir.Devices.DeviceMonitorReport.UtcText(g.FromUtc)} – {LogAnalyzer.Dfir.Devices.DeviceMonitorReport.UtcText(g.ToUtc)}")) + ".", false, cov.Complete ? Ink : Amber));
        }
        foreach (var x in journal.Radios.Where(x => x.BlindNow || x.LastBlindUtc >= from && x.FirstBlindUtc <= to)) l.Add((x.Text, false, Amber));
        if (f.DeviceAudit is { ChainValid: true } reclassAudit)
            foreach (var line in LogAnalyzer.Dfir.Devices.DeviceMonitorAuditCheck.Reclassifications(journal, reclassAudit)) l.Add((line, false, Muted));
        l.Add(("Alertele date în perioadă", true, Ink));
        if (s.Alerts.Count == 0) l.Add(("Nicio alertă în perioadă.", false, Muted));
        foreach (var a in s.Alerts) l.Add((LogAnalyzer.Dfir.Devices.DeviceMonitorReport.AlertLine(a), false, a.Severity == LogAnalyzer.Dfir.Devices.DeviceAlertSeverity.Critical ? Red : Muted));
        l.Add(("Dispozitivele (stare, tip, nume, producător/model, VID/PID, serie, capacitate/literă, utilizator la conectare, prima apariție, ultima conectare/deconectare, nr. de înregistrare, registru)", true, Ink));
        var offending = new HashSet<LogAnalyzer.Dfir.Devices.DeviceJournalEntry>(s.Offending);
        foreach (var d in s.Offending.Concat(s.Devices.Where(d => !offending.Contains(d))).Take(maxDevices))
            l.Add(((offending.Contains(d) ? "ÎNCALCĂ REGULA: " : "") + LogAnalyzer.Dfir.Devices.DeviceMonitorReport.Line(d, f.DeviceAudit, journal.PendingSeals), false, offending.Contains(d) ? Red : Ink));
        if (s.Devices.Count > maxDevices) l.Add(($"… încă {s.Devices.Count - maxDevices} dispozitive (în jurnalul monitorului).", false, Muted));
        return l;
    }

    /// <summary>
    /// The lines of the classified-information section: per file (unjustified exits first), the exits with their verdict and register entry, then the
    /// accesses; without classified folders, why nothing can be said. Pure, so the tests read it without parsing the PDF.
    /// </summary>
    public static List<(string Text, bool Head, string Color)> ClassifiedSectionLines(ControlReport r, int maxAccessesPerFile = 25)
    {
        var l = new List<(string, bool, string)>();
        if (r.Classified is not { } c)
        {
            l.Add(("Profilul de proceduri nu definește foldere clasificate (sau nu a fost transmis controlului): accesul la documentele clasificate și ieșirile lor nu au putut fi urmărite.", false, "#b45309"));
            AddUserActivity(r, l);
            return l;
        }
        l.Add(($"Ieșiri nejustificate: {c.Unjustified} · de verificat: {c.ToVerify} · aprobate (în registrul transferurilor): {c.Approved} · fișiere șterse: {c.Deleted} · " +
               $"schimbări de permisiuni sau de reguli de audit: {c.SecurityChanges.Count} · înregistrări de audit citite: {c.AuditRecords}.", false, Ink));
        foreach (var n in c.Notes) l.Add((n, false, "#b45309"));
        foreach (var s in c.SecurityChanges)
            l.Add(($"{(s.AuditRules ? "REGULI DE AUDIT SCHIMBATE" : "PERMISIUNI SCHIMBATE")}: {s.Object} — {s.Who}, {s.Process}, {(s.TimeUtc is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" : "ora necunoscută")} [{s.Evidence}]", false, s.AuditRules ? "#b91c1c" : "#b45309"));
        if (c.Files.Count == 0) l.Add(("Niciun acces și nicio ieșire a unui fișier clasificat în sursele citite.", false, Muted));
        if (c.SelfActivity.Count > 0)
        {
            l.Add(($"Înregistrări lăsate de aplicație la calculul amprentelor ({ClassifiedAccess.SelfLabel}): {c.SelfActivity.Count}; nu sunt accese ale cuiva.", false, Muted));
            foreach (var a in c.SelfActivity.Take(10)) l.Add(($"  {(a.TimeUtc is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" : "ora necunoscută")} · {a.Who} · {a.Access} [{a.Evidence}]", false, Muted));
        }
        foreach (var f in c.Ordered)
        {
            l.Add(($"{f.StatusText} · {f.Path}", true, f.Rank == 0 ? "#b91c1c" : f.Rank == 1 ? "#b45309" : Ink));
            foreach (var x in f.Exits.OrderBy(x => x.Verdict))
            {
                l.Add(($"  Ieșire {x.VerdictText}: {x.ChannelText} → {x.Target}, {(x.TimeUtc is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" : "ora necunoscută")}, {x.Who}. {x.Detail}" + (x.Weak ? " (dovadă mai slabă)" : ""),
                       false, x.Verdict == ExitVerdict.Unjustified ? "#b91c1c" : x.Verdict == ExitVerdict.ToVerify ? "#b45309" : Ink));
                foreach (var step in x.Chain) l.Add(("      – " + step, false, Muted));
            }
            foreach (var a in f.Accesses.OrderBy(a => a.TimeUtc).Take(maxAccessesPerFile))
                l.Add(($"  Acces {(a.TimeUtc is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" : "ora necunoscută")} · {a.Who} · {a.Process} · {a.Access}{(a.Weak ? " (dovadă mai slabă)" : "")} [{a.Evidence}]", false, Muted));
            if (f.Accesses.Count > maxAccessesPerFile) l.Add(($"  … încă {f.Accesses.Count - maxAccessesPerFile} accese (în fișierul JSON al raportului)", false, Muted));
        }
        AddUserActivity(r, l);
        return l;
    }

    /// <summary>E7: the user-activity rows of section 3b (counts, strongest traces with the meaning of their time, limits), or the statement that this control did not evaluate them.</summary>
    private static void AddUserActivity(ControlReport r, List<(string Text, bool Head, string Color)> l)
    {
        foreach (var (text, head) in LogAnalyzer.Dfir.Analysis.UserActivityAnalysis.ReportLines(r.UserActivity))
            l.Add((text, head, head ? Ink : Muted));
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
        File.WriteAllText(json, JsonSerializer.Serialize(new { r.Facts, r.Checks, r.Users, r.Actions, Classified = ClassifiedJson(r), UserActivity = UserActivityJson(r), ManualChecks = manual ?? [] },
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
