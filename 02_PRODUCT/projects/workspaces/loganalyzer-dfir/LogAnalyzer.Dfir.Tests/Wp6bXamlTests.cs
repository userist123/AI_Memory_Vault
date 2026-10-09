using System.Text.RegularExpressions;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Language;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// WP6b / U17: scans of the XAML sources (the app project only runs on Windows, the XAML text does not). Every <c>{loc:T key}</c> must name a key that exists in both
/// languages, and the pages of this package must not carry literal user-facing text any more.
/// </summary>
public class Wp6bXamlTests
{
    private static string AppDir() => Wp6aXamlTests.AppDir();

    private static IEnumerable<string> AllXaml() =>
        Directory.EnumerateFiles(AppDir(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>The pages and controls localised in this package (item 3 of the WP6b spec). Everything else is listed in LOCALISATION.md.</summary>
    internal static readonly string[][] Localised =
    [
        ["MainWindow.xaml"], ["Views", "HomeView.xaml"], ["Views", "SignInWindow.xaml"], ["Views", "AuthView.xaml"], ["Views", "InvestigationView.xaml"],
        ["Views", "RegisterView.xaml"], ["Views", "ProcedureProfileView.xaml"],
        ["Views", "Controls", "FindingCard.xaml"], ["Views", "Controls", "KnowThinkDontKnowPanel.xaml"], ["Views", "Controls", "SeverityBadge.xaml"],
    ];

    /// <summary>Literal texts that are brand names or identifiers, or a design-time placeholder that code overwrites; they are not translated.</summary>
    private static readonly HashSet<string> NotTranslated = ["LOGANALYZER", "v2.5 PRO", "Ctrl+K", "SQLCIPHER (AES-256)", "INFO", "SHA-256"];

    private static readonly string[] TextAttributes =
        ["Content", "Text", "Header", "ToolTip", "Title", "Watermark", "Label", "AutomationProperties.Name", "AutomationProperties.HelpText"];

    private static bool HasWords(string s) => Regex.IsMatch(Regex.Replace(s, @"\{[^}]*\}", ""), @"[A-Za-zĂÂÎȘȚăâîșț]{2}");

    private static IEnumerable<string> References(string xaml) =>
        Regex.Matches(xaml, @"\{loc:[TF]\s+([A-Za-z0-9_.]+)").Select(m => m.Groups[1].Value);

    [Fact]
    public void Every_key_used_in_xaml_exists_in_both_languages_or_is_declared_romanian_only()
    {
        var used = new List<(string File, string Key)>();
        foreach (var f in AllXaml()) foreach (var k in References(File.ReadAllText(f))) used.Add((Path.GetFileName(f), k));
        Assert.NotEmpty(used);
        foreach (var (file, key) in used)
        {
            Assert.True(Loc.Has(key, AppLanguage.Romanian), $"{file}: key '{key}' has no Romanian text");
            Assert.True(Loc.Has(key, AppLanguage.English) || Loc.RomanianOnly.ContainsKey(key), $"{file}: key '{key}' has no English text and is not declared RO-only");
        }
    }

    [Fact]
    public void The_localised_pages_carry_no_literal_user_facing_text()
    {
        var literal = new List<string>();
        foreach (var rel in Localised)
        {
            var doc = XDocument.Parse(File.ReadAllText(Path.Combine([AppDir(), .. rel])));
            foreach (var e in doc.Descendants())
            {
                foreach (var a in e.Attributes())
                {
                    var n = a.Name.LocalName == "Name" && a.Name.Namespace != XNamespace.None ? a.Name.LocalName : a.Name.LocalName;
                    var full = a.Name.Namespace == XNamespace.None ? a.Name.LocalName : a.Name.LocalName;   // attached properties keep their dotted local name
                    if (!TextAttributes.Contains(full)) continue;
                    var v = a.Value.Trim();
                    if (v.StartsWith('{') || NotTranslated.Contains(v) || !HasWords(v)) continue;
                    literal.Add($"{rel[^1]}: {full}=\"{v}\"");
                }
                if (e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") is "Text" or "Content" or "ToolTip" or "Header" or "Title"
                    && (string?)e.Attribute("Value") is { } sv && !sv.StartsWith('{') && HasWords(sv) && !NotTranslated.Contains(sv))
                    literal.Add($"{rel[^1]}: Setter {e.Attribute("Property")!.Value}=\"{sv}\"");
                if (e.Name.LocalName is "TextBlock" or "Run" or "Button" or "Label" or "CheckBox" or "RadioButton" && !e.HasElements && e.Value.Trim() is { Length: > 0 } inner
                    && !inner.StartsWith('{') && HasWords(inner) && !NotTranslated.Contains(inner))
                    literal.Add($"{rel[^1]}: <{e.Name.LocalName}>{inner}</{e.Name.LocalName}>");
            }
            // literal words inside binding formats / fallbacks ("Sesiune: {0}"); pure date patterns and bullets are formats, not text
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine([AppDir(), .. rel])), @"(?:StringFormat|FallbackValue|TargetNullValue)=('[^']*'|[^,}]*)"))
            {
                var v = m.Groups[1].Value.Trim('\'');
                if (HasWords(v) && !Regex.IsMatch(v, @"^[yMdHhmsfz:\-\. /{}0-9•%]*$")) literal.Add($"{rel[^1]}: {m.Value}");
            }
        }
        Assert.True(literal.Count == 0, "Literal user-facing text (use {loc:T key}):\n" + string.Join("\n", literal));
    }

    [Fact]
    public void The_language_selector_is_on_the_main_window_and_the_sign_in_window_and_stores_the_choice_per_user()
    {
        foreach (var rel in new[] { new[] { "MainWindow.xaml" }, ["Views", "SignInWindow.xaml"] })
        {
            var x = File.ReadAllText(Path.Combine([AppDir(), .. rel]));
            Assert.Contains("LanguageChoice.Options", x);
            Assert.Contains("Path=Selected, Mode=TwoWay", x);
        }
        var app = File.ReadAllText(Path.Combine(AppDir(), "App.xaml.cs"));
        Assert.Contains("LanguageChoice.Instance.ApplyStored()", app);
        Assert.Contains("settings.json", File.ReadAllText(Path.Combine(AppDir(), "..", "LogAnalyzer.Dfir.Core", "Language", "UserSettings.cs")));
    }

    [Fact]
    public void The_custody_label_with_its_compliance_tag_starts_with_the_glossary_phrase_in_each_language()
    {
        foreach (var l in new[] { AppLanguage.Romanian, AppLanguage.English })
            Assert.StartsWith(Glossary.Human("chain_of_custody", l), Loc.T("shell.nav.custody_nis2", l));
    }
}
