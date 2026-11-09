using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Attacks on Active Directory and silencing of the EDR: KERBEROAST, ASREP-ROAST, ADCS-SAN, NTLM-RELAY-MACHINE, EDR-WFP-BLOCK.</summary>
public class AdAttackRulesTests
{
    private static List<Finding> Of(string rule, params TimelineEvent[] events) => Run(events).Where(f => f.RuleId == rule).ToList();
    private static List<Finding> Of(string rule, IEnumerable<TimelineEvent> events) => Run(events).Where(f => f.RuleId == rule).ToList();

    // ---- KERBEROAST ----

    private static TimelineEvent Tgs(double minute, string user, string svc, string enc = "0x17", string status = "0x0", string ip = "::ffff:10.0.0.5") =>
        Sec(4769, minute, ("TargetUserName", user), ("ServiceName", svc), ("TicketEncryptionType", enc), ("Status", status), ("IpAddress", ip));

    [Fact]
    public void Three_distinct_services_with_RC4_tickets_in_ten_minutes_are_one_KERBEROAST_Medium_and_five_are_High()
    {
        var f = Assert.Single(Of("KERBEROAST", Tgs(0, "Ana@CORP.LOCAL", "svc_sql"), Tgs(1, "ana@corp.local", "svc_web"), Tgs(2, "ANA@CORP", "SVC_FILE", "0X18")));
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Severity.Medium, Assert.Single(Of("KERBEROAST", Tgs(0, "ana", "a"), Tgs(1, "ana", "b"), Tgs(2, "ana", "c"), Tgs(3, "ana", "d"))).Severity);
        var five = Assert.Single(Of("KERBEROAST", Enumerable.Range(0, 5).Select(i => Tgs(i, "ana", "svc" + i))));
        Assert.Equal(Severity.High, five.Severity);
        Assert.Contains("5 servicii", five.Title);
        Assert.Equal("T1558.003", f.MitreTechniqueId);
        Assert.Equal("ana", f.User);
        Assert.Contains("svc_sql", f.Description);
        Assert.Contains("10.0.0.5", f.Description);
        Assert.Equal(3, f.SupportingEvidence.Count);
        Assert.NotEmpty(f.AlternativeExplanations); Assert.NotEmpty(f.MissingEvidence); Assert.NotEmpty(f.RecommendedNextSteps);
    }

    [Fact]
    public void Decimal_encryption_types_23_and_24_count_as_RC4()
    {
        Assert.Single(Of("KERBEROAST", Tgs(0, "ana", "a", "23"), Tgs(1, "ana", "b", "24"), Tgs(2, "ana", "c", "0x17")));
    }

    [Fact]
    public void A_single_ticket_the_same_service_many_times_or_two_services_are_not_findings()
    {
        Assert.Empty(Of("KERBEROAST", Tgs(0, "ana", "svc_a")));
        Assert.Empty(Of("KERBEROAST", Enumerable.Range(0, 20).Select(i => Tgs(i * 0.1, "ana", "SVC_A"))));
        Assert.Empty(Of("KERBEROAST", Tgs(0, "ana", "a"), Tgs(1, "ana", "b")));
    }

    [Fact]
    public void AES_failed_machine_and_krbtgt_tickets_are_excluded()
    {
        Assert.Empty(Of("KERBEROAST", Tgs(0, "ana", "a", "0x12"), Tgs(1, "ana", "b", "0x11"), Tgs(2, "ana", "c", "0x12")));
        Assert.Empty(Of("KERBEROAST", Tgs(0, "ana", "a", status: "0x1B"), Tgs(1, "ana", "b", status: "0x1B"), Tgs(2, "ana", "c", status: "0x1B")));
        Assert.Empty(Of("KERBEROAST", Tgs(0, "ana", "pc1$"), Tgs(1, "ana", "pc2$"), Tgs(2, "ana", "pc3$"), Tgs(3, "ana", "krbtgt"), Tgs(4, "ana", "KRBTGT")));
        Assert.Empty(Of("KERBEROAST", Tgs(0, "pc1$@CORP", "a"), Tgs(1, "pc1$@CORP", "b"), Tgs(2, "pc1$@CORP", "c")));
        // two real services plus excluded ones do not add up to three
        Assert.Empty(Of("KERBEROAST", Tgs(0, "ana", "a"), Tgs(1, "ana", "b"), Tgs(2, "ana", "pc$"), Tgs(3, "ana", "krbtgt")));
    }

    [Fact]
    public void The_ten_minute_window_is_inclusive_and_one_second_more_is_out()
    {
        Assert.Single(Of("KERBEROAST", Tgs(0, "ana", "a"), Tgs(5, "ana", "b"), Tgs(10, "ana", "c")));
        Assert.Empty(Of("KERBEROAST", Tgs(0, "ana", "a"), Tgs(5, "ana", "b"), Tgs(10 + 1.0 / 60, "ana", "c")));
    }

    [Fact]
    public void Services_are_counted_per_account_and_missing_fields_or_time_never_crash()
    {
        Assert.Empty(Of("KERBEROAST", Tgs(0, "ana", "a"), Tgs(1, "ion", "b"), Tgs(2, "dan", "c")));
        Assert.Empty(Of("KERBEROAST", Sec(4769, 0), Sec(4769, 1, ("TargetUserName", "ana")), Sec(4769, 2, ("TargetUserName", "ana"), ("ServiceName", "a")),
            Sec(4769, 3, ("TargetUserName", "ana"), ("ServiceName", "b"), ("TicketEncryptionType", ""), ("Status", "0x0")), Tgs(4, "", "c"), Tgs(5, "ana", "")));
        var noTime = Tgs(1, "ana", "b"); var noTime2 = Tgs(2, "ana", "c");
        var untimed = new[] { Tgs(0, "ana", "a"), Untimed(noTime), Untimed(noTime2) };
        Assert.Empty(Of("KERBEROAST", untimed));
    }

    private static TimelineEvent Untimed(TimelineEvent e) => new()
    {
        Time = Timestamp.Unknown(), Source = e.Source, EventId = e.EventId, Provider = e.Provider, EvidenceId = e.EvidenceId, Locator = e.Locator, Summary = e.Summary, Fields = e.Fields,
    };

    [Fact]
    public void Many_accounts_are_listed_up_to_the_cap_and_one_Low_summary_holds_the_rest()
    {
        var events = Enumerable.Range(0, 30).SelectMany(u => Enumerable.Range(0, 5).Select(k => Tgs(u + k * 0.01, $"u{u:D2}", "s" + k))).ToList();
        var found = Of("KERBEROAST", events);
        Assert.Equal(Wp11Rules.AdMaxFindings + 1, found.Count);
        var summary = Assert.Single(found, x => x.Severity == Severity.Low);
        Assert.StartsWith("Neafișate individual: 10 serii", summary.Title);
        Assert.Equal(Wp11Rules.AdMaxFindings, found.Count(x => x.Severity == Severity.High));
    }

    // ---- ASREP-ROAST ----

    private static TimelineEvent Tgt(double minute, string user, string preAuth = "0", string enc = "0x12", string status = "0x0") =>
        Sec(4768, minute, ("TargetUserName", user), ("PreAuthType", preAuth), ("TicketEncryptionType", enc), ("Status", status), ("IpAddress", "10.0.0.9"));

    [Fact]
    public void A_TGT_without_pre_authentication_is_one_Medium_finding_per_account_and_High_with_RC4()
    {
        var med = Assert.Single(Of("ASREP-ROAST", Tgt(0, "svc_old"), Tgt(5, "SVC_OLD@CORP"), Tgt(9, "svc_old")));
        Assert.Equal(Severity.Medium, med.Severity);
        Assert.Equal("T1558.004", med.MitreTechniqueId);
        Assert.Contains("nu cere pre-autentificare Kerberos și i s-a dat un tichet", med.PlainWhat);
        Assert.Equal(3, med.SupportingEvidence.Count);
        var high = Assert.Single(Of("ASREP-ROAST", Tgt(0, "x", enc: "0x17")));
        Assert.Equal(Severity.High, high.Severity);
        Assert.Single(Of("ASREP-ROAST", Tgt(0, "x", enc: "23")), f => f.Severity == Severity.High);
        Assert.Equal(2, Of("ASREP-ROAST", Tgt(0, "a"), Tgt(1, "b")).Count);
    }

    [Fact]
    public void ASREP_evidence_is_capped_at_fifteen_and_pre_authenticated_failed_or_empty_events_are_ignored()
    {
        var f = Assert.Single(Of("ASREP-ROAST", Enumerable.Range(0, 40).Select(i => Tgt(i, "svc_old"))));
        Assert.Equal(15, f.SupportingEvidence.Count);
        Assert.Empty(Of("ASREP-ROAST", Tgt(0, "a", "2"), Tgt(1, "b", "15"), Tgt(2, "c", ""), Tgt(3, "d", status: "0x18"), Tgt(4, ""), Tgt(5, "pc$"), Sec(4768, 6)));
    }

    // ---- ADCS-SAN ----

    private static TimelineEvent Ca(int id, double minute, string attrs, string reqId = "77", string requester = @"CORP\bob") =>
        Sec(id, minute, ("Attributes", attrs), ("RequestId", reqId), ("Requester", requester));

    [Fact]
    public void An_issued_certificate_with_a_SAN_in_the_request_is_High_and_a_request_alone_is_Medium()
    {
        var issued = Assert.Single(Of("ADCS-SAN", Ca(4886, 0, "CertificateTemplate:User\nSAN:upn=administrator@corp"), Ca(4887, 0.1, "CertificateTemplate:User\nsan:upn=administrator@corp")));
        Assert.Equal(Severity.High, issued.Severity);
        Assert.Equal("T1649", issued.MitreTechniqueId);
        Assert.Contains("upn=administrator@corp", issued.Description);
        Assert.Contains(@"CORP\bob", issued.Description);
        Assert.Contains("77", issued.Description);
        var req = Assert.Single(Of("ADCS-SAN", Ca(4886, 0, "SAN:dns=dc01.corp")));
        Assert.Equal(Severity.Medium, req.Severity);
        Assert.Contains(req.MissingEvidence, m => m.Contains("ESC1") && m.Contains("NU apare"));
        Assert.Contains("ESC6", issued.Description);
    }

    [Fact]
    public void Requests_without_a_SAN_or_with_empty_attributes_are_not_findings_and_different_requests_are_separate()
    {
        Assert.Empty(Of("ADCS-SAN", Ca(4887, 0, "CertificateTemplate:User"), Ca(4886, 1, ""), Sec(4887, 2), Sec(4886, 3, ("RequestId", "1"))));
        Assert.Equal(2, Of("ADCS-SAN", Ca(4887, 0, "SAN:upn=a@c", "1"), Ca(4887, 1, "SAN:upn=b@c", "2")).Count);
        // the SAN must start an attribute line: a name that merely contains "san:" is not one
        Assert.Empty(Of("ADCS-SAN", Ca(4887, 0, "CertificateTemplate:Mysan:abc"), Ca(4886, 1, "Comment: unsan:x\nCertificateTemplate:User")));
        Assert.Single(Of("ADCS-SAN", Ca(4887, 0, "CertificateTemplate:User\r\n  SAN:dns=a.corp")));
    }

    // ---- NTLM-RELAY-MACHINE ----

    private static int _loc = 900_000;
    private static TimelineEvent Logon(double minute, string target, string ip, string pkg = "NTLM", string type = "3", string host = "srv1", string ws = "VICTIM") =>
        new()
        {
            Time = Timestamp.FromUtc(T0.AddMinutes(minute), "", "test"), Source = "EventLog:Security", EventId = "4624", Provider = "Microsoft-Windows-Security-Auditing", Host = host,
            EvidenceId = "EV-1", Locator = "EventRecordID=" + Interlocked.Increment(ref _loc), Summary = "t",
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["TargetUserName"] = target, ["WorkstationName"] = ws, ["AuthenticationPackageName"] = pkg, ["LogonType"] = type, ["IpAddress"] = ip },
        };

    private static TimelineEvent Krb(double minute, string target, string ip, string host = "srv1") => Logon(minute, target, ip, "Kerberos", host: host);

    [Fact]
    public void A_machine_account_by_NTLM_from_an_address_it_never_used_with_Kerberos_is_one_Medium_finding_per_account_host_and_ip()
    {
        var found = Of("NTLM-RELAY-MACHINE", Krb(0, "DC02$", "10.0.0.20"), Logon(1, "DC02$", "10.0.0.66", ws: "DC02"), Logon(2, "dc02$", "10.0.0.66"), Logon(3, "DC02$", "10.0.0.66"), Logon(4, "DC02$", "10.0.0.67"));
        Assert.Equal(2, found.Count);
        var g = Assert.Single(found, f => f.Title.Contains("10.0.0.66"));
        Assert.Equal(Severity.Medium, g.Severity);
        Assert.Equal(Classification.Correlated, g.Classification);
        Assert.Equal("T1557.001", g.MitreTechniqueId);
        Assert.Contains("3 evenimente", g.Description);
        Assert.Contains("10.0.0.20", g.Description);
        Assert.Contains("nu s-a mai autentificat (Kerberos)", g.Title);
        Assert.DoesNotContain("retransmis", g.Title);
        Assert.Contains(g.AlternativeExplanations, a => a.Contains("DHCP"));
        Assert.Contains(g.AlternativeExplanations, a => a.Contains("gMSA"));
        Assert.Contains(g.MissingEvidence, m => m.Contains("DHCP"));
    }

    [Fact]
    public void NTLM_from_a_known_Kerberos_address_is_not_a_finding_and_there_is_no_finding_without_a_baseline()
    {
        // gMSA-style / machine account used from its own known address, even with a workstation name that differs from the account
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Krb(0, "GMSA1$", "10.0.0.30"), Logon(1, "GMSA1$", "10.0.0.30", ws: "OTHERHOST"), Logon(2, "gmsa1$", "10.0.0.30")));
        // no Kerberos baseline at all
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Logon(0, "PC1$", "10.0.0.66"), Logon(1, "PC1$", "10.0.0.67")));
        // the baseline of another account does not count
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Krb(0, "PC2$", "10.0.0.20"), Logon(1, "PC1$", "10.0.0.66")));
        // a Kerberos sign-in of another logon type is not a baseline
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Logon(0, "PC1$", "10.0.0.20", "Kerberos", "2"), Logon(1, "PC1$", "10.0.0.66")));
    }

    [Fact]
    public void Addresses_are_normalised_and_loopback_empty_users_anonymous_and_other_types_are_ignored()
    {
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Krb(0, "PC1$", "10.0.0.20"), Logon(1, "PC1$", "::ffff:10.0.0.20"), Logon(2, "PC1$", "::FFFF:10.0.0.20")));
        Assert.Single(Of("NTLM-RELAY-MACHINE", Krb(0, "PC1$", "::ffff:10.0.0.20"), Logon(1, "PC1$", "10.0.0.66")));
        var f = Assert.Single(Of("NTLM-RELAY-MACHINE", Krb(0, "PC1$", "10.0.0.20"), Logon(1, "PC1$", "::ffff:10.0.0.66")));
        Assert.Contains("10.0.0.66", f.Title);
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Krb(0, "PC1$", "10.0.0.20"),
            Logon(1, "PC1$", "::1"), Logon(2, "PC1$", "127.0.0.1"), Logon(3, "PC1$", "::ffff:127.0.0.1"), Logon(4, "PC1$", ""), Logon(5, "PC1$", "-"),
            Logon(6, "ana", "10.0.0.66"), Logon(7, "ANONYMOUS LOGON", "10.0.0.66"), Logon(8, "", "10.0.0.66"), Logon(9, "PC1$", "10.0.0.66", type: "2"),
            Logon(10, "PC1$", "10.0.0.66", "Negotiate"), Sec(4624, 11)));
        // a baseline of loopback / empty addresses is no baseline
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Krb(0, "PC1$", "::1"), Krb(1, "PC1$", "-"), Logon(2, "PC1$", "10.0.0.66")));
    }

    [Fact]
    public void Only_kerberos_addresses_seen_before_the_ntlm_logon_count_and_ipv6_forms_are_canonical()
    {
        // review round 2: relay to the CA web enrolment, then a PKINIT ticket gives the attacker's address a LATER Kerberos sign-in; it must not hide the relay
        var f = Assert.Single(Of("NTLM-RELAY-MACHINE", Krb(0, "DC01$", "10.0.0.10"), Logon(5, "DC01$", "10.0.0.66"), Krb(9, "DC01$", "10.0.0.66")));
        Assert.Contains("10.0.0.66", f.Title);
        Assert.DoesNotContain("10.0.0.66", f.Description.Split("Kerberos doar de la")[1].Split('.')[0]);
        // the baseline must exist before the NTLM sign-in
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Logon(0, "PC1$", "10.0.0.66"), Krb(5, "PC1$", "10.0.0.20")));
        // the same IPv6 address written in two forms is one address
        Assert.Empty(Of("NTLM-RELAY-MACHINE", Krb(0, "PC1$", "fe80:0:0:0:0:0:0:1"), Logon(1, "PC1$", "FE80::1")));
    }

    [Fact]
    public void Relay_groups_are_split_by_host_and_capped_with_a_summary()
    {
        Assert.Equal(2, Of("NTLM-RELAY-MACHINE", Krb(0, "DC02$", "10.0.0.20"), Logon(1, "DC02$", "10.0.0.66", host: "srv1"), Logon(2, "DC02$", "10.0.0.66", host: "srv2")).Count);
        var many = Enumerable.Range(0, 25).SelectMany(i => new[] { Krb(i, $"PC{i:D2}$", "10.0.0.20"), Logon(i + 0.5, $"PC{i:D2}$", "10.0.0.66") });
        var found = Of("NTLM-RELAY-MACHINE", many);
        Assert.Equal(Wp11Rules.AdMaxFindings + 1, found.Count);
        var sum = Assert.Single(found, f => f.Severity == Severity.Low);
        Assert.StartsWith("Neafișate individual: 5 autentificări", sum.Title);
    }

    // ---- EDR-WFP-BLOCK ----

    private static TimelineEvent Wfp(int id, double minute, string name) => Sec(id, minute, ("FilterName", name), ("FilterId", "1234"));
    private static TimelineEvent Block(double minute, string app) =>
        Sec(5157, minute, ("Application", app), ("DestAddress", "52.1.1.1"), ("Direction", "%%14593"));

    [Fact]
    public void A_Custom_Outbound_Filter_is_High_and_says_when_the_5157_audit_is_missing()
    {
        var f = Assert.Single(Of("EDR-WFP-BLOCK", Wfp(5441, 0, "Custom Outbound Filter"), Wfp(5447, 1, "custom outbound filter"), Wfp(5441, 2, "Custom Outbound Filter")));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal("T1562.001", f.MitreTechniqueId);
        Assert.Contains(f.MissingEvidence, m => m.Contains("5157"));
        var with5157 = Assert.Single(Of("EDR-WFP-BLOCK", Wfp(5441, 0, "Custom Outbound Filter"), Block(0, @"\device\harddiskvolume3\x\other.exe")));
        Assert.DoesNotContain(with5157.MissingEvidence, m => m.Contains("lipsește"));
    }

    [Fact]
    public void An_ordinary_filter_name_or_a_missing_one_is_not_a_finding()
    {
        Assert.Empty(Of("EDR-WFP-BLOCK", Wfp(5441, 0, "Allow DNS"), Wfp(5447, 1, "Block inbound"), Sec(5441, 2), Sec(5447, 3, ("FilterName", ""))));
    }

    [Fact]
    public void Five_blocks_of_one_security_process_in_ten_minutes_are_High_four_or_a_window_exceeded_are_not()
    {
        const string app = @"\device\harddiskvolume3\program files\windows defender\MsMpEng.exe";
        var f = Assert.Single(Of("EDR-WFP-BLOCK", Enumerable.Range(0, 5).Select(i => Block(i * 2.5, app))));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("msmpeng.exe", f.Title);
        Assert.Empty(Of("EDR-WFP-BLOCK", Enumerable.Range(0, 4).Select(i => Block(i, app))));
        Assert.Empty(Of("EDR-WFP-BLOCK", Block(0, app), Block(1, app), Block(2, app), Block(3, app), Block(10 + 1.0 / 60 + 0.0, app)));
        Assert.Single(Of("EDR-WFP-BLOCK", Block(0, app), Block(2, app), Block(4, app), Block(6, app), Block(10, app)));
    }

    [Fact]
    public void Blocks_of_other_applications_mixed_processes_and_empty_applications_do_not_add_up()
    {
        const string defender = @"\device\harddiskvolume3\x\MsMpEng.exe";
        Assert.Empty(Of("EDR-WFP-BLOCK", Enumerable.Range(0, 10).Select(i => Block(i, @"\device\harddiskvolume3\x\chrome.exe"))));
        Assert.Empty(Of("EDR-WFP-BLOCK", Block(0, defender), Block(1, defender), Block(2, @"\device\x\xagt.exe"), Block(3, @"\device\x\xagt.exe"), Block(4, defender), Block(5, ""), Sec(5157, 6)));
        // a file that merely ends in the same letters is not the agent
        Assert.Empty(Of("EDR-WFP-BLOCK", Enumerable.Range(0, 6).Select(i => Block(i, @"\device\x\notcb.exe"))));
    }
}
