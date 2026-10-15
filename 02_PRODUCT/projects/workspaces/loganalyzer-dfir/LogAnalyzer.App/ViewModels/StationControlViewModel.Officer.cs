using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.UI.Services;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// Owner decisions 2026-10-16 on the control page: the anchor code of the application's audit log (shown, copied, exported for a CD-R, the previous one
    /// typed from the paper and checked as A11), the security officer's qualified signature of the report (smart card, in the application or in Adobe), its
    /// verification against the fingerprint on the proces-verbal, the officer list (by certificate) and the import of qualified CA certificates and CRLs.
    /// </summary>
    public partial class StationControlViewModel
    {
        /// <summary>The anchor code from the signed proces-verbal of the previous control, typed by the officer. Empty by default on purpose.</summary>
        [ObservableProperty] private string _previousAnchorCode = "";
        /// <summary>The code the station proposes (last "audit.anchor" in its own log): shown only after an explicit click, labelled unsafe.</summary>
        [ObservableProperty] private string _stationAnchorHint = "";
        [ObservableProperty] private string _officerStatus = ControlReportPdf.OfficialLabel + ".";
        [ObservableProperty] private string _officerInfo = "";
        [ObservableProperty] private string _verifyFingerprint = "";
        [ObservableProperty] private string _verifyResult = "";

        /// <summary>The report generated last in this session (the one "Semnează ca ofițer de securitate" signs).</summary>
        private string? _lastReportPdf;

        public string AnchorLine => _report?.Anchor?.Line ?? "";
        public bool HasAnchor => _report?.Anchor is not null;

        public string PreviousAnchorHelp =>
            "Scrieți codul de ancorare de pe procesul-verbal semnat al controlului anterior (4 grupe de 5 caractere). Aplicația verifică dacă jurnalul ei a rămas același de atunci (verificarea A11). " +
            "Lăsați gol la primul control.";

        private static string? AuditLogPath()
        {
            try { return AuthApp.Service.AuditPath; }
            catch (InvalidOperationException) { return null; }
        }

        private static QualifiedSignatureTrust Trust() => QualifiedSignatureTrust.FromDirectory();

        /// <summary>Officer, trust and anchor events carry the state of the officer store and the trust folder, so A13 sees files changed outside the application.</summary>
        internal static void RecordOfficerEvent(string ev, string detail) => AuthApp.Record(ev, detail + "; " + OfficerState.Current().Detail);

        [RelayCommand]
        private void ShowStationAnchorHint()
        {
            var log = AuditLogPath();
            var code = log is null ? "" : AuditAnchor.LastRecorded(log);
            StationAnchorHint = code.Length == 0 ? "Stația nu are un cod propus (niciun control anterior înregistrat)."
                : $"Cod propus de stație (nesigur): {code}. Folosiți-l doar dacă este identic cu cel de pe procesul-verbal; cine a rescris jurnalul poate schimba și propunerea.";
        }

        /// <summary>True when the code to check is the one the station proposes (A11 then says so).</summary>
        private bool PreviousCodeIsStationHint()
        {
            var log = AuditLogPath();
            if (log is null || StationAnchorHint.Length == 0) return false;
            return AuditAnchor.TryNormalize(PreviousAnchorCode, out var typed) && typed == AuditAnchor.LastRecorded(log);
        }

        /// <summary>Before the facts are evaluated: A11 (previous code, typed or proposed) and A13 (officer list changes since that anchor, or in the period).</summary>
        private void PrepareAnchorFacts(StationFacts facts, string auditPath, string previousCode, bool fromStation)
        {
            facts.PreviousAnchor = AuditAnchor.Verify(auditPath, previousCode);
            facts.PreviousAnchorFromStation = fromStation;
            facts.OfficerStateNow = OfficerState.Current().Detail;
            try { facts.OfficerStateRecorded = OfficerState.LastRecorded(auditPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { facts.OfficerStateNow = null; }
            try
            {
                var since = facts.PreviousAnchor.MatchedSeq;
                facts.OfficerListChanges = AuditAnchor.EventsSince(auditPath, OperationAudit.OfficerListEvents, since, facts.PeriodStartUtc);
                facts.OfficerListChangesSince = since is { } s ? $"de la înregistrarea nr. {s} (controlul anterior)" : "în perioada controlată (nu există un cod anterior confirmat)";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { facts.OfficerListChanges = null; }
        }

        /// <summary>After the control: the anchor of the head of the audit log, recorded in the log itself ("audit.anchor"), and the trusted officers to print.</summary>
        private void AfterControlAnchor()
        {
            if (_report is null) return;
            _report.Anchor = RecordAnchor("control");
            _report.RegisteredOfficers = OfficerFlows.Trusted(OfficerStore.Shared.Load());
            OfficerStatus = ControlReportPdf.OfficialLabel + ". Ofițerul de securitate semnează cu cardul: „Semnează ca ofițer de securitate”.";
            OnPropertyChanged(nameof(AnchorLine)); OnPropertyChanged(nameof(HasAnchor));
        }

        /// <summary>Takes the anchor of the log head now and records it as "audit.anchor" (the event comes after the anchored entry, so the code stays valid).</summary>
        private static AuditAnchorPoint? RecordAnchor(string reason)
        {
            var log = AuditLogPath();
            if (log is null) return null;
            try
            {
                var a = AuditAnchor.Current(log);
                if (a is not null) RecordOfficerEvent(OperationAudit.AnchorRecorded, OperationAudit.AnchorRecordedDetail(a, reason));
                return a;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
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
                AuthApp.Record(OperationAudit.AnchorExported, OperationAudit.AnchorExportedDetail(x.Anchor.Seq, x.Anchor.Code, x.LogSha256, x.Folder));
                OfficerInfo = $"Jurnalul aplicației a fost copiat în {x.Folder}.{Environment.NewLine}{x.Anchor.Line}. SHA-256 al copiei: {x.LogSha256}.{Environment.NewLine}" +
                              "Ardeți folderul pe un CD-R (se scrie o singură dată), notați pe disc data și stația și predați-l ofițerului de securitate.";
                Status = "Jurnalul aplicației a fost copiat pentru CD-R.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Status = "Copierea jurnalului nu s-a putut face: " + ex.Message; }
        }

        /// <summary>The qualified signing certificate on the inserted card, picked when there are several; null with the reason in Status.</summary>
        private X509Certificate2? PickCardCertificate(string heading)
        {
            System.Collections.Generic.List<X509Certificate2> certs;
            try { certs = OfficerFlows.CardSigningCertificates(AuthApp.Cards, DateTimeOffset.UtcNow); }
            catch (CardException ex) { Status = "Cardul nu poate fi citit: " + ex.Message; return null; }
            if (certs.Count == 0)
            {
                Status = "Pe cardul introdus nu s-a găsit un certificat de semnătură calificată (cu utilizarea „nerepudiere”). Introduceți cardul în cititorul tastaturii și încercați din nou.";
                return null;
            }
            return LogAnalyzer.UI.Views.CertificatePickerWindow.Pick(heading, certs);
        }

        [RelayCommand]
        private void SignAsOfficer()
        {
            if (_report is null) { Status = "Rulați întâi controlul."; return; }
            var pdf = _lastReportPdf ?? GenerateReport(showCopyDialog: false);
            if (pdf is null) return;
            var cert = PickCardCertificate("Semnează ca ofițer de securitate");
            if (cert is null) return;
            var trusted = OfficerFlows.Trusted(OfficerStore.Shared.Load());
            try
            {
                var (signedPath, v) = OfficerFlows.SignReport(pdf, cert, trusted, OfficerFlows.GeneratedReportHashes(AuditLogPath()), Trust(), Environment.MachineName, DateTimeOffset.UtcNow);
                AfterVerification(Path.GetFileName(signedPath), v);
                OfficerStatus = $"Raport semnat de ofițerul de securitate {v.SignerName}. Verificare: {v.VerdictWord} — {v.Headline}.";
                OfficerInfo = $"Raportul semnat: {signedPath}{Environment.NewLine}{v.ToPlainText()}{Environment.NewLine}{AnchorAfterSigningText()}";
                Status = "Raportul a fost semnat cu certificatul calificat de pe card.";
                Process.Start(new ProcessStartInfo(signedPath) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                Status = "Raportul NU a fost semnat: " + ex.Message;
            }
        }

        private string _anchorAfterSigning = "";
        private string AnchorAfterSigningText() => _anchorAfterSigning.Length == 0 ? "" : "Cod de ancorare după semnare (treceți-l pe procesul-verbal): " + _anchorAfterSigning;

        /// <summary>A signed report that is not INVALID is recorded in the sealed audit, then the log is anchored again so the code covers the signature event.</summary>
        private void AfterVerification(string file, OfficialVerification v)
        {
            if (v.Verdict == OfficialVerdict.Invalid) return;
            AuthApp.Record(OperationAudit.ReportOfficerSigned, OperationAudit.OfficerSignedDetail(file, v.OriginalSha256, v.SignedSha256, v.SignerFingerprint, v.SignerName, v.VerdictWord));
            var a = RecordAnchor("report.officer_signed");
            _anchorAfterSigning = a?.Line ?? "";
            if (a is not null && _report is not null) { _report.Anchor = a; OnPropertyChanged(nameof(AnchorLine)); OnPropertyChanged(nameof(HasAnchor)); }
        }

        [RelayCommand]
        private void VerifySignedReport()
        {
            var dlg = new OpenFileDialog { Title = "Alegeți raportul semnat de ofițerul de securitate (PDF)", Filter = "PDF (*.pdf)|*.pdf" };
            if (dlg.ShowDialog() != true) return;
            if (VerifyFingerprint.Trim().Length > 0 && OfficerFingerprint.Normalize(VerifyFingerprint).Length == 0)
            { VerifyResult = "Amprenta scrisă nu are forma corectă: 64 de caractere 0-9 și A-F (de regulă 16 grupe de 4), exact ca pe procesul-verbal."; return; }
            try
            {
                var v = OfficialReportVerifier.Verify(File.ReadAllBytes(dlg.FileName), SignedCmsVerifier.Instance, OfficerFlows.GeneratedReportHashes(AuditLogPath()),
                                                      OfficerFlows.Trusted(OfficerStore.Shared.Load()), VerifyFingerprint, Trust());
                _anchorAfterSigning = "";
                AfterVerification(Path.GetFileName(dlg.FileName), v);
                VerifyResult = v.ToPlainText() + (AnchorAfterSigningText() is { Length: > 0 } t ? Environment.NewLine + t : "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { VerifyResult = "Fișierul nu poate fi citit: " + ex.Message; }
        }

        [RelayCommand]
        private void ImportOfficerTrust()
        {
            var dlg = new OpenFileDialog { Title = "Certificatele autorității de certificare calificate (CA) și listele de revocare (CRL)", Multiselect = true,
                                           Filter = "Certificate și CRL (*.cer;*.crt;*.der;*.pem;*.crl)|*.cer;*.crt;*.der;*.pem;*.crl|Toate fișierele|*.*" };
            if (dlg.ShowDialog() != true) return;
            var done = new System.Collections.Generic.List<string>();
            foreach (var f in dlg.FileNames)
            {
                try
                {
                    var kind = QualifiedSignatureTrust.Import(f);
                    done.Add($"{Path.GetFileName(f)}: {kind}");
                    RecordOfficerEvent(OperationAudit.OfficerTrustImported, $"file={Path.GetFileName(f)}; kind={kind}; sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}");
                }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException) { done.Add($"{Path.GetFileName(f)}: NU — {ex.Message}"); }
            }
            OfficerInfo = "Import pentru verificarea semnăturii calificate:" + Environment.NewLine + string.Join(Environment.NewLine, done);
            Status = "Importul certificatelor CA / listelor de revocare s-a încheiat.";
        }

        /// <summary>
        /// "Ofițeri de securitate…": registers the certificate from the inserted card or from a file (.cer, or a PDF the officer signed); with no officer yet it is the
        /// first one (fingerprint on the proces-verbal), otherwise a request is generated and signed right away by a registered officer's card. Choosing a request
        /// PDF that was signed elsewhere (Adobe) completes it.
        /// </summary>
        [RelayCommand]
        private void ManageOfficers()
        {
            var store = OfficerStore.Shared;
            var data = store.Load();   // always re-read before changing
            try
            {
                X509Certificate2? cert = null;
                System.Collections.Generic.List<X509Certificate2> card = [];
                try { card = OfficerFlows.CardSigningCertificates(AuthApp.Cards, DateTimeOffset.UtcNow); } catch (CardException) { }
                if (card.Count > 0 && MessageBox.Show($"Înregistrați certificatul de semnătură de pe cardul introdus ({OfficerFingerprint.CommonName(card[0])})?\nNu = alegeți un fișier (certificat sau PDF semnat).",
                                                      "Ofițeri de securitate", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    cert = LogAnalyzer.UI.Views.CertificatePickerWindow.Pick("Certificatul ofițerului care se înregistrează", card);
                else
                {
                    var dlg = new OpenFileDialog { Title = "Certificatul ofițerului (.cer) sau un PDF semnat de el; o cerere semnată se finalizează tot de aici",
                                                   Filter = "Certificat sau PDF semnat (*.cer;*.crt;*.der;*.pem;*.pdf)|*.cer;*.crt;*.der;*.pem;*.pdf|Toate fișierele|*.*" };
                    if (dlg.ShowDialog() != true) return;
                    var bytes = File.ReadAllBytes(dlg.FileName);
                    if (bytes.AsSpan().StartsWith("%PDF-"u8) && data.Pending.Count > 0 && PdfSignatureCheck.Check(bytes, SignedCmsVerifier.Instance) is { Problem: null } check
                        && data.Pending.Any(p => check.OriginalLengthFor(p.RequestSha256) is not null))
                    {
                        Complete(store, data, bytes);
                        return;
                    }
                    cert = OfficerFlows.CertificateFromFile(dlg.FileName);
                }
                if (cert is null) return;
                var identity = OfficerRegistry.Identity(cert);
                if (data.Officers.Count == 0)
                {
                    var row = OfficerRegistry.RegisterFirst(data, identity, DateTimeOffset.UtcNow);
                    store.Save(data);
                    RecordOfficerEvent(OperationAudit.OfficerRegistered, OperationAudit.OfficerRegisteredDetail(row.Fingerprint, row.Name, row.Basis, ""));
                    OfficerInfo = $"Primul ofițer de securitate: {row.Name}{Environment.NewLine}AMPRENTA CERTIFICATULUI: {row.Fingerprint}{Environment.NewLine}" +
                                  "Treceți amprenta pe procesul-verbal și semnați lângă ea. La verificare, semnătura se compară cu amprenta de pe hârtie.";
                    MessageBox.Show($"AMPRENTA CERTIFICATULUI OFIȚERULUI {row.Name}:\n\n{row.Fingerprint}\n\nTreceți-o pe procesul-verbal și semnați lângă ea.", "Primul ofițer de securitate", MessageBoxButton.OK, MessageBoxImage.Information);
                    Status = "Primul ofițer de securitate a fost înregistrat.";
                    return;
                }
                Directory.CreateDirectory(store.RequestsDir);
                var requestPath = Path.Combine(store.RequestsDir, $"Cerere_ofiter_{OfficerFingerprint.Normalize(identity.Fingerprint)[..16]}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.pdf");
                OfficerRequestPdf.Write(requestPath, identity, Environment.MachineName, DateTimeOffset.UtcNow);
                var pending = OfficerRegistry.AddRequest(data, identity, requestPath, DateTimeOffset.UtcNow);
                store.Save(data);
                RecordOfficerEvent(OperationAudit.OfficerRequested, $"cert_fingerprint={pending.Fingerprint}; name={pending.Name}; request_sha256={pending.RequestSha256}");
                OfficerInfo = $"Cererea de înregistrare pentru {identity.Name} (amprenta {identity.Fingerprint}) este în {requestPath}. Un ofițer deja înregistrat trebuie să o semneze cu cardul lui.";
                if (MessageBox.Show("Cererea trebuie semnată de un ofițer de securitate deja înregistrat.\nEste prezent acum, cu cardul în cititor?\nNu = cererea se semnează mai târziu (aici sau în Adobe) și se alege apoi din acest buton.",
                                    "Aprobarea unui ofițer înregistrat", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                { Status = "Cererea de înregistrare a fost generată și așteaptă semnătura unui ofițer înregistrat."; return; }
                var approver = PickCardCertificate("Ofițerul care aprobă");
                if (approver is null) return;
                var signed = OfficerFlows.Sign(File.ReadAllBytes(requestPath), approver, Environment.MachineName, "Aprobarea inregistrarii unui ofiter de securitate", DateTimeOffset.UtcNow);
                File.WriteAllBytes(Path.ChangeExtension(requestPath, null) + "_semnata.pdf", signed);
                Complete(store, data, signed);
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException or FormatException)
            {
                Status = "Ofițerul NU a fost înregistrat: " + ex.Message;
            }
        }

        private void Complete(OfficerStore store, OfficerStoreData data, byte[] signedRequest)
        {
            var row = OfficerRegistry.CompleteRequest(data, signedRequest, SignedCmsVerifier.Instance, DateTimeOffset.UtcNow);
            store.Save(data);
            RecordOfficerEvent(OperationAudit.OfficerRegistered, OperationAudit.OfficerRegisteredDetail(row.Fingerprint, row.Name, row.Basis, row.ApprovedBy));
            OfficerInfo = $"Ofițer de securitate înregistrat: {row.Name}{Environment.NewLine}Amprenta certificatului: {row.Fingerprint}{Environment.NewLine}Aprobat de ofițerul cu amprenta {row.ApprovedBy}.";
            Status = "Ofițerul de securitate a fost înregistrat.";
        }
    }
}
