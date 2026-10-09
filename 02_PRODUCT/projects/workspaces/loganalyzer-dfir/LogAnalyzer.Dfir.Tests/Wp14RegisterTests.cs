using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP14a items 2 and 3 (owner decisions 16, 17): media and users registers - data, validation, import, store, audit chain, case snapshot.</summary>
public sealed class Wp14RegisterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-reg-" + Guid.NewGuid().ToString("N"));
    public Wp14RegisterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static MediaRegister Media(params string[][] rows)
    {
        var r = new MediaRegister();
        r.SetCells(rows);
        return r;
    }

    // RegistrationNumber, Serial, VidPid, Type, Classification, AssignedUser, Zone, ValidFrom, ValidTo, Status, Notes
    private static readonly string[] Good = ["M-001", "AA00112233", "0951:1666", "USB", "NATO SECRET", @"CORP\ion", "ROSU", "2026-01-01", "2026-12-31", "active", "stick de lucru"];

    [Fact]
    public void A_complete_row_is_valid_and_the_columns_match_the_decision_16_fields()
    {
        Assert.Equal(["RegistrationNumber", "Serial", "VidPid", "Type", "Classification", "AssignedUser", "Zone", "ValidFrom", "ValidTo", "Status", "Notes"], MediaRegister.ColumnNames);
        Assert.Empty(Media(Good).Validate());
    }

    [Fact]
    public void Validation_reports_every_problem_on_its_line_and_never_drops_a_row_silently()
    {
        var reg = Media(
            Good,
            ["", "S2", "", "USB", "", "", "", "", "", "active", ""],                        // 2: no registration number
            ["M-001", "S3", "", "USB", "", "", "", "", "", "active", ""],                   // 3: duplicate number
            ["M-004", "S4", "zz", "FLOPPY", "UNCLASSIFIED", "", "", "2026-13-01", "", "lost", ""]);   // 4: vidpid, type, level, date, status
        var issues = reg.Validate();
        Assert.Contains(issues, i => i.Line == 2 && i.Message.Contains("număr de înregistrare"));
        Assert.Contains(issues, i => i.Line == 3 && i.Message.Contains("M-001"));
        Assert.Contains(issues, i => i.Line == 4 && i.Message.Contains("VID/PID"));
        Assert.Contains(issues, i => i.Line == 4 && i.Message.Contains("FLOPPY"));
        Assert.Contains(issues, i => i.Line == 4 && i.Message.Contains("nivel") && i.Message.Contains("decizia 21"));
        Assert.Contains(issues, i => i.Line == 4 && i.Message.Contains("2026-13-01"));
        Assert.Contains(issues, i => i.Line == 4 && i.Message.Contains("lost"));
        Assert.DoesNotContain(issues, i => i.Line == 1 && i.IsError);
    }

    [Fact]
    public void A_validity_that_ends_before_it_starts_and_a_missing_serial_are_errors_except_for_optical_media_where_the_serial_is_a_warning()
    {
        var issues = Media(
            ["M-1", "S1", "", "USB", "", "", "", "2026-06-01", "2026-01-01", "active", ""],
            ["M-2", "", "", "USB", "", "", "", "", "", "active", ""],
            ["M-3", "", "", "DVD", "", "", "", "", "", "active", ""]).Validate();
        Assert.Contains(issues, i => i.Line == 1 && i.IsError && i.Message.Contains("precedă"));
        Assert.Contains(issues, i => i.Line == 2 && i.IsError && i.Message.Contains("seria"));
        Assert.Contains(issues, i => i.Line == 3 && !i.IsError && i.Message.Contains("seria"));
    }

    [Fact]
    public void Romanian_words_for_type_and_status_are_accepted_and_normalised()
    {
        var reg = Media(["M-1", "S1", "", "usb", "", "", "", "", "", "retras", ""], ["M-2", "S2", "", "DVD", "", "", "", "", "", "distrus", ""]);
        Assert.DoesNotContain(reg.Validate(), i => i.IsError);
        Assert.Equal(MediaStatus.Withdrawn, MediaRegister.ParseStatus("retras"));
        Assert.Equal(MediaStatus.Destroyed, MediaRegister.ParseStatus("distrus"));
        Assert.Equal(MediaStatus.Active, MediaRegister.ParseStatus("activ"));
        Assert.Null(MediaRegister.ParseStatus("lost"));
    }

    [Fact]
    public void Csv_import_adds_valid_rows_lists_invalid_lines_with_their_number_and_keeps_going()
    {
        var reg = new MediaRegister();
        var csv = "RegistrationNumber,Serial,VidPid,Type,Classification,AssignedUser,Zone,ValidFrom,ValidTo,Status,Notes\n" +
                  "M-1,S1,,USB,NATO SECRET,CORP\\ion,ROSU,,,active,\n" +
                  "M-2,S2,,TAPE,,,,,,active,\n" +
                  "M-3,S3,,HDD,,,,,,withdrawn,\"cu virgulă, în note\"\n";
        var r = RegisterImport.Text(reg, csv);
        Assert.Equal(2, r.RowsAccepted);
        Assert.Equal(1, r.RowsRejected);
        Assert.Contains(r.Issues, i => i.Line == 3 && i.Message.Contains("TAPE"));
        Assert.Equal(2, reg.Rows.Count);
        Assert.Equal("cu virgulă, în note", reg.Rows[1].Notes);
    }

    [Fact]
    public void Tab_separated_paste_without_a_header_is_positional_and_replace_replaces()
    {
        var reg = Media(Good);
        var r = RegisterImport.Text(reg, "M-9\tS9\t\tCD\t\t\t\t\t\tactive\t", replace: true);
        Assert.Equal(1, r.RowsAccepted);
        Assert.Equal(["M-9"], reg.Rows.Select(x => x.RegistrationNumber));
    }

    [Fact]
    public void A_pasted_row_that_duplicates_an_existing_registration_number_is_rejected()
    {
        var reg = Media(Good);
        var r = RegisterImport.Text(reg, "M-001\tS9\t\tUSB\t\t\t\t\t\tactive\t");
        Assert.Equal(0, r.RowsAccepted);
        Assert.Equal(1, r.RowsRejected);
        Assert.Single(reg.Rows);
    }

    [Fact]
    public void Json_import_refuses_corrupt_json_and_newer_major_versions_and_keeps_per_line_errors()
    {
        Assert.True(RegisterImport.FromJson<MediaRegister>("{ nu e json").HasErrors);
        Assert.True(RegisterImport.FromJson<MediaRegister>("{\"schema_version\":\"2.0\"}").HasErrors);
        var json = new MediaRegister { Rows = [new MediaRow { RegistrationNumber = "", Serial = "S", Type = "USB", Status = "active" }] };
        var back = RegisterImport.FromJson<MediaRegister>(JsonSerializer.Serialize(json, LogAnalyzer.Dfir.IO.Json.Options));
        Assert.NotNull(back.Register);
        Assert.Equal(1, back.RowsRejected);
        Assert.Contains(back.Issues, i => i.Line == 1);
    }

    [Fact]
    public void Save_load_round_trips_records_the_sha256_and_a_missing_file_is_no_register_not_an_empty_one()
    {
        var path = Path.Combine(_dir, "media.json");
        var none = RegisterStore.Load<MediaRegister>(path);
        Assert.Null(none.Register); Assert.Empty(none.Issues);

        var issues = RegisterStore.Save(Media(Good), path, out var sha, who: "tester", action: "save");
        Assert.DoesNotContain(issues, i => i.IsError);
        Assert.Equal(64, sha.Length);
        var back = RegisterStore.Load<MediaRegister>(path);
        Assert.Equal(Good, back.Register!.Cells()[0]);
        Assert.Equal("1.0", back.Register.SchemaVersion);
        Assert.Contains("\"schema_version\"", File.ReadAllText(path));

        File.WriteAllText(path, "{ not json");
        var bad = RegisterStore.Load<MediaRegister>(path);
        Assert.Null(bad.Register); Assert.True(bad.HasErrors);
    }

    [Fact]
    public void Save_with_an_invalid_row_writes_nothing_and_audits_nothing()
    {
        var path = Path.Combine(_dir, "media.json");
        var issues = RegisterStore.Save(Media(["", "S", "", "USB", "", "", "", "", "", "active", ""]), path, out var sha, who: "t", action: "save");
        Assert.Contains(issues, i => i.IsError);
        Assert.Equal("", sha);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(RegisterStore.AuditPathFor(path)));
    }

    [Fact]
    public void Every_edit_goes_to_a_hash_chained_audit_with_who_when_before_after_and_tampering_breaks_the_chain()
    {
        var path = Path.Combine(_dir, "media.json");
        RegisterStore.Save(Media(Good), path, out _, who: @"CORP\admin", action: "save");
        var edited = Media(Good); edited.Rows[0].AssignedUser = @"CORP\maria";
        RegisterStore.Save(edited, path, out _, who: @"CORP\admin", action: "save");
        RegisterStore.Save(edited, path, out _, who: @"CORP\admin", action: "save");   // unchanged: no entry

        var audit = RegisterStore.AuditPathFor(path);
        var lines = File.ReadAllLines(audit);
        Assert.Equal(2, lines.Length);
        var second = JsonDocument.Parse(lines[1]).RootElement;
        Assert.Equal("media", second.GetProperty("register").GetString());
        Assert.Equal(@"CORP\admin", second.GetProperty("who").GetString());
        Assert.True(second.TryGetProperty("whenUtc", out _));
        Assert.Equal(@"CORP\ion", second.GetProperty("before")[0][5].GetString());
        Assert.Equal(@"CORP\maria", second.GetProperty("after")[0][5].GetString());
        Assert.NotEqual(second.GetProperty("beforeSha256").GetString(), second.GetProperty("afterSha256").GetString());
        Assert.Equal(ChainStatus.Valid, RegisterStore.VerifyAudit(path).Status);

        File.WriteAllText(audit, File.ReadAllText(audit).Replace("maria", "marin"));
        Assert.Equal(ChainStatus.Broken, RegisterStore.VerifyAudit(path).Status);
    }

    [Fact]
    public void Outside_a_signed_in_session_the_audit_says_who_is_the_os_account_and_that_no_application_sign_in_was_active()
    {
        var path = Path.Combine(_dir, "media.json");
        using var _s = LogAnalyzer.Dfir.Auth.OperatorIdentity.Scope(null, authenticationRequired: false);
        RegisterStore.Save(Media(Good), path, out _, action: "save");
        var e = JsonDocument.Parse(File.ReadAllLines(RegisterStore.AuditPathFor(path))[0]).RootElement;
        Assert.Contains(Environment.UserName, e.GetProperty("who").GetString());
        Assert.Contains("nu este activă", e.GetProperty("whoSource").GetString());
    }

    // ---- users ----

    // Person, Accounts, Sids, Clearance, ClearanceValidFrom, ClearanceValidTo, NeedToKnow, ZonesAllowed
    private static readonly string[] Ion = ["Ion Popescu", @"CORP\ion;ion.local", "S-1-5-21-1-2-3-1001", "NATO SECRET", "2026-01-01", "2027-01-01", "proiect Alfa", "ROSU;PUBLIC"];

    [Fact]
    public void Users_register_columns_and_a_valid_row()
    {
        Assert.Equal(["Person", "Accounts", "Sids", "Clearance", "ClearanceValidFrom", "ClearanceValidTo", "NeedToKnow", "ZonesAllowed"], UsersRegister.ColumnNames);
        var u = new UsersRegister(); u.SetCells([Ion]);
        Assert.Empty(u.Validate());
        Assert.Equal([@"CORP\ion", "ion.local"], u.Rows[0].AccountList);
        Assert.Equal(["ROSU", "PUBLIC"], u.Rows[0].ZoneList);
    }

    [Fact]
    public void Users_validation_checks_person_account_sid_clearance_and_dates()
    {
        var u = new UsersRegister();
        u.SetCells([
            ["", @"CORP\a", "", "", "", "", "", ""],
            ["B", "", "", "", "", "", "", ""],
            ["C", @"CORP\c\x", "S-1-bad", "NATO UNCLASSIFIED", "2027-01-01", "2026-01-01", "", ""],
            ["D", "d", "", "", "", "", "", ""],
        ]);
        var issues = u.Validate();
        Assert.Contains(issues, i => i.Line == 1 && i.IsError && i.Message.Contains("persoana"));
        Assert.Contains(issues, i => i.Line == 2 && i.IsError && i.Message.Contains("cont"));
        Assert.Contains(issues, i => i.Line == 3 && i.Message.Contains("DOMENIU"));
        Assert.Contains(issues, i => i.Line == 3 && i.Message.Contains("SID"));
        Assert.Contains(issues, i => i.Line == 3 && i.Message.Contains("decizia 21"));
        Assert.Contains(issues, i => i.Line == 3 && i.Message.Contains("precedă"));
        Assert.DoesNotContain(issues, i => i.Line == 4 && i.IsError);        // no clearance is allowed: the person simply has none recorded
    }

    [Fact]
    public void Users_register_uses_the_same_import_store_and_audit_chain()
    {
        var u = new UsersRegister();
        var r = RegisterImport.Text(u, "Ion Popescu;CORP\\ion;;NATO SECRET;;;;ROSU\nFără cont;;;;;;;");
        Assert.Equal(1, r.RowsAccepted); Assert.Equal(1, r.RowsRejected);
        var path = Path.Combine(_dir, "users.json");
        RegisterStore.Save(u, path, out _, who: "admin", action: "save");
        var line = JsonDocument.Parse(File.ReadAllLines(RegisterStore.AuditPathFor(path))[0]).RootElement;
        Assert.Equal("users", line.GetProperty("register").GetString());
        Assert.Equal(ChainStatus.Valid, RegisterStore.VerifyAudit(path).Status);
    }

    [Fact]
    public void The_edit_notice_states_the_administrator_only_rule_of_decision_33()
    {
        Assert.Equal("editare permisă numai administratorului global autentificat (card + PIN; administratorul principal poate folosi și contul + parola)", UsersRegister.EditNotice);
    }

    [Fact]
    public void Empty_registers_are_not_defined()
    {
        Assert.False(new MediaRegister().IsDefined);
        Assert.False(new UsersRegister().IsDefined);
        Assert.True(Media(Good).IsDefined);
    }
}

