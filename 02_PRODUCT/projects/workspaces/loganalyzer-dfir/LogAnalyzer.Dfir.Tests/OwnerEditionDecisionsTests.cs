using System.Net;
using LogAnalyzer.Connectors.Domain;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner decisions on the three editions: the "wrong edition" warning of the Internet edition on an isolated-looking station, the removable-media
/// rules on Internet, and the active domain-controller reachability check N13 (Network edition only).
/// </summary>
public class OwnerEditionDecisionsTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static StationFacts Facts(StationPosture posture)
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = Start, PeriodEndUtc = Start.AddDays(30), CollectedUtc = Start.AddDays(30), IsAdministrator = true, Posture = posture };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, Start.AddDays(-60), 100, true, ""));
        return f;
    }

    // ---- decision 1: wrong-edition warning -------------------------------------------------------------------------------------

    [Fact]
    public void Internet_edition_on_a_station_without_any_active_way_out_gets_the_wrong_edition_warning()
    {
        Assert.Equal(EditionFit.WrongEditionText, EditionFit.Warning(StationPosture.Internet, []));
        Assert.Equal(EditionFit.WrongEditionText,
            EditionFit.Warning(StationPosture.Internet, [new EgressPathFact("Wi-Fi", "adaptor prezent, oprit", false, false), new EgressPathFact("Gateway", "10.0.0.1 (configurat)", true, false)]));
        Assert.StartsWith("Ediție nepotrivită: stația pare izolată.", EditionFit.WrongEditionText);
        Assert.Contains("LogAnalyzer.Standalone", EditionFit.WrongEditionText);
        Assert.Contains("LogAnalyzer.Retea", EditionFit.WrongEditionText);
    }

    [Fact]
    public void Connected_station_unknown_facts_and_other_editions_get_no_warning()
    {
        Assert.Null(EditionFit.Warning(StationPosture.Internet, [new EgressPathFact("Gateway", "192.168.1.1 pe Ethernet", true, true)]));   // NAT router: may lead out
        Assert.Null(EditionFit.Warning(StationPosture.Internet, [new EgressPathFact("Proxy", "WinHTTP: proxy.corp:8080", null, true)]));
        Assert.Null(EditionFit.Warning(StationPosture.Internet, [new EgressPathFact("Date mobile (WWAN)", "modem", false, true)]));
        Assert.Null(EditionFit.Warning(StationPosture.Internet, null));                 // not collected: never guessed
        Assert.Null(EditionFit.Warning(StationPosture.Standalone, []));
        Assert.Null(EditionFit.Warning(StationPosture.IsolatedNetwork, []));
    }

    [Fact]
    public void The_warning_is_a_line_of_the_control_report_header_and_never_changes_check_results()
    {
        var f = Facts(StationPosture.Internet);
        f.EgressPaths = [];
        var header = ControlReportHeader.From(ReportHeaderProfile.Load(null, out _), f, "HW", "Insp", "", "rol", "", "");
        Assert.Contains(header.Lines(), l => l.Label == "Avertisment ediție" && l.Value == EditionFit.WrongEditionText);

        var g = Facts(StationPosture.Internet);
        g.EgressPaths = [new EgressPathFact("Gateway", "192.168.1.1 pe Ethernet", true, true)];
        Assert.DoesNotContain(ControlReportHeader.From(ReportHeaderProfile.Load(null, out _), g, "HW", "Insp", "", "rol", "", "").Lines(), l => l.Label == "Avertisment ediție");

        var a = ControlEvaluator.Evaluate(f).Checks.Select(c => (c.Id, c.Status)).ToList();
        var b = ControlEvaluator.Evaluate(g).Checks.Select(c => (c.Id, c.Status)).ToList();
        Assert.Equal(a, b);
    }

    // ---- decision 3: removable-media rules apply on Internet --------------------------------------------------------------------

    [Theory]
    [InlineData("P14")]   // USB storage blocked by policy
    [InlineData("A08")]   // copies to removable media audited
    [InlineData("D02")]   // BitLocker To Go on removable media
    [InlineData("D01")]   // USB connection history
    public void Removable_media_rules_apply_on_Internet(string id) => Assert.True(ControlApplicability.Applies(id, StationPosture.Internet), id);

    [Theory]
    [InlineData("P29")]   // printing log
    [InlineData("P28")]   // CD/DVD burning
    [InlineData("P26")]   // classification banner
    public void Controlled_room_rules_stay_not_applicable_on_Internet(string id) => Assert.False(ControlApplicability.Applies(id, StationPosture.Internet), id);

    // ---- decision 4: N13, active domain-controller reachability (Network edition only) ----------------------------------------

    private static ControlCheck N13(StationFacts f) => Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N13");

    private static StationFacts Joined()
    {
        var f = Facts(StationPosture.IsolatedNetwork);
        f.Settings["Domain.PartOfDomain"] = "da"; f.Settings["Domain.Name"] = "corp.example";
        return f;
    }

    [Fact]
    public void N13_applies_only_to_the_Network_edition()
    {
        Assert.True(ControlApplicability.Applies("N13", StationPosture.IsolatedNetwork));
        Assert.False(ControlApplicability.Applies("N13", StationPosture.Standalone));
        Assert.False(ControlApplicability.Applies("N13", StationPosture.Internet));
        Assert.Equal(ControlStatus.NuSeAplica, N13(Facts(StationPosture.Standalone)).Status);
        Assert.Equal(ControlStatus.NuSeAplica, N13(Facts(StationPosture.Internet)).Status);
    }

    [Fact]
    public void N13_outcomes()
    {
        var f = Joined();
        f.DomainController = new DomainControllerProbeResult(DcProbeOutcome.Reachable, "dc1.corp.example", ["10.0.0.10"], "LDAP (389) răspunde.");
        Assert.Equal(ControlStatus.Conform, N13(f).Status);

        f = Joined();
        f.DomainController = new DomainControllerProbeResult(DcProbeOutcome.PublicAddress, "dc1.corp.example", ["203.0.113.5"], "");
        var c = N13(f);
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("controlerul de domeniu se rezolvă la o adresă publică", c.Detail);

        f = Joined();
        f.DomainController = new DomainControllerProbeResult(DcProbeOutcome.Unreachable, "dc1.corp.example", ["10.0.0.10"], "timeout");
        Assert.Equal(ControlStatus.DeVerificat, N13(f).Status);

        f = Joined();
        f.DomainController = new DomainControllerProbeResult(DcProbeOutcome.Skipped, "", [], "modul AirGapped");
        Assert.Equal(ControlStatus.Nedeterminat, N13(f).Status);

        f = Joined();                                                     // no probe in this build or not run
        Assert.Equal(ControlStatus.Nedeterminat, N13(f).Status);

        var n = Facts(StationPosture.IsolatedNetwork);                     // not domain-joined: nothing is probed
        n.Settings["Domain.PartOfDomain"] = "nu";
        var nc = N13(n);
        Assert.Equal(ControlStatus.Nedeterminat, nc.Status);
        Assert.Contains("N12", nc.Detail);
    }

    [Theory]
    [InlineData("10.0.0.10", DcAddressClass.Allowed)]
    [InlineData("172.16.5.5", DcAddressClass.Allowed)]
    [InlineData("192.168.1.2", DcAddressClass.Allowed)]
    [InlineData("fd12:3456::10", DcAddressClass.Allowed)]
    [InlineData("169.254.1.1", DcAddressClass.Excluded)]
    [InlineData("fe80::1", DcAddressClass.Excluded)]
    [InlineData("127.0.0.1", DcAddressClass.Excluded)]
    [InlineData("::1", DcAddressClass.Excluded)]
    [InlineData("203.0.113.5", DcAddressClass.Public)]
    [InlineData("100.64.0.1", DcAddressClass.Public)]
    [InlineData("2001:db8::5", DcAddressClass.Public)]
    public void Only_private_addresses_may_be_contacted(string address, DcAddressClass expected) =>
        Assert.Equal(expected, DomainControllerProbe.Classify(IPAddress.Parse(address)));

    [Fact]
    public void A_public_address_among_the_resolved_ones_means_no_connection_at_all()
    {
        int connects = 0;
        var probe = new DomainControllerProbe(
            locate: _ => ("dc1.corp.example", [IPAddress.Parse("10.0.0.10"), IPAddress.Parse("203.0.113.5")]),
            connect: (_, _) => { connects++; return true; }, networkAllowed: () => true);
        var r = probe.Probe("corp.example", CancellationToken.None);
        Assert.Equal(DcProbeOutcome.PublicAddress, r.Outcome);
        Assert.Equal(0, connects);
    }

    [Fact]
    public void Probe_connects_only_to_an_allowed_address_and_reports_reachability()
    {
        var tried = new List<IPAddress>();
        var probe = new DomainControllerProbe(
            locate: _ => ("dc1.corp.example", [IPAddress.Parse("fe80::1"), IPAddress.Parse("10.0.0.10")]),
            connect: (ip, _) => { tried.Add(ip); return true; }, networkAllowed: () => true);
        var r = probe.Probe("corp.example", CancellationToken.None);
        Assert.Equal(DcProbeOutcome.Reachable, r.Outcome);
        Assert.Equal([IPAddress.Parse("10.0.0.10")], tried);

        var down = new DomainControllerProbe(locate: _ => ("dc1", [IPAddress.Parse("10.0.0.10")]), connect: (_, _) => false, networkAllowed: () => true);
        Assert.Equal(DcProbeOutcome.Unreachable, down.Probe("corp.example", CancellationToken.None).Outcome);

        var none = new DomainControllerProbe(locate: _ => null, connect: (_, _) => true, networkAllowed: () => true);
        Assert.Equal(DcProbeOutcome.NotResolved, none.Probe("corp.example", CancellationToken.None).Outcome);
    }

    [Fact]
    public void The_probe_sends_nothing_when_the_network_gate_refuses()
    {
        LogAnalyzer.Core.Services.Connectivity.AppModeContext.ResetForTests();   // default: AirGapped
        bool located = false, connected = false;
        var probe = new DomainControllerProbe(locate: _ => { located = true; return ("dc1", [IPAddress.Parse("10.0.0.10")]); }, connect: (_, _) => connected = true);
        var r = probe.Probe("corp.example", CancellationToken.None);
        Assert.Equal(DcProbeOutcome.Skipped, r.Outcome);
        Assert.False(located);
        Assert.False(connected);
    }
}
