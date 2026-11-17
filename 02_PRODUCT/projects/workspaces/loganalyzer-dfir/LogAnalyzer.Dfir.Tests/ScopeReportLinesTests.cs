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

    [Fact]
    public void A_period_that_ends_before_the_collection_day_is_a_warning_and_one_that_covers_it_is_not()
    {
        // Lab WS02 2026-11-17: a reused scope 01–09.11 for a collection on 17.11; period-aware rules skipped everything after the 9th silently.
        var today = new DateTime(2026, 11, 17, 8, 7, 0, DateTimeKind.Utc);
        var w = ScopeForm.PeriodEndsBeforeWarning(new DateTime(2026, 11, 9), today);
        Assert.NotNull(w);
        Assert.Contains("09.11.2026", w);
        Assert.Contains("17.11.2026", w);
        Assert.Null(ScopeForm.PeriodEndsBeforeWarning(new DateTime(2026, 11, 17), today));   // the collection day itself is covered
        Assert.Null(ScopeForm.PeriodEndsBeforeWarning(new DateTime(2026, 12, 31), today));
        Assert.Null(ScopeForm.PeriodEndsBeforeWarning((DateTime?)null, today));              // open period: nothing to warn about

        // a saved scope ends on the last tick of its day (TryBuild): the same rule
        var form = new ScopeForm { PeriodFrom = new DateTime(2026, 11, 1), PeriodTo = new DateTime(2026, 11, 9) };
        form.TryBuild(out var scope, out _);
        Assert.NotNull(ScopeForm.PeriodEndsBeforeWarning(scope, new DateTimeOffset(today)));
        Assert.Null(ScopeForm.PeriodEndsBeforeWarning(scope, new DateTimeOffset(2026, 11, 9, 23, 0, 0, TimeSpan.Zero)));
        Assert.Null(ScopeForm.PeriodEndsBeforeWarning((CaseScope?)null, new DateTimeOffset(today)));
    }
}
