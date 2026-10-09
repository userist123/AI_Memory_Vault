using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP6a / U4-U6, U8: the finding card, the universal "De ce?", the three evidence levels and Ce știm / Ce suspectăm / Ce nu putem demonstra.</summary>
public class Wp6aFindingCardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 8, 42, 7, TimeSpan.Zero);
    private const string Sha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Sha2 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static EvidenceItem Item(string id, string source, string path, string sha, string parser = "EvtxParser", string ver = "1.2") => new()
    {
        EvidenceId = id, CaseId = "C1", Source = source, SourceType = "evtx", OriginalPath = path, StoredPath = "Evidence/" + id + ".evtx", Sha256 = sha, Parser = parser, ParserVersion = ver,
    };

    private static TimelineEvent Ev(string evidenceId, string locator, DateTimeOffset t) => new()
    {
        Time = Timestamp.FromUtc(t.UtcDateTime, t.ToString("o"), "test"), Source = "Security", EvidenceId = evidenceId, Locator = locator, Summary = "x",
        SourceSha256 = "", ParserId = "EvtxParser", ParserVersion = "1.2",
    };

    private static (Finding F, EvidenceContext Ctx) Fixture(Classification c = Classification.Direct)
    {
        var f = new Finding
        {
            FindingId = "F-1", RuleId = "EXEC-PS", Title = "PowerShell pornit dintr-un document", Severity = Severity.High, Classification = c,
            Description = "PowerShell a fost pornit de Word și a rulat un script dintr-un folder temporar.",
            ClassificationReason = "Procesul părinte este Word, iar linia de comandă conține -enc.",
            SupportingEvidence =
            [
                new("EV-1", "record=101", "4688: proces creat powershell.exe", Sha1),
                new("EV-1", "record=102", "4688: proces creat WINWORD.EXE", Sha1),
                new("EV-2", "pf=POWERSHELL.EXE", "Prefetch: powershell.exe rulat", Sha2),
                new("EV-2", "pf=SCRIPT.PS1", "Prefetch: script.ps1 referit", Sha2),
            ],
            MissingEvidence = ["Conținutul scriptului."],
            AlternativeExplanations = ["Macro legitim al unui șablon intern."],
            RecommendedNextSteps = ["Izolați stația de rețea.", "Colectați fișierul script.ps1."],
            Limitations = ["Execuția unui program nu arată cine l-a pornit și nici intenția."],
            HumanSummary = "PowerShell a fost pornit de Word.",
        };
        f.Status = FindingContractStatus(c);
        var ctx = new EvidenceContext
        {
            Items = [Item("EV-1", "EventLog:Security", @"C:\Windows\System32\winevt\Logs\Security.evtx", Sha1), Item("EV-2", "Prefetch", @"C:\Windows\Prefetch\POWERSHELL.EXE-1.pf", Sha2, "PrefetchParser", "2.0")],
            Timeline = [Ev("EV-1", "record=101", T0), Ev("EV-1", "record=102", T0.AddSeconds(-1)), Ev("EV-2", "pf=POWERSHELL.EXE", T0.AddSeconds(2))],
        };
        return (f, ctx);
    }

    private static StandardState FindingContractStatus(Classification c) => LogAnalyzer.Dfir.Analysis.FindingContract.StatusFor(c);

    // ---- U6 evidence in three levels --------------------------------------------------------------------------------

    [Fact]
    public void Evidence_summary_counts_pieces_and_sources_in_romanian()
    {
        var (f, ctx) = Fixture();
        var e = EvidenceLevels.Build(f, ctx);
        Assert.Equal("4 probe din 2 surse", e.Summary);
        Assert.Equal(4, e.List.Count);
        Assert.Equal(4, e.Technical.Count);
    }

    [Theory]
    [InlineData(0, 0, "Nicio probă atașată")]
    [InlineData(1, 1, "1 probă dintr-o sursă")]
    [InlineData(2, 1, "2 probe dintr-o sursă")]
    [InlineData(5, 3, "5 probe din 3 surse")]
    [InlineData(20, 20, "20 de probe din 20 de surse")]
    [InlineData(21, 2, "21 de probe din 2 surse")]
    [InlineData(119, 100, "119 probe din 100 de surse")]
    public void Evidence_summary_uses_correct_romanian_plurals(int pieces, int sources, string expected)
    {
        Assert.Equal(expected, EvidenceLevels.SummaryLine(pieces, sources));
    }

    [Fact]
    public void Evidence_list_has_source_time_and_a_short_description()
    {
        var (f, ctx) = Fixture();
        var l = EvidenceLevels.Build(f, ctx).List;
        Assert.Equal("EventLog:Security", l[0].Source);
        Assert.Equal("2026-09-19 08:42:07 UTC", l[0].TimeText);
        Assert.Equal("4688: proces creat powershell.exe", l[0].Description);
        Assert.Equal("Prefetch", l[2].Source);
    }

    [Fact]
    public void Technical_level_has_id_sha_source_path_parser_version_timestamp_and_locator()
    {
        var (f, ctx) = Fixture();
        var t = EvidenceLevels.Build(f, ctx).Technical[0];
        Assert.Equal("EV-1", t.EvidenceId);
        Assert.Equal(Sha1, t.Sha256);
        Assert.Equal(@"C:\Windows\System32\winevt\Logs\Security.evtx", t.SourcePath);
        Assert.Equal("EvtxParser", t.Parser);
        Assert.Equal("1.2", t.ParserVersion);
        Assert.Equal("2026-09-19T08:42:07.000Z", t.Timestamp);
        Assert.Equal("record=101", t.Locator);
        var p = EvidenceLevels.Build(f, ctx).Technical[2];
        Assert.Equal(("PrefetchParser", "2.0"), (p.Parser, p.ParserVersion));
    }

    [Fact]
    public void Missing_details_are_said_to_be_unknown_never_left_blank_or_guessed()
    {
        var (f, _) = Fixture();
        var e = EvidenceLevels.Build(f, EvidenceContext.Empty);
        Assert.Equal("4 probe din 2 surse", e.Summary);   // sources fall back to the evidence ids
        var t = e.Technical[0];
        Assert.Equal(Sha1, t.Sha256);                       // the reference itself carries the hash
        Assert.Equal(EvidenceLevels.Unknown, t.SourcePath);
        Assert.Equal(EvidenceLevels.Unknown, t.Parser);
        Assert.Equal(EvidenceLevels.Unknown, t.Timestamp);
        Assert.Equal(EvidenceLevels.Unknown, e.List[0].TimeText);
    }

    // ---- U5 universal Why? ------------------------------------------------------------------------------------------

    [Fact]
    public void Why_has_observation_evidence_reasoning_limitations_and_verification()
    {
        var (f, ctx) = Fixture();
        var w = WhyExplainer.Build(f, ctx);
        Assert.Equal(f.Description, w.Observation);
        Assert.Equal("4 probe din 2 surse", w.EvidenceSummary);
        Assert.Equal(4, w.EvidenceLines.Count);
        Assert.Equal(f.ClassificationReason, w.Reasoning);
        Assert.Contains(w.Limitations, l => l.Contains("Conținutul scriptului."));
        Assert.Contains(w.Limitations, l => l.Contains("Macro legitim"));
        Assert.Contains(w.Limitations, l => l.Contains("nici intenția"));
        Assert.Contains("Neevaluat", w.Verification);
        var text = w.ToPlainText();
        foreach (var h in new[] { "Ce am observat", "Dovezi", "Raționament", "Limite", "Verificare" }) Assert.Contains(h, text);
    }

    [Fact]
    public void Why_never_leaves_the_reasoning_blank()
    {
        var (f, ctx) = Fixture();
        var g = new Finding { FindingId = "F-2", RuleId = "X", Title = "t", Description = "d" };
        Assert.Contains("nu este înregistrat", WhyExplainer.Build(g, ctx).Reasoning);
    }

    [Fact]
    public void A_legacy_score_is_shown_only_next_to_its_factors_never_alone()
    {
        var (f, ctx) = Fixture();
        var alone = WhyExplainer.Build(f, ctx, legacyScore: new LegacyScore(72, []));
        Assert.DoesNotContain("72", alone.ToPlainText());
        Assert.Null(alone.LegacyScoreNote);

        var with = WhyExplainer.Build(f, ctx, legacyScore: new LegacyScore(72, ["linie de comandă codificată (+40)", "proces părinte neobișnuit (+32)"]));
        Assert.NotNull(with.LegacyScoreNote);
        Assert.Contains("72", with.LegacyScoreNote);
        Assert.Contains("linie de comandă codificată", with.LegacyScoreNote);
        Assert.Contains("nu este o verificare", with.LegacyScoreNote);
    }

    [Fact]
    public void Why_shows_the_verifier_verdict_its_reason_and_the_checks()
    {
        var (f, ctx) = Fixture();
        f.Verification = new FindingVerification(StandardState.Supported, "Două tipuri de artefact concordă.", "LogAnalyzer.Verification/1.0", T0);
        var w = WhyExplainer.Build(f, ctx, verificationChecks: ["PROVENANCE: trecut — hash-uri intacte"]);
        Assert.Contains("Susținut de dovezi", w.Verification);
        Assert.Contains("Două tipuri de artefact concordă.", w.Verification);
        Assert.Contains(w.VerificationChecks, c => c.Contains("PROVENANCE"));
    }

    // ---- U8 Know / Think / Don't know --------------------------------------------------------------------------------

    [Fact]
    public void A_direct_finding_with_evidence_is_known_and_its_limits_are_not()
    {
        var (f, ctx) = Fixture(Classification.Direct);
        var k = KnowThinkDontKnow.Build(f, ctx);
        Assert.Contains(k.Know, s => s.Text.Contains("PowerShell pornit dintr-un document"));
        Assert.Contains(k.Know, s => s.Text == "4688: proces creat powershell.exe" && s.Basis.Contains("EV-1"));
        Assert.DoesNotContain(k.Think, s => s.Text.Contains("PowerShell pornit"));
        Assert.Contains(k.DontKnow, s => s.Text == "Conținutul scriptului.");
        Assert.Contains(k.DontKnow, s => s.Text.Contains("nici intenția"));
    }

    [Theory]
    [InlineData(Classification.Correlated)]
    [InlineData(Classification.Candidate)]
    [InlineData(Classification.Unproven)]
    public void A_finding_that_is_not_direct_is_suspected_with_its_reason_not_known(Classification c)
    {
        var (f, ctx) = Fixture(c);
        var k = KnowThinkDontKnow.Build(f, ctx);
        var think = Assert.Single(k.Think, s => s.Text.Contains("PowerShell pornit dintr-un document"));
        Assert.Contains(f.ClassificationReason, think.Basis);
        Assert.DoesNotContain(k.Know, s => s.Text.Contains("PowerShell pornit dintr-un document"));
        Assert.Contains(k.Know, s => s.Text == "4688: proces creat powershell.exe");   // the cited records are still observations
        Assert.Contains(k.Think, s => s.Text.Contains("Macro legitim"));               // an explanation that cannot be excluded is a suspicion, not a fact
    }

    [Fact]
    public void A_claim_without_evidence_is_never_known()
    {
        var f = new Finding { FindingId = "F-3", RuleId = "X", Title = "Fără probe", Description = "d", Classification = Classification.Direct };
        var k = KnowThinkDontKnow.Build(f, EvidenceContext.Empty);
        Assert.Empty(k.Know);
        Assert.Contains(k.DontKnow, s => s.Text.Contains("nu are probe atașate"));
    }

    [Theory]
    [InlineData(StandardState.Contradicted)]
    [InlineData(StandardState.Rejected)]
    public void A_claim_the_verifier_contradicted_or_rejected_is_neither_known_nor_suspected(StandardState verdict)
    {
        var (f, ctx) = Fixture(Classification.Direct);
        f.Verification = new FindingVerification(verdict, "O altă probă contrazice.", "V");
        var k = KnowThinkDontKnow.Build(f, ctx);
        Assert.DoesNotContain(k.Know, s => s.Text.Contains("PowerShell pornit dintr-un document"));
        Assert.DoesNotContain(k.Think, s => s.Text.Contains("PowerShell pornit dintr-un document"));
        Assert.Contains(k.DontKnow, s => s.Text.Contains("PowerShell pornit dintr-un document") && s.Basis.Contains("O altă probă contrazice."));
    }

    [Fact]
    public void Unobserved_sequence_steps_and_gaps_that_touch_the_evidence_are_not_known()
    {
        var (f, ctx) = Fixture();
        f.Sequence = new SequenceDetail("10 min", "doar în timp",
        [
            new(1, "Fișier descărcat", true, T0, "owner", "setup.zip", "EventLog:Security", ["F-1"], "Descărcare observată."),
            new(2, "Fișier copiat pe USB", false, null, "", "", "", [], "pas neobservat: sursa USB nu a fost colectată"),
        ]);
        ctx = new EvidenceContext
        {
            Items = ctx.Items, Timeline = ctx.Timeline,
            Gaps = [new EvidenceGap("EventLog:Security", EvidenceStatus.Partial, "Jurnalul are o istorie de 3 zile.", "poate lipsi activitate mai veche", "", ""), new EvidenceGap("SRUM", EvidenceStatus.NotAvailable, "lipsă", "", "", "")],
        };
        var k = KnowThinkDontKnow.Build(f, ctx);
        Assert.Contains(k.Know, s => s.Text.Contains("Fișier descărcat"));
        Assert.DoesNotContain(k.Know, s => s.Text.Contains("copiat pe USB"));
        Assert.Contains(k.DontKnow, s => s.Text.Contains("Fișier copiat pe USB") && s.Basis.Contains("neobservat"));
        Assert.Contains(k.DontKnow, s => s.Text.Contains("EventLog:Security") && s.Text.Contains("Jurnalul are o istorie"));
        Assert.DoesNotContain(k.DontKnow, s => s.Text.Contains("SRUM"));   // a gap in a source this finding does not use does not affect it
    }

    [Fact]
    public void The_rule_is_documented_in_the_type()
    {
        var src = File.ReadAllText(Path.Combine(Wp6aXamlTests.AppDir(), "..", "LogAnalyzer.Dfir.Core", "Presentation", "KnowThinkDontKnow.cs"));
        foreach (var heading in new[] { "CE ȘTIM", "CE SUSPECTĂM", "CE NU PUTEM DEMONSTRA" }) Assert.Contains(heading, src);
    }

    // ---- U4 finding card -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_card_carries_title_summary_severity_as_text_state_and_verification()
    {
        var (f, ctx) = Fixture();
        var c = FindingCardModel.Build(f, ctx);
        Assert.Equal("PowerShell pornit dintr-un document", c.Title);
        Assert.Equal("PowerShell a fost pornit de Word.", c.HumanSummary);
        Assert.Equal("Ridicată", c.SeverityText);
        Assert.Equal("HIGH", c.SeveritySpec);
        Assert.False(string.IsNullOrWhiteSpace(c.SeverityIcon));     // not colour alone
        Assert.Equal("Observat", c.StateLabel);
        Assert.Equal("Neevaluat", c.VerificationLabel);
        Assert.Contains("Nicio verificare", c.VerificationText);
        Assert.Equal(["Izolați stația de rețea.", "Colectați fișierul script.ps1."], c.NextSteps);
        Assert.Equal("4 probe din 2 surse", c.Evidence.Summary);
        Assert.NotNull(c.Why);
        Assert.NotNull(c.Know);
    }

    [Fact]
    public void Every_severity_has_a_distinct_text_and_a_distinct_icon()
    {
        var all = Enum.GetValues<Severity>();
        Assert.Equal(all.Length, all.Select(SeverityLabels.Romanian).Distinct().Count());
        Assert.Equal(all.Length, all.Select(SeverityLabels.Icon).Distinct().Count());
        Assert.All(all, s => Assert.False(string.IsNullOrWhiteSpace(SeverityLabels.Romanian(s))));
    }

    [Fact]
    public void The_card_falls_back_to_the_description_and_says_when_no_next_step_is_recorded()
    {
        var f = new Finding { FindingId = "F-9", RuleId = "X", Title = "t", Description = "Ce s-a întâmplat." };
        var c = FindingCardModel.Build(f, EvidenceContext.Empty);
        Assert.Equal("Ce s-a întâmplat.", c.HumanSummary);
        Assert.Single(c.NextSteps);
        Assert.Contains("Niciun pas recomandat", c.NextSteps[0]);
    }

    [Fact]
    public void The_four_actions_have_romanian_labels_and_distinct_access_keys()
    {
        var a = FindingCardActions.All;
        Assert.Equal(["De ce?", "Arată dovezile", "Verifică", "Ce trebuie să fac"], a.Select(x => x.Label));
        Assert.Equal(4, a.Select(x => x.AccessKey).Distinct().Count());
        foreach (var x in a) Assert.Contains("_" + x.AccessKey, x.AccessText, StringComparison.OrdinalIgnoreCase);
        Assert.All(a, x => Assert.False(string.IsNullOrWhiteSpace(x.HelpText)));
    }

    [Fact]
    public void Verification_check_lines_name_the_check_and_say_the_outcome_in_romanian()
    {
        var v = new LogAnalyzer.Verification.FindingVerdict
        {
            Checks =
            [
                LogAnalyzer.Verification.CheckResult.Pass(LogAnalyzer.Verification.CheckIds.Provenance, "hash-uri intacte"),
                LogAnalyzer.Verification.CheckResult.Fail(LogAnalyzer.Verification.CheckIds.Contradictions, StandardState.Contradicted, "o probă contrazice"),
                LogAnalyzer.Verification.CheckResult.Unknown(LogAnalyzer.Verification.CheckIds.Temporal, "ora nu este cunoscută"),
                LogAnalyzer.Verification.CheckResult.NotApplicable(LogAnalyzer.Verification.CheckIds.Graph, "fără graf"),
                LogAnalyzer.Verification.CheckResult.Info(LogAnalyzer.Verification.CheckIds.Sufficiency, "două tipuri de artefact"),
            ],
        };
        Assert.Equal(
            ["PROVENANCE: trecut — hash-uri intacte", "CONTRADICTIONS: eșuat — o probă contrazice", "TEMPORAL: necunoscut — ora nu este cunoscută",
             "GRAPH: nu se aplică — fără graf", "SUFFICIENCY: informativ — două tipuri de artefact"],
            v.CheckLines());
    }
}
