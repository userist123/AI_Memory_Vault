using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Evaluation 2026-11-05: signed per-user programs (browser, AI clients) under the profile were raised as high on an ordinary PC.</summary>
public class SignatureCalibrationTests
{
    private static Finding F(string rule, string file, Severity s = Severity.High) =>
        new() { FindingId = "F-0001", RuleId = rule, Title = "t", Description = "d", File = file, Severity = s };

    private static readonly SignatureInfo Signed = new(true, true, "embedded", "valid", "Google LLC");
    private static readonly SignatureInfo Unsigned = new(false, false, "", "unsigned", "");
    private static readonly SignatureInfo Broken = new(true, false, "embedded", "bad digest", "Evil");

    [Fact]
    public void A_validly_signed_file_present_on_the_station_is_lowered_to_medium_and_says_why()
    {
        var f = F("NET-USERPATH-UPLOAD", @"C:\Users\user\AppData\Local\Google\Chrome\Application\chrome.exe");
        var r = Assert.Single(SignatureCalibration.Apply([f], _ => true, _ => Signed));
        Assert.Equal(Severity.Medium, r.Severity);
        Assert.Contains("Google LLC", Assert.Single(r.Limitations));
        Assert.Equal(Severity.High, f.Severity);          // the original is not changed
        Assert.Empty(f.Limitations);
    }

    [Theory]
    [InlineData("EXEC-USERPATH-CORRELATED", @"C:\Users\user\AppData\Local\Temp\x.exe", false, "signed")]     // the file is gone: nothing to check
    [InlineData("EXEC-USERPATH-CORRELATED", @"C:\Users\user\AppData\Local\Temp\x.exe", true, "unsigned")]
    [InlineData("EXEC-USERPATH-CORRELATED", @"C:\Users\user\AppData\Local\Temp\x.exe", true, "broken")]
    [InlineData("PERSIST-RUNKEY-USERPATH", @"C:\Users\user\AppData\Local\x.exe", true, "signed")]           // persistence is not calibrated
    [InlineData("EXEC-USERPATH", @"\VOLUME{01}\USERS\USER\X.EXE", true, "signed")]                           // a prefetch volume path is not resolved
    public void Nothing_is_lowered_when_the_signature_cannot_vouch_for_the_file(string rule, string file, bool exists, string sig)
    {
        var info = sig switch { "signed" => Signed, "unsigned" => Unsigned, _ => Broken };
        Assert.Equal(Severity.High, Assert.Single(SignatureCalibration.Apply([F(rule, file)], _ => exists, _ => info)).Severity);
    }

    private static readonly ArtifactPathResolver Station = new(
        new Dictionary<string, string> { [@"\Device\HarddiskVolume3"] = "C:" }, new Dictionary<uint, string> { [0x1A2B3C4D] = "C:" }, @"C:\");

    [Theory]
    [InlineData(@"\device\harddiskvolume3\users\u\appdata\local\programs\ollama\ollama.exe", @"C:\users\u\appdata\local\programs\ollama\ollama.exe")]
    [InlineData(@"\VOLUME{01d9a0b1c2d3e4f5-1a2b3c4d}\USERS\U\APPDATA\LOCAL\X.EXE", @"C:\USERS\U\APPDATA\LOCAL\X.EXE")]
    [InlineData(@"\users\u\AppData\Local\agy\bin\agy.exe", @"C:\users\u\AppData\Local\agy\bin\agy.exe")]
    [InlineData(@"D:\tools\a.exe", @"D:\tools\a.exe")]
    public void Artifact_path_forms_resolve_to_this_station(string artifact, string local) => Assert.Equal(local, Station.Resolve(artifact));

    [Theory]
    [InlineData(@"\device\harddiskvolume9\users\u\a.exe")]        // a volume this station does not have
    [InlineData(@"\VOLUME{01d9a0b1c2d3e4f5-deadbeef}\USERS\A.EXE")] // another volume serial (an imported image)
    [InlineData(@"\\server\share\a.exe")]
    [InlineData(@"a.exe")]
    [InlineData("")]
    public void A_path_that_cannot_be_placed_on_this_station_is_not_guessed(string artifact) => Assert.Null(Station.Resolve(artifact));

    [Fact]
    public void A_srum_device_path_is_checked_on_the_resolved_local_file()
    {
        string? checkedPath = null;
        var r = Assert.Single(SignatureCalibration.Apply([F("NET-USERPATH-UPLOAD", @"\device\harddiskvolume3\users\u\appdata\local\discord\app-1\discord.exe")],
            p => { checkedPath = p; return true; }, _ => Signed, Station.Resolve, _ => true));
        Assert.Equal(@"C:\users\u\appdata\local\discord\app-1\discord.exe", checkedPath);
        Assert.Equal(Severity.Medium, r.Severity);
    }

    [Fact]
    public void Review_critical_goes_down_one_level_and_the_audit_trail_says_so()
    {
        var r = Assert.Single(SignatureCalibration.Apply([F("EXEC-USERPATH", @"C:\Users\user\AppData\Local\a.exe", Severity.Critical)], _ => true, _ => Signed));
        Assert.Equal(Severity.High, r.Severity);
        var audit = Assert.Single(r.AuditTrail);
        Assert.Equal("severity.calibrated", audit.Step);
        Assert.Contains("Revocarea certificatului nu a fost verificată", Assert.Single(r.Limitations));
    }

    [Fact]
    public void Review_a_correlated_finding_stays_high_even_when_the_program_is_signed()
    {
        // a signed program in AppData beside a DLL Defender flagged (side-loading) must not be lowered
        var r = Assert.Single(SignatureCalibration.Apply([F("EXEC-USERPATH-CORRELATED", @"C:\Users\user\AppData\Local\a.exe")], _ => true, _ => Signed));
        Assert.Equal(Severity.High, r.Severity);
    }

    [Fact]
    public void Review_evidence_not_collected_live_here_is_never_calibrated()
    {
        var r = Assert.Single(SignatureCalibration.Apply([F("NET-USERPATH-UPLOAD", @"\users\x\AppData\Local\app.exe")], _ => true, _ => Signed, Station.Resolve, _ => false));
        Assert.Equal(Severity.High, r.Severity);
    }

    [Fact]
    public void Medium_findings_are_left_alone_and_each_file_is_verified_once()
    {
        int calls = 0;
        var path = @"C:\Users\user\AppData\Local\a.exe";
        var r = SignatureCalibration.Apply([F("EXEC-USERPATH", path), F("EXEC-USERPATH", path), F("EXEC-USERPATH", path, Severity.Medium)], _ => true, _ => { calls++; return Signed; }).ToList();
        Assert.Equal(1, calls);
        Assert.All(r, x => Assert.Equal(Severity.Medium, x.Severity));
    }
}
