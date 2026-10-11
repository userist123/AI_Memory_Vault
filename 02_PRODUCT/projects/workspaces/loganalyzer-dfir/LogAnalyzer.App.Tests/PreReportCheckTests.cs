using System.IO;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Registers;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>F2: every PDF re-verifies the evidence hashes first, prints OK / modified / missing on page 1, and a changed hash blocks "confirmat" wording.</summary>
public sealed class PreReportCheckTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose()
    {
        InvestigationReportPdf.TextObserver.Value = null; ControlReportPdf.PrintedLines.Value = null; TransferReportPdf.PrintedLines.Value = null;
        _k.Dispose();
    }

    [Fact]
    public void Intact_evidence_counts_as_OK_and_allows_confirmed_wording()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"), _k.File("b.bin", "beta"));
        var pre = PreReportCheck.ForCase(ws);
        Assert.Equal((2, 0, 0), (pre.Ok, pre.Changed, pre.Missing));
        Assert.False(pre.BlocksConfirmed);
        Assert.Contains("2 OK · 0 modificate · 0 lipsă", pre.Line);
        Assert.Equal("Constatare confirmată", pre.Unconfirm("Constatare confirmată"));
        Assert.Contains("report.precheck", File.ReadAllText(ws.AppAuditLogPath));
    }

    [Fact]
    public void A_changed_and_a_missing_file_are_counted_and_block_confirmed()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"), _k.File("b.bin", "beta"), _k.File("c.bin", "gamma"));
        var ev = ws.LoadEvidence();
        GapsTestKit.Tamper(ws, ev[0]);
        var missing = ws.FullPath(ev[1].StoredPath);
        File.SetAttributes(missing, FileAttributes.Normal);
        File.Delete(missing);
        var pre = PreReportCheck.ForCase(ws);
        Assert.Equal((1, 1, 1), (pre.Ok, pre.Changed, pre.Missing));
        Assert.True(pre.BlocksConfirmed);
        Assert.Contains("1 OK · 1 modificate · 1 lipsă", pre.Line);
        Assert.Contains("„confirmat” este blocată", pre.Line);
    }

    [Theory]
    [InlineData("Constatare confirmată de verificare", "Constatare neconfirmată de verificare")]
    [InlineData("3 confirmate · 1 indicativă", "3 neconfirmate · 1 indicativă")]
    [InlineData("Confirmat de două surse", "Neconfirmat de două surse")]
    [InlineData("indiciu – de confirmat", "indiciu – de confirmat")]
    [InlineData("deja neconfirmat", "deja neconfirmat")]
    public void Neutralize_turns_positive_confirmed_words_negative_and_keeps_to_be_confirmed(string input, string expected)
    {
        Assert.Equal(expected, PreReportCheck.Neutralize(input));
        Assert.False(PreReportCheck.HasConfirmedWording(expected));
    }

    [Fact]
    public void Investigation_pdf_prints_the_recheck_line_on_page_one_and_has_no_confirmed_wording_after_a_change()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"), _k.File("b.bin", "beta"));
        GapsTestKit.Tamper(ws, ws.LoadEvidence()[0]);
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        var pdf = Path.Combine(_k.Dir, "inv.pdf");
        InvestigationReportPdf.Write(new InvestigationResult { Case = ws }, pdf, "investigator test");
        Assert.True(new FileInfo(pdf).Length > 1000);
        var lines = text.Split('\n');
        var idx = Array.FindIndex(lines, l => l.StartsWith("Reverificarea probelor înainte de raport", StringComparison.Ordinal));
        Assert.InRange(idx, 0, 4);                                     // right after the title lines, before the summary box
        Assert.Contains("1 OK · 1 modificate · 0 lipsă", lines[idx]);
        Assert.False(PreReportCheck.HasConfirmedWording(text), "a report over changed evidence must not say 'confirmat'");
        Assert.Contains("case.recheck", File.ReadAllText(ws.AppAuditLogPath));
    }

    [Fact]
    public void Investigation_pdf_over_intact_evidence_says_all_OK()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        string text = "";
        InvestigationReportPdf.TextObserver.Value = t => text = t;
        InvestigationReportPdf.Write(new InvestigationResult { Case = ws }, Path.Combine(_k.Dir, "ok.pdf"), "investigator test");
        Assert.Contains("1 OK · 0 modificate · 0 lipsă", text);
        Assert.DoesNotContain("este blocată", text);
    }

    [Fact]
    public void Control_report_saved_to_the_case_rechecks_first_and_prints_the_counts()
    {
        var ws = _k.Case(_k.File("a.bin", "alpha"));
        GapsTestKit.Tamper(ws, ws.LoadEvidence()[0]);
        var printed = new List<string>();
        ControlReportPdf.PrintedLines.Value = l => printed.Add(l);
        var r = new ControlReport { Facts = new StationFacts { Host = "H", CollectedUtc = DateTimeOffset.UtcNow, IsAdministrator = true } };
        r.Checks.Add(new("A01", "Conturi", "Cont", ControlStatus.Conform, "Confirmat de registru.", [], ""));
        var (_, pdf) = ControlReportPdf.SaveToCase(r, ws, "inspector");
        Assert.True(File.Exists(pdf));
        var line = Assert.Single(printed);
        Assert.Contains("0 OK · 1 modificate · 0 lipsă", line);
        Assert.Contains("blocată", line);
    }

    [Fact]
    public void Control_report_without_a_case_says_undetermined()
    {
        var printed = new List<string>();
        ControlReportPdf.PrintedLines.Value = l => printed.Add(l);
        ControlReportPdf.Write(new ControlReport { Facts = new StationFacts { Host = "H", CollectedUtc = DateTimeOffset.UtcNow } }, Path.Combine(_k.Dir, "c.pdf"), "inspector");
        Assert.Contains("nedeterminat", Assert.Single(printed));
    }

    [Fact]
    public void Transfer_report_reverifies_declared_hashes_on_the_medium_and_counts_a_change()
    {
        var medium = Path.Combine(_k.Dir, "medium");
        Directory.CreateDirectory(medium);
        File.WriteAllText(Path.Combine(medium, "doc1.txt"), "one");
        File.WriteAllText(Path.Combine(medium, "doc2.txt"), "two");
        string Sha(string f) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(medium, f))));
        var row = new TransferRow { RegistrationNumber = "T-1", DateTime = "2026-09-01 08:00", Content = "doc1.txt; doc2.txt", Sha256 = $"{Sha("doc1.txt")}; {Sha("doc2.txt")}" };
        var v = TransferVerifier.Verify([row], [row], medium);
        Assert.Equal(2, PreReportCheck.ForTransfer(v, [row]).Ok);
        File.WriteAllText(Path.Combine(medium, "doc2.txt"), "two, edited");
        var printed = new List<string>();
        TransferReportPdf.PrintedLines.Value = l => printed.Add(l);
        TransferReportPdf.Write(Path.Combine(_k.Dir, "t.pdf"), new TransferRegister { Rows = [row] }, [], new LogAnalyzer.Dfir.Reporting.ReportHeaderProfile(), "", true, verification: v);
        var line = Assert.Single(printed);
        Assert.Contains("1 OK · 1 modificate · 0 lipsă", line);   // doc2 is still there under its name, with another SHA-256
    }

    [Fact]
    public void Transfer_report_without_a_verification_is_undetermined_never_reuses_an_old_result()
    {
        var pre = PreReportCheck.ForTransfer(null, []);
        Assert.False(pre.Applicable);
        Assert.Contains("nedeterminat", pre.Line);
        var gone = new TransferVerification { Root = Path.Combine(_k.Dir, "absent"), StartedUtc = DateTimeOffset.UtcNow };
        Assert.Contains("nu mai este disponibil", PreReportCheck.ForTransfer(gone, []).Line);
    }
}
