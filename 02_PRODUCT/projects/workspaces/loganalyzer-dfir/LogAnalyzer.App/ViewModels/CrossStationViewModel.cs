using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// "Confruntare între stații" (owner decision 2026-10-16, every edition): the security officer imports the signed media-history exports collected from the
    /// stations, each is verified (officer on this station's list, chain, CRL; tampered or unsigned = refused), compared with the latest signed media register on
    /// this station, and every approved transfer is checked across stations. The table is sorted by severity; one click writes the report signed with the card.
    /// </summary>
    public partial class CrossStationViewModel : ObservableObject
    {
        private readonly List<string> _files = new();
        private CrossStationResult? _result;
        private CrossStationSettings? _settings;

        public ObservableCollection<CrossStationFinding> Findings { get; } = new();
        public ObservableCollection<string> Limits { get; } = new();
        public ObservableCollection<MediaHistoryCheck> Exports { get; } = new();

        [ObservableProperty] private DateTime? _fromDate = DateTime.Today.AddDays(-30);
        [ObservableProperty] private DateTime? _toDate = DateTime.Today;
        [ObservableProperty] private int _minMoveMinutes = 10;
        [ObservableProperty] private string _expectedStations = "";
        /// <summary>The anchor codes from the stations' proces-verbale, one per line: "STAȚIE = COD" (review R5; optional per station).</summary>
        [ObservableProperty] private string _paperAnchors = "";

        /// <summary>"STAȚIE = COD" or "STAȚIE: COD", one per line; lines without both parts are ignored. Keys are case-insensitive.</summary>
        internal static Dictionary<string, string> ParsePaperAnchors(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                int i = line.IndexOfAny(['=', ':']);
                if (i <= 0) continue;
                var name = line[..i].Trim(); var code = line[(i + 1)..].Trim();
                if (name.Length > 0 && code.Length > 0) map[name] = code;
            }
            return map;
        }
        [ObservableProperty] private string _summary = "";
        [ObservableProperty] private string _registerLine = "";
        [ObservableProperty] private string _status = "Importați exporturile semnate ale stațiilor (istoric_medii_*.json, cu semnătura .p7s alături).";
        [ObservableProperty] private MediaHistoryCheck? _selectedExport;
        [ObservableProperty] private string _selectedExportText = "";

        partial void OnSelectedExportChanged(MediaHistoryCheck? value) => SelectedExportText = value?.ToPlainText() ?? "";

        private static string? AuditPath()
        {
            try { return AuthApp.Service.AuditPath; } catch (InvalidOperationException) { return null; }
        }

        [RelayCommand]
        private void ImportExports()
        {
            var dlg = new OpenFileDialog { Title = "Alegeți exporturile stațiilor (istoric_medii_*.json, cu semnătura .p7s alături)", Filter = "Istoric medii (*.json)|*.json", Multiselect = true };
            if (dlg.ShowDialog() != true) return;
            foreach (var f in dlg.FileNames) if (!_files.Contains(f, StringComparer.OrdinalIgnoreCase)) _files.Add(f);
            Reconcile();
        }

        [RelayCommand]
        private void ClearExports() { _files.Clear(); Exports.Clear(); Findings.Clear(); Limits.Clear(); _result = null; Summary = ""; Status = "Lista exporturilor a fost golită."; }

        [RelayCommand]
        private void Reconcile()
        {
            if (_files.Count == 0) { Status = "Nu a fost importat niciun export."; return; }
            if (FromDate is not { } from || ToDate is not { } to || to < from) { Status = "Alegeți perioada: data de început și data de sfârșit (sfârșitul după început)."; return; }
            if (MinMoveMinutes is < 0 or > 1440) { Status = "Timpul minim de mutare între stații trebuie să fie între 0 și 1440 de minute."; return; }
            var active = MediaRegisterFlows.Active(SignedMediaRegisterStore.Shared, AuditPath());
            RegisterLine = active.Usable
                ? $"Registrul de medii semnat de pe stația ofițerului: versiunea {active.Doc!.Version}, SHA-256 {active.Sha256}; versiunea fiecărei stații se compară cu acesta."
                : "Pe stația ofițerului nu există un registru de medii semnat în vigoare (" + active.Problem + "): versiunile stațiilor și mediile neînregistrate nu pot fi stabilite.";
            var checks = MediaHistoryFlows.Load(_files, OfficerFlows.Trusted(OfficerStore.Shared.Load()), QualifiedSignatureTrust.FromDirectory());
            _settings = new CrossStationSettings
            {
                FromUtc = new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Local)).ToUniversalTime(),
                ToUtc = new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Local)).ToUniversalTime(),
                MinMoveTime = TimeSpan.FromMinutes(MinMoveMinutes),
                ExpectedStations = ExpectedStations.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                PaperAnchors = ParsePaperAnchors(PaperAnchors),
            };
            _result = CrossStationReconciliation.Run(checks, active.Usable ? active.Doc : null, active.Usable ? active.Sha256 : "", _settings);
            Exports.Clear();
            foreach (var c in checks) Exports.Add(c);
            Findings.Clear();
            foreach (var f in _result.Ordered) Findings.Add(f);
            Limits.Clear();
            foreach (var l in _result.Limits) Limits.Add(l);
            Summary = _result.Summary;
            Status = _result.Rejected.Count > 0 ? $"Confruntare făcută; {_result.Rejected.Count} exporturi au fost RESPINSE (vezi lista exporturilor)." : "Confruntare făcută.";
        }

        [RelayCommand]
        private void ExportSignedReport()
        {
            if (_result is null || _settings is null) { Status = "Faceți întâi confruntarea."; return; }
            List<System.Security.Cryptography.X509Certificates.X509Certificate2> certs;
            try { certs = OfficerFlows.CardSigningCertificates(AuthApp.Cards, DateTimeOffset.UtcNow); }
            catch (CardException ex) { Status = "Cardul nu poate fi citit: " + ex.Message; return; }
            if (certs.Count == 0) { Status = "Pe cardul introdus nu s-a găsit un certificat de semnătură calificată (cu utilizarea „nerepudiere”)."; return; }
            var cert = LogAnalyzer.UI.Views.CertificatePickerWindow.Pick("Semnează raportul confruntării între stații", certs);
            if (cert is null) return;
            var dlg = new OpenFolderDialog { Title = "Alegeți unde se scrie raportul semnat al confruntării (PDF și tabel CSV)" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var files = MediaHistoryFlows.ExportReport(dlg.FolderName, _result, _settings, Environment.MachineName, RegisterLine, cert, OfficerFlows.Trusted(OfficerStore.Shared.Load()),
                                                           DateTimeOffset.UtcNow, AuthApp.Record);
                Status = $"Raportul semnat a fost scris: {files.SignedPdf} (SHA-256 {files.SignedSha256}) și tabelul {files.Csv}.";
                Process.Start(new ProcessStartInfo(files.SignedPdf) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                Status = "Raportul NU a fost scris: " + ex.Message;
            }
        }
    }
}
