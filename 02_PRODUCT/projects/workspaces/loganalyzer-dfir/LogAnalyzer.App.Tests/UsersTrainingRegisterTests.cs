using System.IO;
using System.Text.Json;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Planner #10 item 4 (HG 585 art. 35, 274(2), 278; matrix rows 2 and 31): the training and pledge columns of the users register (schema 1.1), the profile limit
/// TrainingMaxAgeDays and the control check U07 over local accounts, the register and the limit. The evaluator is pure (StationFacts built in memory).
/// (These tests live here because the compiled LogAnalyzer.Dfir.Tests assembly is quarantined by Defender on the development machine.)
/// </summary>
public sealed class UsersTrainingRegisterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-userstraining-" + Guid.NewGuid().ToString("N"));

    public UsersTrainingRegisterTests() { Directory.CreateDirectory(_dir); LogAnalyzer.Dfir.Language.LanguageLevelContext.ResetForTests(); }
    public void Dispose() { LogAnalyzer.Dfir.Language.LanguageLevelContext.ResetForTests(); try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);   // control date 2026-11-19

    // ---- helpers ------------------------------------------------------------------------------------------------------------

    // Person, Accounts, Sids, Clearance, ClearanceValidFrom, ClearanceValidTo, NeedToKnow, ZonesAllowed, LastTraining, PledgeSigned
    private static string[] Cells(string training = "2026-06-01", string pledge = "2026-01-10", string clearance = "", string validTo = "") =>
        ["Ion Popescu", @"PC\ion", "", clearance, "", validTo, "", "", training, pledge];

    private static List<(string Message, bool IsError)> Validate(string[] c) => new UsersRegister().ValidateRow(c, []);
    private static bool HasError(string[] c, string contains) => Validate(c).Any(m => m.IsError && m.Message.Contains(contains, StringComparison.Ordinal));

    private static UserRow Row(string person = "Ion Popescu", string accounts = @"PC\ion", string sids = "", string training = "2026-06-01", string pledge = "2026-01-10", string clearance = "NATO SECRET", string validTo = "", string validFrom = "") =>
        new() { ClearanceValidFrom = validFrom, Person = person, Accounts = accounts, Sids = sids, Clearance = clearance, ClearanceValidTo = validTo, LastTraining = training, PledgeSigned = pledge };

    private static UsersRegister Reg(params UserRow[] rows) => new() { Rows = [.. rows] };

    private static LocalAccount Acc(string name, string rid = "1001", bool disabled = false) =>
        new(name, $"S-1-5-21-1-2-3-{rid}", disabled, true, true, false, null, false, false, "");

    private static StationFacts Facts(UsersRegister? reg, string problem = "", int maxTraining = 0, List<LocalAccount>? accounts = null, EvidenceGap? gap = null, StationPosture posture = StationPosture.Standalone)
    {
        var f = new StationFacts
        {
            Posture = posture, IsAdministrator = true, UsersRegister = reg, UsersRegisterProblem = problem, TrainingMaxAgeDays = maxTraining,
            CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-31), PeriodEndUtc = Now,
        };
        f.Accounts.AddRange(accounts ?? [Acc("ion")]);
        if (gap is not null) f.Gaps.Add(gap);
        return f;
    }

    private static ControlCheck U07(StationFacts f) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == "U07");

    private static string RepoFile(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "docs", "dfir", "EDITIONS_AND_POSTURE.md"))) return Path.Combine([d.FullName, .. parts]);
        throw new DirectoryNotFoundException("docs/dfir/EDITIONS_AND_POSTURE.md");
    }

    // ---- schema, columns, storage -------------------------------------------------------------------------------------------

    [Fact]
    public void The_two_new_columns_are_the_last_and_the_schema_is_1_1()
    {
        Assert.Equal(10, UsersRegister.ColumnNames.Length);
        Assert.Equal(["Person", "Accounts", "Sids", "Clearance", "ClearanceValidFrom", "ClearanceValidTo", "NeedToKnow", "ZonesAllowed", "LastTraining", "PledgeSigned"], UsersRegister.ColumnNames);
        Assert.Equal("1.1", UsersRegister.CurrentSchemaVersion);
        Assert.Equal("1.1", new UsersRegister().SchemaVersion);
        var u = new UsersRegister(); u.SetCells([Cells()]);
        Assert.Equal(10, u.Cells()[0].Length);
        Assert.Equal("2026-06-01", u.Rows[0].LastTraining);
        Assert.Equal("2026-01-10", u.Rows[0].PledgeSigned);
        Assert.Equal(u.Cells()[0], Cells());
    }

    [Fact]
    public void A_1_0_file_loads_with_blank_new_cells_and_is_written_as_1_1_on_the_next_change()
    {
        var path = Path.Combine(_dir, "users.json");
        File.WriteAllText(path, """
            {
              "schema_version": "1.0",
              "rows": [
                { "person": "Ion Popescu", "accounts": "PC\\ion", "sids": "", "clearance": "NATO SECRET", "clearanceValidFrom": "2026-01-01", "clearanceValidTo": "2027-01-01", "needToKnow": "Alfa", "zonesAllowed": "ROSU" }
              ]
            }
            """);
        var loaded = RegisterStore.Load<UsersRegister>(path);
        Assert.False(loaded.HasErrors);
        Assert.Empty(loaded.Issues);
        Assert.Equal("1.0", loaded.Register!.SchemaVersion);
        var row = loaded.Register.Cells()[0];
        Assert.Equal(10, row.Length);
        Assert.Equal("NATO SECRET", row[3]);
        Assert.Equal("", row[8]);
        Assert.Equal("", row[9]);

        // saved unchanged: nothing is written and nothing audited (existing contract), the file stays what it was
        Assert.DoesNotContain(RegisterStore.Save(loaded.Register, path, out _, who: "test"), i => i.IsError);
        Assert.Contains("\"1.0\"", File.ReadAllText(path));
        Assert.False(File.Exists(RegisterStore.AuditPathFor(path)));

        // a change: written as 1.1 with the new columns, one audit line, chain valid
        loaded.Register.Rows[0].LastTraining = "2026-09-01";
        Assert.DoesNotContain(RegisterStore.Save(loaded.Register, path, out _, who: "test"), i => i.IsError);
        var text = File.ReadAllText(path);
        Assert.Contains("\"schema_version\": \"1.1\"", text);
        Assert.Contains("\"lastTraining\": \"2026-09-01\"", text);
        Assert.Contains("\"pledgeSigned\": \"\"", text);
        var back = RegisterStore.Load<UsersRegister>(path);
        Assert.Equal("1.1", back.Register!.SchemaVersion);
        Assert.Equal(loaded.Register.Cells()[0], back.Register.Cells()[0]);
        Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, RegisterStore.VerifyAudit(path).Status);
        var audit = JsonDocument.Parse(File.ReadAllLines(RegisterStore.AuditPathFor(path))[0]).RootElement;
        Assert.Equal(10, audit.GetProperty("before")[0].GetArrayLength());
        Assert.Equal("", audit.GetProperty("before")[0][8].GetString());
        Assert.Equal("2026-09-01", audit.GetProperty("after")[0][8].GetString());
    }

    [Fact]
    public void Other_registers_keep_schema_1_0_when_saved()
    {
        var path = Path.Combine(_dir, "backups.json");
        var b = new BackupRegister(); b.SetCells([["2026-11-10", "S1", "date", "HDD", "M-1", "Ion", "Seif", "", "", ""]]);
        Assert.DoesNotContain(RegisterStore.Save(b, path, out _, who: "test"), i => i.IsError);
        Assert.Equal("1.0", RegisterStore.Load<BackupRegister>(path).Register!.SchemaVersion);
    }

    [Fact]
    public void A_newer_major_version_is_still_refused()
    {
        var path = Path.Combine(_dir, "v2.json");
        File.WriteAllText(path, "{\"schema_version\":\"2.0\",\"rows\":[]}");
        var r = RegisterStore.Load<UsersRegister>(path);
        Assert.Null(r.Register); Assert.True(r.HasErrors);
    }

    // ---- CSV import / paste -------------------------------------------------------------------------------------------------

    [Fact]
    public void An_8_column_csv_without_header_imports_with_blank_new_cells()
    {
        var u = new UsersRegister();
        var r = RegisterImport.Text(u, "Ion Popescu;PC\\ion;;NATO SECRET;2026-01-01;2027-01-01;Alfa;ROSU");
        Assert.Equal(1, r.RowsAccepted); Assert.Equal(0, r.RowsRejected);
        Assert.DoesNotContain(r.Issues, i => i.IsError);
        Assert.Equal("NATO SECRET", u.Rows[0].Clearance);
        Assert.Equal("", u.Rows[0].LastTraining);
        Assert.Equal("", u.Rows[0].PledgeSigned);
        Assert.Equal(10, u.Cells()[0].Length);
    }

    [Fact]
    public void An_8_column_csv_with_the_old_header_imports_by_name_and_leaves_the_new_cells_blank()
    {
        var u = new UsersRegister();
        var csv = "Person;Accounts;Sids;Clearance;ClearanceValidFrom;ClearanceValidTo;NeedToKnow;ZonesAllowed\nIon Popescu;PC\\ion;;;;;;";
        var r = RegisterImport.Text(u, csv);
        Assert.Equal(1, r.RowsAccepted); Assert.Equal(0, r.RowsRejected);
        Assert.Equal("Ion Popescu", u.Rows[0].Person);
        Assert.Equal("", u.Rows[0].LastTraining);
        Assert.Equal("", u.Rows[0].PledgeSigned);
    }

    [Fact]
    public void A_10_column_csv_carries_the_dates_and_a_bad_date_rejects_only_its_row()
    {
        var u = new UsersRegister();
        var csv = "Ion Popescu;PC\\ion;;;;;;;2026-06-01;2026-01-10\nMaria Ionescu;PC\\maria;;;;;;;ieri;2026-01-10";
        var r = RegisterImport.Text(u, csv);
        Assert.Equal(1, r.RowsAccepted); Assert.Equal(1, r.RowsRejected);
        Assert.Equal("2026-06-01", u.Rows[0].LastTraining);
        Assert.Equal("2026-01-10", u.Rows[0].PledgeSigned);
        Assert.Contains(r.Issues, i => i.IsError && i.Message.Contains("yyyy-MM-dd") && i.Message.Contains("ultima instruire"));
    }

    // ---- validation ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Both_dates_are_optional_and_validated_like_the_other_dates()
    {
        Assert.DoesNotContain(Validate(Cells()), m => m.IsError);
        Assert.DoesNotContain(Validate(Cells(training: "", pledge: "")), m => m.IsError);
        Assert.True(HasError(Cells(training: "01.06.2026"), "yyyy-MM-dd"));
        Assert.True(HasError(Cells(pledge: "mâine"), "yyyy-MM-dd"));
        // a future date is a warning, not an error: a station clock that is behind must not block unrelated edits
        Assert.DoesNotContain(Validate(Cells(training: "2099-01-01")), m => m.IsError);
        Assert.Contains(Validate(Cells(training: "2099-01-01")), m => !m.IsError && m.Message.Contains("în viitor"));
        Assert.Contains(Validate(Cells(pledge: "2099-01-01")), m => !m.IsError && m.Message.Contains("în viitor"));
        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.DoesNotContain(Validate(Cells(training: today.AddDays(1).ToString("yyyy-MM-dd"))), m => m.Message.Contains("în viitor"));                                  // one day of tolerance: no message
        Assert.Contains(Validate(Cells(training: today.AddDays(2).ToString("yyyy-MM-dd"))), m => !m.IsError && m.Message.Contains("în viitor"));
        Assert.DoesNotContain(Validate(Cells(pledge: today.ToString("yyyy-MM-dd"))), m => m.IsError);
    }

    [Fact]
    public void A_row_of_the_old_8_cell_shape_still_validates()
    {
        string[] old = ["Ion Popescu", @"PC\ion", "", "NATO SECRET", "2026-01-01", "2027-01-01", "Alfa", "ROSU"];
        Assert.DoesNotContain(Validate(old), m => m.IsError);
        Assert.Contains(new UsersRegister().ValidateRow(["", "", "", "", "", "", "", ""], []), m => m.IsError);   // the person is still required
    }

    [Fact]
    public void The_users_register_view_model_shows_ten_columns_with_Romanian_headers_and_the_hint_names_them()
    {
        var vm = new UsersRegisterViewModel(Path.Combine(_dir, "v.json"));
        Assert.Equal(10, vm.Data.Columns.Count);
        Assert.Equal("Ultima instruire", LogAnalyzer.UI.Views.ColumnLabels.For("LastTraining"));
        Assert.Equal("Angajament semnat", LogAnalyzer.UI.Views.ColumnLabels.For("PledgeSigned"));
        Assert.Contains("Ultima instruire", vm.Hint);
        Assert.Contains("Angajament semnat", vm.Hint);
        Assert.DoesNotContain("LastTraining", vm.Hint);
        LogAnalyzer.Dfir.Language.LanguageLevelContext.Set(LogAnalyzer.Dfir.Language.UiLanguageLevel.Expert);
        Assert.Contains("LastTraining", vm.Hint);
    }

    // ---- profile limit ------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_training_limit_defaults_to_365_when_zero_or_absent_and_round_trips_otherwise()
    {
        var p = new ProcedureProfile();
        Assert.Equal(0, p.TrainingMaxAgeDays);
        Assert.Equal(365, ProcedureProfile.DefaultTrainingMaxAgeDays);
        Assert.Equal(365, ProcedureProfile.EffectiveTrainingMaxAgeDays(0));
        Assert.Equal(30, ProcedureProfile.EffectiveTrainingMaxAgeDays(30));
        Assert.Equal(3650, ProcedureProfile.EffectiveTrainingMaxAgeDays(3650));
        Assert.Equal(365, ProcedureProfile.EffectiveTrainingMaxAgeDays(-1));
        Assert.Equal(365, ProcedureProfile.EffectiveTrainingMaxAgeDays(3651));
        var path = Path.Combine(_dir, "profile.json");
        p.TrainingMaxAgeDays = 180;
        Assert.DoesNotContain(ProfileStore.Save(p, path, out _), i => i.IsError);
        Assert.Equal(180, ProfileStore.Load(path).Profile!.TrainingMaxAgeDays);
        var old = Path.Combine(_dir, "old.json");
        File.WriteAllText(old, "{\"schema_version\":\"1.0\",\"Name\":\"vechi\"}");
        Assert.Equal(0, ProfileStore.Load(old).Profile!.TrainingMaxAgeDays);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(3650, true)]
    [InlineData(-1, false)]
    [InlineData(3651, false)]
    public void Profile_validation_accepts_zero_and_1_to_3650_for_the_training_limit(int days, bool valid)
    {
        var issues = ProfileOps.Validate(new ProcedureProfile { TrainingMaxAgeDays = days });
        Assert.Equal(valid, !issues.Any(i => i.IsError && i.Message.Contains("instruirii")));
    }

    [Fact]
    public void The_profile_page_has_a_field_for_the_training_limit_next_to_the_backup_ones()
    {
        var xaml = File.ReadAllText(RepoFile("LogAnalyzer.App", "Views", "ProcedureProfileView.xaml"));
        var backup = xaml.IndexOf("BackupRestoreTestMaxAgeDaysText", StringComparison.Ordinal);
        var training = xaml.IndexOf("TrainingMaxAgeDaysText", StringComparison.Ordinal);
        Assert.True(backup > 0 && training > backup);
        Assert.Contains("TrainingLimitHelp", xaml);
        Assert.Contains("(U07)", ProcedureProfileViewModel.TrainingLimitHelp);
    }

    // ---- check U07: status matrix --------------------------------------------------------------------------------------------

    [Fact]
    public void U07_no_register_in_use_or_unreadable_or_empty_is_Nedeterminat()
    {
        var none = U07(Facts(null));
        Assert.Equal(ControlStatus.Nedeterminat, none.Status);
        Assert.Contains("nu este folosit", none.Detail);
        var unreadable = U07(Facts(null, "registrul nu poate fi citit: acces refuzat"));
        Assert.Equal(ControlStatus.Nedeterminat, unreadable.Status);
        Assert.Contains("acces refuzat", unreadable.Detail);
        var empty = U07(Facts(Reg()));
        Assert.Equal(ControlStatus.Nedeterminat, empty.Status);
        Assert.Contains("gol", empty.Detail);
        Assert.Equal("Utilizatori", empty.Area);
    }

    [Fact]
    public void U07_an_enabled_local_account_with_no_register_row_is_Neconform()
    {
        var c = U07(Facts(Reg(Row()), accounts: [Acc("ion"), Acc("maria", "1002")]));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("maria") && e.Contains("fără niciun rând"));
        Assert.DoesNotContain(c.Evidence, e => e.Contains("NECONFORM") && e.Contains("cont ion"));
    }

    [Fact]
    public void U07_accounts_are_matched_like_Wp14_by_account_name_with_or_without_domain_or_by_SID()
    {
        // register says PC\ion, the local account is "ion"
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row(accounts: @"PC\ion")))).Status);
        // register says plain "ion", case does not matter
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row(accounts: "ION")))).Status);
        // a list of accounts: the account is one of them
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row(accounts: @"CORP\x; ion")))).Status);
        // another person's account does not cover this one
        Assert.Equal(ControlStatus.Neconform, U07(Facts(Reg(Row(accounts: @"PC\ioana")))).Status);
        // by SID when the names differ
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row(accounts: "altnume", sids: "S-1-5-21-1-2-3-1001")))).Status);
        Assert.Equal(ControlStatus.Neconform, U07(Facts(Reg(Row(accounts: "altnume", sids: "S-1-5-21-1-2-3-9999")))).Status);
    }

    [Fact]
    public void U07_disabled_built_in_and_system_accounts_need_no_row()
    {
        var accounts = new List<LocalAccount>
        {
            Acc("ion"),
            Acc("fost", "1005", disabled: true),
            Acc("Administrator", "500", disabled: true), Acc("Guest", "501"), Acc("krbtgt", "502"), Acc("DefaultAccount", "503"), Acc("WDAGUtilityAccount", "504"),
            Acc("SYSTEM", "18"), Acc("PC01$", "1100"),
        };
        var c = U07(Facts(Reg(Row()), accounts: accounts));
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.DoesNotContain(c.Evidence, e => e.Contains("fost") || e.Contains("Administrator") || e.Contains("Guest"));
    }

    [Fact]
    public void U07_a_complete_row_for_every_enabled_account_is_Conform_with_one_evidence_line_per_account()
    {
        var reg = Reg(Row(), Row("Maria Ionescu", @"PC\maria", training: "2026-10-01", pledge: "2026-02-01"));
        var c = U07(Facts(reg, accounts: [Acc("ion"), Acc("maria", "1002")]));
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Equal(2, c.Evidence.Count);
        Assert.Contains(c.Evidence, e => e.Contains("cont ion") && e.Contains("Ion Popescu") && e.Contains("instruire 2026-06-01") && e.Contains("angajament 2026-01-10"));
        Assert.Contains(c.Evidence, e => e.Contains("cont maria") && e.Contains("instruire 2026-10-01"));
    }

    [Fact]
    public void U07_a_missing_training_or_a_missing_pledge_is_DeVerificat()
    {
        var noTraining = U07(Facts(Reg(Row(training: ""))));
        Assert.Equal(ControlStatus.DeVerificat, noTraining.Status);
        Assert.Contains(noTraining.Evidence, e => e.Contains("DE VERIFICAT") && e.Contains("instruire"));
        var noPledge = U07(Facts(Reg(Row(pledge: ""))));
        Assert.Equal(ControlStatus.DeVerificat, noPledge.Status);
        Assert.Contains(noPledge.Evidence, e => e.Contains("angajamentului"));
        // a 1.0 register (both blank) is DE VERIFICAT, never CONFORM
        Assert.Equal(ControlStatus.DeVerificat, U07(Facts(Reg(Row(training: "", pledge: "")))).Status);
    }

    [Fact]
    public void U07_training_boundary_exactly_the_limit_is_in_order_one_day_over_is_DeVerificat()
    {
        // control date 2026-11-19, default 365 days: 2025-11-19 is exactly 365 days old, 2025-11-18 is 366
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row(training: "2025-11-19")))).Status);
        var over = U07(Facts(Reg(Row(training: "2025-11-18"))));
        Assert.Equal(ControlStatus.DeVerificat, over.Status);
        Assert.Contains(over.Evidence, e => e.Contains("este de 366 zile"));
        Assert.Contains(over.Evidence, e => e.Contains("instruire 2025-11-18") && e.Contains("366 zile"));
    }

    [Fact]
    public void U07_the_limit_comes_from_the_profile_and_zero_means_the_default()
    {
        var reg = Reg(Row(training: "2026-10-20"));   // 30 days old
        Assert.Equal(ControlStatus.Conform, U07(Facts(reg)).Status);                  // default 365
        Assert.Equal(ControlStatus.Conform, U07(Facts(reg, maxTraining: 30)).Status);
        Assert.Equal(ControlStatus.DeVerificat, U07(Facts(reg, maxTraining: 29)).Status);
        Assert.Equal(ControlStatus.Conform, U07(Facts(reg, maxTraining: 0)).Status);
    }

    [Fact]
    public void U07_the_newest_training_across_the_rows_of_a_person_counts()
    {
        // two clearances of the same person: the older row's training is old, the other is recent
        var reg = Reg(Row(training: "2020-01-01"), Row(training: "2026-09-01", clearance: "NATO SECRET", validTo: "2027-01-01"));
        Assert.Equal(ControlStatus.Conform, U07(Facts(reg)).Status);
    }

    [Fact]
    public void U07_unreadable_or_future_dates_never_count()
    {
        var future = U07(Facts(Reg(Row(training: "2099-01-01"))));
        Assert.Equal(ControlStatus.DeVerificat, future.Status);
        Assert.Contains(future.Evidence, e => e.Contains("în viitor"));
        var bad = U07(Facts(Reg(Row(pledge: "ieri"))));
        Assert.Equal(ControlStatus.DeVerificat, bad.Status);
        Assert.Contains(bad.Evidence, e => e.Contains("nu este yyyy-MM-dd"));
        // one day ahead (clock offsets) is tolerated
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row(training: "2026-11-20", pledge: "2026-11-20")))).Status);
    }

    [Fact]
    public void U07_an_account_with_no_valid_clearance_at_the_date_is_Neconform_and_the_last_valid_day_is_still_valid()
    {
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row(clearance: "NATO SECRET", validTo: "2026-11-19")))).Status);
        var expired = U07(Facts(Reg(Row(clearance: "NATO SECRET", validTo: "2026-11-18"))));
        Assert.Equal(ControlStatus.Neconform, expired.Status);
        Assert.Contains(expired.Evidence, e => e.Contains("NECONFORM") && e.Contains("cont ion") && e.Contains("expirată la 2026-11-18"));
        // not yet valid is no valid clearance either; valid from today is valid
        Assert.Equal(ControlStatus.Neconform, U07(Facts(Reg(Row(validFrom: "2026-11-20")))).Status);
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row(validFrom: "2026-11-19")))).Status);
        // a clearance end date that cannot be read is DE VERIFICAT
        Assert.Equal(ControlStatus.DeVerificat, U07(Facts(Reg(Row(clearance: "NATO SECRET", validTo: "curând")))).Status);
    }

    [Fact]
    public void U07_a_renewal_is_a_new_row_and_the_expired_history_row_does_not_make_it_Neconform()
    {
        var c = U07(Facts(Reg(Row(clearance: "NATO SECRET", validTo: "2025-12-31"), Row(clearance: "NATO SECRET", validFrom: "2026-01-01", validTo: "2027-12-31"))));
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.DoesNotContain(c.Evidence, e => e.StartsWith("NECONFORM"));
    }

    [Fact]
    public void U07_rows_without_any_clearance_for_an_enabled_account_are_DeVerificat_not_approved()
    {
        var c = U07(Facts(Reg(Row(clearance: ""))));
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains("fără abilitare înregistrată"));
    }

    [Fact]
    public void U07_expired_rows_of_a_disabled_or_missing_account_are_only_listed()
    {
        var reg = Reg(Row(), Row("Fost Coleg", @"PC\fost", clearance: "NATO SECRET", validTo: "2025-01-01"), Row("Plecat", @"PC\plecat", clearance: "NATO SECRET", validTo: "2025-01-01"));
        var c = U07(Facts(reg, accounts: [Acc("ion"), Acc("fost", "1005", disabled: true)]));
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains(c.Evidence, e => e.StartsWith("istoric") && e.Contains("Fost Coleg"));
        Assert.Contains(c.Evidence, e => e.StartsWith("istoric") && e.Contains("Plecat"));
    }

    [Fact]
    public void U07_an_enabled_built_in_Administrator_is_compared_a_disabled_one_is_not()
    {
        var enabled = U07(Facts(Reg(Row()), accounts: [Acc("ion"), Acc("Administrator", "500")]));
        Assert.Equal(ControlStatus.Neconform, enabled.Status);
        Assert.Contains(enabled.Evidence, e => e.Contains("cont Administrator") && e.Contains("fără niciun rând"));
        Assert.Equal(ControlStatus.Conform, U07(Facts(Reg(Row()), accounts: [Acc("ion"), Acc("Administrator", "500", disabled: true)])).Status);
    }

    [Fact]
    public void U07_Neconform_wins_over_DeVerificat()
    {
        var c = U07(Facts(Reg(Row(training: "", pledge: "")), accounts: [Acc("ion"), Acc("maria", "1002")]));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains(c.Evidence, e => e.StartsWith("DE VERIFICAT"));
        Assert.Contains(c.Evidence, e => e.StartsWith("NECONFORM"));
    }

    [Fact]
    public void U07_without_the_local_accounts_the_account_part_is_Nedeterminat_with_the_gap_text()
    {
        var unread = U07(Facts(Reg(Row()), accounts: []));
        Assert.Equal(ControlStatus.Nedeterminat, unread.Status);
        Assert.Contains("nu a putut fi citită", unread.Detail);
        var gap = new EvidenceGap("Conturi locale", EvidenceStatus.NotAvailable, "enumerarea s-a oprit", "impact", "sursa", "cum");
        var partial = U07(Facts(Reg(Row()), gap: gap));
        Assert.Equal(ControlStatus.Nedeterminat, partial.Status);
        Assert.Contains("incompletă (enumerarea s-a oprit)", partial.Detail);
        // no NECONFORM from clearances alone when the accounts are unknown; the register part is still listed
        var expired = U07(Facts(Reg(Row(clearance: "NATO SECRET", validTo: "2026-01-01")), accounts: []));
        Assert.Equal(ControlStatus.Nedeterminat, expired.Status);
        var oldTraining = U07(Facts(Reg(Row(training: "2020-01-01")), accounts: []));
        Assert.Equal(ControlStatus.Nedeterminat, oldTraining.Status);
        Assert.Contains(oldTraining.Evidence, e => e.StartsWith("DE VERIFICAT") && e.Contains("persoana Ion Popescu"));
    }

    [Fact]
    public void U07_with_no_local_person_account_to_compare_is_never_Conform_and_still_checks_training_and_pledge_of_the_register()
    {
        var onlyBuiltIn = new List<LocalAccount> { Acc("Administrator", "500", disabled: true), Acc("Guest", "501"), Acc("fost", "1005", disabled: true) };
        var ok = U07(Facts(Reg(Row()), accounts: onlyBuiltIn));
        Assert.Equal(ControlStatus.DeVerificat, ok.Status);
        Assert.Contains("niciun cont local de persoană", ok.Detail);
        Assert.Contains(ok.Evidence, e => e.Contains("Niciun cont local activ"));
        Assert.Contains(ok.Evidence, e => e.StartsWith("în regulă") && e.Contains("persoana Ion Popescu"));
        // a person with a valid clearance, no local account (domain user), old training and no pledge: listed
        var domain = U07(Facts(Reg(Row("Ana Dom", @"DOM\ana", training: "2020-01-01", pledge: "")), accounts: onlyBuiltIn));
        Assert.Equal(ControlStatus.DeVerificat, domain.Status);
        Assert.Contains(domain.Evidence, e => e.StartsWith("DE VERIFICAT") && e.Contains("persoana Ana Dom") && e.Contains("ultima instruire"));
        Assert.Contains(domain.Evidence, e => e.Contains("angajamentului"));
    }

    // ---- downgrade guard ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_of_a_newer_major_schema_is_not_overwritten_by_a_save()
    {
        // review r2: Minor("2.0") = 0 passed the minor-only guard; Load refuses 2.x, the page showed an empty register and a save overwrote every row
        var path = Path.Combine(_dir, "users.json");
        File.WriteAllText(path, "{\"schema_version\":\"2.0\",\"rows\":[]}");
        var before = File.ReadAllBytes(path);
        var reg = new UsersRegister(); reg.SetCells([Cells()]);
        var refused = RegisterStore.Save(reg, path, out var sha, who: "test");
        Assert.Contains(refused, i => i.IsError && i.Message.Contains("schema 2.0"));
        Assert.Equal("", sha);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_writer_that_knows_an_older_schema_does_not_overwrite_a_newer_file()
    {
        var path = Path.Combine(_dir, "users.json");
        var reg = new UsersRegister(); reg.SetCells([Cells()]);
        Assert.DoesNotContain(RegisterStore.Save(reg, path, out _, who: "test"), i => i.IsError);
        Assert.Contains("\"1.1\"", File.ReadAllText(path));
        var before = File.ReadAllBytes(path);
        reg.Rows[0].LastTraining = "2026-10-01";
        var refused = RegisterStore.Save(reg, path, out var sha, who: "test", writerSchema: "1.0");
        var issue = Assert.Single(refused, i => i.IsError);
        Assert.Contains("versiune mai nouă a aplicației (schema 1.1); nu se suprascrie, ca să nu se piardă coloane.", issue.Message);
        Assert.Equal("", sha);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(File.ReadAllLines(RegisterStore.AuditPathFor(path)));
        // the same build writes it as before; the version map is per type
        Assert.Equal("1.1", RegisterStore.SchemaWrittenBy(typeof(UsersRegister)));
        Assert.Equal("1.0", RegisterStore.SchemaWrittenBy(typeof(BackupRegister)));
        Assert.DoesNotContain(RegisterStore.Save(reg, path, out _, who: "test"), i => i.IsError);
        Assert.Contains("2026-10-01", File.ReadAllText(path));
    }

    // ---- control inputs ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Control_inputs_missing_file_is_not_in_use_and_an_unreadable_one_is_a_problem_never_an_empty_register()
    {
        var path = Path.Combine(_dir, "users.json");
        var (none, p0) = StationControlViewModel.CurrentUsers(path);
        Assert.Null(none); Assert.Equal("", p0);
        File.WriteAllText(path, "{ not json");
        var (bad, p1) = StationControlViewModel.CurrentUsers(path);
        Assert.Null(bad); Assert.NotEqual("", p1);
        var reg = new UsersRegister(); reg.SetCells([Cells()]);
        Assert.DoesNotContain(RegisterStore.Save(reg, path, out _, who: "test"), i => i.IsError);
        var (ok, p2) = StationControlViewModel.CurrentUsers(path);
        Assert.Equal("", p2);
        Assert.Equal("2026-06-01", ok!.Rows[0].LastTraining);
    }

    // ---- applicability, guide, documents ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void U07_applies_in_every_edition_and_has_a_guide_and_a_glossary_entry(StationPosture posture)
    {
        Assert.True(ControlApplicability.Applies("U07", posture));
        Assert.True(ControlApplicability.TryGet("U07", out var entry));
        Assert.Contains("users register", entry.For(posture).Expected, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("U07", CheckGuides.Ids);
        var g = CheckGuides.For("U07");
        Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 10));
        Assert.Contains("Registrul utilizatori", g.What);
        Assert.Contains("art. 35", g.Why);
        Assert.Contains("Vechimea maximă a instruirii", g.How);
        Assert.NotNull(TermCatalog.Find("U07"));
        var f = Facts(Reg(Row()), posture: posture);
        var c = ControlEvaluator.Evaluate(f).Checks.Single(x => x.Id == "U07");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Equal("Utilizatori", c.Area);
    }

    [Fact]
    public void The_applicability_row_of_U07_equals_the_document_row_and_the_annex_guide_and_matrix_name_U07()
    {
        Assert.True(ControlApplicability.TryGet("U07", out var e));
        var doc = File.ReadAllLines(RepoFile("docs", "dfir", "EDITIONS_AND_POSTURE.md"));
        var row = Assert.Single(doc, l => l.StartsWith("| U07 |", StringComparison.Ordinal)).Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray();
        Assert.Equal(e.Name, row[1]);
        Assert.Equal(e.Standalone.Expected, row[2]);
        Assert.Equal(e.IsolatedNetwork.Expected, row[3]);
        Assert.Equal(e.Internet.Expected, row[4]);
        Assert.Equal(e.Reason ?? "", row[5]);
        // placed with the U group, between U06 and D01
        var ids = doc.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\| [ACDNPSU][0-9]{2} \|")).Select(l => l[2..5]).ToList();
        Assert.Equal(ids.IndexOf("U06") + 1, ids.IndexOf("U07"));

        var annex = File.ReadAllText(RepoFile("docs", "utilizare", "ANEXA_VERIFICARI_SI_CODURI.md")).ReplaceLineEndings("\n");
        var g = CheckGuides.For("U07");
        Assert.Contains($"### U07\n\n- **Ce verificăm:** {g.What}\n- **De ce contează:** {g.Why}\n- **Cum verifică aplicația:** {g.How}\n- **Sursa tehnică:** `{g.Source}`\n- **Cum se citește rezultatul:** {g.Meaning}\n", annex);
        Assert.True(annex.IndexOf("### U06", StringComparison.Ordinal) < annex.IndexOf("### U07", StringComparison.Ordinal));
        Assert.True(annex.IndexOf("### U07", StringComparison.Ordinal) < annex.IndexOf("### D01", StringComparison.Ordinal));

        var matrix = File.ReadAllLines(RepoFile("docs", "dfir", "MATRICE_HG585_ORNISS_2026-10-23.md"));
        var r2 = Assert.Single(matrix, l => l.StartsWith("| 2 | ", StringComparison.Ordinal));
        var r31 = Assert.Single(matrix, l => l.StartsWith("| 31 | ", StringComparison.Ordinal));
        foreach (var r in new[] { r2, r31 }) { Assert.Contains("U07", r); Assert.Contains("| PARȚIAL |", r); Assert.DoesNotContain("LIPSĂ", r); }
        Assert.Contains("(108 verificări automate", string.Join("\n", matrix.Take(6)));
        Assert.DoesNotContain("107 verificări", string.Join("\n", matrix));
        Assert.Contains("U07", File.ReadAllText(RepoFile("docs", "utilizare", "GHID_DE_UTILIZARE.md")));
    }
}
