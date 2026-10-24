using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Finding B2: a control run without administrator rights reads NEDETERMINAT, with the rights text, for A05 and U01-U04.</summary>
public class LowFindingsB2Tests
{
    private static StationFacts Facts(bool admin, bool securityReadable) => new()
    {
        Host = "LAB-PC", CollectedUtc = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero), IsAdministrator = admin,
        PeriodStartUtc = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero), PeriodEndUtc = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero),
        Coverage = { new ChannelCoverage("Security", null, 0, securityReadable, securityReadable ? "" : "Acces refuzat."), new ChannelCoverage("System", null, 10, true, "") },
    };

    private static readonly string[] Ids = ["A05", "U01", "U02", "U03", "U04"];

    [Fact]
    public void Not_elevated_the_security_log_checks_say_the_result_needs_administrator_rights_and_are_recognised_as_the_rights_limitation()
    {
        var r = ControlEvaluator.Evaluate(Facts(admin: false, securityReadable: false));
        foreach (var id in Ids)
        {
            var c = r.Checks.Single(x => x.Id == id);
            Assert.Equal(ControlStatus.Nedeterminat, c.Status);
            Assert.Contains("necesită drepturi de administrator", c.Detail);
            Assert.DoesNotContain("Nicio ", c.Detail);
            Assert.True(ControlConclusion.UndeterminedForLackOfRights(c), id);
        }
        var limitation = ControlConclusion.Build(r).NotChecked.First();
        foreach (var id in Ids) Assert.Contains(id, limitation);
    }

    [Fact]
    public void Elevated_the_real_results_are_kept()
    {
        var r = ControlEvaluator.Evaluate(Facts(admin: true, securityReadable: true));
        foreach (var id in Ids)
        {
            var c = r.Checks.Single(x => x.Id == id);
            Assert.Equal(ControlStatus.Conform, c.Status);
            Assert.DoesNotContain("necesită drepturi de administrator", c.Detail);
        }
        Assert.Contains("Nicio ștergere de jurnal", r.Checks.Single(x => x.Id == "A05").Detail);
    }

    [Fact]
    public void Not_elevated_but_readable_log_keeps_its_result()
    {
        var r = ControlEvaluator.Evaluate(Facts(admin: false, securityReadable: true));
        Assert.Equal(ControlStatus.Conform, r.Checks.Single(x => x.Id == "A05").Status);
    }
}
