using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Verification;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Final investigation report: a first-page summary box, incident chains first (the story), then findings by severity with their evidence,
/// collection and parsing status, evidence gaps and custody pointers. Every statement points to evidence. Text is Romanian; the English
/// spec codes are explained in a legend at the end. Times: station local time with UTC in brackets, once per event.
/// </summary>
public static class InvestigationReportPdf
{
    // WP18 S5: "Pentru cine este raportul?" chooses sections, never facts.
    private const string Ink = "#0f172a", Muted = "#64748b", Line = "#cbd5e1";

    /// <summary>Tests: receives every text block the report prints, joined by new lines, after the PDF is produced (async-local, so parallel tests do not mix).</summary>
    public static readonly AsyncLocal<Action<string>?> TextObserver = new();

    public static void Write(InvestigationResult r, string path, string investigator, string notes = "", LogAnalyzer.Dfir.Reporting.ReportAudience audience = LogAnalyzer.Dfir.Reporting.ReportAudience.Everything)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var sec = LogAnalyzer.Dfir.Reporting.ReportAudiences.Sections(audience);
        bool fullFindings = LogAnalyzer.Dfir.Reporting.ReportAudiences.FindingsInFull(audience);
        bool S(LogAnalyzer.Dfir.Reporting.ReportSection x) => sec.Contains(x);
        var info = r.Case.Info;
        var zone = TimeZoneInfo.Local;
        string L(DateTimeOffset? t) => t is { } v ? RoText.LocalWithUtc(v, zone) : "—";
        var chains = r.Findings.Where(f => f.RuleId == "INCIDENT-CHAIN").ToList();
        var others = r.Findings.Where(f => f.RuleId != "INCIDENT-CHAIN").ToList();
        var integrity = ReportIntegrity.Check(r);
        var seal = LogAnalyzer.Dfir.Case.ReportSeal.For(r.Case);   // chain heads at the moment the report is printed (decision 30)
        var evidence = r.Case.LoadEvidence().ToDictionary(e => e.EvidenceId, StringComparer.Ordinal);
        var verification = r.Verification ?? ReadVerification(r.Case.Root);
        var station = LogAnalyzer.Dfir.Analysis.StationIdentity.FromEvidence(r.Timeline, info.Host);   // the evidence's station, not the examiner's machine (review I1)
        var summary = InvestigationReportText.Summarize(r, integrity);
        var printed = new List<string>();
        var numbers = new InvestigationReportText.SectionCounter();

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("Segoe UI").FontColor("#1e293b"));
            page.Header().Column(h =>
            {
                string title = $"Raport de investigație — {info.Name}" + (audience == LogAnalyzer.Dfir.Reporting.ReportAudience.Everything ? "" : $" · {LogAnalyzer.Dfir.Reporting.ReportAudiences.Label(audience)}");
                string sub = $"Caz {info.CaseId} · {station.Line} · investigator {investigator} · orele sunt în {RoText.ZoneName(zone, DateTimeOffset.Now)}, cu ora UTC între paranteze";
                printed.Add(title); printed.Add(sub);
                h.Item().Text(title).Bold().FontSize(14).FontColor(Ink);
                h.Item().Text(sub).FontSize(7.5f).FontColor(Muted);
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor(Ink);
            });
            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(7);
                TextBlockDescriptor P(ColumnDescriptor c, string text) { printed.Add(text); return c.Item().Text(text); }
                // Heading: numbered in print order, whatever sections the audience includes.
                void Head(string title) => P(col, numbers.Next(title)).Bold().FontSize(11).FontColor(Ink);

                // ---- Page 1: "Concluzie pe scurt" before everything else ----
                col.Item().Background("#f8fafc").Border(1.2f).BorderColor(Ink).Padding(8).Column(sc =>
                {
                    sc.Spacing(3);
                    P(sc, "Concluzie pe scurt").Bold().FontSize(12).FontColor(Ink);
                    P(sc, summary.Verdict).Bold().FontSize(9);
                    if (summary.Serious.Count > 0)
                    {
                        P(sc, "Ce este grav").Bold().FontSize(8.5f);
                        foreach (var s in summary.Serious) P(sc, "• " + s).FontSize(8);
                    }
                    P(sc, "Ce nu s-a putut verifica (surse lipsă sau incomplete)").Bold().FontSize(8.5f);
                    foreach (var s in summary.NotChecked) P(sc, "• " + s).FontSize(8);
                    P(sc, "Ce înseamnă pentru dumneavoastră").Bold().FontSize(8.5f);
                    P(sc, summary.Meaning).FontSize(8);
                    P(sc, "Pași următori").Bold().FontSize(8.5f);
                    foreach (var s in summary.NextSteps) P(sc, "• " + s).FontSize(8);
                    P(sc, "Ce poate și ce nu poate dovedi acest raport").Bold().FontSize(8.5f);
                    P(sc, summary.CanProve).FontSize(8);
                    P(sc, summary.CannotProve).FontSize(8);
                });

                // Red: a source changed or vanished. Amber: every hash matches, but a source was empty or of an unexpected format (not interpreted).
                var (ibg, iborder, ifg) = !integrity.AllIntact ? ("#fef2f2", "#b91c1c", "#7f1d1d")
                                        : integrity.HasNotices ? ("#fffbeb", "#d97706", "#78350f")
                                        : ("#ecfdf5", "#10b981", "#065f46");
                var banner = RoText.FixSentences(integrity.Banner);
                printed.Add(banner);
                col.Item().Background(ibg).Border(1).BorderColor(iborder).Padding(6).Text(banner).FontSize(8).Bold().FontColor(ifg);
                // WP4: one verification line (automatic cross-check by a separate module of this application, not an external verification) and a warning when needed.
                col.Item().Border(0.8f).BorderColor(Line).Padding(5).Column(vc =>
                {
                    P(vc, verification is null ? "Verificare: nerulată pentru acest caz (Analysis/verification.json lipsește)." : verification.BannerRomanian + " (verificare automată, nu externă)")
                        .FontSize(8).Bold().FontColor(Ink);
                    if (verification?.Warning is { } warn)
                    {
                        printed.Add(warn);
                        vc.Item().PaddingTop(3).Background("#fef2f2").Border(1).BorderColor("#b91c1c").Padding(4).Text(warn).FontSize(8).Bold().FontColor("#7f1d1d");
                    }
                });
                col.Item().Row(row =>
                {
                    void Box(RowDescriptor rw, string value, string label, string sub, string color) { printed.Add($"{value} {label} {sub}".Trim()); InvestigationReportPdf.Box(rw, value, label, sub, color); }
                    Box(row, r.Findings.Count(f => f.Severity == Severity.Critical).ToString(), "CRITICE", $"{RoText.Count(summary.CriticalConfirmed, "confirmată", "confirmate")} · {RoText.Count(summary.CriticalIndicative, "indicativă", "indicative")}", "#7f1d1d");
                    Box(row, r.Findings.Count(f => f.Severity == Severity.High).ToString(), "RIDICATE", $"{RoText.Count(summary.HighConfirmed, "confirmată", "confirmate")} · {RoText.Count(summary.HighIndicative, "doar indicativă", "doar indicative")}", "#b45309");
                    Box(row, r.Findings.Count(f => f.Severity == Severity.Medium).ToString(), "MEDII", "", "#78716c");
                    Box(row, RoText.Number(r.Timeline.Count), "EVENIMENTE", "", "#334155");
                    Box(row, r.Gaps.Count.ToString(), "SURSE LIPSĂ SAU INCOMPLETE", "", "#475569");
                });
                P(col, "Confirmată = susținută sau coroborată de verificarea automată; indicativă = încă neconfirmată, de verificat.").FontSize(7).FontColor(Muted);
                P(col, r.ProcedureProfileLine).FontSize(7.5f).FontColor(Muted);
                if (notes.Length > 0) P(col, "Observații: " + notes);

                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.IncidentChains))
                {
                Head("Ce s-a întâmplat (lanțuri de incident)");
                if (chains.Count == 0) P(col, "Nu s-au găsit constatări grave grupate în timp.").FontColor(Muted);
                foreach (var c in chains)
                    col.Item().Border(0.8f).BorderColor("#7f1d1d").Padding(6).Column(cc =>
                    {
                        P(cc, $"{RoText.StripVolumes(c.Title)}  [{RoLabels.Severity(c.Severity)} · {RoLabels.Classification(c.Classification)}]").Bold();
                        foreach (var step in c.Description.Split(" → ")) P(cc, "→ " + RoText.StripVolumes(step)).FontSize(8);
                        P(cc, "Atenție: " + string.Join(" ", c.AlternativeExplanations)).FontSize(7).Italic().FontColor(Muted);
                    });

                }
                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.Gaps))
                {
                Head("Surse lipsă sau incomplete (înaintea constatărilor: limitează ce se poate afirma)");
                if (r.Gaps.Count == 0) P(col, "Nicio sursă lipsă raportată.").FontColor(Muted);
                foreach (var g in r.Gaps)
                    P(col, RoText.FixSentences($"{RoText.CleanPathsInText(g.Artifact)} — {RoLabels.Source(g.Status)}: {Sentence(RoText.CleanPathsInText(g.Reason))} Impact: {Sentence(g.Impact)} Sursă alternativă: {Sentence(g.AlternativeSource)}")).FontSize(7.5f);

                }
                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.Findings))
                {
                Head("Constatări");
                var groups = InvestigationReportText.GroupFindings(others);
                if (!fullFindings)
                    foreach (var g in groups)
                        col.Item().Column(sc =>
                        {
                            var f = g.Lead;
                            P(sc, $"{f.FindingId} · {RoText.StripVolumes(f.Title)}{(g.Members.Count > 1 ? $" (și {RoText.MoreOfSameKind(g.Members.Count - 1)})" : "")} — {RoLabels.Severity(f.Severity)} · {StateLabels.Romanian(f.Status)}").FontSize(8);
                            foreach (var m in f.MissingEvidence) P(sc, "Lipsește: " + m).FontSize(7).Italic();
                            foreach (var c in f.ContradictingEvidence) P(sc, "Contrazice: " + c).FontSize(7).Italic().FontColor("#92400e");
                            foreach (var l in InvestigationReportText.SpecificLimitations(f, 2).Specific.Take(3)) P(sc, "Limită: " + l).FontSize(7).Italic().FontColor(Muted);
                        });
                else
                foreach (var g in groups)
                    col.Item().Border(0.5f).BorderColor(Line).Padding(5).Column(cc =>
                    {
                        var f = g.Lead;
                        P(cc, $"{f.FindingId} · {RoText.StripVolumes(f.Title)}{(g.Members.Count > 1 ? $" (și {RoText.MoreOfSameKind(g.Members.Count - 1)})" : "")}").Bold();
                        P(cc, $"{RoLabels.Severity(f.Severity)} · {RoLabels.Classification(f.Classification)} · {RoLabels.Confidence(f.Confidence)} · {RoLabels.Category(f.Category)}{(f.MitreTechniqueId.Length > 0 ? " · MITRE " + f.MitreTechniqueId : "")} · {L(f.FirstSeenUtc)} – {L(f.LastSeenUtc)}")
                            .FontSize(7.5f).FontColor(Muted);
                        P(cc, $"Stare: {StateLabels.Romanian(f.Status)} · tip: {RoLabels.Semantic(f.SemanticType)} · verificare automată: {StateLabels.Romanian(f.Verification.State)}")
                            .FontSize(7.5f).FontColor(Muted);
                        if (f.Verification.State != StandardState.NotAssessed && f.Verification.Reason.Length > 0) P(cc, "Verificare: " + f.Verification.Reason).FontSize(7).FontColor(Muted);
                        P(cc, RoText.StripVolumes(f.Description));
                        if (f.ClassificationReason.Length > 0) P(cc, "De ce: " + RoText.StripVolumes(f.ClassificationReason)).FontSize(7.5f);
                        if (g.Members.Count > 1)
                        {
                            P(cc, $"Constatări de același fel ({g.Members.Count}):").FontSize(7.5f).Bold();
                            foreach (var m in g.Members.Take(15)) P(cc, $"{m.FindingId} · {RoText.StripVolumes(m.Title)}").FontSize(7);
                            if (g.Members.Count > 15) P(cc, $"… încă {g.Members.Count - 15} (lista completă în Analysis/findings.json)").FontSize(7).Italic().FontColor(Muted);
                        }
                        var refs = InvestigationReportText.GroupEvidence(g);
                        foreach (var e in refs.Take(8)) P(cc, $"Element de probă {e.EvidenceId} · {RoText.CleanPathsInText(e.Locator)} · {RoText.CleanPathsInText(e.Description)}").FontSize(7).FontColor(Muted);
                        if (refs.Count > 8) P(cc, $"… încă {RoText.Count(refs.Count - 8, "referință", "referințe")} la elemente de probă (în Analysis/findings.json)").FontSize(7).Italic().FontColor(Muted);
                        foreach (var a in f.AlternativeExplanations) P(cc, "Explicație nevinovată posibilă: " + a).FontSize(7).Italic();
                        foreach (var m in f.MissingEvidence) P(cc, "Lipsește: " + m).FontSize(7).Italic();
                        foreach (var c in f.ContradictingEvidence) P(cc, "Contrazice: " + c).FontSize(7).Italic().FontColor("#92400e");
                        var (specific, omitted) = InvestigationReportText.SpecificLimitations(f);
                        foreach (var l in specific) P(cc, "Limită: " + l).FontSize(7).Italic().FontColor(Muted);
                        if (omitted > 0) P(cc, $"Alte {RoText.Count(omitted, "limită", "limite")} ale citirii surselor: Analysis/parsers.json.").FontSize(7).Italic().FontColor(Muted);
                    });

                var general = InvestigationReportText.GenericLimitations(others);
                if (general.Count > 0)
                {
                    Head("Limitări generale (valabile pentru toate constatările)");
                    foreach (var l in general) P(col, "• " + l).FontSize(7.5f);
                    P(col, "Limitările specifice unei constatări sunt trecute la constatarea respectivă.").FontSize(7).Italic().FontColor(Muted);
                }

                }
                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.EvidenceCollected))
                {
                Head("Elemente de probă colectate și citite");
                foreach (var c in r.Collection)
                    P(col, $"{c.Collector}: {RoLabels.Source(c.Status)}, {RoText.Count(c.EvidenceCount, "element de probă", "elemente de probă")} ({L(c.StartUtc)} – {L(c.EndUtc)}) {InvestigationReportText.GroupErrors(c.Errors)}").FontSize(7.5f);
                foreach (var g in r.Parsing.Where(p => p.Status != EvidenceStatus.SkippedByDesign).GroupBy(p => (p.Parser, p.ParserVersion, p.ParserStatus)))
                    P(col, $"{g.Key.Parser} {g.Key.ParserVersion} ({(g.Key.ParserStatus.Length > 0 ? InvestigationReportText.ParserStatusRomanian(g.Key.ParserStatus) : "—")}): {RoText.Count(g.Count(), "fișier", "fișiere")}, {RoText.Count(g.Sum(p => p.Records), "înregistrare", "înregistrări")}, stări: {string.Join(", ", g.Select(p => RoLabels.Source(p.Status)).Distinct())}").FontSize(7.5f);
                foreach (var s in r.Parsing.Where(p => p.Status == EvidenceStatus.SkippedByDesign))
                    P(col, $"{s.EvidenceId} necitit (omis intenționat): {s.Error}").FontSize(7.5f).FontColor(Muted);
                P(col, "Starea unui parser: „validat pe date reale” = test de regresie pe un corpus real; „testat doar pe date sintetice” = doar teste pe date create pentru test; descrierea completă a fiecărui parser: Analysis/parsers.json.").FontSize(7).Italic().FontColor(Muted);

                }
                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.Detections))
                {
                Head("Detecții (reguli IOC / Sigma / YARA)");
                if (r.Detections.Count == 0) P(col, InvestigationReportText.DetectionsNoneText(r.RulesUsed.Count)).FontColor(Muted);
                foreach (var g in r.Detections.GroupBy(d => (d.RuleId, d.RuleVersion, d.RuleSha256, d.Title)).OrderByDescending(g => g.Count()).Take(25))
                    col.Item().Column(cc =>
                    {
                        P(cc, $"{g.Key.Title} — {RoText.Count(g.Count(), "potrivire", "potriviri")}").Bold().FontSize(8);
                        P(cc, $"Regula {g.Key.RuleId} versiunea {g.Key.RuleVersion}, SHA-256 {Short(g.Key.RuleSha256)}").FontSize(7).FontColor(Muted);
                        foreach (var d in g.Take(3)) P(cc, $"Element de probă {d.EvidenceId} · {RoText.CleanPathsInText(d.Locator)} · {d.Match}").FontSize(7).FontColor(Muted);
                    });
                P(col, "O potrivire arată că elementul de probă îndeplinește criteriile regulii; nu dovedește singură intenția sau rezultatul unei activități.").FontSize(7).Italic().FontColor(Muted);

                }
                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.AntiForensics))
                {
                Head("Ascunderea sau ștergerea urmelor (anti-forensics)");
                foreach (var a in r.AntiForensics.OrderBy(a => a.Result).ThenBy(a => a.Id))
                    P(col, RoText.FixSentences($"{a.Id} {a.Technique}{(a.Attack.Length > 0 ? $" ({a.Attack})" : "")} — {a.ResultRomanian}: {RoText.CleanPathsInText(a.Reason)}")).FontSize(7.5f)
                       .FontColor(a.Result == AntiForensicResult.Detected ? "#b91c1c" : a.Result == AntiForensicResult.Undetermined ? "#92400e" : "#1e293b");
                P(col, "DETECTAT = urmă observată în elementele de probă (nu dovedește singură intenția); NEDETECTAT = sursa relevantă a fost analizată și nu arată urma; NEDETERMINAT = sursa lipsește sau nu a fost citită. Nicio verificare nu înseamnă „curat”.").FontSize(7).Italic().FontColor(Muted);

                }
                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.PolicyTimeline))
                {
                Head("Cronologie politici");
                P(col, r.PolicyTimelineLine).FontSize(7.5f);
                if (r.PolicyTimeline is { } policyTimeline)
                    foreach (var pl in PolicyTimelineReport.Lines(policyTimeline))
                        P(col, pl.Text).FontSize(pl.Style == PolicyLineStyle.Heading ? 8.5f : 7.5f).FontColor(pl.Style switch
                        {
                            PolicyLineStyle.Bad => "#b91c1c", PolicyLineStyle.Warn => "#92400e", PolicyLineStyle.Muted => Muted, _ => "#1e293b",
                        });
                P(col, "Comportament: observat / neobservat / neevaluabil, cu motivul; niciodată dedus dincolo de probe. Un nivel nedeterminat nu înseamnă conform. Detalii: Analysis/policy_timeline.json.")
                    .FontSize(7).Italic().FontColor(Muted);

                }
                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.Integrity))
                {
                Head("Integritatea elementelor de probă (reverificată acum)");
                foreach (var i in integrity.Items)
                {
                    var e = evidence[i.EvidenceId];
                    P(col, $"{i.EvidenceId} {InvestigationReportText.SpecStatusRomanian(i.SpecStatus)} · {RoText.ReadablePath(e.StoredPath)} · {RoText.Count(e.Size, "octet", "octeți")} · {InvestigationReportText.AcquisitionRomanian(e.AcquisitionMethod)} {L(e.AcquiredAtUtc)}" +
                                    (i.Detail.Length > 0 ? $" · {RoText.CleanPathsInText(i.Detail)}" : "") + " · amprenta completă în anexă").FontSize(6.5f).FontColor(i.CanParse ? "#1e293b" : "#b91c1c");
                }
                if (r.RejectedFindings.Count > 0)
                    P(col, $"{RoText.Count(r.RejectedFindings.Count, "constatare", "constatări")} {(r.RejectedFindings.Count == 1 ? "a fost respinsă" : "au fost respinse")} pentru că nu trimit la elemente de probă din caz (detalii în findings.json).").FontSize(7.5f).Italic();

                }
                if (S(LogAnalyzer.Dfir.Reporting.ReportSection.LegalNotes))
                {
                Head("Note juridice");
                if (audience == LogAnalyzer.Dfir.Reporting.ReportAudience.Legal)
                    P(col, "Ce poate și ce nu poate dovedi acest raport: " + summary.CanProve + " " + summary.CannotProve).FontSize(7.5f);
                else
                    P(col, "Ce poate și ce nu poate dovedi acest raport: vezi caseta „Concluzie pe scurt” de pe prima pagină.").FontSize(7.5f);
                P(col, "Stările constatărilor folosesc cuvinte cu sens precis: „coroborată” înseamnă susținută de cel puțin două surse independente; „susținută de o singură sursă” nu este coroborată; „asociată” înseamnă doar legătură în timp sau în cale; „indiciu – de confirmat” cere confirmarea unui analist. Nicio stare nu stabilește intenția sau vinovăția.").FontSize(7.5f);
                P(col, "Lanțul de custodie și jurnalul de audit au amprente în subsolul fiecărei pagini; amprenta fiecărui element de probă este în anexă. Orice modificare ulterioară a unui element de probă schimbă amprenta și este semnalată la reverificare.").FontSize(7.5f);

                }
                Head("Legendă coduri");
                P(col, "Corpul raportului folosește termeni în română; codurile în engleză din datele cazului (findings.json și celelalte fișiere din Analysis) se potrivesc astfel:").FontSize(7.5f);
                foreach (var l in InvestigationReportText.Legend()) P(col, l).FontSize(7).FontColor(Muted);

                Head("Anexă: elemente de probă și amprente SHA-256");
                foreach (var e in evidence.Values.OrderBy(e => e.EvidenceId, StringComparer.Ordinal))
                    P(col, $"{e.EvidenceId} · {RoText.ReadablePath(e.StoredPath)} · {RoText.Count(e.Size, "octet", "octeți")} · SHA-256 {e.Sha256} · {InvestigationReportText.AcquisitionRomanian(e.AcquisitionMethod)} {L(e.AcquiredAtUtc)}").FontSize(6.5f);
                if (evidence.Count == 0) P(col, "Niciun element de probă în caz.").FontColor(Muted);

                P(col, $"Cronologia completă: Analysis/timeline.csv · constatări: Analysis/findings.json · lanțul de custodie: Logs/chain_of_custody.csv (în {r.Case.Root}). " +
                       "„Indisponibil” înseamnă că sursa nu a putut fi citită, nu că activitatea nu a avut loc. O constatare marcată „indiciu – de confirmat” cere confirmarea analistului.")
                    .FontSize(7).Italic().FontColor(Muted);
            });
            LogAnalyzer.Dfir.Windows.Audit.ReportFooter.Compose(page.Footer(), "investigation", $"LogAnalyzer {DfirInfo.ApplicationVersion} · {info.CaseId}", seal);
        })).GeneratePdf(path);
        TextObserver.Value?.Invoke(string.Join("\n", printed));
    }

    /// <summary>One sentence ending in exactly one full stop (a reason that already ends with one is not given a second).</summary>
    private static string Sentence(string s) => s.Length == 0 ? "—." : s.TrimEnd().TrimEnd('.') + ".";

    /// <summary>The case's Analysis/verification.json, or null when absent or unreadable (a report must still be produced for an old case).</summary>
    internal static VerificationReport? ReadVerification(string caseRoot)
    {
        try { return VerificationReport.Read(Path.Combine(caseRoot, "Analysis", VerificationReport.FileName)); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static string Short(string sha) => sha.Length >= 16 ? sha[..16] + "…" : sha.Length > 0 ? sha : "—";

    private static void Box(RowDescriptor row, string value, string label, string sub, string color) =>
        row.RelativeItem().Padding(2).Background(color).Padding(6).Column(c =>
        {
            c.Item().AlignCenter().Text(value).Bold().FontSize(13).FontColor(Colors.White);
            c.Item().AlignCenter().Text(label).Bold().FontSize(7).FontColor(Colors.White);
            if (sub.Length > 0) c.Item().AlignCenter().Text(sub).FontSize(6).FontColor(Colors.White);
        });
}
