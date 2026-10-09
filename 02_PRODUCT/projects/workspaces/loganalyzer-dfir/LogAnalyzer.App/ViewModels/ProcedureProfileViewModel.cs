using System;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Profile;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>One table of the profile as the grid shows it: the cells live in a DataTable the operator edits, pastes into and imports into.</summary>
    public sealed class ProfileTableViewModel : ObservableObject
    {
        public ProfileTableViewModel(ProfileTable table)
        {
            Table = table;
            Data = new DataTable(table.ToString());
            foreach (var c in ProfileTables.Columns(table)) Data.Columns.Add(c, typeof(string));
        }

        public ProfileTable Table { get; }
        public string Title => ProfileTables.Title(Table);
        public string Section => ProfileTables.Section(Table);
        public string Columns => string.Join(" | ", ProfileTables.Columns(Table));
        public DataTable Data { get; }
        public DataView View => Data.DefaultView;

        public string Hint => Table switch
        {
            ProfileTable.WorkingHours => "Day: Mon..Sun (sau Luni..Duminică); Start/End: HH:mm. Un schimb de noapte (22:00-06:00) este acceptat.",
            ProfileTable.Holidays => "Date: yyyy-MM-dd.",
            ProfileTable.Shifts => "Name; Start/End: HH:mm.",
            ProfileTable.ApprovedAccounts => "Account: DOMENIU\\utilizator sau utilizator. Conturile care au voie să golească jurnalele.",
            ProfileTable.MaintenanceWindows => "Kind: once (Date), weekly (Day) sau monthly (Ordinal 1-4/last + Day), de exemplu prima zi de luni din lună = monthly, Mon, 1, 08:00-10:00. Ora locală a stației.",
            ProfileTable.RotationProcedure => "Pașii, în ordinea procedurii: EXPORT → HASH → ARCHIVE → VERIFY → CLEAR.",
            ProfileTable.ApprovedSoftware => "Name; Publisher; PathPattern (ex. C:\\Program Files\\7-Zip\\*); Sha256 (opțional, 64 hex).",
            ProfileTable.ExpectedPolicies => "PolicyPath: calea politicii din secțiunea Politici (import GPO existent; nu se copiază aici); Sha256 opțional.",
            ProfileTable.Zones => "Zonele organizației (ex. ROSU, PUBLIC). Fără zone, orice conectivitate pe un sistem air-gapped este „conectivitate observată, necesită explicație”.",
            ProfileTable.TransferChannels => "FromZone/ToZone: zone definite mai sus; Medium: suport sau cale de transfer aprobată.",
            ProfileTable.NetworkDestinations => "Address: IP, rețea CIDR sau nume; Zone: zona în care se află.",
            _ => "",
        };

        public void Load(ProcedureProfile p)
        {
            Data.Clear();
            foreach (var row in ProfileTables.Cells(p, Table)) Data.Rows.Add(row.Cast<object>().ToArray());
        }

        /// <summary>Rows of the grid, without the completely empty ones.</summary>
        public System.Collections.Generic.List<string[]> Rows() =>
            Data.Rows.Cast<DataRow>().Select(r => r.ItemArray.Select(v => v?.ToString() ?? "").ToArray()).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
    }

    /// <summary>
    /// "Profil de proceduri" (owner decisions 18 and 19): working hours, log maintenance, approved software, expected GPO policy, zones and transfers,
    /// entered by hand, pasted or imported. It is data only: nothing here is applied to Windows. A section left empty is "nedefinit", never "conform".
    /// </summary>
    public partial class ProcedureProfileViewModel : ObservableObject
    {
        private ProcedureProfile _profile = new();
        private readonly ProfileProvider _provider;

        public ObservableCollection<ProfileTableViewModel> Tables { get; } = new();
        public ObservableCollection<string> Issues { get; } = new();
        public ObservableCollection<string> SectionLines { get; } = new();

        [ObservableProperty] private ProfileTableViewModel? _selectedTable;
        [ObservableProperty] private string _profilePath;
        [ObservableProperty] private string _status = "Introduceți datele în tabele (sau lipiți / importați), apoi apăsați „Salvează”. Secțiunile goale rămân „nedefinit”. Nimic din profil nu se aplică pe Windows.";
        [ObservableProperty] private bool _replaceOnImport;
        [ObservableProperty] private string _sha256 = "";

        public ProcedureProfileViewModel(ProfileProvider? provider = null)
        {
            _provider = provider ?? ProfileProvider.Shared;
            _profilePath = _provider.Path;
            foreach (var t in ProfileTables.All) Tables.Add(new ProfileTableViewModel(t));
            SelectedTable = Tables[0];
            LoadFrom(_profilePath, startup: true);
            Services.AuthApp.SessionChanged += () => { OnPropertyChanged(nameof(AccessNotice)); OnPropertyChanged(nameof(CanEdit)); };
        }

        /// <summary>Shown when the signed-in user may read but not change the profile (only the global administrator edits; decision 33).</summary>
        public string AccessNotice => LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration ? "" : LogAnalyzer.Dfir.Auth.OperatorIdentity.AdministratorOnlyMessage + ". Puteți citi profilul, nu îl puteți modifica.";
        public bool CanEdit => LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration;

        private void Pull()
        {
            foreach (var t in Tables) ProfileTables.SetCells(_profile, t.Table, t.Rows());
        }

        private void Push()
        {
            foreach (var t in Tables) t.Load(_profile);
            Refresh();
        }

        private void Refresh()
        {
            SectionLines.Clear();
            foreach (var s in ProfileOps.Status(_profile)) SectionLines.Add(s.ToString());
        }

        private void ShowIssues(System.Collections.Generic.IEnumerable<ProfileIssue> issues)
        {
            Issues.Clear();
            foreach (var i in issues) Issues.Add(i.ToString());
        }

        private void LoadFrom(string path, bool startup = false)
        {
            var r = ProfileStore.Load(path);
            if (r.Profile is null)
            {
                _profile = new ProcedureProfile();
                Push();
                ShowIssues(r.Issues);
                Status = r.Issues.Count > 0 ? "Profilul nu a putut fi citit; lista de mai jos arată motivul. Nu s-a înlocuit nimic." : startup ? "Nu există un profil salvat: toate secțiunile sunt nedefinite." : "Fișierul nu există.";
                return;
            }
            _profile = r.Profile;
            Push();
            ShowIssues(r.Issues);
            Status = $"Profil încărcat din {path}." + (r.HasErrors ? " Unele rânduri sunt invalide (vezi lista); ele nu sunt folosite la analiză." : "");
        }

        [RelayCommand]
        private void Reload() => LoadFrom(ProfilePath);

        [RelayCommand]
        private void OpenFile()
        {
            var dlg = new OpenFileDialog { Filter = "Profil de proceduri (*.json)|*.json|Toate fișierele|*.*", Title = "Deschideți un profil de proceduri" };
            if (dlg.ShowDialog() == true) LoadFrom(dlg.FileName);
        }

        [RelayCommand]
        private void Validate()
        {
            Pull();
            var issues = ProfileOps.Validate(_profile);
            ShowIssues(issues);
            Refresh();
            Status = issues.Any(i => i.IsError) ? $"{issues.Count(i => i.IsError)} erori; corectați rândurile indicate înainte de salvare." : "Profilul este valid.";
        }

        [RelayCommand]
        private void Save() => SaveTo(ProfilePath);

        [RelayCommand]
        private void SaveAs()
        {
            var dlg = new SaveFileDialog { FileName = ProfileStore.FileName, Filter = "Profil de proceduri (*.json)|*.json", Title = "Salvați o copie a profilului" };
            if (dlg.ShowDialog() == true) SaveTo(dlg.FileName);
        }

        private void SaveTo(string path)
        {
            Pull();
            try
            {
                var issues = ProfileStore.Save(_profile, path, out var sha);
                ShowIssues(issues);
                Refresh();
                if (issues.Any(i => i.IsError)) { Status = $"Nu s-a salvat nimic: {issues.Count(i => i.IsError)} erori (vezi lista, pe linii)."; return; }
                Sha256 = sha;
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_provider.Path), StringComparison.OrdinalIgnoreCase)) _provider.Replace(_profile);
                Status = $"Profil salvat în {path} (SHA-256 {sha}). Se folosește la următoarea analiză; fiecare caz își păstrează o copie și hash-ul în custodie.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = "Profilul nu a putut fi scris: " + ex.Message;
            }
        }

        [RelayCommand]
        private void ImportJson()
        {
            var dlg = new OpenFileDialog { Filter = "Profil JSON (*.json)|*.json|Toate fișierele|*.*", Title = "Importați un profil (format propriu)" };
            if (dlg.ShowDialog() != true) return;
            ImportJsonText(File.ReadAllText(dlg.FileName), dlg.FileName);
        }

        public void ImportJsonText(string json, string source)
        {
            var r = ProfileImport.FromJson(json);
            ShowIssues(r.Issues);
            if (r.Profile is null) { Status = $"Importul din {source} a eșuat; nimic nu s-a schimbat."; return; }
            _profile = r.Profile;
            Push();
            Status = $"Importat din {source}: {r.RowsAccepted} rânduri valide, {r.RowsRejected} cu erori (rămân în tabel, marcate în listă; nu se salvează până nu sunt corectate).";
        }

        [RelayCommand]
        private void ImportCsv()
        {
            if (SelectedTable is null) return;
            var dlg = new OpenFileDialog { Filter = "CSV / text (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Toate fișierele|*.*", Title = $"Importați CSV în „{SelectedTable.Title}”" };
            if (dlg.ShowDialog() == true) ImportText(File.ReadAllText(dlg.FileName), dlg.FileName);
        }

        [RelayCommand]
        private void Paste()
        {
            if (SelectedTable is null) return;
            string text;
            try { text = Clipboard.GetText(); }
            catch (System.Runtime.InteropServices.COMException) { Status = "Clipboard-ul nu este disponibil."; return; }
            if (string.IsNullOrWhiteSpace(text)) { Status = "Clipboard-ul este gol."; return; }
            ImportText(text, "clipboard");
        }

        /// <summary>CSV / tab-separated text into the selected table; the invalid lines are listed with their line numbers and are not added.</summary>
        public void ImportText(string text, string source)
        {
            if (SelectedTable is null) return;
            Pull();
            var r = ProfileImport.Text(_profile, SelectedTable.Table, text, ReplaceOnImport);
            Push();
            ShowIssues(r.Issues);
            Status = $"{SelectedTable.Title} din {source}: {r.RowsAccepted} rânduri adăugate, {r.RowsRejected} respinse" + (r.RowsRejected > 0 ? " (vezi lista, pe linii)." : ".");
        }
    }
}
