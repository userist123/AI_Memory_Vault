using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Evaluation 2026-11-05: the investigation PDF printed no case scope (purpose, approver, legal basis, period, category, act).</summary>
public class ScopeReportLinesTests
{
    [Fact]
    public void A_confirmed_scope_prints_every_field()
    {
        var lines = ScopeForm.ReportLines(TestScopes.Valid());
        Assert.StartsWith("Scopul cazului: confirmat", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("Scop: ") && !l.Contains("nespecificat"));
        Assert.Contains(lines, l => l.StartsWith("Aprobator: ") && !l.Contains("nespecificat"));
        Assert.Contains(lines, l => l.StartsWith("Perioada: ") && !l.Contains("nespecificat"));
        Assert.Contains(lines, l => l.StartsWith("Temei: ") && !l.Contains("nespecificat"));
        Assert.Contains(lines, l => l.StartsWith("Actul care autorizează: "));
    }

    [Fact]
    public void A_provisional_or_incomplete_scope_says_so_and_names_what_is_missing()
    {
        var lines = ScopeForm.ReportLines(new CaseScope { Purpose = "x", Provisional = true });
        Assert.StartsWith("Scopul cazului: PROVIZORIU, NECONFIRMAT", lines[0]);
        Assert.Contains("approver", lines[0]);
        Assert.Contains(lines, l => l == "Aprobator: nespecificat");
        Assert.Contains(lines, l => l == "Actul care autorizează: nespecificat");
    }

    [Fact]
    public void A_case_without_a_scope_says_it_has_none() =>
        Assert.StartsWith("Scopul cazului: lipsește", Assert.Single(ScopeForm.ReportLines(null)));
}
