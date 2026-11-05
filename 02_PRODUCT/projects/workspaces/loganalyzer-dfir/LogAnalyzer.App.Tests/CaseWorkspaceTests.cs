using System;
using System.IO;
using System.Linq;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Evaluation 2026-11-05: the advanced pages showed 16 161 events, 0 alerts and „niciun atac” next to a case with 67 123 events and a critical
/// incident chain, because they read a working database the case never filled. Now the open case is their source.
/// </summary>
public sealed class CaseWorkspaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_casews_" + Guid.NewGuid().ToString("N"));
    public CaseWorkspaceTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static CaseScope Scope() => new()
    {
        Purpose = "test", PeriodFromUtc = DateTimeOffset.UtcNow.AddDays(-1), PeriodToUtc = DateTimeOffset.UtcNow.AddDays(1), SystemsInScope = ["H"], Approver = "a",
        LegalBasis = LegalBasis.Incident, Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
    };

    private static Finding F(string id, Severity s, string mitre = "T1059.001") =>
        new() { FindingId = id, RuleId = "R", Title = "titlu " + id, Description = "descriere " + id, Severity = s, MitreTechniqueId = mitre, FirstSeenUtc = DateTimeOffset.UtcNow };

    [Fact]
    public void The_alerts_are_the_case_findings_with_the_same_ids_and_severities()
    {
        var issues = MainViewModel.IssuesFromFindings([F("F-0001", Severity.Critical), F("F-0002", Severity.High), F("F-0003", Severity.Info, "")]);
        Assert.Equal(["F-0001 · titlu F-0001", "F-0002 · titlu F-0002", "F-0003 · titlu F-0003"], issues.Select(i => i.Title));
        var d = SeverityDistribution.Count(issues);
        Assert.Equal((1, 1, 0, 1, 0), (d.Critical, d.High, d.Medium, d.LowOrInfo, d.Other));
        Assert.Equal("descriere F-0001", issues[0].Explanation);
        Assert.Equal("T1059.001", issues[0].MitreTechniqueId);
        Assert.Equal("", issues[2].MitreTechniqueId);
    }

    [Fact]
    public void Only_the_case_own_evtx_copies_are_read()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "ws", Scope());
        var evtx = Path.Combine(ws.RawDir("ws"), "Security.evtx");
        File.WriteAllBytes(evtx, [1, 2, 3]);
        ws.RegisterStored(evtx, @"C:\Windows\System32\winevt\Logs\Security.evtx", "ws", "evtx", TemporalType.Historical, "col", "1");
        var txt = Path.Combine(ws.RawDir("ws"), "note.txt");
        File.WriteAllText(txt, "x");
        ws.RegisterStored(txt, @"C:\note.txt", "ws", "txt", TemporalType.Historical, "col", "1");
        var files = MainViewModel.CaseEvtxFiles(ws);
        Assert.Equal([Path.GetFullPath(evtx)], files.Select(Path.GetFullPath));
    }

    [Fact]
    public void The_source_line_names_the_case_and_its_counts()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "nume caz", Scope());
        var r = new InvestigationResult { Case = ws };
        r.Findings.Add(F("F-0001", Severity.High));
        var line = MainViewModel.CaseSourceLine(r, 67123, 0);
        Assert.Contains(ws.Info.CaseId, line);
        Assert.Contains("„nume caz”", line);
        Assert.Contains("67.123", line.Replace("\u00a0", ".").Replace(",", "."));
        Assert.Contains("1 constatări", line);
        Assert.Contains("2 fișiere EVTX nu au putut", MainViewModel.CaseSourceLine(r, 1, 2));
    }

    [Fact]
    public void Conturi_locale_si_USB_is_a_working_data_page() => Assert.Contains(8, MainViewModel.LegacyDataTabs);
}
