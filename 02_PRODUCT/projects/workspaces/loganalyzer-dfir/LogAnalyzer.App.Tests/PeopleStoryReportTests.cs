using System.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Privacy;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>The investigation report prints „Cine ce a făcut” (owner 2026-11-06) before the findings, and a masked export masks it too. Synthetic names only.</summary>
[Collection(GlobalStateCollection.Name)]   // sets PlainStory.CurrentPersonOf
public sealed class PeopleStoryReportTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() { InvestigationReportPdf.TextObserver.Value = null; _k.Dispose(); }

    private static Finding F(string id, string user, int minutes, Severity sev, string what) => new()
    {
        FindingId = id, RuleId = "TEST", Title = "t", Description = "x", Severity = sev, Classification = Classification.Candidate, User = user, Host = "WS-ALFA-07",
        PlainWhat = what, FirstSeenUtc = new DateTimeOffset(2026, 11, 6, 13, minutes, 0, TimeSpan.Zero), LastSeenUtc = new DateTimeOffset(2026, 11, 6, 13, minutes, 0, TimeSpan.Zero),
    };

    private InvestigationResult Result()
    {
        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        r.Timeline.Add(GapsTestKit.Ev("EventLog:Security", 0, "4624", f: [("TargetUserName", "ana.pop"), ("WorkstationName", "WS-ALFA-07")]));
        r.Findings.Add(F("F-0002", "ana.pop", 20, Severity.Medium, "a deschis documentul „plan.docx”"));
        r.Findings.Add(F("F-0001", "ana.pop", 5, Severity.High, "a conectat stick-ul „SanDisk”"));
        r.Findings.Add(F("F-0003", "ana.pop, ion.ion", 30, Severity.Critical, "a șters jurnalul"));
        return r;
    }

    [Fact]
    public void The_report_tells_each_persons_findings_in_time_order_before_the_findings_section()
    {
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        InvestigationReportPdf.Write(Result(), Path.Combine(_k.Dir, "persoane.pdf"), "investigator");
        var section = text.IndexOf("Cine ce a făcut", StringComparison.Ordinal);
        Assert.True(section > 0);
        Assert.True(text.IndexOf(". Constatări", section, StringComparison.Ordinal) > section);   // the numbered findings heading comes after it
        Assert.Contains("nu stabilește vinovăția", text);
        Assert.Contains("Utilizatorul cu contul „ana.pop”: 2 constatări, dintre care una gravă", text);
        var stick = text.IndexOf("a conectat stick-ul „SanDisk”", section, StringComparison.Ordinal);
        var plan = text.IndexOf("a deschis documentul „plan.docx”", section, StringComparison.Ordinal);
        Assert.True(stick > 0 && stick < plan);                                         // in the order it happened
        Assert.Contains("Cât erau conectate mai multe conturi", text);                 // the shared finding is put on neither
    }

    [Fact]
    public void A_masked_report_masks_the_people_section_too()
    {
        var r = Result();
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        var m = PiiMasker.FromTimeline(r.Timeline, r.Case.Info.Host, r.Case.Info.User);
        InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, "persoane-mascat.pdf"), "investigator", masker: m);
        Assert.Contains("Cine ce a făcut", text);
        Assert.DoesNotContain("ana.pop", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(m.User("ana.pop"), text);
    }

    [Fact]
    public void A_masked_report_masks_the_people_of_the_users_register_too()
    {
        // review 2026-11-07: the register is not in the timeline, so its people were printed in clear in a masked export
        var r = Result();
        var before = LogAnalyzer.Dfir.Language.PlainStory.CurrentPersonOf;
        try
        {
            LogAnalyzer.Dfir.Language.PlainStory.CurrentPersonOf = new LogAnalyzer.Dfir.Registers.UsersRegister { Rows = [new LogAnalyzer.Dfir.Registers.UserRow { Person = "Vasilescu Elena", Accounts = "ana.pop" }] }.PersonOf;
            string text = "";
            InvestigationReportPdf.TextObserver.Value = t => text = t;
            InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, "persoane-clar.pdf"), "investigator");
            Assert.Contains("Vasilescu Elena (contul ana.pop): 2 constatări", text);
            var m = PiiMasker.FromTimeline(r.Timeline, r.Case.Info.Host, r.Case.Info.User);
            InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, "persoane-mascat2.pdf"), "investigator", masker: m);
            Assert.Contains("Cine ce a făcut", text);
            foreach (var clear in new[] { "Vasilescu", "Elena", "ana.pop" }) Assert.DoesNotContain(clear, text, StringComparison.OrdinalIgnoreCase);
        }
        finally { LogAnalyzer.Dfir.Language.PlainStory.CurrentPersonOf = before; }
    }

    [Fact]
    public void A_masked_report_masks_a_register_person_found_only_through_a_SID()
    {
        // review round 2: the finding carries only the SID, the case's events name the account, the register names the person
        const string sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
        var r = new InvestigationResult { Case = _k.Case(_k.File("a.bin", "alpha")) };
        r.Timeline.Add(GapsTestKit.Ev("EventLog:Security", 0, "4624", f: [("TargetUserName", "ion.ion"), ("TargetUserSid", sid), ("WorkstationName", "WS-ALFA-07")]));
        r.Findings.Add(F("F-0001", sid, 5, Severity.High, "a conectat stick-ul „SanDisk”"));
        var before = LogAnalyzer.Dfir.Language.PlainStory.CurrentPersonOf;
        try
        {
            LogAnalyzer.Dfir.Language.PlainStory.CurrentPersonOf = new LogAnalyzer.Dfir.Registers.UsersRegister { Rows = [new LogAnalyzer.Dfir.Registers.UserRow { Person = "Vasilescu Elena", Accounts = "ion.ion" }] }.PersonOf;
            string text = "";
            InvestigationReportPdf.TextObserver.Value = t => text = t;
            InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, "sid-clar.pdf"), "investigator");
            Assert.Contains("Vasilescu Elena (contul ion.ion): 1 constatare, dintre care una gravă", text);
            InvestigationReportPdf.Write(r, Path.Combine(_k.Dir, "sid-mascat.pdf"), "investigator", masker: PiiMasker.FromTimeline(r.Timeline, r.Case.Info.Host, r.Case.Info.User));
            Assert.Contains("Cine ce a făcut", text);
            foreach (var clear in new[] { "Vasilescu", "Elena", "ion.ion", sid }) Assert.DoesNotContain(clear, text, StringComparison.OrdinalIgnoreCase);
        }
        finally { LogAnalyzer.Dfir.Language.PlainStory.CurrentPersonOf = before; }
    }
}