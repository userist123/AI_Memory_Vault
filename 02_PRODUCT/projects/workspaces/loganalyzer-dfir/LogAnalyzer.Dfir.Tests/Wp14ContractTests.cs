using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Verification;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Every WP14a rule is in the contract catalog, makes no stronger claim than its evidence, and passes the WP4 sufficiency check.</summary>
public class Wp14ContractTests
{
    private static readonly string[] AllRules =
    [
        "MEDIA-AUTHORIZED", "MEDIA-REGISTERED", "MEDIA-UNREGISTERED", "MEDIA-UNAUTHORIZED", "MEDIA-UNKNOWN", "MEDIA-FILE-ACTIVITY", "MEDIA-OPTICAL-ACTIVITY",
        "AIRGAP-NETWORK-CONNECTED", "AIRGAP-WIFI-ASSOCIATED", "AIRGAP-BLUETOOTH-PAIRED", "AIRGAP-DHCP-LEASE", "AIRGAP-NIC-ADDED",
    ];

    /// <summary>A timeline in which every rule fires at least once (classified, air-gapped scope; register defined).</summary>
    private static List<TimelineEvent> Everything() =>
    [
        Usb("OK1", 0, letter: "E:"), Usb("AUTH1", 1), Usb("WD1", 2), Usb("REG1", 3), Usb("NOREG", 3.5), Usb("", 4, label: "Stick fără serie"),
        Sec(4663, 5, ("ObjectName", @"E:\a.docx"), ("AccessMask", "0x2")),
        Ev("Application", 1, 6, "IMAPI2"),
        Ev("Microsoft-Windows-NetworkProfile/Operational", 10000, 7, "Microsoft-Windows-NetworkProfile", ("Name", "Rețea-X")),
        Ev("Microsoft-Windows-WLAN-AutoConfig/Operational", 8001, 8, "Microsoft-Windows-WLAN-AutoConfig", ("SSID", "Hotspot")),
        Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, 9, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"BTHENUM\DEV_001A7DDA7113\7&1")),
        Ev("Microsoft-Windows-Dhcp-Client/Operational", 50036, 10, "Microsoft-Windows-Dhcp-Client", ("IPAddress", "192.168.1.9")),
        Ev("Microsoft-Windows-Kernel-PnP/Configuration", 400, 11, "Microsoft-Windows-Kernel-PnP", ("DeviceInstanceId", @"PCI\VEN_8086&DEV_15BB\3&1"), ("ClassGuid", "{4d36e972-e325-11ce-bfc1-08002be10318}")),
    ];

    private static List<Finding> Enriched()
    {
        var media = W14.Media(Row("AUTH1"), Row("WD1", status: "withdrawn"), Row("REG1", user: @"CORP\ion"), Row("OK1"));
        return FindingContract.Enrich(Go(Everything(), Classified(), media)).ToList();
    }

    [Fact]
    public void Every_WP14a_rule_fires_in_the_everything_timeline()
    {
        var ids = Enriched().Select(f => f.RuleId).ToHashSet();
        foreach (var r in AllRules) Assert.Contains(r, ids);
    }

    [Fact]
    public void Every_WP14a_rule_has_a_complete_catalog_entry()
    {
        foreach (var id in AllRules)
        {
            var c = RuleContracts.For(id);
            Assert.NotNull(c);
            Assert.EndsWith("Wp14Rules", c!.Producer);
            Assert.NotEmpty(c.Limitations); Assert.NotEmpty(c.MissingEvidence); Assert.NotEmpty(c.NextSteps); Assert.True(c.Meaning.Length > 20);
        }
    }

    [Fact]
    public void Findings_carry_contract_fields_and_no_anti_overclaim_violation()
    {
        foreach (var f in Enriched().Where(f => AllRules.Contains(f.RuleId)))
        {
            Assert.True(f.HasSemanticType, f.RuleId);
            Assert.NotEmpty(f.Limitations); Assert.NotEmpty(f.AlternativeExplanations); Assert.NotEmpty(f.MissingEvidence); Assert.NotEmpty(f.RecommendedNextSteps);
            Assert.NotEmpty(f.SupportingEvidence); Assert.NotEmpty(f.ClassificationReason);
            Assert.Empty(AntiOverclaim.Violations(f));
            Assert.Contains(f.Limitations, l => l.Contains("NOT_ASSESSED"));
            Assert.NotNull(f.AirGap);
        }
    }

    [Fact]
    public void The_declared_semantic_type_is_supported_by_the_cited_artifacts_per_the_WP4_sufficiency_check()
    {
        var events = Everything();
        var media = W14.Media(Row("AUTH1"), Row("WD1", status: "withdrawn"), Row("REG1", user: @"CORP\ion"), Row("OK1"));
        foreach (var f in Go(events, Classified(), media))
        {
            var refs = f.SupportingEvidence.Select(r =>
            {
                var e = events.First(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator);
                var row = new TimelineRow(e.Time.Utc, e.TimeSemantics, e.Source, e.EventId, e.Path, e.Process, e.Summary, e.EvidenceId, e.Locator, "");
                var kind = e.Source.StartsWith("EventLog:", StringComparison.Ordinal) && e.EventId.Length > 0 ? $"{e.Source} {e.EventId}" : e.Source;
                return new ResolvedRef(r, null, [row], e.Source, kind, true);
            }).ToList();
            var res = Checks.Sufficiency(f, refs);
            Assert.True(res.Outcome != CheckOutcome.Fail, $"{f.RuleId} ({f.SemanticType.ToSpec()}): {res.Reason}");
        }
    }

    [Fact]
    public void No_WP14a_rule_is_above_High_and_none_is_Critical_and_the_unclassified_scope_is_one_level_lower()
    {
        var media = W14.Media(Row("AUTH1"), Row("WD1", status: "withdrawn"), Row("REG1", user: @"CORP\ion"), Row("OK1"));
        var hi = Go(Everything(), Classified(), media);
        var lo = Go(Everything(), Unclassified(), media);
        Assert.DoesNotContain(hi, f => f.Severity >= Severity.Critical);
        Assert.Equal(hi.Count, lo.Count);
        foreach (var (a, b) in hi.Zip(lo))
            Assert.True(b.Severity == a.Severity || b.Severity == a.Severity - 1 || a.RuleId == "MEDIA-FILE-ACTIVITY", $"{a.RuleId}: {a.Severity} -> {b.Severity}");
    }
}
