using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #9 item 1 (MRK): the operator's classification marking is part of the scope and is printed on every page of the investigation, checks and cross-station reports.</summary>
public sealed class MarkingTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { ReportFooter.Observer.Value = null; InvestigationReportPdf.TextObserver.Value = null; _k.Dispose(); }

    private CaseWorkspace CaseWith(string marking) =>
        InvestigationPipeline.NewCase(Path.Combine(_k.Dir, "cases", Guid.NewGuid().ToString("N")[..6]), "caz marcaj",
            new CaseScope
            {
                Purpose = "test", PeriodFromUtc = DateTimeOffset.UtcNow.AddDays(-1), PeriodToUtc = DateTimeOffset.UtcNow.AddDays(1), SystemsInScope = ["H"], Approver = "a",
                LegalBasis = LegalBasis.Incident, Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified, Marking = marking,
            });

    private static List<(string Kind, string Text)> Capture() { var l = new List<(string, string)>(); ReportFooter.Observer.Value = (k, t) => { lock (l) l.Add((k, t)); }; return l; }

    [Fact]
    public void Investigation_pdf_footer_carries_the_entered_marking_and_the_header_says_the_case_is_open()
    {
        var ws = CaseWith("SECRET DE SERVICIU");
        var footers = Capture();
        var header = new List<string>();
        InvestigationReportPdf.TextObserver.Value = header.Add;   // the observer gets the whole printed text, one string, lines joined by 

        InvestigationReportPdf.Write(new InvestigationResult { Case = ws }, Path.Combine(_k.Dir, "m.pdf"), "investigator");
        var footer = Assert.Single(footers.Where(f => f.Kind == "investigation").Select(f => f.Text).Distinct());
        Assert.StartsWith("Marcaj: SECRET DE SERVICIU (" + MarkingText.Note + ")", footer);
        Assert.StartsWith("Marcaj: SECRET DE SERVICIU", header[0]);
        Assert.Contains("Document în lucru — cazul nu este închis", header[0]);
    }

    [Fact]
    public void Investigation_pdf_without_marking_says_so_and_a_closed_case_is_no_longer_a_working_document()
    {
        var ws = CaseWith("");
        var footers = Capture();
        var header = new List<string>();
        InvestigationReportPdf.TextObserver.Value = header.Add;   // the observer gets the whole printed text, one string, lines joined by 

        CaseClosure.Close(ws, "op");
        InvestigationReportPdf.Write(new InvestigationResult { Case = ws }, Path.Combine(_k.Dir, "n.pdf"), "investigator");
        var footer = Assert.Single(footers.Where(f => f.Kind == "investigation").Select(f => f.Text).Distinct());
        Assert.Contains("fără marcaj introdus", footer);
        Assert.StartsWith("Marcaj: fără marcaj introdus (marcaj introdus manual, neverificat de aplicație)", header[0]);
        Assert.DoesNotContain("Document în lucru", header[0]);
    }

    [Fact]
    public void Checks_and_cross_station_footers_carry_the_marking_or_the_no_marking_line()
    {
        var footers = Capture();
        ChecksReportPdf.Write(Path.Combine(_k.Dir, "c1.pdf"), "t", "s", (ReportSeal?)null, "CONFIDENȚIAL", [], []);
        ChecksReportPdf.Write(Path.Combine(_k.Dir, "c2.pdf"), "t", "s", (ReportSeal?)null, [], []);
        var settings = new CrossStationSettings { FromUtc = DateTimeOffset.UtcNow.AddDays(-7), ToUtc = DateTimeOffset.UtcNow };
        CrossStationReportPdf.Generate(new CrossStationResult(), settings, "STATIE", "registru: niciunul", DateTimeOffset.UtcNow, "SECRET");
        CrossStationReportPdf.Generate(new CrossStationResult(), settings, "STATIE", "registru: niciunul", DateTimeOffset.UtcNow);
        var checks = footers.Where(f => f.Kind == "checks").Select(f => f.Text).ToList();
        Assert.Contains(checks, t => t.StartsWith("Marcaj: CONFIDENȚIAL (", StringComparison.Ordinal));
        Assert.Contains(checks, t => t.StartsWith("Marcaj: fără marcaj introdus (", StringComparison.Ordinal));
        var cross = footers.Where(f => f.Kind == "confruntare").Select(f => f.Text).ToList();
        Assert.Contains(cross, t => t.StartsWith("Marcaj: SECRET (", StringComparison.Ordinal));
        Assert.Contains(cross, t => t.StartsWith("Marcaj: fără marcaj introdus (", StringComparison.Ordinal));
    }

    [Fact]
    public void A_scope_json_without_Marking_loads_with_an_empty_marking_and_a_stored_one_round_trips()
    {
        var old = Path.Combine(_k.Dir, "old.json");
        File.WriteAllText(old, "{\"Purpose\":\"x\",\"Approver\":\"y\"}");
        Assert.Equal("", Json.Read<CaseScope>(old).Marking);
        var f = Path.Combine(_k.Dir, "new.json");
        Json.Write(f, new CaseScope { Marking = "SECRET" });
        Assert.Equal("SECRET", Json.Read<CaseScope>(f).Marking);
    }

    [Fact]
    public void ScopeForm_rejects_an_unclassified_word_as_marking_trims_a_real_one_and_keeps_it_through_FromScope()
    {
        var form = ScopeForm.FromScope(CaseWith("").Info.Scope);
        form.Marking = "NESECRET";
        Assert.False(form.TryBuild(out _, out var missing));
        Assert.Contains(missing, m => m.Contains("NESECRET") && m.Contains("nu este un nivel de clasificare"));
        form.Marking = "  SECRET  ";
        Assert.True(form.TryBuild(out var scope, out _));
        Assert.Equal("SECRET", scope.Marking);
        Assert.Equal("SECRET", ScopeForm.FromScope(scope).Marking);
        form.Marking = "";
        Assert.True(form.TryBuild(out var none, out _));
        Assert.Equal("", none.Marking);
    }
}
