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

    [Fact]
    public void Brute_force_without_a_network_address_says_so_instead_of_from_dash()
    {
        var f = One(Corr(Enumerable.Range(0, 10).Select(i => Fail(i, "alice", "-"))), "CRED-BRUTEFORCE");
        Assert.DoesNotContain("de la -", f.Title);
        Assert.Contains("fără adresă de rețea", f.Title);
        Assert.Contains("de la 203.0.113.9", One(Corr(Enumerable.Range(0, 10).Select(i => Fail(i, "alice"))), "CRED-BRUTEFORCE").Title);
    }

    [Fact]
    public void Wrong_passwords_refused_by_the_domain_controller_are_brute_force_too()
    {
        // lab DC01 2026-11-07: 14 wrong passwords for a domain account left 14 × 4771 on the domain controller and nothing on the target server
        static TimelineEvent Kerb(double m, string user, string status = "0x18") =>
            Sec(4771, m, ("TargetUserName", user), ("IpAddress", "::ffff:10.10.10.101"), ("Status", status));
        var f = One(Corr(Enumerable.Range(0, 14).Select(i => Kerb(i * 0.1, "ana.pop"))), "CRED-BRUTEFORCE");
        Assert.Equal("10.10.10.101", f.Ip);
        Assert.Equal("ana.pop", f.User);
        Assert.Equal(14, f.SupportingEvidence.Count(e => e.Description == "4771"));
        Assert.Contains("controlerul de domeniu a refuzat parola", f.PlainWhat);
        // a computer account out of sync with the domain is not someone guessing (review 2026-11-07)
        Assert.DoesNotContain(Corr(Enumerable.Range(0, 14).Select(i => Kerb(i * 0.1, "LAB-WS01$"))), x => x.RuleId == "CRED-BRUTEFORCE");
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("parolă veche rămasă salvată"));
        // an expired or revoked account (other Kerberos statuses) is not password guessing
        Assert.DoesNotContain(Corr(Enumerable.Range(0, 14).Select(i => Kerb(i * 0.1, "ana.pop", "0x12"))), x => x.RuleId == "CRED-BRUTEFORCE");
        // NTLM: the same attempt in the controller's log (4776) and in the server's (4625) is counted once
        var both = Enumerable.Range(0, 10).SelectMany(i => new[]
        {
            Sec(4776, i, ("TargetUserName", "ana.pop"), ("Workstation", "LAB-WS01"), ("Status", "0xC000006A")),
            Sec(4625, i + 0.005, ("TargetUserName", "ana.pop"), ("WorkstationName", "LAB-WS01"), ("IpAddress", "-")),
        });
        var ntlm = One(Corr(both), "CRED-BRUTEFORCE");
        Assert.StartsWith("10 autentificări eșuate", ntlm.Title);
        Assert.Contains("de la calculatorul „LAB-WS01”", ntlm.PlainWhat);
        // review round 2: a computer name changed at every attempt (the client declares it) does not split the series below the threshold
        var renamed = One(Corr(Enumerable.Range(0, 10).Select(i => Sec(4776, i, ("TargetUserName", "ana.pop"), ("Workstation", "PC" + i), ("Status", "0xC000006A")))), "CRED-BRUTEFORCE");
        Assert.Contains("cu 10 nume diferite", renamed.PlainWhat);
        Assert.Contains("(controlerul de domeniu nu a înregistrat adresa)", renamed.Title);
        // a successful NTLM validation (status 0x0) is not a failure
        Assert.DoesNotContain(Corr(Enumerable.Range(0, 12).Select(i => Sec(4776, i, ("TargetUserName", "ana.pop"), ("Status", "0x0")))), x => x.RuleId == "CRED-BRUTEFORCE");
    }

    // ---- D13 PowerShell ----

    private static TimelineEvent Block(double minute, string text, string id, params (string K, string V)[] extra) =>
        Ev(PowerShell, 4104, minute, "", [("ScriptBlockText", text), ("ScriptBlockId", id), .. extra]);

    [Fact]
    public void Every_suspicious_script_block_is_its_own_finding_and_an_AMSI_bypass_is_High()
    {
        var found = Corr(Block(0, TestFixtureText.AmsiReflectionBypass, "b1"),
                         Block(1, TestFixtureText.EncodedIexBlock, "b2"));
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

    // ---- DISCOVERY-BURST (lab VM 2026-11-06, scenario S2) ----

    private static TimelineEvent Proc(double minute, string image, string cl, string user = "it.admin") =>
        Sec(4688, minute, ("NewProcessName", image), ("CommandLine", cl), ("SubjectUserName", user), ("SubjectLogonId", "0x1"));

    [Fact]
    public void Three_different_discovery_commands_by_one_account_in_ten_minutes_are_one_discovery_finding()
    {
        var found = Wp11Rules.Run([
            Proc(0, @"C:\Windows\System32\whoami.exe", "whoami /all"),
            Proc(0.1, @"C:\Windows\System32\net.exe", "net group \"Domain Admins\" /domain"),
            Proc(0.1, @"C:\Windows\System32\net1.exe", "C:\\Windows\\system32\\net1 group \"Domain Admins\" /domain"),
            Proc(0.2, @"C:\Windows\System32\nltest.exe", "nltest /dclist:lab.local"),
            Proc(0.3, @"C:\Windows\System32\net.exe", "net view \\\\LAB-SRV01")], [], () => "F-1");
        var f = Assert.Single(found, x => x.RuleId == "DISCOVERY-BURST");
        Assert.Equal("T1087.002", f.MitreTechniqueId);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("nltest", f.Title);
        Assert.Equal(5, f.SupportingEvidence.Count);
    }

    [Fact]
    public void Discovery_needs_three_distinct_commands_in_the_window_and_one_account()
    {
        // the same command repeated, two commands, or three commands spread over an hour are administration
        Assert.DoesNotContain(Wp11Rules.Run([Proc(0, "whoami.exe", "whoami"), Proc(1, "whoami.exe", "whoami /all"), Proc(2, "whoami.exe", "whoami /priv")], [], () => "F-1"), x => x.RuleId == "DISCOVERY-BURST");
        Assert.DoesNotContain(Wp11Rules.Run([Proc(0, "whoami.exe", "whoami"), Proc(1, "nltest.exe", "nltest /dclist:x")], [], () => "F-1"), x => x.RuleId == "DISCOVERY-BURST");
        Assert.DoesNotContain(Wp11Rules.Run([Proc(0, "whoami.exe", "whoami"), Proc(20, "nltest.exe", "nltest /dclist:x"), Proc(40, "systeminfo.exe", "systeminfo")], [], () => "F-1"), x => x.RuleId == "DISCOVERY-BURST");
        Assert.DoesNotContain(Wp11Rules.Run([Proc(0, "whoami.exe", "whoami", "ana"), Proc(1, "nltest.exe", "nltest /dclist:x", "ion"), Proc(2, "systeminfo.exe", "systeminfo", "dan")], [], () => "F-1"), x => x.RuleId == "DISCOVERY-BURST");
        // plain ipconfig / net use are not discovery
        Assert.Equal("", Wp11Rules.DiscoveryKey(@"C:\Windows\System32\ipconfig.exe", "ipconfig"));
        Assert.Equal("", Wp11Rules.DiscoveryKey(@"C:\Windows\System32\net.exe", @"net use \\srv\ipc$"));
        Assert.Equal("net group", Wp11Rules.DiscoveryKey(@"C:\Windows\System32\net1.exe", @"C:\Windows\system32\net1 group x /domain"));
        // review round 1: creating an account is not looking around
        Assert.Equal("", Wp11Rules.DiscoveryKey(@"C:\Windows\System32\net.exe", "net user labbackup$ P@ss /add"));
        Assert.Equal("", Wp11Rules.DiscoveryKey(@"C:\Windows\System32\net.exe", "net localgroup administrators labbackup$ /add"));
    }

    [Fact]
    public void Discovery_is_one_finding_across_4688_and_Sysmon_ignores_the_stations_own_accounts_and_is_capped()
    {
        TimelineEvent Sm(double minute, string image, string cl) => Ev("Microsoft-Windows-Sysmon/Operational", 1, minute, "", ("Image", image), ("CommandLine", cl), ("User", @"LAB\it.admin"), ("LogonId", "0x1"));
        var both = Wp11Rules.Run([
            Proc(0, "whoami.exe", "whoami /all"), Proc(0.1, "nltest.exe", "nltest /dclist:x"), Proc(0.2, "systeminfo.exe", "systeminfo"),
            Sm(0, "whoami.exe", "whoami /all"), Sm(0.1, "nltest.exe", "nltest /dclist:x"), Sm(0.2, "systeminfo.exe", "systeminfo")], [], () => "F-1");
        Assert.Single(both, x => x.RuleId == "DISCOVERY-BURST");
        // an inventory agent running as SYSTEM every hour is Low (review round 2: SYSTEM is not a blind spot) and a person who appears among it
        // on day 2 is listed Medium, never buried in the summary
        var system = Enumerable.Range(0, 48).SelectMany(h => new[] { Proc(h * 60, "whoami.exe", "whoami", "SYSTEM"), Proc(h * 60 + 0.1, "ipconfig.exe", "ipconfig /all", "SYSTEM"), Proc(h * 60 + 0.2, "systeminfo.exe", "systeminfo", "LAB-WS01$") }).ToList();
        system.AddRange([Proc(40 * 60 + 30, "whoami.exe", "whoami /all", "intrus"), Proc(40 * 60 + 31, "nltest.exe", "nltest /dclist:x", "intrus"), Proc(40 * 60 + 32, "net.exe", "net group x /domain", "intrus")]);
        int k = 0;
        var inv = Wp11Rules.Run(system, [], () => "F-" + ++k).Where(x => x.RuleId == "DISCOVERY-BURST").ToList();
        Assert.Single(inv, x => x.User == "intrus" && x.Severity == Severity.Medium);
        Assert.All(inv.Where(x => x.User.StartsWith(Wp11Rules.StationActorPrefix, StringComparison.Ordinal)), x => Assert.Equal(Severity.Low, x.Severity));
        Assert.Contains(inv, x => x.Title.StartsWith("Alte ", StringComparison.Ordinal));
        // a person doing it every hour for two days: 20 listed, the rest in one Low summary
        int n = 0;
        var person = Enumerable.Range(0, 48).SelectMany(h => new[] { Proc(h * 60, "whoami.exe", "whoami"), Proc(h * 60 + 0.1, "ipconfig.exe", "ipconfig /all"), Proc(h * 60 + 0.2, "systeminfo.exe", "systeminfo") }).ToList();
        var capped = Wp11Rules.Run(person, [], () => "F-" + ++n).Where(x => x.RuleId == "DISCOVERY-BURST").ToList();
        Assert.Equal(Wp11Rules.DiscoveryMaxFindings + 1, capped.Count);
        Assert.Single(capped, x => x.Severity == Severity.Low && x.Title.StartsWith("Alte 28 serii", StringComparison.Ordinal));
    }

    private const string CimModule = "#requires -version 3.0\r\ntry { Microsoft.PowerShell.Core\\Set-StrictMode -Off } catch { }\r\n" +
        "$script:MyModule = $MyInvocation.MyCommand.ScriptBlock.Module\r\n$script:ClassName = 'ROOT/Microsoft/Windows/Storage/MSFT_Disk'\r\n" +
        "$script:ObjectModelWrapper = [Microsoft.PowerShell.Cmdletization.Cim.CimCmdletAdapter]\r\n";

    [Fact]
    public void The_module_Windows_generates_for_a_CIM_cmdlet_is_not_suspicious_by_its_Warning_flag_alone()
    {
        // lab VM 2026-11-06: Get-Disk / Get-Volume / Get-NetAdapter gave 87 Medium "suspicious" blocks out of 100 listed; now each module text is one
        // Low finding with its run count, still listed with its evidence (review round 2: never summarised away)
        var cim = Corr(Block(0, CimModule, "cim1", ("_xLevel", "3")), Block(1, CimModule, "cim1a", ("_xLevel", "3")), Block(2, CimModule.Replace("MSFT_Disk", "MSFT_Volume"), "cim1b", ("_xLevel", "3")));
        var rows = cim.Where(x => x.RuleId == "PS-SUSPICIOUS").ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(Severity.Low, r.Severity));
        Assert.Contains(rows, r => r.Title.EndsWith("ROOT/Microsoft/Windows/Storage/MSFT_Disk", StringComparison.Ordinal) && r.Description.Contains("de 2 ori"));
        // the backslash form Windows also generates (lab VM: ROOT\Microsoft\Windows\Defender\MSFT_MpRollback) is the same module
        var backslash = One(Corr(Block(0, CimModule.Replace("'ROOT/Microsoft/Windows/Storage/MSFT_Disk'", @"'ROOT\Microsoft\Windows\Defender\MSFT_MpRollback'"), "cim2", ("_xLevel", "3"))), "PS-SUSPICIOUS");
        Assert.Equal(Severity.Low, backslash.Severity);
        Assert.EndsWith(@"ROOT\Microsoft\Windows\Defender\MSFT_MpRollback", backslash.Title);
        // and the lower-case form Windows Server generates (lab SRV01: root/StandardCimv2/MSFT_NetAdapterRssSettingData), 41 of 168 modules there
        var lower = One(Corr(Block(0, CimModule.Replace("'ROOT/Microsoft/Windows/Storage/MSFT_Disk'", "'root/StandardCimv2/MSFT_NetAdapterRssSettingData'"), "cim3", ("_xLevel", "3"))), "PS-SUSPICIOUS");
        Assert.Equal(Severity.Low, lower.Severity);
        Assert.EndsWith("root/StandardCimv2/MSFT_NetAdapterRssSettingData", lower.Title);
        // over the cap of 100 the CIM modules go into the summary, never a Warning block that is not one, even when it ran last (lab SRV01: 98 CIM rows)
        var many = Enumerable.Range(0, 105).Select(i => Block(i, CimModule.Replace("MSFT_Disk'", $"MSFT_Disk{i}'"), $"cimN{i}", ("_xLevel", "3")))
            .Append(Block(200, "auditpol /set /subcategory:\"Process Creation\" /success:enable", "other", ("_xLevel", "3"))).ToList();
        var listed = Corr(many.ToArray()).Where(x => x.RuleId == "PS-SUSPICIOUS").ToList();
        Assert.Contains(listed, x => x.Title == "Bloc PowerShell marcat de Windows ca suspect (nivel Avertisment)");
        Assert.Contains(listed, x => x.Title.StartsWith("Alte 6 scripturi", StringComparison.Ordinal));
        // review round 2: a payload without any listed API behind the pasted prologue is listed on its own (Low), with its own evidence
        var sneaky = CimModule + "$p=[Runtime.InteropServices.Marshal]::AllocHGlobal(4096); $d=[scriptblock]::Create($s); $d.Invoke()";
        var own = Assert.Single(Corr(Block(0, sneaky, "sneaky", ("_xLevel", "3"))), x => x.RuleId == "PS-SUSPICIOUS");
        Assert.Contains(own.SupportingEvidence, r => r.Locator.Length > 0);
        // review round 1: the prologue pasted in front of a native-code loader (no listed keyword) is NOT the generated module
        var loader = CimModule + "Add-Type -TypeDefinition '[DllImport(\"kernel32\")] public static extern IntPtr CreateThread(...)'";
        Assert.Contains(Corr(Block(0, loader, "evil", ("_xLevel", "3"))), x => x.RuleId == "PS-SUSPICIOUS" && x.Severity >= Severity.Medium);
        // nor are markers that only appear further down (in a comment) instead of at the head
        var buried = "#requires -version 3.0\r\n" + new string('#', 700) + "\r\n# try { Microsoft.PowerShell.Core\\Set-StrictMode -Off } catch { } $script:ClassName = 'ROOT/x' Microsoft.PowerShell.Cmdletization";
        Assert.Contains(Corr(Block(0, buried, "evil2", ("_xLevel", "3"))), x => x.RuleId == "PS-SUSPICIOUS" && x.Severity >= Severity.Medium);
        // a keyword in the same text still counts; the prologue alone without the cmdletization adapter is not the generated module
        Assert.Contains(Corr(Block(0, CimModule + TestFixtureText.EncodedIexBlock, "cim2", ("_xLevel", "3"))), x => x.RuleId == "PS-SUSPICIOUS");
        Assert.Contains(Corr(Block(0, "#requires -version 3.0\r\ntry { Microsoft.PowerShell.Core\\Set-StrictMode -Off } catch { }\r\n$script:ClassName = 'ROOT/x'\r\nGet-Date", "cim3", ("_xLevel", "3"))), x => x.RuleId == "PS-SUSPICIOUS");
    }

    [Fact]
    public void The_same_script_text_run_many_times_is_one_finding_with_the_count()
    {
        var runs = Enumerable.Range(0, 7).Select(i => Block(i * 10, "function PSCopyFromSessionHelper { param() }", "copy" + i, ("_xLevel", "3"))).ToArray();
        var f = One(Corr(runs), "PS-SUSPICIOUS");
        Assert.Contains("de 7 ori", f.Description);
        Assert.Equal(7, f.SupportingEvidence.Count);
        Assert.True(f.LastSeenUtc > f.FirstSeenUtc);
        // review round 1: the same text run by ANOTHER account is its own finding; with many runs the last one is still in the evidence
        var other = Block(500, "function PSCopyFromSessionHelper { param() }", "copyX", ("_xLevel", "3"));
        var mixed = Corr([.. runs, new TimelineEvent { Time = other.Time, Source = other.Source, EventId = other.EventId, EvidenceId = other.EvidenceId, Locator = other.Locator, Fields = other.Fields, Summary = other.Summary, User = "intrus" }]);
        Assert.Equal(2, mixed.Count(x => x.RuleId == "PS-SUSPICIOUS"));
        Assert.Contains(mixed, x => x.RuleId == "PS-SUSPICIOUS" && x.User == "intrus");
        var many = Enumerable.Range(0, 30).Select(i => Block(i * 10, "function H { }", "h" + i, ("_xLevel", "3"))).ToArray();
        var g = One(Corr(many), "PS-SUSPICIOUS");
        Assert.Contains(g.SupportingEvidence, r => r.Locator == many[^1].Locator);
        Assert.Equal(many[^1].Time.Utc, g.LastSeenUtc);
    }

    [Fact]
    public void An_ASR_only_exclusion_is_still_a_Defender_exclusion()
    {
        // review round 1: "\Exclusions\" alone missed the ASR exclusion keys the old "exclusions" caught
        Assert.Contains(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\ASROnlyExclusions\C:\Tools = 0x0"))), x => x.RuleId == "DEF-TAMPER");
        Assert.Contains(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\ASROnlyExclusions\C:\Tools = 0x0"))), x => x.RuleId == "DEF-TAMPER");
        Assert.DoesNotContain(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Features\TPExclusions = 0x0"))), x => x.RuleId == "DEF-TAMPER");
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
        // a name-only row is invalid in the profile (it needs a publisher, path or hash), and the row name must contain one of the tool's aliases
        var approved = ProfileWithSoftware((System.IO.Path.GetFileNameWithoutExtension(exe), "Vendor", "", ""));
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

    // ---- Review follow-up (items 5-10) ----

    private static TimelineEvent OnHost(string host, TimelineEvent e) => new()
    {
        Time = e.Time, Source = e.Source, EvidenceId = e.EvidenceId, EventId = e.EventId, Provider = e.Provider, Host = host, Locator = e.Locator, Summary = e.Summary, Fields = e.Fields,
    };

    [Fact]
    public void A_task_start_alone_is_not_a_creation_and_same_named_tasks_on_two_hosts_are_not_merged()
    {
        var start = Ev(TaskSched, 200, 5, "", ("TaskName", @"\OneDrive Update"), ("ActionName", @"C:\Users\bob\AppData\Local\Microsoft\OneDrive\OneDriveStandaloneUpdater.exe"));
        Assert.DoesNotContain(Corr(start), x => x.RuleId == "PERSIST-TASK");
        var started129 = Ev(TaskSched, 129, 5, "", ("TaskName", @"\Updater"), ("Path", @"C:\Users\bob\AppData\Roaming\run.js"));
        Assert.DoesNotContain(Corr(started129), x => x.RuleId == "PERSIST-TASK");

        var created = OnHost("PC-A", Ev(TaskSched, 106, 0, "", ("TaskName", @"\Updater"), ("UserContext", @"PC-A\bob")));
        var otherHostRan = OnHost("PC-B", Ev(TaskSched, 200, 1, "", ("TaskName", @"\Updater"), ("ActionName", @"C:\Users\bob\AppData\Roaming\run.js")));
        Assert.DoesNotContain(Corr(created, otherHostRan), x => x.RuleId == "PERSIST-TASK");
        var sameHostRan = OnHost("PC-A", Ev(TaskSched, 200, 1, "", ("TaskName", @"\Updater"), ("ActionName", @"C:\Users\bob\AppData\Roaming\run.js")));
        var f = One(Corr(created, sameHostRan), "PERSIST-TASK");
        Assert.Contains("a rulat la", f.Description);
    }

    [Fact]
    public void A_task_created_without_a_known_creator_never_prints_an_empty_name()
    {
        var f = One(Corr(Ev(TaskSched, 106, 0, "", ("TaskName", @"\Updater")), Ev(TaskSched, 129, 1, "", ("TaskName", @"\Updater"), ("Path", @"C:\Users\bob\AppData\Roaming\run.js"))), "PERSIST-TASK");
        Assert.Contains("creatorul nu apare în jurnal", f.Description);
        Assert.DoesNotContain("creat de .", f.Description);
        Assert.DoesNotContain("creat de ;", f.Description);
    }

    [Fact]
    public void A_package_manager_style_download_cradle_is_at_most_Medium_and_one_weak_keyword_is_nothing()
    {
        var choco = Corr(Block(0, TestFixtureText.DownloadCradle, "c1"));
        Assert.True(One(choco, "PS-SUSPICIOUS").Severity <= Severity.Medium);
        var two = Corr(Block(0, TestFixtureText.WebClientProxyIex, "c2"));
        Assert.True(One(two, "PS-SUSPICIOUS").Severity <= Severity.Medium);
        Assert.DoesNotContain(Corr(Block(0, "$w = New-Object Net.WebClient", "c3")), x => x.RuleId == "PS-SUSPICIOUS");
        Assert.Equal(Severity.High, One(Corr(Block(0, TestFixtureText.ReflectionTypeProbe, "c4")), "PS-SUSPICIOUS").Severity);
    }

    [Fact]
    public void A_data_pack_without_the_powershell_section_falls_back_to_the_embedded_section()
    {
        var embedded = ShippedData.ReadEmbedded("rule_lists.json");
        var merged = Wp11Data.WithMissingSections("{ \"sharePipesOfInterest\": [\"x\"] }", embedded);
        var lists = Wp11Data.FromJson(ShippedData.ReadEmbedded("security_agents.json"), ShippedData.ReadEmbedded("remote_access_tools.json"), merged).Lists;
        Assert.NotEmpty(lists.Powershell.Keywords);
        Assert.Equal(["x"], lists.SharePipesOfInterest);        // a section the pack does have is not replaced
        var own = Wp11Data.WithMissingSections("{ \"powershell\": { \"keywords\": [ { \"text\": \"only-this\", \"weight\": 4 } ] } }", embedded);
        Assert.Equal(["only-this"], Wp11Data.FromJson("{}", "{}", own).Lists.Powershell.Keywords.Select(k => k.Text).ToArray());
    }

    [Fact]
    public void Only_protection_disabling_switches_in_a_5007_are_DEF_TAMPER()
    {
        Assert.DoesNotContain(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\DisableLocalAdminMerge = 0x1"))), x => x.RuleId == "DEF-TAMPER");
        Assert.Contains(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\DisableAntiSpyware = 0x1"))), x => x.RuleId == "DEF-TAMPER");
        Assert.Contains(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Real-Time Protection\DisableOnAccessProtection = 0x1"))), x => x.RuleId == "DEF-TAMPER");
    }

    [Fact]
    public void A_Defender_exclusion_is_a_value_under_an_Exclusions_key_not_the_Tamper_Protection_flag()
    {
        // seen on a clean Windows 11 lab VM: Defender writes this itself at startup
        Assert.DoesNotContain(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Features\TPExclusions = 0x0"))), x => x.RuleId == "DEF-TAMPER");
        Assert.Contains(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\C:\Users\Public\x = 0x0"))), x => x.RuleId == "DEF-TAMPER");
        Assert.Contains(Corr(Ev(Defender, 5007, 0, "", ("New Value", @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions\Processes\evil.exe = 0x0"))), x => x.RuleId == "DEF-TAMPER");
    }

    [Fact]
    public void A_generic_executable_name_is_a_remote_tool_only_with_the_vendor_path()
    {
        Assert.DoesNotContain(Run([Prefetch(0, "LEVEL.EXE", @"C:\Users\bob\Downloads\level.exe")]), x => x.RuleId == "REMOTE-TOOL-EXECUTED");
        Assert.DoesNotContain(Run([Prefetch(0, "WINDOWSAGENT.EXE", @"C:\Games\windowsagent.exe")]), x => x.RuleId == "REMOTE-TOOL-EXECUTED");
        Assert.DoesNotContain(Run([Prefetch(0, "REMOTE ACCESS.EXE", @"C:\Tools\remote access.exe")]), x => x.RuleId == "REMOTE-TOOL-EXECUTED");
        Assert.Contains("Level", One(Run([Prefetch(0, "LEVEL.EXE", @"C:\Program Files\Level\level.exe")]), "REMOTE-TOOL-EXECUTED").Title);
        Assert.Contains("N-able", One(Run([Prefetch(0, "WINDOWSAGENT.EXE", @"C:\Program Files (x86)\N-able Technologies\Windows Agent\bin\windowsagent.exe")]), "REMOTE-TOOL-EXECUTED").Title);
    }

    [Fact]
    public void Only_the_exact_built_in_WMI_subscription_is_excluded_not_any_text_that_mentions_it()
    {
        Assert.DoesNotContain(Corr(Ev(Wmi, 5861, 0, "", ("CONSUMER", "NTEventLogEventConsumer.Name=\"SCM Event Log Consumer\""))), x => x.RuleId == "PERSIST-WMI");
        Assert.DoesNotContain(Corr(Sysmon(19, 0, ("Name", "SCM Event Log Filter"))), x => x.RuleId == "PERSIST-WMI");
        Assert.Contains(Corr(Ev(Wmi, 5861, 0, "", ("CONSUMER", "CommandLineEventConsumer.Name=\"Updater\""), ("CommandLineTemplate", "cmd /c echo SCM Event Log & payload.exe"))), x => x.RuleId == "PERSIST-WMI");
        Assert.Contains(Corr(Ev(Wmi, 5861, 0, "", ("CONSUMER", "CommandLineEventConsumer.Name=\"SCM Event Log Consumer\""))), x => x.RuleId == "PERSIST-WMI");
        // the form WMI-Activity 5861 really logs (clean Windows 11 lab VM): Type="name", no .Name
        Assert.DoesNotContain(Corr(Ev(Wmi, 5861, 0, "", ("CONSUMER", "NTEventLogEventConsumer=\"SCM Event Log Consumer\""))), x => x.RuleId == "PERSIST-WMI");
        Assert.Contains(Corr(Ev(Wmi, 5861, 0, "", ("CONSUMER", "CommandLineEventConsumer=\"SCM Event Log Consumer\""))), x => x.RuleId == "PERSIST-WMI");
    }
}
