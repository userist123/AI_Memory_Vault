using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.App.Tests.GapsTestKit;

namespace LogAnalyzer.App.Tests;

/// <summary>E5 „Ce a urmat după conectare” (window −10 min … +2 h, scored, capped, other users excluded) and E1 connection history with the earliest evidence.</summary>
public sealed class AfterConnectAndHistoryTests
{
    private static CaseScope AirGap() => new() { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Unclassified };

    private static List<TimelineEvent> Scenario() =>
    [
        Partition1006(0), UsbRow(0, "E"),
        Logon("0x1", "op-a", -30),
        Ev("Prefetch", 5, path: @"E:\tool.exe"),                                                   // run from the medium, 5 min after: top score
        Ev("EventLog:Security", 10, "4688", f: [("NewProcessName", @"C:\Windows\notepad.exe"), ("SubjectUserName", "op-b")]),   // another user: excluded
        Ev("EventLog:Security", 11, "4688", f: [("NewProcessName", @"C:\Windows\System32\svchost.exe"), ("SubjectUserName", "SYSTEM")]),   // system: excluded
        Ev("LNK", 20, path: @"E:\docs\plan.docx", user: "op-a"),
        Ev("BrowserDownload", 60, path: @"C:\Users\op-a\Downloads\a.zip"),
        Ev("Prefetch", -5, path: @"C:\Windows\explorer.exe"),                                      // inside the 10 minutes before
        Ev("Prefetch", -20, path: @"C:\Windows\early.exe"),                                        // outside the window
        Ev("BrowserDownload", 180, path: @"C:\Users\op-a\Downloads\late.zip"),                     // outside the window
    ];

    [Fact]
    public void Window_keeps_events_from_minus_10_minutes_to_plus_2_hours_and_excludes_other_users_when_the_session_user_is_known()
    {
        var w = AfterConnect.Window(Scenario(), Serial, T0, null, "E");
        Assert.Equal("op-a", w.SessionUser);
        Assert.Equal(2, w.ExcludedOtherUsers);
        Assert.Equal(4, w.Candidates);
        var whats = w.Items.Select(i => i.What).ToList();
        Assert.Contains(@"E:\tool.exe", whats);
        Assert.Contains(@"E:\docs\plan.docx", whats);
        Assert.Contains(@"C:\Users\op-a\Downloads\a.zip", whats);
        Assert.Contains(@"C:\Windows\explorer.exe", whats);
        Assert.DoesNotContain(@"C:\Windows\early.exe", whats);
        Assert.DoesNotContain(@"C:\Users\op-a\Downloads\late.zip", whats);
        Assert.DoesNotContain(@"C:\Windows\notepad.exe", whats);
        var top = w.Items.OrderByDescending(i => i.Score).First();
        Assert.Equal(@"E:\tool.exe", top.What);                                                  // on the medium's letter, soon after
        Assert.True(w.Items.Single(i => i.What.EndsWith("explorer.exe")).Score < top.Score);    // before the connection scores lower
        Assert.Equal(AfterConnectKind.Execution, top.Kind);
    }

    [Fact]
    public void Without_a_known_session_user_nothing_is_excluded()
    {
        var events = Scenario().Where(e => e.EventId != "4624").ToList();
        var w = AfterConnect.Window(events, Serial, T0, null, "E");
        Assert.Equal("", w.SessionUser);
        Assert.Equal(0, w.ExcludedOtherUsers);
        Assert.Contains(w.Items, i => i.What.EndsWith("notepad.exe"));
        Assert.Contains("utilizator necunoscut", w.Heading);
    }

    [Fact]
    public void Window_is_capped_and_says_so()
    {
        var events = new List<TimelineEvent> { Partition1006(0) };
        for (int i = 0; i < 40; i++) events.Add(Ev("Prefetch", 1 + i, path: $@"C:\p\app{i}.exe"));
        var w = AfterConnect.Window(events, Serial, T0, "", "");
        Assert.Equal(40, w.Candidates);
        Assert.Equal(AfterConnect.Cap, w.Items.Count);
        Assert.True(w.Capped);
        Assert.Contains($"afișate primele {AfterConnect.Cap}", w.Heading);
    }

