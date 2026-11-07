using System;
using System.IO;
using System.Windows;
using LogAnalyzer.Core.Services;

namespace LogAnalyzer.UI.Views
{
    /// <summary>Licence activation (security audit #3): accepts only a licence signed with the offline licence key; says plainly why one is refused.</summary>
    public partial class ActivationWindow : Window
    {
        private readonly LicenseService _licenseService;
        private readonly AuditLogService? _audit;

        public ActivationWindow(LicenseService licenseService, AuditLogService? audit = null)
        {
            InitializeComponent();
            _licenseService = licenseService;
            _audit = audit;
            var posture = LogAnalyzer.Core.Services.Edition.StationPostureContext.Current;
            ModuleText.Text = LogAnalyzer.Core.Services.Edition.ModuleCatalog.DisplayName(posture);
            ModulePurposeText.Text = LogAnalyzer.Core.Services.Edition.ModuleCatalog.Purpose(posture);

            try { TxtHardwareId.Text = _licenseService.GetHardwareId(); }
            catch (LogAnalyzer.Core.Services.HardwareIdUnavailableException ex)
            {
                // Independent review #6 + review of the remediations #5: no placeholder id; activation is refused with the exact reason
                // (WMI unreadable, or only empty / placeholder processor and base-board values).
                TxtHardwareId.Text = "";
                ShowError(string.IsNullOrWhiteSpace(ex.Message) ? LicenseService.Explain(LicenseCheck.HardwareIdUnavailable) : ex.Message);
                return;
            }
            if (!_licenseService.HasTrustedKey)
                ShowError(LicenseService.Explain(LicenseCheck.NoTrustedKey));
            else if (_licenseService.CheckSaved() == LicenseCheck.LegacyScheme)
                ShowError("Licența salvată pe această stație este din schema veche și nu mai este valabilă. " + LicenseService.Explain(LicenseCheck.LegacyScheme));
        }

        private void CopyHwid_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtHardwareId.Text))
            {
                Clipboard.SetText(TxtHardwareId.Text);
                TxtStatus.Text = "Hardware ID-ul a fost copiat.";
                TxtStatus.Foreground = Application.Current.TryFindResource("SuccessBrush") as System.Windows.Media.Brush;
            }
        }

        private void Activate_Click(object sender, RoutedEventArgs e) => TryActivate(TxtLicenseKey.Text);

        /// <summary>Offline activation, step 1: writes the request file (product, module, hw, UTC date; nothing else). Nothing is sent anywhere.</summary>
        private void SaveRequest_Click(object sender, RoutedEventArgs e)
        {
            var hw = TxtHardwareId.Text;
            if (!LicenseService.IsHardwareId(hw)) { ShowError(LicenseService.Explain(LicenseCheck.HardwareIdUnavailable)); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = LicenseRequest.FileName(hw),
                Filter = "Cerere de licență (*.json)|*.json",
                Title = "Salvează cererea de licență",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                // review of R2: the hardware fingerprint leaves the station with this file, so it is recorded BEFORE it is written (application log;
                // the sealed chain is opened by the sign-in service only after the licence, so it is not written from here)
                _audit?.LogAction("license.request_saved", $"modul {_licenseService.CompiledModule}, hw {hw}, fișier {dlg.FileName}");
                File.WriteAllText(dlg.FileName, LicenseRequest.Build(hw, _licenseService.CompiledModule, DateTime.UtcNow));
                TxtStatus.Text = "Cererea a fost salvată în " + dlg.FileName + ". Duceți fișierul celui care emite licența.";
                TxtStatus.Foreground = Application.Current.TryFindResource("SuccessBrush") as System.Windows.Media.Brush;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("Cererea nu a putut fi scrisă: " + ex.Message); }
        }

        /// <summary>Offline activation, step 2: reads a .lic / .txt file and validates it exactly as a pasted licence.</summary>
        private void ImportLicense_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Licență (*.lic;*.txt)|*.lic;*.txt", Title = "Importă fișierul de licență" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                if (new FileInfo(dlg.FileName).Length > 32 * 1024) { ShowError(LicenseService.Explain(LicenseCheck.Malformed)); return; }
                var text = File.ReadAllText(dlg.FileName).Trim();
                TxtLicenseKey.Text = text;
                TryActivate(text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("Fișierul nu a putut fi citit: " + ex.Message); }
        }

        private void TryActivate(string input)
        {
            if (_licenseService.ValidateAndSaveKey(input, out var result))
            {
                MessageBox.Show("Licența a fost verificată și activată pentru această stație.", "Activare reușită", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
            }
            else
            {
                ShowError(LicenseService.Explain(result, _licenseService.LastClaims, _licenseService.CompiledPosture));
            }
        }

        private void ShowError(string text)
        {
            TxtStatus.Text = text;
            TxtStatus.Foreground = Application.Current.TryFindResource("CriticalBrush") as System.Windows.Media.Brush;
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
