using System.IO;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// The Open/Save dialog lists (OpenSaveMRU, LastVisitedMRU) in the user-activity view and the USER-ACTIVITY-* rules (2026-11-18). CIDSizeMRU names a program,
/// not a path: it stays out. Synthetic events only.
/// </summary>
public sealed class UserActivityDialogListTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static TimelineEvent Mru(string source, string path, DateTimeOffset? time, string exe = "notepad.exe", int pos = 0, string resolved = "", bool listed = true, string ext = "*")
    {
        var e = new TimelineEvent
        {
            Time = time is { } t ? new Timestamp(t, "", "test") : Timestamp.Unknown(), Source = source, EvidenceId = "EV-7", Path = path, Process = exe,
            Summary = $"Fereastra Deschide/Salvează ({source}): {path}", Locator = $@"NTUSER\ComDlg32\{source}\{pos}",
            Fields =
            {
                ["ExeName"] = exe, ["MruPosition"] = pos.ToString(System.Globalization.CultureInfo.InvariantCulture), ["MruListed"] = listed ? "true" : "false",
                ["Account"] = "utilizator", ["Extension"] = source == UserActivityAnalysis.OpenSaveMru ? ext : "",
            },
        };
        if (resolved.Length > 0) e.Fields["ResolvedPath"] = resolved;
        return e;
    }

    private static readonly ClassifiedFolderRow[] Folders = [new() { Path = @"D:\Clasificat", Note = "test" }];

    [Fact]
    public void Open_save_rows_join_the_view_and_cid_size_rows_do_not()
    {
        var events = new List<TimelineEvent>
        {
            Mru(UserActivityAnalysis.OpenSaveMru, @"D:\Clasificat\plan.docx", T0),
            Mru(UserActivityAnalysis.LastVisitedMru, @"E:\Export", T0.AddMinutes(5), exe: "winword.exe"),
            new() { Time = Timestamp.Unknown(), Source = "CIDSizeMRU", EvidenceId = "EV-7", Path = "", Process = "calc.exe", Summary = "s", Locator = "L" },
        };
        var r = Wp14Rules.UserActivityForCase(events, Folders, ["E"]);
        Assert.Equal(2, r.Total);
        Assert.DoesNotContain(r.Items, i => i.Source == "CIDSizeMRU");
        Assert.True(UserActivityAnalysis.IsSource(UserActivityAnalysis.OpenSaveMru));
        Assert.False(UserActivityAnalysis.IsSource("CIDSizeMRU"));
        Assert.Contains("fișiere alese în Deschide/Salvează 1", r.Summary);
        Assert.Contains("foldere de program din Deschide/Salvează 1", r.Summary);

        var open = Assert.Single(r.Classified);
        Assert.Equal(@"D:\Clasificat", open.ClassifiedRoot, ignoreCase: true);
        Assert.Equal("OpenSaveMRU (fișier ales în fereastra Deschide/Salvează)", open.SourceLabel);
        Assert.Contains("se aplică doar celei mai recente alegeri", open.TimeMeaning);
        Assert.Contains("programul „notepad.exe”", open.Detail);
        Assert.Contains("poziția 1 în listă", open.Detail);
        Assert.Contains("nu dovedește că fișierul a fost citit sau scris", open.Detail);

        var folder = Assert.Single(r.Removable);
        Assert.Equal(UserActivityAnalysis.LastVisitedMru, folder.Source);
        Assert.Contains("winword.exe", folder.Detail);
    }

    [Fact]
    public void An_undated_or_unlisted_dialog_entry_says_so()
    {
        var r = UserActivityAnalysis.Analyze([Mru(UserActivityAnalysis.OpenSaveMru, @"C:\x\a.txt", null, pos: 3, listed: false)]);
        var i = Assert.Single(r.Items);
        Assert.Equal("fără oră", i.TimeText);
        Assert.Contains("fără oră proprie", i.TimeMeaning);
        Assert.Contains("neinclusă în ordinea listei", i.Detail);
    }

    [Fact]
    public void A_resolved_known_folder_path_is_matched_but_the_drive_comes_from_the_displayed_path()
    {
        // the dialog showed "Documente\plan.docx"; the known folder resolves under the classified root
        var e = Mru(UserActivityAnalysis.OpenSaveMru, @"Documente\plan.docx", T0, resolved: @"D:\Clasificat\plan.docx");
        var r = Wp14Rules.UserActivityForCase([e], Folders, []);
        var i = Assert.Single(r.Classified);
        Assert.Equal(@"D:\Clasificat\plan.docx", i.ResolvedPath);
        Assert.Equal(DriveMatch.None, i.Drive);
    }

    [Fact]
    public void The_rules_fire_on_dialog_lists_alone_and_say_what_a_dialog_entry_does_not_prove()
    {
        var events = new List<TimelineEvent>
        {
            Mru(UserActivityAnalysis.OpenSaveMru, @"D:\Clasificat\plan.docx", T0),
            Mru(UserActivityAnalysis.OpenSaveMru, @"E:\copie\plan.docx", T0.AddMinutes(2)),
        };
        int n = 0;
        var findings = Wp14Rules.Run([.. events], () => $"F-{++n:D4}", new Wp14Input { Scope = new LogAnalyzer.Dfir.Model.CaseScope(), Profile = new ProcedureProfile { ClassifiedFolders = [.. Folders] } });
        var classified = Assert.Single(findings, f => f.RuleId == "USER-ACTIVITY-CLASSIFIED");
        Assert.Contains("Deschide/Salvează 1", classified.Description);
        Assert.Contains("nu că a fost citit sau scris", classified.Description);
        Assert.Equal(Classification.Candidate, classified.Classification);
        var removable = Assert.Single(findings, f => f.RuleId == "USER-ACTIVITY-REMOVABLE");
        Assert.Contains("într-o fereastră a unui program", removable.Description);
    }

    [Fact]
    public void A_context_written_before_the_dialog_lists_still_reopens_evaluable()
    {
        var shell = new TimelineEvent { Time = Timestamp.Unknown(), Source = "ShellBags", EvidenceId = "EV-1", Path = @"D:\Clasificat", Summary = "s", Locator = "L", Fields = { ["ShellItemType"] = "0x31" } };
        var dialog = Mru(UserActivityAnalysis.OpenSaveMru, @"D:\Clasificat\a.docx", T0);
        // an older build saved only the four original sources, as schema 1.0
        var old = UserActivityContext.From([shell], Folders, []);
        old.SchemaVersion = "1.0";
        var reopened = new List<TimelineEvent>
        {
            new() { Time = Timestamp.Unknown(), Source = "ShellBags", EvidenceId = "EV-1", Path = @"D:\Clasificat", Summary = "s", Locator = "L" },
            new() { Time = dialog.Time, Source = dialog.Source, EvidenceId = dialog.EvidenceId, Path = dialog.Path, Summary = dialog.Summary, Locator = dialog.Locator },
        };
        Assert.True(old.TryRestoreFields(reopened));
        Assert.Equal("0x31", reopened[0].Fields["ShellItemType"]);

        // a current context carries the dialog rows too, and a "1.1" context missing one does not match
        var current = UserActivityContext.From([shell, dialog], Folders, []);
        Assert.Equal(UserActivityContext.CurrentSchemaVersion, current.SchemaVersion);
        Assert.Equal(2, current.Rows.Count);
        var withoutDialog = UserActivityContext.From([shell], Folders, []);
        Assert.False(withoutDialog.TryRestoreFields(reopened));
    }

    [Fact]
    public void One_choice_written_to_the_star_list_and_to_its_extension_list_is_counted_once()
    {
        var events = new List<TimelineEvent>
        {
            Mru(UserActivityAnalysis.OpenSaveMru, @"D:\Clasificat\plan.docx", T0.AddMinutes(1), ext: "*"),
            Mru(UserActivityAnalysis.OpenSaveMru, @"D:\Clasificat\plan.docx", null, ext: "docx", pos: 2),
            Mru(UserActivityAnalysis.OpenSaveMru, @"D:\Clasificat\alt.docx", null, ext: "docx", pos: 3),
        };
        var r = Wp14Rules.UserActivityForCase(events, Folders, []);
        Assert.Equal(2, r.Total);
        var plan = Assert.Single(r.Items, i => i.Path.EndsWith("plan.docx", StringComparison.Ordinal));
        Assert.Equal(T0.AddMinutes(1), plan.TimeUtc);                                  // the dated row is kept: its time stays tied to its own list
        Assert.Contains("aceeași cale apare în 2 liste (toate extensiile, extensia docx)", plan.Detail);
        int n = 0;
        var f = Assert.Single(Wp14Rules.Run([.. events], () => $"F-{++n:D4}", new Wp14Input { Scope = new LogAnalyzer.Dfir.Model.CaseScope(), Profile = new ProcedureProfile { ClassifiedFolders = [.. Folders] } }),
                              x => x.RuleId == "USER-ACTIVITY-CLASSIFIED");
        Assert.Contains("(2)", f.Title);
    }

    [Fact]
    public void A_finding_built_only_from_dialog_lists_does_not_say_a_folder_was_shown_or_a_document_opened()
    {
        int n = 0;
        var events = new List<TimelineEvent> { Mru(UserActivityAnalysis.OpenSaveMru, @"D:\Clasificat\plan.docx", T0), Mru(UserActivityAnalysis.OpenSaveMru, @"F:\date\x.txt", T0) };
        var findings = Wp14Rules.Run([.. events], () => $"F-{++n:D4}", new Wp14Input { Scope = new LogAnalyzer.Dfir.Model.CaseScope(), Profile = new ProcedureProfile { ClassifiedFolders = [.. Folders] } });
        var c = Assert.Single(findings, x => x.RuleId == "USER-ACTIVITY-CLASSIFIED");
        Assert.Contains("listele ferestrei Deschide/Salvează", c.Title);
        Assert.Contains("alegere într-o fereastră Deschide/Salvează", c.PlainWhat);
        Assert.DoesNotContain("document deschis", c.PlainWhat);
        Assert.DoesNotContain("navigarea sau deschiderea", c.ClassificationReason);
        var rem = Assert.Single(findings, x => x.RuleId == "USER-ACTIVITY-REMOVABLE");
        Assert.DoesNotContain("; Listele", c.Description);
        Assert.Contains("alegerea unui fișier sau folder într-o fereastră", rem.PlainWhat);
        Assert.DoesNotContain("afișarea unui folder", rem.PlainWhat);
    }

    [Fact]
    public void Without_dialog_rows_the_rule_texts_are_the_original_ones()
    {
        int n = 0;
        var bag = new TimelineEvent { Time = new Timestamp(T0, "", "test"), Source = "ShellBags", EvidenceId = "EV-1", Path = @"E:\Documente", Summary = "s", Locator = @"UsrClass\BagMRU\0", Fields = { ["Account"] = "utilizator" } };
        var cls = new TimelineEvent { Time = new Timestamp(T0, "", "test"), Source = "ShellBags", EvidenceId = "EV-1", Path = @"D:\Clasificat", Summary = "s", Locator = @"UsrClass\BagMRU\1", Fields = { ["Account"] = "utilizator" } };
        var usb = new TimelineEvent { Time = new Timestamp(T0, "", "test"), Source = "USB", EvidenceId = "EV-9", Summary = "USB", Locator = "L", Fields = { ["DriveLetter"] = "E:" } };
        var findings = Wp14Rules.Run([bag, cls, usb], () => $"F-{++n:D4}", new Wp14Input { Scope = new LogAnalyzer.Dfir.Model.CaseScope(), Profile = new ProcedureProfile { ClassifiedFolders = [.. Folders] } });
        var c = Assert.Single(findings, x => x.RuleId == "USER-ACTIVITY-CLASSIFIED");
        Assert.Contains("RecentDocs păstrează doar numele. ShellBags (folder", c.Description);
        Assert.Contains("apare în urmele Explorer / documentelor recente", c.Title);
        Assert.Contains("arată navigarea sau deschiderea, nu accesul la conținut", c.ClassificationReason);
        Assert.DoesNotContain("Deschide/Salvează", c.Description + c.Title + c.ClassificationReason + c.PlainWhat);
        Assert.NotEmpty(findings.Where(x => x.RuleId == "USER-ACTIVITY-REMOVABLE"));   // E: is at least "possible": the loop below is not vacuous
        foreach (var r in findings.Where(x => x.RuleId == "USER-ACTIVITY-REMOVABLE"))
        {
            Assert.Contains("ShellBags arată că Explorer a afișat folderul, nu că un fișier a fost citit sau copiat. ", r.Description);
            Assert.DoesNotContain("Deschide/Salvează", r.Description + r.Title + r.PlainWhat);
        }
    }
}