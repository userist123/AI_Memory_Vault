using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Home;
using LogAnalyzer.Dfir.Windows.Investigation;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// WP5 / U3 / U21 / U22: the Home page. It answers is there a problem, how serious, is the evidence trustworthy, what was found and what next,
    /// from the case that is open (a run made here or an existing case opened from disk), shows the coverage matrix, and offers the three starting
    /// intents. It never shows SAFE or NORMAL: with nothing found it says "Nimic detectat în sursele analizate" together with the coverage level.
    /// </summary>
    public partial class HomeViewModel : ObservableObject
    {
        /// <summary>Tab index of the Home page in the main window.</summary>
        public const int HomeTabIndex = 22;
        /// <summary>Tab index of "Investigație completă (caz)", where the three intents lead.</summary>
        public const int InvestigationTabIndex = 16;

        private readonly InvestigationViewModel _investigation;
        private readonly Action<int> _navigate;
        private readonly RecentCases _recent;
        private readonly Func<string?> _pickFolder;

        /// <param name="profile">The role profile (WP18): which intents this PC offers. Default: the role decided at startup, on the unclassified edition.</param>
        public HomeViewModel(InvestigationViewModel investigation, Action<int>? navigate = null, RecentCases? recent = null, Func<string?>? pickFolder = null,
            RoleProfile? profile = null)
        {
            _investigation = investigation;
            _navigate = navigate ?? (_ => { });
            _recent = recent ?? new RecentCases(RecentCases.DefaultFile());
            _pickFolder = pickFolder ?? PickFolderWithDialog;
            Profile = profile ?? RoleProfiles.For(StationRoleContext.Role, new UnclassifiedEditionProfile(), AppModeContext.Current.Mode);
            foreach (var i in Profile.PrimaryIntents) PrimaryIntents.Add(new HomeIntentView(i));
            foreach (var i in Profile.MoreIntents) MoreIntents.Add(new HomeIntentView(i));
            _investigation.StateChanged += (_, _) => Refresh();
            Refresh();
            ReloadRecent();
        }

        // The five answers.
        [ObservableProperty] private string _attentionLabel = "";
        [ObservableProperty] private AttentionLevel _attention;
        [ObservableProperty] private string _problem = "";
        [ObservableProperty] private string _seriousness = "";
        [ObservableProperty] private string _trust = "";
        [ObservableProperty] private string _found = "";
        [ObservableProperty] private string _coverageLine = "";
        [ObservableProperty] private string _caseTitle = "";
        [ObservableProperty] private bool _hasCase;
        [ObservableProperty] private bool _isReadOnly;
        [ObservableProperty] private string _readOnlyNotice = "";
        [ObservableProperty] private string _openStatus = "";
        [ObservableProperty] private bool _isBusy;

        /// <summary>Short, data-driven next steps (at most <see cref="HomeAggregator.MaxNextSteps"/>).</summary>
        public ObservableCollection<string> NextSteps { get; } = new();
        /// <summary>The coverage matrix (U21): one row per artifact family.</summary>
        public ObservableCollection<CoverageRow> CoverageRows { get; } = new();
        /// <summary>Cases opened before; a folder that no longer exists is listed as missing.</summary>
        public ObservableCollection<RecentCaseView> RecentList { get; } = new();

        /// <summary>The role profile this page renders (WP18 S2): data, not decisions.</summary>
        public RoleProfile Profile { get; }
        public string RoleTitle => Profile.Title;
        public string RoleIntro => Profile.Intro;
        /// <summary>At most five big buttons, in the user's words. Unavailable ones stay visible, disabled, with the reason.</summary>
        public ObservableCollection<HomeIntentView> PrimaryIntents { get; } = new();
        /// <summary>The rest, under "Mai multe".</summary>
        public ObservableCollection<HomeIntentView> MoreIntents { get; } = new();

        /// <summary>
        /// Titles of the primary intents (U22, now per role). The question chooser ("A rulat un program?") belongs to WP7; its hook is
        /// <see cref="ChooseQuestion"/>, which does nothing yet.
        /// </summary>
        public string[] Intents => PrimaryIntents.Select(i => i.Title).ToArray();

        /// <summary>Runs an intent: navigates to its page with the source it declares, or says why it is unavailable. Never widens what the edition allows.</summary>
        [RelayCommand]
        private async Task RunIntent(HomeIntentView? intent)
        {
            if (intent is null) return;
            if (!intent.Enabled) { OpenStatus = $"„{intent.Title}” nu este disponibil pe această stație: {intent.Reason}."; return; }
            switch (intent.Source)
            {
                case IntentSource.CollectFromThisStation: _investigation.CollectFromThisStation = true; _navigate(intent.TargetTab); break;
                case IntentSource.ImportEvidence: _investigation.CollectFromThisStation = false; _navigate(intent.TargetTab); break;
                case IntentSource.OpenExistingCase: await OpenExistingCase(); return;
                default: _navigate(intent.TargetTab); break;
            }
            OpenStatus = $"{intent.Title}: {intent.Description}";
        }

        /// <summary>WP7 hook: maps a question to an existing workflow. Intentionally empty in WP5.</summary>
        public void ChooseQuestion(string question) { }

        /// <summary>Recomputes the answers from the case shown on the Investigation page, or says that no case is open.</summary>
        public void Refresh()
        {
            HomeSummary h;
            if (_investigation.CurrentResult is { } r)
            {
                var readOnlyReasons = _investigation.Loaded?.ReadOnlyReasons;
                h = HomeBuilder.Build(r, r.Case, null, readOnlyReasons);
                HasCase = true;
                CaseTitle = $"{r.Case.Info.Name} ({r.Case.Info.CaseId})";
                IsReadOnly = _investigation.IsReadOnlyCase;
                ReadOnlyNotice = _investigation.ReadOnlyNotice;
            }
            else
            {
                h = HomeBuilder.NoCase();
                HasCase = false; CaseTitle = ""; IsReadOnly = false; ReadOnlyNotice = "";
            }
            Attention = h.Attention; AttentionLabel = h.AttentionLabel; Problem = h.Problem; Seriousness = h.Seriousness; Trust = h.Trust; Found = h.Found;
            CoverageLine = h.CoverageLine;
            NextSteps.Clear(); foreach (var s in h.NextSteps) NextSteps.Add(s);
            CoverageRows.Clear(); foreach (var row in h.Coverage.Rows) CoverageRows.Add(row);
        }

        public void ReloadRecent()
        {
            RecentList.Clear();
            foreach (var c in _recent.Load()) RecentList.Add(c);
        }

        // ---- the three intents ----

        [RelayCommand]
        private void CheckThisComputer()
        {
            _investigation.CollectFromThisStation = true;
            _navigate(InvestigationTabIndex);
            OpenStatus = "Verificare calculator: completați scopul cazului și porniți investigația (colectare de pe această stație și analiză).";
        }

        [RelayCommand]
        private void AnalyzeEvidence()
        {
            _investigation.CollectFromThisStation = false;
            _navigate(InvestigationTabIndex);
            OpenStatus = "Analiză probe: adăugați fișierele sau folderul de probe, completați scopul cazului și porniți investigația.";
        }

        /// <summary>Asks for a case folder and opens it (R9.1).</summary>
        [RelayCommand]
        private async Task OpenExistingCase()
        {
            var folder = _pickFolder();
            if (string.IsNullOrWhiteSpace(folder)) { OpenStatus = "Deschiderea cazului a fost anulată."; return; }
            await OpenFolder(folder);
        }

        [RelayCommand]
        private async Task OpenRecent(RecentCaseView? entry)
        {
            if (entry is null) return;
            if (!entry.Exists) { OpenStatus = $"Folderul cazului nu mai există: {entry.Entry.Path}. Intrarea rămâne în listă până o eliminați."; return; }
            await OpenFolder(entry.Entry.Path);
        }

        /// <summary>Removes an entry from the recent list; only the operator's explicit action does this.</summary>
        [RelayCommand]
        private void ForgetRecent(RecentCaseView? entry)
        {
            if (entry is null) return;
            _recent.Forget(entry.Entry.Path);
            ReloadRecent();
        }

        /// <summary>Opens a case folder on a background task (the integrity re-check can be long) and shows it; a refused folder says why.</summary>
        public async Task OpenFolder(string folder)
        {
            IsBusy = true;
            OpenStatus = "Se deschide cazul și se rulează reverificarea de integritate…";
            try
            {
                var res = await CaseLoader.OpenAsync(folder, _recent);
                if (!res.Opened) { OpenStatus = "Cazul nu a fost deschis: " + res.RefusedReason; return; }
                _investigation.ShowLoaded(res.Case!);   // raises StateChanged: Home refreshes
                OpenStatus = res.Case!.ReadOnly
                    ? "Caz deschis doar pentru citire: " + string.Join("; ", res.Case.ReadOnlyReasons)
                    : $"Caz deschis: {res.Case.Workspace.Info.CaseId}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                OpenStatus = "Cazul nu a putut fi deschis: " + ex.Message;
            }
            finally { IsBusy = false; ReloadRecent(); }
        }

        private static string? PickFolderWithDialog()
        {
            var dlg = new OpenFolderDialog { Title = "Folderul unui caz existent (conține case.json)" };
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }
    }

    /// <summary>A Home button as the view shows it: title, one-sentence description, and, when disabled, the reason next to it.</summary>
    public sealed class HomeIntentView
    {
        public HomeIntentView(IntentAvailability a) { Availability = a; }
        public IntentAvailability Availability { get; }
        public string Key => Availability.Key;
        public string Title => Availability.Title;
        public string Description => Availability.Description;
        public bool Enabled => Availability.Enabled;
        public string Reason => Availability.Reason;
        public int TargetTab => Availability.TargetTab;
        public IntentSource Source => Availability.Intent.Source;
        /// <summary>What the screen reader and the tooltip say: the description, or the reason when the button is disabled.</summary>
        public string Tooltip => Enabled ? Description : $"Nu este disponibil: {Reason}";
        public string ReasonLine => Enabled ? "" : $"Nu este disponibil: {Reason}";
    }
}