/// <summary>WP14a item 2: a case that uses a register keeps a copy and its SHA-256 in custody.</summary>
public sealed class Wp14RegisterSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-regsnap-" + Guid.NewGuid().ToString("N"));
    public Wp14RegisterSnapshotTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void The_snapshot_is_written_into_the_case_and_its_sha256_is_in_custody_and_in_the_audit()
    {
        var ws = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "wp14", TestScopes.Valid());
        var reg = new MediaRegister();
        reg.SetCells([["M-1", "S1", "", "USB", "NATO SECRET", "", "", "", "", "active", ""]]);
        var sha = RegisterSnapshot.WriteTo(ws, reg);
        var rel = RegisterSnapshot.RelPath(reg);
        Assert.Equal("Analysis/media_register.json", rel);
        Assert.Equal(LogAnalyzer.Dfir.IO.Hashing.Sha256File(ws.FullPath(rel)), sha);
        var custody = File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement).Where(e => e.GetProperty("action").GetString() == "output.written");
        Assert.Contains(custody, e => e.GetProperty("to").GetString() == rel && e.GetProperty("sha256").GetString() == sha);
        Assert.Contains(File.ReadAllLines(ws.AuditChainPath), l => l.Contains("register.used") && l.Contains(sha) && l.Contains("media"));
        Assert.Equal(["M-1"], RegisterImport.FromJson<MediaRegister>(File.ReadAllText(ws.FullPath(rel))).Register!.Rows.Select(r => r.RegistrationNumber));
    }

    [Fact]
    public void A_register_that_is_not_defined_says_so_in_the_audit_instead_of_writing_a_snapshot()
    {
        var ws = LogAnalyzer.Dfir.Windows.Investigation.InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "wp14b", TestScopes.Valid());
        Assert.Equal("", RegisterSnapshot.WriteTo(ws, new MediaRegister()));
        Assert.False(File.Exists(ws.FullPath("Analysis/media_register.json")));
        Assert.Contains(File.ReadAllLines(ws.AuditChainPath), l => l.Contains("register.undefined") && l.Contains("registru nedefinit"));
    }
}
