using System.Diagnostics;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review round for USN-JOURNAL-DELETED / AF07: text that is only printed (strings, here-strings, comments, a findstr argument) is not a command;
/// the process image is the authority for 4688 / Sysmon 1; no backtracking on the raw input (linear scan with a length cap and an explicit gap);
/// a volume is paired only when the same and non-empty; 4688 + Sysmon 1 of one execution are one command; AF07 anchors on the 1st percentile of EVTX times.
/// </summary>
public sealed class UsnJournalDeletionReviewTests
{
    private const string RuleId = "USN-JOURNAL-DELETED";
    private const string Sysmon = "EventLog:Microsoft-Windows-Sysmon/Operational";
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
    private static int _n;

    private static TimelineEvent Row(string source, string eventId, DateTime utc, string host, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(utc, "", "test"), Source = source, EventId = eventId, EvidenceId = "EV-R", Host = host, Summary = source + " " + eventId,
        Locator = $"row={Interlocked.Increment(ref _n)}", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent P4688(string cmd, string image = "", double sec = 0, string host = "PC1") =>
        Row("EventLog:Security", "4688", T0.AddSeconds(sec), host, image.Length > 0 ? [("CommandLine", cmd), ("NewProcessName", image)] : [("CommandLine", cmd)]);
    private static TimelineEvent S1(string cmd, string image = "", string original = "", double sec = 0, string host = "PC1") =>
        Row(Sysmon, "1", T0.AddSeconds(sec), host, new[] { ("CommandLine", cmd), ("Image", image), ("OriginalFileName", original) });
    private static TimelineEvent Ps(string script, double min = 0) => Row("EventLog:Microsoft-Windows-PowerShell/Operational", "4104", T0.AddMinutes(min), "PC1", ("ScriptBlockText", script));

    private static List<Finding> All(params TimelineEvent[] events)
    {
        int n = 0;
        return Wp14Rules.Run(events, () => $"F-{++n:D4}", new Wp14Input());
    }
    private static List<Finding> Rule(params TimelineEvent[] events) => All(events).Where(f => f.RuleId == RuleId).ToList();

    // ---------------- M1: text that is only displayed ----------------

    public static TheoryData<string> PowerShellTextOnly() =>
    [
        "Write-Host \"Pasul 2; fsutil usn deletejournal /d C:\"",
        "Write-Host 'Comanda | fsutil usn deletejournal /d C:'",
        "Write-Host \"{fsutil usn deletejournal /d C:}\"",
        "$help = @\"\nUtilizare:\nfsutil usn deletejournal /d C:\n\"@\nWrite-Host $help",
        "$help = @'\nfsutil usn deletejournal /d C:\n'@\nWrite-Host $help",
        "<#\nfsutil usn deletejournal /d C:\n#>\nGet-Date",
        "Get-Date # fsutil usn deletejournal /d C:",
        "Select-String -Pattern 'fsutil usn deletejournal' -Path *.ps1",
    ];

    [Theory]
    [MemberData(nameof(PowerShellTextOnly))]
    public void PowerShell_text_that_is_only_displayed_makes_no_finding(string script) => Assert.Empty(Rule(Ps(script)));

    [Theory]
    [InlineData("findstr \"x & fsutil usn deletejournal\" a.txt")]
    [InlineData("cmd /c echo \"fsutil usn deletejournal /d C:\" & echo x")]
    [InlineData("cmd /c echo fsutil usn deletejournal /d C:")]
    [InlineData("powershell -c \"Write-Host 'a; fsutil usn deletejournal /d C:'\"")]
    public void Process_command_lines_that_only_carry_the_text_make_no_finding(string cmd)
    {
        Assert.Empty(Rule(P4688(cmd)));
        Assert.Empty(Rule(P4688(cmd, @"C:\Windows\System32\findstr.exe")));
        Assert.Empty(Rule(S1(cmd, @"C:\Windows\System32\cmd.exe")));
    }

    [Fact]
    public void The_process_image_is_the_authority_argv0_is_not_read()
    {
        // argv[0] spoofed, the image is fsutil.exe: the arguments decide
        var spoof = Assert.Single(Rule(P4688("svchost usn deletejournal /d C:", @"C:\Windows\System32\fsutil.exe")));
        Assert.Equal(Severity.High, spoof.Severity);
        // renamed fsutil: OriginalFileName
        var renamed = Assert.Single(Rule(S1("x.exe usn deletejournal /d C:", @"C:\Users\Public\x.exe", "fsutil.exe")));
        Assert.Equal(Severity.High, renamed.Severity);
        // fsutil.exe image but another verb, or the word only in a later argument
        Assert.Empty(Rule(P4688("fsutil usn queryjournal C:", @"C:\Windows\System32\fsutil.exe")));
        Assert.Empty(Rule(P4688("fsutil file findbyname deletejournal usn", @"C:\Windows\System32\fsutil.exe")));
        // image is not fsutil and the line does not run it as a statement
        Assert.Empty(Rule(P4688("fsutil.exe usn deletejournal /d C:", @"C:\Windows\System32\notepad.exe")));
    }

    [Theory]
    [InlineData("\"C:\\Windows\\system32\\cmd.exe\" /c fsutil usn deletejournal /d C:", "C:\\Windows\\system32\\cmd.exe")]
    [InlineData("cmd /q /c \"fsutil usn deletejournal /n c:\"", "")]
    [InlineData("cmd /c echo start & fsutil usn deletejournal /d C:", "")]
    [InlineData("powershell.exe -NoProfile -Command \"fsutil usn deletejournal /d C:\"", "")]
    [InlineData("powershell -c fsutil usn deletejournal /d C:", "")]
    [InlineData("cmd /c powershell -c fsutil usn deletejournal /d C:", "")]
    public void Real_statements_through_a_shell_stay_High(string cmd, string image)
    {
        var f = Assert.Single(Rule(P4688(cmd, image)));
        Assert.Equal(Severity.High, f.Severity);
    }

    [Theory]
    [InlineData("Write-Host 'x'; fsutil usn deletejournal /d C:")]
    [InlineData("if ($true) {fsutil.exe usn deletejournal /n D:}")]
    [InlineData("Get-Date | Out-Null\nfsutil usn deletejournal /d C:")]
    [InlineData("& 'C:\\Windows\\System32\\fsutil.exe' usn deletejournal /d C:")]
    [InlineData("cmd /c \"fsutil usn deletejournal /d C:\"")]
    [InlineData("$x = @\"\ntext\n\"@\nfsutil usn deletejournal /d C:")]
    [InlineData("<# note #> fsutil usn deletejournal /d C:")]
    public void Real_PowerShell_statements_stay_High(string script)
    {
        var f = Assert.Single(Rule(Ps(script)));
        Assert.Equal(Severity.High, f.Severity);
    }

    // ---------------- M2: linear, capped, explicit gap ----------------

    [Theory]
    [InlineData(3000)]
    [InlineData(15000)]
    public void Padding_with_spaces_and_the_word_journal_does_not_slow_the_scan_or_hide_a_real_command(int spaces)
    {
        string pad = "journal" + new string(' ', spaces) + "journal";
        var events = new[]
        {
            P4688($"cmd /c echo {pad} & fsutil usn deletejournal /d C:", "", 0),
            Ps($"Write-Host '{pad}'; fsutil usn deletejournal /d C:", 5),
            P4688($"fsutil usn deletejournal /d D:{new string(' ', spaces)}journal", @"C:\Windows\System32\fsutil.exe", 600),
        };
        Rule(events);   // warm up the JIT: the bound is for the scan, not for loading the code
        var sw = Stopwatch.StartNew();
        var found = Rule(events);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 100, $"{sw.ElapsedMilliseconds} ms");
        Assert.Single(found, f => f.Severity == Severity.High);   // all three are deletes: one High finding listing them
        Assert.Contains("3 comenzi", found.Single(f => f.Severity == Severity.High).Description);
    }

