using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Legal research G2/G3: every report page says which program (with its SHA-256), which detection lists and which time zone produced it.</summary>
[Collection("ReportFooterObserver")]
public sealed class ReportProvenanceTests
{
    [Fact]
    public void Every_report_footer_carries_program_hash_lists_and_time_zone()
    {
        var seen = new List<string>();
        ReportFooter.Observer.Value = (_, text) => { lock (seen) seen.Add(text); };
        var path = Path.Combine(Path.GetTempPath(), $"la-prov-{Guid.NewGuid():N}.pdf");
        try
        {
            ChecksReportPdf.Write(path, "Test", "provenance", [], ["nota"]);
            Assert.Contains(seen, t => t.Contains("SHA-256") && t.Contains("liste de detecție") && t.Contains("fus orar al stației") && t.Contains("UTC"));
        }
        finally { ReportFooter.Observer.Value = null; if (File.Exists(path)) File.Delete(path); }
    }
}

public sealed class ReportLimitationsTests
{
    [Fact]
    public void Limitations_are_always_stated_and_name_missing_admin_rights()
    {
        var f = new StationFacts { Host = "WS", PeriodStartUtc = DateTimeOffset.UtcNow.AddDays(-1), PeriodEndUtc = DateTimeOffset.UtcNow, CollectedUtc = DateTimeOffset.UtcNow, IsAdministrator = false };
        var l = ControlReportPdf.Limitations(f);
        Assert.StartsWith("Controlul a rulat FĂRĂ drepturi de administrator", l[0]);
        Assert.Contains(l, x => x.Contains("Lipsa unei urme nu dovedește"));
    }
}
