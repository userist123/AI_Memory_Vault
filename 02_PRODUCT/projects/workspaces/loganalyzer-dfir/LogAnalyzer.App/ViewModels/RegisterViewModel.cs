using System;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Registers;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// "Registru medii" / "Registru utilizatori" (owner decisions 16 and 17): one register as an editable grid - typed in, pasted from a table or imported
    /// (CSV / JSON), validated line by line. It is data only: nothing here is applied to Windows, and every saved change goes to the hash-chained register
    /// audit. An empty register is "registru nedefinit", never "conform".
    /// </summary>
    public abstract class RegisterViewModel<T> : ObservableObject where T : class, IRegisterData, new()
    {
        private T _register = new();
        private string _registerPath;
        private string _status;
        private bool _replaceOnImport;
        private string _sha256 = "";
        private string _definition = "";

        protected RegisterViewModel(string? path = null)
        {
            _registerPath = path ?? RegisterStore.DefaultPath<T>();
            _status = Loc.T("reg.vm.initial");
            Data = new DataTable(_register.Kind);
            foreach (var c in _register.Columns) Data.Columns.Add(c, typeof(string));
            SaveCommand = new RelayCommand(Save);
            ReloadCommand = new RelayCommand(() => LoadFrom(RegisterPath));
            OpenFileCommand = new RelayCommand(OpenFile);
            ValidateCommand = new RelayCommand(ValidateNow);
            ImportJsonCommand = new RelayCommand(ImportJson);
            ImportCsvCommand = new RelayCommand(ImportCsv);
            PasteCommand = new RelayCommand(Paste);
            LoadFrom(_registerPath, startup: true);
            Services.AuthApp.SessionChanged += () => { OnPropertyChanged(nameof(AccessNotice)); OnPropertyChanged(nameof(CanEdit)); };
        }

        public string Title => _register.Title;
        public string Columns => string.Join(" | ", _register.Columns);
        /// <summary>A shown-always line under the title (for the users register: who may edit it, and that the application cannot enforce it yet).</summary>
        public virtual string Notice => "";
        /// <summary>Shown when the signed-in user may read but not change this register (only the global administrator edits; decision 33).</summary>
        public string AccessNotice => LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration ? "" : Loc.Format("reg.vm.access_notice", LogAnalyzer.Dfir.Auth.OperatorIdentity.AdministratorOnlyMessage);
        public bool CanEdit => LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration;
        public abstract string Hint { get; }
        public DataTable Data { get; }
        public DataView View => Data.DefaultView;
        public ObservableCollection<string> Issues { get; } = new();

        public string RegisterPath { get => _registerPath; private set => SetProperty(ref _registerPath, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public bool ReplaceOnImport { get => _replaceOnImport; set => SetProperty(ref _replaceOnImport, value); }
        public string Sha256 { get => _sha256; private set => SetProperty(ref _sha256, value); }
        /// <summary>"definit (n rânduri)" or "nedefinit".</summary>
        public string Definition { get => _definition; private set => SetProperty(ref _definition, value); }

        public IRelayCommand SaveCommand { get; }
        public IRelayCommand ReloadCommand { get; }
        public IRelayCommand OpenFileCommand { get; }
        public IRelayCommand ValidateCommand { get; }
        public IRelayCommand ImportJsonCommand { get; }
        public IRelayCommand ImportCsvCommand { get; }
        public IRelayCommand PasteCommand { get; }

        /// <summary>Rows of the grid, without the completely empty ones.</summary>
        public System.Collections.Generic.List<string[]> Rows() =>
            Data.Rows.Cast<DataRow>().Select(r => r.ItemArray.Select(v => v?.ToString() ?? "").ToArray()).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();

        private void Pull() => _register.SetCells(Rows());

        private void Push()
        {
            Data.Clear();
            foreach (var row in _register.Cells()) Data.Rows.Add(row.Cast<object>().ToArray());
            Definition = _register.IsDefined ? Loc.Format("reg.vm.defined", _register.Cells().Count) : Loc.T("reg.vm.undefined");
        }

        private void ShowIssues(System.Collections.Generic.IEnumerable<RegisterIssue> issues)
        {
            Issues.Clear();
            foreach (var i in issues) Issues.Add(i.ToString());
        }

        private void LoadFrom(string path, bool startup = false)
        {
            var r = RegisterStore.Load<T>(path);
            if (r.Register is null)
            {
                _register = new T();
                Push();
                ShowIssues(r.Issues);
                Status = r.Issues.Count > 0 ? Loc.T("reg.vm.unreadable") : startup ? Loc.T("reg.vm.none_saved") : Loc.T("reg.vm.no_file");
                return;
            }
            _register = r.Register;
            Push();
            ShowIssues(r.Issues);
            Status = Loc.Format("reg.vm.loaded", path) + (r.HasErrors ? Loc.T("reg.vm.loaded_invalid") : "");
        }

        private void OpenFile()
        {
            var dlg = new OpenFileDialog { Filter = Loc.T("reg.vm.dialog_filter"), Title = Loc.Format("reg.vm.dialog_open", Title.ToLowerInvariant()) };
            if (dlg.ShowDialog() == true) LoadFrom(dlg.FileName);
        }

        private void ValidateNow()
        {
            Pull();
            var issues = _register.Validate();
            ShowIssues(issues);
            Status = issues.Any(i => i.IsError) ? Loc.Format("reg.vm.errors_fix", issues.Count(i => i.IsError)) : Loc.T("reg.vm.valid");
        }

        private void Save()
        {
            Pull();
            try
            {
                var issues = RegisterStore.Save(_register, RegisterPath, out var sha, action: "save");
                ShowIssues(issues);
                if (issues.Any(i => i.IsError)) { Status = Loc.Format("reg.vm.not_saved", issues.Count(i => i.IsError)); return; }
                Sha256 = sha;
                Definition = _register.IsDefined ? Loc.Format("reg.vm.defined", _register.Cells().Count) : Loc.T("reg.vm.undefined");
                Status = Loc.Format("reg.vm.saved", RegisterPath, sha);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = Loc.Format("reg.vm.write_failed", ex.Message);
            }
        }

        private void ImportJson()
        {
            var dlg = new OpenFileDialog { Filter = Loc.T("reg.vm.dialog_json_filter"), Title = Loc.T("reg.vm.dialog_import") };
            if (dlg.ShowDialog() != true) return;
            ImportJsonText(File.ReadAllText(dlg.FileName), dlg.FileName);
        }

        public void ImportJsonText(string json, string source)
        {
            var r = RegisterImport.FromJson<T>(json);
            ShowIssues(r.Issues);
            if (r.Register is null) { Status = Loc.Format("reg.vm.import_failed", source); return; }
            _register = r.Register;
            Push();
            Status = Loc.Format("reg.vm.imported", source, r.RowsAccepted, r.RowsRejected);
        }

        private void ImportCsv()
        {
            var dlg = new OpenFileDialog { Filter = Loc.T("reg.vm.dialog_csv_filter"), Title = Loc.Format("reg.vm.dialog_csv", Title) };
            if (dlg.ShowDialog() == true) ImportText(File.ReadAllText(dlg.FileName), dlg.FileName);
        }

        private void Paste()
        {
            string text;
            try { text = Clipboard.GetText(); }
            catch (System.Runtime.InteropServices.COMException) { Status = Loc.T("reg.vm.clipboard_unavailable"); return; }
            if (string.IsNullOrWhiteSpace(text)) { Status = Loc.T("reg.vm.clipboard_empty"); return; }
            ImportText(text, Loc.T("reg.vm.clipboard_source"));
        }

        /// <summary>CSV / tab-separated text into the grid; the invalid lines are listed with their line numbers and are not added.</summary>
        public void ImportText(string text, string source)
        {
            Pull();
            var r = RegisterImport.Text(_register, text, ReplaceOnImport);
            Push();
            ShowIssues(r.Issues);
            Status = Loc.Format("reg.vm.added", Title, source, r.RowsAccepted, r.RowsRejected) + (r.RowsRejected > 0 ? Loc.T("reg.vm.see_list") : ".");
        }
    }

    /// <summary>"Registru medii" (decision 16): registration number, serial, VID/PID, type, classification, assigned user, zone, validity, status.</summary>
    public sealed class MediaRegisterViewModel : RegisterViewModel<MediaRegister>
    {
        public MediaRegisterViewModel(string? path = null) : base(path) { }
        public override string Hint => Loc.T("reg.hint.media");
    }

    /// <summary>"Registru utilizatori" (decision 17): persons, accounts and clearances, entered by hand by the global administrator.</summary>
    public sealed class UsersRegisterViewModel : RegisterViewModel<UsersRegister>
    {
        public UsersRegisterViewModel(string? path = null) : base(path) { }
        public override string Notice => Loc.T("reg.vm.users_notice");
        public override string Hint => Loc.T("reg.hint.users");
    }
}
