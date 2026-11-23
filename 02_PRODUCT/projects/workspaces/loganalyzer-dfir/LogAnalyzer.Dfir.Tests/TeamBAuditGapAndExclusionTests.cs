using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>G3 (audit gaps quote the auditpol row) and G2 (Defender exclusion linked to its 5007 and to the detections in that folder), on synthetic rows.</summary>
public class TeamBAuditGapAndExclusionTests
{
    private static readonly DateTime T0 = new(2026, 9, 19, 15, 0, 0, DateTimeKind.Utc);
    private const string GWfp = "0CCE9226-69AE-11D9-BED3-505054503030", GFs = "0CCE921D-69AE-11D9-BED3-505054503030";

    private static TimelineEvent Sec(int id, int n) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(n), "", "test"), Source = "EventLog:Security", EventId = id.ToString(), EvidenceId = "EV-SEC",
        Locator = $"EventRecordID={n}", Summary = "s",
    };

    private static TimelineEvent Auditpol(string name, string guid, string setting, string ok, string fail) => new()
    {
        Time = Timestamp.FromUtc(T0.AddDays(1), "", "test"), Source = "auditpol", EvidenceId = "EV-AP", Locator = $"auditpol:{guid}", Summary = "p",
        Fields = { ["Subcategory"] = name, ["SubcategoryGuid"] = guid, ["Setting"] = setting, ["AuditSuccess"] = ok, ["AuditFailure"] = fail,
                   ["RawLine"] = $"PC01,System,{name},{{{guid}}},{setting},," },
    };

    [Fact]
    public void A_No_Auditing_auditpol_row_is_quoted_in_the_gap_with_its_locator_and_4663_also_names_the_SACL()
    {
        var gaps = AuditCoverage.Gaps([Sec(4624, 1), Sec(4688, 2),
            Auditpol("Filtering Platform Connection", GWfp, "No Auditing", "nu", "nu"), Auditpol("File System", GFs, "No Auditing", "nu", "nu")]);
        var wfp = Assert.Single(gaps, g => g.Artifact.Contains("5156"));
        Assert.Contains($"auditpol:{GWfp}", wfp.Reason);
        Assert.Contains($"„PC01,System,Filtering Platform Connection,{{{GWfp}}},No Auditing,,”", wfp.Reason);
        Assert.DoesNotContain("cel mai probabil", wfp.Reason);
        var fs = Assert.Single(gaps, g => g.Artifact.Contains("4663"));
        Assert.Contains($"auditpol:{GFs}", fs.Reason);
        Assert.Contains("„File System” cu setarea „No Auditing”", fs.Reason);
        Assert.Contains("SACL", fs.Reason);
        Assert.DoesNotContain("SACL", wfp.Reason);
        Assert.Equal(EvidenceStatus.NotAvailable, fs.Status);
    }

    [Fact]
    public void Without_an_auditpol_row_or_with_auditing_on_the_gap_text_is_unchanged()
    {
        foreach (var extra in new[] { Array.Empty<TimelineEvent>(), [Auditpol("Filtering Platform Connection", GWfp, "Success", "da", "nu")] })
        {
            var gaps = AuditCoverage.Gaps([Sec(4624, 1), Sec(4688, 2), .. extra]);
            var wfp = Assert.Single(gaps, g => g.Artifact.Contains("5156"));
            Assert.EndsWith("nu conține niciun eveniment 5156/5157: subcategoria de audit era, cel mai probabil, dezactivată", wfp.Reason);
            Assert.DoesNotContain("auditpol:", wfp.Reason);
        }
    }

    // ---- G2 ----

    private const string Folder = @"C:\Users\ana\Tools";

    private static TimelineEvent Def(int id, long rid, DateTime t, Dictionary<string, string> fields) => new()
    {
        Time = Timestamp.FromUtc(t, "", "test"), Source = DefenderExclusionDetectionMerger.DefenderChannel, EventId = id.ToString(), EvidenceId = "EV-DEF",
        Locator = $"EventRecordID={rid}", Summary = "d", Fields = new(fields, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Exclusion(long rid, DateTime t) => Def(5007, rid, t, new()
    {
        ["Old Value"] = "", ["New Value"] = $@"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\{Folder} = 0x0",
    });

    private static TimelineEvent Detection(long rid, DateTime t) => Def(1116, rid, t, new()
    {
        ["Threat Name"] = "Trojan:Win32/Test", ["Path"] = $@"file:_{Folder}\x.exe", ["Detection User"] = @"PC01\ana",
    });

    private static Finding Cfg() => new()
    {
        FindingId = "F-0009", RuleId = "CFG-DEFENDER-EXCL", Title = "Microsoft Defender are 2 excluderi de la scanare", Description = "d", Severity = Severity.Medium,
        SupportingEvidence = [new EvidenceRef("EV-S", @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths", "cheia", "", $@"2 excluderi: {Folder}; D:\other")],
    };

    private static Finding DefFinding(string id, TimelineEvent det) => new()
    {
        FindingId = id, RuleId = "DEF-DETECTION", Title = "Microsoft Defender a detectat Trojan:Win32/Test în x.exe", Description = "d", Severity = Severity.High,
        File = $@"{Folder}\x.exe", FirstSeenUtc = det.Time.Utc, LastSeenUtc = det.Time.Utc, SupportingEvidence = [new EvidenceRef(det.EvidenceId, det.Locator, "Defender 1116")],
    };

    [Fact]
    public void A_detection_before_the_5007_is_linked_and_the_CFG_finding_says_the_folder_was_excluded_after_it()
    {
        var det = Detection(10, T0);
        var excl = Exclusion(20, T0.AddHours(2));
        var cfg = Cfg();
        var other = DefFinding("F-0001", det);
        var r = DefenderExclusionDetectionMerger.Merge([cfg, other], [det, excl]);
        Assert.Equal(2, r.Findings.Count);   // nothing dropped
        Assert.Equal(1, r.Linked);
        Assert.Equal(["F-0001"], cfg.RelatedFindingIds);
        var ev = Assert.Single(cfg.SupportingEvidence, e => e.Locator == "EventRecordID=20");
        Assert.Equal("EV-DEF", ev.EvidenceId);
        Assert.Contains("EventRecordID 20", ev.Description);
        Assert.Contains("2026-09-19 17:00:00 UTC", ev.Description);
        Assert.Contains(@"Exclusions\Paths\C:\Users\ana\Tools", ev.Quote);
        Assert.Contains(DefenderExclusionDetectionMerger.DetectionBeforeExclusion, ev.Description);
        var lim = Assert.Single(cfg.Limitations, l => l.Contains(DefenderExclusionDetectionMerger.DetectionBeforeExclusion));
        Assert.Contains("Ce NU dovedește: intenția sau cine a făcut-o", lim);
        Assert.Contains(cfg.AuditTrail, a => a.Step =="finding.exclusion_detection_linked");
        // deterministic and idempotent: a second run adds nothing
        Assert.Equal(0, DefenderExclusionDetectionMerger.Merge([cfg, other], [det, excl]).Linked);
        Assert.Single(cfg.Limitations, l => l.Contains(DefenderExclusionDetectionMerger.DetectionBeforeExclusion));
    }

    [Fact]
    public void A_detection_after_the_exclusion_is_linked_without_the_sentence()
    {
        var excl = Exclusion(20, T0);
        var det = Detection(30, T0.AddHours(2));
        var cfg = Cfg();
        var r = DefenderExclusionDetectionMerger.Merge([cfg, DefFinding("F-0001", det)], [excl, det]);
        Assert.Equal(2, r.Findings.Count);
        Assert.Equal(["F-0001"], cfg.RelatedFindingIds);
        var ev = Assert.Single(cfg.SupportingEvidence, e => e.Locator == "EventRecordID=20");
        Assert.DoesNotContain(DefenderExclusionDetectionMerger.DetectionBeforeExclusion, ev.Description);
        Assert.DoesNotContain(cfg.Limitations, l => l.Contains(DefenderExclusionDetectionMerger.DetectionBeforeExclusion));
    }

    [Fact]
    public void A_detection_outside_the_excluded_folder_is_not_linked()
    {
        var excl = Exclusion(20, T0.AddHours(2));
        var det = Def(1116, 10, T0, new() { ["Threat Name"] = "T", ["Path"] = @"file:_C:\Users\ana\ToolsOld\x.exe" });
        var cfg = Cfg();
        var f = DefFinding("F-0001", det);
        var r = DefenderExclusionDetectionMerger.Merge([cfg, new Finding { FindingId = f.FindingId, RuleId = f.RuleId, Title = f.Title, Description = "d", File = @"C:\Users\ana\ToolsOld\x.exe", SupportingEvidence = f.SupportingEvidence }], [excl, det]);
        Assert.Empty(cfg.RelatedFindingIds);
        Assert.Contains(cfg.SupportingEvidence, e => e.Locator == "EventRecordID=20");
        Assert.DoesNotContain(cfg.Limitations, l => l.Contains(DefenderExclusionDetectionMerger.DetectionBeforeExclusion));
        Assert.Equal(2, r.Findings.Count);
    }
}
