using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Usability 2026-10-16. (1) Every check id and every known technical word that a screen shows has an entry in the glossary, so its tooltip and its „?”
/// never come up empty. (2) Home runs the station control in one click and offers the report, the officer's signature and the export in the same panel.
/// </summary>
public sealed class UsabilityGlossaryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-usability-" + Guid.NewGuid().ToString("N"));
    public UsabilityGlossaryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
        throw new DirectoryNotFoundException("LogAnalyzer.App");
    }

    private static IEnumerable<string> Xaml() =>
        Directory.EnumerateFiles(AppDir(), "*.xaml", SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// The technical words a controller meets on the control, media, investigation and report screens. A word in this list that appears in any XAML text
    /// (attribute values: Text, Content, Header, ToolTip, automation names, binding string formats) must have a glossary entry.
    /// </summary>
    private static readonly string[] KnownTerms =
    [
        "SACL", "SRUM", "NCSI", "Prefetch", "Amcache", "EVTX", "BitLocker To Go", "BitLocker", "PAdES", "CRL", "SHA-256", "cod de ancorare", "codul de ancorare",
        "UAC", "WMI", "SID", "LDAP", "UTC", "DNS", "USB", "USBSTOR", "PIN", "RDP", "GPO", "DHCP", "NetworkList", "auditpol", "RecordID", "Hardware ID",
        "IOC", "YARA", "Sigma", "MITRE ATT&CK", "SAM", "EID", "VSS", "CD-R", "proces-verbal", "procesul-verbal",
        "CONFORM", "NECONFORM", "DE VERIFICAT", "NEDETERMINAT", "NU SE APLICĂ", "NECONFIRMAT", "CRITICĂ", "RIDICATĂ", "MEDIE", "SCĂZUTĂ", "INFORMATIVĂ",
    ];

    private static IEnumerable<(string File, string Text)> XamlTexts()
    {
        foreach (var f in Xaml())
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(?:Text|Content|Header|Title|ToolTip|AutomationProperties\.Name|AutomationProperties\.HelpText|StringFormat|HeaderStringFormat|Value)=""([^""]*)"""))
                yield return (Path.GetFileName(f), System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));
    }

    [Fact]
    public void Every_check_id_shown_in_a_view_has_a_glossary_entry()
    {
        var missing = new List<string>();
        int seen = 0;
        foreach (var (file, text) in XamlTexts())
            foreach (Match m in Regex.Matches(text, @"(?<![\p{L}\p{N}])(?:[ACDNPSU]\d{2}|DM1\d)(?![\p{L}\p{N}])"))
            {
                seen++;
                if (TermCatalog.Find(m.Value) is null) missing.Add($"{file}: {m.Value} in „{text}”");
            }
        Assert.True(seen > 0, "no check id found in any view: the scan is broken");
        Assert.True(missing.Count == 0, "check ids without a glossary entry:\n" + string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void Every_known_technical_word_shown_in_a_view_has_a_glossary_entry()
    {
        var missing = new List<string>();
        var shown = new HashSet<string>();
        foreach (var (file, text) in XamlTexts())
            foreach (var term in KnownTerms)
                if (Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])"))
                {
                    shown.Add(term);
                    if (TermCatalog.Find(term) is null) missing.Add($"{file}: „{term}”");
                }
        Assert.True(shown.Count >= 5, "the scan found almost no technical word: it is broken");
        Assert.True(missing.Count == 0, "terms shown without a glossary entry:\n" + string.Join("\n", missing.Distinct()));
        // the list itself is explained, shown or not: a screen that adds one of them later is covered
        Assert.All(KnownTerms, t => Assert.True(TermCatalog.Find(t) is not null, t));
    }

    [Fact]
    public void Check_ids_and_results_in_the_control_grids_get_a_tooltip_from_the_glossary()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "StationControlView.xaml"));
        Assert.Matches(@"Header=""Rezultat"" Binding=""\{Binding Status\}""[^>]*>\s*<DataGridTextColumn\.ElementStyle><Style TargetType=""TextBlock""><Setter Property=""local:TermTip\.Auto"" Value=""True""/>", xaml);
        Assert.Matches(@"Header=""Cod"" Binding=""\{Binding Id\}"" Width=""50"">\s*<DataGridTextColumn\.ElementStyle><Style TargetType=""TextBlock""><Setter Property=""local:TermTip\.Auto"" Value=""True""/>", xaml);
        Assert.Contains("local:HelpButton Term=\"anchor_code\"", xaml);
        // the converter behind the tooltips: the enum the grid binds, a check id, and nothing for an ordinary word
        Assert.StartsWith("DE VERIFICAT — ", (string)LogAnalyzer.UI.Views.TermTooltipConverter.Instance.Convert(ControlStatus.DeVerificat, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture)!);
        Assert.StartsWith("A11 — ", (string)LogAnalyzer.UI.Views.TermTooltipConverter.Instance.Convert("A11", typeof(object), null, System.Globalization.CultureInfo.InvariantCulture)!);
        Assert.Null(LogAnalyzer.UI.Views.TermTooltipConverter.Instance.Convert("Conturi locale", typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class FakeQuick : IStationQuickControl
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public int Runs, Opens, Signs, Exports;
        public bool IsBusy { get; set; }
        public string Status { get; set; } = "";
        public string Summary => "";
        public bool HasResult { get; private set; }
        public string AnchorLine => "";
        public IReadOnlyList<StatusCount> ResultCounts => [];
        public string PreviousAnchorCode { get; set; } = "";
        public Task RunQuickAsync() { Runs++; HasResult = true; PropertyChanged?.Invoke(this, new(nameof(HasResult))); return Task.CompletedTask; }
        public void OpenReport() => Opens++;
        public void SignAsOfficer() => Signs++;
        public void ExportForOfficer() => Exports++;
    }

    [Fact]
    public async Task Home_runs_the_station_control_in_one_click_and_offers_the_follow_ups_in_the_same_panel()
    {
        var quick = new FakeQuick();
        var nav = new List<int>();
        var control = RoleProfiles.For(StationRole.Control, new UnclassifiedEditionProfile(), AppMode.AirGapped);
        var vm = new HomeViewModel(new InvestigationViewModel(), nav.Add, new RecentCases(Path.Combine(_dir, "recent.json")), () => null, control, () => null, quick);
        Assert.True(vm.ShowQuickControl);
        await vm.RunStationControlCommand.ExecuteAsync(null);
        Assert.Equal(1, quick.Runs);
        Assert.Empty(nav);                                  // the result stays on Home: no page change
        vm.OpenControlReportCommand.Execute(null);
        vm.SignControlReportCommand.Execute(null);
        vm.ExportForOfficerCommand.Execute(null);
        Assert.Equal((1, 1, 1), (quick.Opens, quick.Signs, quick.Exports));
        vm.ShowControlDetailsCommand.Execute(null);
        Assert.Equal([RoleProfiles.TabStationControl], nav);
        quick.IsBusy = true;                                // a second Ctrl+Enter while it runs does not start another control
        await vm.RunStationControlCommand.ExecuteAsync(null);
        Assert.Equal(1, quick.Runs);
    }

    [Fact]
    public async Task The_incident_response_station_does_not_get_the_one_click_control()
    {
        var quick = new FakeQuick();
        var csirt = RoleProfiles.For(StationRole.Csirt, new UnclassifiedEditionProfile(), AppMode.AirGapped);
        var vm = new HomeViewModel(new InvestigationViewModel(), null, new RecentCases(Path.Combine(_dir, "recent.json")), () => null, csirt, () => null, quick);
        Assert.False(vm.ShowQuickControl);
        await vm.RunStationControlCommand.ExecuteAsync(null);
        Assert.Equal(0, quick.Runs);
        Assert.Contains("Control stație", vm.OpenStatus);
    }

    [Fact]
    public void The_primary_actions_have_keyboard_shortcuts_and_the_buttons_say_them()
    {
        var main = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        foreach (var cmd in new[] { "Home.RunStationControlCommand", "Home.OpenControlReportCommand", "Home.SignControlReportCommand", "Home.ExportForOfficerCommand", "OpenGlossaryCommand" })
            Assert.Matches($@"<KeyBinding [^>]*Command=""\{{Binding {Regex.Escape(cmd)}\}}""", main);
        var home = File.ReadAllText(Path.Combine(AppDir(), "Views", "HomeView.xaml"));
        foreach (var key in new[] { "Ctrl+Enter", "Ctrl+O", "Ctrl+Shift+S", "Ctrl+E" }) Assert.Contains(key, home);
    }
}
