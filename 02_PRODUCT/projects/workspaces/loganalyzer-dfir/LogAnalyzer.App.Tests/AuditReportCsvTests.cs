using System.IO;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Independent review, finding 4: the Audit_Report CSV export of the main window goes through CsvWriter.Escape (formula neutralisation and
/// hidden-character removal), not through formula neutralisation alone.
/// </summary>
public class AuditReportCsvTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"x\"", "\"say \"\"x\"\"\"")]
    [InlineData(null, "")]
    public void Audit_report_cells_use_the_shared_csv_escaping(string? input, string expected) =>
        Assert.Equal(expected, MainViewModel.CsvCell(input));

    [Fact]
    public void Hidden_characters_are_removed_and_marked()
    {
        var cell = MainViewModel.CsvCell("invoice‮txt.exe");
        Assert.DoesNotContain("‮", cell, StringComparison.Ordinal);   // ordinal: culture comparison ignores the bidi control
        Assert.Contains("[caractere ascunse eliminate: U+202E]", cell);
    }

    [Fact]
    public void The_export_uses_the_shared_cell_helper_only()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "LogAnalyzer.slnx"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!, "LogAnalyzer.App", "ViewModels", "MainViewModel.cs"));
        Assert.DoesNotContain("EscapeCsv(", src);
        Assert.DoesNotContain("NeutraliseFormula(", src);
        Assert.Contains("CsvCell(item.Description)", src);
    }
}
