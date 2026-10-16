using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>One row of the draft register: the medium's data (read from the station) and what the operator fills in. Editable until signed.</summary>
    public sealed partial class MediaEnrollmentRow : ObservableObject
    {
        public MediaEnrollmentRow(SignedMediaEntry e, bool isNew) { Entry = e; IsNew = isNew; _inventoryNumber = e.InventoryNumber; _classification = e.Classification; _holder = e.Holder; _status = e.Status; }
        public SignedMediaEntry Entry { get; }
        public bool IsNew { get; }
        [ObservableProperty] private string _inventoryNumber;
        [ObservableProperty] private string _classification;
        [ObservableProperty] private string _holder;
        [ObservableProperty] private string _status;
        public string Identity => $"{Entry.Vendor} {Entry.Model}".Trim() + $" · SN {Entry.UsbSerial} · {Entry.VidPid} · {(Entry.CapacityBytes > 0 ? DeviceText.Bytes(Entry.CapacityBytes) : "capacitate necunoscută")}";
        public string Encryption => Entry.BitLockerStatus switch { "criptat" => "criptat (BitLocker To Go)", "necriptat" => "NECRIPTAT — se recomandă BitLocker To Go", _ => "necunoscut" };
        public bool NotEncrypted => Entry.BitLockerStatus != "criptat";
        public string Origin => IsNew ? "nou (înrolat acum)" : "din registrul în vigoare";

        /// <summary>The entry as it will be signed (status change dated now when it changes).</summary>
        public SignedMediaEntry ToEntry(DateTimeOffset nowUtc)
        {
            var e = Entry.Copy();
            e.InventoryNumber = InventoryNumber.Trim(); e.Classification = Classification.Trim(); e.Holder = Holder.Trim();
            var st = (Status ?? "").Trim().ToLowerInvariant();
            if (st != e.Status) { e.StatusChangedUtc = st == SignedMediaRegister.StatusActive ? "" : SignedMediaRegister.FormatUtc(nowUtc); e.Status = st; }
            return e;
        }
    }

    /// <summary>
    /// "Registrul semnat" (owner decision 2026-10-16): on the transfer station, one click enrols every removable medium connected now; the operator fills in the
    /// inventory number, classification and holder (HG 585/2002 art. 294); the security officer signs the new version with the card and it is exported for
    /// transport. On every station the signed file is imported: OFICIAL only with the fingerprint from the proces-verbal; older versions are refused.
    /// </summary>
    public partial class SignedMediaRegisterViewModel : ObservableObject
    {
        private readonly SignedMediaRegisterStore _store;
        private readonly Func<IReadOnlyList<DeviceRecord>> _devices;

        public SignedMediaRegisterViewModel() : this(SignedMediaRegisterStore.Shared, () => LogAnalyzer.Dfir.Windows.Devices.DeviceInventory.Collect().Devices) { }

        public SignedMediaRegisterViewModel(SignedMediaRegisterStore store, Func<IReadOnlyList<DeviceRecord>> devices)
        {
            _store = store; _devices = devices;
            RefreshActive();
        }

        public ObservableCollection<MediaEnrollmentRow> Draft { get; } = new();
        public ObservableCollection<MediaRegisterHistoryRow> History { get; } = new();
        public string[] StatusChoices { get; } = SignedMediaRegister.Statuses;

        [ObservableProperty] private string _activeText = "";
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private string _warnings = "";
        [ObservableProperty] private string _paperFingerprint = "";
        [ObservableProperty] private string _result = "";

        private ActiveMediaRegister _active = ActiveMediaRegister.None("");

        public void RefreshActive()
        {
            string? audit = null;
            try { audit = AuthApp.Service.AuditPath; } catch (InvalidOperationException) { }
            _active = MediaRegisterFlows.Active(_store, audit);
            ActiveText = _active.Usable
                ? $"Registrul semnat în vigoare: versiunea {_active.Doc!.Version}, emis de „{_active.Doc.StationId}” la {_active.Doc.IssuedUtc}; {_active.Doc.Active.Count()} medii active din {_active.Doc.Entries.Count}. SHA-256 {_active.Sha256}."
                : "Nu există un registru semnat în vigoare: " + _active.Problem + ".";
            History.Clear();
            foreach (var h in _store.LoadState().History.AsEnumerable().Reverse()) History.Add(h);
        }

        /// <summary>The draft starts from the register in force, so a new version keeps every medium; then the media connected now that it lacks are added.</summary>
        [RelayCommand]
        private void EnrollConnected()
        {
            RefreshActive();
            if (Draft.Count == 0 && _active.Usable) foreach (var e in _active.Doc!.Entries) Draft.Add(new MediaEnrollmentRow(e.Copy(), false));
            IReadOnlyList<DeviceRecord> devices;
            try { devices = _devices(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Status = "Mediile conectate nu au putut fi citite: " + ex.Message; return; }
            var connected = SignedMediaEnrollment.Connected(devices);
            int added = 0, noSerial = 0, known = 0;
            foreach (var d in connected)
            {
                var e = SignedMediaEnrollment.Entry(d, DateTimeOffset.UtcNow);
                if (e.UsbSerial.Length == 0) { noSerial++; continue; }
                if (Draft.Any(r => MediaRegister.NormalizeSerial(r.Entry.UsbSerial) == e.UsbSerial && r.Status == SignedMediaRegister.StatusActive)) { known++; continue; }
                Draft.Add(new MediaEnrollmentRow(e, true));
                added++;
            }
            UpdateWarnings();
            Status = connected.Count == 0 ? "Nu este conectat niciun mediu amovibil. Introduceți toate mediile de înrolat și apăsați din nou."
                : $"Medii conectate: {connected.Count}; înrolate acum: {added}; deja în registru: {known}" + (noSerial > 0 ? $"; fără serie unică (nu pot fi înrolate): {noSerial}" : "") +
                  ". Completați numărul de inventar, nivelul de clasificare și deținătorul, apoi „Semnează și exportă”.";
        }

        private void UpdateWarnings()
        {
            var w = Draft.Where(r => r.Status == SignedMediaRegister.StatusActive && r.NotEncrypted).Select(r => $"{(r.InventoryNumber.Length > 0 ? r.InventoryNumber : r.Entry.UsbSerial)}: {r.Encryption}").ToList();
            Warnings = w.Count == 0 ? "" : "Atenție, medii necriptate sau cu stare necunoscută (se recomandă BitLocker To Go înainte de folosire): " + string.Join("; ", w);
        }

        [RelayCommand]
        private void ClearDraft() { Draft.Clear(); Warnings = ""; Status = "Ciorna a fost golită; registrul în vigoare nu s-a schimbat."; }

        /// <summary>Builds the next version from the draft, has the officer sign it with the card, records it and exports it to a chosen folder.</summary>
        [RelayCommand]
        private void SignAndExport()
        {
            if (Draft.Count == 0) { Status = "Ciorna este goală: apăsați întâi „Înrolează mediile conectate”."; return; }
            var now = DateTimeOffset.UtcNow;
            var doc = SignedMediaRegister.NextVersion(_active.Usable ? _active.Doc : null, _active.Usable ? _active.Sha256 : null, Draft.Select(r => r.ToEntry(now)), Environment.MachineName, now);
            var problems = SignedMediaRegister.Problems(doc);
            if (problems.Count > 0) { Status = "Registrul nu poate fi semnat: " + string.Join("; ", problems) + "."; return; }
            List<X509Certificate2> certs;
            try { certs = OfficerFlows.CardSigningCertificates(AuthApp.Cards, now); }
            catch (CardException ex) { Status = "Cardul nu poate fi citit: " + ex.Message; return; }
            if (certs.Count == 0) { Status = "Pe cardul introdus nu s-a găsit un certificat de semnătură calificată (cu utilizarea „nerepudiere”)."; return; }
            var cert = LogAnalyzer.UI.Views.CertificatePickerWindow.Pick("Semnează registrul de medii", certs);
            if (cert is null) return;
            // usability 2026-10-16: the dialog starts in the folder used last time
            var folder = StationControlViewModel.PickFolder("Alegeți unde se exportă registrul semnat (pentru transport la stații)", LogAnalyzer.Dfir.Language.FolderMemory.MediaRegisterExport);
            if (folder is null) return;
            try
            {
                var signed = MediaRegisterFlows.Sign(doc, cert, OfficerFlows.Trusted(OfficerStore.Shared.Load()));
                var path = MediaRegisterFlows.Export(folder, signed);
                AuthApp.Record(OperationAudit.MediaRegisterSigned, OperationAudit.MediaRegisterDetail(doc.Version, signed.Sha256, "SEMNAT", false, OfficerFingerprint.Of(cert), $"{doc.Entries.Count} medii, exportat în {folder}"));
                Result = $"Registrul versiunea {doc.Version} a fost semnat și exportat: {path} (și semnătura .p7s).{Environment.NewLine}SHA-256: {signed.Sha256}{Environment.NewLine}" +
                         "Importați-l pe fiecare stație (și pe aceasta) cu „Importă registrul semnat”, scriind amprenta ofițerului de pe procesul-verbal.";
                Status = "Registrul a fost semnat cu certificatul calificat de pe card.";
                Draft.Clear();
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                Status = "Registrul NU a fost semnat: " + ex.Message;
            }
        }

        [RelayCommand]
        private void ImportSigned()
        {
            if (PaperFingerprint.Trim().Length > 0 && OfficerFingerprint.Normalize(PaperFingerprint).Length == 0)
            { Status = "Amprenta scrisă nu are forma corectă: 64 de caractere 0-9 și A-F, exact ca pe procesul-verbal."; return; }
            var dlg = new OpenFileDialog { Title = "Alegeți registrul de medii semnat (registru_medii_vNNNN.json, cu semnătura .p7s alături)", Filter = "Registru de medii (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var check = MediaRegisterFlows.Import(dlg.FileName, PaperFingerprint, _store, OfficerFlows.Trusted(OfficerStore.Shared.Load()), QualifiedSignatureTrust.FromDirectory(),
                                                      DateTimeOffset.UtcNow, AuthApp.Record);
                Result = check.ToPlainText();
                Status = check.Installable ? $"Registrul versiunea {check.Doc!.Version} este acum în vigoare pe această stație." : "Registrul NU a fost instalat: " + check.Headline + ".";
                RefreshActive();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AuthApp.Record(OperationAudit.MediaRegisterRefused, OperationAudit.MediaRegisterDetail(0, "", "INVALID", false, "", ex.Message));
                Status = "Registrul nu poate fi importat: " + ex.Message;
            }
        }
    }
}
