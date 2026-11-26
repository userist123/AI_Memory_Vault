using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// The interface is in Romanian. Visible literal texts (Text, Content, Header, ToolTip, AutomationProperties.Name and the literal
/// parts of StringFormat / FallbackValue) must not contain the English phrases that used to appear on the main pages.
/// Technology names (EVTX, Sigma, YARA, STIX, KQL, SPL, IOC, ATT&amp;CK ids) are not in the list and stay allowed.
/// </summary>
public sealed class RomanianUiTextTests
{
    private static readonly string[] EnglishPhrases =
    [
        "Ingested", "Correlated", "Prioritized", "Threat Queue", "Workbench", "Compile &", "Compile &amp;", "Transpile",
        "Alert Title", "Severity", "Technique", "Click row", "Indicator Value", "Profiles", "Export Incident",
        "Intrusion Stages", "HOSTS IN SCOPE", "REGISTRY ARTIFACTS", "Operational posture", "Ready to compile",
    ];

    private static readonly Regex LiteralAttribute = new(
        @"\b(?:Text|Content|Header|ToolTip|AutomationProperties\.Name)=""(?<v>[^""{][^""]*)""", RegexOptions.Compiled);

    private static readonly Regex BindingLiteral = new(
        @"\b(?:StringFormat|FallbackValue)=(?:'(?<v>[^']*)'|(?<v>[^,}\s']+))", RegexOptions.Compiled);

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    private static IEnumerable<string> LiteralsOf(string line)
    {
        foreach (Match m in LiteralAttribute.Matches(line)) yield return m.Groups["v"].Value;
        foreach (Match m in BindingLiteral.Matches(line)) yield return m.Groups["v"].Value;
    }

    [Fact]
    public void Views_contain_no_english_ui_text()
    {
        var views = Path.Combine(Root(), "LogAnalyzer.App", "Views");
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var literal in LiteralsOf(lines[i]))
                {
                    var phrase = EnglishPhrases.FirstOrDefault(p => literal.Contains(p, StringComparison.OrdinalIgnoreCase));
                    if (phrase is not null)
                        hits.Add($"{Path.GetRelativePath(views, file)}:{i + 1}: \"{literal}\" (conține „{phrase}”)");
                }
            }
        }
        Assert.True(hits.Count == 0, "Text englezesc în interfață:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void The_dashboard_and_storyline_no_longer_show_fixed_technique_grids()
    {
        var views = Path.Combine(Root(), "LogAnalyzer.App", "Views");
        var dashboard = File.ReadAllText(Path.Combine(views, "DashboardView.xaml"));
        var storyline = File.ReadAllText(Path.Combine(views, "AttackStorylineView.xaml"));
        Assert.DoesNotContain("T1190", dashboard);
        Assert.DoesNotContain("MitreInitialAccessTechniques", dashboard);
        Assert.Contains("LoadedAlertTechniques", dashboard);
        foreach (var fixedStage in new[] { "01. INITIAL ACCESS", "T1110 (Brute Force)", "04. LATERAL MOVE" })
            Assert.DoesNotContain(fixedStage, storyline);
    }
}
