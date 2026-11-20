using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Privacy;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #11 A3: the "Greșeli găsite" section opens the report; each block lets the owner verify on paper. Synthetic data only.</summary>
public sealed class MistakesSectionTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { InvestigationReportPdf.TextObserver.Value = null; _k.Dispose(); }

    private static readonly string[] Labels = ["Ce s-a întâmplat:", "Când:", "Unde:", "Dovada:", "Cum verifici tu:", "Ce NU dovedește:", "Siguranță:"];

    private static Finding F(string id, Severity sev, string title, int minute, Classification cls = Classification.Candidate, StandardState state = StandardState.NotAssessed,
                             bool alternatives = true, string user = "ana.pop") => new()
    {
        FindingId = id, RuleId = "TEST", Title = title, Description = "d " + title, Severity = sev, Classification = cls, Confidence = Confidence.Medium, User = user, Host = "WS-ALFA-07",
        FirstSeenUtc = new DateTimeOffset(2026, 11, 6, 13, minute, 0, TimeSpan.Zero), LastSeenUtc = new DateTimeOffset(2026, 11, 6, 13, minute, 0, TimeSpan.Zero),
        SupportingEvidence = [new EvidenceRef("E-0001", "EventRecordID=" + (4000 + minute), "x", "ab", "TargetUserName=" + user, "EVTX TimeCreated", "Security")],
        AlternativeExplanations = alternatives ? ["Poate fi o acțiune administrativă planificată."] : [],
        Verification = new FindingVerification(state, "test"),
    };

    private string Print(IEnumerable<Finding> findings, PiiMasker? masker = null)
    {
        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        r.Findings.AddRange(findings);
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, Guid.NewGuid().ToString("N") + ".pdf"), "investigator", masker: masker);
        return text;
    }

    private static string Section(string text)
    {
        int start = text.IndexOf(". Greșeli găsite\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "section missing");
        var next = Regex.Match(text[(start + 5)..], @"\n\d+\. ");
        return next.Success ? text.Substring(start, next.Index + 5) : text[start..];
    }

    [Fact]
    public void Section_lists_only_qualifying_findings_in_severity_order_with_all_seven_labels_and_comes_before_the_findings_list()
    {
        var text = Print([
            F("F-0001", Severity.Medium, "Logon medie", 5),
            F("F-0002", Severity.Info, "Doar informativ", 1),
            F("F-0003", Severity.Critical, "Logon critic", 20, alternatives: false),
            F("F-0004", Severity.High, "Inofensiv dupa verificare", 2, state: StandardState.Contradicted)]);
        var sec = Section(text);
        Assert.Contains("Logon critic", sec);
        Assert.Contains("Logon medie", sec);
        Assert.DoesNotContain("Doar informativ", sec);
        Assert.DoesNotContain("Inofensiv dupa verificare", sec);
        Assert.True(sec.IndexOf("Logon critic", StringComparison.Ordinal) < sec.IndexOf("Logon medie", StringComparison.Ordinal));
        foreach (var l in Labels) Assert.Equal(2, Regex.Matches(sec, "(?m)^" + Regex.Escape(l)).Count);
        Assert.Contains("Citat: „TargetUserName=ana.pop”", sec);
        Assert.Contains("ora din: EVTX TimeCreated", sec);
        Assert.Contains("1. ", sec[sec.IndexOf("Cum verifici tu:", StringComparison.Ordinal)..]);
        Assert.True(text.IndexOf(". Greșeli găsite\n", StringComparison.Ordinal) < text.IndexOf(". Constatări\n", StringComparison.Ordinal));
        // the finding without alternatives prints the fallback, the one with them prints its alternative
        Assert.Contains(InvestigationReportText.NoAlternatives, sec);
        Assert.Contains("acțiune administrativă planificată", sec);
        Assert.Contains("Greșeli găsite: 2 (vezi secțiunea „Greșeli găsite”)", text);
    }

    [Fact]
    public void Without_qualifying_findings_the_absence_sentence_is_printed_and_the_count_is_zero()
    {
        var text = Print([F("F-0001", Severity.Info, "Doar informativ", 1)]);
        Assert.Contains(InvestigationReportText.MistakesNone, Section(text));
        Assert.Contains("Greșeli găsite: 0 (vezi secțiunea „Greșeli găsite”)", text);
    }

    [Fact]
    public void Same_severity_is_ordered_by_first_seen()
    {
        var text = Print([F("F-0001", Severity.High, "Mai tarziu", 30), F("F-0002", Severity.High, "Mai devreme", 3)]);
        var sec = Section(text);
        Assert.True(sec.IndexOf("Mai devreme", StringComparison.Ordinal) < sec.IndexOf("Mai tarziu", StringComparison.Ordinal));
    }

    [Fact]
    public void The_section_is_capped_and_says_how_many_more_exist()
    {
        var text = Print(Enumerable.Range(1, 53).Select(i => F($"F-{i:0000}", Severity.High, "Problema numarul " + i, i % 50)));
        var sec = Section(text);
        Assert.Equal(50, Regex.Matches(sec, "(?m)^Siguranță:").Count);
        Assert.Contains("Încă 3 greșeli nu sunt afișate aici", sec);
        Assert.Contains("Greșeli găsite: 53 (", text);
    }

    [Fact]
    public void Masked_export_prints_no_original_user_or_host_in_the_section()
    {
        var findings = new[] { F("F-0001", Severity.High, "Logon pe WS-ALFA-07 al lui ana.pop", 5) };
        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        r.Timeline.Add(GapsTestKit.Ev("EventLog:Security", 0, "4624", path: "", user: "", f: [("TargetUserName", "ana.pop"), ("WorkstationName", "WS-ALFA-07")]));
        var m = PiiMasker.FromTimeline(r.Timeline, r.Case.Info.Host, r.Case.Info.User);
        var text = Print(findings, m);
        var sec = Section(text);
        Assert.DoesNotContain("ana.pop", sec, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WS-ALFA-07", sec, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Citat:", sec);
    }
}
