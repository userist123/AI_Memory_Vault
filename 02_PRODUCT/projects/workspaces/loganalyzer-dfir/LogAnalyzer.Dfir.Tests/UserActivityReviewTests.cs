using System.Diagnostics;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;
using static LogAnalyzer.Dfir.Tests.Synthetic.ShellItemBuilder;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// E7 slice 3, review round 1: a reopened case shows the same user-activity view (or says it cannot), known-folder and network roots of ShellBags are matched against
/// the classified folders, the drive letter must be a letter, and the analysis stays bounded on control characters and on a large timeline. Synthetic data only.
/// </summary>
public sealed class UserActivityReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_uarev_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Shell = new(2026, 8, 20, 10, 0, 2);
    private const string BagRoot = @"Local Settings\Software\Microsoft\Windows\Shell\BagMRU";
    private const uint End = 0xFFFFFFFF;
    private static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

    public UserActivityReviewTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    // ---- finding 1: reopened case ----

    /// <summary>A UsrClass.dat whose BagMRU shows Documents\Clasificat (a known-folder root) and E:\ under This PC.</summary>
    private string WriteHive(string profile)
    {
        var dir = Path.Combine(_dir, "Users", profile, "AppData", "Local", "Microsoft", "Windows");
        Directory.CreateDirectory(dir);
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = root;
        foreach (var part in BagRoot.Split((char)92)) { var k = new RegfBuilder.Key(part, T0); bag.Children.Add(k); bag = k; }
        bag.Value("0", RegfBuilder.Binary, List(Root(Documents)));
        bag.Value("1", RegfBuilder.Binary, List(Root(ThisPc)));
        bag.Value("MRUListEx", RegfBuilder.Binary, Mru(0, 1, End));
        var docs = new RegfBuilder.Key("0", T0.AddDays(1)); bag.Children.Add(docs);
        docs.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "Clasificat", 0, Shell)));
        docs.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var pc = new RegfBuilder.Key("1", T0.AddDays(2)); bag.Children.Add(pc);
        pc.Value("0", RegfBuilder.Binary, List(Volume(@"E:\")));
        pc.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var p = Path.Combine(dir, "UsrClass.dat");
        File.WriteAllBytes(p, RegfBuilder.Build(root, "UsrClass.dat"));
        return p;
    }

    private (InvestigationResult Run, CaseWorkspace Ws, string ClassifiedRoot) RunCase(string profile = "analist")
    {
        var hive = WriteHive(profile);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "ua-reopen", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [hive]);
        var classified = Path.Combine(_dir, "Users", profile, "Documents", "Clasificat");
        var prof = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = classified, Note = "test" }] };
        var run = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, procedureProfile: prof);
        return (run, ws, classified);
    }

    private static List<string> Rows(UserActivityReport v) =>
        [.. v.Items.Select(i => $"{i.Source}|{i.Path}|{i.ResolvedPath}|{i.ClassifiedRoot}|{i.Drive}|{i.DriveLetter}|{i.Who}|{i.Detail}|{i.Relevance}|{i.TimeText}")];

    [Fact]
    public void A_reopened_case_shows_the_same_user_activity_view_as_the_run()
    {
        var (run, ws, classified) = RunCase();
        var before = run.UserActivityView();
        Assert.True(before.Items.Any(i => i.ClassifiedRoot.Equals(classified, StringComparison.OrdinalIgnoreCase)), string.Join(" || ", Rows(before)) + " ## " + classified);   // Documente\Clasificat resolved through the profile folder
        Assert.True(before.HasFolders);
        Assert.True(File.Exists(ws.FullPath(UserActivityContext.RelPath)));

        var res = CaseLoader.Open(ws.Root);
        Assert.True(res.Opened, res.RefusedReason);
        var after = res.Case!.Result.UserActivityView();
        Assert.Equal(Rows(before), Rows(after));
        Assert.Equal(before.Summary, after.Summary);
        Assert.Equal("", after.Unevaluable);
        Assert.DoesNotContain("neevaluabil", after.Summary);
        Assert.Equal(UserActivityAnalysis.ReportLines(before).Select(l => l.Text), UserActivityAnalysis.ReportLines(after).Select(l => l.Text));
    }

    [Fact]
    public void An_old_case_without_the_saved_context_says_so_and_does_not_claim_undefined_folders()
    {
        var (_, ws, _) = RunCase();
        File.Delete(ws.FullPath(UserActivityContext.RelPath));
        var res = CaseLoader.Open(ws.Root, recheck: false);
        Assert.True(res.Opened, res.RefusedReason);
        var view = res.Case!.Result.UserActivityView();
        Assert.True(view.Total > 0);
        Assert.Contains("neevaluabil la redeschidere: profilul și literele confirmate nu au fost salvate cu acest caz", view.Summary);
        Assert.DoesNotContain("nedefinite", view.Summary);
        Assert.Empty(view.Classified);
        Assert.All(view.Items, i => Assert.False(i.IsRelevant));
        var lines = UserActivityAnalysis.ReportLines(view);
        Assert.Contains(lines, l => l.Text.Contains("neevaluabil la redeschidere"));
        Assert.DoesNotContain(lines, l => l.Text.Contains("nedefinite"));
        Assert.Contains(res.Case.Notes, n => n.Contains("neevaluabil la redeschidere"));
    }

    [Fact]
    public void A_context_that_does_not_match_the_timeline_is_not_mixed_with_it()
    {
        var (_, ws, _) = RunCase();
        var path = ws.FullPath(UserActivityContext.RelPath);
        var ctx = UserActivityContext.Read(path)!;
        ctx.Rows.RemoveAt(0);
        ctx.Write(path);
        var view = CaseLoader.Open(ws.Root, recheck: false).Case!.Result.UserActivityView();
        Assert.Contains("neevaluabil la redeschidere", view.Summary);
    }

    [Fact]
    public void The_context_file_keeps_the_removable_letters_and_the_folders()
    {
        var ctx = UserActivityContext.From([], [new ClassifiedFolderRow { Path = @"D:\Clasificat", Note = "n" }], ["E", "e"]);
        var p = Path.Combine(_dir, "ctx.json");
        ctx.Write(p);
        var back = UserActivityContext.Read(p)!;
        Assert.Equal(@"D:\Clasificat", back.ClassifiedFolders.Single().Path);
        Assert.Equal(["E"], back.RemovableLetters);
        Assert.Null(UserActivityContext.Read(Path.Combine(_dir, "nope.json")));
    }

    // ---- finding 2: known folders and network ----

    private static TimelineEvent Bag(string path, params (string, string)[] fields)
    {
        var e = new TimelineEvent { Time = Timestamp.Unknown(), Source = "ShellBags", EvidenceId = "EV-1", Path = path, Summary = "ShellBags " + path, Locator = "L" };
        foreach (var (k, v) in fields) e.Fields[k] = v;
        return e;
    }

    private const string Profile = @"C:\Users\ana";

    [Fact]
    public void Known_folder_roots_resolve_to_the_profile_folders_by_guid()
    {
        Assert.Equal(@"C:\Users\ana\Documents\Clasificat", KnownFolders.Resolve("{FDD39AD0-238F-46AF-ADB4-6C85480369C7}", ["Documente", "Clasificat"], Profile));
        Assert.Equal(@"C:\Users\ana\Documents\Clasificat", KnownFolders.Resolve("{450D8FBA-AD25-11D0-98A8-0800361B1103}", ["Documente", "Clasificat"], Profile));
        Assert.Equal(@"C:\Users\ana\Desktop\x\y", KnownFolders.Resolve("{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}", ["Desktop", "x", "y"], Profile));
        Assert.Equal(@"C:\Users\ana\Downloads", KnownFolders.Resolve("{374DE290-123F-4565-9164-39C4925E467B}", ["Descărcări"], Profile));
        Assert.Equal(@"C:\Users\ana\Pictures\a", KnownFolders.Resolve("{24AD3AD4-A569-4530-98E1-AB02F9417AA8}", ["Imagini", "a"], Profile));
        Assert.Equal(@"C:\Users\ana\Music", KnownFolders.Resolve("{4BD8D571-6D19-48D3-BE97-422220080E43}", ["Muzică"], Profile));
        Assert.Equal(@"C:\Users\ana\Videos", KnownFolders.Resolve("{A0953C92-50DC-43BF-BE83-3742FED03C9C}", ["Videoclipuri"], Profile));
        Assert.Equal(@"C:\Users\ana\Proiect", KnownFolders.Resolve("{59031A47-3F72-44A7-89C5-5595FE6B30EE}", ["Dosarul utilizatorului", "Proiect"], Profile));
        Assert.Null(KnownFolders.Resolve("{FDD39AD0-238F-46AF-ADB4-6C85480369C7}", ["Documente", "x"], ""));       // profile unknown: not guessed
        Assert.Null(KnownFolders.Resolve("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", ["Acest PC", @"C:\"], Profile));  // not a known-folder root
    }

    [Fact]
    public void The_network_root_maps_to_a_unc_path()
    {
        Assert.Equal(@"\\srv\share\Clasificat", KnownFolders.Resolve(KnownFolders.NetworkRootGuid, ["Rețea", "srv", "share", "Clasificat"], ""));
        Assert.Equal(@"\\srv\share\x", KnownFolders.Resolve(KnownFolders.NetworkRootGuid, ["Rețea", @"\\srv", @"\\srv\share", "x"], ""));
        Assert.Null(KnownFolders.Resolve(KnownFolders.NetworkRootGuid, ["Rețea"], ""));
    }

    [Fact]
    public void The_profile_folder_comes_from_the_collection_note_or_the_hive_path()
    {
        Assert.Equal(@"C:\Users\ana", KnownFolders.ProfileDirOf(@"\\?\GLOBALROOT\Device\X\Users\ana\NTUSER.DAT", @"Profil S-1-5-21-1-2-3-1001 (C:\Users\ana). Copiat din copia de umbră VSS."));
        Assert.Equal(@"C:\Users\ana", KnownFolders.ProfileDirOf(@"C:\Users\ana\NTUSER.DAT", ""));
        Assert.Equal(@"D:\Users\ana", KnownFolders.ProfileDirOf(@"D:\Users\ana\AppData\Local\Microsoft\Windows\UsrClass.dat", null));
        Assert.Equal("", KnownFolders.ProfileDirOf(@"\\?\GLOBALROOT\Device\X\Users\ana\NTUSER.DAT", ""));   // a device path is not a profile folder
        Assert.Equal("", KnownFolders.ProfileDirOf("NTUSER.DAT", ""));
    }

    [Fact]
    public void A_resolved_known_folder_matches_the_classified_root_and_the_displayed_path_is_kept()
    {
        var shown = @"Documente\Clasificat\Proiect";
        var events = new[] { Bag(shown, ("ResolvedPath", @"C:\Users\ana\Documents\Clasificat\Proiect")), Bag(shown) };
        var report = UserActivityAnalysis.Analyze(events, [new ClassifiedRoot(@"C:\Users\ana\Documents\Clasificat")]);
        var hit = report.Items.Single(i => i.ResolvedPath.Length > 0);
        Assert.Equal(@"C:\Users\ana\Documents\Clasificat", hit.ClassifiedRoot);
        Assert.Equal(shown, hit.Path);                       // as Explorer showed it
        Assert.Contains("rezolvată", hit.Relevance);
        Assert.Equal("", report.Items.Single(i => i.ResolvedPath.Length == 0).ClassifiedRoot);   // unresolved: no false claim
        var net = UserActivityAnalysis.Analyze([Bag(@"Rețea\srv\share\a", ("ResolvedPath", @"\\srv\share\a"))], [new ClassifiedRoot(@"\\srv\share")]);
        Assert.Equal(@"\\srv\share", net.Items.Single().ClassifiedRoot);
    }

    [Fact]
    public void Resolved_paths_are_not_taken_from_other_sources()
    {
        var e = new TimelineEvent { Time = Timestamp.Unknown(), Source = "OfficeMRU", EvidenceId = "E", Path = @"C:\x\a.docx", Summary = "s", Fields = { ["ResolvedPath"] = @"D:\Clasificat\a.docx" } };
        Assert.Equal("", UserActivityAnalysis.Analyze([e], [new ClassifiedRoot(@"D:\Clasificat")]).Items.Single().ClassifiedRoot);
    }

    // ---- finding 5: drive letter, control characters, volume ----

    [Theory]
    [InlineData(@"1:\x")]
    [InlineData(@"$:\x")]
    [InlineData("\u0000:", @"\x")]
    [InlineData("é:", @"\x")]
    public void A_non_letter_drive_is_not_a_drive(string head, string tail = "")
    {
        var path = head + tail;
        Assert.Equal("", ClassifiedAccess.Drive(path));
        var item = UserActivityAnalysis.Analyze([Bag(path)], [], ["E:"]).Items.Single();
        Assert.Equal(DriveMatch.None, item.Drive);
        Assert.Equal("", item.DriveLetter);
    }

    [Fact]
    public void A_letter_is_still_a_drive_in_either_case()
    {
        Assert.Equal("E:", ClassifiedAccess.Drive(@"e:\x"));
        Assert.Equal(DriveMatch.Removable, UserActivityAnalysis.Analyze([Bag(@"e:\x")], [], ["E:"]).Items.Single().Drive);
    }

    [Fact]
    public void Control_characters_in_paths_do_not_break_the_analysis_or_the_match()
    {
        var root = @"D:\Clasificat\Proiect";
        var events = new[]
        {
            Bag(root + @"\a" + "\u0000b.docx"), Bag(root + @"\" + "\u0007\r\n\t"), Bag("\u0000" + @":\x"), Bag("\u001B[31m" + @"E:\x"), Bag(""), Bag("\u202E" + root),
            Bag(root + @"\ " + "\u0085 \u2028 x"),
        };
        var report = UserActivityAnalysis.Analyze(events, [new ClassifiedRoot(root)], ["E:"]);
        Assert.Equal(events.Length, report.Total);
        Assert.Equal(3, report.Classified.Count);   // the three children of the root; the others do not match by accident
        Assert.DoesNotContain(report.Items, i => i.Drive == DriveMatch.Removable);
        var lines = UserActivityAnalysis.ReportLines(report);   // building the report text over such paths must not throw
        Assert.NotEmpty(lines);
        Assert.NotEmpty(report.Summary);
    }

    [Fact]
    public void Fifty_thousand_events_are_analysed_in_bounded_time()
    {
        var events = new List<TimelineEvent>(50_000);
        for (int n = 0; n < 50_000; n++)
        {
            var path = (n % 5) switch { 0 => $@"D:\Clasificat\Proiect\d{n}", 1 => $@"E:\d{n}", 2 => $@"F:\x\d{n}", 3 => $@"C:\Users\u\d{n}", _ => $@"Documente\Clasificat\d{n}" };
            events.Add(Bag(path, ("Account", "u")));
        }
        var roots = Enumerable.Range(0, 20).Select(i => new ClassifiedRoot($@"D:\Clasificat\Altul{i}")).Append(new ClassifiedRoot(@"D:\Clasificat\Proiect")).ToList();
        var sw = Stopwatch.StartNew();
        var report = UserActivityAnalysis.Analyze(events, roots, ["E:"]);
        var lines = UserActivityAnalysis.ReportLines(report);
        sw.Stop();
        Assert.Equal(50_000, report.Total);
        Assert.Equal(10_000, report.Classified.Count);
        Assert.Equal(10_000, report.Removable.Count);
        Assert.True(lines.Count < 100);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"prea lent: {sw.Elapsed}");
    }
}
