using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Devices;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>One line of the live feed: when, what happened, which device, and its verdict against the media register.</summary>
    public sealed record LiveDeviceEntry(DateTimeOffset TimeUtc, string What, string Device, string Serial, string Verdict, bool NeedsAttention)
    {
        public string TimeText => TimeUtc.ToLocalTime().ToString("HH:mm:ss");
    }

    /// <summary>Result line of the quick check of one inserted removable volume, shown on the medium card.</summary>
    public sealed record MediumCheckLine(string Device, string Text, bool Suspicious);

    /// <summary>
    /// "Medii de stocare" (owner 2026-10-11: „ceva viu ca la Endpoint Protector”): what is connected now, every device the station knows with all its
    /// data compared with the media register, a live feed with an alert when an unregistered medium is connected, and the Windows protections.
    /// Read-only towards the station; the live log is written only in the station's case folder.
    /// </summary>
    public partial class MediaCenterViewModel : ObservableObject, IDisposable
    {
        private readonly MediaRegisterViewModel _register;
        private DeviceWatcher? _watcher;
        private List<DeviceAssessment> _all = [];
        private CancellationTokenSource? _debounce;

        public MediaCenterViewModel(MediaRegisterViewModel register)
        {
            _register = register;
            // G3: a new analysis result refreshes the medium detail (history, what followed the connection) without collecting again.
            LogAnalyzer.UI.Services.CollectAllState.Instance.Published += _ => OnSelectedDeviceChanged(SelectedDevice);
        }

        /// <summary>Set by the main window: opens the separate "Registrul de transferuri" page (owner 2026-10-11: the transfer register is not part of this page).</summary>
        public Action? NavigateToTransferRegister { get; set; }
        [RelayCommand] private void OpenTransferRegister() => NavigateToTransferRegister?.Invoke();
        [RelayCommand] private void RecordTransfer() { AlertText = ""; NavigateToTransferRegister?.Invoke(); }

        public MediaRegisterViewModel Register => _register;

        /// <summary>
        /// Owner decision 2026-10-16: the media register tabs ("Registrul de medii", "Registrul semnat") and the register columns are shown only while
        /// <see cref="MediaRegisterFeature.Enabled"/> is on (off by default); the code stays, switched off.
        /// </summary>
        public bool SignedRegisterVisible => MediaRegisterFeature.Enabled;

        private DeviceMonitorViewModel? _monitor;
        /// <summary>"Monitor în timp real": every device of every class, live, with status circle, driver state and registration number (owner decision 2026-10-16).</summary>
        public DeviceMonitorViewModel Monitor
        {
            get
            {
                if (_monitor is null)
                {
                    _monitor = DeviceMonitorViewModel.CreateForStation();
                    _monitor.Alert += OnMonitorAlert;
                }
                return _monitor;
            }
        }

        /// <summary>The sub-tab shown on the page; 0 = the real-time monitor.</summary>
        [ObservableProperty] private int _selectedTab;
        /// <summary>The banner is a critical alert (red, with a sound) rather than information about a connection.</summary>
        [ObservableProperty] private bool _alertIsCritical = true;

        /// <summary>
        /// A device was connected (seen by the monitor): an information banner, or a critical one for a storage medium without a registration number or a class
        /// the station policy forbids. Information never covers a critical alert that has not been acknowledged.
        /// </summary>
        private void OnMonitorAlert(DeviceAlertRecord a)
        {
            bool critical = a.Severity == DeviceAlertSeverity.Critical;
            if (!critical && HasAlert && AlertIsCritical) return;
            var when = a.TimeUtc.ToLocalTime().ToString("HH:mm:ss");
            var cls = DeviceMonitorRules.ClassText(a.Class);
            AlertIsCritical = critical;
            AlertText = critical
                ? $"ATENȚIE {when}: s-a conectat „{a.Device}” ({cls}) — {a.Text}. " +
                  (DeviceMonitorRules.IsStorageMedium(a.Class) ? "Aflați al cui este și treceți numărul de înregistrare în monitorul de dispozitive." : "Aflați cine l-a conectat și de ce; pe o stație izolată nu este permis.")
                : $"{when}: s-a conectat „{a.Device}” ({cls}).";
            if (critical) System.Media.SystemSounds.Exclamation.Play();
            Log(new LiveDeviceEntry(a.TimeUtc, critical ? "alertă critică (monitor)" : "conectare (monitor)", a.Device, "", a.Text, critical));
        }

        private SignedMediaRegisterViewModel? _signed;
        /// <summary>"Registrul semnat": enrolment on the transfer station, officer signature, import on every station (created when its tab is first shown).</summary>
        public SignedMediaRegisterViewModel Signed => _signed ??= new SignedMediaRegisterViewModel();

        private CrossStationViewModel? _cross;
        /// <summary>"Confruntare între stații": the officer reconciles the signed media-history exports of every station (created when its tab is first shown).</summary>
        public CrossStationViewModel Cross => _cross ??= new CrossStationViewModel();

        /// <summary>Every device the station knows (also those not connected now), with its media-register match; used to reconcile USB sessions with the transfer register.</summary>
        public IReadOnlyList<DeviceAssessment> AllDevices => _all;
        /// <summary>Raised after the device inventory was (re)read.</summary>
        public event Action? DevicesRefreshed;
        public ObservableCollection<DeviceAssessment> Devices { get; } = new();
        public ObservableCollection<DeviceAssessment> ConnectedNow { get; } = new();
        public ObservableCollection<LiveDeviceEntry> LiveFeed { get; } = new();
        public ObservableCollection<DeviceProtection> Protections { get; } = new();
        public ObservableCollection<DeviceDetail> SelectedDetails { get; } = new();

        public string[] Filters { get; } = MediaRegisterFeature.Enabled
            ? new[] { "Toate", "Conectate acum", "Cer atenție (nu sunt în registru)", "Stocare USB și carduri", "Discuri interne", "Telefoane și Bluetooth", "Rețea și discuri virtuale" }
            : new[] { "Toate", "Conectate acum", "Stocare USB și carduri", "Discuri interne", "Telefoane și Bluetooth", "Rețea și discuri virtuale" };
        [ObservableProperty] private string _filter = "Toate";
        partial void OnFilterChanged(string value) => ApplyFilter();

        [ObservableProperty] private string _summary = "Apăsați „Reîmprospătează” sau așteptați: lista se încarcă la deschiderea paginii.";
        [ObservableProperty] private string _problems = "";
        [ObservableProperty] private string _liveStatus = "Supravegherea în timp real nu este pornită.";
        [ObservableProperty] private bool _isLive;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _alertText = "";
        [ObservableProperty] private DeviceAssessment? _selectedDevice;
        public bool HasAlert => AlertText.Length > 0;
        partial void OnAlertTextChanged(string value) => OnPropertyChanged(nameof(HasAlert));

        partial void OnSelectedDeviceChanged(DeviceAssessment? value)
        {
            SelectedDetails.Clear();
            if (value is null) return;
            // without the register (MediaRegisterFeature off) the register comparison and its advice are not shown
            foreach (var d in DeviceDetails.For(value).Where(d => MediaRegisterFeature.Enabled || d.Label is not ("Față de registrul de medii" or "Ce faceți"))) SelectedDetails.Add(d);
            foreach (var d in AnalysisDetails(value.Device.Serial, LogAnalyzer.UI.Services.CollectAllState.Instance.Current?.Timeline)) SelectedDetails.Add(d);
        }

        /// <summary>
        /// E1 / E5 on the medium detail, from the result of the last analysis (G3: the same result every page uses): the connection history from every source
        /// with the earliest evidence, and „Ce a urmat după conectare”. Without an analysis it says so and the page offers „Colectează tot”.
        /// </summary>
        public static IReadOnlyList<DeviceDetail> AnalysisDetails(string serial, IReadOnlyList<LogAnalyzer.Dfir.Model.TimelineEvent>? timeline)
        {
            if (serial.Length == 0) return [];
            if (timeline is null)
                return [new DeviceDetail("Istoric și ce a urmat după conectare", LogAnalyzer.UI.Services.CollectAllState.EmptyText,
                    "Apăsați „Colectează tot”: jurnalele (inclusiv cele arhivate și Windows.old), Prefetch, scurtăturile și istoricul browserului sunt analizate o singură dată și folosite aici.")];
            var history = LogAnalyzer.Dfir.Analysis.Wp14Rules.History(timeline, serial);
            var after = LogAnalyzer.Dfir.Analysis.AfterConnect.DetailLines(timeline, serial);
            return
            [
                new DeviceDetail("Istoricul conectărilor (toate sursele)",
                    history.Count == 0 ? "Nicio urmă datată în cronologia analizată." : $"Cea mai veche dovadă: {history[0].Text}" + Environment.NewLine + string.Join(Environment.NewLine, history.Take(30).Select(h => "• " + h.Text)),
                    "Cea mai veche urmă nu este neapărat prima conectare: sursele mai vechi (jurnale suprascrise) pot lipsi."),
                new DeviceDetail("Ce a urmat după conectare (−10 min … +2 h)", string.Join(Environment.NewLine, after),
                    "Execuții (Prefetch, 4688, Sysmon), fișiere deschise (LNK, Jump Lists) și descărcări, cu scor; activitatea altor utilizatori este exclusă când utilizatorul sesiunii este cunoscut. Apropierea în timp nu dovedește legătura."),
            ];
        }

        private bool _activated;

        /// <summary>Called once the main window is shown: first inventory and live supervision (the page stays „alive” in the background).</summary>
        public void Activate()
        {
            if (_activated) return;
            _activated = true;
            _ = RefreshThenInspectAsync();
            StartLive();
            _ = Monitor.StartAsync(_watcher?.PnpEventsActive == true);
        }

        // ---- inspection of an inserted stick (research 2026-10-12, item 4): read-only, root + one level --------------------------------

        public ObservableCollection<MediumCheckLine> MediumChecks { get; } = new();
        private readonly HashSet<string> _inspected = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Checks every removable volume connected now that has not been checked yet (<paramref name="force"/> = all); raises the live alert on anything suspicious.</summary>
        public async Task InspectConnectedAsync(bool force = false)
        {
            foreach (var a in ConnectedNow.Where(a => a.Device.Kind is DeviceKind.UsbStorage or DeviceKind.MemoryCard).ToList())
            {
                var letter = a.Device.DriveLetters.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(l => l.Length == 2 && l[1] == ':');
                if (letter is null) continue;
                var key = a.Device.InstanceId + "|" + letter;
                if (!force && !_inspected.Add(key)) continue;
                _inspected.Add(key);
                var label = a.Device.VolumeLabel;
                // E6: the listing (no hashing, so no file is opened for the arrival analysis) is taken BEFORE Inspect reads any shortcut, so last-access times
                // are not our own; the alert is published first and the arrival summary follows
                var (result, listing, fs) = await Task.Run(() =>
                {
                    var l = RemovableMediaInspector.List(letter + "\\", label, maxHashed: 0);
                    var r = RemovableMediaInspector.Inspect(letter + "\\", label);
                    string f; try { f = new System.IO.DriveInfo(letter).DriveFormat; } catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { f = ""; }
                    return (r, l, f);
                });
                void Publish(string text)
                {
                    var line = new MediumCheckLine(a.Device.DisplayName, text, result.IsSuspicious);
                    var old = MediumChecks.FirstOrDefault(m => m.Device == line.Device);
                    if (old is not null) MediumChecks.Remove(old);
                    MediumChecks.Add(line);
                }
                Publish(result.Text + " Se pregătește rezumatul sosirii fișierelor…");
                Log(new LiveDeviceEntry(DateTimeOffset.UtcNow, "verificare suport", a.Device.DisplayName, a.Device.Serial, result.Text, result.IsSuspicious));
                if (result.IsSuspicious)
                {
                    AlertIsCritical = true;
                    AlertText = $"ATENȚIE {DateTime.Now:HH:mm:ss}: pe „{a.Device.DisplayName}” ({letter}) s-au găsit semne suspecte: {string.Join("; ", result.Findings.Take(5).Select(f => f.Message))}. Nu deschideți nimic de pe suport; anunțați responsabilul de securitate.";
                    System.Media.SystemSounds.Exclamation.Play();
                }
                var arrival = await Task.Run(() =>
                {
                    // when the file times were copied onto the medium; shown as an observation, never as a suspicion
                    var report = MediaArrivalAnalysis.Analyze(listing, fs);
                    var usn = MediaArrivalReport.UsnNote(fs, letter[..1]);
                    return report.Text + (usn.Length > 0 ? " " + usn : "");
                });
                Publish(result.Text + " " + arrival);
            }
        }

        [RelayCommand] private Task InspectMedia() => InspectConnectedAsync(force: true);

        private async Task RefreshThenInspectAsync() { await RefreshAsync(); await InspectConnectedAsync(); }

        private Task? _running;

        /// <summary>One inventory at a time; a caller that arrives while one is running waits for it instead of returning with stale data (senior review L4).</summary>
        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (_running is { IsCompleted: false } r) { await r; return; }
            _running = RefreshCoreAsync();
            await _running;
        }

        private async Task RefreshCoreAsync()
        {
            IsBusy = true;
            Summary = "Se citesc dispozitivele stației…";
            try
            {
                var result = await Task.Run(DeviceInventory.Collect);
                var register = _register.Snapshot();
                var today = DateOnly.FromDateTime(DateTime.Now);
                _all = result.Devices.Select(d => DeviceRules.Assess(d, register, today, AppModeContext.IsAirGapped)).ToList();
                Problems = result.Problems.Count == 0 ? "" : "Nu s-au putut citi: " + string.Join(" ", result.Problems);
                var protections = await Task.Run(() => DevicePolicyCheck.Read(audit: () =>
                    LogAnalyzer.Dfir.Windows.Native.AuditPolicy.Query()?.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()) as IReadOnlyDictionary<string, string>));
                Protections.Clear();
                foreach (var p in protections) Protections.Add(p);
                ApplyFilter();
                var attention = _all.Count(a => a.NeedsAttention);
                Summary = $"Conectate acum: {_all.Count(a => a.Device.ConnectedNow && a.Device.Kind != DeviceKind.Bluetooth)} · cunoscute de stație: {_all.Count}" +
                          (!MediaRegisterFeature.Enabled ? "." : attention == 0 ? " · toate mediile urmărite sunt în registru." : $" · {attention} cer atenție (nu sunt în registru, sunt retrase sau au expirat).");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Summary = "Dispozitivele nu au putut fi citite: " + ex.Message + " Încercați să porniți aplicația ca administrator.";
            }
            finally { IsBusy = false; }
        }

        private void ApplyFilter()
        {
            var keep = SelectedDevice?.Device.InstanceId;
            Devices.Clear();
            foreach (var a in _all.Where(Matches)) Devices.Add(a);
            ConnectedNow.Clear();
            foreach (var a in _all.Where(a => a.Device.ConnectedNow && a.Device.Kind != DeviceKind.Bluetooth)) ConnectedNow.Add(a);
            SelectedDevice = Devices.FirstOrDefault(a => a.Device.InstanceId == keep) ?? Devices.FirstOrDefault(a => a.NeedsAttention) ?? Devices.FirstOrDefault();
            DevicesRefreshed?.Invoke();
        }

        private bool Matches(DeviceAssessment a) => Filter switch
        {
            "Conectate acum" => a.Device.ConnectedNow,
            "Cer atenție (nu sunt în registru)" => a.NeedsAttention,
            "Stocare USB și carduri" => a.Device.Kind is DeviceKind.UsbStorage or DeviceKind.MemoryCard,
            "Discuri interne" => a.Device.Kind == DeviceKind.InternalDisk,
            "Telefoane și Bluetooth" => a.Device.Kind is DeviceKind.PortableDevice or DeviceKind.Bluetooth,
            "Rețea și discuri virtuale" => a.Device.Kind is DeviceKind.NetworkDrive or DeviceKind.VirtualDisk,
            _ => true,
        };

        // ---- live --------------------------------------------------------------------------------------------------------------------

        [RelayCommand]
        private void ToggleLive()
        {
            if (IsLive) StopLive(); else StartLive();
        }

        private void StartLive()
        {
            if (_watcher is not null) return;
            _watcher = new DeviceWatcher();
            _watcher.Changed += c => Application.Current?.Dispatcher.BeginInvoke(() => OnChange(c));
            _watcher.Failed += m => Application.Current?.Dispatcher.BeginInvoke(() => LiveStatus = m);
            // the device monitor reads the device list again on every Plug and Play event, whatever the class of the device
            _watcher.PnpChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => _monitor?.OnPnpEvent());
            _watcher.Start();
            _monitor?.SetEventsAvailable(_watcher.PnpEventsActive);
            IsLive = true;
            LiveStatus = "Supraveghere în timp real PORNITĂ: orice dispozitiv conectat sau scos apare mai jos în câteva secunde.";
        }

        private void StopLive()
        {
            _watcher?.Dispose();
            _watcher = null;
            _monitor?.SetEventsAvailable(false);   // without the events the monitor reads the device list every 2 seconds
            IsLive = false;
            LiveStatus = "Supravegherea în timp real este oprită. Apăsați „Pornește supravegherea” ca să vedeți conectările pe loc.";
        }

        private readonly List<DeviceChange> _pending = new();

        private async void OnChange(DeviceChange c)
        {
            // several WMI events arrive for one physical device: the INVENTORY is debounced, never the events — every change of the burst is
            // assessed after one fresh inventory (senior review L5: of two sticks inserted together only the last used to be checked)
            _pending.Add(c);
            _debounce?.Cancel();
            var cts = _debounce = new CancellationTokenSource();
            try { await Task.Delay(1500, cts.Token); } catch (TaskCanceledException) { return; }   // a later change of the same burst handles the batch
            var batch = _pending.ToList();
            _pending.Clear();
            if (_running is { IsCompleted: false } r) await r;                                    // an inventory already running predates these changes
            await RefreshAsync();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in batch)
            {
                var match = _all.FirstOrDefault(a => p.InstanceId.Length > 0 && a.Device.InstanceId.Equals(p.InstanceId, StringComparison.OrdinalIgnoreCase))
                            ?? (p.Arrived && p.InstanceId.Length > 0
                                ? _all.Where(a => a.Device.ConnectedNow && !used.Contains(a.Device.InstanceId)).OrderByDescending(a => a.Device.LastConnectedUtc).FirstOrDefault()
                                : null);
                if (match is not null) used.Add(match.Device.InstanceId);
                AddFeed(p, match);
            }
            if (batch.Any(p => p.Arrived)) await InspectConnectedAsync();
        }

        private void AddFeed(DeviceChange c, DeviceAssessment? a)
        {
            var kind = a?.Device.KindText ?? (c.Kind is { } k ? DeviceText.Kind(k) : "Unitate");
            var what = c.Arrived ? $"{kind}: conectat" : $"{kind}: scos";
            if (c.InstanceId.Length == 0) what = c.Detail;
            var entry = new LiveDeviceEntry(c.TimeUtc, what, a?.Device.Name is { Length: > 0 } n ? n : c.Name, a?.Device.Serial ?? "",
                                            a is null || !MediaRegisterFeature.Enabled ? "" : a.MatchText + (a.NeedsAttention ? " — " + a.WhatToDo : ""),
                                            MediaRegisterFeature.Enabled && c.Arrived && a?.NeedsAttention == true);
            LiveFeed.Insert(0, entry);
            while (LiveFeed.Count > 500) LiveFeed.RemoveAt(LiveFeed.Count - 1);
            if (entry.NeedsAttention)
            {
                AlertIsCritical = true;
                AlertText = $"ATENȚIE {entry.TimeText}: s-a conectat „{entry.Device}” (serie {(entry.Serial.Length > 0 ? entry.Serial : "necunoscută")}), care {a!.MatchText}. {a.WhatToDo}";
                System.Media.SystemSounds.Exclamation.Play();
            }
            Log(entry);
        }

        /// <summary>"Am văzut": recorded with who and when in the station's live log, so there is proof the alert was handled (senior review L3).</summary>
        [RelayCommand]
        private void DismissAlert()
        {
            if (AlertText.Length == 0) return;
            Log(new LiveDeviceEntry(DateTimeOffset.UtcNow, "alertă văzută de " + LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified, AlertText, "", "confirmată", false));
            AlertText = "";
        }

        private static void Log(LiveDeviceEntry e)
        {
            try
            {
                var dir = LogAnalyzer.UI.Services.LiveCase.Root;
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "media_live.jsonl"), JsonSerializer.Serialize(e) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        [RelayCommand]
        private void CopyDetails()
        {
            if (SelectedDevice is null) return;
            var text = string.Join(Environment.NewLine, SelectedDetails.Select(d => $"{d.Label}: {d.Value}" + (d.Meaning.Length > 0 ? $" — {d.Meaning}" : "")));
            try
            {
                if (LogAnalyzer.UI.Services.ExportAudit.Clipboard(LogAnalyzer.UI.Services.ExportAudit.LiveCaseId, "device_details", text) is { } refused) { Summary = refused; return; }
                Summary = "Datele dispozitivului au fost copiate; le puteți lipi în procesul-verbal.";
            }
            catch (System.Runtime.InteropServices.COMException) { Summary = "Clipboard-ul este ocupat de alt program; încercați din nou."; }
        }

        [RelayCommand]
        private async Task AddToRegister()
        {
            if (!_register.CanEdit) { Summary = _register.AccessNotice; return; }
            await _register.DiscoverCommand.ExecuteAsync(null);
            Summary = _register.Status + " Deschideți fila „Registrul de medii” ca să completați nivelul, zona și utilizatorul, apoi salvați.";
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _monitor?.Dispose();
        }
    }
}
