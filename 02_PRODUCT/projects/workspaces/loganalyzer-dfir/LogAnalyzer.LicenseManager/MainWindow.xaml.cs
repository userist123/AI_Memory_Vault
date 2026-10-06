using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using LogAnalyzer.Core.Services;
using Microsoft.Win32;

namespace LogAnalyzer.LicenseManager;

public partial class MainWindow : Window
{
    // The application shows a 16-hex-digit Hardware ID (LicenseService.GetHardwareId); accept exactly that.
    private static readonly Regex HwidFormat = new("^[0-9A-F]{16}$", RegexOptions.Compiled);

    private readonly LicenseService _license = new();
    private readonly LicenseLedger _ledger = new();
    private IssuedLicense? _current;

    public MainWindow()
    {
        InitializeComponent();
        DpCustom.SelectedDate = DateTime.Today.AddYears(1);
        RefreshLedger();
    }

    private void UseLocalHwid_Click(object sender, RoutedEventArgs e)
    {
        TxtHwid.Text = _license.GetHardwareId();
        if (string.IsNullOrWhiteSpace(TxtClient.Text)) TxtClient.Text = Environment.MachineName;
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var hwid = TxtHwid.Text.Trim().ToUpperInvariant();
        if (!HwidFormat.IsMatch(hwid))
        {
            ShowStatus("Hardware ID invalid: trebuie să aibă exact 16 caractere hexazecimale, așa cum apare în fereastra de activare a aplicației.", ok: false);
            return;
        }

        DateTime expiry;
        if (RbCustom.IsChecked == true)
        {
            if (DpCustom.SelectedDate is not DateTime d || d.Date <= DateTime.UtcNow.Date)
            {
                ShowStatus("Alege o dată de expirare din viitor.", ok: false);
                return;
            }
            expiry = d.Date;
        }
        else
        {
            int years = Rb10.IsChecked == true ? 10 : Rb3.IsChecked == true ? 3 : 1;
            expiry = DateTime.UtcNow.Date.AddYears(years);
        }

        var licenseString = _license.BuildLicenseString(hwid, expiry);

        // Self-check with the exact verification the application performs at activation and at every start.
        if (!_license.VerifyLicenseString(hwid, licenseString, DateTime.UtcNow))
        {
            ShowStatus("Auto-verificarea a eșuat: licența nu ar fi acceptată de aplicație. Nu o distribui.", ok: false);
            return;
        }

        _current = new IssuedLicense(DateTime.UtcNow, TxtClient.Text.Trim(), hwid, expiry, licenseString, TxtNotes.Text.Trim());
        _ledger.Append(_current);
        TxtLicense.Text = licenseString;
        BtnCopy.IsEnabled = BtnSave.IsEnabled = true;
        ShowStatus($"Licență validă pentru {hwid}, până la {expiry:yyyy-MM-dd} (verificată cu aceeași logică ca aplicația). Înregistrată în registru.", ok: true);
        RefreshLedger();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        Clipboard.SetText(_current.License);
        ShowStatus("Licența a fost copiată. Lipește-o în fereastra de activare a aplicației.", ok: true);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var dlg = new SaveFileDialog
        {
            FileName = "license.lic",
            Filter = "Licență LogAnalyzer (license.lic)|license.lic|Toate fișierele|*.*",
            Title = "Salvează license.lic lângă executabilul aplicației pe stația clientului",
        };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, _current.License);
        ShowStatus($"Salvat: {dlg.FileName}. Fișierul trebuie pus în același folder cu LogAnalyzer.exe.", ok: true);
    }

    private void OpenLedger_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LicenseLedger.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", LicenseLedger.Folder) { UseShellExecute = true });
    }

    private void RefreshLedger()
    {
        try { GridLedger.ItemsSource = _ledger.ReadAll().OrderByDescending(l => l.IssuedUtc).ToList(); }
        catch (IOException ex) { ShowStatus($"Registrul nu poate fi citit: {ex.Message}", ok: false); }
    }

    private void ShowStatus(string text, bool ok)
    {
        TxtStatus.Text = text;
        TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource(ok ? "Ok" : "Bad");
    }
}
