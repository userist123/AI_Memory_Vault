using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

public sealed class ForensicValidationTests(ITestOutputHelper output)
{
    [Fact]
    public void Summary_never_reports_available_when_a_section_is_missing()
    {
        Assert.StartsWith("FORENSIC VALIDATION = UNAVAILABLE", ForensicValidation.Summarize([("a", "x", false), ("b", "y", false)]));
        var partial = ForensicValidation.Summarize([("a", "x", true), ("b", "y", false)]);
        Assert.StartsWith("FORENSIC VALIDATION = PARTIAL (1/2", partial);
        Assert.Contains("NU constituie validare", partial);
        Assert.StartsWith("FORENSIC VALIDATION = AVAILABLE (2/2", ForensicValidation.Summarize([("a", "x", true), ("b", "y", true)]));
    }

    /// <summary>Always runs (never skipped): writes the validation state next to the test binaries for CI and people.</summary>
    [Fact]
    public void Forensic_validation_state_is_reported()
    {
        var sections = ForensicValidation.Sections();
        Assert.NotEmpty(sections);
        var text = ForensicValidation.Summarize(sections);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "forensic_validation.txt"), text + Environment.NewLine);
        output.WriteLine(text);
        if (ForensicValidation.CorpusRequired)
            Assert.True(sections.All(s => s.Present), "LADFIR_REQUIRE_CORPUS=1, dar corpusul este incomplet:" + Environment.NewLine + text);
    }
}
