using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.9: bookmarks, annotations, saved views and view comparison on the case timeline, in a chained append-only log.</summary>
public sealed class TimelineNotebookTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_tl_{Guid.NewGuid():N}");
    private static readonly DateTime T0 = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase() =>
        CaseWorkspace.Create(Path.Combine(_root, "case"), new CaseInfo { CaseId = "CASE-TL", Name = "tl", CreatedAtUtc = DateTimeOffset.UtcNow, Host = "LAB-WS02", Scope = TestScopes.Valid() });

    private static TimelineEvent E(int minute, string source, string summary, string locator, string user = "") => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = source, EvidenceId = "EV-000001", Locator = locator, Summary = summary, User = user,
    };

    private static readonly TimelineEvent[] Events =
    [
        E(0, "Security", "Autentificare eșuată pentru ana", "rec 1", "ana"),
        E(5, "Security", "Autentificare reușită pentru ana", "rec 2", "ana"),
        E(9, "USB", "Disc USB conectat", "row 7"),
        E(30, "PowerShell", "Script codificat rulat", "rec 40", "bob"),
    ];

    [Fact]
    public void The_event_key_is_stable_across_a_reread_and_does_not_depend_on_row_order()
    {
        var copy = E(5, "Security", "Autentificare reușită pentru ana", "rec 2", "ana");
        Assert.Equal(TimelineNotebook.EventKey(Events[1]), TimelineNotebook.EventKey(copy));
        Assert.NotEqual(TimelineNotebook.EventKey(Events[0]), TimelineNotebook.EventKey(Events[1]));
        Assert.Equal(20, TimelineNotebook.EventKey(Events[0]).Length);
    }

    [Fact]
    public void Filters_combine_text_sources_interval_and_bookmarks_and_an_event_without_time_is_left_out_of_an_interval()
    {
        var noTime = new TimelineEvent { Time = Timestamp.Unknown("??"), Source = "Security", EvidenceId = "EV-000001", Summary = "fără oră ana" };
        TimelineEvent[] all = [.. Events, noTime];
        Assert.Equal(3, TimelineNotebook.Apply(all, new TimelineViewSpec(Text: "ana")).Count());
        Assert.Equal(2, TimelineNotebook.Apply(all, new TimelineViewSpec(Text: "ana", FromUtc: T0)).Count());
        Assert.Single(TimelineNotebook.Apply(all, new TimelineViewSpec(Sources: ["usb"])));
        var marked = new HashSet<string> { TimelineNotebook.EventKey(Events[3]) };
        Assert.Equal("Script codificat rulat", Assert.Single(TimelineNotebook.Apply(all, new TimelineViewSpec(OnlyBookmarked: true), marked)).Summary);
    }

    [Fact]
    public void Bookmarks_annotations_and_views_are_kept_in_the_chained_log_and_survive_reopening()
    {
        var ws = NewCase();
        var nb = new TimelineNotebook(ws);
        var k = TimelineNotebook.EventKey(Events[3]);
        Assert.True(nb.Bookmark("ana", k, "execuție suspectă").Ok);
        Assert.Contains("deja marcat", nb.Bookmark("ana", k).Reason);
        Assert.True(nb.Annotate("ana", k, "Corelat cu sarcina T001.").Ok);
        Assert.True(nb.SaveView("ana", "Conturi ana", new TimelineViewSpec(Text: "ana", Sources: ["Security"])).Ok);

        var s = new TimelineNotebook(CaseWorkspace.Open(ws.Root, recheck: false)).Load();
        Assert.Equal(ChainStatus.Valid, s.Chain!.Status);
        Assert.Equal("execuție suspectă", s.Bookmarks[k].Label);
        Assert.Equal("Corelat cu sarcina T001.", Assert.Single(s.NotesOf(k)).Text);
        Assert.Equal("text „ana”; surse Security", s.Views["Conturi ana"].Describe());
        Assert.Contains("timeline.note.bookmark.add", File.ReadAllText(ws.AppAuditLogPath));
    }

    [Fact]
    public void Two_saved_views_are_compared_by_event()
    {
        var c = TimelineNotebook.Compare("ana", new TimelineViewSpec(Text: "ana"), "Security", new TimelineViewSpec(Sources: ["Security"]), Events);
        Assert.Equal((0, 0, 2), (c.OnlyA, c.OnlyB, c.Both));
        c = TimelineNotebook.Compare("prima oră", new TimelineViewSpec(ToUtc: T0.AddMinutes(10)), "Security", new TimelineViewSpec(Sources: ["Security"]), Events);
        Assert.Equal((1, 0, 2), (c.OnlyA, c.OnlyB, c.Both));
    }

    [Fact]
    public void Invalid_input_is_refused_and_audited()
    {
        var ws = NewCase();
        var nb = new TimelineNotebook(ws);
        Assert.Contains("eveniment necunoscut", nb.Bookmark("ana", "../x").Reason);
        Assert.Contains("nota este goală", nb.Annotate("ana", TimelineNotebook.EventKey(Events[0]), " ").Reason);
        Assert.Contains("intervalul este inversat", nb.SaveView("ana", "v", new TimelineViewSpec(FromUtc: T0.AddDays(1), ToUtc: T0)).Reason);
        Assert.Contains("vedere necunoscută", nb.DeleteView("ana", "nu există").Reason);
        Assert.Contains("timeline.note.refused", File.ReadAllText(ws.AppAuditLogPath));
    }

    [Fact]
    public void A_truncated_or_deleted_notes_log_is_broken_and_refuses_writes_and_a_read_only_case_refuses_too()
    {
        var ws = NewCase();
        var nb = new TimelineNotebook(ws);
        Assert.True(nb.Bookmark("ana", TimelineNotebook.EventKey(Events[0])).Ok);
        Assert.True(nb.Bookmark("ana", TimelineNotebook.EventKey(Events[1])).Ok);
        var lines = File.ReadAllLines(nb.LogPath);
        File.WriteAllLines(nb.LogPath, lines[..^1]);
        Assert.Equal("jurnal trunchiat", nb.Load().Chain!.Reason);
        Assert.Contains("compromise", nb.Bookmark("ana", TimelineNotebook.EventKey(Events[2])).Reason);
        File.Delete(nb.LogPath);
        Assert.Equal("jurnal lipsă", nb.Load().Chain!.Reason);

        var ws2 = CaseWorkspace.Create(Path.Combine(_root, "case2"), new CaseInfo { CaseId = "C2", Name = "c2", CreatedAtUtc = DateTimeOffset.UtcNow, Host = "H", Scope = TestScopes.Valid() });
        ws2.MarkReadOnly("sigilat");
        Assert.Contains("doar pentru citire", new TimelineNotebook(ws2).Bookmark("ana", TimelineNotebook.EventKey(Events[0])).Reason);
    }

    [Fact]
    public void Other_timeline_audit_actions_such_as_a_masked_export_do_not_count_as_notes()
    {
        var ws = NewCase();
        var nb = new TimelineNotebook(ws);
        ws.Audit("timeline.export_masked", "cronologie.csv rows=4 pseudonime=2");
        Assert.Null(nb.Load().Chain);                                         // no notes yet, and no false "jurnal lipsă"
        Assert.True(nb.Bookmark("ana", TimelineNotebook.EventKey(Events[0])).Ok);
        ws.Audit("timeline.export_masked", "again");
        Assert.Equal(ChainStatus.Valid, nb.Load().Chain!.Status);
        Assert.True(nb.Annotate("ana", TimelineNotebook.EventKey(Events[0]), "notă după export").Ok);
    }
}
