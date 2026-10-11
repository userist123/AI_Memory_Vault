using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Detection wave 3 (senior review D11-D23): one positive and one benign negative case per rule.</summary>
public class DetectionWave3Tests
{
    private const string Defender = "Microsoft-Windows-Windows Defender/Operational";
    private const string PowerShell = "Microsoft-Windows-PowerShell/Operational";
    private const string TaskSched = "Microsoft-Windows-TaskScheduler/Operational";
    private const string Wmi = "Microsoft-Windows-WMI-Activity/Operational";

    private static List<Finding> Corr(params TimelineEvent[] events) => Correlation.Run(events);
    private static List<Finding> Corr(IEnumerable<TimelineEvent> events) => Correlation.Run(events.ToList());
    private static TimelineEvent Fail(double minute, string user, string ip = "203.0.113.9") =>
        Sec(4625, minute, ("TargetUserName", user), ("IpAddress", ip));

    // ---- D11 Defender tamper ----

    [Fact]
    public void Defender_5013_and_a_Disable_setting_set_to_1_are_DEF_TAMPER()
    {
        Assert.Contains(Corr(Ev(Defender, 5013, 0)), x => x.RuleId == "DEF-TAMPER");
        var f = One(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Real-Time Protection\DisableRealtimeMonitoring = 0x1"))), "DEF-TAMPER");
        Assert.Equal("T1562.001", f.MitreTechniqueId);
        Assert.Contains("setare slăbită", f.Description);
    }

    [Fact]
    public void Defender_setting_that_stays_protective_is_not_DEF_TAMPER()
    {
        Assert.DoesNotContain(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Real-Time Protection\DisableRealtimeMonitoring = 0x0"))), x => x.RuleId == "DEF-TAMPER");
        Assert.DoesNotContain(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Signature Updates\SignatureUpdateInterval = 0x8"))), x => x.RuleId == "DEF-TAMPER");
    }

    [Fact]
    public void Exclusion_command_lines_are_DEF_TAMPER_and_a_re_enable_is_not()
    {
        var add = Sec(4688, 0, ("NewProcessName", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"), ("CommandLine", @"powershell Add-MpPreference -ExclusionPath C:\Users\bob\AppData\Local\Temp"), ("SubjectUserName", "bob"));
        Assert.Contains(Corr(add), x => x.RuleId == "DEF-TAMPER");
        var script = Ev(PowerShell, 4104, 0, "", ("ScriptBlockText", "Set-MpPreference -DisableRealtimeMonitoring $true"));
        Assert.Contains(Corr(script), x => x.RuleId == "DEF-TAMPER");
        var enable = Sec(4688, 0, ("NewProcessName", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"), ("CommandLine", "powershell Set-MpPreference -DisableRealtimeMonitoring $false"), ("SubjectUserName", "bob"));
        Assert.DoesNotContain(Corr(enable), x => x.RuleId == "DEF-TAMPER");
    }

    // ---- D12 password spray and brute-force window ----

    [Fact]
    public void Ten_distinct_users_from_one_address_in_30_minutes_is_CRED_SPRAY()
    {
        var f = One(Corr(Enumerable.Range(0, 10).Select(i => Fail(i, "user" + i))), "CRED-SPRAY");
        Assert.Equal("T1110.003", f.MitreTechniqueId);
        Assert.Equal("203.0.113.9", f.Ip);
        Assert.DoesNotContain(Corr(Enumerable.Range(0, 10).Select(i => Fail(i, "user" + i))), x => x.RuleId == "CRED-BRUTEFORCE");
    }

    [Fact]
    public void Nine_users_or_users_spread_over_hours_are_not_CRED_SPRAY()
    {
        Assert.DoesNotContain(Corr(Enumerable.Range(0, 9).Select(i => Fail(i, "user" + i))), x => x.RuleId == "CRED-SPRAY");
        Assert.DoesNotContain(Corr(Enumerable.Range(0, 10).Select(i => Fail(i * 60, "user" + i))), x => x.RuleId == "CRED-SPRAY");
    }

    [Fact]
    public void Brute_force_needs_the_failures_inside_the_window()
    {
        Assert.Contains(Corr(Enumerable.Range(0, 10).Select(i => Fail(i, "alice"))), x => x.RuleId == "CRED-BRUTEFORCE");
        Assert.DoesNotContain(Corr(Enumerable.Range(0, 10).Select(i => Fail(i * 120, "alice"))), x => x.RuleId == "CRED-BRUTEFORCE");
    }

    // ---- D13 PowerShell ----

    private static TimelineEvent Block(double minute, string text, string id, params (string K, string V)[] extra) =>
        Ev(PowerShell, 4104, minute, "", [("ScriptBlockText", text), ("ScriptBlockId", id), .. extra]);

    [Fact]
    public void Every_suspicious_script_block_is_its_own_finding_and_an_AMSI_bypass_is_High()
    {
        var found = Corr(Block(0, "[Ref].Assembly.GetType('System.Management.Automation.AmsiUtils').GetField('amsiInitFailed','NonPublic,Static').SetValue($null,$true)", "b1"),
                         Block(1, "$b=[Convert]::FromBase64String($s); iex ($x)", "b2"));
        var ps = found.Where(x => x.RuleId == "PS-SUSPICIOUS").ToList();
        Assert.Equal(2, ps.Count);
        Assert.Single(ps, x => x.Severity == Severity.High && x.MitreTechniqueId == "T1562.001");
        Assert.Single(ps, x => x.Severity == Severity.Medium);
    }

    [Fact]
    public void A_Warning_level_block_is_suspicious_by_itself_but_an_execution_policy_switch_is_not()
    {
        Assert.Single(Corr(Block(0, "Get-Date", "w1", ("_xLevel", "3"))), x => x.RuleId == "PS-SUSPICIOUS");
        Assert.DoesNotContain(Corr(Block(0, "powershell.exe -ExecutionPolicy Bypass -File C:\\ProgramData\\Sccm\\install.ps1", "w2", ("_xLevel", "5"))), x => x.RuleId == "PS-SUSPICIOUS");
        Assert.DoesNotContain(Corr(Block(0, "Get-ChildItem | Out-File -Encoding utf8 out.txt", "w3")), x => x.RuleId == "PS-SUSPICIOUS");
    }

    // ---- D14 WMI persistence ----

    [Fact]
    public void A_permanent_WMI_consumer_is_PERSIST_WMI_but_the_built_in_one_is_not()
    {
        var f = One(Corr(Ev(Wmi, 5861, 0, "", ("CONSUMER", "CommandLineEventConsumer.Name=\"Updater\""), ("Namespace", "//./root/subscription"))), "PERSIST-WMI");
        Assert.Equal("T1546.003", f.MitreTechniqueId);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("PERSIST-WMI", Corr(Sysmon(21, 0, ("Consumer", "CommandLineEventConsumer.Name=\"x\""))).Select(x => x.RuleId));
        Assert.DoesNotContain(Corr(Ev(Wmi, 5861, 0, "", ("CONSUMER", "NTEventLogEventConsumer.Name=\"SCM Event Log Consumer\""))), x => x.RuleId == "PERSIST-WMI");
    }

    // ---- D15 RMM catalog ----

    private static TimelineEvent Prefetch(double m, string exe, string path) =>
        Row("Prefetch", m, exe, path, f: [("RunCount", "2"), ("ReferencedFiles", @"\VOLUME{01d}" + path.Replace("C:", ""))]);

    [Theory]
    [InlineData("AteraAgent.exe", "Atera")]
    [InlineData("client32.exe", "NetSupport")]
    [InlineData("LogMeIn.exe", "LogMeIn")]
    [InlineData("NinjaRMMAgent.exe", "Ninja")]
    [InlineData("SimpleHelp.exe", "SimpleHelp")]
    [InlineData("TacticalAgent.exe", "Tactical")]
    public void An_executed_unapproved_new_RMM_tool_is_High(string exe, string name)
    {
        var f = One(Run([Prefetch(0, exe.ToUpperInvariant(), @"C:\Program Files\Tool\" + exe)]), "REMOTE-TOOL-EXECUTED");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains(name, f.Title);
        var approved = ProfileWithSoftware((name, "", "", ""));
        Assert.Equal(Severity.Info, One(Run([Prefetch(0, exe.ToUpperInvariant(), @"C:\Program Files\Tool\" + exe)], approved), "REMOTE-TOOL-EXECUTED").Severity);
    }

    [Fact]
    public void An_unrelated_program_is_not_an_RMM_tool()
    {
        Assert.DoesNotContain(Run([Prefetch(0, "NOTEPAD.EXE", @"C:\Windows\notepad.exe")]), x => x.RuleId.StartsWith("REMOTE-TOOL"));
    }

    // ---- D19 inhibit system recovery ----

    private static TimelineEvent Cmd(string cmd) => Sec(4688, 0, ("NewProcessName", @"C:\Windows\System32\cmd.exe"), ("CommandLine", cmd), ("SubjectUserName", "bob"));

    [Theory]
    [InlineData("wbadmin delete catalog -quiet")]
    [InlineData("wbadmin.exe DELETE SYSTEMSTATEBACKUP -keepVersions:0")]
    [InlineData("bcdedit /set {default} recoveryenabled no")]
    [InlineData("bcdedit /set {default} bootstatuspolicy ignoreallfailures")]
    [InlineData("wmic shadowcopy delete")]
    [InlineData("vssadmin resize shadowstorage /for=c: /on=c: /maxsize=401MB")]
    public void Recovery_inhibiting_commands_are_VSS_SNAPSHOT_DELETED(string cmd)
    {
        var f = One(Run([Cmd(cmd)]), "VSS-SNAPSHOT-DELETED");
        Assert.Equal("T1490", f.MitreTechniqueId);
    }

    [Theory]
    [InlineData("wbadmin get versions")]
    [InlineData("bcdedit /set {default} recoveryenabled yes")]
    [InlineData("bcdedit /enum")]
    public void Harmless_backup_and_boot_commands_are_not_findings(string cmd)
    {
        Assert.DoesNotContain(Run([Cmd(cmd)]), x => x.RuleId == "VSS-SNAPSHOT-DELETED");
    }

    // ---- D21 shipped Sigma rules no longer contradict LOG-TAMPER ----

    [Fact]
    public void Shipped_sigma_rules_do_not_flag_log_clears()
    {
        var sigma = SigmaLite.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "Rules", "sigma"));
        Assert.Equal(2, sigma.Rules.Count);
        Assert.DoesNotContain(sigma.Rules, r => r.Title.Contains("șters", StringComparison.OrdinalIgnoreCase) || r.Title.Contains("golit", StringComparison.OrdinalIgnoreCase));
    }

    // ---- D23 scheduled tasks ----

    [Fact]
    public void Task_created_with_a_script_in_a_user_folder_is_PERSIST_TASK_through_106_and_200()
    {
        var found = Corr(Ev(TaskSched, 106, 0, "", ("TaskName", @"\Updater"), ("UserContext", "WS\\bob")),
                         Ev(TaskSched, 200, 1, "", ("TaskName", @"\Updater"), ("ActionName", @"C:\Users\bob\AppData\Roaming\run.js")));
        var f = One(found, "PERSIST-TASK");
        Assert.Contains("Updater", f.Title);
        Assert.Equal(2, f.SupportingEvidence.Count);
    }

    [Fact]
    public void Task_4698_with_a_script_extension_is_PERSIST_TASK()
    {
        var xml = @"<Task><Actions><Exec><Command>C:\ProgramData\x\sync.vbs</Command></Exec></Actions></Task>";
        Assert.Contains(Corr(Sec(4698, 0, ("TaskName", @"\Sync"), ("TaskContent", xml), ("SubjectUserName", "bob"))), x => x.RuleId == "PERSIST-TASK");
    }

    [Fact]
    public void A_json_file_is_not_a_script_and_a_registration_without_action_is_not_flagged()
    {
        var xml = @"<Task><Actions><Exec><Command>C:\Program Files\App\app.exe</Command><Arguments>--config C:\Program Files\App\settings.json</Arguments></Exec></Actions></Task>";
        Assert.DoesNotContain(Corr(Sec(4698, 0, ("TaskName", @"\App"), ("TaskContent", xml), ("SubjectUserName", "bob"))), x => x.RuleId == "PERSIST-TASK");
        Assert.DoesNotContain(Corr(Ev(TaskSched, 106, 0, "", ("TaskName", @"\Plain"), ("UserContext", "WS\\bob"))), x => x.RuleId == "PERSIST-TASK");
        Assert.False(Correlation.HasScriptPath(@"C:\x\settings.json"));
        Assert.True(Correlation.HasScriptPath(@"C:\x\run.js"));
    }
}
