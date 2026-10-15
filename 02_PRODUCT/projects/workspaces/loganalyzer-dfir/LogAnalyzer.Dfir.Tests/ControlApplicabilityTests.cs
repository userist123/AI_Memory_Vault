using System.Net;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Station posture (Standalone / IsolatedNetwork / Internet): which control checks apply, the "Nu se aplica" state and the passive internet-path check N11.</summary>
public class ControlApplicabilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly TheoryData<StationPosture> Postures = new() { StationPosture.Standalone, StationPosture.IsolatedNetwork, StationPosture.Internet };

    private static StationFacts Facts(StationPosture posture)
    {
        var f = new StationFacts { Host = "ST-1", PeriodStartUtc = Start, PeriodEndUtc = Start.AddDays(30), CollectedUtc = Start.AddDays(30), IsAdministrator = true, Posture = posture };
        foreach (var ch in StationFactCollector.Queries.Keys) f.Coverage.Add(new ChannelCoverage(ch, Start.AddDays(-60), 100, true, ""));
        return f;
    }

    private static string RepoFile(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        throw new FileNotFoundException(relative);
    }

    [Theory, MemberData(nameof(Postures))]
    public void Every_check_the_evaluator_produces_has_an_explicit_entry_for_all_three_postures(StationPosture posture)
    {
        var ids = ControlEvaluator.Evaluate(Facts(posture)).Checks.Select(c => c.Id).Distinct().ToList();
        Assert.NotEmpty(ids);
        foreach (var id in ids)
        {
            Assert.True(ControlApplicability.TryGet(id, out var e), $"no applicability entry for {id}");
            foreach (var p in Enum.GetValues<StationPosture>())
                Assert.False(string.IsNullOrWhiteSpace(e.For(p).Expected), $"{id}/{p}: empty expected value");
        }
    }

    [Fact]
    public void Every_check_id_written_in_the_evaluator_source_has_an_entry()
    {
        var src = File.ReadAllText(RepoFile(Path.Combine("LogAnalyzer.Dfir.Windows", "Audit", "ControlEvaluator.cs")));
        var ids = Regex.Matches(src, "\"([ACDNPSU][0-9]{2})\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(ids.Count >= 79, "the source scan found too few ids: " + ids.Count);
        foreach (var id in ids) Assert.True(ControlApplicability.TryGet(id, out _), $"no applicability entry for {id}");
    }

    [Fact]
    public void Unknown_ids_have_no_silent_default()
    {
        Assert.Throws<InvalidOperationException>(() => ControlApplicability.Applies("Z99", StationPosture.Standalone));
    }

    [Theory, MemberData(nameof(Postures))]
    public void Checks_that_do_not_apply_are_reported_as_NuSeAplica_and_are_never_findings(StationPosture posture)
    {
        var r = ControlEvaluator.Evaluate(Facts(posture));
        foreach (var c in r.Checks)
        {
            bool applies = ControlApplicability.Applies(c.Id, posture);
            Assert.Equal(!applies, c.Status == ControlStatus.NuSeAplica);
            if (!applies) Assert.StartsWith("Nu se aplică", c.Detail);
        }
        Assert.Equal(r.Checks.Count(c => c.Status == ControlStatus.NuSeAplica), r.Count(ControlStatus.NuSeAplica));
        Assert.DoesNotContain(r.Checks, c => c.Status == ControlStatus.NuSeAplica && ControlApplicability.Applies(c.Id, posture));
        Assert.Equal("NU SE APLICĂ", ControlReportPdf.StatusText(ControlStatus.NuSeAplica));
    }

    [Fact]
    public void Isolation_rules_apply_to_Standalone_not_to_Internet_and_the_network_rules_are_in_between()
    {
        Assert.True(ControlApplicability.Applies("N01", StationPosture.Standalone));
        Assert.False(ControlApplicability.Applies("N01", StationPosture.IsolatedNetwork));
        Assert.False(ControlApplicability.Applies("N01", StationPosture.Internet));
        Assert.True(ControlApplicability.Applies("N11", StationPosture.Standalone));
        Assert.True(ControlApplicability.Applies("N11", StationPosture.IsolatedNetwork));
        Assert.False(ControlApplicability.Applies("N11", StationPosture.Internet));
        Assert.False(ControlApplicability.Applies("N12", StationPosture.Standalone));
        Assert.True(ControlApplicability.Applies("N12", StationPosture.IsolatedNetwork));
        Assert.False(ControlApplicability.Applies("N03", StationPosture.Internet));
        Assert.True(ControlApplicability.Applies("P01", StationPosture.Internet));      // hardening applies everywhere
    }

    [Fact]
    public void The_applicability_document_equals_the_code_table()
    {
        var doc = File.ReadAllLines(RepoFile(Path.Combine("docs", "dfir", "EDITIONS_AND_POSTURE.md")));
        var rows = doc.Where(l => Regex.IsMatch(l, @"^\| [ACDNPSU][0-9]{2} \|")).Select(l => l.Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray()).ToList();
        Assert.Equal(ControlApplicability.All.Count, rows.Count);
        foreach (var e in ControlApplicability.All)
        {
            var row = Assert.Single(rows, x => x[0] == e.Id);
            Assert.Equal(e.Name, row[1]);
            Assert.Equal(e.Standalone.Applies ? e.Standalone.Expected : "n/a", row[2]);
            Assert.Equal(e.IsolatedNetwork.Applies ? e.IsolatedNetwork.Expected : "n/a", row[3]);
            Assert.Equal(e.Internet.Applies ? e.Internet.Expected : "n/a", row[4]);
            if (!(e.Standalone.Applies && e.IsolatedNetwork.Applies && e.Internet.Applies)) Assert.False(string.IsNullOrWhiteSpace(e.Reason), e.Id + ": missing reason");
            Assert.Equal(e.Reason ?? "", row[5]);
        }
    }

    // ---- N11: passive check for a path to the internet ------------------------------------------------------------------------

    private static ControlCheck N11(StationPosture posture, params EgressPathFact[] paths)
    {
        var f = Facts(posture);
        f.EgressPaths = [.. paths];
        ControlMissingSourceTests.N11SourcesRead(f);   // every N11 source read: an unread one makes N11 NEDETERMINAT (ControlMissingSourceTests)
        return Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N11");
    }

    [Fact]
    public void N11_Standalone_any_path_is_non_conform_even_an_internal_gateway_or_an_inactive_radio()
    {
        Assert.Equal(ControlStatus.Conform, N11(StationPosture.Standalone).Status);
        Assert.Equal(ControlStatus.Neconform, N11(StationPosture.Standalone, new EgressPathFact("Gateway", "192.168.1.1 pe Ethernet", true, true)).Status);
        Assert.Equal(ControlStatus.Neconform, N11(StationPosture.Standalone, new EgressPathFact("Wi-Fi", "adaptor prezent, oprit", false, false)).Status);
        Assert.Equal(ControlStatus.Neconform, N11(StationPosture.Standalone, new EgressPathFact("Proxy", "WinHTTP: proxy.local:3128", null, true)).Status);
    }

    [Fact]
    public void N11_Network_accepts_internal_gateway_and_dns_and_flags_what_leads_outside()
    {
        Assert.Equal(ControlStatus.Conform, N11(StationPosture.IsolatedNetwork, new EgressPathFact("Gateway", "10.0.0.1", true, true), new EgressPathFact("Server DNS", "10.0.0.10", true, true)).Status);
        Assert.Equal(ControlStatus.Neconform, N11(StationPosture.IsolatedNetwork, new EgressPathFact("Server DNS", "8.8.8.8", false, true)).Status);
        Assert.Equal(ControlStatus.Neconform, N11(StationPosture.IsolatedNetwork, new EgressPathFact("Gateway", "203.0.113.1", false, false)).Status);
        Assert.Equal(ControlStatus.Neconform, N11(StationPosture.IsolatedNetwork, new EgressPathFact("Wi-Fi", "adaptor pornit", false, true)).Status);
        Assert.Equal(ControlStatus.Neconform, N11(StationPosture.IsolatedNetwork, new EgressPathFact("Proxy", "WinINet: 198.51.100.7:8080", false, true)).Status);
        Assert.Equal(ControlStatus.DeVerificat, N11(StationPosture.IsolatedNetwork, new EgressPathFact("Proxy", "WinINet: proxy.corp:8080", null, true)).Status);
        Assert.Equal(ControlStatus.DeVerificat, N11(StationPosture.IsolatedNetwork, new EgressPathFact("Wi-Fi", "adaptor prezent, oprit", false, false)).Status);
    }

    [Fact]
    public void N11_not_collected_is_undetermined_and_does_not_apply_to_Internet()
    {
        var f = Facts(StationPosture.Standalone);
        Assert.Equal(ControlStatus.Nedeterminat, Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N11").Status);
        Assert.Equal(ControlStatus.NuSeAplica, N11(StationPosture.Internet, new EgressPathFact("Gateway", "8.8.8.8", false, true)).Status);
    }

    [Fact]
    public void N11_reads_saved_wifi_profiles_ics_and_cellular_from_the_collected_settings()
    {
        var f = Facts(StationPosture.IsolatedNetwork);
        f.EgressPaths = [];
        f.Settings["ICS.Shared"] = "da: Ethernet";
        Assert.Equal(ControlStatus.Neconform, Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N11").Status);
        var g = Facts(StationPosture.Standalone);
        g.EgressPaths = [];
        g.Settings["Wlan.ProfileCount"] = "2";
        Assert.Equal(ControlStatus.Neconform, Assert.Single(ControlEvaluator.Evaluate(g).Checks, c => c.Id == "N11").Status);
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.0.9", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void Address_classes(string address, bool expectedInternal) => Assert.Equal(expectedInternal, EgressPathCollector.IsInternal(IPAddress.Parse(address)));

    [Theory]
    [InlineData("10.0.0.5:3128", true)]
    [InlineData("http=10.0.0.5:80;https=10.0.0.5:443", true)]
    [InlineData("203.0.113.9:8080", false)]
    [InlineData("http=10.0.0.5:80;https=203.0.113.9:443", false)]
    public void Proxy_strings_with_ip_literals_are_classified(string proxy, bool expectedInternal) => Assert.Equal(expectedInternal, EgressPathCollector.ProxyIsInternal(proxy));

    [Fact]
    public void Proxy_given_by_name_cannot_be_classified_without_resolving_it()
    {
        Assert.Null(EgressPathCollector.ProxyIsInternal("proxy.corp:8080"));
    }

    [Fact]
    public void WinHttp_blob_and_phonebook_parsing()
    {
        var proxy = System.Text.Encoding.ASCII.GetBytes("10.0.0.5:3128");
        var blob = new byte[16 + proxy.Length];
        blob[0] = 0x28; blob[8] = 0x03; BitConverter.GetBytes(proxy.Length).CopyTo(blob, 12); proxy.CopyTo(blob, 16);
        Assert.Equal("10.0.0.5:3128", EgressPathCollector.ParseWinHttpProxy(blob));
        blob[8] = 0x01;                                                    // direct access
        Assert.Null(EgressPathCollector.ParseWinHttpProxy(blob));
        Assert.Equal(["Office VPN", "Backup line"], EgressPathCollector.PhonebookEntries("[Office VPN]\r\nPhoneNumber=vpn.example\r\n\r\n[Backup line]\r\nDevice=modem\r\n"));
    }

    [Fact]
    public void N12_domain_membership_is_expected_only_on_the_isolated_network()
    {
        var f = Facts(StationPosture.IsolatedNetwork);
        f.Settings["Domain.PartOfDomain"] = "nu"; f.Settings["Domain.Name"] = "WORKGROUP";
        Assert.Equal(ControlStatus.Neconform, Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N12").Status);
        f.Settings["Domain.PartOfDomain"] = "da"; f.Settings["Domain.Name"] = "corp.example";
        Assert.Equal(ControlStatus.Conform, Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N12").Status);
        var s = Facts(StationPosture.Standalone);
        s.Settings["Domain.PartOfDomain"] = "nu";
        Assert.Equal(ControlStatus.NuSeAplica, Assert.Single(ControlEvaluator.Evaluate(s).Checks, c => c.Id == "N12").Status);
    }

    [Fact]
    public void Posture_policy_interaction_the_compiled_posture_wins()
    {
        Assert.Equal(AppMode.AirGapped, StationPostures.ClampMode(StationPosture.Standalone, AppMode.Network));
        Assert.Equal(AppMode.AirGapped, StationPostures.ClampMode(StationPosture.IsolatedNetwork, AppMode.AirGapped));
        Assert.Equal(AppMode.Network, StationPostures.ClampMode(StationPosture.Internet, AppMode.Network));
    }
}
