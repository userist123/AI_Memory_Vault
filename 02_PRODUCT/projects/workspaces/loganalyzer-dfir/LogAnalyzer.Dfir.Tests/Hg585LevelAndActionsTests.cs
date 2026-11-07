using System.Text.Json;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Matrix HG 585 / ORNISS (docs/dfir/MATRICE_HG585_ORNISS_2026-10-23.md): the station level from the profile drives the level rules (A18 retention, N14 internet),
/// every NECONFORM check gets a measure, a responsible person and a deadline (row 38), an action closes only on a CONFORM check, and the proces-verbal prints
/// the measures and the difference from the previous control (row 39).
/// </summary>
public class Hg585LevelAndActionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static StationFacts Facts(string level, DateTimeOffset? securityOldest, StationPosture posture = StationPosture.Standalone, bool readable = true) => new()
    {
        Host = "LAB-PC", CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now, IsAdministrator = true, Posture = posture, StationLevel = level,
        Coverage = { new ChannelCoverage("Security", securityOldest, 100, readable, ""), new ChannelCoverage("System", Now.AddDays(-60), 10, true, "") },
    };

    private static ControlCheck Check(ControlReport r, string id) => Assert.Single(r.Checks, c => c.Id == id);
    private static ControlCheck C(string id, ControlStatus s) => new(id, "Rețea", "Verificarea " + id, s, "detaliu", [], "recomandare");

    // ---- level --------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("secret de serviciu", 1, null)]
    [InlineData("SECRET", 2, 3)]
    [InlineData("strict secret", 3, 3)]
    [InlineData("SSID", 4, 10)]
    [InlineData("NATO SECRET", 3, 3)]
    public void Level_rules_follow_the_rank_of_the_marking(string marking, int rank, int? years)
    {
        var level = StationLevelRules.Resolve(marking);
        Assert.NotNull(level);
        Assert.Equal(rank, level!.Rank);
        var rules = StationLevelRules.For(level);
        Assert.Equal(years, rules.AccessLogRetentionYears);
        Assert.Equal(rank == 4, rules.InternetForbidden);
        Assert.Equal(rank == 4, rules.DestructionOnly);
        Assert.Equal(rank >= 2, rules.TempestApplies);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nemarcat")]
    [InlineData("NECLASIFICAT")]
    [InlineData("confidențial")]
    public void An_empty_or_unknown_level_is_never_guessed(string text)
    {
        Assert.Null(StationLevelRules.Resolve(text));
        Assert.Equal(StationLevelRules.UndefinedText, StationLevelRules.Describe(text));
    }

    [Fact]
    public void A18_is_undetermined_without_a_level_and_never_conform()
    {
        var r = ControlEvaluator.Evaluate(Facts("", Now.AddYears(-20)));
        var a18 = Check(r, "A18");
        Assert.Equal(ControlStatus.Nedeterminat, a18.Status);
        Assert.Contains("nu este definit în profilul de proceduri", a18.Detail);
    }

    [Fact]
    public void A18_is_conform_when_the_live_log_alone_covers_the_level_retention()
    {
        var a18 = Check(ControlEvaluator.Evaluate(Facts("SECRET", Now.AddYears(-3).AddDays(-1))), "A18");
        Assert.Equal(ControlStatus.Conform, a18.Status);
        Assert.Contains("ultimii 3 ani", a18.Detail);
    }

    [Fact]
    public void A18_asks_for_the_exported_archives_when_the_log_starts_later_never_neconform()
    {
        var a18 = Check(ControlEvaluator.Evaluate(Facts("strict secret de importanță deosebită", Now.AddYears(-2))), "A18");
        Assert.Equal(ControlStatus.DeVerificat, a18.Status);
        Assert.Contains("arhivele exportate", a18.Detail);
        Assert.Contains("2016-11-04", a18.Detail);   // ten years before the control
    }

    [Fact]
    public void A18_at_secret_de_serviciu_defers_to_the_accreditation_documentation()
    {
        var a18 = Check(ControlEvaluator.Evaluate(Facts("secret de serviciu", Now.AddYears(-1))), "A18");
        Assert.Equal(ControlStatus.DeVerificat, a18.Status);
        Assert.Contains("documentația de acreditare", a18.Detail);
    }

    [Fact]
    public void A18_is_undetermined_when_the_security_log_was_not_read()
    {
        var a18 = Check(ControlEvaluator.Evaluate(Facts("SECRET", null, readable: false)), "A18");
        Assert.Equal(ControlStatus.Nedeterminat, a18.Status);
    }

    [Fact]
    public void N14_is_neconform_for_SSID_on_the_internet_edition_and_review_for_lower_levels()
    {
        Assert.Equal(ControlStatus.Neconform, Check(ControlEvaluator.Evaluate(Facts("SSID", Now.AddYears(-11), StationPosture.Internet)), "N14").Status);
        Assert.Equal(ControlStatus.DeVerificat, Check(ControlEvaluator.Evaluate(Facts("SECRET", Now.AddYears(-4), StationPosture.Internet)), "N14").Status);
        Assert.Equal(ControlStatus.Nedeterminat, Check(ControlEvaluator.Evaluate(Facts("", Now.AddYears(-4), StationPosture.Internet)), "N14").Status);
    }

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    public void N14_does_not_apply_to_editions_without_internet(StationPosture posture) =>
        Assert.Equal(ControlStatus.NuSeAplica, Check(ControlEvaluator.Evaluate(Facts("SSID", Now.AddYears(-11), posture)), "N14").Status);

    [Fact]
    public void The_profile_keeps_the_station_level_through_json()
    {
        var p = new ProcedureProfile { StationLevel = "STRICT SECRET" };
        var back = JsonSerializer.Deserialize<ProcedureProfile>(JsonSerializer.Serialize(p))!;
        Assert.Equal("STRICT SECRET", back.StationLevel);
        Assert.Equal("", new ProcedureProfile().StationLevel);
    }

    // ---- corrective actions (row 38) ---------------------------------------------------------------------------------------------

    [Fact]
    public void Every_neconform_check_gets_one_new_open_action()
    {
        var plan = CorrectiveActions.Plan([C("N03", ControlStatus.Neconform), C("N04", ControlStatus.DeVerificat), C("N03", ControlStatus.Neconform)], Now, null);
        var a = Assert.Single(plan);
        Assert.Equal(("N03", CorrectiveState.Deschisa, Now), (a.CheckId, a.State, a.OpenedUtc));
        Assert.Null(a.CarriedFromUtc);
    }

    [Fact]
    public void Missing_lists_every_open_action_without_measure_responsible_or_valid_deadline()
    {
        var plan = CorrectiveActions.Plan([C("N03", ControlStatus.Neconform), C("C01", ControlStatus.Neconform)], Now, null);
        Assert.Equal(2, CorrectiveActions.Missing(plan).Count);
        plan[0].Measure = "Dezactivarea adaptorului Wi-Fi"; plan[0].Responsible = "Administratorul de sistem"; plan[0].Deadline = "15.11.2026";
        plan[1].Measure = "Dezactivarea contului Guest"; plan[1].Responsible = "Administratorul de sistem"; plan[1].Deadline = "mâine";
        var missing = Assert.Single(CorrectiveActions.Missing(plan));
        Assert.Contains("termen valid", missing);
        plan[1].Deadline = "2026-11-20";
        Assert.Empty(CorrectiveActions.Missing(plan));
    }

    private static PreviousControl Previous(params CorrectiveAction[] actions) =>
        new("x", Now.AddDays(-90), "LAB-PC", [], actions);

    private static CorrectiveAction Open(string id, string deadline = "2026-09-30") => new()
    {
        CheckId = id, Title = "Verificarea " + id, OpenedUtc = Now.AddDays(-90), Measure = "măsura " + id, Responsible = "responsabil", Deadline = deadline,
    };

    [Fact]
    public void A_previous_action_closes_only_when_its_check_is_conform_now()
    {
        var plan = CorrectiveActions.Plan([C("N03", ControlStatus.Conform), C("N04", ControlStatus.Nedeterminat), C("N05", ControlStatus.Neconform)],
            Now, Previous(Open("N03"), Open("N04"), Open("N05"), Open("N06")));
        Assert.Equal(4, plan.Count);
        var n03 = plan.Single(a => a.CheckId == "N03");
        Assert.Equal((CorrectiveState.Inchisa, (DateTimeOffset?)Now), (n03.State, n03.ClosedUtc));
        foreach (var id in new[] { "N04", "N05", "N06" })
        {
            var a = plan.Single(x => x.CheckId == id);
            Assert.Equal(CorrectiveState.Deschisa, a.State);
            Assert.StartsWith("Rămâne deschisă", a.Note);
            Assert.Equal("măsura " + id, a.Measure);                      // carried with its measure and deadline
            Assert.Equal(Now.AddDays(-90), a.CarriedFromUtc);
        }
        Assert.Contains("NEDETERMINAT", plan.Single(a => a.CheckId == "N04").Note);
        Assert.Contains("nu a fost evaluată", plan.Single(a => a.CheckId == "N06").Note);
    }

    [Fact]
    public void A_previous_closed_action_is_not_repeated_and_a_recurring_neconformity_opens_again()
    {
        var closed = Open("N03"); closed.State = CorrectiveState.Inchisa;
        var a = Assert.Single(CorrectiveActions.Plan([C("N03", ControlStatus.Neconform)], Now, Previous(closed)));
        Assert.Null(a.CarriedFromUtc);
        Assert.Equal("", a.Measure);
    }

    [Fact]
    public void A_saved_closed_state_never_hides_a_check_that_was_neconform_in_the_same_previous_report()
    {
        var tampered = Open("N03"); tampered.State = CorrectiveState.Inchisa;
        var prev = new PreviousControl("x", Now.AddDays(-90), "LAB-PC", [C("N03", ControlStatus.Neconform), C("N04", ControlStatus.Neconform)], [tampered]);
        var plan = CorrectiveActions.Plan([C("N03", ControlStatus.Nedeterminat)], Now, prev);
        var n03 = plan.Single(a => a.CheckId == "N03");
        Assert.Equal(CorrectiveState.Deschisa, n03.State);
        Assert.Equal("măsura N03", n03.Measure);
        Assert.StartsWith("Starea salvată nu corespunde", n03.Note);
        var n04 = plan.Single(a => a.CheckId == "N04");            // NECONFORM before, with no action at all: carried open
        Assert.Equal(CorrectiveState.Deschisa, n04.State);
        Assert.Contains("nu a fost evaluată", n04.Note);
    }

    [Fact]
    public void A_report_saved_before_the_register_existed_does_not_invent_actions()
    {
        var old = new PreviousControl("x", Now.AddDays(-90), "LAB-PC", [C("N03", ControlStatus.Neconform)]);
        Assert.Empty(CorrectiveActions.Plan([C("N03", ControlStatus.Conform)], Now, old));
    }

    [Theory]
    [InlineData("NATO SECRET")]
    [InlineData("SECRET UE/EU SECRET")]
    public void A18_is_never_conform_for_a_NATO_or_EU_level(string level)
    {
        var a18 = Check(ControlEvaluator.Evaluate(Facts(level, Now.AddYears(-20))), "A18");
        Assert.Equal(ControlStatus.DeVerificat, a18.Status);
        Assert.Contains("nu este un nivel național", a18.Detail);
    }

    [Fact]
    public void Overdue_open_actions_are_printed_first_and_in_red()
    {
        var plan = CorrectiveActions.Plan([C("N03", ControlStatus.Neconform), C("N04", ControlStatus.Neconform)], Now,
            Previous(Open("N04", "2026-12-31"), Open("N03", "2026-10-01")));
        var lines = CorrectiveActions.SectionLines(plan, DateOnly.FromDateTime(Now.UtcDateTime));
        var first = lines.First(l => l.Head && l.Text.StartsWith("N0"));
        Assert.Equal("N03 Verificarea N03 — TERMEN DEPĂȘIT", first.Text);
        Assert.Equal("#b91c1c", first.Color);
        Assert.Contains("termen depășit: 1", lines[0].Text);
    }

    [Fact]
    public void Corrective_actions_survive_the_saved_report_and_reach_the_next_control()
    {
        var root = Path.Combine(Path.GetTempPath(), "hg585_" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, "Control", "CONTROL_20260806_090000");
            Directory.CreateDirectory(folder);
            var action = Open("N03");
            var json = JsonSerializer.Serialize(new
            {
                Facts = new { Host = "LAB-PC", CollectedUtc = Now.AddDays(-90) },
                Checks = new[] { C("N03", ControlStatus.Neconform) },
                CorrectiveActions = new[] { action },
            }, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            File.WriteAllText(Path.Combine(folder, "control_report.json"), json);
            var prev = Assert.Single(ControlArchive.List(root, out var problems));
            Assert.Empty(problems);
            var carried = Assert.Single(prev.Actions!);
            Assert.Equal(("N03", "măsura N03", CorrectiveState.Deschisa), (carried.CheckId, carried.Measure, carried.State));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    // ---- proces-verbal (rows 38 and 39) ------------------------------------------------------------------------------------------

    [Fact]
    public void The_report_prints_the_level_the_measures_and_the_comparison()
    {
        var r = ControlEvaluator.Evaluate(Facts("SECRET", Now.AddYears(-1)));
        var prev = new PreviousControl("x", Now.AddDays(-90), "LAB-PC", [C("A18", ControlStatus.Conform)], [Open("N03")]);
        var plan = CorrectiveActions.Plan(r.Checks, Now, prev);
        var cmp = ControlComparison.Compare(prev, r.Checks);
        var corpus = ControlReportPdf.Corpus(r, actions: plan, comparison: cmp);
        Assert.Contains("măsura N03", corpus);
        Assert.Contains("Față de controlul din", corpus);
        Assert.Contains("A18 Retenția registrelor de acces cerută de nivelul stației: CONFORM → DE VERIFICAT", corpus);

        var printed = new List<string>();
        ControlReportPdf.TextObserver.Value = printed.Add;
        var pdf = Path.Combine(Path.GetTempPath(), $"hg585_{Guid.NewGuid():N}.pdf");
        try
        {
            ControlReportPdf.Write(r, pdf, "Inspector Test", actions: plan, comparison: cmp);
            Assert.Contains(CorrectiveActions.SectionTitle, printed);
            Assert.Contains(ControlComparison.SectionTitle, printed);
            Assert.True(printed.IndexOf(CorrectiveActions.SectionTitle) < printed.IndexOf(ControlComparison.SectionTitle));
            Assert.True(new FileInfo(pdf).Length > 0);
        }
        finally { ControlReportPdf.TextObserver.Value = null; File.Delete(pdf); }
    }

    [Fact]
    public void Without_a_register_or_a_previous_control_the_report_says_so()
    {
        Assert.Contains("nu a fost completat", CorrectiveActions.SectionLines(null, DateOnly.FromDateTime(Now.UtcDateTime))[0].Text);
        Assert.Contains("Nu există un control anterior", ControlComparison.SectionLines(null)[0].Text);
    }
}
