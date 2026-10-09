using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Language;
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

        public HomeViewModel(InvestigationViewModel investigation, Action<int>? navigate = null, RecentCases? recent = null, Func<string?>? pickFolder = null)
        {
            _investigation = investigation;
            _navigate = navigate ?? (_ => { });
            _recent = recent ?? new RecentCases(RecentCases.DefaultFile());
            _pickFolder = pickFolder ?? PickFolderWithDialog;
            _investigation.StateChanged += (_, _) => Refresh();
            Loc.LanguageChanged += (_, _) => { OnUi(Refresh); OnUi(ReloadRecent); };
            Refresh();
            ReloadRecent();
        }

        private static void OnUi(Action a)
        {
            if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.Invoke(a); else a();
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

        /// <summary>
        /// The three intents of the first-run chooser (U22). The question chooser ("A rulat un program?") belongs to WP7; its hook is
        /// <see cref="ChooseQuestion"/>, which does nothing yet.
        /// </summary>
        public string[] Intents => new[] { Loc.T("home.verifica_acest_calculator"), Loc.T("home.analizeaza_probe"), Loc.T("home.deschide_caz_existent") };

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
            OpenStatus = Loc.T("homevm.check_status");
        }

        [RelayCommand]
        private void AnalyzeEvidence()
        {
            _investigation.CollectFromThisStation = false;
            _navigate(InvestigationTabIndex);
            OpenStatus = Loc.T("homevm.analyze_status");
        }

        /// <summary>Asks for a case folder and opens it (R9.1).</summary>
        [RelayCommand]
        private async Task OpenExistingCase()
        {
            var folder = _pickFolder();
            if (string.IsNullOrWhiteSpace(folder)) { OpenStatus = Loc.T("homevm.open_cancelled"); return; }
            await OpenFolder(folder);
        }

        [RelayCommand]
        private async Task OpenRecent(RecentCaseView? entry)
        {
            if (entry is null) return;
            if (!entry.Exists) { OpenStatus = Loc.Format("homevm.folder_missing", entry.Entry.Path); return; }
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
            OpenStatus = Loc.T("homevm.opening");
            try
            {
                var res = await CaseLoader.OpenAsync(folder, _recent);
                if (!res.Opened) { OpenStatus = Loc.Format("homevm.not_opened", res.RefusedReason); return; }
                _investigation.ShowLoaded(res.Case!);   // raises StateChanged: Home refreshes
                OpenStatus = res.Case!.ReadOnly
                    ? Loc.Format("homevm.opened_read_only", string.Join("; ", res.Case.ReadOnlyReasons))
                    : Loc.Format("homevm.opened", res.Case.Workspace.Info.CaseId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                OpenStatus = Loc.Format("homevm.open_failed", ex.Message);
            }
            finally { IsBusy = false; ReloadRecent(); }
        }

        private static string? PickFolderWithDialog()
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("homevm.dialog_title") };
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }
    }
}
