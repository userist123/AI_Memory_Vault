using System.IO;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>WP18 S5: the unit's report header, the control report for the "proces-verbal" (header, marking on every page, signatures) and the audiences of the incident report.</summary>
public sealed class Wp18ReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-rep-" + Guid.NewGuid().ToString("N"));
    public Wp18ReportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { ReportFooter.Observer.Value = null; try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Header_profile_round_trips_and_a_missing_or_broken_file_gives_an_empty_header_never_an_invented_one()
    {
        var path = Path.Combine(_dir, "report_header.json");
        var empty = ReportHeaderProfile.Load(path, out var problem);
        Assert.Null(problem); Assert.Equal("", empty.Unit); Assert.Equal("Cel care a efectuat controlul", empty.SignatureLeft);
        ReportHeaderProfile.Save(new ReportHeaderProfile { Unit = "U.M. 01234", Structure = "Compartimentul documente clasificate", InspectorFunction = "ofițer de securitate",
            ExtraFields = [new() { Label = "Dosar", Value = "D-7" }] }, path);
        var h = ReportHeaderProfile.Load(path, out problem);
        Assert.Null(problem); Assert.Equal("U.M. 01234", h.Unit); Assert.Single(h.ExtraFields);
        File.WriteAllText(path, "{broken");
        var broken = ReportHeaderProfile.Load(path, out problem);
        Assert.NotNull(problem); Assert.Equal("", broken.Unit);
    }

    private static StationFacts Facts() => new()
    {
        Host = "PC-CTRL-01", CollectedUtc = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero), IsAdministrator = true,
        PeriodStartUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), PeriodEndUtc = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero),
        Coverage = { new ChannelCoverage("Security", null, 10, true, ""), new ChannelCoverage("System", null, 0, false, "acces refuzat") },
    };

    [Fact]
    public void Control_report_header_has_every_form_field_with_a_dash_for_what_is_empty_and_the_manual_marking_note()
    {
        var profile = new ReportHeaderProfile { Unit = "U.M. 01234", InspectorFunction = "ofițer de securitate", ExtraFields = [new() { Label = "Dosar", Value = "" }] };
        var h = ControlReportHeader.From(profile, Facts(), "ABCDEF0123456789", "Pop Ion", "", "Stație de control", "", "SECRET DE SERVICIU");
        var lines = h.Lines();
        Assert.Contains(lines, l => l.Label == "Unitatea" && l.Value == "U.M. 01234");
        Assert.Contains(lines, l => l.Label == "Structura" && l.Value == "—");
        Assert.Contains(lines, l => l.Label.Contains("Hardware ID") && l.Value == "ABCDEF0123456789");
        Assert.Contains(lines, l => l.Label == "Cine a efectuat controlul" && l.Value == "Pop Ion, ofițer de securitate");
        Assert.Contains(lines, l => l.Label == "Rolul stației" && l.Value == "Stație de control");
        Assert.Contains(lines, l => l.Label == "Număr de înregistrare" && l.Value == "—");
        Assert.Contains(lines, l => l.Label == "Dosar" && l.Value == "—");
        Assert.Contains("SECRET DE SERVICIU", h.MarkingLine);
        Assert.Contains(ControlReportHeader.MarkingNote, h.MarkingLine);
        Assert.Contains(ControlReportHeader.NoMarking, (h with { Marking = "" }).MarkingLine);
    }

    [Fact]
    public void Control_pdf_prints_the_marking_on_every_page_and_is_registered_with_the_header()
    {
        var r = new ControlReport { Facts = Facts() };
        r.Checks.Add(new("A01", "Conturi", "Cont Guest activ", ControlStatus.Neconform, "Guest este activ.", ["SAM: Guest enabled"], "Dezactivați Guest."));
        r.Checks.Add(new("N01", "Rețea", "Conectări la rețele", ControlStatus.Nedeterminat, "Jurnalul nu a putut fi citit.", [], ""));
        var header = ControlReportHeader.From(new ReportHeaderProfile { Unit = "U.M. 01234" }, r.Facts, "HWID1234", "Pop Ion", "ofițer", "Stație de control", "123/2026", "SECRET DE SERVICIU");
        var result = ControlResultScreen.Build(r, ProfileSummary.Sections(null));
        var captured = new List<(string Kind, string Text)>();
        ReportFooter.Observer.Value = (k, t) => { lock (captured) captured.Add((k, t)); };
        var pdf = Path.Combine(_dir, "control.pdf");
        ControlReportPdf.Write(r, pdf, "Pop Ion", "", null, null, header, result);
        Assert.True(File.Exists(pdf) && new FileInfo(pdf).Length > 1000);
        var footer = Assert.Single(captured.Select(c => c.Text).Distinct());
        Assert.Contains("SECRET DE SERVICIU", footer);
        Assert.Contains(ControlReportHeader.MarkingNote, footer);
        Assert.Equal("control", captured[0].Kind);
    }

    [Fact]
    public void Incident_report_prints_the_gaps_before_the_findings_and_names_the_station_role()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.Dfir.Windows", "Investigation", "InvestigationReportPdf.cs"))) dir = dir.Parent;
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "LogAnalyzer.Dfir.Windows", "Investigation", "InvestigationReportPdf.cs"));
        Assert.True(src.IndexOf("ReportSection.Gaps))", StringComparison.Ordinal) < src.IndexOf("ReportSection.Findings))", StringComparison.Ordinal));
        Assert.Contains("rolul stației:", src);
    }

    [Fact]
    public void Audiences_choose_sections_never_facts_and_everything_prints_all()
    {
        Assert.Equal(6, ReportAudiences.All.Count);
        foreach (var a in ReportAudiences.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ReportAudiences.Label(a)));
            Assert.False(string.IsNullOrWhiteSpace(ReportAudiences.Description(a)));
            Assert.Contains(ReportSection.Gaps, ReportAudiences.Sections(a));            // the gaps are never dropped for anyone
            Assert.Contains(ReportSection.IncidentChains, ReportAudiences.Sections(a));
        }
        Assert.Equal(Enum.GetValues<ReportSection>().Length, ReportAudiences.Sections(ReportAudience.Everything).Count);
        foreach (var a in ReportAudiences.All)
            foreach (var must in new[] { ReportSection.Findings, ReportSection.AntiForensics })
                Assert.Contains(must, ReportAudiences.Sections(a));            // limitations, contradictions and UNDETERMINED never dropped
        Assert.True((int)ReportSection.Gaps < (int)ReportSection.Findings);   // printed in this order
        Assert.DoesNotContain(ReportSection.Integrity, ReportAudiences.Sections(ReportAudience.Management));
        Assert.Contains(ReportSection.Integrity, ReportAudiences.Sections(ReportAudience.Forensic));
        Assert.Contains(ReportSection.PolicyTimeline, ReportAudiences.Sections(ReportAudience.Audit));
        Assert.False(ReportAudiences.FindingsInFull(ReportAudience.Management));
        Assert.True(ReportAudiences.FindingsInFull(ReportAudience.Forensic));
    }
}
