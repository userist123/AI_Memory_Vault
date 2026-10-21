using System.IO;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// E7 slice 3: the "Activitatea utilizatorului" tab of the investigation page (view model rows, semantics next to the time), the rows of the investigation
/// PDF and of section 3b of the control PDF. Synthetic events only: invented folders, accounts and SIDs.
/// </summary>
public sealed class UserActivityUiTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private const string Root = @"D:\Clasificat\Proiect";
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

    private static TimelineEvent Bag(string path, DateTimeOffset? time = null) => new()
    {
        Time = time is { } t ? new Timestamp(t, "", "test") : Timestamp.Unknown(), Source = "ShellBags", EvidenceId = "EV-1", Path = path, Summary = "ShellBags " + path, Locator = "L",
        Fields = { ["Account"] = "utilizator.a", ["ShellItemType"] = "0x31", ["ShellItemKind"] = "Folder" },
    };

    private static TimelineEvent Bin(string path, DateTimeOffset t, string r) => new()
    {
        Time = new Timestamp(t, "", "test"), Source = "RecycleBin", EvidenceId = "EV-4", Path = path, Summary = "Șters " + path, Locator = "$I1 (v2)",
        Fields = { ["OriginalPath"] = path, ["OriginalSize"] = "10", ["RContent"] = r, ["OwnerSid"] = "S-1-5-21-1-2-3-1001" },
    };

    private static UserActivityReport Report() =>
        UserActivityAnalysis.Analyze([Bag(@"C:\x", T0), Bag(Root, T0), Bag(@"F:\y"), Bin(Root + @"\a.docx", T0, "absent")], [new ClassifiedRoot(Root)], ["E:"]);

    [Fact]
    public void The_tab_lists_the_rows_relevant_first_and_says_what_the_time_means_next_to_the_table()
    {
        var vm = new InvestigationViewModel();
        Assert.Contains("nedefinită", vm.UserActivitySummary);
        vm.FillUserActivity(Report());
        Assert.Equal(4, vm.UserActivity.Count);
        Assert.StartsWith("Folder clasificat:", vm.UserActivity[0].Relevance);
        Assert.Equal("—", vm.UserActivity[^1].Relevance);
        Assert.Contains("în foldere clasificate: 1", vm.UserActivitySummary);
        var note = vm.UserActivityTimeNote;
        Assert.Contains("ShellBags = ultima actualizare a intrării folderului de către Explorer", note);
        Assert.Contains("nu dovedește citirea unui fișier", note);
        Assert.Contains("RecentDocs = ora cheii, doar pentru cea mai nouă intrare", note);
        Assert.Contains("Office MRU = ultima deschidere", note);
        Assert.Contains("conținut golit sau eliminat", note);
        Assert.Contains("DE VERIFICAT", note);
        Assert.Contains(vm.UserActivity, i => i.Source == "RecycleBin" && i.Who == "SID S-1-5-21-1-2-3-1001" && i.Detail.Contains("conținut golit sau eliminat"));
        vm.FillUserActivity(null);
        Assert.Empty(vm.UserActivity);
    }

    [Fact]
    public void A_very_long_list_is_capped_and_the_summary_says_so()
    {
        var events = Enumerable.Range(0, InvestigationViewModel.MaxUserActivityRows + 10).Select(n => Bag($@"E:\d{n}", T0.AddSeconds(n))).ToList();
        var vm = new InvestigationViewModel();
        vm.FillUserActivity(UserActivityAnalysis.Analyze(events, [], ["E:"]));
        Assert.Equal(InvestigationViewModel.MaxUserActivityRows, vm.UserActivity.Count);
        Assert.Contains("Se listează primele", vm.UserActivitySummary);
    }

    [Fact]
    public void The_investigation_tab_is_declared_with_its_columns_and_binds_the_collection()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "LogAnalyzer.App", "Views", "InvestigationView.xaml"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!, "LogAnalyzer.App", "Views", "InvestigationView.xaml"));
        Assert.Contains("Header=\"Activitatea utilizatorului\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding UserActivity}\"", xaml);
        foreach (var col in new[] { "Sursă", "Cale", "Ora", "Ce înseamnă ora", "Utilizator / SID", "Detalii" }) Assert.Contains($"Header=\"{col}\"", xaml);
        Assert.Contains("{Binding UserActivityTimeNote, Mode=OneWay}", xaml);
    }

    [Fact]
    public void The_investigation_pdf_rows_come_from_the_same_view_as_the_rules_and_the_page()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        var result = new InvestigationResult { Case = ws, Procedure = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = Root }] } };
        result.Timeline.AddRange([Bag(Root, T0), Bin(Root + @"\a.docx", T0, "absent"), Bag(@"C:\x", T0)]);
        var lines = InvestigationReportText.UserActivityLines(result);
        Assert.True(lines[0].Head);
        Assert.Contains(lines, l => l.Text.Contains("în foldere clasificate: 1"));
        Assert.Contains(lines, l => l.Text.Contains(Root) && l.Text.Contains("nu dovedește că un fișier a fost citit"));
        Assert.Contains(lines, l => l.Text.Contains("conținut golit sau eliminat"));
        Assert.Equal(result.UserActivityView().Summary, lines[1].Text);   // the same numbers as the page
        var pdf = Path.Combine(_k.Dir, "ua.pdf");
        InvestigationReportPdf.Write(result, pdf, "investigator test");
        Assert.True(new FileInfo(pdf).Length > 1000);
    }

    private static StationFacts Station(bool folders = true)
    {
        var f = new StationFacts { Posture = StationPosture.Standalone, IsAdministrator = true, CollectedUtc = T0, PeriodStartUtc = T0.AddDays(-30), PeriodEndUtc = T0 };
        if (folders) f.FolderAudits = [new(Root, true, 1, "Everyone: Modify", "", [new AuditRuleFact("S-1-1-0", "Everyone", 0x301BF, true, true, true)])];
        return f;
    }

    [Fact]
    public void Control_section_3b_says_user_activity_was_not_evaluated_when_it_has_none_and_lists_the_rows_when_given()
    {
        var report = ControlEvaluator.Evaluate(Station());
        var without = ControlReportPdf.ClassifiedSectionLines(report);
        Assert.Contains(without, l => l.Text.Contains("nu a fost evaluată de acest raport"));

        var a15Before = report.Checks.Single(c => c.Id == "A15").Status;
        report.UserActivity = Report();
        var with = ControlReportPdf.ClassifiedSectionLines(report);
        Assert.Contains(with, l => l.Head && l.Text.StartsWith("Activitatea utilizatorului"));
        Assert.Contains(with, l => l.Text.Contains("în foldere clasificate: 1") && l.Text.Contains("șterse din foldere clasificate sau de pe volume amovibile: 1"));
        Assert.Contains(with, l => l.Text.Contains(Root) && l.Text.Contains("ultima actualizare a intrării folderului de către Explorer"));
        Assert.Contains(with, l => l.Text.Contains("DE VERIFICAT, nu NECONFORM"));
        Assert.DoesNotContain(with, l => l.Text.Contains("nu a fost evaluată de acest raport"));
        // the check A15 is untouched by these traces (no NECONFORM from them alone)
        Assert.Equal(a15Before, report.Checks.Single(c => c.Id == "A15").Status);
    }

    [Fact]
    public void Control_section_3b_without_classified_folders_still_carries_the_user_activity_statement()
    {
        var lines = ControlReportPdf.ClassifiedSectionLines(ControlEvaluator.Evaluate(Station(folders: false)));
        Assert.Contains(lines, l => l.Text.Contains("nu definește foldere clasificate"));
        Assert.Contains(lines, l => l.Text.Contains("nu a fost evaluată de acest raport"));
    }
}
