using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Language;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// WP6a: checks on the XAML sources of the app (text scans; the app project only runs on Windows, the XAML text does not).
/// They guard the glossary wiring (R18) and the accessibility baseline (U18) of the investigation page, Home and the new controls.
/// </summary>
public class Wp6aXamlTests
{
    internal static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "LogAnalyzer.App");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("LogAnalyzer.App not found above " + AppContext.BaseDirectory);
    }

    internal static string Read(params string[] relative) => File.ReadAllText(Path.Combine([AppDir(), .. relative]));

    private static IEnumerable<string> AllXaml() =>
        Directory.EnumerateFiles(AppDir(), "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_glossary_reference_in_xaml_names_a_term_that_exists()
    {
        var refs = new List<(string File, string Term)>();
        foreach (var f in AllXaml())
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{views:Term\s+([A-Za-z_ ]+?)\s*[,}]"))
                refs.Add((Path.GetFileName(f), m.Groups[1].Value));
        Assert.NotEmpty(refs);
        foreach (var (file, term) in refs) Assert.True(Glossary.Find(term) is not null, $"{file}: unknown glossary term '{term}'");
    }

    [Fact]
    public void Sidebar_shows_human_phrases_for_the_glossary_terms_and_keeps_the_technical_name_as_tooltip()
    {
        var main = Read("MainWindow.xaml");
        Assert.DoesNotContain("Content=\"Event Explorer (EVTX)\"", main);
        Assert.DoesNotContain("Content=\"Chain of Custody (NIS2)\"", main);
        Assert.Matches(@"Content=""\{views:Term evtx\}""[^>]*ToolTip=""\{views:Term evtx, Tooltip=True\}""", main);
        Assert.Matches(@"Content=""\{views:Term chain_of_custody[^}]*\}""[^>]*ToolTip=""\{views:Term chain_of_custody, Tooltip=True\}""", main);
        Assert.Contains("Investigație completă (caz)", main);   // no page was removed or renamed
    }

    [Fact]
    public void Investigation_page_and_coverage_matrix_use_the_glossary()
    {
        var inv = Read("Views", "InvestigationView.xaml");
        Assert.Matches(@"Header=""\{views:Term evidence_graph\}""", inv);
        Assert.Contains("{views:Term evidence_graph, Tooltip=True}", inv);
        var home = Read("Views", "HomeView.xaml");
        Assert.Contains("{Binding HumanFamily}", home);
        Assert.Contains("FamilyTooltip", home);
    }
}
