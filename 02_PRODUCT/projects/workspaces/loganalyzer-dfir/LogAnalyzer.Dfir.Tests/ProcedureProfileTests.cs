using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP15a: the procedure profile (owner decisions 18, 19) - model, import, paste, validation, expansion, use.</summary>
public class ProcedureProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-profile-" + Guid.NewGuid().ToString("N"));
    public ProcedureProfileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static ProcedureProfile Full()
    {
        var p = new ProcedureProfile { Name = "Unitatea X" };
        ProfileTables.SetCells(p, ProfileTable.WorkingHours, [["Mon", "08:00", "16:00"], ["Tue", "08:00", "16:00"]]);
        ProfileTables.SetCells(p, ProfileTable.Holidays, [["2026-12-25", "Crăciun"]]);
        ProfileTables.SetCells(p, ProfileTable.ApprovedAccounts, [[@"CORP\alice", "administrator jurnale"]]);
        ProfileTables.SetCells(p, ProfileTable.MaintenanceWindows, [["monthly", "", "Mon", "1", "08:00", "10:00", "rotire"], ["once", "2026-10-15", "", "", "22:00", "23:00", ""]]);
        ProfileTables.SetCells(p, ProfileTable.RotationProcedure, [["EXPORT", ""], ["HASH", ""], ["ARCHIVE", ""], ["VERIFY", ""], ["CLEAR", ""]]);
        ProfileTables.SetCells(p, ProfileTable.ApprovedSoftware, [["7-Zip", "Igor Pavlov", @"C:\Program Files\7-Zip\*", ""]]);
        ProfileTables.SetCells(p, ProfileTable.ExpectedPolicies, [[@"C:\gpo\baseline.lapolicy", "", "baseline"]]);
        ProfileTables.SetCells(p, ProfileTable.Zones, [["ROSU", "rețea clasificată"], ["PUBLIC", "internet"]]);
        ProfileTables.SetCells(p, ProfileTable.TransferChannels, [["Export jurnale", "ROSU", "PUBLIC", "USB marcat", ""]]);
        ProfileTables.SetCells(p, ProfileTable.NetworkDestinations, [["10.0.0.5", "ROSU", "server NTP"]]);
        return p;
    }

    [Fact]
    public void Round_trip_through_the_file_keeps_every_table_and_records_a_sha256()
    {
        var path = Path.Combine(_dir, "p.json");
        var issues = ProfileStore.Save(Full(), path, out var sha);
        Assert.DoesNotContain(issues, i => i.IsError);
        Assert.Equal(64, sha.Length);
        var back = ProfileStore.Load(path);
        Assert.False(back.HasErrors);
        foreach (var t in ProfileTables.All)
            Assert.Equal(ProfileTables.Cells(Full(), t), ProfileTables.Cells(back.Profile!, t));
        Assert.Equal("1.0", back.Profile!.SchemaVersion);
        Assert.Contains("\"schema_version\"", File.ReadAllText(path));
    }

    [Fact]
    public void Missing_file_is_no_profile_and_a_corrupt_file_is_an_error_not_an_empty_profile()
    {
        var none = ProfileStore.Load(Path.Combine(_dir, "nope.json"));
        Assert.Null(none.Profile); Assert.Empty(none.Issues);
        var bad = Path.Combine(_dir, "bad.json"); File.WriteAllText(bad, "{ not json");
        var r = ProfileStore.Load(bad);
        Assert.Null(r.Profile); Assert.True(r.HasErrors);
        File.WriteAllText(bad, "{\"schema_version\":\"2.0\"}");
        Assert.True(ProfileStore.Load(bad).HasErrors);
    }

    [Fact]
    public void Save_refuses_a_profile_with_errors_and_writes_nothing()
    {
        var p = Full();
        p.WorkingHours.Add(new WorkingHoursRow { Day = "Funday", Start = "25:00", End = "08:00" });
        var path = Path.Combine(_dir, "x.json");
        var issues = ProfileStore.Save(p, path, out var sha);
        Assert.False(File.Exists(path)); Assert.Equal("", sha);
        Assert.Contains(issues, i => i.IsError && i.Table == ProfileTable.WorkingHours && i.Line == 3 && i.Message.Contains("Funday"));
        Assert.Contains(issues, i => i.IsError && i.Line == 3 && i.Message.Contains("25:00"));
    }

    [Fact]
    public void Json_import_lists_invalid_rows_per_line()
    {
        var p = Full();
        p.ApprovedSoftware.Add(new ApprovedSoftwareRow { Name = "x", Sha256 = "zz" });
        p.TransferChannels.Add(new TransferChannelRow { Name = "c", FromZone = "ROSU", ToZone = "NECUNOSCUT", Medium = "cablu" });
        var r = ProfileImport.FromJson(ProfileStore.ToJson(p));
        Assert.True(r.HasErrors);
        Assert.Equal(2, r.RowsRejected);
        Assert.Contains(r.Issues, i => i.Table == ProfileTable.ApprovedSoftware && i.Line == 2 && i.Message.Contains("SHA-256"));
        Assert.Contains(r.Issues, i => i.Table == ProfileTable.TransferChannels && i.Line == 2 && i.Message.Contains("NECUNOSCUT"));
    }

    [Fact]
    public void Csv_import_with_header_maps_columns_by_name_and_reports_bad_lines()
    {
        var p = new ProcedureProfile();
        var csv = "End,Day,Start\r\n16:00,Mon,08:00\r\n17:00,Foo,09:00\r\n12:00,Sat,08:00\r\n";
        var r = ProfileImport.Text(p, ProfileTable.WorkingHours, csv);
        Assert.Equal(2, r.RowsAccepted); Assert.Equal(1, r.RowsRejected);
        Assert.Equal(["Mon", "Sat"], p.WorkingHours.Select(w => w.Day));
        Assert.Equal("08:00", p.WorkingHours[0].Start); Assert.Equal("16:00", p.WorkingHours[0].End);
        var issue = Assert.Single(r.Issues, i => i.IsError);
        Assert.Equal(3, issue.Line); Assert.Contains("Foo", issue.Message);
    }

    [Fact]
    public void Paste_of_tab_separated_text_without_header_is_positional_and_appends()
    {
        var p = new ProcedureProfile();
        ProfileImport.Text(p, ProfileTable.ApprovedAccounts, "CORP\\alice\tadmin\nCORP\\bob\t");
        var r = ProfileImport.Text(p, ProfileTable.ApprovedAccounts, "svc_backup\tcont de serviciu");
        Assert.Equal(1, r.RowsAccepted);
        Assert.Equal([@"CORP\alice", @"CORP\bob", "svc_backup"], p.ApprovedAccounts.Select(a => a.Account));
        Assert.Equal("cont de serviciu", p.ApprovedAccounts[2].Note);
    }

    [Fact]
    public void Csv_quoted_fields_with_delimiters_and_extra_fields_are_handled_and_reported()
    {
        var p = new ProcedureProfile();
        var r = ProfileImport.Text(p, ProfileTable.Zones, "\"ROSU, clasificat\",\"descriere\"\"cu ghilimele\"\nALBASTRU,desc,in-plus");
        Assert.Equal(2, r.RowsAccepted);
        Assert.Equal("ROSU, clasificat", p.Zones[0].Name);
        Assert.Equal("descriere\"cu ghilimele", p.Zones[0].Description);
        Assert.Contains(r.Issues, i => !i.IsError && i.Line == 2 && i.Message.Contains("câmpuri"));
    }

    [Fact]
    public void Replace_import_replaces_the_table()
    {
        var p = Full();
        ProfileImport.Text(p, ProfileTable.Zones, "NOU,zona noua", replace: true);
        Assert.Equal(["NOU"], p.Zones.Select(z => z.Name));
    }

    [Fact]
    public void Rotation_in_a_different_order_or_incomplete_is_a_warning_not_an_error()
    {
        var p = new ProcedureProfile();
        ProfileTables.SetCells(p, ProfileTable.RotationProcedure, [["CLEAR", ""], ["EXPORT", ""]]);
        var issues = ProfileOps.Validate(p);
        Assert.DoesNotContain(issues, i => i.IsError);
        Assert.Contains(issues, i => !i.IsError && i.Message.Contains("ordinea"));
        Assert.Contains(issues, i => !i.IsError && i.Message.Contains("lipsesc"));
    }

    // ---- recurring windows ----

    [Fact]
    public void First_monday_of_the_month_expands_to_one_window_per_month_in_the_zone()
    {
        var row = new MaintenanceWindowRow { Kind = "monthly", Day = "Monday", Ordinal = "1", Start = "08:00", End = "10:00" };
        var w = MaintenanceSchedule.Expand([row], new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 11, 30, 0, 0, 0, TimeSpan.Zero), Utc);
        Assert.Equal([new DateTime(2026, 9, 7), new DateTime(2026, 10, 5), new DateTime(2026, 11, 2)], w.Select(x => x.StartUtc.UtcDateTime.Date));
        Assert.All(w, x => Assert.Equal(TimeSpan.FromHours(2), x.EndUtc - x.StartUtc));
        Assert.All(w, x => Assert.Equal(8, x.StartUtc.Hour));
    }

    [Fact]
    public void Local_time_is_converted_with_the_zone_offset_including_summer_time()
    {
        var bucharest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        var row = new MaintenanceWindowRow { Kind = "once", Date = "2026-07-06", Start = "08:00", End = "10:00" };   // EEST, UTC+3
        var w = Assert.Single(MaintenanceSchedule.Expand([row], new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero), bucharest));
        Assert.Equal(5, w.StartUtc.Hour); Assert.Equal(7, w.EndUtc.Hour);
        var winter = new MaintenanceWindowRow { Kind = "once", Date = "2026-12-07", Start = "08:00", End = "10:00" };    // EET, UTC+2
        Assert.Equal(6, Assert.Single(MaintenanceSchedule.Expand([winter], new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero), bucharest)).StartUtc.Hour);
    }

    [Fact]
    public void Last_weekday_weekly_and_overnight_windows_expand()
    {
        var last = new MaintenanceWindowRow { Kind = "monthly", Day = "Fri", Ordinal = "last", Start = "22:00", End = "02:00" };
        var w = MaintenanceSchedule.Expand([last], new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 9, 30, 23, 0, 0, TimeSpan.Zero), Utc);
        var sep = Assert.Single(w, x => x.StartUtc.Month == 9);
        Assert.Equal(new DateTime(2026, 9, 25, 22, 0, 0), sep.StartUtc.UtcDateTime);   // last Friday of September 2026
        Assert.Equal(new DateTime(2026, 9, 26, 2, 0, 0), sep.EndUtc.UtcDateTime);
        var weekly = new MaintenanceWindowRow { Kind = "weekly", Day = "Sun", Start = "01:00", End = "02:00" };
        Assert.Equal(4, MaintenanceSchedule.Expand([weekly], new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), Utc).Count(x => x.StartUtc.Month == 9));
    }

    [Fact]
    public void Invalid_window_rows_are_not_expanded()
    {
        var bad = new MaintenanceWindowRow { Kind = "monthly", Day = "Mon", Ordinal = "9", Start = "08:00", End = "10:00" };
        Assert.Empty(MaintenanceSchedule.Expand([bad], new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), Utc));
    }

    // ---- use: maintenance policy and working hours ----

    [Fact]
    public void Empty_or_absent_profile_gives_no_policy_and_no_working_hours_and_every_section_nedefinit()
    {
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(ProfileOps.ToMaintenancePolicy(null, from, from.AddDays(30)));
        Assert.Null(ProfileOps.ToMaintenancePolicy(new ProcedureProfile(), from, from.AddDays(30)));
        Assert.Null(ProfileOps.ToWorkingHours(new ProcedureProfile()));
        Assert.All(ProfileOps.Status(null), s => Assert.False(s.Defined));
        Assert.All(ProfileOps.Status(new ProcedureProfile()), s => { Assert.False(s.Defined); Assert.Equal("nedefinit", s.Text); });
        Assert.DoesNotContain("conform", ProfileOps.Describe(new ProcedureProfile()));
    }

    [Fact]
    public void Status_reports_defined_sections_and_leaves_the_others_nedefinit()
    {
        var p = new ProcedureProfile();
        ProfileTables.SetCells(p, ProfileTable.Zones, [["ROSU", ""]]);
        var s = ProfileOps.Status(p).ToDictionary(x => x.Section);
        Assert.True(s["Zone și transferuri"].Defined);
        Assert.False(s["Software aprobat"].Defined);
        Assert.Contains("Politică GPO așteptată: nedefinit", ProfileOps.Describe(p));
    }

    [Fact]
    public void Profile_policy_makes_a_clear_Routine_or_Unexpected_without_it_stays_NotAssessed()
    {
        var p = Full();
        var clearAt = new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.Zero);   // first Monday of October 2026, inside 08:00-10:00 (UTC zone)
        var policy = ProfileOps.ToMaintenancePolicy(p, clearAt.AddDays(-40), clearAt.AddDays(40), Utc);
        Assert.NotNull(policy);
        Assert.Equal(LogClearLifecycle.Routine, LogClearAssessment.Assess([new("Security", clearAt, "alice", "CORP")], policy, [])[0].Lifecycle);
        Assert.Equal(LogClearLifecycle.Unexpected, LogClearAssessment.Assess([new("Security", clearAt.AddHours(3), "alice", "CORP")], policy, [])[0].Lifecycle);
        Assert.Equal(LogClearLifecycle.Unexpected, LogClearAssessment.Assess([new("Security", clearAt, "mallory", "CORP")], policy, [])[0].Lifecycle);
        Assert.Equal(LogClearLifecycle.NotAssessed, LogClearAssessment.Assess([new("Security", clearAt, "alice", "CORP")], null, [])[0].Lifecycle);
    }

    [Fact]
    public void Working_hours_cover_all_intervals_and_shifts_including_overnight_ones()
    {
        var p = new ProcedureProfile();
        ProfileTables.SetCells(p, ProfileTable.WorkingHours, [["Mon", "08:00", "16:00"], ["Tue", "09:00", "17:00"]]);
        var wh = ProfileOps.ToWorkingHours(p)!;
        Assert.Equal("08:00-17:00", wh.Describe());
        ProfileTables.SetCells(p, ProfileTable.Shifts, [["noapte", "22:00", "06:00"]]);
        var night = ProfileOps.ToWorkingHours(p)!;
        Assert.True(night.Contains(new TimeOnly(23, 0)) && night.Contains(new TimeOnly(3, 0)) && night.Contains(new TimeOnly(12, 0)));
        var only = new ProcedureProfile();
        ProfileTables.SetCells(only, ProfileTable.Shifts, [["noapte", "22:00", "06:00"]]);
        Assert.Equal("22:00-06:00", ProfileOps.ToWorkingHours(only)!.Describe());
    }
}
