using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Registers;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// „Registrul de transferuri” (owner 2026-10-11): one form, almost everything filled in. The medium connected now is chosen, with its type, serial and
    /// media-register number; the files' SHA-256 are computed; the number is automatic; source = this station; person = the signed-in operator; the
    /// classification defaults to the medium's. The operator types only the destination and the witness. Append-only; the register prints as PDF.
    /// </summary>
    public partial class TransferViewModel : ObservableObject
    {
        private readonly MediaCenterViewModel _media;
        private readonly LogAnalyzer.Dfir.Reporting.ReportHeaderProfile _header = LogAnalyzer.Dfir.Reporting.ReportHeaderProfile.Load(null, out _);
        private TransferRegister _register = new();

        public TransferViewModel(MediaCenterViewModel media)
        {
            _media = media;
            _media.ConnectedNow.CollectionChanged += (_, _) => RefreshMedia();
            _media.DevicesRefreshed += RefreshUnregistered;
            System.ComponentModel.PropertyChangedEventManager.AddHandler(LogAnalyzer.UI.Services.LanguageLevelState.Instance, OnLevelChanged, nameof(LogAnalyzer.UI.Services.LanguageLevelState.IsSimple));
            Reload();
            RefreshMedia();
        }

        public ObservableCollection<TransferRow> Transfers { get; } = new();
        public ObservableCollection<DeviceAssessment> Media { get; } = new();
        public ObservableCollection<HashedFile> Files { get; } = new();
        public ObservableCollection<string> Problems { get; } = new();
        public IReadOnlyList<string> Levels { get; } = new[] { "nemarcat" }.Concat(SecrecyLevels.All.Select(l => l.Name)).ToList();
        public IReadOnlyList<string> Directions { get; } = ["ieșire", "intrare"];
        public ObservableCollection<string> KnownDestinations { get; } = new();
        public ObservableCollection<string> KnownWitnesses { get; } = new();

        /// <summary>USB/card connections in the last <see cref="ReconcileDays"/> days with no transfer on that medium (same day or +-1 day).</summary>
        public ObservableCollection<UnregisteredConnection> UnregisteredConnections { get; } = new();
        public const int ReconcileDays = 30;
        public const string UsbMediumLabel = "Mediu de stocare USB";

        /// <summary>One line for a non-IT controller: which stick, when, and what is missing.</summary>
        public sealed record UnregisteredConnection(UsbSessionCheck Check)
        {
            public string Text => $"{Check.Device.DisplayName} · S/N {Check.Device.Serial} · conectat {Check.Connected.ToLocalTime():dd.MM.yyyy HH:mm} · {Check.Message}";
        }

        [ObservableProperty] private string _unregisteredSummary = "";
        /// <summary>Date of the transfer being recorded; empty = now. Pre-filled from an unregistered connection.</summary>
        [ObservableProperty] private DateTime? _transferDate;

        public sealed record HashedFile(string Name, string Sha256, long Bytes)
        {
            public string SizeText => DeviceText.Bytes(Bytes);
        }

        [ObservableProperty] private string _direction = "ieșire";
        [ObservableProperty] private DeviceAssessment? _selectedMedium;
        [ObservableProperty] private string _mediumType = "";
        [ObservableProperty] private string _mediumSerial = "";
        [ObservableProperty] private string _mediumRegistration = "";
        [ObservableProperty] private string _classification = "nemarcat";
        [ObservableProperty] private string _source = "";
        [ObservableProperty] private string _sourceStation = Environment.MachineName;
        [ObservableProperty] private string _destination = "";
        [ObservableProperty] private string _destinationStation = "";
        [ObservableProperty] private string _person = LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified;
        [ObservableProperty] private string _personFunction = "";
        [ObservableProperty] private string _witness = "";
        [ObservableProperty] private string _approver = "";
        [ObservableProperty] private string _notes = "";
        /// <summary>How many copies / exits the transfer covers ("" = 1): at a control each covered exit uses one.</summary>
        [ObservableProperty] private string _copies = "";
        [ObservableProperty] private string _status = "Alegeți fișierele; restul este completat. Scrieți destinația și martorul, apoi „Înregistrează transferul”.";
        [ObservableProperty] private string _nextNumber = "";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private DateTime? _reportFrom;
        [ObservableProperty] private DateTime? _reportTo;

        private string Prefix => LogAnalyzer.Dfir.Registers.StationName.Parse(Environment.MachineName).UnitCode is { Length: > 0 } u ? u : "TR";

        partial void OnDirectionChanged(string value)
        {
            // for an outgoing transfer the source is this station; for an incoming one the destination is
            if (value == "intrare") { DestinationStation = Environment.MachineName; if (SourceStation == Environment.MachineName) SourceStation = ""; }
            else { SourceStation = Environment.MachineName; if (DestinationStation == Environment.MachineName) DestinationStation = ""; }
        }

        partial void OnSelectedMediumChanged(DeviceAssessment? value)
        {
            if (value is null) return;
            var d = value.Device;
            MediumType = d.Kind switch { DeviceKind.UsbStorage => UsbMediumLabel, DeviceKind.MemoryCard => "Card de memorie", DeviceKind.Optical => "CD/DVD", DeviceKind.PortableDevice => "Dispozitiv portabil", _ => d.KindText };
            MediumSerial = d.Serial;
            MediumRegistration = value.RegistrationNumber;
            var row = _media.Register.Snapshot().Rows.FirstOrDefault(r => r.RegistrationNumber == value.RegistrationNumber);
            if (row?.Level is { } l) Classification = l.Name;
            _mediumStatus = MediumStatus(value, LogAnalyzer.UI.Services.LanguageLevelState.Instance.IsSimple);
            Status = _mediumStatus;
            OnPropertyChanged(nameof(StatusIsWarning));
        }

        /// <summary>True while <see cref="Status"/> is the warning about the chosen medium (the page colours it).</summary>
        public bool StatusIsWarning => SelectedMedium is { Match: not RegisterMatch.Registered } && Status == _mediumStatus;
        partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusIsWarning));
        private string _mediumStatus = "";
        private void OnLevelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // the medium's status text follows the level; any other status message is left alone
            if (SelectedMedium is null || Status != _mediumStatus) return;
            _mediumStatus = MediumStatus(SelectedMedium, LogAnalyzer.UI.Services.LanguageLevelState.Instance.IsSimple);
            Status = _mediumStatus;
        }

        /// <summary>The line under the form about the chosen medium. Simplu: sentence case ("Atenție: mediul nu este în registru. …"); Expert: as before.</summary>
        internal static string MediumStatus(DeviceAssessment value, bool simple)
        {
            if (value.Match == RegisterMatch.Registered) return $"Mediu din registru: {value.RegistrationNumber}.";
            if (!simple) return $"ATENȚIE: {value.MatchText}. {value.WhatToDo}";
            var what = value.Match switch
            {
                RegisterMatch.NotRegistered => "mediul nu este în registru",
                RegisterMatch.Withdrawn => "mediul este retras / distrus în registru",
                RegisterMatch.Expired => "mediul este în registru, dar valabilitatea a expirat",
                _ => "mediul nu se ține în registru",
            };
            return $"Atenție: {what}. {value.WhatToDo}";
        }

        private void RefreshMedia()
        {
            var keep = SelectedMedium?.Device.InstanceId;
            Media.Clear();
            foreach (var a in _media.ConnectedNow.Where(a => a.Device.Kind is DeviceKind.UsbStorage or DeviceKind.MemoryCard or DeviceKind.Optical or DeviceKind.PortableDevice or DeviceKind.ExternalDisk))
                Media.Add(a);
            SelectedMedium = Media.FirstOrDefault(m => m.Device.InstanceId == keep) ?? Media.FirstOrDefault();
            if (Media.Count == 0) Status = "Niciun mediu de stocare conectat acum (stick USB, card, disc extern sau CD). Conectați mediul sau completați seria de mână.";
        }

        private void Reload()
        {
            _register = TransferStore.Load(null, out var issues);
            Problems.Clear();
            foreach (var i in issues) Problems.Add(i.ToString());
            Transfers.Clear();
            foreach (var t in _register.Rows.OrderByDescending(r => r.DateTime, StringComparer.Ordinal)) Transfers.Add(t);
            KnownDestinations.Clear();
            foreach (var d in _register.Rows.Select(r => r.Destination).Concat(_register.Rows.Select(r => r.Source)).Where(s => s.Length > 0).Distinct().Take(30)) KnownDestinations.Add(d);
            KnownWitnesses.Clear();
            foreach (var w in _register.Rows.Select(r => r.Witness).Where(s => s.Length > 0).Distinct().Take(30)) KnownWitnesses.Add(w);
            Source = Source.Length > 0 ? Source : _header.Unit;
            PersonFunction = PersonFunction.Length > 0 ? PersonFunction : _header.InspectorFunction;
            NextNumber = _register.NextNumber(Prefix, DateTime.Now.Year);
            foreach (var f in TransferChecks.Check(_register, _media.Register.Snapshot())) Problems.Add($"{f.RegistrationNumber}: {f.Message}");
            RefreshUnregistered();
        }

        private void RefreshUnregistered()
        {
            UnregisteredConnections.Clear();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var all = _media.AllDevices;
            var regOf = all.ToDictionary(a => a.Device.InstanceId, a => a.RegistrationNumber, StringComparer.OrdinalIgnoreCase);
            var checks = TransferReconciliation.CheckUsbSessions(all.Select(a => a.Device), _register.Rows, today.AddDays(-ReconcileDays), today,
                d => regOf.TryGetValue(d.InstanceId, out var n) ? n : "");
            foreach (var c in checks.Where(c => c.Status == UsbSessionStatus.ToVerify).OrderByDescending(c => c.Connected)) UnregisteredConnections.Add(new UnregisteredConnection(c));
            UnregisteredSummary = all.Count == 0 ? "Mediile stației nu au fost citite încă (deschideți pagina „Monitor dispozitive”)."
                : UnregisteredConnections.Count == 0 ? $"Toate conectările din ultimele {ReconcileDays} de zile au un transfer înregistrat."
                : $"{UnregisteredConnections.Count} conectări din ultimele {ReconcileDays} de zile nu au un transfer înregistrat.";
        }

        [RelayCommand]
        private void RegisterTransferFor(UnregisteredConnection? item)
        {
            if (item is null) return;
            var d = item.Check.Device;
            SelectedMedium = Media.FirstOrDefault(m => m.Device.InstanceId == d.InstanceId);
            MediumType = d.Kind == DeviceKind.MemoryCard ? "Card de memorie" : UsbMediumLabel;
            MediumSerial = d.Serial;
            MediumRegistration = _media.AllDevices.FirstOrDefault(a => a.Device.InstanceId == d.InstanceId)?.RegistrationNumber ?? "";
            TransferDate = item.Check.Connected.ToLocalTime().DateTime.Date;
            Status = $"Formularul este pregătit pentru mediul {d.DisplayName}, data {TransferDate:dd.MM.yyyy}. Alegeți fișierele, scrieți destinația și martorul.";
        }

        [RelayCommand]
        private async Task ChooseFiles()
        {
            var dlg = new OpenFileDialog { Title = "Fișierele transferate", Multiselect = true, InitialDirectory = SelectedMedium?.Device.DriveLetters.Split(' ').FirstOrDefault() is { Length: 2 } l ? l + "\\" : "" };
            if (dlg.ShowDialog() != true) return;
            await Hash(dlg.FileNames);
        }

        [RelayCommand]
        private async Task WholeMedium()
        {
            var letter = SelectedMedium?.Device.DriveLetters.Split(' ').FirstOrDefault(l => l.Length == 2);
            if (letter is null) { Status = "Mediul ales nu are o literă de unitate (de exemplu un telefon): alegeți fișierele cu „Alege fișierele…”."; return; }
            await Hash([letter + "\\"]);
        }

        private async Task Hash(IReadOnlyList<string> paths)
        {
            IsBusy = true;
            Status = "Se calculează amprentele SHA-256…";
            try
            {
                var list = await Task.Run(() => TransferReportPdf.HashFiles(paths));
                Files.Clear();
                foreach (var (n, h, b) in list) Files.Add(new HashedFile(n, h, b));
                Status = $"{Files.Count} fișiere, {DeviceText.Bytes(Files.Sum(f => f.Bytes))}. Scrieți destinația și martorul, apoi „Înregistrează transferul”.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Un fișier nu a putut fi citit: " + ex.Message; }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void Register()
        {
            if (Files.Count == 0) { Status = "Ca să înregistrați transferul, alegeți întâi fișierele (amprentele lor intră în registru)."; return; }
            var row = new TransferRow
            {
                RegistrationNumber = NextNumber,
                DateTime = TransferDate is { } td ? td.Date.Add(DateTime.Now.TimeOfDay).ToString("yyyy-MM-dd HH:mm") : DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Classification = Classification == "nemarcat" ? "" : Classification,
                Direction = Direction,
                Source = Source, SourceStation = SourceStation, Destination = Destination, DestinationStation = DestinationStation,
                Person = Person, PersonFunction = PersonFunction,
                MediumType = MediumType, MediumSerial = MediumSerial, MediumRegistration = MediumRegistration,
                Content = string.Join("; ", Files.Select(f => f.Name)),
                Sha256 = string.Join(" ", Files.Select(f => f.Sha256)),
                Witness = Witness, Approver = Approver, Notes = Notes, Copies = Copies.Trim(),
            };
            // Control research P2: only the channels approved in the procedure profile (zone to zone, medium). A transfer outside them is stopped
            // once with a plain message; pressing again records it, with the deviation written in the notes for the controller.
            var channel = TransferChannelCheck.Check(row, CurrentProfile());
            var signature = $"{row.Source}|{row.SourceStation}|{row.Destination}|{row.DestinationStation}|{row.MediumType}";
            if (channel.NeedsConfirmation && _confirmedChannelDeviation != signature)
            {
                _confirmedChannelDeviation = signature;
                Status = "Transferul NU a fost încă înregistrat. " + channel.Message +
                         " Dacă transferul are o aprobare specială, apăsați din nou „Înregistrează transferul”: abaterea va fi trecută la observații.";
                return;
            }
            if (channel.NeedsConfirmation)
                row.Notes = (row.Notes.Length > 0 ? row.Notes + " " : "") + "[Canal neaprobat în profilul de proceduri: " + channel.Message + "]";
            _confirmedChannelDeviation = null;
            var issues = TransferStore.Append(row, null, out _);
            var errors = issues.Where(i => i.IsError).ToList();
            if (errors.Count > 0)
            {
                Status = "Transferul NU a fost înregistrat: " + string.Join("; ", errors.Select(e => e.Message)) + ".";
                return;
            }
            TransferDate = null;
            Reload();
            Files.Clear();
            Destination = ""; DestinationStation = Direction == "intrare" ? Environment.MachineName : ""; Notes = "";
            Status = $"Transferul {row.RegistrationNumber} a fost înregistrat. {channel.Message}" + (issues.Count > 0 ? " Atenție: " + string.Join("; ", issues.Select(i => i.Message)) : "");
        }

        /// <summary>The route of a transfer the operator was warned about (outside the approved channels); a second press with the same route records it.</summary>
        private string? _confirmedChannelDeviation;

        /// <summary>The procedure profile in force; null when there is none or it cannot be read (the channel check then says it is not defined).</summary>
        internal Func<LogAnalyzer.Dfir.Profile.ProcedureProfile?> CurrentProfile { get; set; } = () =>
        {
            try { return LogAnalyzer.Dfir.Profile.ProfileProvider.Shared.Current; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
        };

        // ---- verification by content (research 2026-10-12, item 1) ------------------------------------------------------------------

        /// <summary>The transfer chosen in the list; empty = every transfer is compared.</summary>
        [ObservableProperty] private TransferRow? _selectedTransfer;
        [ObservableProperty] private bool _reportUnlisted = true;
        [ObservableProperty] private bool _isVerifying;
        [ObservableProperty] private string _verifySummary = "Verificarea după conținut compară amprentele din registru cu un folder sau cu mediul conectat. Suportul este citit doar pentru citire.";
        [ObservableProperty] private string _verifyProgress = "";
        public ObservableCollection<DeclaredFileResult> VerifyResults { get; } = new();
        public ObservableCollection<UnlistedFile> VerifyUnlisted { get; } = new();
        private TransferVerification? _lastVerification;
        private CancellationTokenSource? _verifyCts;

        [RelayCommand]
        private async Task VerifyFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Folderul sau mediul de verificat (se citește, nu se modifică)" };
            if (dlg.ShowDialog() != true) return;
            await VerifyAt(dlg.FolderName);
        }

        [RelayCommand]
        private async Task VerifyMedium()
        {
            var letter = SelectedMedium?.Device.DriveLetters.Split(' ').FirstOrDefault(l => l.Length == 2);
            if (letter is null) { VerifySummary = "Mediul ales nu are o literă de unitate: alegeți un folder cu „Verifică un folder…”."; return; }
            await VerifyAt(letter + "\\");
        }

        [RelayCommand]
        private void CancelVerify() => _verifyCts?.Cancel();

        private async Task VerifyAt(string root)
        {
            if (IsVerifying) return;
            if (_register.Rows.Count == 0) { VerifySummary = "Registrul nu are niciun transfer de verificat."; return; }
            var scope = SelectedTransfer is { } t ? new List<TransferRow> { t } : _register.Rows.ToList();
            var all = _register.Rows.ToList();
            var opts = new TransferVerifyOptions { ReportUnlisted = ReportUnlisted };
            IsVerifying = true;
            VerifyResults.Clear(); VerifyUnlisted.Clear();
            VerifySummary = "Se citește suportul și se calculează amprentele…";
            using var cts = _verifyCts = new CancellationTokenSource();
            var progress = new Progress<TransferVerifyProgress>(p => VerifyProgress = p.Current.Length == 0 ? "" : $"{p.FilesDone}/{p.FilesTotal}: {p.Current}");
            try
            {
                var v = await Task.Run(() => TransferVerifier.Verify(scope, all, root, opts, progress, cts.Token));
                _lastVerification = v;
                foreach (var d in v.Declared) VerifyResults.Add(d);
                foreach (var u in v.Unlisted) VerifyUnlisted.Add(u);
                var logged = TransferVerifier.WriteAudit(v);
                VerifySummary = v.SummaryText + (logged ? " Verificarea a fost înscrisă în jurnalul de audit." : " NU a fost înscrisă în audit: autentificați-vă în aplicație.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                VerifySummary = "Suportul nu a putut fi citit: " + ex.Message;
            }
            finally { IsVerifying = false; VerifyProgress = ""; _verifyCts = null; }
        }

        [RelayCommand]
        private void Report()
        {
            var dlg = new SaveFileDialog { FileName = $"Registru_transferuri_{DateTime.Now:yyyyMMdd}.pdf", Filter = "PDF (*.pdf)|*.pdf", Title = "Raportul registrului de transferuri" };
            if (dlg.ShowDialog() != true) return;
            var findings = TransferChecks.Check(_register, _media.Register.Snapshot());
            // F2: the declared SHA-256 are re-verified on the medium just before the report; F3: then the PDF is signed when a key is configured.
            var pre = LogAnalyzer.Dfir.Case.PreReportCheck.ForTransfer(_lastVerification, _register.Rows);
            if (LogAnalyzer.UI.Services.ExportAudit.Run(LogAnalyzer.UI.Services.ExportAudit.LiveCaseId, "pdf-transfers", _register.Rows.Count, dlg.FileName,
                    () => TransferReportPdf.Write(dlg.FileName, _register, findings, _header, "", AppModeContext.IsAirGapped, ReportFrom, ReportTo?.Date.AddDays(1).AddTicks(-1), _lastVerification, pre)) is { } refused)
            { Status = refused; return; }
            var signLine = LogAnalyzer.Dfir.Reporting.ReportSigningSettings.Load().SignIfConfigured(dlg.FileName);
            Status = $"Raportul a fost salvat: {dlg.FileName}. {pre.Line} {signLine}";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
    }
}
