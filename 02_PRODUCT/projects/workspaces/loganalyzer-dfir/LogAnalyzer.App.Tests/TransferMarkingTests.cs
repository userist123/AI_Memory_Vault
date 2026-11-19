using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Planner #10 item 1: the transfer-register PDF printed its marking only when one was passed, and the application always passed "";
/// the officer request PDF had none. Every page now carries the marking line ("fără marcaj introdus" when nothing was typed).
/// </summary>
public sealed class TransferMarkingTests
{
    private static List<string> Footers(string marking)
    {
        var seen = new List<string>();
        var path = Path.Combine(Path.GetTempPath(), "la-trm-" + Guid.NewGuid().ToString("N") + ".pdf");
        ReportFooter.Observer.Value = (kind, text) => { if (kind == "transfers") seen.Add(text); };
        try
        {
            LogAnalyzer.Dfir.Windows.Registers.TransferReportPdf.Write(path, new TransferRegister(), [], new LogAnalyzer.Dfir.Reporting.ReportHeaderProfile(), marking, true);
        }
        finally
        {
            ReportFooter.Observer.Value = null;
            if (File.Exists(path)) File.Delete(path);
        }
        return seen;
    }

    [Fact]
    public void Without_a_marking_the_transfer_report_says_so_on_the_footer()
    {
        var f = Footers("");
        Assert.NotEmpty(f);
        Assert.All(f, t => Assert.Contains("Marcaj: fără marcaj introdus (marcaj introdus manual, neverificat de aplicație)", t));
    }

    [Fact]
    public void A_typed_marking_is_printed_with_the_not_verified_note()
    {
        var f = Footers("SECRET");
        Assert.NotEmpty(f);
        Assert.All(f, t => Assert.Contains("Marcaj: SECRET (marcaj introdus manual, neverificat de aplicație)", t));
    }

    [Fact]
    public void The_application_passes_a_marking_to_the_transfer_and_officer_request_pdfs()
    {
        static string Src(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
        }
        var transfer = Src("LogAnalyzer.App", "ViewModels", "TransferViewModel.cs");
        Assert.DoesNotContain("_header, \"\", AppModeContext", transfer);
        Assert.Contains("_header, CurrentMarking(), AppModeContext", transfer);
        Assert.Contains("DateTimeOffset.UtcNow, MarkingLevel);", Src("LogAnalyzer.App", "ViewModels", "StationControlViewModel.Officer.cs"));
    }
}
