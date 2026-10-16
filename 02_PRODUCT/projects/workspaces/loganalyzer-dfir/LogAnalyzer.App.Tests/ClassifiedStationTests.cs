using System.IO;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Station control of classified folders (owner request 2026-10-15): A10 (SACL covers read / write / delete, success and failure), A14 (the sources that
/// make exits visible, "sursă lipsă: … cum se pornește"), A15 (accesses and exits against the transfer register), the PDF section and the UI tab.
/// StationFacts are built in memory with records shaped like the EVTX EventData.
/// </summary>
public sealed class ClassifiedStationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private const string Root = @"D:\Clasificat";

    private static readonly AuditRuleFact Full = new("S-1-1-0", "Everyone", 0x301BF, true, true, true);

    private static StationFacts Station(StationPosture posture = StationPosture.Standalone)
    {
        var f = new StationFacts { Posture = posture, IsAdministrator = true, CollectedUtc = Now, PeriodStartUtc = Now.AddDays(-30), PeriodEndUtc = Now };
        f.AuditPolicy = new()
        {
            ["File System"] = AuditSetting.Success | AuditSetting.Failure, ["Removable Storage"] = AuditSetting.Success | AuditSetting.Failure,
            ["Handle Manipulation"] = AuditSetting.Success, ["Process Creation"] = AuditSetting.Success, ["Detailed File Share"] = AuditSetting.Success,
        };
        f.Settings["Audit.ProcessCreationIncludeCmdLine"] = "1";
        f.Settings["PrintService.OperationalEnabled"] = "1";
        f.FolderAudits = [new(Root, true, 1, "Everyone: Modify", "", [Full])];
        f.Coverage.Add(new ChannelCoverage("Security", Now.AddDays(-60), 100, true, ""));
        f.RemovableDrivesNow.Add("E:");
        return f;
    }

    private static LogFact Sec(int id, DateTimeOffset t, params (string K, string V)[] d) =>
        new("Security", id, t, id, "", d.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase));

    private static LogFact File4663(DateTimeOffset t, string obj, string mask, string handle) =>
        Sec(4663, t, ("SubjectUserName", "ion"), ("ObjectType", "File"), ("ObjectName", obj), ("AccessMask", mask), ("HandleId", handle), ("ProcessId", "0x10"), ("ProcessName", "explorer.exe"));

    private static ControlCheck Check(StationFacts f, string id) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == id);

    // ---- A10 ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A10_rules_that_miss_failure_delete_or_inheritance_or_other_principals_are_Neconform_and_say_what_is_missing()
    {
        var f = Station();
        f.FolderAudits = [new(Root, true, 1, "Everyone: ReadData (Success)", "", [new AuditRuleFact("S-1-1-0", "Everyone", 0x1, true, false, true)])];
        var c = Check(f, "A10");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("ștergere (succes)", c.Detail);
        Assert.Contains("citire (eșec)", c.Detail);

        Assert.Contains("nu moștenesc", string.Join(" ", ControlEvaluator.SaclGaps([Full with { InheritsToFiles = false }])));
        Assert.Contains("Everyone", string.Join(" ", ControlEvaluator.SaclGaps([Full with { Sid = "S-1-5-21-1-2-3-1001" }])));
        Assert.Empty(ControlEvaluator.SaclGaps([Full]));
    }

    [Fact]
    public void A10_rules_counted_but_not_read_in_detail_are_Nedeterminat_never_Conform_and_a_junction_is_named()
    {
        var f = Station();
        f.FolderAudits = [new(Root, true, 2, "x", "")];
        Assert.Equal(ControlStatus.Nedeterminat, Check(f, "A10").Status);
        f.FolderAudits = [new(Root, true, 1, "x", "", [Full], LinkTarget: @"F:\Real")];
        var c = Check(f, "A10");
        Assert.Equal(ControlStatus.Conform, c.Status);
        Assert.Contains(@"F:\Real", c.Detail);
    }

    // ---- A14 ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A11_all_sources_on_is_Conform_a_missing_one_is_Neconform_with_how_to_turn_it_on_and_unread_is_Nedeterminat()
    {
        Assert.Equal(ControlStatus.Conform, Check(Station(), "A14").Status);

        var f = Station();
        f.AuditPolicy!["Removable Storage"] = AuditSetting.None;
        f.Settings["Audit.ProcessCreationIncludeCmdLine"] = StationFactCollector.Absent;
        var c = Check(f, "A14");
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Sursă lipsă", c.Detail);
        Assert.Contains("cum se pornește", c.Detail);
        Assert.Contains("Audit Removable Storage", c.Detail);
        Assert.Contains("Include command line", c.Detail);

        var u = Station();
        u.AuditPolicy = null;
        Assert.Equal(ControlStatus.Nedeterminat, Check(u, "A14").Status);
    }

    [Fact]
    public void A11_asks_for_Detailed_File_Share_only_where_there_is_a_network()
    {
        var f = Station();
        f.AuditPolicy!.Remove("Detailed File Share");
        Assert.Equal(ControlStatus.Conform, Check(f, "A14").Status);
        var n = Station(StationPosture.IsolatedNetwork);
        n.AuditPolicy!["Detailed File Share"] = AuditSetting.None;
        Assert.Equal(ControlStatus.Neconform, Check(n, "A14").Status);
    }

    // ---- A15 ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A12_a_classified_file_written_to_a_stick_without_a_register_entry_is_Neconform_with_the_chain_in_the_report_and_the_tab()
    {
        var f = Station();
        f.Logs.Add(File4663(Now.AddHours(-2), @"D:\Clasificat\plan.docx", "0x1", "0x1"));
        f.Logs.Add(File4663(Now.AddHours(-2).AddMinutes(1), @"E:\plan.docx", "0x2", "0x2"));
        f.Logs.Add(new LogFact("Microsoft-Windows-Partition/Diagnostic", 1006, Now.AddHours(-2).AddMinutes(-5), 1, "",
            new Dictionary<string, string> { ["SerialNumber"] = "AA11", ["Capacity"] = "16000000000", ["BusType"] = "7", ["Manufacturer"] = "Kingston", ["Model"] = "DT" }));
        var report = ControlEvaluator.Evaluate(f);
        var a12 = report.Checks.Single(c => c.Id == "A15");
        Assert.Equal(ControlStatus.Neconform, a12.Status);
        Assert.Contains("NEJUSTIFICAT", a12.Detail);
        Assert.Contains(a12.Evidence, e => e.Contains("plan.docx") && e.Contains("AA11"));
        Assert.Contains(report.Actions, a => a.Action.Contains("informație clasificată"));

        var lines = ControlReportPdf.ClassifiedSectionLines(report);
        Assert.Contains(lines, l => l.Head && l.Text.StartsWith("IEȘIRE NEJUSTIFICATĂ") && l.Text.Contains("plan.docx"));
        Assert.Contains(lines, l => l.Text.Contains("Acces ") && l.Text.Contains("citire"));

        var vm = new LogAnalyzer.UI.ViewModels.StationControlViewModel();
        vm.FillClassified(report);
        Assert.Equal(@"D:\Clasificat\plan.docx", vm.ClassifiedFiles[0].Path);
        Assert.Same(vm.ClassifiedFiles[0], vm.SelectedClassifiedFile);
        Assert.Contains(vm.SelectedClassifiedLines, l => l.StartsWith("Ieșire NEJUSTIFICATĂ"));
    }

    [Fact]
    public void A12_the_same_copy_recorded_in_the_transfer_register_is_approved_and_only_to_verify_without_findings_never_Conform()
    {
        var f = Station();
        f.Logs.Add(File4663(Now.AddHours(-2), @"D:\Clasificat\plan.docx", "0x1", "0x1"));
        f.Logs.Add(File4663(Now.AddHours(-2).AddMinutes(1), @"E:\plan.docx", "0x2", "0x2"));
        var when = TimeZoneInfo.ConvertTime(Now.AddHours(-2), TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm");
        f.Transfers = new TransferRegister { Rows = [new TransferRow { RegistrationNumber = "TR-2026-0001", DateTime = when, Direction = "ieșire", Content = "plan.docx", Person = "ion", MediumSerial = "AA11", Approver = "sef" }] };
        f.Logs.Add(new LogFact("Microsoft-Windows-Partition/Diagnostic", 1006, Now.AddHours(-2).AddMinutes(-5), 1, "",
            new Dictionary<string, string> { ["SerialNumber"] = "AA11", ["Capacity"] = "16000000000", ["BusType"] = "7" }));
        var report = ControlEvaluator.Evaluate(f);
        Assert.Equal(1, report.Classified!.Approved);
        Assert.Equal(ControlStatus.DeVerificat, report.Checks.Single(c => c.Id == "A15").Status);   // owner decision 24: absence is never CONFORM

        var empty = Station();
        Assert.Equal(ControlStatus.DeVerificat, Check(empty, "A15").Status);
        Assert.Contains("Absența observațiilor", Check(empty, "A15").Detail);
        empty.Coverage.Clear();
        Assert.Equal(ControlStatus.Nedeterminat, Check(empty, "A15").Status);
        empty.FolderAudits = [];
        Assert.Equal(ControlStatus.Nedeterminat, Check(empty, "A15").Status);
        Assert.Contains(ControlReportPdf.ClassifiedSectionLines(ControlEvaluator.Evaluate(empty)), l => l.Text.Contains("nu definește foldere clasificate"));
    }

    [Fact]
    public void A12_a_read_and_a_stick_connected_soon_after_is_only_a_temporal_coincidence()
    {
        var f = Station();
        f.Logs.Add(File4663(Now.AddHours(-2), @"D:\Clasificat\plan.docx", "0x1", "0x1"));
        f.Logs.Add(new LogFact("Microsoft-Windows-Partition/Diagnostic", 1006, Now.AddHours(-1), 1, "",
            new Dictionary<string, string> { ["SerialNumber"] = "AA11", ["Capacity"] = "16000000000", ["BusType"] = "7" }));
        var c = Check(f, "A15");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains(c.Evidence, e => e.Contains(ClassifiedAccess.TemporalNote));
    }

    [Fact]
    public void A12_permission_and_audit_rule_changes_and_shortcuts_from_the_profiles_are_used()
    {
        var f = Station();
        f.Logs.Add(Sec(4907, Now.AddHours(-1), ("SubjectUserName", "admin"), ("ObjectType", "File"), ("ObjectName", Root), ("ProcessName", "x.exe")));
        f.ShellTraces = [new ShellTraceFact("ion", @"C:\Users\ion\Recent\plan.lnk", @"D:\Clasificat\plan.docx", "FIXED", "", Now.AddHours(-3))];
        var report = ControlEvaluator.Evaluate(f);
        Assert.Equal(ControlStatus.Neconform, report.Checks.Single(c => c.Id == "A15").Status);
        Assert.Contains(report.Classified!.Files, x => x.Accesses.Any(a => a.Weak));
    }

    [Fact]
    public void The_new_checks_have_guides_and_apply_only_to_the_controlled_editions()
    {
        foreach (var id in new[] { "A10", "A14", "A15" })
        {
            Assert.Contains(id, CheckGuides.Ids);
            Assert.True(ControlApplicability.Applies(id, StationPosture.Standalone));
            Assert.True(ControlApplicability.Applies(id, StationPosture.IsolatedNetwork));
            Assert.False(ControlApplicability.Applies(id, StationPosture.Internet));
        }
        Assert.Contains("sursă lipsă", CheckGuides.For("A14").Meaning);
        Assert.Contains("corelare temporală", CheckGuides.For("A15").Meaning);
    }

    [Fact]
    public void The_station_view_has_one_tab_for_classified_information()
    {
        var dir = AppContext.BaseDirectory;
        string? xaml = null;
        for (var d = new DirectoryInfo(dir); d is not null && xaml is null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.App", "Views", "StationControlView.xaml"))) xaml = File.ReadAllText(Path.Combine(d.FullName, "LogAnalyzer.App", "Views", "StationControlView.xaml"));
        Assert.NotNull(xaml);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xaml!, "TabItem Header=\"Informații clasificate\""));
        Assert.Contains("ItemsSource=\"{Binding ClassifiedFiles}\"", xaml);
    }

    // ---- independent review of PR #291: capped logs, the application's own reads, files without a fingerprint ---------------------------------------

    [Fact]
    public void A15_says_when_the_access_log_was_capped_and_is_never_better_than_to_verify()
    {
        var f = Station();
        f.Gaps.Add(new LogAnalyzer.Dfir.Model.EvidenceGap("Security 4663 (foldere clasificate)", LogAnalyzer.Dfir.Model.EvidenceStatus.Partial, "limitat la cele mai recente 50000 de evenimente", "x", "y", "Da"));
        var c = Check(f, "A15");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("au fost citite doar ultimele", c.Detail);
        Assert.Contains("pot lipsi accese mai vechi", c.Detail);
    }

    [Fact]
    public void Records_of_the_applications_own_hashing_from_a_previous_control_are_listed_apart()
    {
        var f = Station();
        const string image = @"C:\Program Files\LogAnalyzer\LogAnalyzer.exe";
        f.SelfWindows.Add(new SelfActivityWindow(Now.AddDays(-1), Now.AddDays(-1).AddMinutes(2), 4242, image, "inspector", 1));
        f.AppImagePath = image;                                   // the running application's executable and its fingerprint
        f.AppImageSha256 = new string('c', 64);
        f.ImageSha256[image] = new string('c', 64);
        f.Logs.Add(Sec(4663, Now.AddDays(-1).AddMinutes(1), ("SubjectUserName", "inspector"), ("ObjectType", "File"), ("ObjectName", @"D:\Clasificat\plan.docx"),
            ("AccessMask", "0x1"), ("HandleId", "0x5"), ("ProcessId", "0x1092"), ("ProcessName", image)));
        f.Logs.Add(new LogFact("Microsoft-Windows-Partition/Diagnostic", 1006, Now.AddDays(-1).AddMinutes(30), 1, "",
            new Dictionary<string, string> { ["SerialNumber"] = "AA11", ["Capacity"] = "16000000000", ["BusType"] = "7" }));
        var report = ControlEvaluator.Evaluate(f);
        Assert.Empty(report.Classified!.Files);                       // no access by the inspector, no temporal coincidence with the stick
        Assert.Single(report.Classified.SelfActivity);
        Assert.Contains(ControlReportPdf.ClassifiedSectionLines(report), l => l.Text.Contains(ClassifiedAccess.SelfLabel));
    }

    private static string TempFolder(string name)
    {
        var d = Path.Combine(AppContext.BaseDirectory, "classified-tests", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Hashing_keeps_the_last_access_time_records_its_window_and_reports_files_without_a_fingerprint()
    {
        var dir = TempFolder("hash");
        var old = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var n in new[] { "a.txt", "b.txt", "c.txt" })
        {
            var p = Path.Combine(dir, n);
            File.WriteAllText(p, n);
            File.SetLastAccessTimeUtc(p, old);
        }
        var f = Station();
        ClassifiedFactCollector.ReadFiles(f, [dir], maxHashed: 2);
        Assert.Equal(3, f.ClassifiedFiles!.Count);
        Assert.Equal(2, f.ClassifiedHashes!.Count);
        Assert.Contains(f.Gaps, g => g.Reason.Contains("fișiere fără amprentă: 1"));
        foreach (var p in f.ClassifiedHashes.Keys) Assert.Equal(old, File.GetLastAccessTimeUtc(p));
        var w = Assert.IsType<SelfActivityWindow>(f.SelfWindow);
        Assert.Equal(Environment.ProcessId, w.ProcessId);
        Assert.True(w.EndUtc >= w.StartUtc);
        Assert.Equal(2, w.Files);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Files_over_the_size_limit_are_reported_not_skipped_silently()
    {
        var dir = TempFolder("big");
        File.WriteAllText(Path.Combine(dir, "mare.bin"), new string('x', 2048));
        var f = Station();
        ClassifiedFactCollector.ReadFiles(f, [dir], maxHashBytes: 1024);
        Assert.Empty(f.ClassifiedHashes!);
        Assert.Contains(f.Gaps, g => g.Reason.Contains("fișiere fără amprentă: 1") && g.Reason.Contains("1024"));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void The_classified_event_filter_keeps_classified_removable_and_unmapped_paths_only()
    {
        var keep = ClassifiedFactCollector.ClassifiedEventFilter([Root], [@"C:\Alias"], new HashSet<string>(["E:"], StringComparer.OrdinalIgnoreCase), null);
        Dictionary<string, string> D(string obj, string task = "12800") => new(StringComparer.OrdinalIgnoreCase) { ["ObjectName"] = obj, ["_Task"] = task };
        Assert.True(keep(4663, D(@"D:\Clasificat\a.docx")));
        Assert.True(keep(4663, D(@"C:\Alias\a.docx")));
        Assert.True(keep(4663, D(@"E:\a.docx")));
        Assert.True(keep(4663, D(@"\Device\HarddiskVolume9\a.docx")));
        Assert.True(keep(4663, D(@"C:\Users\x\a.docx", "12802")));
        Assert.True(keep(4660, D("")));
        Assert.False(keep(4663, D(@"C:\Windows\System32\x.dll")));
    }

    [Fact]
    public void A_register_row_declares_its_number_of_copies()
    {
        Assert.Equal("Copies", TransferRegister.ColumnNames[^1]);
        var reg = new TransferRegister();
        reg.SetCells([["TR-1", "2026-10-14 09:00", "", "ieșire", "s", "", "d", "", "p", "", "stick", "SN", "", "a.docx", new string('a', 64), "w", "sef", "", "3"]]);
        Assert.Equal("3", reg.Rows[0].Copies);
        Assert.Equal(3, ClassifiedAccess.Copies(reg.Rows[0]));
        Assert.Contains(reg.ValidateRow([.. reg.Cells()[0][..^1], "zero"], []), m => m.IsError && m.Message.Contains("copii"));
    }

    [Fact]
    public void A15_reports_an_invalid_hashing_window_and_is_at_least_to_verify()
    {
        var f = Station();
        f.InvalidSelfWindows.Add("fereastră de calcul amprente nevalidă: nu este în interiorul controlului ei (run-X)");
        var c = Check(f, "A15");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("fereastră de calcul amprente nevalidă", c.Detail);
    }

    [Fact]
    public void A15_never_throws_on_a_register_time_in_the_skipped_dst_hour()
    {
        var f = Station();
        f.Logs.Add(File4663(Now.AddHours(-2), @"D:\Clasificat\plan.docx", "0x1", "0x1"));
        f.Logs.Add(File4663(Now.AddHours(-2).AddMinutes(1), @"E:\plan.docx", "0x2", "0x2"));
        // a local time that does not exist in any zone with a spring-forward hour; on a zone without DST it is simply a normal time
        f.Transfers = new TransferRegister { Rows = [new TransferRow { RegistrationNumber = "TR-1", DateTime = "2026-03-29 03:30", Direction = "ieșire", Content = "plan.docx", MediumSerial = "AA11", Approver = "sef" }] };
        var ex = Record.Exception(() => ControlEvaluator.Evaluate(f));
        Assert.Null(ex);
    }

    [Fact]
    public void A15_reports_backwards_times_in_the_application_journal_and_is_at_least_to_verify()
    {
        var f = Station();
        f.InvalidSelfWindows.Add("înregistrări cu oră în urmă în jurnalul aplicației: înregistrarea 7 are ora 2026-10-11 10:00 UTC, înaintea celei precedente");
        var c = Check(f, "A15");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("înregistrări cu oră în urmă în jurnalul aplicației", c.Detail);
    }
}
