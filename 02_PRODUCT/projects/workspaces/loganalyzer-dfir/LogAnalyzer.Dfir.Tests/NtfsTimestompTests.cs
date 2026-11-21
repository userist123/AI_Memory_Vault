using System.Globalization;
using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// NTFS-TIMESTOMP (T1070.006) and AF08, calibrated on a real Windows 11 volume (Corpus/NtfsCalib: scenarios A-K) and on the older
/// Corpus/Ntfs (MFT only). A-E are timestomps, F-K are legitimate operations; see the README next to the corpus for what tells them apart.
/// </summary>
public sealed partial class NtfsTimestompTests
{
    private const string RuleId = "NTFS-TIMESTOMP";
    private static readonly DateTimeOffset Acquired = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    private static string Calib(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "NtfsCalib", n);
    private static string Old(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "Ntfs", n);
    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";

    private static List<TimelineEvent> Mft(string path)
    {
        var sink = new ListSink();
        new MftParser().Parse(new EvidenceItem { EvidenceId = "EV-MFT", CaseId = "C", Source = "t", SourceType = MftParser.SourceTypeName, StoredPath = path, AcquiredAtUtc = Acquired }, path, sink, default);
        return sink.Events.ToList();
    }

    private static List<TimelineEvent> Usn(string path, string zoneId)
    {
        var sink = new ListSink();
        new UsnJournalParser(TimeZoneInfo.FindSystemTimeZoneById(zoneId)).Parse(new EvidenceItem { EvidenceId = "EV-USN", CaseId = "C", Source = "t", SourceType = "usn_journal", StoredPath = path }, path, sink, default);
        return sink.Events.ToList();
    }

    private static List<Finding> Rule(IEnumerable<TimelineEvent> events)
    {
        int n = 0;
        return Wp14Rules.Run(events.ToList(), () => $"F-{++n:D4}", new Wp14Input()).Where(f => f.RuleId == RuleId).ToList();
    }

    private static AntiForensicCheck Af08(IEnumerable<TimelineEvent> events) => AntiForensics.Evaluate(events.ToList(), []).Single(c => c.Id == "AF08");

    private sealed record Scenario(string Name, string Path, string FileId);

