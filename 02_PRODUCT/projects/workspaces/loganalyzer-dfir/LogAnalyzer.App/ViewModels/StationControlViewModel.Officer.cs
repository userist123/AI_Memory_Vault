using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// Owner decisions 2026-10-16 on the control page: the anchor code of the application's audit log (shown, copied, exported for a CD-R, and the previous
    /// one checked as A11), and the security officer's signature of the official report (one button + the passphrase), the officer list and "Verifică raport".
    /// </summary>
    public partial class StationControlViewModel
    {
        /// <summary>The anchor code from the signed proces-verbal of the previous control (optional). Proposed from the profile; the paper is what counts.</summary>
        [ObservableProperty] private string _previousAnchorCode = "";
        [ObservableProperty] private string _officerStatus = OfficerSignature.UnsignedLabel + ".";
        [ObservableProperty] private string _officerInfo = "";
        [ObservableProperty] private string _verifyFingerprint = "";
        [ObservableProperty] private string _verifyResult = "";

        public string AnchorLine => _report?.Anchor?.Line ?? "";
        public bool HasAnchor => _report?.Anchor is not null;

        public string PreviousAnchorHelp =>
            "Codul de ancorare de pe procesul-verbal semnat al controlului anterior (4 grupe de 5 caractere). Aplicația verifică dacă jurnalul ei a rămas același de atunci (verificarea A11). " +
            "Valoarea propusă este ultima cunoscută pe această stație: comparați-o cu hârtia; dacă diferă, scrieți codul de pe hârtie.";

        private void InitOfficer()
        {
            try { PreviousAnchorCode = ProfileProvider.Shared.Current?.LastAnchorCode ?? ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }

        private static string? AuditLogPath()
        {
            try { return LogAnalyzer.UI.Services.AuthApp.Service.AuditPath; }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>After the control: the anchor of the head of the audit log, the trusted officers to print, and the code remembered for the next control.</summary>
        private void AfterControlAnchor()
        {
            if (_report is null) return;
            var log = AuditLogPath();
            try { _report.Anchor = log is null ? null : AuditAnchor.Current(log); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _report.Anchor = null; }
            ProcedureProfile? profile = null;
            try { profile = ProfileProvider.Shared.Current; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            _report.RegisteredOfficers = OfficerRegistry.Trusted(profile, out _);
            OfficerStatus = OfficerSignature.UnsignedLabel + ". Raportul devine oficial după ce ofițerul de securitate apasă „Semnează ca ofițer de securitate”.";
            OnPropertyChanged(nameof(AnchorLine)); OnPropertyChanged(nameof(HasAnchor));
            if (_report.Anchor is { } a && profile is not null && profile.LastAnchorCode != a.Code)
            {
                var copy = Clone(profile);
                copy.LastAnchorCode = a.Code;
                SaveProfile(copy, out _);
            }
        }

        private static ProcedureProfile Clone(ProcedureProfile p) =>
            System.Text.Json.JsonSerializer.Deserialize<ProcedureProfile>(ProfileStore.ToJson(p), LogAnalyzer.Dfir.IO.Json.Options) ?? new ProcedureProfile();

        /// <summary>Saves the profile in force (administrator session required by ProfileStore) and audits the change. Returns false with the reason.</summary>
        private static bool SaveProfile(ProcedureProfile p, out string error)
        {
            error = "";
            var provider = ProfileProvider.Shared;
            try
            {
                var before = ProfileStore.Load(provider.Path).Profile;
                var issues = ProfileStore.Save(p, provider.Path, out _);
                if (issues.Any(i => i.IsError)) { error = string.Join("; ", issues.Where(i => i.IsError).Take(3)); return false; }
                if (LogAnalyzer.UI.Services.AuthApp.IsReady)
                    OperationAudit.AuditProfileSave(LogAnalyzer.UI.Services.AuthApp.Service, LogAnalyzer.UI.Services.AuthApp.Session, before, p, Path.GetFileName(provider.Path));
                provider.Replace(p);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex.Message; return false; }
        }

        [RelayCommand]
        private void CopyAnchorCode()
        {
            if (_report?.Anchor is not { } a) { Status = "Rulați întâi controlul: codul de ancorare apare la sfârșitul lui."; return; }
            try { Clipboard.SetText(a.Code); Status = $"Codul de ancorare {a.Code} a fost copiat; treceți-l pe procesul-verbal."; }
            catch (System.Runtime.InteropServices.COMException) { Status = "Clipboard-ul este ocupat de alt program; încercați din nou."; }
        }

        [RelayCommand]
        private void ExportAnchor()
        {
            var log = AuditLogPath();
            if (log is null) { Status = "Jurnalul aplicației nu este disponibil (autentificarea nu este pornită)."; return; }
            var dlg = new OpenFolderDialog { Title = "Alegeți unde se copiază jurnalul aplicației (apoi ardeți folderul pe un CD-R)" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var x = AuditAnchor.Export(log, dlg.FolderName, Environment.MachineName, DateTimeOffset.UtcNow);
                LogAnalyzer.UI.Services.AuthApp.Record(OperationAudit.AnchorExported, OperationAudit.AnchorExportedDetail(x.Anchor.Seq, x.Anchor.Code, x.LogSha256, x.Folder));
                OfficerInfo = $"Jurnalul aplicației a fost copiat în {x.Folder}.{Environment.NewLine}{x.Anchor.Line}. SHA-256 al copiei: {x.LogSha256}.{Environment.NewLine}" +
                              "Ardeți folderul pe un CD-R (se scrie o singură dată), notați pe disc data și stația și predați-l ofițerului de securitate.";
                Status = "Jurnalul aplicației a fost copiat pentru CD-R.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Status = "Copierea jurnalului nu s-a putut face: " + ex.Message; }
        }

        [RelayCommand]
        private void SignAsOfficer()
        {
            if (_report is null) { Status = "Rulați întâi controlul."; return; }
            var ws = LogAnalyzer.UI.Services.LiveCase.GetConfirmed();
            if (ws is null) { Status = LogAnalyzer.UI.Services.LiveCase.ScopeRequiredMessage; return; }
            var keyDlg = new OpenFileDialog { Title = "Alegeți fișierul cheii dumneavoastră de ofițer de securitate (de pe mediul dumneavoastră)", Filter = "Cheie ofițer (*.key)|*.key|Toate fișierele|*.*" };
            if (keyDlg.ShowDialog() != true) return;
            string keyText;
            try { keyText = File.ReadAllText(keyDlg.FileName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Cheia nu a putut fi citită: " + ex.Message; return; }
            var pass = LogAnalyzer.UI.Views.OfficerKeyDialog.Ask("Semnează ca ofițer de securitate",
                "Cheia se deschide doar pentru această semnătură și se închide imediat după. Raportul semnat devine raportul oficial.", false, out _);
            if (pass is null) return;
            if (Result is null) BuildResult();
            ProcedureProfile? profile = null;
            try { profile = ProfileProvider.Shared.Current; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            var report = _report;
            var manual = ManualItems.Select(m => m.ToResult()).ToList();
            try
            {
                var res = OfficerSigning.SignOfficial(report, profile, keyText, pass,
                    r => ControlReportPdf.SaveOfficialToCase(r, ws, Inspector, Notes, BuildHeader(r), Result, manual), DateTimeOffset.UtcNow);
                pass = null;
                LogAnalyzer.UI.Services.AuthApp.Record(OperationAudit.ReportOfficerSigned, OperationAudit.OfficerSignedDetail(Path.GetFileName(res.Pdf), res.Sha256, res.Fingerprint, res.AnchorCode));
                OfficerStatus = $"RAPORT OFICIAL semnat de ofițerul de securitate {res.Officer} (amprenta {res.Fingerprint}).";
                OfficerInfo = $"Raportul oficial: {res.Pdf}{Environment.NewLine}Semnătura: {res.SignatureFile}{Environment.NewLine}SHA-256: {res.Sha256}{Environment.NewLine}" +
                              "Tipăriți raportul oficial; amprenta cheii și codul de ancorare sunt pe ultima pagină.";
                Status = "Raportul a fost semnat de ofițerul de securitate.";
                Process.Start(new ProcessStartInfo(res.Pdf) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Status = "Raportul NU a fost semnat: " + ex.Message;
            }
        }

        [RelayCommand]
        private void RegisterOfficer()
        {
            ProcedureProfile current;
            try { current = ProfileProvider.Shared.Current ?? new ProcedureProfile(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { Status = "Profilul de proceduri nu a putut fi citit: " + ex.Message; return; }
            var profile = Clone(current);
            bool first = profile.SecurityOfficers.Count == 0;
            var newDlg = new OpenFileDialog { Title = "Alegeți fișierul cheii ofițerului care se înregistrează", Filter = "Cheie ofițer (*.key)|*.key|Toate fișierele|*.*" };
            if (newDlg.ShowDialog() != true) return;
            try
            {
                var newText = File.ReadAllText(newDlg.FileName);
                var newPass = LogAnalyzer.UI.Views.OfficerKeyDialog.Ask("Înregistrează ofițer de securitate",
                    "Ofițerul care se înregistrează scrie numele și parola cheii lui (dovedește că deține cheia).", true, out var name);
                if (newPass is null) return;
                string? approverText = null, approverPass = null;
                if (!first)
                {
                    MessageBox.Show("Există deja ofițeri de securitate înregistrați. Înregistrarea trebuie aprobată de unul dintre ei, cu cheia și parola lui.",
                                    "Aprobare necesară", MessageBoxButton.OK, MessageBoxImage.Information);
                    var apDlg = new OpenFileDialog { Title = "Alegeți fișierul cheii ofițerului care aprobă", Filter = "Cheie ofițer (*.key)|*.key|Toate fișierele|*.*" };
                    if (apDlg.ShowDialog() != true) return;
                    approverText = File.ReadAllText(apDlg.FileName);
                    approverPass = LogAnalyzer.UI.Views.OfficerKeyDialog.Ask("Aprobarea unui ofițer înregistrat", "Ofițerul care aprobă scrie parola cheii lui.", false, out _);
                    if (approverPass is null) return;
                }
                var row = OfficerSigning.Register(profile, name, newText, newPass, approverText, approverPass, DateTimeOffset.UtcNow);
                newPass = null; approverPass = null;
                if (!SaveProfile(profile, out var err)) { Status = "Ofițerul NU a fost înregistrat: profilul nu s-a putut salva (" + err + ")."; return; }
                LogAnalyzer.UI.Services.AuthApp.Record(OperationAudit.OfficerRegistered, OperationAudit.OfficerRegisteredDetail(row.Fingerprint, row.Basis, row.ApprovedBy ?? ""));
                OfficerInfo = $"Ofițer de securitate înregistrat: {row.Name}{Environment.NewLine}AMPRENTA CHEII: {row.Fingerprint}{Environment.NewLine}" +
                              (first ? "Este primul ofițer: treceți amprenta de mai sus pe procesul-verbal și semnați lângă ea. La verificare, semnătura se compară cu amprenta de pe hârtie."
                                     : $"Aprobat de ofițerul cu amprenta {row.ApprovedBy}.");
                if (first)
                    MessageBox.Show($"AMPRENTA CHEII OFIȚERULUI {row.Name}:\n\n{row.Fingerprint}\n\nTreceți-o pe procesul-verbal și semnați lângă ea.",
                                    "Primul ofițer de securitate", MessageBoxButton.OK, MessageBoxImage.Information);
                Status = "Ofițerul de securitate a fost înregistrat în profilul de proceduri.";
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                Status = "Ofițerul NU a fost înregistrat: " + ex.Message;
            }
        }

        [RelayCommand]
        private void VerifyReport()
        {
            var dlg = new OpenFileDialog { Title = "Alegeți raportul de verificat (PDF)", Filter = "PDF (*.pdf)|*.pdf" };
            if (dlg.ShowDialog() != true) return;
            string source;
            System.Collections.Generic.List<string> pinned;
            if (OfficerKey.Normalize(VerifyFingerprint).Length > 0) { pinned = [VerifyFingerprint]; source = "amprenta scrisă de dumneavoastră"; }
            else if (VerifyFingerprint.Trim().Length > 0) { VerifyResult = "Amprenta scrisă nu are forma corectă (32 de caractere 0-9 și A-F, de regulă în 8 grupe de 4)."; return; }
            else
            {
                ProcedureProfile? p = null;
                try { p = ProfileProvider.Shared.Current; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                pinned = OfficerRegistry.Trusted(p, out _).Select(r => r.Fingerprint).ToList();
                source = "ofițerii de securitate înregistrați pe această stație";
            }
            var r = OfficerSignature.Verify(dlg.FileName, pinned);
            VerifyResult = $"{r.Word}: {r.Message}{Environment.NewLine}(Comparat cu: {source}. Pentru o dovadă independentă de stație, scrieți amprenta de pe procesul-verbal.)";
        }
    }
}
