using System.Globalization;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// NTFS tunnelling (Corpus/NtfsCalib scenarios L-N2): a save through a temporary file, File.Replace and delete + recreate must not give a High (nor, with a USN, a Medium).
/// With only the $MFT a mismatch on a record that has a short (8.3) name is Low, not Medium. A-E stay detected as before.
/// </summary>
public sealed partial class NtfsTimestompTunnelTests
{
    private const string RuleId = "NTFS-TIMESTOMP";
    private static readonly string[] Names = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "L2", "M2", "N2"];

    private static string Calib(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "NtfsCalib", n);

    private sealed record Sc(string Name, string Path);

    private static (string Zone, List<Sc> Scenarios) Truth()
    {
        var root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Calib("truth.json"))).RootElement;
        return (root.GetProperty("tz").GetString()!, root.GetProperty("scenarios").EnumerateArray().Select(s => new Sc(s.GetProperty("name").GetString()!, s.GetProperty("path").GetString()!)).ToList());
    }

    private static List<TimelineEvent> Mft()
    {
        var sink = new ListSink();
        var p = Calib("mft.bin");
        new MftParser().Parse(new EvidenceItem { EvidenceId = "EV-MFT", CaseId = "C", Source = "t", SourceType = MftParser.SourceTypeName, StoredPath = p, AcquiredAtUtc = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero) }, p, sink, default);
        return sink.Events.ToList();
    }

    private static List<TimelineEvent> Usn(string zoneId)
    {
        var sink = new ListSink();
        var p = Calib("usn.csv");
        new UsnJournalParser(TimeZoneInfo.FindSystemTimeZoneById(zoneId)).Parse(new EvidenceItem { EvidenceId = "EV-USN", CaseId = "C", Source = "t", SourceType = "usn_journal", StoredPath = p }, p, sink, default);
        return sink.Events.ToList();
    }

    private static List<Finding> Rule(IEnumerable<TimelineEvent> events)
    {
        int n = 0;
        return Wp14Rules.Run(events.ToList(), () => $"F-{++n:D4}", new Wp14Input()).Where(f => f.RuleId == RuleId).ToList();
    }

    private static Severity? Level(List<Finding> findings, string path) => findings.Where(f => f.File == path).Select(f => (Severity?)f.Severity).DefaultIfEmpty(null).Max();

    private static string Table(List<Finding> findings, List<Sc> scenarios) => string.Join(", ", scenarios.Select(s => s.Name + "=" + (Level(findings, s.Path)?.ToString() ?? "-")));

    [Fact]
    public void With_usn_A_to_E_are_high_and_every_other_scenario_incl_L_to_N2_has_nothing()
    {
        var (zone, sc) = Truth();
        Assert.Equal(Names.Length, sc.Count);
        var findings = Rule(Mft().Concat(Usn(zone)));
        foreach (var s in sc)
        {
            if (s.Name is "A" or "B" or "C" or "D" or "E") Assert.True(Level(findings, s.Path) == Severity.High, $"{s.Name} must be High: {Table(findings, sc)}");
            else Assert.True(Level(findings, s.Path) is null, $"{s.Name} must have no finding: {Table(findings, sc)}");
        }
    }

    [Fact]
    public void With_usn_L2_is_not_high_and_not_medium_the_tunnelling_signature_explains_it()
    {
        var (zone, sc) = Truth();
        var findings = Rule(Mft().Concat(Usn(zone)));
        Assert.Null(Level(findings, sc.Single(s => s.Name == "L2").Path));
    }

    [Fact]
    public void With_usn_M2_file_replace_is_silent_because_the_basic_info_change_and_the_rename_are_one_operation()
    {
        var (zone, sc) = Truth();
        var findings = Rule(Mft().Concat(Usn(zone)));
        Assert.Null(Level(findings, sc.Single(s => s.Name == "M2").Path));
        Assert.Null(Level(findings, sc.Single(s => s.Name == "M").Path));
    }

    [Fact]
    public void Mft_only_L2_is_low_with_the_tunnelling_wording_and_the_other_scenarios_are_as_before()
    {
        var (_, sc) = Truth();
        var findings = Rule(Mft());
        foreach (var s in sc)
        {
            var got = Level(findings, s.Path);
            if (s.Name is "A" or "B" or "E") Assert.True(got == Severity.Medium, $"{s.Name} must be Medium: {Table(findings, sc)}");
            else if (s.Name == "L2") Assert.True(got == Severity.Low, $"L2 must be Low: {Table(findings, sc)}");
            else Assert.True(got is null, $"{s.Name} must have no finding: {Table(findings, sc)}");
        }
        var low = Assert.Single(findings, f => f.File == sc.Single(s => s.Name == "L2").Path);
        Assert.Contains("tunelare", low.Description);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("W. Europe Standard Time")]
    [InlineData("Arabic Standard Time")]
    [InlineData("GTB Standard Time")]
    public void Zone_does_not_change_the_result_with_usn(string zone)
    {
        var (_, sc) = Truth();
        var findings = Rule(Mft().Concat(Usn(zone)));
        foreach (var s in sc)
        {
            if (s.Name is "A" or "B" or "C" or "D" or "E") Assert.True(Level(findings, s.Path) == Severity.High, $"{zone} {s.Name}: {Table(findings, sc)}");
            else Assert.True(Level(findings, s.Path) is null, $"{zone} {s.Name}: {Table(findings, sc)}");
        }
    }

    [Fact]
    public void Mft_has_dos_name_only_on_the_8_3_scenarios()
    {
        var (_, sc) = Truth();
        var mft = Mft().Where(e => e.Fields.GetValueOrDefault("Category") != "summary").ToList();
        string Dos(string name) => mft.Where(e => e.Path == sc.Single(s => s.Name == name).Path).Select(e => e.Fields.GetValueOrDefault("HasDosName", "")).FirstOrDefault() ?? "(no record)";
        Assert.Equal("true", Dos("L2"));
        Assert.Equal("false", Dos("A"));
        Assert.Equal("false", Dos("L"));
    }

    // ---------------- synthetic ----------------

    private const string Si2019 = "2019-03-14T09:26:53.1234567Z", Fn2026 = "2026-11-13T18:28:51.9316850Z", Chg2026 = "2026-11-13T18:28:53.9523090Z";
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 18, 28, 51, TimeSpan.Zero);
    private const string Dir = "0x00000000000000000005000000000005";

    private static List<TimelineEvent> L(params object[] parts) => parts.SelectMany(p => p is TimelineEvent e ? [e] : (IEnumerable<TimelineEvent>)p).ToList();

    private static string Id(int num) => "0x" + "0".PadLeft(16, '0') + "0001" + num.ToString("x12");

    private static TimelineEvent MftEv(string id, string name, bool dos, string si = Si2019, string fn = Fn2026)
    {
        var e = new TimelineEvent
        {
            Time = Timestamp.Unknown(), Source = "MFT", EvidenceId = "EV-MFT", Path = @"\x\" + name, Process = "", Summary = "MFT " + name, TimeSemantics = "t",
            TemporalType = TemporalType.Historical, Classification = Classification.Candidate, Confidence = Confidence.Low, Locator = "MFT#" + id,
        };
        e.Fields["Category"] = "mismatch"; e.Fields["FileId"] = id; e.Fields["FileName"] = name;
        e.Fields["SiCreated"] = si; e.Fields["SiModified"] = si; e.Fields["SiMftModified"] = Chg2026; e.Fields["SiAccessed"] = si;
        e.Fields["FnCreated"] = fn; e.Fields["FnModified"] = fn; e.Fields["FnMftModified"] = fn; e.Fields["FnAccessed"] = fn;
        if (dos) e.Fields["HasDosName"] = "true";
        return e;
    }

    private static TimelineEvent Op(string id, string name, string reason, DateTimeOffset? at, long usn, string parent = Dir)
    {
        var e = new TimelineEvent
        {
            Time = at is { } a ? Timestamp.FromUtc(a.UtcDateTime, "r", "t") : Timestamp.Unknown("bad"), Source = "USN", EvidenceId = "EV-USN", Path = @"\x\" + name, Process = "",
            Summary = "USN " + reason, TimeSemantics = "t", TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "USN=" + usn,
        };
        e.Fields["Usn"] = usn.ToString(CultureInfo.InvariantCulture); e.Fields["FileName"] = name; e.Fields["Reason"] = reason; e.Fields["FileId"] = id; e.Fields["ParentFileId"] = parent;
        return e;
    }

    /// <summary>The A pattern: creation, then a separate "Basic info change" two seconds later.</summary>
    private static IEnumerable<TimelineEvent> Stomp(string id, string name, long usn = 1000) =>
    [
        Op(id, name, "Data extend | File create | Close", T0, usn),
        Op(id, name, "Basic info change | Close", T0.AddSeconds(2), usn + 100),
    ];

    [Fact]
    public void Stomp_on_a_file_with_a_dos_name_and_no_tunnelling_signature_stays_high()
    {
        var f = Assert.Single(Rule(L(MftEv(Id(100), "a.txt", dos: true), Stomp(Id(100), "a.txt"))));
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void Stomp_whose_name_was_released_by_another_file_seconds_before_is_not_reported()
    {
        var events = L(
            MftEv(Id(100), "report.docx", dos: true),
            Op(Id(7), "report.docx", "Rename: old name | Close", T0.AddSeconds(-3), 900),
            Op(Id(7), "report.docx", "File delete | Close", T0.AddSeconds(-3), 910),
            Op(Id(100), "~tmp.tmp", "Data extend | File create | Close", T0.AddSeconds(-2), 950),
            Op(Id(100), "~tmp.tmp", "Basic info change | Close", T0.AddSeconds(-1), 960),
            Op(Id(100), "report.docx", "Rename: new name | Close", T0, 1000)
        );
        Assert.Empty(Rule(events));
    }

    [Fact]
    public void Release_is_matched_case_insensitively_and_by_final_name_of_a_create()
    {
        var events = L(
            MftEv(Id(100), "Report.DOCX", dos: true),
            Op(Id(7), "REPORT.docx", "File delete | Close", T0.AddSeconds(-5), 900),
            Stomp(Id(100), "Report.DOCX")
        );
        Assert.Empty(Rule(events));
    }

    [Fact]
    public void Release_more_than_15_seconds_before_does_not_explain_it()
    {
        var events = L(MftEv(Id(100), "x.txt", dos: true), Op(Id(7), "x.txt", "File delete | Close", T0.AddSeconds(-16), 900), Stomp(Id(100), "x.txt"));
        Assert.Equal(Severity.High, Assert.Single(Rule(events)).Severity);
    }

    [Fact]
    public void Release_exactly_15_seconds_before_explains_it()
    {
        var events = L(MftEv(Id(100), "x.txt", dos: true), Op(Id(7), "x.txt", "File delete | Close", T0.AddSeconds(-15), 900), Stomp(Id(100), "x.txt"));
        Assert.Empty(Rule(events));
    }

    [Fact]
    public void Release_in_another_directory_or_after_the_creation_or_by_the_same_file_does_not_explain_it()
    {
        var other = L(MftEv(Id(100), "x.txt", dos: true), Op(Id(7), "x.txt", "File delete | Close", T0.AddSeconds(-3), 900, parent: "0x00000000000000000005000000000099"), Stomp(Id(100), "x.txt"));
        Assert.Equal(Severity.High, Assert.Single(Rule(other)).Severity);
        var after = L(MftEv(Id(100), "x.txt", dos: true), Op(Id(7), "x.txt", "File delete | Close", T0.AddSeconds(-3), 5000), Stomp(Id(100), "x.txt"));
        Assert.Equal(Severity.High, Assert.Single(Rule(after)).Severity);
        var same = L(MftEv(Id(100), "x.txt", dos: true), Op(Id(100), "x.txt", "Rename: old name | Close", T0.AddSeconds(-3), 900), Stomp(Id(100), "x.txt"));
        Assert.Equal(Severity.High, Assert.Single(Rule(same)).Severity);
    }

    [Fact]
    public void Release_with_unknown_times_is_judged_by_the_usn_order_only()
    {
        var events = L(
            MftEv(Id(100), "x.txt", dos: true),
            Op(Id(7), "x.txt", "File delete | Close", null, 900),
            Op(Id(100), "x.txt", "Data extend | File create | Close", null, 1000),
            Op(Id(100), "x.txt", "Basic info change | Close", null, 1100)
        );
        Assert.Empty(Rule(events));
    }

    [Fact]
    public void A_basic_info_change_that_holds_the_rename_is_not_a_separate_operation_on_the_mismatch_path()
    {
        var events = L(
            MftEv(Id(100), "x.txt", dos: false),
            Op(Id(100), "x.txt", "Data extend | File create | Close", T0, 1000),
            Op(Id(100), "x.txt", "Security change | Basic info change | Rename: new name | Close", T0.AddSeconds(2), 1100)
        );
        Assert.Equal(Severity.Medium, Assert.Single(Rule(events)).Severity);
    }

    [Fact]
    public void A_basic_info_change_that_holds_the_rename_is_not_a_separate_operation_on_the_rename_path()
    {
        const string S = "2024-01-01T00:00:00.0000000Z";
        var events = L(
            MftEv(Id(100), "x.txt", dos: false, si: S, fn: S),
            Op(Id(100), "x.txt", "Data extend | File create | Close", T0, 1000),
            Op(Id(100), "x.txt", "Security change | Basic info change | Rename: new name | Close", T0.AddSeconds(2), 1100)
        );
        Assert.Empty(Rule(events));
    }

    [Fact]
    public void Mft_only_mismatch_is_low_with_a_dos_name_and_medium_without()
    {
        // Tunnelling inherits the creation time only; the saved file's $SI modification time is new (calibration L2).
        var creationOnly = MftEv(Id(1), "dos.txt", dos: true);
        creationOnly.Fields["SiModified"] = Fn2026; creationOnly.Fields["SiAccessed"] = Fn2026;
        var f = Rule([creationOnly, MftEv(Id(2), "plain.txt", dos: false)]);
        Assert.Equal(Severity.Low, Assert.Single(f, x => x.File == @"\x\dos.txt").Severity);
        Assert.Equal(Severity.Medium, Assert.Single(f, x => x.File == @"\x\plain.txt").Severity);
    }

    [Fact]
    public void Mft_only_full_stomp_on_a_dos_named_file_stays_medium_because_tunnelling_never_backdates_the_modification_time()
    {
        var f = Rule([MftEv(Id(1), "dos_full.txt", dos: true)]);   // all three $SI times in 2019
        Assert.Equal(Severity.Medium, Assert.Single(f, x => x.File == @"\x\dos_full.txt").Severity);
    }

    [Fact]
    public void Tunnelling_is_counted_not_reported_and_af08_says_so()
    {
        var (zone, sc) = Truth();
        var events = Mft().Concat(Usn(zone)).ToList();
        var outcome = NtfsTimestomp.Analyze(events);
        Assert.Equal(1, outcome.TunnelExplained);   // L2 only; M2 and N2 never reach a High test
        Assert.DoesNotContain(outcome.Hits, h => h.Path == sc.Single(s => s.Name == "L2").Path);
        var af = AntiForensics.Evaluate(events, []).Single(c => c.Id == "AF08");
        Assert.Equal(AntiForensicResult.Detected, af.Result);
        Assert.Contains("explicate prin tunelare", af.Reason);
    }

    [Fact]
    public void Af08_not_detected_names_tunnelling_as_a_limit_and_counts_the_explained_files()
    {
        var events = L(
            MftEv(Id(100), "report.docx", dos: true),
            Op(Id(7), "report.docx", "File delete | Close", T0.AddSeconds(-3), 900),
            Stomp(Id(100), "report.docx"));
        events.Add(MftSummary());
        var af = AntiForensics.Evaluate(events, []).Single(c => c.Id == "AF08");
        Assert.NotEqual(AntiForensicResult.Detected, af.Result);
        Assert.Contains("tunelare", af.Reason);
        Assert.Contains("1 fișiere explicate prin tunelare", af.Reason);
    }

    private static TimelineEvent MftSummary()
    {
        var e = new TimelineEvent
        {
            Time = Timestamp.FromUtc(new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), "r", "t"), Source = "MFT", EvidenceId = "EV-MFT", Path = "", Process = "", Summary = "sum", TimeSemantics = "t",
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "MFT#summary",
        };
        e.Fields["Category"] = "summary"; e.Fields["TotalRecords"] = "10"; e.Fields["MismatchTotal"] = "0"; e.Fields["MismatchEmitted"] = "0"; e.Fields["RecentTotal"] = "0"; e.Fields["RecentEmitted"] = "0"; e.Fields["RecentCategory"] = "emisă";
        return e;
    }
}
