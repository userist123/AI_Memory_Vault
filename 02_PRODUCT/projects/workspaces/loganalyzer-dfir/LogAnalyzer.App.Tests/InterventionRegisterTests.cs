using System.IO;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Planner #9 item 7 (HG 585 art. 277 "orice modificare a SIC de cel puțin 2 persoane", art. 329 alin. 2; ORNISS model Anexa 14; matrix rows 20, 23, 29): the register of hardware / software
/// interventions with a witness, and the control check S08 over it. The evaluator is pure (StationFacts built in memory).
/// (These tests live here because the compiled LogAnalyzer.Dfir.Tests assembly is quarantined by Defender on the development machine.)
/// </summary>
public sealed class InterventionRegisterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-interv-" + Guid.NewGuid().ToString("N"));

    public InterventionRegisterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    // Date, Time, Station, Kind, Components, Description, Performer, Witness, AuthorizationRef, PostCheckDate, PostCheckBy, Notes
    private static string[] Cells(string date = "2026-11-01", string kind = "software", string performer = "Ion Popescu", string witness = "Maria Ionescu",
                                  string postDate = "", string postBy = "") =>
        [date, "10:30", "1234DOC5143SV", kind, "Placa de rețea", "Înlocuire componentă", performer, witness, "ORD-12/2026", postDate, postBy, ""];

    private static List<(string Message, bool IsError)> Validate(string[] c) => new InterventionRegister().ValidateRow(c, []);
    private static bool HasError(string[] c, string contains) => Validate(c).Any(m => m.IsError && m.Message.Contains(contains, StringComparison.Ordinal));

    private static InterventionRow Row(string date = "2026-11-01", string kind = "software", string performer = "Ion Popescu", string witness = "Maria Ionescu",
                                       string postDate = "", string postBy = "") =>
        new() { Date = date, Time = "10:30", Station = "S1", Kind = kind, Components = "Placa de rețea", Description = "Înlocuire", Performer = performer, Witness = witness, PostCheckDate = postDate, PostCheckBy = postBy };

    private static StationFacts Facts(List<InterventionRow>? rows, string problem = "") => new()
    {
        Posture = StationPosture.Standalone, IsAdministrator = true, InterventionRegister = rows, InterventionRegisterProblem = problem,
        CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-31), PeriodEndUtc = Now,
    };

    private static ControlCheck S08(StationFacts f) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "S08");

    private static LogFact Msi(DateTimeOffset t) =>
        new("Application", 11707, t, 1, "", new Dictionary<string, string> { ["Data0"] = "Produs instalat" });

    // ---- validation -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_complete_row_is_valid()
    {
        Assert.DoesNotContain(Validate(Cells()), m => m.IsError);
        Assert.DoesNotContain(Validate(Cells(kind: "reparație", postDate: "2026-11-02", postBy: "Maria Ionescu")), m => m.IsError);
    }

    [Theory]
    [InlineData("Ion Popescu", "Ion Popescu")]
    [InlineData("Ion Popescu", "ion popescu")]
    [InlineData("Ion Popescu", "  ION   POPESCU ")]
    [InlineData(@"DOM\ion", "ion")]
    public void The_witness_must_differ_from_the_performer_ignoring_case_and_spacing(string performer, string witness)
    {
        Assert.True(HasError(Cells(performer: performer, witness: witness), "martorul trebuie să fie altă persoană"));
    }

    [Fact]
    public void A_missing_witness_or_performer_is_refused()
    {
        Assert.True(HasError(Cells(witness: ""), "martorul lipsește"));
        Assert.True(HasError(Cells(witness: "   "), "martorul lipsește"));
        Assert.True(HasError(Cells(performer: ""), "executantul lipsește"));
    }

    [Fact]
    public void Kind_date_time_and_post_check_are_validated()
    {
        Assert.True(HasError(Cells(kind: "altceva"), "nu este hardware | software | configurare | reparatie"));
        Assert.True(HasError(Cells(kind: ""), "felul"));
        foreach (var k in new[] { "hardware", "SOFTWARE", "configurare", "reparatie", "reparație", "repair" })
            Assert.DoesNotContain(Validate(Cells(kind: k)), m => m.IsError);
        Assert.True(HasError(Cells(date: ""), "data intervenției lipsește"));
        Assert.True(HasError(Cells(date: "01.11.2026"), "yyyy-MM-dd"));
        Assert.True(HasError(Cells(date: "2099-01-01"), "în viitor"));
        var c = Cells(); c[1] = "25:99";
        Assert.True(HasError(c, "HH:mm"));
        Assert.True(HasError(Cells(postDate: "2026-11-02"), "nu are persoana"));
        Assert.True(HasError(Cells(postBy: "Maria Ionescu"), "nu are data"));
        Assert.True(HasError(Cells(date: "2026-11-05", postDate: "2026-11-01", postBy: "Maria Ionescu"), "înaintea intervenției"));
        // hardware without the check is a warning at entry time (the check S08 decides later), not an error
        var w = Validate(Cells(kind: "hardware"));
        Assert.DoesNotContain(w, m => m.IsError);
        Assert.Contains(w, m => !m.IsError && m.Message.Contains("verificarea dispozitivelor de securitate", StringComparison.Ordinal));
    }

    // ---- storage: the same mechanism as the other registers ---------------------------------------------------------------

    [Fact]
    public void Rows_round_trip_through_the_register_file_with_an_audit_line()
    {
        var path = Path.Combine(_dir, "interventions.json");
        var reg = new InterventionRegister();
        reg.SetCells([Cells(kind: "hardware", postDate: "2026-11-02", postBy: "Maria Ionescu"), Cells(date: "2026-11-03", kind: "configurare")]);
        var issues = RegisterStore.Save(reg, path, out var sha, who: "test");
        Assert.DoesNotContain(issues, i => i.IsError);
        Assert.True(sha.Length == 64);
        var loaded = RegisterStore.Load<InterventionRegister>(path);
        Assert.False(loaded.HasErrors);
        Assert.NotNull(loaded.Register);
        Assert.Equal(reg.Cells().Count, loaded.Register!.Cells().Count);
        for (int i = 0; i < reg.Cells().Count; i++) Assert.Equal(reg.Cells()[i], loaded.Register.Cells()[i]);
        Assert.Equal("Maria Ionescu", loaded.Register.Rows[0].PostCheckBy);
        Assert.Equal("interventions", loaded.Register.Kind);
        Assert.True(File.Exists(RegisterStore.AuditPathFor(path)));
        Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, RegisterStore.VerifyAudit(path).Status);
    }

    [Fact]
    public void A_register_with_an_invalid_row_is_not_written()
    {
        var path = Path.Combine(_dir, "interventions.json");
        var reg = new InterventionRegister();
        reg.SetCells([Cells(witness: "ion popescu")]);
        var issues = RegisterStore.Save(reg, path, out _);
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("martorul", StringComparison.Ordinal));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Control_inputs_missing_file_is_not_in_use_and_an_unreadable_one_is_a_problem_never_an_empty_register()
    {
        var path = Path.Combine(_dir, "interventions.json");
        var (none, p0) = StationControlViewModel.CurrentInterventions(path);
        Assert.Null(none); Assert.Equal("", p0);
        File.WriteAllText(path, "{ not json");
        var (bad, p1) = StationControlViewModel.CurrentInterventions(path);
        Assert.Null(bad); Assert.NotEqual("", p1);
        var reg = new InterventionRegister();
        reg.SetCells([Cells()]);
        Assert.DoesNotContain(RegisterStore.Save(reg, path, out _, who: "test"), i => i.IsError);
        var (rows, p2) = StationControlViewModel.CurrentInterventions(path);
        Assert.Single(rows!); Assert.Equal("", p2);
    }

    [Fact]
    public void The_view_model_shows_the_register_with_Romanian_headers_for_every_column()
    {
        var vm = new InterventionRegisterViewModel(Path.Combine(_dir, "v.json"));
        Assert.Equal("Registrul intervențiilor hardware și software", vm.Title);
        Assert.Equal(InterventionRegister.ColumnNames.Length, vm.Data.Columns.Count);
        foreach (var c in InterventionRegister.ColumnNames)
            Assert.NotEqual(c, LogAnalyzer.UI.Views.ColumnLabels.For(c));
        Assert.Contains("Martor", vm.Columns);
        Assert.Contains("Executant", vm.Columns);
    }

    // ---- check S08: status matrix ---------------------------------------------------------------------------------------

    [Fact]
    public void S08_no_register_in_use_or_unreadable_or_empty_is_Nedeterminat()
    {
        var c = S08(Facts(null));
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Equal("Software", c.Area);
        var unreadable = S08(Facts(null, "registrul nu poate fi citit: acces refuzat"));
        Assert.Equal(ControlStatus.Nedeterminat, unreadable.Status);
        Assert.Contains("acces refuzat", unreadable.Detail);
        Assert.Equal(ControlStatus.Nedeterminat, S08(Facts([])).Status);
    }

    [Fact]
    public void S08_an_intervention_without_a_witness_is_Neconform()
    {
        var c = S08(Facts([Row(witness: "")]));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(c.Evidence, e => e.StartsWith("NECONFORM", StringComparison.Ordinal) && e.Contains("fără martor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Ion Popescu", "ion  popescu")]
    [InlineData("Ion Popescu", "Ion Popescu")]
    public void S08_a_witness_equal_to_the_performer_is_Neconform(string performer, string witness)
    {
        var c = S08(Facts([Row(performer: performer, witness: witness)]));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("aceeași persoană", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("hardware")]
    [InlineData("reparatie")]
    [InlineData("reparație")]
    public void S08_a_hardware_or_repair_without_the_post_check_after_the_grace_period_is_Neconform(string kind)
    {
        // 2026-11-01 is 18 days before the control; 2026-11-11 is 8 days (just past the 7-day grace)
        Assert.Equal(ControlStatus.Neconform, S08(Facts([Row(date: "2026-11-01", kind: kind)])).Status);
        var c = S08(Facts([Row(date: "2026-11-11", kind: kind)]));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("fără verificare după intervenție", StringComparison.Ordinal));
    }

    [Fact]
    public void S08_a_hardware_without_the_post_check_inside_the_grace_period_is_DeVerificat()
    {
        Assert.Equal(ControlStatus.DeVerificat, S08(Facts([Row(date: "2026-11-15", kind: "hardware")])).Status);
        Assert.Equal(ControlStatus.DeVerificat, S08(Facts([Row(date: "2026-11-12", kind: "reparatie")])).Status);   // exactly 7 days: still inside
        Assert.Equal(ControlStatus.DeVerificat, S08(Facts([Row(date: "2026-11-19", kind: "hardware")])).Status);
        // a post check with a date but no person does not count as done
        Assert.Equal(ControlStatus.Neconform, S08(Facts([Row(date: "2026-11-01", kind: "hardware", postDate: "2026-11-02")])).Status);
    }

    [Fact]
    public void S08_a_witness_failure_wins_over_a_pending_post_check()
    {
        var c = S08(Facts([Row(date: "2026-11-15", kind: "hardware"), Row(date: "2026-11-10", witness: "")]));
        Assert.Equal(ControlStatus.Neconform, c.Status);
    }

    [Fact]
    public void S08_everything_in_order_is_Conform()
    {
        var c = S08(Facts([Row(date: "2026-11-01", kind: "hardware", postDate: "2026-11-02", postBy: "Maria Ionescu"), Row(date: "2026-11-05", kind: "software"), Row(date: "2026-11-06", kind: "configurare")]));
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains("3 intervenții în perioadă", c.Detail);
    }

    [Fact]
    public void S08_no_intervention_in_the_period_is_Conform_with_the_limit_sentence_and_rows_outside_the_period_are_not_judged()
    {
        var c = S08(Facts([Row(date: "2026-01-05", kind: "hardware", witness: "")]));   // months before the period: not judged here
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains("nicio intervenție înregistrată în perioadă; absența înregistrărilor nu dovedește absența intervențiilor", c.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void S08_a_row_with_an_unreadable_date_or_an_unknown_kind_is_DeVerificat_never_Conform()
    {
        Assert.Equal(ControlStatus.DeVerificat, S08(Facts([Row(date: "ieri")])).Status);
        Assert.Equal(ControlStatus.DeVerificat, S08(Facts([Row(kind: "altceva")])).Status);
    }

    [Fact]
    public void S08_an_installation_on_a_day_of_the_offline_updates_register_is_already_documented()
    {
        var f = Facts([Row(date: "2026-11-05", kind: "software")]);
        f.Logs.Add(Msi(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)));
        f.OfflineUpdateRegister = [new LogAnalyzer.Dfir.Registers.UpdateRow { Date = "2026-11-10", Kind = "KB", Package = "pachet" }];
        Assert.Equal(ControlStatus.Conform, S08(f).Status);
    }

    [Fact]
    public void S08_a_Windows_Installer_operation_without_a_row_of_that_day_is_DeVerificat()
    {
        var f = Facts([Row(date: "2026-11-05", kind: "software")]);
        f.Logs.Add(Msi(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)));
        var c = S08(f);
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("fără intervenție în registru", StringComparison.Ordinal));
        // the same operation one day after a row (UTC against local time) is covered
        var g = Facts([Row(date: "2026-11-09", kind: "software")]);
        g.Logs.Add(Msi(new DateTimeOffset(2026, 11, 10, 0, 30, 0, TimeSpan.Zero)));
        Assert.Equal(ControlStatus.Conform, S08(g).Status);
        // with no rows in the period at all the installation is still reported
        var h = Facts([Row(date: "2026-01-05")]);
        h.Logs.Add(Msi(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)));
        Assert.Equal(ControlStatus.DeVerificat, S08(h).Status);
    }

    // ---- applicability and guide ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void S08_applies_in_every_edition_and_has_a_guide_and_a_glossary_entry(StationPosture posture)
    {
        Assert.True(ControlApplicability.Applies("S08", posture));
        Assert.True(ControlApplicability.TryGet("S08", out var entry));
        Assert.Contains("witness", entry.For(posture).Expected, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S08", CheckGuides.Ids);
        var g = CheckGuides.For("S08");
        Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 10));
        Assert.Contains("art. 277", g.Why);
        Assert.Contains("martor", g.What);
        Assert.NotNull(TermCatalog.Find("S08"));
        var f = new StationFacts { Posture = posture, IsAdministrator = true, InterventionRegister = [Row()], CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-31), PeriodEndUtc = Now };
        Assert.Equal(ControlStatus.Conform, ControlEvaluator.Evaluate(f).Checks.Single(x => x.Id == "S08").Status);
    }
}
