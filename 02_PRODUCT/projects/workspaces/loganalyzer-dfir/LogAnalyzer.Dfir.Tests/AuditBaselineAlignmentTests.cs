using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Research 2026-11-07 („Acoperirea auditului pe stații izolate”), plan P1: the station control follows the published baselines where they are stricter than
/// the application was. DISA STIG WN11-AU-000085/-000090 ask Removable Storage on success AND failure; CIS Windows 11 v5.0.1 asks a Security log of at least
/// 196,608 KB. The capability texts say what 4663 and 6416 really prove.
/// </summary>
public sealed class AuditBaselineAlignmentTests
{
    private static StationFacts Station(Dictionary<string, AuditSetting>? audit = null, string? logMb = null)
    {
        var f = new StationFacts { Posture = StationPosture.Standalone, IsAdministrator = true, AuditPolicy = audit };
        if (logMb is not null) f.Settings["SecurityLog.MaxSizeMB"] = logMb;
        return f;
    }

    private static Dictionary<string, AuditSetting> AllRequired(AuditSetting removable) => new()
    {
        ["Logon"] = AuditSetting.Success | AuditSetting.Failure, ["Special Logon"] = AuditSetting.Success, ["User Account Management"] = AuditSetting.Success,
        ["Security Group Management"] = AuditSetting.Success, ["Audit Policy Change"] = AuditSetting.Success, ["Process Creation"] = AuditSetting.Success,
        ["Removable Storage"] = removable, ["Plug and Play Events"] = AuditSetting.Success, ["Security State Change"] = AuditSetting.Success,
        ["Other Object Access Events"] = AuditSetting.Success,
    };

    private static ControlCheck Check(StationFacts f, string id) => ControlEvaluator.Evaluate(f).Checks.Single(c => c.Id == id);

    [Fact]
    public void Removable_storage_audited_on_success_only_is_no_longer_enough_for_A01()
    {
        var onlySuccess = Check(Station(AllRequired(AuditSetting.Success)), "A01");
        Assert.Equal(ControlStatus.Neconform, onlySuccess.Status);
        Assert.Contains("Removable Storage", onlySuccess.Detail);
        Assert.Equal(ControlStatus.Conform, Check(Station(AllRequired(AuditSetting.Success | AuditSetting.Failure)), "A01").Status);
        // A08 asks only whether copies are recorded: success is enough there
        Assert.Equal(ControlStatus.Conform, Check(Station(AllRequired(AuditSetting.Success)), "A08").Status);
    }

    [Theory]
    [InlineData("128", ControlStatus.Neconform)]   // the old threshold, below CIS
    [InlineData("191", ControlStatus.Neconform)]
    [InlineData("192", ControlStatus.Conform)]
    [InlineData("4096", ControlStatus.Conform)]
    public void The_Security_log_must_hold_at_least_the_CIS_minimum(string mb, ControlStatus expected)
    {
        var a02 = Check(Station(logMb: mb), "A02");
        Assert.Equal(expected, a02.Status);
        if (expected == ControlStatus.Neconform) Assert.Contains("192 MB", a02.Recommendation);
    }

    [Fact]
    public void The_capability_texts_say_4663_needs_no_SACL_on_removable_volumes_and_6416_does_not_name_the_user()
    {
        var all = string.Join("\n", ParserCapabilities.All.SelectMany(p => p.Channels).SelectMany(c => c.CanProve.Concat(c.CannotProve)));
        Assert.Contains("Removable Storage (fără SACL pe fișiere)", all);
        Assert.Contains("contul din eveniment este de regulă SYSTEM", all);
        Assert.DoesNotContain("contul care era conectat", all);
    }
}
