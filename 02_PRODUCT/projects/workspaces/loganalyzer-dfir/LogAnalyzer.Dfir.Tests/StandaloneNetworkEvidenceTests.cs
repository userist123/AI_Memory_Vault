using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner directive 2026-10-16: on a Standalone station every network connection after the installation of the application is NECONFORM, earlier ones are history
/// (DE VERIFICAT); the alternative explanations about an approved network do not apply there; the "ce nu putem demonstra" items are replaced by evidence collected
/// from Windows (adapter, volume, internet egress, duration, approval) or by „sursă lipsă: …, de ce lipsește, cum se pornește”; EVTX completeness is checked.
/// </summary>
public sealed class StandaloneNetworkEvidenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_standalone_net_" + Guid.NewGuid().ToString("N"));
    public StandaloneNetworkEvidenceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly DateTimeOffset Install = new DateTimeOffset(T0).AddMinutes(30);
    private const string NetCh = "Microsoft-Windows-NetworkProfile/Operational";

    private static TimelineEvent NetUp(double m, string name = "Rețea-X") => Ev(NetCh, 10000, m, "Microsoft-Windows-NetworkProfile", ("Name", name), ("Category", "1"));
    private static TimelineEvent NetDown(double m, string name = "Rețea-X") => Ev(NetCh, 10001, m, "Microsoft-Windows-NetworkProfile", ("Name", name));

    private static TimelineEvent Srum(double m, string provider, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(m), "", "test"), Source = "SRUM", Provider = provider, EvidenceId = "EV-SRUM", Locator = $"srum/{m}/{provider}", Summary = "srum",
        Process = "browser.exe", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static List<Finding> Run(IEnumerable<TimelineEvent> events, NetworkCategory n = NetworkCategory.StandalonePc, DateTimeOffset? install = null, string basis = "",
        AirGapFacts? facts = null, IReadOnlyCollection<string>? sources = null)
    {
        int k = 0;
        return Wp14Rules.Run(events.ToList(), () => $"F-{++k:D4}", new Wp14Input
        {
            Scope = Classified(n), AppInstalledUtc = install, AppInstallBasis = basis, Facts = facts, CollectedSources = sources,
        });
    }

    // ---- item 1: installation date, NECONFORM after it, history before it ----

    [Fact]
    public void The_installation_date_is_the_first_chained_record_of_the_application_audit_chain()
    {
        var path = Path.Combine(_dir, "auth_audit.jsonl");
        var chain = new HashChain(path);
        chain.Append(new { @event = "admin.created", whenUtc = Install });
        chain.Append(new { @event = "clock.seen", whenUtc = Install.AddDays(3) });
        var r = AppInstallDate.Read(path);
        Assert.Equal(Install, r.Utc);
        Assert.Contains("seq 1", r.Basis);

        var missing = AppInstallDate.Read(Path.Combine(_dir, "nu-exista.jsonl"));
        Assert.Null(missing.Utc);
        Assert.StartsWith("sursă lipsă:", missing.Basis);
        Assert.Contains("cum se pornește", missing.Basis);
    }

    [Fact]
    public void On_Standalone_a_connection_after_installation_is_NECONFORM_without_the_approved_network_alternatives_and_one_before_is_history()
    {
        var f = Run([NetUp(10), NetUp(60)], install: Install, basis: "primul rând înlănțuit");
        var after = Assert.Single(f, x => x.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.StartsWith("NECONFORM", after.Title);
        Assert.Contains("NECONFORM: în postura Standalone", after.Description);
        Assert.DoesNotContain("necesită explicație", after.Description);
        Assert.Empty(after.AlternativeExplanations);
        Assert.Equal(AirGapAuthorization.NotAuthorized, after.AirGap!.Authorized);
        Assert.Equal(new DateTimeOffset(T0).AddMinutes(60), after.FirstSeenUtc);

        var before = Assert.Single(f, x => x.RuleId == "AIRGAP-NETWORK-HISTORY");
        Assert.Contains("DE VERIFICAT", before.Title);
        Assert.DoesNotContain("NECONFORM", before.Title);
        Assert.Equal(new DateTimeOffset(T0).AddMinutes(10), before.FirstSeenUtc);
        Assert.True(before.Severity < after.Severity);
    }

    [Fact]
    public void On_Standalone_with_an_unknown_installation_date_every_connection_is_NECONFORM_and_the_missing_source_is_named()
    {
        var x = Assert.Single(Run([NetUp(10)]), f => f.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.StartsWith("NECONFORM", x.Title);
        Assert.Contains("data instalării aplicației nu este cunoscută", x.Description);
        Assert.Contains("sursă lipsă: data instalării aplicației", x.Description);
        Assert.DoesNotContain(Run([NetUp(10)]), f => f.RuleId == "AIRGAP-NETWORK-HISTORY");
    }

    [Fact]
    public void On_Standalone_a_profile_cannot_authorize_and_wifi_and_dhcp_drop_the_approved_network_alternatives()
    {
        var p = ProfileWithDestinations(("Rețea-X", "ROSU"));
        int k = 0;
        var f = Wp14Rules.Run([NetUp(40), Ev("Microsoft-Windows-WLAN-AutoConfig/Operational", 8001, 41, "", ("SSID", "Hotspot")), Ev("Microsoft-Windows-Dhcp-Client/Operational", 50036, 42, "", ("IPAddress", "10.0.0.5"))],
            () => $"F-{++k}", new Wp14Input { Scope = Classified(NetworkCategory.StandalonePc), Profile = p, AppInstalledUtc = Install });
        foreach (var x in f.Where(x => x.RuleId is "AIRGAP-NETWORK-CONNECTED" or "AIRGAP-WIFI-ASSOCIATED" or "AIRGAP-DHCP-LEASE"))
        {
            Assert.Equal(AirGapAuthorization.NotAuthorized, x.AirGap!.Authorized);
            Assert.StartsWith("NECONFORM", x.Title);
            Assert.DoesNotContain(x.AlternativeExplanations, a => a.Contains("aprobat", StringComparison.OrdinalIgnoreCase) || a.Contains("legitim", StringComparison.OrdinalIgnoreCase) || a.Contains("fără ieșire", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void On_an_isolated_network_the_alternative_explanations_stay()
    {
        var x = Assert.Single(Run([NetUp(10)], NetworkCategory.AirGappedNetwork), f => f.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.Contains(x.AlternativeExplanations, a => a.Contains("aprobată de procedură"));
        Assert.Contains(x.AlternativeExplanations, a => a.StartsWith("Evenimentul 10000 apare și pentru rețele fără acces în afară"));
        Assert.DoesNotContain("NECONFORM", x.Title);
    }

    // ---- item 2: evidence collected around the connection, or the missing source named ----

    [Fact]
    public void The_adapter_volume_internet_egress_duration_and_approval_are_collected_from_the_sources()
    {
        var facts = new AirGapFacts();
        facts.Network.ProfilesRead = true;
        facts.Network.Profiles.Add(new NetworkListProfileFact { ProfileName = "Rețea-X", NameType = 71, Category = 1, DefaultGatewayMac = "AA-BB-CC-DD-EE-FF", DnsSuffix = "acasa.lan", DateLastConnectedUtc = new DateTimeOffset(T0).AddMinutes(60) });
        var events = new List<TimelineEvent>
        {
            NetUp(40),
            Ev("Microsoft-Windows-WLAN-AutoConfig/Operational", 8001, 40.5, "", ("SSID", "Rețea-X"), ("InterfaceDescription", "Intel Wi-Fi 6 AX201"), ("InterfaceGuid", "{1111}"), ("BSSID", "11:22:33:44:55:66")),
            Ev("Microsoft-Windows-Dhcp-Client/Operational", 50036, 41, "", ("InterfaceGuid", "{1111}"), ("IPAddress", "192.168.1.20"), ("Gateway", "192.168.1.1")),
            Sec(5156, 45, ("DestAddress", "93.184.216.34"), ("Application", @"\device\harddiskvolume3\program files\browser\browser.exe")),
            Ev("Microsoft-Windows-NCSI/Operational", 4042, 41.5, "", ("Capability", "Internet")),
            Srum(100, "SRUM Network Data Usage", ("BytesSent", "2000000"), ("BytesRecvd", "50000000"), ("InterfaceLuid", "1689399632855040")),
            Srum(100, "SRUM Network Connectivity", ("ConnectedTime", "1200"), ("InterfaceLuid", "1689399632855040")),
            NetDown(60),
        };
        var x = Assert.Single(Run(events, install: Install, facts: facts), f => f.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.Contains("Intel Wi-Fi 6 AX201", x.Description);                 // adapter used (WLAN 8001)
        Assert.Contains("BSSID 11:22:33:44:55:66", x.Description);
        Assert.Contains("gateway 192.168.1.1", x.Description);                  // DHCP
        Assert.Contains("MAC gateway AA-BB-CC-DD-EE-FF", x.Description);        // NetworkList signature
        Assert.Contains("93.184.216.34", x.Description);                        // firewall 5156
        Assert.Contains("50.0 MB primiți", x.Description);                      // SRUM volume
        Assert.Contains("AVEA ieșire la internet", x.Description);              // NCSI 4042
        Assert.Contains("20 min din 1 conectări", x.Description);               // 10001 duration
        Assert.Contains("SRUM Network Connectivity: 20 min", x.Description);    // SRUM duration
        Assert.Contains("Aprobare: în postura Standalone nu există nicio conectare aprobată", x.Description);
        Assert.Contains(x.SupportingEvidence, r => r.EvidenceId == "EV-SRUM");
        // Only what really cannot be had stays: the firewall text log was not in the case, and packet content never is.
        Assert.All(x.MissingEvidence, m => Assert.StartsWith("sursă lipsă:", m));
        Assert.Contains(x.MissingEvidence, m => m.Contains("pfirewall.log") && m.Contains("cum se pornește"));
        Assert.Contains(x.MissingEvidence, m => m.Contains("PCAP"));
        Assert.DoesNotContain(x.MissingEvidence, m => m.Contains("SRUM") || m.Contains("NCSI") || m.Contains("WLAN") || m.Contains("Dhcp") || m.Contains("5156"));
    }

    [Fact]
    public void Every_unreadable_source_is_named_with_why_and_how_and_a_required_audit_source_is_NECONFORM_on_Standalone()
    {
        var x = Assert.Single(Run([NetUp(40)], install: Install, sources: ["EventLog:" + NetCh, "EventLog:Security"]), f => f.RuleId == "AIRGAP-NETWORK-CONNECTED");
        Assert.All(x.MissingEvidence, m => { Assert.StartsWith("sursă lipsă:", m); Assert.Contains("de ce lipsește", m); Assert.Contains("cum se pornește", m); });
        foreach (var src in new[] { "WLAN-AutoConfig", "Dhcp-Client", "NetworkList", "SRUM", "NCSI", "pfirewall.log", "momentul deconectării" })
            Assert.Contains(x.MissingEvidence, m => m.Contains(src));
        var wfp = Assert.Single(x.MissingEvidence, m => m.Contains("Security 5156/5157"));
        Assert.Contains("NECONFORM în postura Standalone", wfp);
        Assert.Contains("„Filtering Platform Connection” (succes și eșec)", wfp);
        Assert.DoesNotContain("/failure:enable", wfp);   // the Standalone build holds no ready command that changes the audit policy
        // The old blanket items are gone.
        var all = string.Join("\n", x.MissingEvidence.Concat(x.Limitations));
        Assert.DoesNotContain("Traficul (firewall 5156, PCAP), adaptorul folosit, aprobarea conectării", all);
        Assert.DoesNotContain("Autorizarea vine din destinațiile", all);
        Assert.DoesNotContain("Durata se calculează doar dacă", all);
    }

    [Fact]
    public void The_rule_contract_keeps_only_what_no_station_source_can_give()
    {
        var c = RuleContracts.For("AIRGAP-NETWORK-CONNECTED")!;
        Assert.DoesNotContain(c.Limitations, l => l.Contains("Autorizarea vine din") || l.Contains("Durata se calculează doar") || l.Contains("nu dacă rețeaua avea ieșire"));
        Assert.All(c.MissingEvidence, m => Assert.StartsWith("sursă lipsă:", m));
        Assert.NotNull(RuleContracts.For("AIRGAP-NETWORK-HISTORY"));
        Assert.DoesNotContain(ParserCapabilities.For("EvtxParser")!.CannotProve, c2 => c2.Contains("jurnalul e complet"));
    }

    // ---- item 2d: EVTX completeness ----

    private static TimelineEvent Rec(string channel, long id, double m, int eventId = 1, string evidence = "EV-C", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(m), "", "test"), Source = "EventLog:" + channel, EventId = eventId.ToString(), EvidenceId = evidence,
        Locator = $"EventRecordID={id}", Summary = "t", Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public void A_log_with_continuous_record_numbers_covering_the_period_is_complete()
    {
        var tl = new List<TimelineEvent> { Rec(NetCh, 1, 0), Rec(NetCh, 2, 5), Rec(NetCh, 3, 9) };
        var v = EvtxCompleteness.Assess(NetCh, tl, new DateTimeOffset(T0));
        Assert.True(v.Complete);
        Assert.StartsWith($"Jurnalul {NetCh}: complet", v.Text);
    }

    [Fact]
    public void Gaps_clearing_and_an_oldest_record_after_the_period_start_make_the_log_incomplete_with_the_missing_interval()
    {
        var gap = EvtxCompleteness.Assess(NetCh, [Rec(NetCh, 1, 0), Rec(NetCh, 2, 1), Rec(NetCh, 7, 30)], null);
        Assert.False(gap.Complete);
        Assert.Contains("RecordID 3–6 (4 înregistrări)", gap.Text);
        Assert.Contains("incomplet", gap.Text);

        var cleared = EvtxCompleteness.Assess("Security", [Rec("Security", 1, 0, 1102), Rec("Security", 2, 1, 4624)], null);
        Assert.False(cleared.Complete);
        Assert.Contains("golit", cleared.Text);

        var otherLog = EvtxCompleteness.Assess(NetCh, [Rec(NetCh, 1, 5), Rec("System", 9, 4, 104, "EV-S", ("Channel", NetCh))], null);
        Assert.False(otherLog.Complete);

        var wrapped = EvtxCompleteness.Assess(NetCh, [Rec(NetCh, 500, 60), Rec(NetCh, 501, 61)], new DateTimeOffset(T0));
        Assert.False(wrapped.Complete);
        Assert.Contains("suprascris", wrapped.Text);
        Assert.Contains("interval lipsă", wrapped.Text);

        var none = EvtxCompleteness.Assess("Microsoft-Windows-NCSI/Operational", [Rec(NetCh, 1, 0)], null);
        Assert.Null(none.Complete);
        Assert.Contains("sursă lipsă:", none.Text);
    }

    [Fact]
    public void The_finding_card_says_complete_or_incomplete_for_every_log_the_finding_rests_on()
    {
        var up = Rec(NetCh, 1, 40, 10000, "EV-N", ("Name", "Rețea-X"));
        var tl = new List<TimelineEvent> { up, Rec(NetCh, 2, 41, 10001, "EV-N", ("Name", "Rețea-X")) };
        var f = new Finding { Description = "d", FindingId = "F-1", RuleId = "AIRGAP-NETWORK-CONNECTED", Title = "t", Classification = Classification.Direct, SupportingEvidence = [new EvidenceRef("EV-N", up.Locator, "10000")] };
        var ok = KnowThinkDontKnow.Build(f, new EvidenceContext { Timeline = tl });
        Assert.Contains(ok.Know, s => s.Text.StartsWith($"Jurnalul {NetCh}: complet") && s.Basis == EvtxCompleteness.Basis);

        var tl2 = new List<TimelineEvent> { up, Rec(NetCh, 9, 50, 10001, "EV-N", ("Name", "Rețea-X")) };
        var bad = KnowThinkDontKnow.Build(f, new EvidenceContext { Timeline = tl2 });
        Assert.Contains(bad.DontKnow, s => s.Text.StartsWith($"Jurnalul {NetCh}: incomplet") && s.Text.Contains("RecordID 2–8"));
    }

    [Fact]
    public void A_missing_source_reads_as_such_on_the_card()
    {
        var f = new Finding { Description = "d", FindingId = "F-1", RuleId = "X-Y", Title = "t", MissingEvidence = ["sursă lipsă: A, de ce lipsește: b, cum se pornește: c"], Limitations = ["sursă lipsă: D, de ce lipsește: e, cum se pornește: f"] };
        var k = KnowThinkDontKnow.Build(f, EvidenceContext.Empty);
        Assert.All(k.DontKnow.Where(s => s.Text.StartsWith("sursă lipsă:")), s => Assert.StartsWith("sursă lipsă", s.Basis));
        Assert.DoesNotContain(k.DontKnow, s => s.Basis.Contains("limită a acestui tip de probă"));
    }

    // ---- firewall text log ----

    [Fact]
    public void The_firewall_text_log_is_recognised_and_parsed_into_connections()
    {
        var path = Path.Combine(_dir, "pfirewall.log");
        File.WriteAllText(path, "#Version: 1.5\r\n#Software: Microsoft Windows Firewall\r\n#Time Format: Local\r\n" +
            "#Fields: date time action protocol src-ip dst-ip src-port dst-port size tcpflags tcpsyn tcpack tcpwin icmptype icmpcode info path pid\r\n\r\n" +
            "2026-10-01 10:05:00 ALLOW TCP 192.168.1.20 93.184.216.34 50000 443 0 - 0 0 0 - - - SEND 4242\r\n" +
            "2026-10-01 10:06:00 DROP UDP 10.0.0.9 192.168.1.20 5353 5353 0 - - - - - - - RECEIVE 0\r\n");
        Assert.Equal("pfirewall", LogAnalyzer.Dfir.Integrity.EvidenceFingerprint.Detect(path));
        var item = new EvidenceItem { EvidenceId = "EV-FW", CaseId = "C", Source = "Firewall", SourceType = FirewallLogParser.SourceTypeName, StoredPath = path, Timezone = "UTC" };
        var sink = new ListSink();
        var res = new FirewallLogParser().Parse(item, path, sink, CancellationToken.None);
        Assert.Equal(2, res.Records);
        var a = sink.Events[0];
        Assert.Equal(FirewallLogParser.SourceName, a.Source);
        Assert.Equal("93.184.216.34", a.RemoteIp);
        Assert.Equal(443, a.RemotePort);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 10, 5, 0, TimeSpan.Zero), a.Time.Utc);
        Assert.Equal("10.0.0.9", sink.Events[1].RemoteIp);
    }

    // ---- station control N01 on Standalone ----

    private static StationFacts Station(DateTimeOffset? install, params NetworkProfileFact[] profiles)
    {
        var start = Install.AddDays(-60);
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = start, PeriodEndUtc = Install.AddDays(30), CollectedUtc = Install.AddDays(30), IsAdministrator = true, Posture = StationPosture.Standalone,
                                   AppInstalledUtc = install, AppInstallBasis = "primul rând înlănțuit" };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, start, 100, true, ""));
        f.NetworkProfiles.AddRange(profiles);
        return f;
    }

    private static ControlCheck N01(StationFacts f) => Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N01");

    [Fact]
    public void Station_control_N01_on_Standalone_judges_against_the_installation_date()
    {
        var before = new NetworkProfileFact("Rețea-pregătire", "cablu (Ethernet)", null, Install.AddDays(-2).LocalDateTime, "");
        var after = new NetworkProfileFact("Hotspot", "Wi-Fi", null, Install.AddDays(2).LocalDateTime, "");
        var hist = N01(Station(Install, before));
        Assert.Equal(ControlStatus.DeVerificat, hist.Status);
        Assert.Contains("istoric", hist.Detail);
        var bad = N01(Station(Install, before, after));
        Assert.Equal(ControlStatus.Neconform, bad.Status);
        Assert.Contains("după instalarea aplicației", bad.Detail);
        Assert.Contains(bad.Evidence, e => e.StartsWith("după instalare: Hotspot"));
        Assert.Contains(bad.Evidence, e => e.StartsWith("înainte de instalare (istoric): Rețea-pregătire"));
        // Unknown installation date: the period rule applies as before (a connection in the period is NECONFORM).
        Assert.Equal(ControlStatus.Neconform, N01(Station(null, after)).Status);
    }
}