    [Fact]
    public void Rules_add_one_after_connect_finding_per_medium_linked_to_the_medium_findings()
    {
        int n = 0;
        var findings = Wp14Rules.Run(Scenario(), () => $"F-{++n:D4}", new Wp14Input { Scope = AirGap() });
        var after = Assert.Single(findings, f => f.RuleId == "MEDIA-AFTER-CONNECT");
        Assert.Contains(Serial, after.Title);
        Assert.Contains(@"E:\tool.exe", after.Description);
        Assert.DoesNotContain("notepad.exe", after.Description);
        Assert.Equal("op-a", after.User);
        Assert.NotEmpty(after.SupportingEvidence);
        Assert.Contains(after.AlternativeExplanations, a => a.Contains("nu dovedește"));
        var mediumFindings = findings.Where(f => f.RuleId.StartsWith("MEDIA", StringComparison.Ordinal) && f.RuleId is not ("MEDIA-AFTER-CONNECT" or "MEDIA-HISTORY") && (f.Title.Contains(Serial) || f.Description.Contains(Serial))).ToList();
        Assert.NotEmpty(mediumFindings);
        Assert.All(mediumFindings, f => Assert.Contains(after.FindingId, f.RelatedFindingIds));
        Assert.All(mediumFindings, f => Assert.Contains(f.FindingId, after.RelatedFindingIds));
    }

    [Fact]
    public void Medium_detail_shows_what_followed_and_the_history_or_offers_collect_when_there_is_no_analysis()
    {
        var details = LogAnalyzer.UI.ViewModels.MediaCenterViewModel.AnalysisDetails(Serial, Scenario());
        Assert.Contains(details, d => d.Label.StartsWith("Ce a urmat după conectare") && d.Value.Contains(@"E:\tool.exe"));
        Assert.Contains(details, d => d.Label.StartsWith("Istoricul conectărilor") && d.Value.Contains("Cea mai veche dovadă"));
        var empty = Assert.Single(LogAnalyzer.UI.ViewModels.MediaCenterViewModel.AnalysisDetails(Serial, null));
        Assert.Equal(LogAnalyzer.UI.Services.CollectAllState.EmptyText, empty.Value);
    }

    // ---- E1: history from every source, earliest evidence first, origin of archived / Windows.old sources ----

    [Fact]
    public void History_merges_every_source_and_names_the_earliest_evidence_with_its_origin()
    {
        var events = new List<TimelineEvent>
        {
            Partition1006(0, "EV-LIVE"),
            Partition1006(-60 * 24 * 40, "EV-ARCH"),                                                       // 40 days earlier, from an archived log
            Ev("EventLog:Security", -60 * 24 * 300, "6416", "EV-OLD", f: [("DeviceId", $@"USBSTOR\DISK&VEN_V&PROD_S\{Serial}"), ("SubjectUserName", "op-a")]),   // Windows.old
            UsbRow(-10, evidence: "EV-HIVE"),
        };
        var origins = new Dictionary<string, string> { ["EV-ARCH"] = "jurnal arhivat", ["EV-OLD"] = "Windows.old (instalare anterioară)" };
        var h = Wp14Rules.History(events, Serial, origins);
        Assert.Equal(4, h.Count);
        Assert.Equal("Security 6416", h[0].Source);
        Assert.Equal("Windows.old (instalare anterioară)", h[0].Origin);
        Assert.True(h.Zip(h.Skip(1)).All(p => p.First.WhenUtc <= p.Second.WhenUtc));
        Assert.Contains(h, t => t.Origin == "jurnal arhivat");

        int n = 0;
        var findings = Wp14Rules.Run(events, () => $"F-{++n:D4}", new Wp14Input { Scope = AirGap(), EvidenceOrigins = origins });
        var hist = Assert.Single(findings, f => f.RuleId == "MEDIA-HISTORY");
        Assert.Contains($"prima dovadă {h[0].WhenUtc:yyyy-MM-dd HH:mm} UTC", hist.Title);
        Assert.Contains("Cea mai veche dovadă", hist.Description);
        Assert.Contains("surse istorice", hist.Description);
        Assert.Equal(h[0].WhenUtc, hist.FirstSeenUtc);
    }

    [Theory]
    [InlineData(@"C:\img\Windows\System32\winevt\Logs\Archive-Security-2026-01-02-03-04-05-678.evtx", "jurnal arhivat")]
    [InlineData(@"D:\copy\Windows.old\Windows\System32\config\SYSTEM", "Windows.old (instalare anterioară)")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy3\Windows\System32\winevt\Logs\System.evtx", "copie de umbră (VSS) 3")]
    [InlineData(@"C:\Windows\System32\winevt\Logs\System.evtx", "")]
    public void Evidence_origin_labels_archived_logs_windows_old_and_shadow_copies(string path, string expected) =>
        Assert.Equal(expected, EvidenceOrigin.Label(path));
}
