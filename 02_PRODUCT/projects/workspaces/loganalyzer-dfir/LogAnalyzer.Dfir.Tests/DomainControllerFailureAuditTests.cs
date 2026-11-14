using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// A19 (LA-Lab 2026-11-07): DC01 audited only Kerberos successes, so wrong passwords for a domain account left no trace on any computer and the
/// password guessing could not be found. The station control says so on a domain controller, and only there.
/// </summary>
public sealed class DomainControllerFailureAuditTests
{
    private static StationFacts Station(string productType, Dictionary<string, AuditSetting>? audit, StationPosture posture = StationPosture.IsolatedNetwork)
    {
        var f = new StationFacts { Posture = posture, IsAdministrator = true, AuditPolicy = audit };
        f.Settings["OS.ProductType"] = productType;
        return f;
    }

    private static ControlCheck? A19(StationFacts f) => ControlEvaluator.Evaluate(f).Checks.SingleOrDefault(c => c.Id == "A19");

    [Fact]
    public void A_domain_controller_that_does_not_record_refused_passwords_is_non_conform_and_says_why_in_plain_words()
    {
        // the lab DC01 before the fix: Kerberos successes only
        var c = A19(Station("LanmanNT", new() { ["Kerberos Authentication Service"] = AuditSetting.Success, ["Credential Validation"] = AuditSetting.Success | AuditSetting.Failure }));
        Assert.NotNull(c);
        Assert.Equal(ControlStatus.Neconform, c.Status);
        Assert.Contains("Kerberos Authentication Service", c.Detail);
        Assert.DoesNotContain("Credential Validation (", c.Detail);
        Assert.Contains("nu rămâne nicio urmă", c.Detail);

        var none = A19(Station("LanmanNT", new() { ["Kerberos Authentication Service"] = AuditSetting.None, ["Credential Validation"] = AuditSetting.None }));   // nothing audited at all
        Assert.Equal(ControlStatus.Neconform, none!.Status);
        Assert.Contains("Credential Validation", none.Detail);
    }

    [Fact]
    public void A_domain_controller_that_records_both_is_conform_and_an_unreadable_policy_is_never_conform()
    {
        var ok = A19(Station("LanmanNT", new() { ["Kerberos Authentication Service"] = AuditSetting.Success | AuditSetting.Failure, ["Credential Validation"] = AuditSetting.Failure }));
        Assert.Equal(ControlStatus.Conform, ok!.Status);
        Assert.Equal(ControlStatus.Nedeterminat, A19(Station("LanmanNT", null))!.Status);
    }

    [Fact]
    public void Workstations_member_servers_and_an_unknown_role_get_no_A19_line()
    {
        foreach (var t in new[] { "WinNT", "ServerNT", StationFactCollector.Absent })
            Assert.Null(A19(Station(t, new())));
        // a role that could not be read: unknown, never silently "not a domain controller"
        Assert.Equal(ControlStatus.Nedeterminat, A19(Station("", new()))!.Status);
    }

    [Fact]
    public void A19_has_a_guide_and_an_applicability_row()
    {
        var g = CheckGuides.For("A19");
        Assert.Contains("controler de domeniu", g.What);
        var row = Assert.Single(ControlApplicability.All, e => e.Id == "A19");
        Assert.False(row.Standalone.Applies);
        Assert.True(row.IsolatedNetwork.Applies);
    }

    [Fact]
    public void Every_audit_subcategory_a_check_reads_is_in_the_list_the_station_query_reads()
    {
        // lab DC01 2026-11-08: Kerberos Authentication Service was missing from AuditPolicy.Subcategories, so A19 read it as "None" while auditpol said
        // "Success and Failure"; the synthetic tests above could not see that
        foreach (var name in ControlEvaluator.AuditSubcategoriesUsed())
            Assert.True(AuditPolicy.Subcategories.ContainsKey(name), $"„{name}” is read by a check but not queried by AuditPolicy.Query");
        Assert.Equal(new Guid("0cce9242-69ae-11d9-bed3-505054503030"), AuditPolicy.Subcategories["Kerberos Authentication Service"]);
    }

    [Fact]
    public void A_subcategory_that_was_not_read_is_unknown_never_non_conform()
    {
        var c = A19(Station("LanmanNT", new() { ["Credential Validation"] = AuditSetting.Success | AuditSetting.Failure }));
        Assert.Equal(ControlStatus.Nedeterminat, c!.Status);
        Assert.Contains("Kerberos Authentication Service", c.Detail);
    }
}