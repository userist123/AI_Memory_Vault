using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.Core.Services;
using Microsoft.Win32;

namespace LogAnalyzer.LicenseManager;

public partial class MainWindow : Window
{
    // The application shows a 16-hex-digit Hardware ID (LicenseService.GetHardwareId); accept exactly that.
    private static readonly Regex HwidFormat = new("^[0-9A-F]{16}$", RegexOptions.Compiled);

    private readonly LicenseService _license = new(); // hardware id of this PC only; signing uses the chosen offline key
    private readonly LicenseLedger _ledger = new();
    private IssuedLicense? _current;

    public MainWindow()
    {
        InitializeComponent();
        DpCustom.SelectedDate = DateTime.Today.AddYears(1);
        ApplyModuleDefaults();
        RefreshLedger();
        // B10: a runtime older than .NET 10.0.13 is shown in the status line; the window opens and works either way
        if (LogAnalyzer.Dfir.Reporting.RuntimeGuard.Warning(Environment.Version) is { } runtimeWarning) ShowStatus("Atenție .NET: " + runtimeWarning + ".", ok: false);
    }

    private void UseLocalHwid_Click(object sender, RoutedEventArgs e)
    {
        try { TxtHwid.Text = _license.GetHardwareId(); }
        catch (HardwareIdUnavailableException ex)   // review #6: no placeholder id
        {
            TxtHwid.Text = "";
            MessageBox.Show(ex.Message, "Hardware ID indisponibil", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (string.IsNullOrWhiteSpace(TxtClient.Text)) TxtClient.Text = Environment.MachineName;
    }

    private string SelectedModule => (CmbModule.SelectedItem as ComboBoxItem)?.Content as string ?? LicenseFeatures.ModuleInsula;

    private IEnumerable<CheckBox> FeatureBoxes => PnlFeatures.Children.OfType<CheckBox>();

    /// <summary>Capabilities pre-filled with the base set of the module; those the module can never carry are unchecked and disabled.</summary>
    private void ApplyModuleDefaults()
    {
        if (PnlFeatures is null) return;
        var module = SelectedModule;
        foreach (var c in FeatureBoxes)
        {
            var allowed = LicenseFeatures.IsAllowedFor(module, (string)c.Tag);
            c.IsEnabled = allowed;
            c.IsChecked = allowed;
        }
    }

    private void Module_Changed(object sender, SelectionChangedEventArgs e) => ApplyModuleDefaults();

    /// <summary>Reads a cerere_licenta_*.json from the client and fills in the Hardware ID and the module (strict parse; nothing else is taken from the file).</summary>
    private void LoadRequest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Cererea de licență a clientului", Filter = "Cerere de licență (*.json)|*.json|Toate fișierele|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > LicenseRequest.MaxBytes) { ShowStatus("Fișierul nu este o cerere de licență (prea mare).", ok: false); return; }
            if (LicenseRequest.Parse(File.ReadAllText(dlg.FileName)) is not { } req)
            {
                ShowStatus("Fișierul nu este o cerere de licență validă (produs, modul, hw, dată).", ok: false);
                return;
            }
            TxtHwid.Text = req.HardwareId;
            foreach (ComboBoxItem item in CmbModule.Items) if ((string)item.Content == req.Module) CmbModule.SelectedItem = item;
            ApplyModuleDefaults();
            ShowStatus($"Cerere încărcată: Hardware ID {req.HardwareId}, modulul {req.Module}. Completați titularul și generați licența.", ok: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowStatus("Fișierul nu poate fi citit: " + ex.Message, ok: false); }
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var hwid = TxtHwid.Text.Trim().ToUpperInvariant();
        if (!HwidFormat.IsMatch(hwid))
        {
            ShowStatus("Hardware ID invalid: trebuie să aibă exact 16 caractere hexazecimale, așa cum apare în fereastra de activare a aplicației.", ok: false);
            return;
        }

        var holderText = TxtHolder.Text.Trim();
        if (holderText.Length == 0 || holderText.Length > LicenseService.MaxHolderLength)
        {
            ShowStatus($"Titularul este obligatoriu (1–{LicenseService.MaxHolderLength} caractere).", ok: false);
            return;
        }
        if (!FeatureBoxes.Any(c => c.IsChecked == true))
        {
            ShowStatus("Alege cel puțin o capabilitate.", ok: false);
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

        if (!File.Exists(TxtKeyPath.Text.Trim()))
        {
            ShowStatus("Alege fișierul cheii private de licență (creat cu LogAnalyzer.KeyGen license-keygen).", ok: false);
            return;
        }
        string licenseString;
        try
        {
            var keyText = File.ReadAllText(TxtKeyPath.Text.Trim());
            using var key = LogAnalyzer.Dfir.Reporting.ReportSignature.LoadPrivateKey(keyText,
                LogAnalyzer.Dfir.Reporting.ReportKeyProtection.NeedsPassphrase(keyText) ? PwdKey.Password : null);
            licenseString = LicenseService.SignV2(key, hwid, expiry, SelectedModule, TxtHolder.Text,
                FeatureBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList());
            // Self-check with the exact verification the application performs at activation and at every start (as the module it is issued for).
            var verifier = new LicenseService([Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())], null, null, LicenseFeatures.PostureOf(SelectedModule));
            if (!verifier.VerifyLicenseString(hwid, licenseString, DateTime.UtcNow))
            {
                ShowStatus("Auto-verificarea a eșuat: licența nu ar fi acceptată de aplicație. Nu o distribui.", ok: false);
                return;
            }
            if (!LogAnalyzer.Dfir.Reporting.ReportKeyProtection.IsProtected(keyText))
                ShowStatus("Atenție: cheia privată este stocată necriptat.", ok: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            ShowStatus("Cheia privată nu poate fi folosită: " + ex.Message, ok: false);
            return;
        }

        _current = new IssuedLicense(DateTime.UtcNow, TxtClient.Text.Trim(), hwid, expiry, licenseString, TxtNotes.Text.Trim());
        _ledger.Append(_current);
        TxtLicense.Text = licenseString;
        BtnCopy.IsEnabled = BtnSave.IsEnabled = true;
        ShowStatus($"Licență validă pentru {hwid}, până la {expiry:yyyy-MM-dd} (verificată cu aceeași logică ca aplicația). Înregistrată în registru.", ok: true);
        RefreshLedger();
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Cheia privată de licență (license-keygen)", Filter = "Toate fișierele|*.*" };
        if (dlg.ShowDialog(this) == true) TxtKeyPath.Text = dlg.FileName;
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
