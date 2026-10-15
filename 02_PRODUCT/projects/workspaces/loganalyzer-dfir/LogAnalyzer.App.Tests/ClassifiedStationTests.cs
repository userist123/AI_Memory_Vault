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
        f.Transfers = new TransferRegister { Rows = [new TransferRow { RegistrationNumber = "TR-2026-0001", DateTime = when, Direction = "ieșire", Content = "plan.docx", Person = "ion" }] };
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
}
