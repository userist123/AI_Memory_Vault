using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.UI.Views;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// WP18 S3: the language level (Simple for every account, Expert as a switch), the per-account preference, the glossary terms of the control
/// line, and a lint over the XAML of the role pages: no bare technical term in a visible literal, except inside parentheses or through the glossary.
/// </summary>
[Collection(GlobalStateCollection.Name)]
public sealed class Wp18LanguageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-lang-" + Guid.NewGuid().ToString("N"));
    public Wp18LanguageTests() { Directory.CreateDirectory(_dir); LanguageLevelContext.ResetForTests(); }
    public void Dispose() { LanguageLevelContext.ResetForTests(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Simple_is_the_default_for_every_account_and_the_preference_is_per_account()
    {
        var prefs = new LanguagePreferences(_dir);
        Assert.Equal(UiLanguageLevel.Simple, prefs.Load("DOM\\admin.principal"));
        Assert.Equal(UiLanguageLevel.Simple, prefs.Load("operator"));
        Assert.True(prefs.Save("DOM\\admin.principal", UiLanguageLevel.Expert));
        Assert.Equal(UiLanguageLevel.Expert, prefs.Load("DOM\\admin.principal"));
        Assert.Equal(UiLanguageLevel.Expert, prefs.Load("dom\\ADMIN.PRINCIPAL"));   // same account, any case
        Assert.Equal(UiLanguageLevel.Simple, prefs.Load("operator"));               // another account is untouched
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{not json");
        Assert.Equal(UiLanguageLevel.Simple, prefs.Load("broken"));                 // unreadable = Simple, never an error
    }

    [Fact]
    public void Changing_the_level_notifies_once_and_glossary_terms_follow_it()
    {
        int changes = 0;
        LanguageLevelContext.Changed += () => changes++;
        var term = new TermExtension("evtx");
        Assert.Equal("Jurnale Windows", term.Resolve());
        LanguageLevelContext.Set(UiLanguageLevel.Expert);
        LanguageLevelContext.Set(UiLanguageLevel.Expert);
        Assert.Equal(1, changes);
        Assert.Contains("EVTX", term.Resolve());
        Assert.Contains("Jurnale Windows", term.Resolve());
        LanguageLevelContext.Set(UiLanguageLevel.Simple);
        Assert.Equal("Jurnale Windows", term.Resolve());
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("Hardware ID", "Identificatorul stației")]
    [InlineData("NetworkList", "Rețele la care s-a conectat stația")]
    [InlineData("USBSTOR", "Suporturi USB conectate")]
    [InlineData("auditpol", "Setările de jurnalizare")]
    [InlineData("RecordID", "Numărul înregistrării din jurnal")]
    [InlineData("CRL", "Lista certificatelor revocate")]
    [InlineData("CA", "Autoritatea de certificare")]
    [InlineData("SHA-256", "Amprenta fișierului")]
    [InlineData("SRUM", "Consumul de rețea al programelor")]
    [InlineData("RDP", "Acces de la distanță")]
    [InlineData("GPO", "Politica de grup")]
    public void The_control_line_terms_of_wp18_are_in_the_single_glossary_with_an_explanation(string technical, string romanian)
    {
        var t = Glossary.Find(technical);
        Assert.NotNull(t);
        Assert.Equal(romanian, t!.Romanian);
        Assert.False(string.IsNullOrWhiteSpace(t.Explanation));
        Assert.False(string.IsNullOrWhiteSpace(t.English));
    }

    // ---- XAML lint: the role pages in the Simple level ----------------------------------------------------------------------------

    private static string AppDir()
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

    /// <summary>The pages a non-technical user reaches from the role intents (prompt §2 / §6); legacy pages under "Avansat" are exempt (decision 5).</summary>
    private static readonly string[] RolePages =
    [
        "Views/HomeView.xaml", "Views/StationControlView.xaml", "Views/InvestigationView.xaml", "Views/RegisterView.xaml", "Views/ProcedureProfileView.xaml",
        "Views/AuthView.xaml", "Views/SignInWindow.xaml", "Views/GlossaryWindow.xaml", "Views/Controls/FindingCard.xaml", "Views/Controls/KnowThinkDontKnowPanel.xaml",
    ];

    /// <summary>Jargon that must not appear bare in a visible literal: the glossary's technical names plus acronyms without a glossary entry.</summary>
    private static IEnumerable<string> Jargon() =>
        Glossary.Terms.Select(t => t.Technical).Concat(["HWID", "PCAPNG", "LDAP", "NTUSER", "WDigest", "SMBv1", "thumbprint", "wevtutil", "EvidenceId", "ShimCache"]);

    [Fact]
    public void Role_pages_show_no_bare_technical_term_in_a_visible_literal()
    {
        var jargon = Jargon().ToList();
        var violations = new List<string>();
        foreach (var rel in RolePages)
        {
            var xaml = File.ReadAllText(Path.Combine(AppDir(), rel.Replace('/', Path.DirectorySeparatorChar)));
            foreach (Match m in Regex.Matches(xaml, @"\b(Content|Text|Header|Title|ToolTip|AutomationProperties\.Name|AutomationProperties\.HelpText)=""([^""{][^""]*)"""))
            {
                var visible = Regex.Replace(m.Groups[2].Value, @"\([^)]*\)", "");   // a technical name in parentheses is the allowed form
                foreach (var j in jargon)
                    if (Regex.IsMatch(visible, $@"(?<![\p{{L}}\p{{N}}-]){Regex.Escape(j)}(?![\p{{L}}\p{{N}}-])"))
                        violations.Add($"{rel}: {m.Groups[1].Value}=\"{m.Groups[2].Value}\" — „{j}”");
            }
        }
        Assert.True(violations.Count == 0, "Bare technical terms in role pages:\n" + string.Join("\n", violations.Distinct()));
    }

    [Fact]
    public void Header_has_the_language_switch_and_the_glossary_button_with_automation_names()
    {
        var main = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        Assert.Contains("IsChecked=\"{Binding IsExpertLevel", main);
        Assert.Contains("OpenGlossaryCommand", main);
        Assert.Matches(@"AutomationProperties\.Name=""Nivel de limbaj", main);
        Assert.Matches(@"Content=""Ce înseamnă\?""", main);
    }
}

/// <summary>Tests that change process-wide state (LanguageLevelContext) run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalStateCollection { public const string Name = "global-state"; }
