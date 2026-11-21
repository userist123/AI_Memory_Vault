using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Research 2026-11-07, plan P3.3: event 307 of "Microsoft Print to PDF" names the document but not the file it became. A PDF created right after the job
/// is listed as its probable output, worded as an inference from time.
/// </summary>
public sealed class PrintToPdfOutputTests
{
    private static TimelineEvent Job(double minute, string printer = "Microsoft Print to PDF", string port = "PORTPROMPT:") =>
        Ev("Microsoft-Windows-PrintService/Operational", 307, minute, "Microsoft-Windows-PrintService", ("Param2", "Plan.docx"), ("Param3", "bob"), ("Param4", "LAB-WS02"),
           ("Param5", printer), ("Param6", port), ("Param8", "4"));

    private static TimelineEvent UsnCreate(double minute, string path) => new()
    {
        Time = Timestamp.FromUtc(DateTime.SpecifyKind(T0.AddMinutes(minute), DateTimeKind.Utc), "", "test"), Source = "USN", EvidenceId = "EV-U", Path = path, Summary = "USN: " + path,
        Fields = { ["Reason"] = "Data extend | File create | Close", ["FileName"] = Path.GetFileName(path) },
    };

    private static Finding PrintFinding(params TimelineEvent[] events) => Assert.Single(Go(events, Classified()), f => f.RuleId == "AIRGAP-PRINT-JOB");

    [Fact]
    public void A_PDF_created_minutes_after_a_print_to_PDF_job_is_named_as_its_probable_output()
    {
        var f = PrintFinding(Job(3), UsnCreate(4, @"…\Documente\Plan.pdf"), UsnCreate(30, @"…\Documente\Altul.pdf"));
        Assert.Contains("„…\\Documente\\Plan.pdf”", f.Description);
        Assert.DoesNotContain("Altul.pdf", f.Description);                      // outside the window
        Assert.Contains("legătură probabilă după oră, nedovedită", f.Description);
        Assert.Contains("a apărut fișierul „Plan.pdf”, probabil documentul tipărit", f.PlainWhat);
    }

    [Fact]
    public void A_paper_printer_or_a_file_created_before_the_job_gets_no_output_file()
    {
        Assert.DoesNotContain("PDF/XPS apărute", PrintFinding(Job(3, "HP LaserJet", "IP_10.0.0.5"), UsnCreate(4, @"…\Plan.pdf")).Description);
        Assert.DoesNotContain("PDF/XPS apărute", PrintFinding(Job(10), UsnCreate(4, @"…\Plan.pdf")).Description);
    }
}
