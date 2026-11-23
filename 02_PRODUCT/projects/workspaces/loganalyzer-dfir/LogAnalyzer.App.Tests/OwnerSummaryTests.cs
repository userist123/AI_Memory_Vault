using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Privacy;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #14 E1: page one lists every mistake of medium severity or higher with what to do; the last annex is a printable checklist. Synthetic data only.</summary>
public sealed class OwnerSummaryTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { InvestigationReportPdf.TextObserver.Value = null; _k.Dispose(); }

    private static Finding F(string id, string rule, Severity sev, string title, int minute, params string[] ownSteps)
    {
        var f = new Finding
        {
            FindingId = id, RuleId = rule, Title = title, Description = "d " + title, Severity = sev, Classification = Classification.Candidate, Confidence = Confidence.Medium,
            Host = "WS-ALFA-07", FirstSeenUtc = new DateTimeOffset(2026, 11, 6, 13, minute % 60, 0, TimeSpan.Zero), LastSeenUtc = new DateTimeOffset(2026, 11, 6, 13, minute % 60, 0, TimeSpan.Zero),
            SupportingEvidence = [new EvidenceRef("E-0001", "EventRecordID=" + (4000 + minute), "x", "ab", "Valoare=" + minute, "EVTX TimeCreated", "Security")],
            Verification = new FindingVerification(StandardState.NotAssessed, "test"),
        };
        f.RecommendedNextSteps.AddRange(ownSteps);
        return f;
    }

    private string Print(IEnumerable<Finding> findings, PiiMasker? masker = null)
    {
        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        r.Findings.AddRange(findings);
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, Guid.NewGuid().ToString("N") + ".pdf"), "investigator", masker: masker);
        return text;
    }

    private static string Box(string text)
    {
        int a = text.IndexOf("Rezumat pentru proprietar\n", StringComparison.Ordinal);
        Assert.True(a >= 0, "box missing");
        int b = text.IndexOf("Greșeli găsite: ", a, StringComparison.Ordinal);
        return text[a..b];
    }

    private static string Annex(string text)
    {
        int a = text.IndexOf(". Listă de verificare (Cum verificați)\n", StringComparison.Ordinal);
        Assert.True(a >= 0, "annex missing");
        return text[a..];
    }

    private static Finding[] ThreeMedium() =>
    [
        F("F-0001", "CFG-UAC-NOPROMPT", Severity.Medium, "UAC nu cere confirmare", 1),
        F("F-0002", "CFG-DEFENDER-EXCL", Severity.Medium, "Excluderi Defender", 2),
        F("F-0003", "CFG-AUDIT-LOGON", Severity.Medium, "Auditul autentificarilor lipseste", 3),
    ];

    [Fact]
    public void With_no_high_finding_page_one_lists_all_three_medium_mistakes_each_with_its_ce_faci_sentence()
    {
        var text = Print(ThreeMedium());
        var box = Box(text);
        Assert.Contains("Medie · UAC nu cere confirmare → Ce faceți: Readuceți la nivelul implicit setarea Control cont utilizator", box);
        Assert.Contains("Medie · Excluderi Defender → Ce faceți: Notați (sau fotografiați) lista de excluderi, apoi ștergeți-le pe cele pe care nu le recunoașteți.", box);
        Assert.Contains("Medie · Auditul autentificarilor lipseste → Ce faceți: Porniți auditarea pentru autentificări", box);
        Assert.Equal(3, Regex.Matches(box, "→ Ce faceți:").Count);
        Assert.True(text.IndexOf("Rezumat pentru proprietar\n", StringComparison.Ordinal) < text.IndexOf(". Greșeli găsite\n", StringComparison.Ordinal));
        Assert.DoesNotContain("neconform", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_box_is_ordered_by_severity_and_capped_at_fifteen_with_a_count_of_the_rest()
    {
        var fs = Enumerable.Range(1, 18).Select(i => F($"F-{i:0000}", "CFG-SMB1", i == 18 ? Severity.High : Severity.Medium, "Problema " + i, i)).ToList();
        var box = Box(Print(fs));
        Assert.Equal(15, Regex.Matches(box, "→ Ce faceți:").Count);
        Assert.Contains("… și încă 3 în secțiunea Greșeli găsite", box);
        Assert.True(box.IndexOf("Problema 18", StringComparison.Ordinal) < box.IndexOf("Problema 1 ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_grave_mistake_is_listed_once_on_page_one_in_the_box_not_again_under_ce_este_grav()
    {
        var text = Print([F("F-0001", "CFG-SMB1", Severity.High, "Problema grava unica", 1), .. ThreeMedium()]);
        Assert.Equal(1, Regex.Count(text[..text.IndexOf(". Greșeli găsite\n", StringComparison.Ordinal)], "Problema grava unica"));
        Assert.Contains("Ridicată · Problema grava unica → Ce faceți:", Box(text));
    }

    [Fact]
    public void Low_findings_stay_out_of_the_box_and_the_annex()
    {
        var text = Print([.. ThreeMedium(), F("F-0009", "CFG-SMB1", Severity.Low, "Doar joasa", 9)]);
        Assert.DoesNotContain("Doar joasa", Box(text));
        Assert.DoesNotContain("Doar joasa", Annex(text));
    }

    [Fact]
    public void The_findings_own_first_recommended_step_wins_over_the_table()
    {
        var text = Print([F("F-0001", "CFG-UAC-NOPROMPT", Severity.Medium, "UAC", 1, "Pasul propriu al constatării.", "Al doilea pas.")]);
        var box = Box(text);
        Assert.Contains("→ Ce faceți: Pasul propriu al constatării.", box);
        Assert.DoesNotContain("Readuceți la nivelul implicit", box);
        Assert.DoesNotContain("Al doilea pas.", box);
        Assert.Contains("Ce faceți: Pasul propriu al constatării.", Annex(text));
    }

    [Fact]
    public void The_annex_closes_the_report_with_a_checkbox_locator_and_numbered_steps_per_mistake()
    {
        var annex = Annex(Print(ThreeMedium()));
        Assert.Equal(3, Regex.Matches(annex, "(?m)^☐ ").Count);
        Assert.Contains("☐ Medie · UAC nu cere confirmare", annex);
        Assert.Contains("  Unde: E-0001 · EventRecordID=4001", annex);
        Assert.Equal(3, Regex.Matches(annex, "(?m)^  Cum verificați:").Count);
        Assert.Matches(@"(?m)^  1\. ", annex);
        Assert.True(annex.IndexOf("☐ Medie · UAC", StringComparison.Ordinal) < annex.IndexOf("☐ Medie · Excluderi", StringComparison.Ordinal));
        Assert.Contains("Verificat de:", annex);
    }

    private static Finding Cfg(string id, string rule, Severity sev, string title, int minute)
    {
        var f = F(id, rule, sev, title, minute);
        f.SemanticType = SemanticType.Configuration;
        return f;
    }

    [Fact]
    public void G5_the_box_splits_settings_to_correct_from_traces_to_investigate_each_by_severity_with_its_count()
    {
        var box = Box(Print(
        [
            F("F-0001", "DEF-DETECTION", Severity.Medium, "Antivirusul a gasit un fisier", 1),
            Cfg("F-0002", "CFG-SMB1", Severity.Medium, "SMB1 pornit", 2),
            F("F-0003", "DEF-TAMPER", Severity.High, "Protectia Defender oprita", 3),
            Cfg("F-0004", "CFG-UAC-OFF", Severity.High, "UAC oprit", 4),
            Cfg("F-0005", "CFG-RDP-NLA", Severity.Medium, "RDP fara NLA", 5),
        ]));
        int settings = box.IndexOf("Setări de corectat (3)\n", StringComparison.Ordinal);
        int traces = box.IndexOf("Urme de investigat (2)\n", StringComparison.Ordinal);
        Assert.True(settings >= 0, "settings block missing");
        Assert.True(traces > settings, "traces block missing or before the settings block");
        var s = box[settings..traces];
        var t = box[traces..];
        Assert.Equal(3, Regex.Matches(s, "→ Ce faceți:").Count);
        Assert.Equal(2, Regex.Matches(t, "→ Ce faceți:").Count);
        Assert.Contains("Ridicată · UAC oprit", s);
        Assert.Contains("Medie · Antivirusul a gasit un fisier", t);
        Assert.DoesNotContain("Defender", s);
        Assert.DoesNotContain("SMB1", t);
        Assert.True(s.IndexOf("UAC oprit", StringComparison.Ordinal) < s.IndexOf("SMB1 pornit", StringComparison.Ordinal));
        Assert.True(t.IndexOf("Protectia Defender oprita", StringComparison.Ordinal) < t.IndexOf("Antivirusul a gasit", StringComparison.Ordinal));
    }

    [Fact]
    public void G5_the_fifteen_line_cap_is_overall_across_both_blocks_and_the_counts_are_totals()
    {
        var fs = Enumerable.Range(1, 10).Select(i => Cfg($"F-{i:0000}", "CFG-SMB1", Severity.High, "Setare " + i, i))
            .Concat(Enumerable.Range(11, 10).Select(i => F($"F-{i:0000}", "DEF-DETECTION", Severity.Medium, "Urma " + i, i))).ToList();
        var box = Box(Print(fs));
        Assert.Equal(15, Regex.Matches(box, "→ Ce faceți:").Count);
        Assert.Contains("Setări de corectat (10)\n", box);
        Assert.Contains("Urme de investigat (10)\n", box);
        Assert.Contains("… și încă 5 în secțiunea Greșeli găsite", box);
    }

    [Fact]
    public void Without_mistakes_neither_the_box_nor_the_annex_is_printed()
    {
        var none = Print([F("F-0001", "CFG-SMB1", Severity.Info, "Informativ", 1)]);
        Assert.DoesNotContain("Rezumat pentru proprietar", none);
        Assert.DoesNotContain("Listă de verificare (Cum verificați)", none);
    }

    [Fact]
    public void The_annex_and_the_box_are_masked_like_the_rest_of_the_report()
    {
        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        r.Timeline.Add(GapsTestKit.Ev("EventLog:Security", 0, "4624", path: "", user: "", f: [("TargetUserName", "ana.pop"), ("WorkstationName", "WS-ALFA-07")]));
        var m = PiiMasker.FromTimeline(r.Timeline, r.Case.Info.Host, r.Case.Info.User);
        var text = Print([F("F-0001", "CFG-SMB1", Severity.Medium, "SMB1 pe WS-ALFA-07 al lui ana.pop", 5)], m);
        Assert.DoesNotContain("ana.pop", Box(text), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WS-ALFA-07", Annex(text), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ana.pop", Annex(text), StringComparison.OrdinalIgnoreCase);
    }
}
