using System.Windows;
using LogAnalyzer.Core.Services;

namespace LogAnalyzer.UI.Views
{
    /// <summary>Licence activation (security audit #3): accepts only a licence signed with the offline licence key; says plainly why one is refused.</summary>
    public partial class ActivationWindow : Window
    {
        private readonly LicenseService _licenseService;

        public ActivationWindow(LicenseService licenseService)
        {
            InitializeComponent();
            _licenseService = licenseService;

            try { TxtHardwareId.Text = _licenseService.GetHardwareId(); }
            catch (LogAnalyzer.Core.Services.HardwareIdUnavailableException)
            {
                // Independent review #6: no placeholder id; activation is refused with the reason.
                TxtHardwareId.Text = "";
                ShowError(LicenseService.Explain(LicenseCheck.HardwareIdUnavailable));
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

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            if (_licenseService.ValidateAndSaveKey(TxtLicenseKey.Text, out var result))
            {
                MessageBox.Show("Licența a fost verificată și activată pentru această stație.", "Activare reușită", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
            }
            else
            {
                ShowError(LicenseService.Explain(result));
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
