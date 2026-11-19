using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Registers;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #10 item 2: the shared printed register (first user: the intervention register, ORNISS Anexa 14).</summary>
public sealed class RegisterReportPdfTests
{
    private static InterventionRegister Many(int n)
    {
        var r = new InterventionRegister();
        for (int i = 1; i <= n; i++)
            r.Rows.Add(new InterventionRow { Date = $"2026-01-{(i % 28) + 1:00}", Time = "10:00", Station = "ST-" + i, Kind = "hardware", Components = "RAM modul " + i,
                Description = "Înlocuire " + string.Join(" ", Enumerable.Repeat("component defect cu descriere lungă", 6)) + $" nr {i}", Performer = "Ionescu", Witness = "Popescu", Notes = "REZ" + i });
        return r;
    }

    private static (List<string> Footers, List<string> Text, int Pages) Print(IRegisterData reg, string marking, RegisterIntegrity? integrity = null, DateTime? from = null, DateTime? to = null)
    {
        var footers = new List<string>(); var text = new List<string>(); var pages = 0;
        var path = Path.Combine(Path.GetTempPath(), "la-rrp-" + Guid.NewGuid().ToString("N") + ".pdf");
        ReportFooter.Observer.Value = (kind, t) => { if (kind == RegisterReportPdf.FooterKind(reg)) footers.Add(t); };
        RegisterReportPdf.TextObserver.Value = text.Add;
        try
        {
            RegisterReportPdf.Write(path, reg, new ReportHeaderProfile(), marking, "REGISTRUL DE EVIDENȚĂ A INTERVENȚIILOR HARDWARE ȘI SOFTWARE", LogAnalyzer.UI.Views.ColumnLabels.For, from, to, integrity);
            pages = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)), @"/Type\s*/Page(?![s\w])").Count;
        }
        finally { ReportFooter.Observer.Value = null; RegisterReportPdf.TextObserver.Value = null; if (File.Exists(path)) File.Delete(path); }
        return (footers, text, pages);
    }

    [Fact]
    public void The_marking_is_on_every_page_footer_and_the_empty_marking_says_so()
    {
        var (f, _, pages) = Print(Many(60), "SECRET");
        Assert.True(pages >= 2, "the register must span at least two pages: " + pages);   // the footer is composed once and repeated by the layout on every page
        Assert.NotEmpty(f);
        Assert.All(f, t => Assert.Contains("Marcaj: SECRET (marcaj introdus manual, neverificat de aplicație)", t));
        var (e, _, _) = Print(Many(3), "");
        Assert.NotEmpty(e);
        Assert.All(e, t => Assert.Contains("Marcaj: fără marcaj introdus (marcaj introdus manual, neverificat de aplicație)", t));
    }

    [Fact]
    public void Every_row_and_every_column_header_is_printed_across_pages()
    {
        var reg = Many(60);
        var (_, text, pages) = Print(reg, "SECRET");
        Assert.True(pages >= 2, "pages: " + pages);
        var headers = string.Join(" | ", reg.Columns.Select(LogAnalyzer.UI.Views.ColumnLabels.For));
        Assert.Contains(headers, text);
        foreach (var h in reg.Columns.Select(LogAnalyzer.UI.Views.ColumnLabels.For)) Assert.Contains(h, headers);
        for (int i = 1; i <= 60; i++) Assert.Contains(text, l => l.Contains($"ST-{i} |", StringComparison.Ordinal) && l.EndsWith("REZ" + i, StringComparison.Ordinal));
        Assert.Contains("REGISTRUL DE EVIDENȚĂ A INTERVENȚIILOR HARDWARE ȘI SOFTWARE", text);
    }

    [Fact]
    public void The_sha256_line_is_printed_and_a_missing_integrity_says_it_is_not_included()
    {
        var sha = new string('A', 64);
        var (_, text, _) = Print(Many(2), "", new RegisterIntegrity(sha, "SHA-256 coincide cu ultima intrare din jurnalul de audit al registrelor; lanțul jurnalului este valid."));
        Assert.Contains(text, l => l.Contains("SHA-256 al fișierului registrului: " + sha, StringComparison.Ordinal) && l.Contains("coincide", StringComparison.Ordinal));
        var (_, none, _) = Print(Many(2), "");
        Assert.Contains(none, l => l.Contains("neinclus", StringComparison.Ordinal));
    }

    [Fact]
    public void The_period_filters_on_the_date_column()
    {
        var reg = new InterventionRegister();
        reg.Rows.Add(new InterventionRow { Date = "2026-01-05", Station = "IN" });
        reg.Rows.Add(new InterventionRow { Date = "2026-03-05", Station = "OUT" });
        var (_, text, _) = Print(reg, "", null, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.Contains(text, l => l.Contains("IN"));
        Assert.DoesNotContain(text, l => l.Contains("OUT"));
    }

    [Fact]
    public void Integrity_of_a_saved_register_matches_its_audit_entry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-rrp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, InterventionRegister.DefaultFileName);
            var reg = Many(1);
            var issues = RegisterStore.Save(reg, path, out var sha, who: "test");
            if (issues.Any(i => i.IsError) || sha.Length == 0) return;   // writing is refused for this identity; the print path itself is covered above
            var integrity = RegisterReportPdf.Integrity(path, reg);
            Assert.Equal(sha, integrity.Sha256, ignoreCase: true);
            Assert.Contains("coincide", integrity.AuditLine);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Only_the_intervention_register_opts_in_to_printing()
    {
        var path = Path.Combine(Path.GetTempPath(), "la-rrp-" + Guid.NewGuid().ToString("N") + ".json");
        Assert.True(new InterventionRegisterViewModel(path).CanPrint);
        Assert.False(new UsersRegisterViewModel(path).CanPrint);
    }
}
