using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Review 2026-10-12 (usefulness, grammar, terminology): the Romanian text helpers and the investigation report built on them.</summary>
public sealed class ReportUsefulnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ladfir_useful_{Guid.NewGuid():N}");
    public ReportUsefulnessTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        InvestigationReportPdf.TextObserver.Value = null;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    // ---- Count helper and numbers ----

    [Theory]
    [InlineData(0, "0 evenimente")]
    [InlineData(1, "1 eveniment")]
    [InlineData(2, "2 evenimente")]
    [InlineData(19, "19 evenimente")]
    [InlineData(20, "20 de evenimente")]
    [InlineData(21, "21 de evenimente")]
    [InlineData(74, "74 de evenimente")]
    [InlineData(100, "100 de evenimente")]
    [InlineData(101, "101 evenimente")]
    [InlineData(119, "119 evenimente")]
    [InlineData(120, "120 de evenimente")]
    [InlineData(3078, "3.078 de evenimente")]
    public void Count_uses_de_after_twenty_and_the_singular_for_one(long n, string expected) =>
        Assert.Equal(expected, RoText.Count(n, "eveniment", "evenimente"));

    [Fact]
    public void Count_covers_the_nouns_the_review_lists_and_numbers_use_the_romanian_grouping()
    {
        Assert.Equal("1 observație", RoText.Count(1, "observație", "observații"));
        Assert.Equal("35 de anomalii", RoText.Count(35, "anomalie", "anomalii"));
        Assert.Equal("132 de programe", RoText.Count(132, "program", "programe"));
        Assert.Equal("70 de ori", RoText.Times(70));
        Assert.Equal("o dată", RoText.Times(1));
        Assert.Equal("3 ori", RoText.Times(3));
        Assert.Equal("19.988", RoText.Number(19988));
        Assert.Equal("988", RoText.Number(988));
    }

    // ---- Text hygiene ----

    [Fact]
    public void Sentences_get_a_capital_after_a_full_stop_and_never_a_doubled_stop()
    {
        Assert.Equal("Oprit la 10:00, planificată. Serviciul a repornit.", RoText.FixSentences("Oprit la 10:00, planificată. serviciul a repornit."));
        Assert.Equal("(așteptat: prefetch/prefetch_mam).", RoText.FixSentences("(așteptat: prefetch/prefetch_mam).."));
        Assert.Equal("Versiunea 1.2 este nouă.", RoText.FixSentences("Versiunea 1.2 este nouă."));
        Assert.Equal("a...b", RoText.FixSentences("a...b"));
    }

    [Fact]
    public void Common_windows_error_codes_are_translated_and_others_keep_the_system_message_in_brackets()
    {
        Assert.Equal("cod 87: parametru incorect", RoText.OsError(87, "The parameter is incorrect."));
        Assert.Equal("cod 5: acces refuzat", RoText.OsError(5));
        Assert.Equal("cod 2: fișierul nu a fost găsit", RoText.OsError(2));
        Assert.Equal("cod 32: fișierul este folosit de alt proces", RoText.OsError(32));
        Assert.Equal("cod 1450: mesaj Windows, Insufficient system resources", RoText.OsError(1450, "Insufficient system resources."));
    }

    [Fact]
    public void Volume_paths_become_readable_with_the_volume_named_once()
    {
        Assert.Equal(("Users\\x\\a.exe", "volumul 1a2b3c4d-0000-1111-2222-333344445555"), RoText.CleanPath(@"\VOLUME{1A2B3C4D-0000-1111-2222-333344445555}\Users\x\a.exe"));
        Assert.Equal("Users\\x\\a.exe (volumul 1a2b3c4d-0000-1111-2222-333344445555)", RoText.ReadablePath(@"\VOLUME{1a2b3c4d-0000-1111-2222-333344445555}\Users\x\a.exe"));
        Assert.Equal(("C:\\Windows\\x.exe", ""), RoText.CleanPath(@"\\?\C:\Windows\x.exe"));
        Assert.Equal(("C:\\a.exe", ""), RoText.CleanPath(@"C:\a.exe"));
        var t = RoText.CleanPathsInText(@"Programul \VOLUME{1a2b3c4d-0000-1111-2222-333344445555}\Temp\a.exe a scris în \VOLUME{1a2b3c4d-0000-1111-2222-333344445555}\Temp\b.log.");
        Assert.Equal("Programul Temp\\a.exe a scris în Temp\\b.log. (volumul 1a2b3c4d-0000-1111-2222-333344445555)", t);
        Assert.Equal("fără căi", RoText.CleanPathsInText("fără căi"));
    }

    [Fact]
    public void The_romanian_time_zone_is_named_in_words_and_other_zones_by_their_windows_id()
    {
        var summer = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var gtb = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");
        Assert.Equal("ora României (UTC+03:00)", RoText.ZoneName(gtb, summer));
        Assert.Equal("ora României (UTC+02:00)", RoText.ZoneName(gtb, new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
        var utc = TimeZoneInfo.Utc;
        Assert.Equal($"{utc.Id} (UTC+00:00)", RoText.ZoneName(utc, summer));
        Assert.Equal("2026-07-01 15:00 (UTC 12:00)", RoText.LocalWithUtc(summer, gtb));
        Assert.Equal("2026-07-01 03:00 (UTC 2026-06-30 18:00)", RoText.LocalWithUtc(new DateTimeOffset(2026, 6, 30, 18, 0, 0, TimeSpan.Zero), TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time")));
    }

    // ---- Labels ----

    [Fact]
    public void State_severity_and_result_labels_are_romanian_and_feminine_to_agree_with_constatare()
    {
        Assert.Equal("Coroborată", StateLabels.Romanian(StandardState.Verified));
        Assert.Equal("Susținută de o singură sursă", StateLabels.Romanian(StandardState.Supported));
        Assert.Equal("Contrazisă", StateLabels.Romanian(StandardState.Contradicted));
        Assert.Equal("Nedeterminată", StateLabels.Romanian(StandardState.Unknown));
        Assert.Equal("Indiciu – de confirmat", StateLabels.ForClassification(Classification.Candidate));
        Assert.Equal("2 coroborate", StateLabels.RomanianCount(StandardState.Verified, 2));
        Assert.Equal("1 contrazisă", StateLabels.RomanianCount(StandardState.Contradicted, 1));
        Assert.Equal(["CRITICĂ", "RIDICATĂ", "MEDIE", "SCĂZUTĂ", "INFORMATIVĂ"], new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.Info }.Select(RoLabels.Severity));
        Assert.Equal("grad de încredere: mediu", RoLabels.Confidence(Confidence.Medium));
        Assert.Equal(["DETECTAT", "NEDETECTAT", "NEDETERMINAT"], new[] { AntiForensicResult.Detected, AntiForensicResult.NotDetected, AntiForensicResult.Undetermined }.Select(RoLabels.AntiForensic));
        Assert.Equal("INDISPONIBIL".ToLowerInvariant(), RoLabels.Source(EvidenceStatus.NotAvailable));
    }

    // ---- Repeated errors, limitations, groups ----

    [Fact]
    public void Identical_collector_errors_become_one_line_with_the_list_of_channels()
    {
        var errors = string.Join("; ", Enumerable.Range(1, 23).Select(i => $"Canal{i}: export eșuat (cod 87: parametru incorect)").Append("Security: necesită drepturi de administrator"));
        var g = InvestigationReportText.GroupErrors(errors);
        Assert.Equal(1, Regex.Count(g, "cod 87"));
        Assert.Contains("23 de canale: Canal1, Canal2", g);
        Assert.Contains("Security: necesită drepturi de administrator", g);
        Assert.Equal("A: x", InvestigationReportText.GroupErrors("A: x"));
        Assert.Equal("", InvestigationReportText.GroupErrors(""));
    }

    private static Finding F(string id, string rule, string title, Severity sev = Severity.Medium, string reason = "motiv", params string[] limitations)
    {
        var f = new Finding { FindingId = id, RuleId = rule, Title = title, Severity = sev, Classification = Classification.Candidate, Description = "descriere " + title, ClassificationReason = reason };
        f.Limitations.AddRange(limitations);
        return f;
    }

    [Fact]
    public void Generic_limitations_are_separated_and_a_parser_limit_stays_only_where_that_parser_supplied_the_evidence()
    {
        var semantic = FindingContract.SemanticLimitation(SemanticType.Execution);
        var usb = F("F-1", "MEDIA-X", "USB", limitations: [semantic, "Regula nu vede copierea.", "Verificare independentă: neefectuată.",
            "Instantaneul serviciilor nu poate dovedi că serviciul a pornit", "Amcache nu poate dovedi că fișierul a fost executat."]);
        usb.Provenance = new FindingProvenance("MEDIA-X", "1", "p", "1", ["EV-1"], [], ["UsbDevicesParser", "AmcacheParser"], "1.0");
        var other = F("F-2", "EXEC", "Program", limitations: [semantic, "Alta"]);

        Assert.Equal([semantic, "Verificare independentă: neefectuată."], InvestigationReportText.GenericLimitations([usb, other]));
        var (specific, omitted) = InvestigationReportText.SpecificLimitations(usb);
        Assert.Equal(["Regula nu vede copierea.", "Amcache nu poate dovedi că fișierul a fost executat."], specific);   // the services-snapshot limit is not relevant to this finding
        Assert.Equal(1, omitted);
        Assert.DoesNotContain(semantic, specific);
    }

    [Fact]
    public void Parser_limits_are_capped_per_finding_and_the_rest_is_counted_not_hidden()
    {
        var f = F("F-1", "R", "t");
        f.Limitations.AddRange(Enumerable.Range(1, 6).Select(i => $"Amcache nu poate dovedi {i}"));
        f.Provenance = new FindingProvenance("R", "1", "p", "1", [], [], ["AmcacheParser"], "1.0");
        var (specific, omitted) = InvestigationReportText.SpecificLimitations(f, 3);
        Assert.Equal(3, specific.Count);
        Assert.Equal(3, omitted);
    }

    [Fact]
    public void Findings_of_the_same_rule_and_reason_are_grouped_but_chains_and_different_reasons_are_not()
    {
        var a = F("F-1", "PERSIST-TASK-CONFIG", "Activitate planificată A", Severity.Medium, "aceeași");
        var b = F("F-2", "PERSIST-TASK-CONFIG", "Activitate planificată B", Severity.Medium, "aceeași");
        var c = F("F-3", "PERSIST-TASK-CONFIG", "Activitate planificată C", Severity.Medium, "alta");
        var ch1 = F("F-4", "INCIDENT-CHAIN", "Lanț 1", Severity.High, "x");
        var ch2 = F("F-5", "INCIDENT-CHAIN", "Lanț 2", Severity.High, "x");
        var groups = InvestigationReportText.GroupFindings([a, b, c, ch1, ch2]);
        Assert.Equal(4, groups.Count);
        var grouped = Assert.Single(groups, g => g.Members.Count > 1);
        Assert.Equal(["F-1", "F-2"], grouped.Members.Select(m => m.FindingId));
        Assert.Equal("F-1", grouped.Lead.FindingId);
        a.SupportingEvidence.Add(new("EV-1", "r=1", "d"));
        b.SupportingEvidence.AddRange([new("EV-1", "r=1", "d"), new("EV-2", "r=2", "d")]);
        Assert.Equal(["EV-1", "EV-2"], InvestigationReportText.GroupEvidence(grouped).Select(e => e.EvidenceId));   // evidence references of every member are kept
    }

    [Fact]
    public void Section_numbers_run_one_two_three_whatever_sections_are_printed()
    {
        var n = new InvestigationReportText.SectionCounter();
        Assert.Equal(["1. A", "2. B", "3. C"], [n.Next("A"), n.Next("B"), n.Next("C")]);
    }

    // ---- The report itself ----

    private InvestigationResult Fixture(string name = "fx", bool withFindings = true)
    {
        var sample = Path.Combine(_dir, name + ".bin");
        File.WriteAllText(sample, "MZ not really a program " + name);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases_" + name), name, TestScopes.Valid());
        InvestigationPipeline.Import(ws, [sample]);
        var r = new InvestigationResult { Case = ws };
        var ev = ws.LoadEvidence().First();
        var T0 = new DateTimeOffset(2026, 10, 11, 9, 30, 0, TimeSpan.Zero);
        if (withFindings)
        {
            var serious = new Finding
            {
                FindingId = "F-0001", RuleId = "PERSIST-TASK", Title = $"Activitate planificată care rulează dintr-o locație în care utilizatorii pot scrie: \\VOLUME{{1a2b3c4d-0000-1111-2222-333344445555}}\\Users\\x\\Temp\\run.cmd", Severity = Severity.High, Category = "Persistence",
                Classification = Classification.Candidate, Confidence = Confidence.Medium, FirstSeenUtc = T0, LastSeenUtc = T0.AddMinutes(5),
                Description = "O activitate planificată pornește run.cmd.", ClassificationReason = "Acțiunea indică o locație în care utilizatorii pot scrie.",
                SupportingEvidence = [new(ev.EvidenceId, "activitatea nr. 1", "definiția activității din \\VOLUME{1a2b3c4d-0000-1111-2222-333344445555}\\Users\\x\\Temp\\run.cmd", ev.Sha256)],
                RecommendedNextSteps = ["Citiți scriptul complet și identificați contul care l-a creat."],
                AlternativeExplanations = ["Administrare legitimă."], MissingEvidence = ["Conținutul scriptului."],
            };
            var vendorA = new Finding { FindingId = "F-0002", RuleId = "PERSIST-TASK-CONFIG", Title = "Activitate planificată Lenovo A", Severity = Severity.Medium, Category = "Persistence", Classification = Classification.Candidate, Description = "A", ClassificationReason = "producător cunoscut", SupportingEvidence = [new(ev.EvidenceId, "activitatea nr. 2", "A", ev.Sha256)] };
            var vendorB = new Finding { FindingId = "F-0003", RuleId = "PERSIST-TASK-CONFIG", Title = "Activitate planificată Lenovo B", Severity = Severity.Medium, Category = "Persistence", Classification = Classification.Candidate, Description = "B", ClassificationReason = "producător cunoscut", SupportingEvidence = [new(ev.EvidenceId, "activitatea nr. 3", "B", ev.Sha256)] };
            r.Findings.AddRange(FindingContract.Enrich([serious, vendorA, vendorB]));
            r.Gaps.Add(new EvidenceGap("Security.evtx", EvidenceStatus.NotAvailable, "canalul nu a putut fi exportat", "nu se poate verifica auditarea", "export manual cu wevtutil", "Recuperabil"));
            r.AntiForensics.Add(new AntiForensicCheck("AF01", "Jurnal șters", "T1070.001", AntiForensicResult.NotDetected, "Nicio ștergere observată.", []));
            r.Collection.Add(new CollectionRow("EventLogCollector", EvidenceStatus.Partial, 3, string.Join("; ", Enumerable.Range(1, 5).Select(i => $"Canal{i}: export eșuat (cod 87: parametru incorect)")), T0, T0.AddMinutes(1)));
        }
        return r;
    }

    private (string Text, string Pdf) Render(InvestigationResult r, ReportAudience audience)
    {
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        var pdf = Path.Combine(_dir, $"r_{audience}.pdf");
        InvestigationReportPdf.Write(r, pdf, "test", audience: audience);
        Assert.True(new FileInfo(pdf).Length > 1000);
        return (text, pdf);
    }

    public static IEnumerable<object[]> Audiences() => Enum.GetValues<ReportAudience>().Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(Audiences))]
    public void Every_audience_gets_the_first_page_box_before_anything_else_and_numbering_without_holes(ReportAudience audience)
    {
        var (text, _) = Render(Fixture(), audience);
        int box = text.IndexOf("Concluzie pe scurt", StringComparison.Ordinal);
        Assert.True(box >= 0);
        foreach (var part in new[] { "Ce înseamnă pentru dumneavoastră", "Pași următori", "Ce poate și ce nu poate dovedi acest raport", "Ce nu s-a putut verifica", "Nu este un raport de constatare tehnico-științifică", "raport de expertiză" })
            Assert.Contains(part, text);
        // the box precedes the integrity banner, the counters and every numbered section
        Assert.True(box < text.IndexOf("Integritatea elementelor de probă verificată", StringComparison.Ordinal));
        var numbered = Regex.Matches(text, @"^(\d+)\. ", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.True(numbered.Count >= 4);
        Assert.Equal(Enumerable.Range(1, numbered.Count), numbered);
        Assert.Contains("Legendă coduri", text);
        Assert.Contains("Anexă: elemente de probă și amprente SHA-256", text);
    }

    [Fact]
    public void The_legal_audience_has_the_legal_notes_and_no_raw_lists_and_everything_has_both()
    {
        var legal = Render(Fixture(), ReportAudience.Legal).Text;
        Assert.Contains("Note juridice", legal);
        Assert.DoesNotContain("Detecții (reguli IOC", legal);
        Assert.DoesNotContain("Elemente de probă colectate și citite", legal);
        Assert.Contains("Integritatea elementelor de probă (reverificată acum)", legal);
        var all = Render(Fixture("fx2"), ReportAudience.Everything).Text;
        Assert.Contains("Note juridice", all);
        Assert.Contains("Detecții (reguli IOC", all);
        Assert.DoesNotContain("Note juridice", Render(Fixture("fx3"), ReportAudience.Management).Text);
    }

    [Fact]
    public void The_report_prints_generic_limits_once_groups_repeats_and_writes_romanian_labels_with_the_codes_only_in_the_legend()
    {
        var r = Fixture();
        var (text, _) = Render(r, ReportAudience.Everything);
        // general limitations once, in their own section; not repeated per finding
        var exec = FindingContract.SemanticLimitation(SemanticType.Configuration);
        Assert.Contains("Limitări generale", text);
        Assert.Equal(1, Regex.Count(text, Regex.Escape("• " + exec)));
        Assert.DoesNotContain("Limită: " + exec, text);
        Assert.DoesNotContain("Limită: Verificare independentă", text);
        // identical collector errors: one line with the channels
        Assert.Equal(1, Regex.Count(text, "cod 87"));
        Assert.Contains("5 canale: Canal1, Canal2", text);
        // two vendor tasks of the same rule and reason: one finding listing both, evidence references kept
        Assert.Contains("(și încă una de același fel)", text);
        Assert.Contains("F-0003 · Activitate planificată Lenovo B", text);
        Assert.Contains("activitatea nr. 3", text);
        // Romanian labels in the body; the English codes only in the legend (the last two sections)
        var body = text[..text.IndexOf("Legendă coduri", StringComparison.Ordinal)];
        foreach (var code in new[] { "HIGH", "CORRELATED", "CANDIDATE", "NOT_AVAILABLE", "UNDETERMINED", "tip: Configuration", "încredere MEDIUM" })
            Assert.DoesNotContain(code, body);
        Assert.Contains("RIDICATĂ", body);
        Assert.Contains("Indiciu – de confirmat", body);
        Assert.Contains("grad de încredere: mediu", body);
        Assert.Contains("Explicație nevinovată posibilă: Administrare legitimă.", body);
        Assert.Contains("Sursă alternativă: export manual cu wevtutil.", body);
        Assert.DoesNotContain("Alternativă:", body.Replace("Sursă alternativă:", ""));
        Assert.Contains("RIDICATĂ = HIGH", text);
        // readable path: no raw volume guid anywhere; the volume is named only on the evidence line, never in a title or a one-line summary
        Assert.DoesNotContain("\\VOLUME{", text);
        Assert.Contains("(volumul 1a2b3c4d-0000-1111-2222-333344445555)", text);
        foreach (var line in text.Split('\n').Where(l => l.StartsWith("F-0001") || l.StartsWith("• F-0001")))
            Assert.DoesNotContain("volumul", line);
        Assert.Contains("Activitate planificată care rulează dintr-o locație în care utilizatorii pot scrie: Users\\x\\Temp\\run.cmd", text);
        // the full SHA-256 of every evidence item once, in the annex
        foreach (var e in r.Case.LoadEvidence())
        {
            Assert.Equal(64, e.Sha256.Length);
            Assert.Equal(1, Regex.Count(text, Regex.Escape("SHA-256 " + e.Sha256)));
        }
    }

    [Fact]
    public void Headline_counters_do_not_mislead_confirmed_versus_indicative_and_no_match_is_not_clean()
    {
        var r = Fixture();
        var s = InvestigationReportText.Summarize(r, ReportIntegrity.Check(r));
        Assert.Equal(1, s.HighIndicative);
        Assert.Equal(0, s.HighConfirmed);
        Assert.Contains("doar indicativă", s.Verdict);
        Assert.Contains("Un indiciu nu stabilește singur o compromitere", s.Verdict);
        Assert.Contains(s.NextSteps, x => x.StartsWith("Citiți scriptul"));
        Assert.Contains("3 reguli aplicate", InvestigationReportText.DetectionsNoneText(3));
        Assert.Contains("nu înseamnă „curat”", InvestigationReportText.DetectionsNoneText(3));
        Assert.Contains("25 de reguli aplicate", InvestigationReportText.DetectionsNoneText(25));
    }

    [Fact]
    public void A_case_without_findings_says_that_absence_is_not_proof_of_a_clean_system()
    {
        var r = Fixture("empty", withFindings: false);
        var s = InvestigationReportText.Summarize(r, ReportIntegrity.Check(r));
        Assert.Contains("nu dovedește că sistemul este curat", s.Verdict);
        Assert.Contains("Nicio acțiune urgentă", string.Join(" ", s.NextSteps));
    }

    [Fact]
    public void The_sample_report_can_be_written_to_a_path_for_a_manual_read()
    {
        var target = Environment.GetEnvironmentVariable("LA_SAMPLE_REPORT_PDF");
        if (string.IsNullOrEmpty(target)) return;   // only when a person asks for the sample (see the report of the change)
        InvestigationReportPdf.Write(Fixture("sample"), target, "investigator de probă");
        Assert.True(File.Exists(target));
    }

    // ---- Agreement, vocabulary and layout fixes (review of the sample PDF) ----

    [Fact]
    public void Numerals_agree_in_gender_with_una_o_niciuna_and_the_same_kind_phrase_counts_correctly()
    {
        Assert.Equal("o constatare", RoText.CountFem(1, "constatare", "constatări"));
        Assert.Equal("3 constatări", RoText.CountFem(3, "constatare", "constatări"));
        Assert.Equal("un fișier", RoText.CountMasc(1, "fișier", "fișiere"));
        Assert.Equal("niciuna confirmată", RoText.Pronoun(0, "confirmată", "confirmate"));
        Assert.Equal("una doar indicativă", RoText.Pronoun(1, "doar indicativă", "doar indicative"));
        Assert.Equal("2 confirmate", RoText.Pronoun(2, "confirmată", "confirmate"));
        Assert.Equal("25 de confirmate", RoText.Pronoun(25, "confirmată", "confirmate"));
        Assert.Equal("niciunul confirmat", RoText.Pronoun(0, "confirmat", "confirmați", feminine: false));
        Assert.Equal("încă una de același fel", RoText.MoreOfSameKind(1));
        Assert.Equal("încă 4 de același fel", RoText.MoreOfSameKind(4));
        Assert.Equal("O constatare", RoText.Capitalize("o constatare"));
        Assert.Equal(@"Users\x\a.exe rulat", RoText.StripVolumes(@"\VOLUME{1a2b3c4d-0000-1111-2222-333344445555}\Users\x\a.exe rulat"));
    }

    private static Finding Grave(string id, Classification cls) => new()
    {
        FindingId = id, RuleId = "PERSIST-TASK-CONFIG", Title = "Activitate planificată " + id, Severity = Severity.High, Category = "Persistence", Classification = cls,
        Description = "d" + id, ClassificationReason = "motiv " + id,
    };

    [Fact]
    public void The_verdict_uses_exista_o_constatare_and_niciuna_una_agreement_for_every_count()
    {
        var one = Fixture("v1");
        var v1 = InvestigationReportText.Summarize(one, ReportIntegrity.Check(one)).Verdict;
        Assert.StartsWith("Există o constatare de severitate ridicată sau critică: niciuna confirmată de verificarea automată și una doar indicativă (de confirmat).", v1);
        Assert.DoesNotContain("Sunt 1", v1);

        var three = Fixture("v3");
        three.Findings.Clear();
        three.Findings.AddRange(FindingContract.Enrich([Grave("F-1", Classification.Candidate), Grave("F-2", Classification.Candidate), Grave("F-3", Classification.Candidate)]));
        var v3 = InvestigationReportText.Summarize(three, ReportIntegrity.Check(three)).Verdict;
        Assert.StartsWith("Există 3 constatări de severitate ridicată sau critică: niciuna confirmată de verificarea automată și 3 doar indicative (de confirmat).", v3);
    }

    [Fact]
    public void Headline_boxes_agree_with_the_count_and_the_legend_text_is_feminine()
    {
        var (text, _) = Render(Fixture("box"), ReportAudience.Everything);
        Assert.Contains("0 confirmate · 1 doar indicativă", text.Replace("\n", " "));
        Assert.Contains("Confirmată = susținută sau coroborată", text);
        Assert.Contains("indicativă = încă neconfirmată", text);
    }

    [Fact]
    public void The_report_uses_only_romanian_words_for_the_agreed_vocabulary()
    {
        var (text, _) = Render(Fixture("vocab"), ReportAudience.Everything);
        var body = text[..text.IndexOf("Legendă coduri", StringComparison.Ordinal)];
        Assert.DoesNotMatch(@"(?i)\btask(ul|uri|urile|ului)?\b", body);
        Assert.DoesNotContain("Dovezi contradictorii", text);
        Assert.Contains("Probe contradictorii", body);
        Assert.Contains("orele sunt în ", text);
        Assert.Contains(", cu ora UTC între paranteze", text);
        Assert.Contains("(numele nu apare în probe; s-a folosit numele calculatorului pe care s-a făcut colectarea)", text.Replace("\n", " "));
        Assert.Contains("Cronologie politici: nedefinită", body);
        Assert.DoesNotContain("nedefinit (", body);
        Assert.Contains("export eșuat (cod 87: parametru incorect)", body);
        Assert.DoesNotContain("Failed", body);
        Assert.DoesNotContain("(cod 87 (", text);
        Assert.Contains("importat la", body);
        Assert.DoesNotContain("imported ", body);
        Assert.Contains("Verificare independentă: neefectuată.", body);
        Assert.DoesNotContain("NOT_ASSESSED", body);
        Assert.DoesNotContain("și alte ", body);
    }

    private InvestigationResult FixtureWithAllStates()
    {
        var r = Fixture("all");
        var ev = r.Case.LoadEvidence().First();
        var T0 = new DateTimeOffset(2026, 10, 11, 9, 30, 0, TimeSpan.Zero);
        var extra = new List<Finding>();
        int n = 10;
        foreach (var cls in Enum.GetValues<Classification>())
            foreach (var sev in new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.Info })
            {
                var id = $"F-{++n:0000}";
                extra.Add(new Finding
                {
                    FindingId = id, RuleId = "PERSIST-TASK-CONFIG", Title = $"Activitate planificată {id}", Severity = sev, Category = "Persistence", Classification = cls,
                    Description = "descriere " + id, ClassificationReason = $"motiv {cls} {sev}", FirstSeenUtc = T0, SupportingEvidence = [new(ev.EvidenceId, "activitatea nr. " + n, "x", ev.Sha256)],
                });
            }
        r.Findings.AddRange(FindingContract.Enrich(extra));
        r.AntiForensics.Add(new AntiForensicCheck("AF02", "Ceas schimbat", "T1070.006", AntiForensicResult.Detected, "Ceasul a fost mutat.", []));
        r.AntiForensics.Add(new AntiForensicCheck("AF03", "Jurnal oprit", "T1562.002", AntiForensicResult.Undetermined, "Sursa lipsește.", []));
        foreach (var st in new[] { EvidenceStatus.Success, EvidenceStatus.Empty, EvidenceStatus.Failed, EvidenceStatus.Partial, EvidenceStatus.SkippedByDesign })
            r.Gaps.Add(new EvidenceGap("Sursa " + st, st, "motiv", "impact", "alternativă", "Posibil"));
        foreach (var status in new[] { "VALIDATED", "TESTED", "EXPERIMENTAL" })
            r.Parsing.Add(new ParseResult { EvidenceId = ev.EvidenceId, Parser = "P" + status, ParserVersion = "1", ParserStatus = status, Status = EvidenceStatus.Success, Records = 3 });
        return r;
    }

    [Theory]
    [MemberData(nameof(Audiences))]
    public void No_english_code_from_the_legend_appears_in_the_body_of_a_report_with_every_state(ReportAudience audience)
    {
        var (text, _) = Render(FixtureWithAllStates(), audience);
        int legendAt = text.IndexOf("Legendă coduri", StringComparison.Ordinal);
        Assert.True(legendAt > 0);
        var body = text[..legendAt];
        var codes = InvestigationReportText.Legend().SelectMany(l => Regex.Matches(l, @"= ([A-Z][A-Z_]*(?:/[A-Z][A-Z_]*)?)").Select(m => m.Groups[1].Value))
            .SelectMany(c => c.Split('/')).Distinct().ToList();
        Assert.Contains("BENIGN", codes);
        Assert.Contains("VALIDATED", codes);
        foreach (var code in codes)
            Assert.False(Regex.IsMatch(body, $@"(?<![A-Za-z_]){Regex.Escape(code)}(?![A-Za-z_])"), $"English code {code} appears in the body of the {audience} report");
    }

    [Fact]
    public void Legend_labels_are_unique_in_each_group_and_the_benign_known_class_has_its_own()
    {
        Assert.Equal("Cunoscută, fără risc", RoLabels.Classification(Classification.BenignKnown));
        Assert.NotEqual(RoLabels.Classification(Classification.Direct), RoLabels.Classification(Classification.BenignKnown));
        foreach (var line in InvestigationReportText.Legend())
        {
            var labels = Regex.Matches(line.Substring(line.IndexOf(": ", StringComparison.Ordinal) + 2), @"(?:^|, )(?<l>[^=,]+?) = ").Select(m => m.Groups["l"].Value.Trim()).ToList();
            Assert.True(labels.Count >= 2, line);
            Assert.Equal(labels.Count, labels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void Page_one_lists_each_serious_finding_once_and_the_legal_notes_point_back_to_it_unless_the_audience_is_legal()
    {
        var (all, _) = Render(Fixture("p1"), ReportAudience.Everything);
        Assert.DoesNotContain("Cele mai importante constatări", all);
        string page1 = all[..all.IndexOf("Integritatea elementelor de probă verificată", StringComparison.Ordinal)];
        Assert.Equal(1, Regex.Count(page1, "Activitate planificată care rulează dintr-o locație"));
        Assert.Equal(1, Regex.Count(all, Regex.Escape(InvestigationReportText.CannotProveText)));
        Assert.Contains("vezi caseta „Concluzie pe scurt” de pe prima pagină", all);
        var (legal, _) = Render(Fixture("p1l"), ReportAudience.Legal);
        Assert.Equal(2, Regex.Count(legal, Regex.Escape(InvestigationReportText.CannotProveText)));
    }

    [Fact]
    public void The_serious_list_on_page_one_is_capped_at_five()
    {
        var r = Fixture("cap");
        r.Findings.Clear();
        r.Findings.AddRange(FindingContract.Enrich(Enumerable.Range(1, 8).Select(i => Grave($"F-{i}", Classification.Candidate)).ToList()));
        Assert.Equal(5, InvestigationReportText.Summarize(r, ReportIntegrity.Check(r)).Serious.Count);
    }

    [Fact]
    public void Footer_lines_are_short_enough_not_to_be_cut_at_the_page_edge()
    {
        Assert.NotEmpty(LogAnalyzer.Dfir.Windows.Audit.ReportFooter.ProvenanceLines);
        foreach (var l in LogAnalyzer.Dfir.Windows.Audit.ReportFooter.ProvenanceLines)
            Assert.True(l.Length <= LogAnalyzer.Dfir.Windows.Audit.ReportFooter.MaxLineLength, $"footer line too long ({l.Length}): {l}");
        var seen = new List<string>();
        LogAnalyzer.Dfir.Windows.Audit.ReportFooter.Observer.Value = (_, t) => seen.AddRange(t.Split('\n'));
        try { Render(Fixture("foot"), ReportAudience.Everything); }
        finally { LogAnalyzer.Dfir.Windows.Audit.ReportFooter.Observer.Value = null; }
        Assert.NotEmpty(seen);
        Assert.All(seen, l => Assert.True(l.Length <= 140, $"footer line too long ({l.Length}): {l}"));
        Assert.Contains(seen, l => l.StartsWith("Generat de "));
    }
}
