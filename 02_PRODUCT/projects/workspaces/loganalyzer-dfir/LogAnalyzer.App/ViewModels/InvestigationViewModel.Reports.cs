using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Privacy;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.Services;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// Report signing and verification (F3), verifiable case archive (F4), PII-masked export (F10), shadow copies as a target (E10), historical sources
    /// (E1) and „Colectează tot” (G3) on the investigation page.
    /// </summary>
    public partial class InvestigationViewModel
    {
        /// <summary>Last line about a report or archive operation (signed / verified / archived / masked).</summary>
        [ObservableProperty] private string _reportStatus = "";

        // ---- F3: signing settings (per Windows user, %APPDATA%\LogAnalyzer\report_signing.json; the private key file stays where the operator keeps it) ----
        [ObservableProperty] private string _signingKeyPath = ReportSigningSettings.Load().PrivateKeyPath;
        [ObservableProperty] private string _signerName = ReportSigningSettings.Load().Signer;
        /// <summary>Trusted public keys (base64 SubjectPublicKeyInfo), one per line.</summary>
        [ObservableProperty] private string _trustedKeysText = string.Join(Environment.NewLine, ReportSigningSettings.Load().TrustedPublicKeys);

        /// <summary>The manifest SHA-256 the operator copies from the custody register to authenticate an archive being imported.</summary>
        [ObservableProperty] private string _expectedArchiveSha256 = "";

        internal static string? SigningSettingsPathOverride { get; set; }

        private ReportSigningSettings CurrentSigning() => new()
        {
            PrivateKeyPath = SigningKeyPath.Trim(), Signer = SignerName.Trim(),
            TrustedPublicKeys = TrustedKeysText.Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        };

        [RelayCommand]
        private void SaveSigningSettings()
        {
            var s = CurrentSigning();
            s.Save(SigningSettingsPathOverride);
            ReportStatus = $"Setările semnăturii au fost salvate: {(s.CanSign ? "cheia de semnare este accesibilă" : "fără cheie de semnare accesibilă (rapoartele nu vor fi semnate)")}, {s.TrustedPublicKeys.Count} chei de încredere.";
            // Security audit #20: a legacy bare key is accepted but never silently; the warning names the re-protection command.
            try { if (s.CanSign && ReportKeyProtection.IsLegacyBareKey(File.ReadAllText(s.PrivateKeyPath))) ReportStatus += " " + ReportKeyProtection.LegacyBareKeyWarning; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        [RelayCommand]
        private void BrowseSigningKey()
        {
            var dlg = new OpenFileDialog { Title = "Fișierul cheii private de semnare (creat cu LogAnalyzer.KeyGen report-keygen)", Filter = "Toate fișierele|*.*" };
            if (dlg.ShowDialog() == true) SigningKeyPath = dlg.FileName;
        }

        /// <summary>Signs a report just written, audits it in the case and returns the line to show.</summary>
        public string SignReport(string pdf, CaseWorkspace? ws, string? signerOverride = null)
        {
            var line = CurrentSigning().SignIfConfigured(pdf, signerOverride);
            ws?.Audit("report.signature", $"{Path.GetFileName(pdf)}: {line}");
            return line;
        }

        /// <summary>„Verifică un raport”: VALID / MODIFIED / UNKNOWN KEY against the trusted keys configured here.</summary>
        [RelayCommand]
        private void VerifyReport()
        {
            var dlg = new OpenFileDialog { Title = "Raportul de verificat (fișierul .sig trebuie să fie alături)", Filter = "PDF (*.pdf)|*.pdf|Toate fișierele|*.*" };
            if (dlg.ShowDialog() != true) return;
            ReportStatus = VerifyReportFile(dlg.FileName);
        }

        public string VerifyReportFile(string pdf)
        {
            var r = ReportSignature.Verify(pdf, CurrentSigning().TrustedPublicKeys);
            return $"{r.Word}: {r.Message}";
        }

        // ---- F10: export with personal data masked ----

        [RelayCommand]
        private void ExportMaskedPdf()
        {
            if (_result is null) { ReportStatus = "Rulați întâi o investigație sau deschideți un caz."; return; }
            var dlg = new SaveFileDialog { FileName = $"Raport_{_result.Case.Info.CaseId}_mascat.pdf", Filter = "PDF (*.pdf)|*.pdf", Title = "Raport cu date personale mascate (pentru partajare)" };
            if (dlg.ShowDialog() != true) return;
            var masker = PiiMasker.FromTimeline(_result.Timeline, _result.Case.Info.Host, _result.Case.Info.User);
            var audience = (LogAnalyzer.Dfir.Reporting.ReportAudience)Math.Clamp(ReportAudienceIndex, 0, ReportAudienceLabels.Length - 1);
            InvestigationReportPdf.Write(_result, dlg.FileName, masker.User(LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified.Split('\\').Last()), audience: audience, masker: masker);
            _result.Case.Audit("report.pdf_masked", $"{Path.GetFileName(dlg.FileName)} pseudonime={masker.Mapping.Count}");
            ReportStatus = $"Raport cu date personale mascate salvat ({masker.Mapping.Count} valori înlocuite cu pseudonime consecvente). " + SignReport(dlg.FileName, _result.Case, MaskedSigner(masker));
        }

        /// <summary>The signer written into the .sig of a masked export: the configured signer label passed through the masker, else the operator's pseudonym. Never the raw Windows user name.</summary>
        public string MaskedSigner(PiiMasker masker)
        {
            var label = SignerName.Trim();
            var op = LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified.Split('\\').Last();
            masker.KnownUser(op);
            return label.Length > 0 ? masker.MaskSignerLabel(label) : masker.User(op);   // a person's display name becomes a pseudonym; an organisation label stays
        }

        [RelayCommand]
        private void ExportMaskedTimeline()
        {
            if (_result is null) { ReportStatus = "Rulați întâi o investigație sau deschideți un caz."; return; }
            var dlg = new SaveFileDialog { FileName = $"Cronologie_{_result.Case.Info.CaseId}_mascata.csv", Filter = "CSV (*.csv)|*.csv", Title = "Cronologie cu date personale mascate" };
            if (dlg.ShowDialog() != true) return;
            var masker = PiiMasker.FromTimeline(_result.Timeline, _result.Case.Info.Host, _result.Case.Info.User);
            LogAnalyzer.Dfir.IO.TimelineCsv.Write(dlg.FileName, _result.Timeline.Select(masker.Mask).ToList());
            _result.Case.Audit("timeline.export_masked", $"{Path.GetFileName(dlg.FileName)} rows={_result.Timeline.Count} pseudonime={masker.Mapping.Count}");
            ReportStatus = $"Cronologie mascată salvată: {_result.Timeline.Count:N0} rânduri, {masker.Mapping.Count} valori înlocuite (același pseudonim pentru aceeași valoare, ca în raportul mascat).";
        }

        // ---- F4: verifiable case archive ----

        [RelayCommand]
        private void ExportCaseArchive()
        {
            if (_result is null) { ReportStatus = "Rulați întâi o investigație sau deschideți un caz."; return; }
            var dlg = new SaveFileDialog { FileName = $"{_result.Case.Info.CaseId}.zip", Filter = "Arhivă ZIP (*.zip)|*.zip", Title = "Arhivă verificabilă a cazului" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var signing = CurrentSigning();
                var a = CaseArchive.Export(_result.Case, dlg.FileName, signing: signing);
                var check = CaseArchive.Verify(a.ZipPath, a.ManifestSha256, signing.TrustedPublicKeys);
                ReportStatus = (check.Ok ? "Arhivă creată și verificată: " : "Arhivă creată, dar VERIFICAREA A EȘUAT: ") +
                               $"{a.FileCount} fișiere; SHA-256 manifest {a.ManifestSha256}; SHA-256 arhivă {a.ZipSha256} (și în {Path.GetFileName(CaseArchive.SidecarPath(a.ZipPath))}). " +
                               (a.Signed ? "Manifestul este semnat digital (manifest.json.sig). " : "Arhiva NU este semnată (" + a.SignNote + "): la import va trebui autentificată cu SHA-256 al manifestului. ") +
                               "README.txt din arhivă arată cum se verifică fără LogAnalyzer (Get-FileHash, certutil). Treceți SHA-256 al manifestului în registrul de custodie.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ReportStatus = "Arhiva nu a putut fi creată: " + ex.Message; }
        }

        [RelayCommand]
        private async Task ImportCaseArchive()
        {
            var dlg = new OpenFileDialog { Title = "Arhivă de caz (ZIP cu manifest.json)", Filter = "Arhivă ZIP (*.zip)|*.zip" };
            if (dlg.ShowDialog() != true) return;
            var casesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "Cases");
            try
            {
                var trusted = CurrentSigning().TrustedPublicKeys;
                var expected = ExpectedArchiveSha256.Trim();
                var (ws, check) = await Task.Run(() => CaseArchive.Import(dlg.FileName, casesRoot, expected.Length > 0 ? expected : null, trusted));
                if (ws is null && check.Problems.Contains("arhivă neverificabilă"))
                {
                    var choice = System.Windows.MessageBox.Show(check.Summary + Environment.NewLine + Environment.NewLine + "Importați totuși ca neverificată? Alegerea se înregistrează în custodie.", "Arhivă neverificabilă",
                        System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
                    if (choice == System.Windows.MessageBoxResult.Yes)
                        (ws, check) = await Task.Run(() => CaseArchive.Import(dlg.FileName, casesRoot, expected.Length > 0 ? expected : null, trusted, importAsUnverified: true));
                }
                if (ws is null) { ReportStatus = "Arhiva NU a fost importată: " + check.Summary + " " + string.Join("; ", check.Problems.Take(10)); return; }
                var res = await CaseLoader.OpenAsync(ws.Root, new LogAnalyzer.Dfir.Case.RecentCases(LogAnalyzer.Dfir.Case.RecentCases.DefaultFile()));
                if (res.Opened) ShowLoaded(res.Case!);
                ReportStatus = (check.Authenticated ? "Arhivă verificată și importată: " : "Arhivă importată ca NEVERIFICATĂ (înregistrat în custodie): ") + check.Summary;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { ReportStatus = "Arhiva nu a putut fi importată: " + ex.Message; }
        }

        // ---- E10: shadow copies as an analysis target ----

        public ObservableCollection<ShadowCopyInfo> ShadowCopyList { get; } = new();
        [ObservableProperty] private ShadowCopyInfo? _selectedShadowCopy;
        [ObservableProperty] private string _shadowStatus = "Copiile de umbră (VSS) se listează doar pentru citire; se cer drepturi de administrator.";

        [RelayCommand]
        private async Task ListShadowCopies()
        {
            ShadowStatus = "Se citesc copiile de umbră (WMI Win32_ShadowCopy)…";
            var r = await Task.Run(() => ShadowCopies.List());
            ShadowCopyList.Clear();
            foreach (var s in r.Items) ShadowCopyList.Add(s);
            SelectedShadowCopy = ShadowCopyList.FirstOrDefault();
            ShadowStatus = r.Message;
        }

        /// <summary>Adds the logs and hives of the chosen copy to the evidence list; at „Pornește analiza” they are copied into the case with the copy recorded in custody.</summary>
        [RelayCommand]
        private async Task ImportFromShadowCopy()
        {
            if (SelectedShadowCopy is not { } copy) { ShadowStatus = "Alegeți întâi o copie de umbră din listă."; return; }
            if (!ShadowCopies.IsAdministrator()) { ShadowStatus = "Nedeterminat: citirea unei copii de umbră cere drepturi de administrator."; return; }
            var files = await Task.Run(() => ShadowCopies.ArtifactPaths(copy));
            foreach (var f in files) if (!ImportFiles.Contains(f)) ImportFiles.Add(f);
            CollectFromThisStation = false;
            ShadowStatus = files.Count == 0
                ? $"Copia {copy.Label} nu conține jurnale sau hive-uri citibile."
                : $"Copia {copy.Label} este ținta analizei: {files.Count} fișiere (jurnale, hive-uri, Prefetch) adăugate la probe. La „Pornește analiza” se copiază în caz, cu originea în custodie.";
        }

        // ---- E1: historical sources in a folder (archived logs, Windows.old) ----

        [RelayCommand]
        private void AddHistoricalFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Folder cu o copie a discului sau cu Windows.old (se caută jurnale arhivate, jurnale și hive-uri vechi)" };
            if (dlg.ShowDialog() != true) return;
            var found = HistoricalSources.Discover(dlg.FolderName);
            foreach (var s in found) if (!ImportFiles.Contains(s.Path)) ImportFiles.Add(s.Path);
            Log += $"Surse istorice găsite: {found.Count} ({found.Count(s => s.Origin.Contains("arhivat"))} jurnale arhivate, {found.Count(s => s.Origin.Contains("Windows.old"))} din Windows.old).{Environment.NewLine}";
        }

        // ---- G3: „Colectează tot” ----

        /// <summary>Runs the complete collection and analysis for the station's role; the result is published once and reused by every page.</summary>
        [RelayCommand]
        private async Task CollectAll()
        {
            var plan = CollectAllState.Plan(LogAnalyzer.Core.Services.Edition.StationRoleContext.Role);
            CollectFromThisStation = true;
            ProfileIndex = plan.ProfileIndex;
            CollectAllState.Instance.IsRunning = true;
            CollectAllState.Instance.Status = "Colectează tot: " + plan.Description;
            try { await Run(); }
            finally { CollectAllState.Instance.IsRunning = false; }
            if (_result is null) CollectAllState.Instance.Status = "Colectarea nu a produs date: " + OperationStatus;
        }
    }
}
