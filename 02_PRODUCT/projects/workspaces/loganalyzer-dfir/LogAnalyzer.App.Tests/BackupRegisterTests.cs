using System.IO;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Planner #10 item 3 (ORNISS model Anexa 21; matrix row 30): the register of backup copies, the two profile limits and the control check S09 over them. The evaluator is pure
/// (StationFacts built in memory). (These tests live here because the compiled LogAnalyzer.Dfir.Tests assembly is quarantined by Defender on the development machine.)
/// </summary>
public sealed class BackupRegisterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-backup-" + Guid.NewGuid().ToString("N"));

    public BackupRegisterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    // Date, Station, What, Medium, MediumNumber, MadeBy, StoredAt, RestoreTestDate, RestoreTestBy, Notes
    private static string[] Cells(string date = "2026-11-10", string what = "date", string madeBy = "Ion Popescu", string testDate = "", string testBy = "") =>
        [date, "1234DOC5143SV", what, "HDD extern", "M-17", madeBy, "Seif, camera 3", testDate, testBy, ""];

    private static List<(string Message, bool IsError)> Validate(string[] c) => new BackupRegister().ValidateRow(c, []);
    private static bool HasError(string[] c, string contains) => Validate(c).Any(m => m.IsError && m.Message.Contains(contains, StringComparison.Ordinal));

    private static BackupRow Row(string date = "2026-11-10", string testDate = "", string testBy = "") =>
        new() { Date = date, Station = "S1", What = "date", Medium = "HDD extern", MediumNumber = "M-17", MadeBy = "Ion Popescu", StoredAt = "Seif", RestoreTestDate = testDate, RestoreTestBy = testBy };

    private static StationFacts Facts(List<BackupRow>? rows, string problem = "", int maxAge = 0, int maxTest = 0) => new()
    {
        Posture = StationPosture.Standalone, IsAdministrator = true, BackupRegister = rows, BackupRegisterProblem = problem,
        BackupMaxAgeDays = maxAge, BackupRestoreTestMaxAgeDays = maxTest,
        CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-31), PeriodEndUtc = Now,
    };

    private static ControlCheck S09(StationFacts f) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "S09");

    // ---- validation -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_complete_row_is_valid_with_or_without_a_restore_test()
    {
        Assert.DoesNotContain(Validate(Cells()), m => m.IsError);
        Assert.DoesNotContain(Validate(Cells(testDate: "2026-11-12", testBy: "Maria Ionescu")), m => m.IsError);
        Assert.DoesNotContain(Validate(Cells(testDate: "2026-11-10", testBy: "Maria Ionescu")), m => m.IsError);   // the same day is allowed
    }

    [Fact]
    public void Date_what_and_who_are_required_and_the_date_may_not_be_in_the_future()
    {
        Assert.True(HasError(Cells(date: ""), "data copiei lipsește"));
        Assert.True(HasError(Cells(date: "10.11.2026"), "yyyy-MM-dd"));
        Assert.True(HasError(Cells(what: ""), "ce s-a copiat lipsește"));
        Assert.True(HasError(Cells(madeBy: "  "), "persoana care a făcut copia lipsește"));
        Assert.True(HasError(Cells(date: "2099-01-01"), "în viitor"));
        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.DoesNotContain(Validate(Cells(date: today.AddDays(1).ToString("yyyy-MM-dd"))), m => m.IsError);   // one day of tolerance
        Assert.True(HasError(Cells(date: today.AddDays(2).ToString("yyyy-MM-dd")), "în viitor"));
    }

    [Fact]
    public void The_restore_test_may_not_be_before_the_copy_or_in_the_future_and_its_person_is_a_warning()
    {
        Assert.True(HasError(Cells(date: "2026-11-10", testDate: "2026-11-09", testBy: "Maria Ionescu"), "nu poate fi înaintea copiei"));
        Assert.True(HasError(Cells(testDate: "11/12/2026", testBy: "Maria Ionescu"), "yyyy-MM-dd"));
        Assert.True(HasError(Cells(testDate: "2099-01-01", testBy: "Maria Ionescu"), "în viitor"));
        var w = Validate(Cells(testDate: "2026-11-12"));
        Assert.DoesNotContain(w, m => m.IsError);
        Assert.Contains(w, m => !m.IsError && m.Message.Contains("nu are persoana", StringComparison.Ordinal));
        Assert.Contains(Validate(Cells(testBy: "Maria Ionescu")), m => !m.IsError && m.Message.Contains("nu are data", StringComparison.Ordinal));
    }

    // ---- storage ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Rows_round_trip_through_the_register_file_with_an_audit_line()
    {
        var path = Path.Combine(_dir, "backups.json");
        var reg = new BackupRegister();
        reg.SetCells([Cells(testDate: "2026-11-12", testBy: "Maria Ionescu"), Cells(date: "2026-11-15", what: "sistem")]);
        var issues = RegisterStore.Save(reg, path, out var sha, who: "test");
        Assert.DoesNotContain(issues, i => i.IsError);
        Assert.True(sha.Length == 64);
        var loaded = RegisterStore.Load<BackupRegister>(path);
        Assert.False(loaded.HasErrors);
        Assert.NotNull(loaded.Register);
        Assert.Equal(reg.Cells().Count, loaded.Register!.Cells().Count);
        for (int i = 0; i < reg.Cells().Count; i++) Assert.Equal(reg.Cells()[i], loaded.Register.Cells()[i]);
        Assert.Equal("Maria Ionescu", loaded.Register.Rows[0].RestoreTestBy);
        Assert.Equal("M-17", loaded.Register.Rows[0].MediumNumber);
        Assert.Equal("backups", loaded.Register.Kind);
        Assert.Equal("backups.json", BackupRegister.DefaultFileName);
        Assert.True(File.Exists(RegisterStore.AuditPathFor(path)));
        Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, RegisterStore.VerifyAudit(path).Status);
    }

    [Fact]
    public void A_register_with_an_invalid_row_is_not_written()
    {
        var path = Path.Combine(_dir, "backups.json");
        var reg = new BackupRegister();
        reg.SetCells([Cells(madeBy: "")]);
        Assert.Contains(RegisterStore.Save(reg, path, out _, who: "test"), i => i.IsError);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Control_inputs_missing_file_is_not_in_use_and_an_unreadable_one_is_a_problem_never_an_empty_register()
    {
        var path = Path.Combine(_dir, "backups.json");
        var (none, p0) = StationControlViewModel.CurrentBackups(path);
        Assert.Null(none); Assert.Equal("", p0);
        File.WriteAllText(path, "{ not json");
        var (bad, p1) = StationControlViewModel.CurrentBackups(path);
        Assert.Null(bad); Assert.NotEqual("", p1);
        var reg = new BackupRegister();
        reg.SetCells([Cells()]);
        Assert.DoesNotContain(RegisterStore.Save(reg, path, out _, who: "test"), i => i.IsError);
        var (rows, p2) = StationControlViewModel.CurrentBackups(path);
        Assert.Single(rows!); Assert.Equal("", p2);
    }

    [Fact]
    public void The_view_model_shows_the_register_with_Romanian_headers_prints_and_has_no_station_discovery()
    {
        var vm = new BackupRegisterViewModel(Path.Combine(_dir, "v.json"));
        Assert.Equal("Registrul copiilor de siguranță", vm.Title);
        Assert.Equal(BackupRegister.ColumnNames.Length, vm.Data.Columns.Count);
        foreach (var c in BackupRegister.ColumnNames)
            Assert.NotEqual(c, LogAnalyzer.UI.Views.ColumnLabels.For(c));
        Assert.Contains("Făcută de", vm.Columns);
        Assert.False(vm.HasDiscovery);
        Assert.True(vm.CanPrint);
        Assert.Equal("REGISTRUL DE EVIDENȚĂ A COPIILOR DE SIGURANȚĂ", PrintTitleOf(vm));
        Assert.Equal("pdf-register-copii-siguranta", PrintKindOf(vm));
    }

    private static string PrintTitleOf(BackupRegisterViewModel vm) =>
        (string)typeof(BackupRegisterViewModel).GetProperty("PrintTitle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
    private static string PrintKindOf(BackupRegisterViewModel vm) =>
        (string)typeof(BackupRegisterViewModel).GetProperty("PrintAuditKind", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;

    // ---- profile limits ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Profile_limits_default_to_30_and_180_when_zero_or_absent_and_round_trip_otherwise()
    {
        var p = new ProcedureProfile();
        Assert.Equal(0, p.BackupMaxAgeDays);
        Assert.Equal(30, ProcedureProfile.EffectiveBackupMaxAgeDays(p.BackupMaxAgeDays));
        Assert.Equal(180, ProcedureProfile.EffectiveBackupRestoreTestMaxAgeDays(p.BackupRestoreTestMaxAgeDays));
        Assert.Equal(30, ProcedureProfile.DefaultBackupMaxAgeDays);
        Assert.Equal(180, ProcedureProfile.DefaultBackupRestoreTestMaxAgeDays);
        Assert.Equal(7, ProcedureProfile.EffectiveBackupMaxAgeDays(7));
        Assert.Equal(3650, ProcedureProfile.EffectiveBackupMaxAgeDays(3650));
        Assert.Equal(30, ProcedureProfile.EffectiveBackupMaxAgeDays(-5));
        Assert.Equal(180, ProcedureProfile.EffectiveBackupRestoreTestMaxAgeDays(3651));
        var path = Path.Combine(_dir, "profile.json");
        p.BackupMaxAgeDays = 14; p.BackupRestoreTestMaxAgeDays = 90;
        Assert.DoesNotContain(ProfileStore.Save(p, path, out _), i => i.IsError);
        var loaded = ProfileStore.Load(path).Profile!;
        Assert.Equal(14, loaded.BackupMaxAgeDays);
        Assert.Equal(90, loaded.BackupRestoreTestMaxAgeDays);
        // a profile written before the fields existed has neither: both are 0 and the defaults apply
        var old = Path.Combine(_dir, "old.json");
        File.WriteAllText(old, "{\"schema_version\":\"1.0\",\"Name\":\"vechi\"}");
        var oldProfile = ProfileStore.Load(old).Profile!;
        Assert.Equal(0, oldProfile.BackupMaxAgeDays);
        Assert.Equal(0, oldProfile.BackupRestoreTestMaxAgeDays);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(3650, true)]
    [InlineData(-1, false)]
    [InlineData(3651, false)]
    public void Profile_validation_accepts_zero_and_1_to_3650_for_both_limits(int days, bool valid)
    {
        var a = ProfileOps.Validate(new ProcedureProfile { BackupMaxAgeDays = days });
        var b = ProfileOps.Validate(new ProcedureProfile { BackupRestoreTestMaxAgeDays = days });
        Assert.Equal(valid, !a.Any(i => i.IsError && i.Message.Contains("copiei de siguranță")));
        Assert.Equal(valid, !b.Any(i => i.IsError && i.Message.Contains("testului de restaurare")));
    }

    // ---- check S09: status matrix --------------------------------------------------------------------------------------------

    [Fact]
    public void S09_no_register_in_use_or_unreadable_or_empty_is_Nedeterminat()
    {
        var c = S09(Facts(null));
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains("nu este folosit", c.Detail);
        var unreadable = S09(Facts(null, "registrul nu poate fi citit: acces refuzat"));
        Assert.Equal(ControlStatus.Nedeterminat, unreadable.Status);
        Assert.Contains("acces refuzat", unreadable.Detail);
        var empty = S09(Facts([]));
        Assert.Equal(ControlStatus.Nedeterminat, empty.Status);
        Assert.Contains("gol", empty.Detail);
    }

    [Fact]
    public void S09_the_age_boundary_exactly_the_limit_is_in_order_one_day_over_is_Neconform()
    {
        // control date 2026-11-19, default limit 30 days: 2026-10-20 is exactly 30 days old, 2026-10-19 is 31
        var atLimit = S09(Facts([Row("2026-10-20", "2026-11-01", "Maria Ionescu")]));
        Assert.Equal(ControlStatus.Conform, atLimit.Status);
        var over = S09(Facts([Row("2026-10-19", "2026-11-01", "Maria Ionescu")]));
        Assert.Equal(ControlStatus.Neconform, over.Status);
        Assert.Contains("31 zile", over.Detail);
        Assert.Contains(over.Evidence, e => e.Contains("2026-10-19") && e.Contains("31 zile"));
    }

    [Fact]
    public void S09_the_limit_comes_from_the_profile_and_zero_means_the_default()
    {
        var rows = new List<BackupRow> { Row("2026-11-05", "2026-11-06", "Maria Ionescu") };   // 14 days old
        Assert.Equal(ControlStatus.Conform, S09(Facts(rows)).Status);                         // default 30
        Assert.Equal(ControlStatus.Conform, S09(Facts(rows, maxAge: 14)).Status);
        Assert.Equal(ControlStatus.Neconform, S09(Facts(rows, maxAge: 13)).Status);
        Assert.Equal(ControlStatus.Conform, S09(Facts(rows, maxAge: 0)).Status);
    }

    [Fact]
    public void S09_only_the_newest_copy_counts()
    {
        var c = S09(Facts([Row("2026-05-01", "2026-05-02", "Maria Ionescu"), Row("2026-11-17", "2026-11-18", "Maria Ionescu")]));
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("2026-11-17") && e.Contains("2 zile"));
    }

    [Fact]
    public void S09_no_restore_test_ever_is_DeVerificat()
    {
        var c = S09(Facts([Row("2026-11-17")]));
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("niciun test de restaurare"));
    }

    [Fact]
    public void S09_restore_test_boundary_exactly_180_days_is_Conform_and_181_is_DeVerificat()
    {
        // 2026-11-19 minus 180 days = 2026-05-23
        Assert.Equal(ControlStatus.Conform, S09(Facts([Row("2026-11-17", "2026-05-23", "Maria Ionescu")])).Status);
        var c = S09(Facts([Row("2026-11-17", "2026-05-22", "Maria Ionescu")]));
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("2026-05-22") && e.Contains("181 zile"));
        // the limit comes from the profile; the newest test counts, an old one next to a recent one does not hurt
        Assert.Equal(ControlStatus.DeVerificat, S09(Facts([Row("2026-11-17", "2026-11-01", "Maria Ionescu")], maxTest: 17)).Status);
        Assert.Equal(ControlStatus.Conform, S09(Facts([Row("2026-11-17", "2026-11-01", "Maria Ionescu")], maxTest: 18)).Status);
        Assert.Equal(ControlStatus.Conform, S09(Facts([Row("2026-11-10", "2025-01-01", "X"), Row("2026-11-17", "2026-11-18", "Maria Ionescu")])).Status);
    }

    [Fact]
    public void S09_an_old_copy_is_Neconform_even_when_no_restore_test_exists()
    {
        Assert.Equal(ControlStatus.Neconform, S09(Facts([Row("2026-01-01")])).Status);
    }

    [Fact]
    public void S09_a_row_with_an_unreadable_or_future_date_is_never_counted_as_the_newest_copy()
    {
        var future = S09(Facts([Row("2026-01-01", "2026-01-02", "X"), Row("2099-01-01", "", "")]));
        Assert.Equal(ControlStatus.Neconform, future.Status);                               // the future row does not hide the stale real one
        var bad = S09(Facts([Row("ieri", "2026-11-18", "X")]));
        Assert.Equal(ControlStatus.DeVerificat, bad.Status);
        var mixed = S09(Facts([Row("2026-11-17", "2026-11-18", "X"), Row("ieri")]));
        Assert.Equal(ControlStatus.DeVerificat, mixed.Status);
        Assert.Contains(mixed.Evidence, e => e.Contains("rândul 2"));
        var tolerated = S09(Facts([Row("2026-11-20", "2026-11-20", "X")]));                 // one day ahead (clock offsets) is tolerated
        Assert.Equal(ControlStatus.Conform, tolerated.Status);
    }

    // ---- applicability and guide -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void S09_applies_in_every_edition_and_has_a_guide_and_a_glossary_entry(StationPosture posture)
    {
        Assert.True(ControlApplicability.Applies("S09", posture));
        Assert.True(ControlApplicability.TryGet("S09", out var entry));
        Assert.Contains("backup", entry.For(posture).Expected, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S09", CheckGuides.Ids);
        var g = CheckGuides.For("S09");
        Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 10));
        Assert.Contains("Anexa 21", g.Why);
        Assert.Contains("copie de siguranță", g.What);
        Assert.NotNull(TermCatalog.Find("S09"));
        var f = new StationFacts { Posture = posture, IsAdministrator = true, BackupRegister = [Row("2026-11-17", "2026-11-18", "X")], CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-31), PeriodEndUtc = Now };
        var c = ControlEvaluator.Evaluate(f).Checks.Single(x => x.Id == "S09");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Equal("Software", c.Area);
    }
}
