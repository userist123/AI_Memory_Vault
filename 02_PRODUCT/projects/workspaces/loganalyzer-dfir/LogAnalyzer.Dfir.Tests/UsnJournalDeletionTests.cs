using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// AF07 (the change journal absent / recreated) and the USN-JOURNAL-DELETED rule (fsutil usn deletejournal / createjournal in 4688, Sysmon 1, PowerShell 4104).
/// A command that only prints the text, a file name containing "deletejournal" and the read-only fsutil verbs never produce a finding.
/// </summary>
public sealed class UsnJournalDeletionTests
{
    private const string RuleId = "USN-JOURNAL-DELETED";
    private const string Sysmon = "EventLog:Microsoft-Windows-Sysmon/Operational";
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
    private static int _n;

    private static TimelineEvent Row(string source, string eventId, DateTime utc, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(utc, "", "test"), Source = source, EventId = eventId, EvidenceId = "EV-T", Summary = source + " " + eventId,
        Locator = $"row={Interlocked.Increment(ref _n)}", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Proc4688(string cmd, double minute = 0) => Row("EventLog:Security", "4688", T0.AddMinutes(minute), ("CommandLine", cmd), ("SubjectUserName", "admin"));
    private static TimelineEvent Sys1(string cmd, double minute = 0) => Row(Sysmon, "1", T0.AddMinutes(minute), ("CommandLine", cmd), ("User", "LAB\\admin"));
    private static TimelineEvent Ps4104(string script, double minute = 0) => Row("EventLog:Microsoft-Windows-PowerShell/Operational", "4104", T0.AddMinutes(minute), ("ScriptBlockText", script));

    private static List<Finding> Rule(params TimelineEvent[] events)
    {
        int n = 0;
        return Wp14Rules.Run(events, () => $"F-{++n:D4}", new Wp14Input()).Where(f => f.RuleId == RuleId).ToList();
    }

    // ---------------- the rule ----------------

    [Theory]
    [InlineData("fsutil usn deletejournal /d C:")]
    [InlineData("FSUTIL.EXE USN DeleteJournal /N c:")]
    [InlineData(@"C:\Windows\System32\fsutil.exe usn deletejournal /d C:")]
    [InlineData("\"C:\\Windows\\System32\\fsutil.exe\" usn deletejournal /d C:")]
    [InlineData("cmd.exe /c fsutil usn deletejournal /d C:")]
    public void Deletejournal_in_4688_or_Sysmon_1_is_High_with_the_technique(string cmd)
    {
        foreach (var e in new[] { Proc4688(cmd), Sys1(cmd) })
        {
            var f = Assert.Single(Rule(e));
            Assert.Equal(Severity.High, f.Severity);
            Assert.Equal("T1070.004", f.MitreTechniqueId);
            Assert.Equal("Defense Evasion", f.Category);
            Assert.False(string.IsNullOrWhiteSpace(f.PlainWhat));
            Assert.Contains("a fost lansată", f.Description);
        }
    }

    [Fact]
    public void Deletejournal_in_a_PowerShell_script_block_is_High()
    {
        var f = Assert.Single(Rule(Ps4104("Write-Host 'start'\n& fsutil usn deletejournal /d C:\nWrite-Host 'gata'")));
        Assert.Equal(Severity.High, f.Severity);
        var g = Assert.Single(Rule(Ps4104("if ($true) { fsutil.exe usn deletejournal /n D: }")));
        Assert.Equal(Severity.High, g.Severity);
    }

    [Fact]
    public void Createjournal_alone_is_Low_and_informative()
    {
        var f = Assert.Single(Rule(Proc4688("fsutil usn createjournal m=1000 a=100 C:")));
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("legitim", f.Description);
    }

    [Fact]
    public void Createjournal_within_one_hour_after_a_delete_on_the_same_volume_is_High()
    {
        var f = Assert.Single(Rule(Proc4688("fsutil usn deletejournal /d C:", 0), Proc4688("fsutil usn createjournal m=1000 a=100 C:", 30)));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("recreat", f.Description);
    }

    [Fact]
    public void Createjournal_after_more_than_one_hour_or_on_another_volume_stays_a_separate_Low_finding()
    {
        foreach (var create in new[] { Proc4688("fsutil usn createjournal m=1000 a=100 C:", 90), Proc4688("fsutil usn createjournal m=1000 a=100 D:", 10) })
        {
            var found = Rule(Proc4688("fsutil usn deletejournal /d C:", 0), create);
            Assert.Equal(2, found.Count);
            Assert.Single(found, f => f.Severity == Severity.High);
            Assert.Single(found, f => f.Severity == Severity.Low);
        }
        // a create BEFORE the delete is not "after"
        var before = Rule(Proc4688("fsutil usn createjournal m=1000 a=100 C:", 0), Proc4688("fsutil usn deletejournal /d C:", 10));
        Assert.Equal(2, before.Count);
    }

    [Theory]
    [InlineData("Write-Host \"fsutil usn deletejournal /d C:\"")]
    [InlineData("Write-Output 'fsutil usn deletejournal /d C:'")]
    [InlineData("# fsutil usn deletejournal /d C:")]
    [InlineData("notepad.exe C:\\Users\\a\\deletejournal.txt")]
    [InlineData("copy deletejournal.log D:\\backup")]
    [InlineData("fsutil usn queryjournal C:")]
    [InlineData("fsutil usn readjournal C: csv")]
    [InlineData("fsutil fsinfo drives")]
    public void Text_only_file_names_and_read_verbs_make_no_finding(string cmd)
    {
        Assert.Empty(Rule(Proc4688(cmd)));
        Assert.Empty(Rule(Sys1(cmd)));
        Assert.Empty(Rule(Ps4104(cmd)));
    }

    [Fact]
    public void The_rule_is_in_the_contract_catalog()
    {
        Assert.Contains(RuleContracts.All, c => c.RuleId == RuleId);
    }

    // ---------------- AF07 ----------------

    private static AntiForensicCheck Af07(IReadOnlyList<TimelineEvent> events, params EvidenceGap[] gaps) => AntiForensics.Evaluate(events, gaps).Single(c => c.Id == "AF07");

    private static EvidenceGap NoJournal(string letter = "C") => new($"NTFS metadata", EvidenceStatus.Partial,
        $"jurnalul USN {letter}: nu a putut fi citit (volumul nu are jurnal USN activ (nu s-a creat unul)); $MFT și $I30 rămân valabile, dar regula NTFS-TIMESTOMP nu poate folosi calea cu jurnal", "", "", "");

    private static TimelineEvent UsnRow(string journalId) => Row("USN", "", T0, ("JournalId", journalId), ("FileName", "x.txt"), ("Reason", "Close"));

    private static string IdAt(DateTime utc) => "0x" + utc.ToFileTimeUtc().ToString("x16");

    [Fact]
    public void A_missing_journal_on_the_system_volume_is_detected_medium()
    {
        var c = Af07([Proc4688(@"C:\Windows\System32\cmd.exe /c whoami")], NoJournal("C"));
        // the process path shows C: is the volume Windows runs from
        Assert.Equal(AntiForensicResult.Detected, c.Result);
        Assert.Contains("volumul de sistem nu are jurnal USN activ", c.Reason);
        Assert.Contains("Medie", c.Reason);
        Assert.Contains("creează implicit", c.Reason);
    }

    [Fact]
    public void A_missing_journal_is_undetermined_when_it_is_not_known_to_be_the_system_volume()
    {
        Assert.Equal(AntiForensicResult.Undetermined, Af07([], NoJournal("C")).Result);
        var other = Af07([Proc4688(@"C:\Windows\System32\cmd.exe /c whoami")], NoJournal("D"));
        Assert.Equal(AntiForensicResult.Undetermined, other.Result);
        Assert.Contains("nu se știe", other.Reason);
    }

    [Fact]
    public void A_journal_created_long_after_the_oldest_event_record_is_a_recreation_signal_with_the_tolerance_stated()
    {
        var evtx = Row("EventLog:System", "6005", T0, ("x", "y"));
        var c = Af07([evtx, UsnRow(IdAt(T0.AddDays(30)))]);
        Assert.Equal(AntiForensicResult.Detected, c.Result);
        Assert.Contains("De verificat", c.Reason);
        Assert.Contains("chkdsk", c.Reason);
    }

    [Fact]
    public void A_journal_created_within_the_tolerance_after_the_oldest_record_is_not_detected()
    {
        var evtx = Row("EventLog:System", "6005", T0, ("x", "y"));
        Assert.Equal(AntiForensicResult.NotDetected, Af07([evtx, UsnRow(IdAt(T0.AddHours(3)))]).Result);
        Assert.Equal(AntiForensicResult.NotDetected, Af07([evtx, UsnRow(IdAt(T0.AddDays(-5)))]).Result);
    }

    [Fact]
    public void Without_an_analysed_journal_and_without_a_gap_AF07_is_undetermined()
    {
        Assert.Equal(AntiForensicResult.Undetermined, Af07([]).Result);
        Assert.Equal(AntiForensicResult.Undetermined, Af07([Proc4688("whoami")]).Result);
    }

    [Fact]
    public void Two_journal_ids_are_still_detected()
    {
        Assert.Equal(AntiForensicResult.Detected, Af07([UsnRow(IdAt(T0)), UsnRow(IdAt(T0.AddDays(1)))]).Result);
    }

    // ---------------- real corpus: a fresh volume, journal created right after the format ----------------

    private static string Ntfs(string n) => Path.Combine(AppContext.BaseDirectory, "Corpus", "NtfsLog", n);

    [Fact]
    public void The_real_fresh_volume_corpus_is_not_detected_and_has_no_deletion_finding()
    {
        var sink = new ListSink();
        var usnPath = Ntfs("usn.csv");
        new UsnJournalParser(TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time"))
            .Parse(new EvidenceItem { EvidenceId = "EV-USN", CaseId = "C", Source = "t", SourceType = "usn_journal", StoredPath = usnPath }, usnPath, sink, default);
        var mftPath = Ntfs("mft.bin");
        new MftParser().Parse(new EvidenceItem { EvidenceId = "EV-MFT", CaseId = "C", Source = "t", SourceType = MftParser.SourceTypeName, StoredPath = mftPath,
            AcquiredAtUtc = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero) }, mftPath, sink, default);
        var events = sink.Events.ToList();
        Assert.Contains(events, e => e.Source == "USN");
        var c = Af07(events);
        Assert.NotEqual(AntiForensicResult.Detected, c.Result);
        Assert.Equal(AntiForensicResult.NotDetected, c.Result);
        Assert.Empty(Rule(events.ToArray()));
    }
}
