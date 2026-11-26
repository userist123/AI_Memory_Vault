using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review 2026-11-14 (L1, round 2), fixed by the main session after the last allowed round: compact PowerShell blocks were missed, and
/// typographic quotes, here-strings after '=' and a powershell.exe line split by cmd rules turned printed text into a High.
/// </summary>
public sealed class UsnJournalDeletionRound2Tests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private static int _n;

    private static TimelineEvent Row(string source, string eventId, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0, "", "test"), Source = source, EventId = eventId, EvidenceId = "EV-R2", Host = "PC1", Summary = source + " " + eventId,
        Locator = $"row={Interlocked.Increment(ref _n)}", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Ps(string script) => Row("EventLog:Microsoft-Windows-PowerShell/Operational", "4104", ("ScriptBlockText", script));
    private static TimelineEvent P4688(string cmd, string image) => Row("EventLog:Security", "4688", ("CommandLine", cmd), ("NewProcessName", image));

    private static List<Finding> Rule(params TimelineEvent[] events)
    {
        int n = 0;
        return Wp14Rules.Run(events, () => $"F-{++n:D4}", new Wp14Input()).Where(f => f.RuleId == "USN-JOURNAL-DELETED").ToList();
    }

    [Theory]
    [InlineData("try{fsutil usn deletejournal /d C:}catch{}")]
    [InlineData("if($true){fsutil usn deletejournal /d C:}")]
    [InlineData("foreach($v in $vols){fsutil usn deletejournal /d C:}")]
    public void Compact_blocks_that_PowerShell_runs_are_found(string script) =>
        Assert.Contains(Rule(Ps(script)), f => f.Severity == Severity.High);

    [Theory]
    [InlineData("Write-Host \u201EPasul 2; fsutil usn deletejournal /d C:\u201D")]
    [InlineData("Write-Host \u201Ca; fsutil usn deletejournal /d C:\u201D")]
    [InlineData("Write-Host \u2018a; fsutil usn deletejournal /d C:\u2019")]
    [InlineData("$t=@'\nDon't run:\nfsutil usn deletejournal /d C:\n'@\nWrite-Host $t")]
    [InlineData("$t=@\"\n5\" disk\nfsutil usn deletejournal /d C:\n\"@\nWrite-Host $t")]
    public void Printed_text_in_typographic_quotes_or_here_strings_is_not_a_command(string script) =>
        Assert.Empty(Rule(Ps(script)));

    [Fact]
    public void A_typographic_quote_closes_its_string_so_the_next_line_is_read()
    {
        // before the fix the lexer did not know “ ” as quotes: the apostrophe in it's opened a string that hid the real command
        Assert.Contains(Rule(Ps("Write-Host \u201Cit's ok\u201D\nfsutil usn deletejournal /d C:")), f => f.Severity == Severity.High);
    }

    [Fact]
    public void A_powershell_exe_line_is_not_split_by_cmd_rules()
    {
        Assert.Empty(Rule(P4688("powershell.exe -Command Write-Host 'a; fsutil usn deletejournal /d C:'", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")));
        Assert.Contains(Rule(P4688("powershell.exe -Command fsutil usn deletejournal /d C:", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")),
            f => f.Severity == Severity.High);
    }
}
