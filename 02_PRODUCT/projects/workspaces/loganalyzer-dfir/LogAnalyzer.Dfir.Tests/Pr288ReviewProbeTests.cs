using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The probes of the independent review of PR #288, as regression tests: a rewritten or re-initialised audit chain must not move the installation date
/// (and with it NECONFORM connections into history); EVTX "complet" only with a covered period; no fact invented from field names that the Windows
/// templates do not have; no firewall-log path outside a local fixed disk; Standalone N01 with an unknown date judges every connection.
/// </summary>
public sealed class Pr288ReviewProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_pr288_" + Guid.NewGuid().ToString("N"));
    public Pr288ReviewProbeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly DateTimeOffset Install = new DateTimeOffset(T0).AddMinutes(30);
    private static readonly DateTimeOffset Now = Install.AddDays(20);
    private const string NetCh = "Microsoft-Windows-NetworkProfile/Operational";

    private static AuthAuditEntry E(string ev, DateTimeOffset when) => new(ev, "x", "admin", "", "", "ok", "", when, "", "");

    private string Chain(params AuthAuditEntry[] entries)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".jsonl");
        var c = new HashChain(path);
        foreach (var e in entries) c.Append(e);
        return path;
    }

    private static AppInstall Read(string path, params AppTrace[] traces) => AppInstallDate.Read(path, traces, Now);

    // ---- 1. installation date ----

    [Fact]
    public void A_valid_chain_that_starts_with_the_store_creation_gives_its_first_date()
    {
        var r = Read(Chain(E(ClockMemory.Event, Install), E(AppInstallDate.StoreCreatedEvent, Install.AddMinutes(1)), E("auth.signin", Install.AddDays(1))),
                     new AppTrace("fișierul aplicației LogAnalyzer.exe", Install.AddHours(-3)));
        Assert.Equal(Install.AddHours(-3), r.Utc);                // the program files are a little older: the earliest trace wins, within tolerance
        Assert.Empty(r.Violations);
    }

    [Fact]
    public void Probe_editing_the_date_of_line_1_breaks_the_chain_and_the_date_becomes_unknown()
    {
        var path = Chain(E(AppInstallDate.StoreCreatedEvent, Install), E("auth.signin", Install.AddDays(1)));
        var lines = File.ReadAllLines(path);
        lines[0] = lines[0].Replace(Install.ToString("yyyy-MM-dd"), "2030-01-01");
        File.WriteAllLines(path, lines);
        var r = Read(path);
        Assert.Null(r.Utc);
        Assert.Contains(r.Violations, v => v.Contains("nu se verifică"));
    }

    [Fact]
    public void Probe_a_first_record_in_the_future_is_refused_even_when_the_chain_is_consistent()
    {
        var r = Read(Chain(E(AppInstallDate.StoreCreatedEvent, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero))));
        Assert.Null(r.Utc);
        Assert.Contains(r.Violations, v => v.Contains("viitor"));
    }

    [Fact]
    public void Probe_deleting_line_1_is_not_silently_seq_2()
    {
        var path = Chain(E(AppInstallDate.StoreCreatedEvent, Install), E("auth.signin", Install.AddDays(1)), E("auth.signin", Install.AddDays(2)));
        File.WriteAllLines(path, File.ReadAllLines(path).Skip(1));
        var r = Read(path);
        Assert.Null(r.Utc);
        Assert.NotEmpty(r.Violations);
    }

    [Fact]
    public void Probe_a_chain_that_does_not_start_with_the_store_creation_is_not_an_installation_date()
    {
        var r = Read(Chain(E("auth.signin", Install), E("auth.signin", Install.AddDays(1))));
        Assert.Null(r.Utc);
        Assert.Contains(r.Violations, v => v.Contains("nu începe cu crearea magazinului"));
    }

    [Fact]
    public void Probe_a_recreated_auth_folder_cannot_move_the_date_forward_and_the_mismatch_is_a_violation()
    {
        // auth folder deleted, admin created again 10 days after the program was installed
        var r = Read(Chain(E(AppInstallDate.StoreCreatedEvent, Install.AddDays(10))), new AppTrace("folderul de caz CASE-1", Install), new AppTrace("licența (license.lic)", Install.AddDays(1)));
        Assert.Equal(Install, r.Utc);
        Assert.Contains(r.Violations, v => v.StartsWith("data instalării nu se potrivește — jurnalul aplicației a fost reinițializat"));
    }

    [Fact]
    public void Probe_every_store_reinitialisation_is_reported()
    {
        var r = Read(Chain(E(AppInstallDate.StoreCreatedEvent, Install), E(AppInstallDate.StoreReinitialisedEvent, Install.AddDays(5)), E(AppInstallDate.StoreCreatedEvent, Install.AddDays(5))));
        Assert.Equal(Install, r.Utc);
        Assert.Contains(r.Violations, v => v.Contains("reinițializat offline"));
        Assert.Contains(r.Violations, v => v.Contains("creat din nou"));
    }

    [Fact]
    public void Install_violations_are_NECONFORM_on_Standalone_in_the_findings_and_in_N01()
    {
        int k = 0;
        var f = Wp14Rules.Run([], () => $"F-{++k}", new Wp14Input { Scope = Classified(NetworkCategory.StandalonePc), AppInstalledUtc = Install, AppInstallViolations = ["magazinul a fost reinițializat"] });
        var x = Assert.Single(f, y => y.RuleId == "AIRGAP-APP-INSTALL-INTEGRITY");
        Assert.StartsWith("NECONFORM", x.Title);
        k = 0;
        Assert.DoesNotContain(Wp14Rules.Run([], () => $"F-{++k}", new Wp14Input { Scope = Classified(NetworkCategory.AirGappedNetwork), AppInstalledUtc = Install, AppInstallViolations = ["x"] }),
            y => y.RuleId == "AIRGAP-APP-INSTALL-INTEGRITY");

        var s = Station(Install);
        s.AppInstallViolations.Add("magazinul a fost reinițializat");
        var n01 = N01(s);
        Assert.Equal(ControlStatus.Neconform, n01.Status);
        Assert.Contains("NECONFORM și pentru data instalării", n01.Detail);
    }

    // ---- 2. EVTX completeness ----

    private static TimelineEvent Rec(string channel, long id, double minutes, int eventId = 1, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minutes), "", "test"), Source = "EventLog:" + channel, EventId = eventId.ToString(), EvidenceId = "EV-" + channel,
        Locator = $"EventRecordID={id}", Summary = "t", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static readonly DateTimeOffset P0 = new(T0), P1 = new DateTimeOffset(T0).AddDays(10);

    [Fact]
    public void Probe_without_a_controlled_period_the_log_is_never_complete()
    {
        var v = EvtxCompleteness.Assess(NetCh, [Rec(NetCh, 1, 0), Rec(NetCh, 2, 5)], null);
        Assert.Null(v.Complete);
        Assert.StartsWith($"Jurnalul {NetCh}: neevaluat", v.Text);
    }

    [Fact]
    public void Probe_an_always_written_log_that_stops_before_the_period_end_is_incomplete()
    {
        var v = EvtxCompleteness.Assess("Security", [Rec("Security", 1, -10), Rec("Security", 2, 60)], P0, P1);
        Assert.False(v.Complete);
        Assert.Contains("jurnalul a fost oprit", v.Text);
    }

    [Fact]
    public void A_quiet_channel_whose_tail_does_not_reach_the_end_is_not_evaluated_not_complete()
    {
        var v = EvtxCompleteness.Assess(NetCh, [Rec(NetCh, 1, -10), Rec(NetCh, 2, 60)], P0, P1);
        Assert.Null(v.Complete);
    }

    [Fact]
    public void Probe_a_clear_long_before_the_period_with_full_coverage_is_complete_and_a_clear_inside_it_is_not()
    {
        var before = EvtxCompleteness.Assess("Security", [Rec("Security", 1, -5000, 1102), Rec("Security", 2, -10), Rec("Security", 3, 14390)], P0, P1);
        Assert.True(before.Complete, before.Text);
        Assert.Contains("înainte de perioada controlată", before.Text);
        var inside = EvtxCompleteness.Assess("Security", [Rec("Security", 1, -10), Rec("Security", 2, 100, 1102), Rec("Security", 3, 14390)], P0, P1);
        Assert.False(inside.Complete);
    }

    // ---- 3. only the fields the Windows templates really have ----

    private static List<Finding> Go(params TimelineEvent[] events)
    {
        int k = 0;
        return Wp14Rules.Run(events.ToList(), () => $"F-{++k}", new Wp14Input { Scope = Classified(NetworkCategory.StandalonePc), AppInstalledUtc = Install });
    }

    private static TimelineEvent NetUp(double m) => Ev(NetCh, 10000, m, "Microsoft-Windows-NetworkProfile", ("Name", "Rețea-X"), ("Description", "Rețea-X"), ("Guid", "{AAAA}"), ("Type", "1"), ("State", "1"), ("Category", "1"));

    [Fact]
    public void Probe_wlan_8001_with_its_real_template_fields_never_claims_a_bssid_and_names_a_missing_field()
    {
        var x = Assert.Single(Go(NetUp(40), Ev("Microsoft-Windows-WLAN-AutoConfig/Operational", 8001, 40.5, "", ("InterfaceGuid", "{1111}"), ("ConnectionMode", "Automatic"),
            ("ProfileName", "Rețea-X"), ("SSID", "Rețea-X"), ("BSSType", "Infrastructure"), ("PHYType", "802.11ax"), ("AuthenticationAlgorithm", "WPA3-SAE"), ("CipherAlgorithm", "AES-CCMP"))),
            f => f.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.Contains("BSSID-ul punctului de acces nu face parte din evenimentul 8001", x.Description);
        Assert.Contains("(câmpul InterfaceDescription lipsește din eveniment, format neașteptat)", x.Description);
        Assert.Contains("autentificare „WPA3-SAE”", x.Description);
    }

    [Fact]
    public void Probe_ncsi_4042_with_a_numeric_capability_is_reported_raw_and_not_turned_into_a_verdict()
    {
        var x = Assert.Single(Go(NetUp(40), Ev("Microsoft-Windows-NCSI/Operational", 4042, 41, "", ("InterfaceGuid", "{1111}"), ("IfLuid", "1"), ("Family", "2"), ("Capability", "2"),
            ("CapabilityChangeReason", "0"), ("PreviousCapability", "1"))), f => f.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.Contains("capacitate valoarea brută 2 (anterior 1, familia 2)", x.Description);
        Assert.DoesNotContain("a raportat acces la internet", x.Description);
        Assert.DoesNotContain("nu a raportat acces la internet", x.Description);
        var missing = Assert.Single(Go(NetUp(40), Ev("Microsoft-Windows-NCSI/Operational", 4042, 41, "", ("InterfaceGuid", "{1111}"))), f => f.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.Contains("câmpul Capability lipsește din eveniment", missing.Description);
    }

    [Fact]
    public void Probe_dhcp_events_with_their_real_template_fields_do_not_claim_an_address()
    {
        var x = Assert.Single(Go(NetUp(40), Ev("Microsoft-Windows-Dhcp-Client/Operational", 50037, 40.2, "", ("DwordVal", "0")),
            Ev("Microsoft-Windows-Dhcp-Client/Operational", 50065, 40.3, "", ("NetworkHintString", "acasa"), ("HWAddress", "00-11-22-33-44-55"))), f => f.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.Contains("câmpul adresei IP lipsește din eveniment", x.Description);
        Assert.Contains("adresa fizică a interfeței 00-11-22-33-44-55", x.Description);
        var lease = Assert.Single(Go(Ev("Microsoft-Windows-Dhcp-Client/Operational", 50037, 40.2, "", ("DwordVal", "0"))), f => f.RuleId == "AIRGAP-DHCP-LEASE");
        Assert.Contains("câmpul adresei IP lipsește din eveniment", lease.Description);
    }

    // ---- 4. firewall log path, size and line length ----

    [Theory]
    [InlineData(@"\\server\share\pfirewall.log")]
    [InlineData(@"\\?\UNC\server\share\pfirewall.log")]
    [InlineData(@"\\?\C:\Windows\pfirewall.log")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData(@"//server/share/pfirewall.log")]
    [InlineData(@"logs\pfirewall.log")]
    [InlineData(@"C:pfirewall.log")]
    public void Probe_unc_device_and_relative_firewall_log_paths_are_refused_without_asking_the_drive(string path)
    {
        var asked = false;
        var why = FirewallLogParser.RejectPath(path, _ => { asked = true; return DriveType.Fixed; });
        Assert.StartsWith("cale jurnal firewall nepermisă", why);
        Assert.False(asked);
    }

    [Fact]
    public void Probe_a_mapped_network_or_removable_drive_is_refused_and_a_local_fixed_disk_is_accepted()
    {
        Assert.StartsWith("cale jurnal firewall nepermisă", FirewallLogParser.RejectPath(@"Z:\fw\pfirewall.log", _ => DriveType.Network));
        Assert.StartsWith("cale jurnal firewall nepermisă", FirewallLogParser.RejectPath(@"E:\pfirewall.log", _ => DriveType.Removable));
        Assert.Equal("", FirewallLogParser.RejectPath(@"C:\Windows\System32\LogFiles\Firewall\pfirewall.log", _ => DriveType.Fixed));
    }

    [Fact]
    public void Probe_the_firewall_parser_is_size_bounded_and_skips_an_over_long_line()
    {
        Assert.Equal(FirewallLogParser.MaxLogBytes, new FirewallLogParser().Descriptor.MaxInputBytes);
        var path = Path.Combine(_dir, "pfirewall.log");
        File.WriteAllText(path, "#Version: 1.5\r\n#Software: Microsoft Windows Firewall\r\n#Time Format: Local\r\n" +
            "#Fields: date time action protocol src-ip dst-ip src-port dst-port size tcpflags tcpsyn tcpack tcpwin icmptype icmpcode info path pid\r\n" +
            new string('A', FirewallLogParser.MaxLineChars * 3) + "\r\n" +
            "2026-10-01 10:05:00 ALLOW TCP 192.168.1.20 93.184.216.34 50000 443 0 - 0 0 0 - - - SEND 4242\r\n");
        var sink = new ListSink();
        var r = new FirewallLogParser().Parse(new EvidenceItem { EvidenceId = "EV-FW", CaseId = "C", Source = "Firewall", SourceType = FirewallLogParser.SourceTypeName, StoredPath = path, Timezone = "UTC" }, path, sink, CancellationToken.None);
        Assert.Equal(1, r.Records);
        Assert.Equal(1, r.MalformedRecords);
    }

    [Fact]
    public void A_refused_firewall_log_path_is_a_finding_in_every_scope_and_the_remedies_are_described_not_commands()
    {
        var facts = new AirGapFacts();
        facts.Network.FirewallLogs.Add(new FirewallLogFact { Profile = "Domain", Path = @"\\srv\fw\pfirewall.log", Rejected = "cale jurnal firewall nepermisă: test" });
        foreach (var n in new[] { NetworkCategory.StandalonePc, NetworkCategory.Connected })
        {
            int k = 0;
            var f = Wp14Rules.Run([], () => $"F-{++k}", new Wp14Input { Scope = Classified(n), Facts = facts });
            Assert.Contains(f, x => x.RuleId == "FIREWALL-LOG-PATH-REJECTED" && x.Description.Contains("cale jurnal firewall nepermisă"));
        }
        Assert.DoesNotContain("Set-NetFirewallProfile", Wp14Rules.HowToEnableFirewallLog);
        Assert.DoesNotContain("/failure:enable", Wp14Rules.HowToEnableFirewallLog);
    }

    // ---- 5. Standalone N01 with an unknown installation date ----

    private static StationFacts Station(DateTimeOffset? install, params NetworkProfileFact[] profiles)
    {
        var start = Install.AddDays(10);   // the controlled period starts AFTER the old connection below
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = start, PeriodEndUtc = start.AddDays(30), CollectedUtc = start.AddDays(30), IsAdministrator = true, Posture = StationPosture.Standalone,
                                   AppInstalledUtc = install, AppInstallBasis = install is null ? "sursă lipsă: test" : "test" };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, start.AddDays(-60), 100, true, ""));
        f.NetworkProfiles.AddRange(profiles);
        return f;
    }

    private static ControlCheck N01(StationFacts f) => Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N01");

    [Fact]
    public void Probe_Standalone_N01_with_an_unknown_date_is_NECONFORM_for_a_connection_outside_the_period_too()
    {
        var old = new NetworkProfileFact("Vechi", "Wi-Fi", null, Install.AddDays(1).LocalDateTime, "");
        var c = N01(Station(null, old));
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Data instalării aplicației nu este cunoscută", c.Detail);
    }
}
