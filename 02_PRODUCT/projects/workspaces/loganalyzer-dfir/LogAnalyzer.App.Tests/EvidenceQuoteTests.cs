using System.IO;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Privacy;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #11 A1: every evidence reference quotes the original, says where its time comes from and its channel. Synthetic data only.</summary>
public sealed class EvidenceQuoteTests
{
    private static TimelineEvent Row(string source, string locator, string semantics, Dictionary<string, string> fields, string summary = "s", bool withTime = true, string evidenceId = "E-0001") => new()
    {
        Time = withTime ? Timestamp.FromUtc(new DateTime(2026, 11, 6, 13, 0, 0, DateTimeKind.Utc), "raw", "test") : Timestamp.Unknown(),
        Source = source, EvidenceId = evidenceId, Locator = locator, TimeSemantics = semantics, Summary = summary, EventId = "4624",
        Fields = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase),
    };

    private static EvidenceItem Item(string id = "E-0001") => new() { EvidenceId = id, CaseId = "C", Source = "s", SourceType = "t", StoredPath = @"Raw\a", Size = 1, Sha256 = "ab" };

    private static Finding FindingOf(params EvidenceRef[] refs) => new()
    {
        FindingId = "F-0001", RuleId = "TEST", Title = "t", Description = "d", Severity = Severity.High, Classification = Classification.Candidate, User = "alice.demo",
        SupportingEvidence = [.. refs],
    };

    private static Finding Bind(Finding f, params TimelineEvent[] rows)
    {
        var (kept, rejected) = ProvenanceBinder.BindFindings([f], new Dictionary<string, EvidenceItem> { ["E-0001"] = Item() }, rows);
        Assert.Empty(rejected);
        return Assert.Single(kept);
    }

    [Fact]
    public void An_evtx_row_gives_a_quote_of_the_key_fields_most_relevant_first_with_time_source_and_channel()
    {
        var row = Row("EventLog:Security", "EventRecordID=4711", "event recorded", new()
        {
            ["Zeta"] = "z", ["LogonType"] = "3", ["IpAddress"] = "10.0.0.5", ["SubjectUserName"] = "SYSTEM", ["TargetUserName"] = "alice.demo", ["_message"] = "must not appear", ["Empty"] = "",
        });
        var e = Assert.Single(Bind(FindingOf(new EvidenceRef("E-0001", "EventRecordID=4711", "Security 4624")), row).SupportingEvidence);
        Assert.Equal("TargetUserName=alice.demo; SubjectUserName=SYSTEM; IpAddress=10.0.0.5; LogonType=3; Zeta=z", e.Quote);
        Assert.Equal("EVTX TimeCreated", e.TimeSource);
        Assert.Equal("Security", e.Channel);
        Assert.Equal("ab", e.Sha256);   // the hash is still bound in the same pass
    }

    [Fact]
    public void A_long_quote_is_cut_at_400_characters_with_an_ellipsis()
    {
        var row = Row("EventLog:Security", "EventRecordID=1", "event recorded", new() { ["CommandLine"] = new string('a', 900) });
        var q = Assert.Single(Bind(FindingOf(new EvidenceRef("E-0001", "EventRecordID=1", "x")), row).SupportingEvidence).Quote;
        Assert.Equal(EvidenceQuote.MaxQuoteLength, q.Length);
        Assert.EndsWith("…", q);
        Assert.StartsWith("CommandLine=aaa", q);
    }

    [Fact]
    public void The_cut_never_splits_a_surrogate_pair()
    {
        // "CommandLine=" is 12 characters: the high surrogate of the emoji sits at index 398, exactly where the cut (399 characters + "…") would fall
        var row = Row("EventLog:Security", "EventRecordID=1", "event recorded", new() { ["CommandLine"] = new string('a', 386) + "\U0001F600" + "bbbbbbbb" });
        var q = Assert.Single(Bind(FindingOf(new EvidenceRef("E-0001", "EventRecordID=1", "x")), row).SupportingEvidence).Quote;
        Assert.True(q.Length <= EvidenceQuote.MaxQuoteLength);
        Assert.EndsWith("…", q);
        for (int i = 0; i < q.Length; i++)
        {
            if (char.IsHighSurrogate(q[i])) { Assert.True(i + 1 < q.Length && char.IsLowSurrogate(q[i + 1])); i++; }
            else Assert.False(char.IsLowSurrogate(q[i]));
        }
        Assert.DoesNotContain("\U0001F600", q);   // the pair did not fit, so it is dropped whole
        Assert.Equal("a\U0001F600", EvidenceQuote.Truncate("a\U0001F600", 3));   // fits exactly: untouched
    }

    [Fact]
    public void A_registry_row_quotes_the_value_name_and_data_and_says_the_time_is_the_key_last_write()
    {
        var row = Row("RunKey", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run\Updater", "Run key last written (not this value's creation)",
            new() { ["Key"] = @"Microsoft\Windows\CurrentVersion\Run", ["ValueName"] = "Updater", ["Command"] = @"C:\Users\alice.demo\AppData\u.exe -s", ["Hive"] = "SOFTWARE" });
        var e = Assert.Single(Bind(FindingOf(new EvidenceRef("E-0001", row.Locator, "x")), row).SupportingEvidence);
        Assert.StartsWith(@"ValueName=Updater; Command=C:\Users\alice.demo\AppData\u.exe -s; Key=", e.Quote);
        Assert.Equal("Registry LastWrite (cheie)", e.TimeSource);
        Assert.Equal("RunKey", e.Channel);
    }

    [Fact]
    public void A_text_log_row_quotes_the_line_and_a_prefetch_row_says_last_run()
    {
        var line = Row("Artifact:RustDeskLog", "log.txt:line=12", "t", new(), summary: "Connection opened from 10.1.1.1");
        var pf = Row("Prefetch", "APP.EXE-1234.pf#run1", "t", new() { ["Exe"] = "APP.EXE" });
        var f = Bind(FindingOf(new EvidenceRef("E-0001", line.Locator, "x"), new EvidenceRef("E-0001", pf.Locator, "y")), line, pf);
        // the parser keeps no raw line here: the summary is quoted, labelled as a summary (never presented as the original text)
        Assert.Equal((EvidenceQuote.SummaryLabel + "Connection opened from 10.1.1.1", "Linie de jurnal text"), (f.SupportingEvidence[0].Quote, f.SupportingEvidence[0].TimeSource));
        Assert.Equal("Prefetch: ultima rulare", f.SupportingEvidence[1].TimeSource);
    }

    [Fact]
    public void A_reference_without_its_row_keeps_an_empty_quote_and_an_unknown_time_source()
    {
        var other = Row("EventLog:Security", "EventRecordID=1", "event recorded", new() { ["TargetUserName"] = "x" });
        var e = Assert.Single(Bind(FindingOf(new EvidenceRef("E-0001", "EventRecordID=999", "x")), other).SupportingEvidence);
        Assert.Equal("", e.Quote);
        Assert.Equal("Necunoscut", e.TimeSource);
        Assert.Equal("", e.Channel);
    }

    [Fact]
    public void A_findings_json_written_before_the_fields_reads_back_with_the_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-quote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "findings.json");
            File.WriteAllText(path, """
                {"schema_version":"2.0","Findings":[{"FindingId":"F-0001","RuleId":"T","Title":"t","Description":"d",
                 "SupportingEvidence":[{"EvidenceId":"E-0001","Locator":"EventRecordID=1","Description":"x","Sha256":"ab"}]}],"Gaps":[]}
                """);
            var e = Assert.Single(FindingsFile.Read(path).Findings[0].SupportingEvidence);
            Assert.Equal("", e.Quote);
            Assert.Equal("Necunoscut", e.TimeSource);
            Assert.Equal("", e.Channel);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_fields_survive_a_findings_json_round_trip_and_the_schema_minor_is_bumped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-quote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "findings.json");
            var f = FindingOf(new EvidenceRef("E-0001", "EventRecordID=1", "x", "ab", "TargetUserName=bob; LogonType=3", "EVTX TimeCreated", "Security"));
            File.WriteAllText(path, SchemaVersions.WithVersion(new { Findings = new[] { f }, Gaps = Array.Empty<EvidenceGap>() }, SchemaVersions.Findings));
            var back = FindingsFile.Read(path);
            Assert.Equal("2.1", back.SchemaVersion);
            Assert.Equal(f.SupportingEvidence[0], back.Findings[0].SupportingEvidence[0]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_investigation_pdf_prints_Citat_and_Ora_din_under_the_evidence_line_and_the_manual_step_points_to_it()
    {
        using var k = new GapsTestKit();
        try
        {
            var ws = k.Case();
            var item = ws.ImportFile(k.File("Security.evtx", "evtx-bytes"), "Import:Security", "EventLog:Security", TemporalType.Historical, "Import", "t");
            var row = Row("EventLog:Security", "EventRecordID=4711", "event recorded", new() { ["TargetUserName"] = "alice.demo", ["LogonType"] = "3" }, evidenceId: item.EvidenceId);
            var (kept, _) = ProvenanceBinder.BindFindings([FindingOf(new EvidenceRef(item.EvidenceId, "EventRecordID=4711", "Security 4624"))], new Dictionary<string, EvidenceItem> { [item.EvidenceId] = item }, [row]);
            var r = new InvestigationResult { Case = ws };
            r.Findings.AddRange(kept);
            r.Timeline.Add(row);

            string text = "";
            InvestigationReportPdf.TextObserver.Value = t => text = t;
            InvestigationReportPdf.Write(r, Path.Combine(k.Dir, "citat.pdf"), "investigator");
            Assert.Contains("Citat: „TargetUserName=alice.demo; LogonType=3”", text);
            Assert.Contains("Ora din: EVTX TimeCreated · canal: Security", text);
            Assert.Contains("compară valorile cu rândul „Citat:”", text);

            // a masked export must not print the original user name inside the quote
            string masked = "";
            InvestigationReportPdf.TextObserver.Value = t => masked = t;
            InvestigationReportPdf.Write(r, Path.Combine(k.Dir, "citat-mascat.pdf"), "investigator", masker: PiiMasker.FromTimeline(r.Timeline, r.Case.Info.Host, r.Case.Info.User));
            Assert.Contains("Citat:", masked);
            Assert.DoesNotContain("alice.demo", masked);
        }
        finally { InvestigationReportPdf.TextObserver.Value = null; }
    }

    [Fact]
    public void A_reopened_case_prints_the_quote_from_the_reference_even_though_the_timeline_has_no_fields()
    {
        using var k = new GapsTestKit();
        try
        {
            var ws = k.Case();
            var item = ws.ImportFile(k.File("Security.evtx", "evtx-bytes"), "Import:Security", "EventLog:Security", TemporalType.Historical, "Import", "t");
            var r = new InvestigationResult { Case = ws };
            r.Findings.Add(FindingOf(new EvidenceRef(item.EvidenceId, "EventRecordID=4711", "x", "", "TargetUserName=bob.demo", "EVTX TimeCreated", "Security")));
            string text = "";
            InvestigationReportPdf.TextObserver.Value = t => text = t;
            InvestigationReportPdf.Write(r, Path.Combine(k.Dir, "redeschis.pdf"), "investigator");
            Assert.Contains("Citat: „TargetUserName=bob.demo”", text);
        }
        finally { InvestigationReportPdf.TextObserver.Value = null; }
    }

    [Fact]
    public void The_finding_card_evidence_list_carries_the_quote_and_the_time_source()
    {
        var f = FindingOf(new EvidenceRef("E-0001", "EventRecordID=1", "x", "", "TargetUserName=bob", "EVTX TimeCreated", "Security"), new EvidenceRef("E-0001", "EventRecordID=2", "y"));
        var lines = LogAnalyzer.Dfir.Presentation.FindingCardModel.Build(f, new LogAnalyzer.Dfir.Presentation.EvidenceContext { Items = [Item()] }).Evidence.List;
        Assert.Equal(("TargetUserName=bob", "EVTX TimeCreated · canal: Security"), (lines[0].Quote, lines[0].TimeSource));
        Assert.Equal((LogAnalyzer.Dfir.Presentation.EvidenceLevels.QuoteUnavailable, "Necunoscut"), (lines[1].Quote, lines[1].TimeSource));
    }
}
