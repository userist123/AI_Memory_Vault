using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using static LogAnalyzer.Dfir.Tests.Synthetic.ShellItemBuilder;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// E7 slice 3, final owner decisions: the drive letter comes only from the displayed path; known folders are placed only through the collection note of an item
/// collected on the station; the known-folder GUID anchors the resolution at any depth (Explorer's "Acest PC" layout). Synthetic hives only.
/// </summary>
public sealed class UserActivityFinalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_uafin_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Shell = new(2026, 8, 20, 10, 0, 2);
    private const string BagRoot = @"Local Settings\Software\Microsoft\Windows\Shell\BagMRU";
    private const uint End = 0xFFFFFFFF;
    private static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private const string ProfileDir = @"C:\Users\ana";
    private const string CollectedNote = @"Profil S-1-5-21-1-2-3-1001 (" + ProfileDir + @"). Copiat din copia de umbră VSS.";
    private const string NotPlaced = "nu au putut fi plasate pe discul stației";

    public UserActivityFinalTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static RegfBuilder.Key Child(RegfBuilder.Key parent, string name, DateTime t)
    {
        var k = new RegfBuilder.Key(name, t);
        parent.Children.Add(k);
        return k;
    }

    /// <summary>
    /// A UsrClass.dat with two roots: "Acest PC" -> Documents -> Clasificat (Explorer's default layout, the known folder one level below the first item) and
    /// Documents -> Direct (a known folder at the root level).
    /// </summary>
    private string WriteHive()
    {
        var root = new RegfBuilder.Key("ROOT", T0);
        var bag = root;
        foreach (var part in BagRoot.Split((char)92)) bag = Child(bag, part, T0);
        bag.Value("0", RegfBuilder.Binary, List(Root(ThisPc)));
        bag.Value("1", RegfBuilder.Binary, List(Root(Documents)));
        bag.Value("MRUListEx", RegfBuilder.Binary, Mru(0, 1, End));
        var pc = Child(bag, "0", T0.AddDays(1));
        pc.Value("0", RegfBuilder.Binary, List(Root(Documents)));
        pc.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var pcDocs = Child(pc, "0", T0.AddDays(2));
        pcDocs.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "Clasificat", 0, Shell)));
        pcDocs.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var direct = Child(bag, "1", T0.AddDays(3));
        direct.Value("0", RegfBuilder.Binary, List(FileEntry(0x31, "Direct", 0, Shell)));
        direct.Value("MRUListEx", RegfBuilder.Binary, Mru(0, End));
        var p = Path.Combine(_dir, "UsrClass.dat");
        File.WriteAllBytes(p, RegfBuilder.Build(root, "UsrClass.dat"));
        return p;
    }

    private static List<TimelineEvent> Parse(string path, string collector, string method, string notes, string original = @"C:\Users\ana\AppData\Local\Microsoft\Windows\UsrClass.dat")
    {
        var item = new EvidenceItem { EvidenceId = "EV-U", CaseId = "C", Source = "usrclass_hive", SourceType = "usrclass_hive", StoredPath = path, OriginalPath = original, Collector = collector, AcquisitionMethod = method, Notes = notes };
        var sink = new ListSink();
        new ShellBagsParser().Parse(item, path, sink, default);
        return [.. sink.Events];
    }

    private static TimelineEvent Row(List<TimelineEvent> events, string bagPath) => events.Single(e => e.Fields["BagPath"] == bagPath);

    private static string Fld(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";

    // ---- decision 3: the known-folder GUID at any depth ----

    [Fact]
    public void A_known_folder_below_This_PC_anchors_the_resolution_and_the_classified_rule_fires()
    {
        var events = Parse(WriteHive(), RecycleBinInfoParser.CollectedBy, "acquired", CollectedNote);
        var below = Row(events, @"Acest PC\Documente\Clasificat");
        Assert.Equal(ProfileDir + @"\Documents\Clasificat", Fld(below, "ResolvedPath"));
        Assert.Equal(ProfileDir + @"\Documents", Fld(Row(events, @"Acest PC\Documente"), "ResolvedPath"));
        Assert.Equal("", Fld(Row(events, "Acest PC"), "ResolvedPath"));                           // This PC itself is not a known folder
        Assert.Equal(ProfileDir + @"\Documents\Direct", Fld(Row(events, @"Documente\Direct"), "ResolvedPath"));   // a direct root-level item

        var classified = ProfileDir + @"\Documents\Clasificat";
        int n = 0;
        var findings = Wp14Rules.Run(events, () => $"F-{++n:D4}", new Wp14Input
        {
            Scope = new CaseScope(),
            Profile = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = classified }] },
        });
        Assert.Contains(findings, f => f.RuleId == "USER-ACTIVITY-CLASSIFIED");
        var view = UserActivityAnalysis.Analyze(events, [new ClassifiedRoot(classified)]);
        Assert.Contains(view.Items, i => i.ResolvedPath == classified && i.ClassifiedRoot == classified);
    }

    // ---- decision 2: profile folder only from the collection note of a collected item ----

    [Fact]
    public void An_imported_hive_does_not_resolve_known_folders_even_with_a_crafted_profile_note()
    {
        var p = WriteHive();
        foreach (var events in new[]
        {
            Parse(p, "Import", "imported", CollectedNote),   // crafted note on an import
            Parse(p, "Import", "imported", ""),              // the examiner's own path
        })
        {
            Assert.All(events, e => Assert.Equal("", Fld(e, "ResolvedPath")));
            Assert.Contains(NotPlaced, Fld(Row(events, @"Acest PC\Documente\Clasificat"), "ResolutionNote"));
            Assert.Contains(NotPlaced, Fld(Row(events, @"Documente\Direct"), "ResolutionNote"));
            Assert.Equal("", Fld(Row(events, "Acest PC"), "ResolutionNote"));                 // a row that is not under a known folder carries no note
            var view = UserActivityAnalysis.Analyze(events, [new ClassifiedRoot(ProfileDir + @"\Documents\Clasificat")]);
            Assert.All(view.Items, i => Assert.Equal("", i.ClassifiedRoot));
            Assert.Contains(view.Items, i => i.Detail.Contains(NotPlaced));                   // the view says why
        }
    }

    [Fact]
    public void A_device_prefixed_profile_note_is_refused_and_the_row_says_so()
    {
        var events = Parse(WriteHive(), RecycleBinInfoParser.CollectedBy, "acquired", @"Profil S-1-5-21-1-2-3-1001 (\\?\GLOBALROOT\Device\X\Users\ana). Copiat.");
        Assert.All(events, e => Assert.Equal("", Fld(e, "ResolvedPath")));
        Assert.Contains(NotPlaced, Fld(Row(events, @"Documente\Direct"), "ResolutionNote"));
    }

    // ---- decision 1: the drive letter only from the displayed path ----

    [Fact]
    public void A_resolved_path_on_a_removable_letter_does_not_make_a_documents_row_removable()
    {
        // a collected profile on F:, e.g. the station's data volume: Documents rows are resolved to F:\..., but Explorer displayed no drive
        var events = Parse(WriteHive(), RecycleBinInfoParser.CollectedBy, "acquired", @"Profil S-1-5-21-1-2-3-1001 (F:\Users\ana). Copiat.", @"F:\Users\ana\NTUSER.DAT");
        Assert.StartsWith(@"F:\Users\ana\Documents", Fld(Row(events, @"Documente\Direct"), "ResolvedPath"));
        var report = UserActivityAnalysis.Analyze(events, [], ["F:"]);
        Assert.All(report.Items, i => { Assert.Equal(DriveMatch.None, i.Drive); Assert.Equal("", i.DriveLetter); });
        Assert.Empty(report.Removable);
        Assert.Empty(report.PossibleRemovable);

        // the same through the analysis directly: the displayed path has no drive, the resolved one has
        var shown = new TimelineEvent { Time = Timestamp.Unknown(), Source = "ShellBags", EvidenceId = "E", Path = @"Documente\x", Summary = "s", Locator = "L", Fields = { ["ResolvedPath"] = @"F:\Users\ana\Documents\x" } };
        foreach (var removable in new[] { new[] { "F:" }, Array.Empty<string>() })
        {
            var item = UserActivityAnalysis.Analyze([shown], [], removable).Items.Single();
            Assert.Equal(DriveMatch.None, item.Drive);
            Assert.DoesNotContain("amovibil", item.Relevance);
        }

        // a displayed drive letter still counts
        var onF = new TimelineEvent { Time = Timestamp.Unknown(), Source = "ShellBags", EvidenceId = "E", Path = @"F:\x", Summary = "s", Locator = "L" };
        Assert.Equal(DriveMatch.Removable, UserActivityAnalysis.Analyze([onF], [], ["F:"]).Items.Single().Drive);
        Assert.Equal(DriveMatch.Possible, UserActivityAnalysis.Analyze([onF], [], []).Items.Single().Drive);
    }
}
