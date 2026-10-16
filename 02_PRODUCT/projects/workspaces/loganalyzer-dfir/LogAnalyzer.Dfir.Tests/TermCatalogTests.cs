using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Usability 2026-10-16 (owner: „fiecare termen din interfață și din rapoarte explicat acolo unde apare, pe înțelesul unui controlor fără pregătire IT”):
/// one catalogue for tooltips, „?” buttons and the „Ce înseamnă termenii” appendix of the reports.
/// </summary>
public class TermCatalogTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_check_guide_id_has_a_glossary_entry_with_its_plain_explanation()
    {
        Assert.True(CheckGuides.Ids.Count >= 80, $"only {CheckGuides.Ids.Count} check ids");
        foreach (var id in CheckGuides.Ids)
        {
            var t = TermCatalog.Find(id);
            Assert.True(t is not null, $"{id}: no glossary entry");
            Assert.Equal(id, t!.Technical);
            Assert.Contains(CheckGuides.For(id).What, t.Explanation);
            Assert.StartsWith(id + " — ", TermCatalog.Tooltip(id));
            Assert.NotNull(TermCatalog.Find(id.ToLowerInvariant()));
        }
    }

    [Fact]
    public void Every_result_word_and_every_severity_word_is_explained()
    {
        foreach (var s in Enum.GetValues<ControlStatus>())
        {
            Assert.True(TermCatalog.Find(ControlReportPdf.StatusText(s)) is not null, "printed result " + ControlReportPdf.StatusText(s));
            Assert.True(TermCatalog.Find(s.ToString()) is not null, "enum name " + s);   // the control grid binds the enum
        }
        Assert.NotNull(TermCatalog.Find("NECONFIRMAT"));
        foreach (var sev in Enum.GetValues<Severity>())
        {
            Assert.True(TermCatalog.Find(RoLabels.Severity(sev)) is not null, "severity " + RoLabels.Severity(sev));
            Assert.True(TermCatalog.Find(LogAnalyzer.Dfir.Presentation.SeverityLabels.Romanian(sev)) is not null, "severity label " + sev);
        }
        Assert.NotNull(TermCatalog.Find("GRAV"));
        Assert.NotNull(TermCatalog.Find("RIDICAT"));
    }

    [Theory]
    [InlineData("SACL")] [InlineData("SRUM")] [InlineData("NCSI")] [InlineData("Prefetch")] [InlineData("Amcache")] [InlineData("EVTX")]
    [InlineData("BitLocker To Go")] [InlineData("PAdES")] [InlineData("CRL")] [InlineData("SHA-256")] [InlineData("amprentă SHA-256")]
    [InlineData("cod de ancorare")] [InlineData("UAC")] [InlineData("proces-verbal")] [InlineData("CD-R")] [InlineData("PIN")] [InlineData("UTC")]
    public void The_technical_words_of_the_control_screens_and_reports_are_explained(string word)
    {
        var t = TermCatalog.Find(word);
        Assert.NotNull(t);
        Assert.True(t!.Explanation.Length > 30, word);
        Assert.Contains(t.Romanian, TermCatalog.Tooltip(word));
    }

    [Fact]
    public void An_unknown_word_has_no_entry_and_no_tooltip()
    {
        Assert.Null(TermCatalog.Find("ShimCache"));
        Assert.Equal("", TermCatalog.Tooltip("ShimCache"));
        Assert.Equal("", TermCatalog.Tooltip(""));
        Assert.Equal("", TermCatalog.Tooltip(null));
    }

    [Fact]
    public void Terms_in_a_text_are_only_those_that_appear_as_whole_words()
    {
        var found = TermCatalog.TermsIn("A11 NECONFORM: jurnalul SRUM lipsește; codul de ancorare nu se potrivește. În medie 3 conectări.").Select(t => t.Key).ToList();
        Assert.Contains("A11", found);
        Assert.Contains("status_neconform", found);
        Assert.Contains("srum", found);
        Assert.Contains("anchor_code", found);
        Assert.DoesNotContain("status_conform", found);    // „CONFORM” is inside „NECONFORM”, not a word of its own
        Assert.DoesNotContain("A13", found);
        Assert.DoesNotContain("severity_medium", found);   // „medie” in prose is not the severity word „MEDIE”
        Assert.Equal(found.Count, found.Distinct().Count());
        Assert.Empty(TermCatalog.TermsIn(""));
    }

    [Fact]
    public void The_control_report_prints_an_appendix_with_only_the_terms_it_uses()
    {
        var r = new ControlReport { Facts = new StationFacts { Host = "ST-TEST", PeriodStartUtc = Start, PeriodEndUtc = Start.AddDays(30), CollectedUtc = Start.AddDays(30), IsAdministrator = true,
                                                               Posture = LogAnalyzer.Core.Services.Edition.StationPosture.Standalone } };
        r.Checks.Add(new("A10", "Audit", "Auditarea accesului la folderele clasificate (SACL)", ControlStatus.Nedeterminat, "Profilul nu definește foldere.", [], ""));
        r.Checks.Add(new("C01", "Conturi", "Contul Guest este dezactivat", ControlStatus.Neconform, "Contul Guest este ACTIV.", [], "Dezactivați contul Guest."));
        var printed = new List<string>();
        ControlReportPdf.TextObserver.Value = t => printed.Add(t);
        try
        {
            var pdf = Path.Combine(Path.GetTempPath(), $"ctl_terms_{Guid.NewGuid():N}.pdf");
            ControlReportPdf.Write(r, pdf, "inspector");
            File.Delete(pdf);
        }
        finally { ControlReportPdf.TextObserver.Value = null; }
        var text = string.Join("\n", printed);
        int at = text.IndexOf(TermCatalog.AppendixTitle, StringComparison.Ordinal);
        Assert.True(at > 0, "no appendix");
        var appendix = text[at..];
        foreach (var expected in new[] { "A10 — ", "C01 — ", "SACL — ", "NECONFORM — ", "NEDETERMINAT — ", "CONFORM — " })
            Assert.Contains(expected, appendix);
        Assert.DoesNotContain("A11 — ", appendix);
        Assert.DoesNotContain("SRUM — ", appendix);
        Assert.DoesNotContain("BitLocker To Go — ", appendix);
        // the officer section stays the last section of the body (it must land on the last page, before the signature page)
        Assert.True(at < text.IndexOf("6. Ancorarea jurnalului aplicației", StringComparison.Ordinal));
    }

    [Fact]
    public void Folders_are_remembered_per_user_and_a_folder_that_no_longer_exists_is_forgotten()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-folders-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(dir, "export");
        Directory.CreateDirectory(target);
        try
        {
            var alice = new FolderMemory(dir, "alice");
            Assert.Null(alice.Get(FolderMemory.OfficerExport));
            Assert.True(alice.Remember(FolderMemory.OfficerExport, target));
            Assert.Equal(target, new FolderMemory(dir, "alice").Get(FolderMemory.OfficerExport));
            Assert.Null(new FolderMemory(dir, "bob").Get(FolderMemory.OfficerExport));           // another user does not inherit it
            Assert.True(alice.Remember(FolderMemory.ReportCopy, Path.Combine(target, "raport.pdf")));   // a file: its folder
            Assert.Equal(target, alice.Get(FolderMemory.ReportCopy));
            Directory.Delete(target, true);
            Assert.Null(alice.Get(FolderMemory.OfficerExport));                                   // gone: not offered
            alice.Forget(FolderMemory.ReportCopy);
            Assert.Null(alice.Get(FolderMemory.ReportCopy));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_officer_export_writes_the_media_history_and_names_its_folder_safely()
    {
        var f = new StationFacts { Host = "ST-TEST" };
        f.UsbDevices.Add(new UsbDeviceFact("Disk&Ven_Kingston", "SER123", "Kingston DataTraveler", "USBSTOR"));
        f.ObservedMedia = [new LogAnalyzer.Dfir.Registers.SeenMedium("SER999", "0951:1666", 32_000_000_000, "", "Stick înrolat", true, [Start, Start.AddDays(2)])];
        var path = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid():N}.csv");
        try
        {
            Assert.Equal(2, OfficerExport.WriteMediaHistory(f, path));
            var csv = File.ReadAllText(path);
            Assert.Contains("SER123", csv);
            Assert.Contains("SER999", csv);
            Assert.Contains("2026-09-03 00:00:00", csv);
        }
        finally { File.Delete(path); }
        Assert.Equal("Pentru_ofiter_ST_X_20260901_000000", OfficerExport.FolderName("ST/X", Start));
        var readme = OfficerExport.Readme("ST-TEST", Start, ["raport.pdf — raportul de control"], "Cod de ancorare: ABCDE-…");
        Assert.Contains("raport.pdf", readme);
        Assert.Contains(OfficerExport.MediaHistoryFile, readme);
    }
}
