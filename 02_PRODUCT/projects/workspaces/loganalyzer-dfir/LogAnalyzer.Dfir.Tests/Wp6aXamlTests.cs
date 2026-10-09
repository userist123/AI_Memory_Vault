using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Presentation;
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
        Assert.Matches(@"Content=""\{loc:T shell\.nav\.custody_nis2\}""[^>]*ToolTip=""\{views:Term chain_of_custody, Tooltip=True\}""", main);   // phrase + NIS2 tag, one key
        Assert.Contains("Content=\"{loc:T shell.investigatie_completa_caz}\"", main);   // no page was removed or renamed (WP6b: the label is a key)
        Assert.Equal("Investigație completă (caz)", LogAnalyzer.Dfir.Language.Loc.T("shell.investigatie_completa_caz", LogAnalyzer.Dfir.Language.AppLanguage.Romanian));
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

    // ---- U18 accessibility baseline ---------------------------------------------------------------------------------------------

    /// <summary>The XAML files the baseline covers: the investigation page, Home and the new controls.</summary>
    private static readonly string[][] Scope =
    [
        ["Views", "InvestigationView.xaml"], ["Views", "HomeView.xaml"],
        ["Views", "Controls", "FindingCard.xaml"], ["Views", "Controls", "KnowThinkDontKnowPanel.xaml"], ["Views", "Controls", "SeverityBadge.xaml"],
    ];

    private static readonly HashSet<string> Interactive = ["Button", "ToggleButton", "CheckBox", "RadioButton", "TextBox", "ComboBox", "DatePicker", "DataGrid", "ListBox", "ListView", "TabControl", "Expander", "Slider"];
    private static readonly HashSet<string> NamedByOwnText = ["Button", "ToggleButton", "CheckBox", "RadioButton"];

    private static bool Has(XElement e, string attr) => e.Attributes().Any(a => a.Name.LocalName == attr && a.Value.Trim().Length > 0);
    private static bool IsLiteral(XElement e, string attr) => e.Attributes().Any(a => a.Name.LocalName == attr && a.Value.Trim().Length > 0 && !a.Value.TrimStart().StartsWith("{"));

    /// <summary>A resource-layer key (<c>{loc:T key}</c>) is the element's own visible text, exactly like a literal (WP6b).</summary>
    private static bool IsKey(XElement e, string attr) => e.Attributes().Any(a => a.Name.LocalName == attr && a.Value.TrimStart().StartsWith("{loc:T "));

    [Fact]
    public void Every_interactive_element_of_the_baseline_pages_has_an_automation_name()
    {
        var missing = new List<string>();
        foreach (var rel in Scope)
        {
            var doc = XDocument.Parse(Read(rel));
            foreach (var e in doc.Descendants().Where(e => Interactive.Contains(e.Name.LocalName)))
            {
                bool named = Has(e, "AutomationProperties.Name") || Has(e, "AutomationProperties.LabeledBy")
                             || (NamedByOwnText.Contains(e.Name.LocalName) && (IsLiteral(e, "Content") || IsKey(e, "Content")))
                             || (e.Name.LocalName == "Expander" && (IsLiteral(e, "Header") || IsKey(e, "Header")));
                if (!named) missing.Add($"{rel[^1]}: <{e.Name.LocalName}> {string.Join(" ", e.Attributes().Take(3).Select(a => a.Name.LocalName + "=" + a.Value))}");
            }
        }
        Assert.True(missing.Count == 0, "Elements without an automation name:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_data_grid_column_of_the_baseline_pages_has_a_header()
    {
        var missing = new List<string>();
        foreach (var rel in Scope)
            foreach (var c in XDocument.Parse(Read(rel)).Descendants().Where(e => e.Name.LocalName.StartsWith("DataGrid") && e.Name.LocalName.EndsWith("Column") && !e.Name.LocalName.Contains('.')))
                if (!Has(c, "Header")) missing.Add($"{rel[^1]}: {c.Name.LocalName} {c.Attributes().FirstOrDefault(a => a.Name.LocalName == "Binding")?.Value}");
        Assert.True(missing.Count == 0, "Columns without a header:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_baseline_pages_have_no_fixed_tiny_fonts()
    {
        var tiny = new List<string>();
        foreach (var rel in Scope)
            foreach (Match m in Regex.Matches(Read(rel), @"FontSize=""([0-9.]+)"""))
                if (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) < 12) tiny.Add($"{rel[^1]}: FontSize={m.Groups[1].Value}");
        Assert.True(tiny.Count == 0, "Fonts below 12:\n" + string.Join("\n", tiny));
        Assert.DoesNotContain("FontSize = ", Read("Views", "Controls", "SeverityBadge.xaml.cs"));
    }

    // ---- U4 / U8 the new controls ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_finding_card_has_the_four_actions_with_their_access_keys_and_help_text()
    {
        var xaml = Read("Views", "Controls", "FindingCard.xaml");
        foreach (var a in FindingCardActions.All)
        {
            // WP6b: the texts are keys of the resource layer; the model and the XAML name the same keys, in both languages.
            Assert.Matches($@"Content=""\{{loc:T {Regex.Escape(a.AccessTextKey)}\}}""[^>]*AutomationProperties\.HelpText=""\{{loc:T {Regex.Escape(a.HelpKey)}\}}""", xaml);
            Assert.Contains("_", a.AccessText);
        }
        Assert.Equal(4, Regex.Matches(xaml, @"<Button\b").Count);
    }

    [Fact]
    public void The_finding_card_states_severity_state_and_verification_as_text()
    {
        var xaml = Read("Views", "Controls", "FindingCard.xaml");
        foreach (var path in new[] { "Card.Title", "Card.HumanSummary", "Card.SeverityText", "Card.SeverityIcon", "Card.StateLabel", "Card.VerificationLabel" })
            Assert.Contains("{Binding " + path, xaml);
        Assert.Contains("{views:Term provenance", xaml);
        Assert.Contains("{views:Term verification", xaml);
        Assert.Contains("SeverityLabels.Icon", Read("Views", "Controls", "SeverityBadge.xaml.cs"));
    }

    [Fact]
    public void The_know_think_dont_know_control_uses_the_three_documented_headings_and_marks_each_column_with_a_symbol()
    {
        var xaml = Read("Views", "Controls", "KnowThinkDontKnowPanel.xaml");
        foreach (var key in new[] { "ktd.ce_stim", "ktd.ce_suspectam", "ktd.ce_nu_putem_demonstra" }) Assert.Contains("{loc:T " + key + "}", xaml);   // WP6b: headings are keys
        Assert.Equal("CE ȘTIM", Loc.T("ktd.ce_stim", AppLanguage.Romanian));
        Assert.Equal("CE ȘTIM", KnowThinkDontKnow.KnowHeading);
        foreach (var sym in new[] { "✓", "⚠", "?" }) Assert.Contains(sym, xaml);
        foreach (var path in new[] { "Know", "Think", "DontKnow" }) Assert.Contains("ItemsSource=\"{Binding " + path + "}\"", xaml);
    }

    [Fact]
    public void The_findings_grid_stays_and_the_card_is_its_selected_item_panel()
    {
        var inv = Read("Views", "InvestigationView.xaml");
        Assert.Contains("ItemsSource=\"{Binding Findings}\"", inv);
        Assert.Contains("SelectedItem=\"{Binding SelectedFinding}\"", inv);
        Assert.Contains("<ctrl:FindingCard DataContext=\"{Binding SelectedCard}\"", inv);
        foreach (var header in new[] { "Severitate", "Clasificare", "Stare", "Verificare", "Prima (UTC)", "Regulă", "Constatare", "Detalii" })
        {
            // WP6b: the grid headers are keys; the Romanian text (what the operator saw in WP6a) is unchanged.
            var keys = Loc.Keys(AppLanguage.Romanian).Where(k => Loc.T(k, AppLanguage.Romanian) == header).ToList();
            Assert.True(keys.Any(k => inv.Contains($"Header=\"{{loc:T {k}}}\"")), $"no grid header uses a key whose Romanian text is '{header}'");
        }
    }
}
