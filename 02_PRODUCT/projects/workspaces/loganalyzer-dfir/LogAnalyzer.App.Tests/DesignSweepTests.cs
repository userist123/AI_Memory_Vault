using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Design plan steps 2-5 ("Registru" theme, docs/design/PLAN.md), enforced on the source text: one page header (title style + register rule),
/// no pictographic characters, no colours outside the tokens, labels written as sentences and in Romanian. The checks read the files as text, so they
/// need no window and no handle.
/// </summary>
public sealed class DesignSweepTests
{
    private static string RepoDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("LogAnalyzer.App not found above " + AppContext.BaseDirectory);
    }

    private static string AppDir() => Path.Combine(RepoDir(), "LogAnalyzer.App");

    private static string Read(string path) => File.ReadAllText(path);

    private static IEnumerable<string> Files(string dir, string pattern)
    {
        foreach (var f in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
        {
            var n = f.Replace('\\', '/');
            if (n.Contains("/obj/") || n.Contains("/bin/")) continue;
            yield return f;
        }
    }

    private static string StripXmlComments(string xaml) => Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);

    // ---------------------------------------------------------------- step 2: page header

    /// <summary>The pages of the main menu (MainWindow tabs and the unclassified-edition feature pages) that start with a title.</summary>
    private static readonly string[] TitledPages =
    [
        "Views/StationControlView.xaml", "Views/MediaCenterView.xaml", "Views/TransferRegisterView.xaml", "Views/RegisterView.xaml",
        "Views/ProcedureProfileView.xaml", "Views/InvestigationView.xaml", "Views/PolicyView.xaml", "Views/IncidentTimelineView.xaml",
        "Views/ChainOfCustodyView.xaml", "Views/DataCollectionView.xaml", "Views/DashboardView.xaml", "Views/AlertsAndRulesView.xaml",
        "Views/RiskAnalysisView.xaml", "Views/AttackStorylineView.xaml", "Views/MitreMatrixView.xaml", "Views/ThreatIntelView.xaml",
        "Views/ActiveDirectoryAuditView.xaml", "Views/KerberosAndLateralMovementView.xaml", "Views/AuthView.xaml",
        "Edition/Unclassified/ProcessContainmentView.xaml", "Edition/Unclassified/DomainInvestigationView.xaml",
        "Views/EventExplorerView.xaml", "Views/RegistryForensicsView.xaml", "Views/LiveSocView.xaml",
    ];

    /// <summary>Pages that must also carry a one-line explanation (PageLeadStyle) under the title: the former untitled advanced pages and the storyline.</summary>
    private static readonly string[] LeadPages =
    [
        "Views/EventExplorerView.xaml", "Views/RegistryForensicsView.xaml", "Views/LiveSocView.xaml", "Views/AttackStorylineView.xaml",
    ];

    [Fact]
    public void Every_titled_page_uses_PageTitleStyle_and_has_exactly_one_register_rule()
    {
        var problems = new List<string>();
        foreach (var rel in TitledPages)
        {
            var x = StripXmlComments(Read(Path.Combine(AppDir(), rel)));
            if (!x.Contains("Style=\"{StaticResource PageTitleStyle}\"")) problems.Add(rel + ": no PageTitleStyle");
            var rules = Regex.Matches(x, Regex.Escape("Style=\"{StaticResource PageRuleStyle}\"")).Count;
            if (rules != 1) problems.Add($"{rel}: {rules} PageRuleStyle (expected 1)");
            // the title must not carry a literal size any more: the style owns it
            foreach (Match m in Regex.Matches(x, "<TextBlock[^>]*PageTitleStyle[^>]*>"))
                if (Regex.IsMatch(m.Value, "FontSize=|FontWeight=|Foreground=")) problems.Add(rel + ": title with literal font attributes: " + m.Value);
        }
        foreach (var rel in LeadPages)
        {
            var x = StripXmlComments(Read(Path.Combine(AppDir(), rel)));
            var leads = Regex.Matches(x, Regex.Escape("Style=\"{StaticResource PageLeadStyle}\"")).Count;
            if (leads != 1) problems.Add($"{rel}: {leads} PageLeadStyle (expected 1)");
            if (!x.Contains("Style=\"{StaticResource PageTitleStyle}\"")) problems.Add(rel + ": no PageTitleStyle");
            var rules = Regex.Matches(x, Regex.Escape("Style=\"{StaticResource PageRuleStyle}\"")).Count;
            if (rules != 1) problems.Add($"{rel}: {rules} PageRuleStyle (expected 1)");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---------------------------------------------------------------- one accent button per screen

    /// <summary>Files allowed more than one AccentButtonStyle, with the reason (the buttons are never visible together).</summary>
    private static readonly Dictionary<string, (int Max, string Reason)> AccentExceptions = new()
    {
        ["Edition/Unclassified/DomainInvestigationView.xaml"] = (3, "one accent button per tab of a TabControl; only one tab is visible at a time"),
    };

    [Fact]
    public void Every_page_and_window_has_at_most_one_AccentButtonStyle_and_the_main_window_has_none()
    {
        var problems = new List<string>();
        foreach (var dir in new[] { "Views", "Edition" })
            foreach (var f in Files(Path.Combine(AppDir(), dir), "*.xaml"))
            {
                var rel = Path.GetRelativePath(AppDir(), f).Replace('\\', '/');
                var n = Regex.Matches(StripXmlComments(Read(f)), "AccentButtonStyle").Count;
                var max = AccentExceptions.TryGetValue(rel, out var e) ? e.Max : 1;
                if (n > max) problems.Add($"{rel}: {n} AccentButtonStyle (max {max})");
            }
        var main = StripXmlComments(Read(Path.Combine(AppDir(), "MainWindow.xaml")));
        if (Regex.IsMatch(main, "(?<![A-Za-z])AccentButtonStyle")) problems.Add("MainWindow.xaml still uses AccentButtonStyle (the frame uses FrameAccentButtonStyle)");
        Assert.Contains("x:Key=\"FrameAccentButtonStyle\"", Read(Path.Combine(AppDir(), "Themes/Components/Frame.xaml")));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_Home_question_stays_at_29_SemiBold_without_a_rule()
    {
        var x = StripXmlComments(Read(Path.Combine(AppDir(), "Views/HomeView.xaml")));
        Assert.Matches("<TextBlock Text=\"Ce doriți să faceți\\?\"[^>]*FontSize=\"29\" FontWeight=\"SemiBold\"", x);
        Assert.DoesNotContain("PageRuleStyle", x);
    }

    [Fact]
    public void Page_xaml_defines_the_title_lead_and_rule_styles_and_App_xaml_merges_it()
    {
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(Path.Combine(AppDir(), "Themes/Components/Page.xaml"));
        XElement StyleOf(string key) => doc.Root!.Elements(p + "Style").Single(s => (string?)s.Attribute(x + "Key") == key);
        string Setter(XElement style, string prop) => style.Elements(p + "Setter").Single(s => (string?)s.Attribute("Property") == prop).Attribute("Value")!.Value;

        var title = StyleOf("PageTitleStyle");
        Assert.Equal("TextBlock", (string?)title.Attribute("TargetType"));
        Assert.Equal("24", Setter(title, "FontSize"));
        Assert.Equal("SemiBold", Setter(title, "FontWeight"));
        Assert.Equal("{StaticResource TextPrimaryBrush}", Setter(title, "Foreground"));
        Assert.Equal("Wrap", Setter(title, "TextWrapping"));
        Assert.Equal("0,0,0,4", Setter(title, "Margin"));

        var lead = StyleOf("PageLeadStyle");
        Assert.Equal("TextBlock", (string?)lead.Attribute("TargetType"));
        Assert.Equal("14", Setter(lead, "FontSize"));
        Assert.Equal("{StaticResource TextSecondaryBrush}", Setter(lead, "Foreground"));
        Assert.Equal("Wrap", Setter(lead, "TextWrapping"));
        Assert.Equal("0,0,0,0", Setter(lead, "Margin"));

        var rule = StyleOf("PageRuleStyle");
        Assert.Equal("Border", (string?)rule.Attribute("TargetType"));
        Assert.Equal("1", Setter(rule, "Height"));
        Assert.Equal("{StaticResource StrongBorderBrush}", Setter(rule, "Background"));
        Assert.Equal("0,12,0,16", Setter(rule, "Margin"));
        Assert.Equal("True", Setter(rule, "SnapsToDevicePixels"));

        var app = Read(Path.Combine(AppDir(), "App.xaml"));
        Assert.True(app.IndexOf("Themes/Components/Cards.xaml", StringComparison.Ordinal) < app.IndexOf("Themes/Components/Page.xaml", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- step 3: no pictographic characters

    /// <summary>
    /// Emoji and dingbat blocks plus the characters that were used as icons (gear, warning, info, check marks, star, pencil, dot, stop). Private-use glyphs
    /// of Segoe Fluent Icons (U+E000-U+F8FF) are the replacement and are not listed. The first alternative is the surrogate range of U+1F000-U+1FBFF.
    /// </summary>
    private static readonly Regex Pictographic = new("[\uD83C-\uD83E][\uDC00-\uDFFF]|[\u2600-\u27BF\u2B00-\u2BFF\uFE0F\u2139\u25CF\u23E9-\u23FF]", RegexOptions.Compiled);
    private static readonly Regex PictographicEscape = new("\\\\u(?:2[67][0-9a-fA-F]{2}|2[Bb][0-9a-fA-F]{2}|FE0F|fe0f|2139|25[Cc][Ff])|\\\\U0001[Ff][0-9a-fA-F]{3}", RegexOptions.Compiled);

    /// <summary>
    /// Data marks that may stay as characters, with the reason. Empty: the bookmark and note marks of the timeline table now come from the icon font
    /// (private-use glyphs E734 / E70F) and the column header has an automation name.
    /// </summary>
    private static readonly string[] PictographicExceptions = [];

    private static IEnumerable<string> StringLiterals(string cs)
    {
        // string literals only; comments are skipped so documentation may name a symbol
        foreach (Match m in Regex.Matches(cs, "\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"|//[^\\r\\n]*|/\\*.*?\\*/", RegexOptions.Singleline))
            if (m.Value.StartsWith('"')) yield return m.Value;
    }

    [Fact]
    public void No_xaml_file_contains_a_pictographic_character()
    {
        var hits = new List<string>();
        foreach (var f in Files(AppDir(), "*.xaml"))
        {
            var t = Read(f);
            foreach (Match m in Pictographic.Matches(t))
            {
                var rel = Path.GetRelativePath(AppDir(), f);
                if (PictographicExceptions.Contains(rel)) continue;
                hits.Add($"{rel}: U+{(int)m.Value[0]:X4}");
            }
        }
        Assert.True(hits.Count == 0, string.Join("\n", hits.Distinct()));
    }

    [Fact]
    public void No_string_literal_of_the_view_models_views_services_or_network_services_contains_a_pictographic_character()
    {
        var dirs = new[]
        {
            Path.Combine(AppDir(), "ViewModels"), Path.Combine(AppDir(), "Views"), Path.Combine(AppDir(), "Services"),
            Path.Combine(RepoDir(), "LogAnalyzer.Core", "Services", "Network"),
        };
        var hits = new List<string>();
        foreach (var dir in dirs)
            foreach (var f in Files(dir, "*.cs"))
                foreach (var lit in StringLiterals(Read(f)))
                    if (Pictographic.IsMatch(lit) || PictographicEscape.IsMatch(lit))
                    {
                        var rel = Path.GetRelativePath(RepoDir(), f);
                        if (!PictographicExceptions.Contains(rel)) hits.Add(rel + ": " + lit);
                    }
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    // ---------------------------------------------------------------- step 4: colours come from the tokens

    private static readonly Dictionary<string, string> TokenKeyOf = new()
    {
        ["Critical"] = "CriticalColor", ["High"] = "HighColor", ["Medium"] = "MediumColor", ["Low"] = "LowColor", ["Success"] = "SuccessColor",
        ["PrimaryAccent"] = "PrimaryAccentColor", ["EvidenceAccent"] = "EvidenceAccentColor", ["TextPrimary"] = "TextPrimaryColor",
        ["TextSecondary"] = "TextSecondaryColor", ["TextMuted"] = "TextMutedColor", ["Surface1"] = "Surface1Color", ["Surface2"] = "Surface2Color",
        ["Surface3"] = "Surface3Color", ["Border"] = "HairlineBorderColor", ["StrongBorder"] = "StrongBorderColor",
    };

    private static string TokenValue(string name)
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Themes/Tokens/ColorTokens.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        if (name == "BadgeCriticalBg")
            return doc.Descendants().Single(e => (string?)e.Attribute(x + "Key") == "BadgeCriticalBgBrush").Attribute("Color")!.Value;
        var key = TokenKeyOf[name];
        return doc.Descendants().Single(e => (string?)e.Attribute(x + "Key") == key).Value.Trim();
    }

    [Fact]
    public void ThemeColors_constants_equal_the_tokens_of_ColorTokens_xaml()
    {
        var fields = typeof(ThemeColors).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).ToList();
        Assert.Equal(16, fields.Count);
        foreach (var f in fields)
            Assert.Equal(TokenValue(f.Name), (string)f.GetRawConstantValue()!, ignoreCase: true);
    }

    [Fact]
    public void Core_colour_literals_carry_the_token_they_copy_and_equal_it()
    {
        var core = Path.Combine(RepoDir(), "LogAnalyzer.Core");
        var seen = 0;
        foreach (var f in Files(core, "*.cs"))
            foreach (Match m in Regex.Matches(Read(f), "\"(#[0-9A-Fa-f]{6})\"\\s*/\\*\\s*Registru theme token (\\w+)\\s*\\*/"))
            {
                seen++;
                Assert.Equal(TokenValue(m.Groups[2].Value), m.Groups[1].Value, ignoreCase: true);
            }
        Assert.True(seen >= 12, "expected the Core engines to carry token-tagged colours, found " + seen);
    }

    /// <summary>Hex literals that may stay in a page, with the reason. None today.</summary>
    private static readonly string[] HexExceptions = [];

    [Fact]
    public void No_xaml_outside_the_tokens_has_a_literal_hex_colour()
    {
        var hits = new List<string>();
        foreach (var f in Files(AppDir(), "*.xaml"))
        {
            var rel = Path.GetRelativePath(AppDir(), f).Replace('\\', '/');
            if (rel.StartsWith("Themes/Tokens/")) continue;
            foreach (Match m in Regex.Matches(StripXmlComments(Read(f)), "[\"'>]#[0-9A-Fa-f]{3,8}[\"'<]"))
                if (!HexExceptions.Contains(rel)) hits.Add(rel + ": " + m.Value);
        }
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    private static readonly string[] OldThemeColors =
        ["#121622", "#1e2538", "#38bdf8", "#00ff87", "#ef4444", "#e1e7f0", "#0f172a", "#1e293b", "#22d3ee", "#a78bfa", "#f59e0b", "#10b981"];

    [Fact]
    public void View_models_and_the_APT_engine_use_none_of_the_old_dark_theme_colours()
    {
        var files = Files(Path.Combine(AppDir(), "ViewModels"), "*.cs").Append(Path.Combine(RepoDir(), "LogAnalyzer.Core", "Services", "AptAttributionEngine.cs")).ToList();
        Assert.True(files.Count > 5);
        var hits = new List<string>();
        foreach (var f in files)
        {
            var t = Read(f);
            foreach (var c in OldThemeColors)
                if (t.Contains(c, StringComparison.OrdinalIgnoreCase)) hits.Add(Path.GetFileName(f) + ": " + c);
        }
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    // ---------------------------------------------------------------- step 5: sentences, Romanian

    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal)
    {
        "MITRE", "EVTX", "JSON", "YARA", "STIX", "MISP", "LOLBAS", "SHA", "NIST", "NTUSER", "SYSTEM", "SOFTWARE", "SECURITY", "USBSTOR",
        "BUILTIN", "TEMP", "GDPR", "DRAFT", "VALIDATED", "TESTED", "APPROVED", "DEPLOYED", "VERIFIED", "CERT", "INFO", "LIVE",
    };

    [Fact]
    public void No_literal_label_of_a_page_has_two_consecutive_all_capital_words()
    {
        var attr = new Regex("\\b(?:Text|Content|Header)=\"([^\"{][^\"]*)\"");
        var caps = new Regex("^[A-ZĂÂÎȘȚ]{4,}$");
        var hits = new List<string>();
        foreach (var f in Files(AppDir(), "*.xaml"))
        {
            var rel = Path.GetRelativePath(AppDir(), f).Replace('\\', '/');
            if (rel.StartsWith("Themes/")) continue;
            foreach (Match m in attr.Matches(StripXmlComments(Read(f))))
            {
                var words = Regex.Split(m.Groups[1].Value.Replace("&amp;", "&"), "\\s+");
                var prev = false;
                foreach (var w in words)
                {
                    var core = w.Trim('(', ')', ',', '.', ':', ';', '„', '”');
                    var isCaps = caps.IsMatch(core) && !Acronyms.Contains(core);
                    if (isCaps && prev) { hits.Add($"{rel}: {m.Groups[1].Value}"); break; }
                    prev = isCaps;
                }
            }
        }
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    [Fact]
    public void The_English_button_labels_of_the_data_collection_page_are_Romanian_sentences()
    {
        var x = Read(Path.Combine(AppDir(), "Views/DataCollectionView.xaml"));
        Assert.Contains("Rulează trierea pe loc", x);
        Assert.Contains("Sanitizare mediu și certificat PDF", x);
        Assert.Contains("Sigilează pachetul cazului", x);
        Assert.DoesNotContain("Live Triage", x);
        Assert.DoesNotContain("Pachet Caz", x);
    }

    [Fact]
    public void No_page_label_is_written_in_Title_Case_Romanian_or_left_in_English()
    {
        var banned = new[] { "Before / After", "Timestamp", "Off-Hours", "Impossible Travel", "Conditional Access", "Live Triage" };
        foreach (var f in Files(AppDir(), "*.xaml"))
        {
            var rel = Path.GetRelativePath(AppDir(), f).Replace('\\', '/');
            if (rel.StartsWith("Themes/")) continue;
            var t = StripXmlComments(Read(f));
            foreach (var b in banned)
                if (Regex.IsMatch(t, "(?:Text|Content|Header)=\"(?!\\{)[^\"]*" + Regex.Escape(b))) Assert.Fail(rel + " still has " + b);
        }
    }

    // ---------------------------------------------------------------- display wording of state values (no capitals, no English)

    [Fact]
    public void State_values_shown_on_screen_are_sentences_while_the_report_constants_stay_as_they_are()
    {
        Assert.Equal("Izolare ridicată (verificat)", LogAnalyzer.Dfir.Windows.Containment.ContainmentText.Display(LogAnalyzer.Dfir.Windows.Containment.ContainmentText.State(LogAnalyzer.Dfir.Windows.Containment.ContainmentState.Released)));
        Assert.Equal("Izolat (verificat)", LogAnalyzer.Dfir.Windows.Containment.ContainmentText.Display("IZOLAT (VERIFICAT)"));
        Assert.Equal("Izolare eșuată", LogAnalyzer.Dfir.Windows.Containment.ContainmentText.Display("IZOLARE EȘUATĂ"));
        // the report constant is untouched
        Assert.Equal("IZOLARE RIDICATĂ (VERIFICAT)", LogAnalyzer.Dfir.Windows.Containment.ContainmentText.State(LogAnalyzer.Dfir.Windows.Containment.ContainmentState.Released));
        Assert.Equal("Informativ", LogAnalyzer.UI.Views.LiveLevelLabelConverter.Label("Information"));
        Assert.Equal("Altceva", LogAnalyzer.UI.Views.LiveLevelLabelConverter.Label("Altceva"));
        Assert.StartsWith("Informativ", LogAnalyzer.Core.Models.TimelineSummary.From(new[] { new LogAnalyzer.Core.Models.TimelineItem { Timestamp = DateTime.UtcNow, Severity = "Information" } }).Peak);
    }

    [Fact]
    public void The_dashboard_containment_card_and_the_policy_page_use_no_capital_state_words_or_English()
    {
        var dash = StripXmlComments(Read(Path.Combine(AppDir(), "Views/DashboardView.xaml")));
        foreach (var w in new[] { "\"OPRITĂ\"", "\"PORNITĂ\"", "\"INDISPONIBILĂ\"" }) Assert.DoesNotContain(w, dash);
        var pol = Read(Path.Combine(AppDir(), "Views/PolicyView.xaml"));
        Assert.Contains("Stări: Ciornă → Validată", pol);
        foreach (var w in new[] { "DRAFT", "VALIDATED", "DEPLOYED" }) Assert.DoesNotContain(w, pol);
        var live = StripXmlComments(Read(Path.Combine(AppDir(), "Views/LiveSocView.xaml")));
        Assert.DoesNotContain("SuccessButtonStyle", live);
        Assert.DoesNotContain("DangerButtonStyle", live);
        Assert.DoesNotContain("Modul Network", Read(Path.Combine(AppDir(), "Edition/Unclassified/DomainInvestigationViewModel.cs")));
        var risk = StripXmlComments(Read(Path.Combine(AppDir(), "Views/RiskAnalysisView.xaml")));
        Assert.Contains("Padding=\"12,6\" VerticalAlignment=\"Top\"", risk);
    }

    // ---------------------------------------------------------------- step 6 (2026-11-11): table headers, long cells, cards, date fields

    /// <summary>Header width estimate: 7.5 px per character at 12 SemiBold plus 20 px of padding, rounded up to a multiple of 10.</summary>
    private static int HeaderEstimate(string header) => (int)Math.Ceiling((7.5 * header.Length + 20) / 10.0) * 10;

    [Fact]
    public void Every_fixed_width_table_column_is_at_least_as_wide_as_its_header()
    {
        var column = new Regex(@"<DataGrid(?:Text|Template|CheckBox)Column[^>]*>", RegexOptions.Singleline);
        var problems = new List<string>();
        var dirs = new[] { Path.Combine(AppDir(), "Views"), Path.Combine(AppDir(), "Edition") };
        foreach (var file in dirs.SelectMany(d => Files(d, "*.xaml")))
        {
            foreach (Match m in column.Matches(StripXmlComments(Read(file))))
            {
                var tag = m.Value;
                var header = Regex.Match(tag, @"\sHeader=""([^""{]+)""");
                if (!header.Success) continue;
                // exception: an icon-only header (a glyph code such as &#xE734;) is drawn through a HeaderTemplate and has an accessible name, not a text width
                if (header.Groups[1].Value.StartsWith("&#", StringComparison.Ordinal)) continue;
                var text = System.Net.WebUtility.HtmlDecode(header.Groups[1].Value);
                var width = Regex.Match(tag, @"\sWidth=""([^""]+)""");
                var minWidth = Regex.Match(tag, @"\sMinWidth=""([^""]+)""");
                // star and Auto widths share the remaining room or follow the content; only a numeric Width (or an explicit MinWidth) is a promise to the header
                bool numeric = width.Success && double.TryParse(width.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out _);
                if (!numeric && !minWidth.Success) continue;
                double w = numeric ? double.Parse(width.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                double mw = minWidth.Success ? double.Parse(minWidth.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                if (Math.Max(w, mw) < HeaderEstimate(text))
                    problems.Add($"{Path.GetFileName(file)}: \"{text}\" Width={w} MinWidth={mw} needs {HeaderEstimate(text)}");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_message_columns_show_two_lines_and_the_whole_text_in_a_capped_tooltip()
    {
        var grids = Read(Path.Combine(AppDir(), "Themes/Components/DataGrids.xaml"));
        Assert.Contains("x:Key=\"TwoLineCellTextStyle\"", grids);
        Assert.Contains("Property=\"MaxHeight\" Value=\"34\"", grids);
        Assert.Contains("Property=\"TextTrimming\" Value=\"CharacterEllipsis\"", grids);
        Assert.Contains("Property=\"ToolTipService.ShowDuration\" Value=\"60000\"", grids);
        foreach (var page in new[] { "Views/LiveSocView.xaml", "Views/EventExplorerView.xaml" })
            Assert.Matches(@"Binding=""\{Binding Message\}""[^>]*ElementStyle=""\{StaticResource TwoLineCellTextStyle\}""", Read(Path.Combine(AppDir(), page)));
        var tips = Read(Path.Combine(AppDir(), "Themes/Components/Timeline.xaml"));
        Assert.Contains("TargetType=\"ToolTip\"", tips);
        Assert.Contains("Property=\"MaxWidth\" Value=\"560\"", tips);
        Assert.Contains("Property=\"TextWrapping\" Value=\"Wrap\"", tips);
    }

    [Fact]
    public void The_actor_cards_and_the_active_directory_cards_wrap_and_do_not_overlap()
    {
        var ti = StripXmlComments(Read(Path.Combine(AppDir(), "Views/ThreatIntelView.xaml")));
        var card = ti.Substring(ti.IndexOf("<DataTemplate>", StringComparison.Ordinal), ti.IndexOf("</DataTemplate>", StringComparison.Ordinal) - ti.IndexOf("<DataTemplate>", StringComparison.Ordinal));
        Assert.DoesNotContain("DockPanel", card);   // a DockPanel with free left and right children is what drew the texts over each other
        Assert.Contains("<ColumnDefinition Width=\"Auto\"/>", card);
        var ad = StripXmlComments(Read(Path.Combine(AppDir(), "Views/ActiveDirectoryAuditView.xaml")));
        var ribbon = ad.Substring(ad.IndexOf("<UniformGrid Grid.Row=\"1\"", StringComparison.Ordinal), ad.IndexOf("</UniformGrid>", StringComparison.Ordinal) - ad.IndexOf("<UniformGrid Grid.Row=\"1\"", StringComparison.Ordinal));
        Assert.Equal(6, Regex.Matches(ribbon, @"FontSize=""12"" FontWeight=""SemiBold"" Foreground=""\{StaticResource TextMutedBrush\}"" TextWrapping=""Wrap""").Count);
        Assert.DoesNotContain("FontSize=\"8.5\"", ribbon);
        var risk = StripXmlComments(Read(Path.Combine(AppDir(), "Views/RiskAnalysisView.xaml")));
        Assert.DoesNotContain("TextTrimming", risk);
        Assert.Contains("{Binding AiRiskVerdictNote}", risk);   // the explanation after NEDETERMINAT is its own TextBlock
        Assert.Contains("{Binding AiHasEvidence}", risk);       // no analysis, no figures: the cards show a dash
    }

    [Fact]
    public void Date_fields_use_the_dosar_colours_on_top_of_the_fluent_style()
    {
        var inputs = Read(Path.Combine(AppDir(), "Themes/Components/Inputs.xaml"));
        Assert.Contains("TargetType=\"DatePicker\" BasedOn=\"{StaticResource {x:Type DatePicker}}\"", inputs);
        Assert.Contains("Property=\"MinHeight\" Value=\"32\"", inputs);
        // App.xaml keeps the Romanian language but must build on the Fluent style, or the old square WPF picker comes back
        var app = Read(Path.Combine(AppDir(), "App.xaml"));
        Assert.Contains("TargetType=\"DatePicker\" BasedOn=\"{StaticResource {x:Type DatePicker}}\"", app);
    }
}