    [Fact]
    public void A_one_megabyte_event_is_capped_with_an_explicit_gap_and_the_analysed_part_still_counts()
    {
        string filler = string.Concat(Enumerable.Repeat("Get-Date ; ", 100_000));   // ~1.1 MB
        Assert.True(filler.Length > 1_000_000);
        var headHit = Ps("fsutil usn deletejournal /d C:\n" + filler);
        var all = All(headHit, P4688("fsutil usn createjournal m=1 a=1 D:"));
        Assert.Contains(all, f => f.RuleId == RuleId && f.Severity == Severity.High);   // the analysed part gave a result
        Assert.Contains(all, f => f.RuleId == RulePatterns.NotEvaluatedRuleId && f.Description.Contains("256"));   // and the cap is reported
        Assert.Contains(all, f => f.RuleId == RuleId && f.Severity == Severity.Low);    // the rest of the events were still analysed

        // a command placed after the cap is NOT found, and that is stated, not silent
        var tailHit = All(Ps(filler + "\nfsutil usn deletejournal /d C:"));
        Assert.DoesNotContain(tailHit, f => f.RuleId == RuleId);
        Assert.Contains(tailHit, f => f.RuleId == RulePatterns.NotEvaluatedRuleId);
    }

    // ---------------- L5: volume pairing and duplicates ----------------

