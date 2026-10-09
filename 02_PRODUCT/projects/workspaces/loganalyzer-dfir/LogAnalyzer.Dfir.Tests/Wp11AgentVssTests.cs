using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

public class Wp11AgentTests
{
    private const string Scm = "Service Control Manager";

    private static TimelineEvent Stopped(double m, string display = "Windows Defender Antivirus Service") => Sys(7036, m, Scm, ("param1", display), ("param2", "stopped"));
    private static TimelineEvent Running(double m, string display = "Windows Defender Antivirus Service") => Sys(7036, m, Scm, ("param1", display), ("param2", "running"));
    private static TimelineEvent StartType(double m, string display, string newType) => Sys(7040, m, Scm, ("param1", display), ("param2", "auto start"), ("param3", newType), ("param4", "WinDefend"));
    private static TimelineEvent Shutdown1074(double m) => Sys(1074, m, "User32", ("param5", "shutdown"));
    private static TimelineEvent Msi(int id, double m, string text) => Ev("Application", id, m, "MsiInstaller", ("Data0", text));

    [Fact]
    public void Known_agent_service_stopped_is_Medium_and_distinct_from_DEF_TAMPER()
    {
        var found = Run([Stopped(0)]);
        var f = One(found, "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("Microsoft Defender Antivirus", f.Title);
        Assert.DoesNotContain(found, x => x.RuleId == "DEF-TAMPER");
        Assert.Contains("nu arată cine", f.Description + string.Join(" ", f.AlternativeExplanations) + string.Join(" ", f.MissingEvidence), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("T1562.001", f.MitreTechniqueId);
    }

    [Fact]
    public void Unknown_services_and_running_states_are_not_findings()
    {
        Assert.DoesNotContain(Run([Stopped(0, "Print Spooler"), Running(1)]), x => x.RuleId == "SECURITY-AGENT-STOPPED");
    }

    [Fact]
    public void Stop_followed_by_a_start_is_Info_with_the_restart_as_contradiction()
    {
        var f = One(Run([Stopped(0), Running(3)]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Info, f.Severity);
        Assert.NotEmpty(f.ContradictingEvidence);
        var late = One(Run([Stopped(0), Running(30)]), "SECURITY-AGENT-STOPPED");   // restart after the window does not excuse it
        Assert.Equal(Severity.Medium, late.Severity);
    }

    [Fact]
    public void Stop_during_a_shutdown_is_Info()
    {
        var f = One(Run([Shutdown1074(-2), Stopped(0)]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("oprire/repornire", string.Join(" ", f.ContradictingEvidence));
        var closing = One(Run([Stopped(0), Sys(6006, 1, "EventLog")]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Info, closing.Severity);
    }

    [Fact]
    public void Start_type_changed_to_disabled_is_Medium_and_other_changes_are_ignored()
    {
        var f = One(Run([StartType(0, "Windows Defender Antivirus Service", "disabled")]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Contains("dezactivat", f.Title + f.Description);
        Assert.DoesNotContain(Run([StartType(0, "Windows Defender Antivirus Service", "auto start")]), x => x.RuleId == "SECURITY-AGENT-STOPPED");
        Assert.Single(Run([StartType(0, "Serviciu Defender", "dezactivat"), StartType(1, "Sysmon", "disabled")]), x => x.RuleId == "SECURITY-AGENT-STOPPED" && x.Title.Contains("Sysmon"));
    }

    [Fact]
    public void Localized_stop_words_come_from_the_data()
    {
        var f = One(Run([Sys(7036, 0, Scm, ("param1", "Windows Defender Antivirus Service"), ("param2", "oprit"))]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void New_service_with_an_agent_name_is_Info_from_a_normal_path_and_Medium_from_a_writable_one()
    {
        var ok = One(Run([Sys(7045, 0, Scm, ("ServiceName", "Sysmon64"), ("ImagePath", @"C:\Windows\Sysmon64.exe"), ("AccountName", "LocalSystem"))]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Info, ok.Severity);
        var bad = One(Run([Sys(7045, 0, Scm, ("ServiceName", "CSFalconService"), ("ImagePath", @"C:\Users\Public\x.exe"), ("AccountName", "LocalSystem"))]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Medium, bad.Severity);
        Assert.Contains("suprapune", bad.Description + bad.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sysmon_state_stopped_is_Medium_started_is_ignored_and_config_change_is_Low()
    {
        Assert.Equal(Severity.Medium, One(Run([Sysmon(4, 0, ("State", "Stopped"))]), "SECURITY-AGENT-STOPPED").Severity);
        Assert.DoesNotContain(Run([Sysmon(4, 0, ("State", "Started"))]), x => x.RuleId == "SECURITY-AGENT-STOPPED");
        var cfg = One(Run([Sysmon(16, 0, ("Configuration", "sysmonconfig.xml"), ("ConfigurationFileHash", "SHA256=AB"))]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Low, cfg.Severity);
    }

    [Fact]
    public void Product_removal_from_MsiInstaller_is_Medium_for_known_products_only()
    {
        var f = One(Run([Msi(1034, 0, "Windows Installer removed the product. Product Name: CrowdStrike Windows Sensor. Product Version: 7.1."), Msi(11724, 0, "Product: CrowdStrike Windows Sensor -- Removal completed successfully.")]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(2, f.SupportingEvidence.Count);
        Assert.DoesNotContain(Run([Msi(1034, 0, "Windows Installer removed the product. Product Name: 7-Zip 23.01.")]), x => x.RuleId == "SECURITY-AGENT-STOPPED");
        Assert.DoesNotContain(Run([Msi(11707, 0, "Product: CrowdStrike Windows Sensor -- Installation completed successfully.")]), x => x.RuleId == "SECURITY-AGENT-STOPPED");
    }

    [Fact]
    public void A_High_finding_nearby_raises_the_stop_to_High_a_distant_one_does_not()
    {
        var near = One(Run([Stopped(0)], existing: [Other("DEF-DETECTION", Severity.High, 20)]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.High, near.Severity);
        Assert.Equal(Classification.Correlated, near.Classification);
        var far = One(Run([Stopped(0)], existing: [Other("DEF-DETECTION", Severity.High, 300)]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Medium, far.Severity);
        var lowNear = One(Run([Stopped(0)], existing: [Other("EXEC-USERPATH", Severity.Low, 5)]), "SECURITY-AGENT-STOPPED");
        Assert.Equal(Severity.Medium, lowNear.Severity);
    }

    [Fact]
    public void The_agent_list_is_data_and_replacing_it_changes_what_is_detected()
    {
        var acme = Wp11Data.FromJson(
            """{"stoppedWords":["stopped"],"runningWords":["running"],"disabledWords":["disabled"],"agents":[{"id":"acme","name":"Acme EDR","kind":"EDR","serviceNames":["AcmeSensor"],"displayContains":["Acme Sensor"],"productContains":["Acme EDR"]}]}""",
            """{"tools":[]}""", """{}""");
        var ev = Sys(7036, 0, Scm, ("param1", "Acme Sensor Service"), ("param2", "stopped"));
        Assert.DoesNotContain(Run([ev]), x => x.RuleId == "SECURITY-AGENT-STOPPED");
        var f = One(Run([ev], data: acme), "SECURITY-AGENT-STOPPED");
        Assert.Contains("Acme EDR", f.Title);
        Assert.DoesNotContain(Run([Stopped(0)], data: acme), x => x.RuleId == "SECURITY-AGENT-STOPPED");   // Defender is not in the replacement list
    }

    [Fact]
    public void Shipped_agent_data_covers_the_named_products()
    {
        var names = Wp11Data.Default.Agents.Agents.Select(a => a.Id).ToList();
        foreach (var id in new[] { "defender", "sysmon", "crowdstrike", "sentinelone", "carbonblack" }) Assert.Contains(id, names);
        Assert.All(Wp11Data.Default.Agents.Agents, a => Assert.True(a.ServiceNames.Count + a.DisplayContains.Count > 0 && a.ProductContains.Count > 0, a.Id));
    }
}

public class Wp11VssTests
{
    private static TimelineEvent Proc4688(double m, string cmd, string exe = @"C:\Windows\System32\vssadmin.exe") =>
        Sec(4688, m, ("NewProcessName", exe), ("CommandLine", cmd), ("SubjectUserName", "bob"), ("SubjectDomainName", "CORP"));

    [Fact]
    public void Vssadmin_delete_shadows_alone_is_Medium_and_does_not_claim_the_deletion_succeeded()
    {
        var f = One(Run([Proc4688(0, "vssadmin.exe delete shadows /all /quiet")]), "VSS-SNAPSHOT-DELETED");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(SemanticType.Execution, f.SemanticType);
        Assert.Contains("nu arată că ștergerea a reușit", f.Description);
        Assert.Equal("T1490", f.MitreTechniqueId);
    }

    [Fact]
    public void Wmic_powershell_and_sysmon_forms_are_found_and_listing_is_not()
    {
        Assert.Equal(Severity.Medium, One(Run([Proc4688(0, "wmic shadowcopy delete", @"C:\Windows\System32\wbem\WMIC.exe")]), "VSS-SNAPSHOT-DELETED").Severity);
        var ps = One(Run([Ev("Microsoft-Windows-PowerShell/Operational", 4104, 0, "Microsoft-Windows-PowerShell", ("ScriptBlockText", "Get-WmiObject Win32_ShadowCopy | ForEach-Object { $_.Delete() }"))]), "VSS-SNAPSHOT-DELETED");
        Assert.Equal(Severity.Medium, ps.Severity);
        Assert.Equal(SemanticType.Observation, ps.SemanticType);      // a script block is not a process-start record
        Assert.Equal(Severity.Medium, One(Run([Sysmon(1, 0, ("Image", @"C:\Windows\System32\vssadmin.exe"), ("CommandLine", "vssadmin Delete Shadows /For=C: /Oldest"))]), "VSS-SNAPSHOT-DELETED").Severity);
        Assert.DoesNotContain(Run([Proc4688(0, "vssadmin list shadows"), Proc4688(1, "wmic logicaldisk get name")]), x => x.RuleId == "VSS-SNAPSHOT-DELETED");
    }

    [Fact]
    public void Resizing_the_shadow_storage_is_only_Low()
    {
        Assert.Equal(Severity.Low, One(Run([Proc4688(0, "vssadmin resize shadowstorage /for=C: /on=C: /maxsize=401MB")]), "VSS-SNAPSHOT-DELETED").Severity);
    }

    [Fact]
    public void Existing_impact_evidence_in_the_case_makes_it_High_other_findings_do_not()
    {
        var cmd = Proc4688(0, "vssadmin delete shadows /all /quiet");
        var viaMitre = One(Run([cmd], existing: [Other("SOMETHING-IMPACT", Severity.High, 30, mitre: "T1486")]), "VSS-SNAPSHOT-DELETED");
        Assert.Equal(Severity.High, viaMitre.Severity);
        Assert.Equal(Classification.Correlated, viaMitre.Classification);
        Assert.NotEmpty(viaMitre.RelatedFindingIds);
        var viaCategory = One(Run([cmd], existing: [Other("SOMETHING-IMPACT", Severity.Medium, -30, category: "Impact")]), "VSS-SNAPSHOT-DELETED");
        Assert.Equal(Severity.High, viaCategory.Severity);
        var viaRansom = One(Run([cmd], existing: [Other("DEF-DETECTION", Severity.High, 10, description: "Defender: Ransom:Win32/Locky.A")]), "VSS-SNAPSHOT-DELETED");
        Assert.Equal(Severity.High, viaRansom.Severity);
        Assert.Equal(Severity.Medium, One(Run([cmd], existing: [Other("DEF-DETECTION", Severity.High, 10, description: "Trojan:Win32/X")]), "VSS-SNAPSHOT-DELETED").Severity);
        Assert.Equal(Severity.Medium, One(Run([cmd], existing: [Other("SOMETHING-IMPACT", Severity.High, 60 * 48, mitre: "T1486")]), "VSS-SNAPSHOT-DELETED").Severity);   // two days away
    }

    [Fact]
    public void Volsnap_alone_is_Info_or_Low_and_VSS_errors_alone_are_not_a_finding()
    {
        var oldest = One(Run([Sys(33, 0, "volsnap")]), "VSS-SNAPSHOT-DELETED");
        Assert.Equal(Severity.Info, oldest.Severity);
        Assert.Contains("limita", oldest.Description);
        var aborted = One(Run([Sys(25, 0, "volsnap")]), "VSS-SNAPSHOT-DELETED");
        Assert.Equal(Severity.Low, aborted.Severity);
        Assert.DoesNotContain(Run([Ev("Application", 8193, 0, "VSS"), Ev("Application", 8194, 1, "VSS")]), x => x.RuleId == "VSS-SNAPSHOT-DELETED");
    }

    [Fact]
    public void System_events_near_a_delete_command_are_attached_to_it_not_separate()
    {
        var found = Run([Proc4688(0, "vssadmin delete shadows /all"), Sys(33, 2, "volsnap"), Ev("Application", 8193, 3, "VSS")]).Where(x => x.RuleId == "VSS-SNAPSHOT-DELETED").ToList();
        var f = Assert.Single(found);
        Assert.Equal(3, f.SupportingEvidence.Count);
        Assert.Contains("volsnap", f.Description);
    }
}