    private static (string Zone, List<Scenario> Scenarios) Truth()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Calib("truth.json"))).RootElement;
        return (root.GetProperty("tz").GetString()!, root.GetProperty("scenarios").EnumerateArray()
            .Select(s => new Scenario(s.GetProperty("name").GetString()!, s.GetProperty("path").GetString()!, s.GetProperty("fileId").GetString()!)).ToList());
    }

    private static Severity? LevelOf(List<Finding> findings, Scenario s) => findings.Where(f => f.File == s.Path).Select(f => (Severity?)f.Severity).DefaultIfEmpty(null).Max();

    // ---------------- calibration corpus ----------------

    [Fact]
    public void Calibration_with_usn_A_to_E_are_high_and_F_to_K_have_no_finding()
    {
        var (zone, scenarios) = Truth();
        var findings = Rule(Mft(Calib("mft.bin")).Concat(Usn(Calib("usn.csv"), zone)));
        foreach (var s in scenarios)
        {
            if (s.Name is "A" or "B" or "C" or "D" or "E") Assert.True(LevelOf(findings, s) == Severity.High, $"{s.Name}: așteptat High, obținut {LevelOf(findings, s)?.ToString() ?? "nimic"}");
            else Assert.True(LevelOf(findings, s) is null, $"{s.Name}: așteptat nimic, obținut {LevelOf(findings, s)}");
        }
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Medium);
        Assert.All(findings.Where(f => f.Severity == Severity.High), f =>
        {
            Assert.Equal(Confidence.Medium, f.Confidence);
            Assert.Equal(Classification.Candidate, f.Classification);
            Assert.Equal("T1070.006", f.MitreTechniqueId);
            Assert.NotEmpty(f.SupportingEvidence);
        });
    }

    [Fact]
    public void Calibration_mft_only_A_B_E_are_medium_and_everything_else_is_silent()
    {
        var (_, scenarios) = Truth();
        var findings = Rule(Mft(Calib("mft.bin")));
        foreach (var s in scenarios)
        {
            if (s.Name is "A" or "B" or "E") Assert.True(LevelOf(findings, s) == Severity.Medium, $"{s.Name}: așteptat Medium, obținut {LevelOf(findings, s)?.ToString() ?? "nimic"}");
            else if (s.Name == "L2") Assert.True(LevelOf(findings, s) == Severity.Low, $"{s.Name}: așteptat Low (nume 8.3, posibilă tunelare), obținut {LevelOf(findings, s)?.ToString() ?? "nimic"}");   // was "nothing" before the 8.3 rule
            else Assert.True(LevelOf(findings, s) is null, $"{s.Name}: așteptat nimic, obținut {LevelOf(findings, s)}");
        }
        var medium = findings.First(f => f.Severity == Severity.Medium);
        Assert.Contains("arhivatoarele", medium.Description);   // an archiver that restores the creation time gives the same pattern
        Assert.DoesNotContain("a modificat", medium.Description);
    }

    [Fact]
    public void Calibration_af08_is_detected_with_references()
    {
        var (zone, _) = Truth();
        var af = Af08(Mft(Calib("mft.bin")).Concat(Usn(Calib("usn.csv"), zone)));
        Assert.Equal(AntiForensicResult.Detected, af.Result);
        Assert.Equal("T1070.006", af.Attack);
        Assert.NotEmpty(af.Evidence);
        Assert.Equal(AntiForensicResult.Detected, Af08(Mft(Calib("mft.bin"))).Result);
    }

    [Fact]
    public void Old_corpus_mft_only_stomped_file_is_medium_although_it_has_a_dos_name_because_its_modification_time_is_old_too()
    {
        var events = Mft(Old("mft.bin"));
        var findings = Rule(events);
        var f = Assert.Single(findings, x => x.File == @"\docs\stomped.txt");
        // Corpus/Ntfs was made with 8.3 names on, so the record has a DOS name; tunnelling inherits only the creation time, and here the
        // $SI modification time was moved back to 2019 as well (all three times stomped) -> not explained by tunnelling -> Medium.
        Assert.Contains(events, e => e.Path == @"\docs\stomped.txt" && e.Fields.TryGetValue("HasDosName", out var d) && d == "true");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(AntiForensicResult.Detected, Af08(events).Result);
    }

    [Fact]
    public void A_dos_named_file_whose_creation_time_alone_is_old_is_only_low_possible_tunnelling()
    {
        // Calibration L2 (8.3 names on): safe save, the creation time was inherited by tunnelling, the modification time is new.
        var (_, scenarios) = Truth();
        var events = Mft(Calib("mft.bin"));
        var l2 = scenarios.Single(s => s.Name == "L2");
        var f = Assert.Single(Rule(events), x => x.File == l2.Path);
        Assert.Equal(Severity.Low, f.Severity);
    }

    // ---------------- MftParser criteria ----------------

    [Fact]
    public void MftParser_mismatch_needs_more_than_60_seconds_and_copied_change_time_is_a_signal()
    {
        var (_, scenarios) = Truth();
        var mft = Mft(Calib("mft.bin"));
        TimelineEvent? Ev(string name) => mft.FirstOrDefault(e => F(e, "FileId") == scenarios.Single(s => s.Name == name).FileId && F(e, "Category") != "summary");
        foreach (var n in new[] { "A", "B", "E", "H" }) Assert.Equal("mismatch", F(Ev(n)!, "Category"));
        foreach (var n in new[] { "F", "G", "I", "J", "K" }) Assert.NotEqual("mismatch", F(Ev(n)!, "Category"));   // K: 0.49 s; J: only $SI modified old
        Assert.Contains("COPIED_CHANGE_TIME", F(Ev("H")!, "Signals"));
        Assert.DoesNotContain("COPIED_CHANGE_TIME", F(Ev("A")!, "Signals"));
        Assert.Contains("SI_CREATED_ZERO_FRACTION", F(Ev("A")!, "Signals"));
        Assert.DoesNotContain("SI_CREATED_ZERO_FRACTION", F(Ev("B")!, "Signals"));
    }

    [Fact]
    public void MftParser_si_modified_alone_emits_no_event_but_is_counted_in_the_summary()
    {
        var mft = Mft(Calib("mft.bin"));
        Assert.DoesNotContain(mft, e => F(e, "Signals").Contains("SI_MODIFIED_BEFORE_FN_CREATED") && !F(e, "Signals").Contains("SI_CREATED_BEFORE_FN_CREATED"));
        var summary = mft.Single(e => F(e, "Category") == "summary");
        Assert.True(long.Parse(F(summary, "SiModifiedBeforeFnCreatedTotal"), CultureInfo.InvariantCulture) >= 2);   // F (copy) and J (zip) at least
    }

    [Fact]
    public void MftParser_recent_includes_files_whose_fn_created_is_in_the_window()
    {
        var (_, scenarios) = Truth();
        var mft = Mft(Calib("mft.bin"));
        foreach (var n in new[] { "C", "D" })   // $SI and $FN creation are 2019 after rename; the record still has to reach the rule
            Assert.Contains(mft, e => F(e, "FileId") == scenarios.Single(s => s.Name == n).FileId && F(e, "Category") is "recent" or "mismatch");
    }

    // ---------------- AF08 states ----------------

    [Fact]
    public void Af08_is_not_detected_on_a_corpus_without_mismatch_and_says_what_was_checked()
    {
        var (zone, scenarios) = Truth();
        var legit = scenarios.Where(s => s.Name is "F" or "G" or "I" or "J" or "K").Select(s => s.FileId).ToHashSet();
        var events = Mft(Calib("mft.bin")).Where(e => F(e, "Category") == "summary" || legit.Contains(F(e, "FileId")))
            .Concat(Usn(Calib("usn.csv"), zone).Where(e => legit.Contains("0x" + F(e, "FileId")))).ToList();
        var af = Af08(events);
        Assert.Equal(AntiForensicResult.NotDetected, af.Result);
        Assert.Contains("înregistrări", af.Reason);
        Assert.Contains("USN", af.Reason);
        Assert.Contains("$FILE_NAME", af.Reason);   // the limit: tools that change $FN too are not visible
        Assert.Empty(Rule(events));
    }

    [Fact]
    public void Af08_is_undetermined_without_mft()
    {
        var (zone, _) = Truth();
        var af = Af08(Usn(Calib("usn.csv"), zone));
        Assert.Equal(AntiForensicResult.Undetermined, af.Result);
        Assert.Contains("$MFT", af.Reason);
        Assert.Equal(AntiForensicResult.Undetermined, Af08([]).Result);
    }

    // ---------------- synthetic probes ----------------

    private static string Id(int seq, int num) => "0x" + "0".PadLeft(16, '0') + seq.ToString("x4") + num.ToString("x12");
    private static string Ts(string iso) => iso;

    private static TimelineEvent MftEv(string fileId, string name, string siCreated, string fnCreated, string siMftModified, string category = "mismatch", string evidence = "EV-MFT")
    {
        var e = new TimelineEvent
        {
            Time = Timestamp.Unknown(), Source = "MFT", EvidenceId = evidence, Path = @"\x\" + name, Process = "", Summary = "MFT " + name, TimeSemantics = "t",
            TemporalType = TemporalType.Historical, Classification = Classification.Candidate, Confidence = Confidence.Low, Locator = "MFT#" + fileId,
        };
        e.Fields["Category"] = category; e.Fields["FileId"] = fileId; e.Fields["FileName"] = name;
        e.Fields["SiCreated"] = siCreated; e.Fields["SiModified"] = siCreated; e.Fields["SiMftModified"] = siMftModified; e.Fields["SiAccessed"] = siCreated;
        e.Fields["FnCreated"] = fnCreated; e.Fields["FnModified"] = fnCreated; e.Fields["FnMftModified"] = fnCreated; e.Fields["FnAccessed"] = fnCreated;
        return e;
    }

    private static TimelineEvent UsnEv(string fileId, string reason, DateTimeOffset? at, long usn, string name = "x.txt")
    {
        var e = new TimelineEvent
        {
            Time = at is { } a ? Timestamp.FromUtc(a.UtcDateTime, "r", "t") : Timestamp.Unknown("bad"), Source = "USN", EvidenceId = "EV-USN", Path = @"\x\" + name, Process = "",
            Summary = "USN " + reason, TimeSemantics = "t", TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "USN=" + usn,
        };
        e.Fields["Usn"] = usn.ToString(CultureInfo.InvariantCulture); e.Fields["FileName"] = name; e.Fields["Reason"] = reason; e.Fields["FileId"] = fileId;
        return e;
    }

    private const string Si2019 = "2019-03-14T09:26:53.1234567Z", Fn2026 = "2026-11-13T18:28:51.9316850Z", Chg2026 = "2026-11-13T18:28:53.9523090Z";
    private static readonly DateTimeOffset Create = new(2026, 11, 13, 18, 28, 51, TimeSpan.Zero);

    private static IEnumerable<TimelineEvent> UsnStomp(string id, string name = "x.txt", long usn = 100) =>
    [
        UsnEv(id, "Data extend | File create | Close", Create, usn, name),
        UsnEv(id, "Basic info change | Close", Create.AddSeconds(2), usn + 100, name),
    ];

    [Fact]
    public void Probe_file_id_is_normalised_upper_case_and_without_leading_zeros()
    {
        var mftId = Id(1, 0x30).ToUpperInvariant().Replace("0X", "0x");
        var usnId = Id(1, 0x30)[2..].TrimStart('0').ToUpperInvariant();   // no 0x, no leading zeros
        var f = Assert.Single(Rule([MftEv(mftId, "x.txt", Si2019, Fn2026, Chg2026), .. UsnStomp(usnId)]));
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void Probe_different_sequence_is_a_different_file()
    {
        // the USN rows belong to sequence 1; the MFT record is sequence 2 of the same record number (reused): no USN for it -> Medium, never High
        var f = Assert.Single(Rule([MftEv(Id(2, 0x30), "x.txt", Si2019, Fn2026, Chg2026), .. UsnStomp(Id(1, 0x30)[2..])]));
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void Probe_duplicate_mft_events_for_one_file_id_give_one_finding()
    {
        var a = MftEv(Id(1, 0x30), "x.txt", Si2019, Fn2026, Chg2026);
        var b = MftEv(Id(1, 0x30), "hardlink.txt", Si2019, Fn2026, Chg2026, evidence: "EV-MFT2");
        Assert.Single(Rule([a, b, .. UsnStomp(Id(1, 0x30)[2..])]));
    }

    [Fact]
    public void Probe_usn_with_impossible_times_never_makes_a_rename_path_high_and_never_orders_by_time()
    {
        var id = Id(1, 0x30);
        // the mismatch path compares $SI with $FN and orders the operations by Usn number: the USN times do not matter there (a wrong zone must not change the verdict)
        var far = new[] { UsnEv(id[2..], "Data extend | File create | Close", new DateTimeOffset(2999, 1, 1, 0, 0, 0, TimeSpan.Zero), 100), UsnEv(id[2..], "Basic info change | Close", new DateTimeOffset(2999, 1, 1, 0, 0, 2, TimeSpan.Zero), 200) };
        Assert.Equal(Severity.High, Assert.Single(Rule([MftEv(id, "x.txt", Si2019, Fn2026, Chg2026), .. far])).Severity);
        // the rename path needs a plausible creation time for its 24 h margin: impossible or unknown times give nothing
        const string si = "2019-03-14T09:26:53.0000000Z";
        foreach (var at in new DateTimeOffset?[] { new DateTimeOffset(2999, 1, 1, 0, 0, 0, TimeSpan.Zero), null })
        {
            var ren = new[] { MftEv(id, "x.txt", si, si, Chg2026, category: "recent"), UsnEv(id[2..], "Data extend | File create | Close", at, 100), UsnEv(id[2..], "Basic info change | Close", at, 200), UsnEv(id[2..], "Rename: new name | Close", at, 300) };
            Assert.Empty(Rule(ren));
        }
        // the basic-info operation numbered BEFORE the creation does not count, whatever its time says
        var before = new[] { UsnEv(id[2..], "Basic info change | Close", Create.AddSeconds(5), 50), UsnEv(id[2..], "Data extend | File create | Close", Create, 100) };
        Assert.DoesNotContain(Rule([MftEv(id, "x.txt", Si2019, Fn2026, Chg2026), .. before]), f => f.Severity == Severity.High);
    }

    [Fact]
    public void Probe_no_finding_for_copied_change_time_small_difference_or_old_si_modified_only()
    {
        var id = Id(1, 0x30);
        Assert.Empty(Rule([MftEv(id, "copied.txt", Si2019, Fn2026, "2019-03-14T09:26:53.0000000Z"), .. UsnStomp(id[2..], "copied.txt")]));
        Assert.Empty(Rule([MftEv(id, "copied.txt", Si2019, Fn2026, "2019-03-14T09:26:53.0000000Z")]));
        Assert.Empty(Rule([MftEv(id, "close.txt", "2026-11-13T18:28:00.0000000Z", Fn2026, Chg2026)]));      // 51.9 s
        Assert.Empty(Rule([MftEv(id, "k.txt", "2026-11-13T18:28:51.4000000Z", Fn2026, Chg2026)]));          // 0.5 s
        var onlyModified = MftEv(id, "m.txt", Fn2026, Fn2026, Chg2026); onlyModified.Fields["SiModified"] = "2018-06-01T07:00:00.0000000Z";
        Assert.Empty(Rule([onlyModified]));
    }

    [Fact]
    public void Probe_usn_without_a_separate_operation_stays_medium_not_high_and_not_lost()
    {
        var id = Id(1, 0x30);
        var oneOp = new[] { UsnEv(id[2..], "Data extend | File create | Basic info change | Close", Create, 100) };
        Assert.Equal(Severity.Medium, Assert.Single(Rule([MftEv(id, "x.txt", Si2019, Fn2026, Chg2026), .. oneOp])).Severity);
        var dataAfter = new[] { UsnEv(id[2..], "Data extend | File create | Close", Create, 100), UsnEv(id[2..], "Data overwrite | Basic info change | Close", Create.AddSeconds(1), 200) };
        Assert.Equal(Severity.Medium, Assert.Single(Rule([MftEv(id, "x.txt", Si2019, Fn2026, Chg2026), .. dataAfter])).Severity);
    }

    [Fact]
    public void Probe_hostile_names_are_escaped_in_every_text()
    {
        var id = Id(1, 0x30);
        var name = "a‮b\nc\u0000d.txt";
        var f = Assert.Single(Rule([MftEv(id, name, Si2019, Fn2026, Chg2026)]));
        var af = Af08([MftEv(id, name, Si2019, Fn2026, Chg2026)]);
        foreach (var text in new[] { f.Title, f.Description, f.PlainWhat, string.Concat(f.SupportingEvidence.Select(r => r.Description)), af.Reason })
        {
            Assert.DoesNotContain('‮', text);
            Assert.DoesNotContain('\n', text);
            Assert.DoesNotContain('\0', text);
        }
        Assert.Contains("\\u202E", f.Description);
    }

    [Fact]
    public void Probe_findings_are_capped_with_a_low_summary_for_the_rest()
    {
        var events = new List<TimelineEvent>();
        for (int i = 0; i < 60; i++) events.Add(MftEv(Id(1, 100 + i), $"f{i}.txt", Si2019, Fn2026, Chg2026));
        var findings = Rule(events);
        Assert.Equal(51, findings.Count);
        Assert.Equal(50, findings.Count(f => f.Severity == Severity.Medium));
        var low = Assert.Single(findings, f => f.Severity == Severity.Low);
        Assert.Contains("10", low.Description);
        Assert.Equal(AntiForensicResult.Detected, Af08(events).Result);
    }
}