    [Fact]
    public void A_volume_guid_survives_and_pairs_a_delete_with_a_create_on_the_same_volume()
    {
        const string vol = @"\\?\Volume{0a1b2c3d-1111-2222-3333-444455556666}";
        var f = Assert.Single(Rule(Ps($"fsutil usn deletejournal /d {vol}\\", 0), Ps($"fsutil usn createjournal m=1 a=1 {vol}\\", 20)));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("recreat", f.Description);
        // unquoted in a script block, braces and all
        var g = Assert.Single(Rule(Ps($"fsutil usn deletejournal {vol}")));
        Assert.Equal(Severity.High, g.Severity);
    }

    [Fact]
    public void Without_a_volume_nothing_is_paired_so_recreation_is_never_asserted()
    {
        var found = Rule(P4688("fsutil usn deletejournal", "", 0), P4688("fsutil usn createjournal", "", 600));
        Assert.Equal(2, found.Count);
        Assert.DoesNotContain(found, f => f.Description.Contains("șters și recreat"));
        Assert.Contains(found, f => f.Severity == Severity.Low);
    }

    [Fact]
    public void One_execution_logged_by_4688_and_by_Sysmon_1_is_one_command()
    {
        string cmd = "fsutil  usn  deletejournal /D C:";
        var f = Assert.Single(Rule(P4688(cmd, @"C:\Windows\System32\fsutil.exe", 0), S1("FSUTIL usn deletejournal /d C:", @"C:\Windows\System32\fsutil.exe", "fsutil.exe", 1.5)));
        Assert.Contains("1 comandă de ștergere", f.Description);
        // another host, or a later second execution, is a separate command
        var two = Assert.Single(Rule(P4688(cmd, "", 0, "PC1"), S1(cmd, "", "", 1, "PC2")));
        Assert.Contains("2 comenzi de ștergere", two.Description);
        var later = Assert.Single(Rule(P4688(cmd, "", 0), S1(cmd, "", "", 30)));
        Assert.Contains("2 comenzi de ștergere", later.Description);
    }

    // ---------------- L3: AF07 anchor ----------------

    private static string IdAt(DateTime utc) => "0x" + utc.ToFileTimeUtc().ToString("x16");

    [Fact]
    public void One_event_with_a_wrong_clock_does_not_move_the_AF07_anchor()
    {
        var install = T0.AddDays(-200);
        var evtx = Enumerable.Range(0, 2000).Select(i => Row("EventLog:System", "6005", install.AddMinutes(i), "PC1", ("x", "y"))).ToList();
        var usn = Row("USN", "", T0, "PC1", ("JournalId", IdAt(install)), ("FileName", "x"), ("Reason", "Close"));
        AntiForensicCheck A(IEnumerable<TimelineEvent> ev) => AntiForensics.Evaluate(ev.ToList(), []).Single(c => c.Id == "AF07");
        Assert.Equal(AntiForensicResult.NotDetected, A(evtx.Append(usn)).Result);
        var cmos = Row("EventLog:System", "12", new DateTime(2000, 1, 1, 0, 0, 5, DateTimeKind.Utc), "PC1", ("x", "y"));
        var withClock = A(evtx.Append(cmos).Append(usn));
        Assert.NotEqual(AntiForensicResult.Detected, withClock.Result);
        // and the text says what moves the anchor
        var late = A(evtx.Select(e => e).Append(Row("USN", "", T0, "PC1", ("JournalId", IdAt(install.AddDays(60))), ("FileName", "x"), ("Reason", "Close"))));
        Assert.Equal(AntiForensicResult.Detected, late.Result);
        Assert.Contains("ceas", late.Reason);
        Assert.Contains("rotite", late.Reason);
    }
}
