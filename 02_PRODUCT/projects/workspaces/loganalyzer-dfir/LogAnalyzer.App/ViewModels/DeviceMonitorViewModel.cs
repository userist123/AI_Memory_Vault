using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// One row of the real-time device monitor: the device as the journal knows it, a status circle (green while connected, red after) and the editable
    /// „Nr. de înregistrare”. Editing the number goes through <see cref="DeviceMonitorViewModel.SetRegistrationNumber"/> (sealed audit first, then the journal).
    /// There are no driver columns: „Nu funcționează (fără driver)” is only a status word.
    /// </summary>
    public sealed partial class DeviceMonitorRow : ObservableObject
    {
        private readonly DeviceMonitorViewModel _owner;
        private string _registrationNumber;

        public DeviceMonitorRow(DeviceJournalEntry e, DeviceMonitorViewModel owner)
        {
            _owner = owner;
            Identity = e.Identity;
            _registrationNumber = e.RegistrationNumber;
            Update(e);
        }

        public string Identity { get; }
        [ObservableProperty] private bool _present;
        [ObservableProperty] private DeviceClass _class;
        [ObservableProperty] private DeviceStatus _status;
        [ObservableProperty] private string _statusTip = "";
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _manufacturer = "";
        [ObservableProperty] private string _model = "";
        [ObservableProperty] private string _vidPid = "";
        [ObservableProperty] private string _serial = "";
        [ObservableProperty] private string _capacityText = "";
        [ObservableProperty] private string _letter = "";
        [ObservableProperty] private string _label = "";
        [ObservableProperty] private string _connectedUser = "";
        [ObservableProperty] private string _instanceId = "";
        [ObservableProperty] private string _firstSeenText = "";
        [ObservableProperty] private string _lastConnectedText = "";
        [ObservableProperty] private string _lastDisconnectedText = "";
        [ObservableProperty] private string _alertText = "";
        [ObservableProperty] private bool _alertIsCritical;

        public string ClassText => DeviceMonitorRules.ClassText(Class);
        public string StatusText => DeviceMonitorRules.StatusText(Status);
        public bool IsStorageMedium => DeviceMonitorRules.IsStorageMedium(Class);
        /// <summary>A storage medium without a registration number: the row is shown in red until the number is written.</summary>
        public bool NeedsRegistration => IsStorageMedium && RegistrationNumber.Trim().Length == 0;

        /// <summary>„Nr. de înregistrare”: editable for every device. A change the sealed audit refuses is undone and the reason shown on the page.</summary>
        public string RegistrationNumber
        {
            get => _registrationNumber;
            set
            {
                var v = (value ?? "").Trim();
                if (v == _registrationNumber) return;
                if (_owner.SetRegistrationNumber(this, _registrationNumber, v)) _registrationNumber = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NeedsRegistration));
                _owner.Refresh(this);
            }
        }

        partial void OnStatusChanged(DeviceStatus value) => OnPropertyChanged(nameof(StatusText));
        partial void OnClassChanged(DeviceClass value) { OnPropertyChanged(nameof(ClassText)); OnPropertyChanged(nameof(IsStorageMedium)); OnPropertyChanged(nameof(NeedsRegistration)); }

        public void Update(DeviceJournalEntry e, string alertText = "", bool alertCritical = false)
        {
            Present = e.Present; Class = e.Class; Status = e.Status;
            StatusTip = StatusText + (e.Present && e.ConfigManagerErrorCode > 0 ? $" (cod {e.ConfigManagerErrorCode}: {DeviceMonitorRules.ProblemText(e.ConfigManagerErrorCode)})" : "");
            Name = e.Name.Length > 0 ? e.Name : "(fără nume)"; Manufacturer = e.Manufacturer; Model = e.Model; VidPid = e.VidPid;
            Serial = e.Serial.Length > 0 ? e.Serial : e.NoUniqueSerial ? DeviceMonitorRules.NoUniqueSerialText : "";
            CapacityText = e.CapacityBytes > 0 ? DeviceText.Bytes(e.CapacityBytes) : "";
            Letter = e.VolumeLetters; Label = e.VolumeLabel;
            ConnectedUser = e.ConnectedUser.Length > 0 ? e.ConnectedUser : "—";
            InstanceId = e.InstanceId;
            FirstSeenText = DeviceMonitorViewModel.When(e.FirstSeenUtc);
            LastConnectedText = DeviceMonitorViewModel.When(e.LastConnectedUtc);
            LastDisconnectedText = DeviceMonitorViewModel.When(e.LastDisconnectedUtc);
            AlertText = alertText; AlertIsCritical = alertCritical;
            if (e.RegistrationNumber != _registrationNumber) { _registrationNumber = e.RegistrationNumber; OnPropertyChanged(nameof(RegistrationNumber)); OnPropertyChanged(nameof(NeedsRegistration)); }
        }
    }

    /// <summary>
    /// Real-time device monitor (owner decision 2026-10-16, modelled on Endpoint Protector's device control view; observe and alert only): storage media and
    /// externally connected devices (<see cref="DeviceScope"/>) appear as soon as they are connected and turn red when they are disconnected, without any button.
    /// The device list is read through SetupAPI and diffed by Plug and Play id; a change is noticed from the WMI device events of
    /// <see cref="LogAnalyzer.Dfir.Windows.Devices.DeviceWatcher"/>, or by reading the list every 2 seconds when the events are not available. Rows are kept in a
    /// JSON journal under ProgramData, so they survive a restart. The monitor's own running windows (start, heartbeat every 15 minutes, stop), lost intervals,
    /// alerts and registration numbers go to the sealed audit: the control computes coverage only from those records.
    /// </summary>
    public partial class DeviceMonitorViewModel : ObservableObject, IDisposable
    {
        public static readonly TimeSpan PollWithoutEvents = TimeSpan.FromSeconds(2), PollWithEvents = TimeSpan.FromSeconds(30);

        /// <summary>A gap between two readings longer than this (the station slept, the application was frozen) is recorded as a lost interval.</summary>
        public static readonly TimeSpan LostIntervalThreshold = TimeSpan.FromMinutes(3);

        private readonly Func<IReadOnlyList<PnpSnapshotEntry>> _snapshot;
        private readonly Func<IReadOnlyCollection<string>, IReadOnlyDictionary<string, MediumInfo>> _media;
        private readonly Func<string, string, string?> _audit;
        private readonly Func<DeviceJournal, string?> _save;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Func<string> _consoleUser;
        private readonly bool _isolated;
        private readonly bool _owner;
        private readonly DeviceJournal _journal;
        private readonly Dictionary<string, DeviceMonitorRow> _rows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MediumInfo> _mediaCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly AlertRateLimiter _limiter = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _runId = Guid.NewGuid().ToString("N");
        private List<PnpSnapshotEntry> _last = [];
        private string _mediaSignature = "";
        private DateTimeOffset? _lastReadUtc;
        private (DateTimeOffset From, string Reason)? _pendingLoss;
        private DateTimeOffset? _resumeFrom;
        private bool _started, _stopped;
        private Timer? _timer, _beat;
        private CancellationTokenSource? _debounce;

        /// <param name="snapshot">Reads the present devices (SetupAPI).</param>
        /// <param name="media">Reads the medium of each drive (capacity, volume serial, letters, label) for the drive ids asked.</param>
        /// <param name="audit">Writes one entry to the sealed operation audit: (event, detail) → null when written, otherwise the reason.</param>
        /// <param name="save">Writes the journal: null when written, otherwise the reason.</param>
        /// <param name="isolated">The station posture is isolated (Standalone or isolated LAN): forbidden classes raise a critical alert.</param>
        /// <param name="consoleUser">The account of the active console session ("" = nobody logged on).</param>
        /// <param name="owner">This process runs the monitor; false = another instance does (or the monitor could not start) and this one only shows the journal.</param>
        public DeviceMonitorViewModel(Func<IReadOnlyList<PnpSnapshotEntry>> snapshot, Func<IReadOnlyCollection<string>, IReadOnlyDictionary<string, MediumInfo>> media,
            Func<string, string, string?> audit, DeviceJournal journal, Func<DeviceJournal, string?> save, bool isolated, Func<DateTimeOffset>? clock = null,
            string loadProblem = "", Func<string>? consoleUser = null, bool owner = true, string notRunning = "")
        {
            _snapshot = snapshot; _media = media; _audit = audit; _journal = journal; _save = save; _isolated = isolated; _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _consoleUser = consoleUser ?? (() => ""); _owner = owner;
            foreach (var e in _journal.Devices.Where(DeviceScope.IsInScope)) _rows[e.Identity] = new DeviceMonitorRow(e, this);
            if (loadProblem.Length > 0) Problem = loadProblem;
            if (!owner) { Mode = notRunning.Length > 0 ? notRunning : "Monitor nepornit în această instanță."; }
            ApplyFilter();
        }

        /// <summary>The application's monitor: the station's SetupAPI list, the media reader, the sealed audit and the machine-wide journal.</summary>
        public static DeviceMonitorViewModel CreateForStation()
        {
            var path = DeviceJournalStore.DefaultPath;
            var own = DeviceJournalStore.TryOwnMonitor();
            var folder = DeviceJournalStore.EnsureSecure(path, create: own.State == MonitorOwnership.Owner);
            var problems = new List<string>();
            if (folder.Warning.Length > 0) problems.Add(folder.Warning);
            string notRunning = own.State switch
            {
                MonitorOwnership.OtherInstance => "Monitor nepornit în această instanță: o altă instanță a aplicației rulează monitorul pe această stație; aici se vede jurnalul lui.",
                MonitorOwnership.Failed => "Monitor nepornit: nu s-a putut stabili dacă monitorul rulează (" + own.Reason + "). Dispozitivele conectate nu sunt urmărite.",
                _ => "",
            };
            if (own.State == MonitorOwnership.Failed)
            {
                System.Diagnostics.Trace.TraceError("LogAnalyzer device monitor not started: " + own.Reason);
                var now = DateTimeOffset.UtcNow;
                LogAnalyzer.UI.Services.AuthApp.TryRecord(OperationAudit.DeviceMonitorGap, OperationAudit.DeviceMonitorGapDetail("", now, now, "monitorul nu a putut porni: " + own.Reason));
            }
            if (own.State == MonitorOwnership.Owner && DeviceJournalStore.MigrateLegacy(DeviceJournalStore.LegacyPath, path, LogAnalyzer.UI.Services.AuthApp.TryRecord) is { } migration) problems.Add(migration);
            var journal = DeviceJournalStore.Load(path, out var loadProblem);
            if (loadProblem.Length > 0) problems.Add(loadProblem);
            return new DeviceMonitorViewModel(
                LogAnalyzer.Dfir.Windows.Devices.PnpEnumerator.Snapshot,
                LogAnalyzer.Dfir.Windows.Devices.MediumReader.For,
                LogAnalyzer.UI.Services.AuthApp.TryRecord,
                journal,
                j => DeviceJournalStore.Save(path, j),
                LogAnalyzer.Core.Services.Edition.StationPostureContext.Current != LogAnalyzer.Core.Services.Edition.StationPosture.Internet,
                loadProblem: string.Join(" ", problems),
                consoleUser: LogAnalyzer.Dfir.Windows.Devices.ConsoleUser.Current,
                owner: own.State == MonitorOwnership.Owner, notRunning: notRunning);
        }

        /// <summary>Raised for every connection seen after the first reading whose pop-up is not suppressed (information or critical).</summary>
        public event Action<DeviceAlertRecord>? Alert;

        public ObservableCollection<DeviceMonitorRow> Rows { get; } = new();

        public string[] Filters { get; } = { FilterAll, FilterConnected, FilterStorage, FilterNoNumber, FilterAlert };
        public const string FilterAll = "Toate dispozitivele", FilterConnected = "Conectate acum", FilterStorage = "Medii de stocare",
                            FilterNoNumber = "Medii fără număr de înregistrare", FilterAlert = "Cu alertă";
        [ObservableProperty] private string _filter = FilterAll;
        partial void OnFilterChanged(string value) => ApplyFilter();

        [ObservableProperty] private string _summary = "Monitorul pornește singur la deschiderea aplicației.";
        [ObservableProperty] private string _mode = "";
        [ObservableProperty] private string _problem = "";
        /// <summary>Radios the monitor cannot see through (Bluetooth off or absent, Wi-Fi disabled): never presented as „no devices”.</summary>
        [ObservableProperty] private string _radioNotice = "";
        /// <summary>„N alerte suprimate”: pop-ups held back by the rate limit (the records are all written).</summary>
        [ObservableProperty] private string _suppressedText = "";

        /// <summary>Pop-ups held back by the rate limit since the start.</summary>
        public int SuppressedAlerts => _limiter.Suppressed;

        /// <summary>The journal as it is now (a copy, for the control report and the tests).</summary>
        public DeviceJournal JournalCopy() { lock (_journal) return _journal.Copy(); }

        /// <summary>Local time with the UTC time next to it; empty when unknown.</summary>
        public static string When(DateTimeOffset? t) =>
            t is { } v ? $"{v.ToLocalTime():yyyy-MM-dd HH:mm:ss} (UTC {v.UtcDateTime:HH:mm:ss})" : "";

        /// <summary>
        /// The monitor starts: its start is sealed (and written in the journal), then the first reading (no alerts: what is connected was there when the monitor
        /// started), then the change source: events when <paramref name="eventsAvailable"/>, otherwise the list every 2 seconds. A safety reading every 30 seconds
        /// catches an event that was missed, and a heartbeat every 15 minutes is sealed.
        /// </summary>
        public async Task StartAsync(bool eventsAvailable)
        {
            if (_started) return;
            _started = true;
            if (!_owner) { UpdateSummary(); return; }
            var now = _clock();
            _resumeFrom = _journal.Runs.Count > 0 ? _journal.Runs.Max(r => r.EndUtc ?? r.LastBeatUtc) : null;
            if (_audit(OperationAudit.DeviceMonitorStarted, OperationAudit.DeviceMonitorRunDetail(_runId, "pornit")) is { } refused)
                Problem = "Pornirea monitorului nu a putut fi trecută în jurnalul de audit: " + refused + ". Fără ea, controlul nu poate dovedi că monitorul a rulat.";
            DeviceJournalMerge.StartRun(_journal, _runId, now);
            _lastReadUtc = now;
            await RefreshAsync(initial: true);
            SetEventsAvailable(eventsAvailable);
            _beat = new Timer(_ => Beat(), null, DeviceJournalMerge.HeartbeatInterval, DeviceJournalMerge.HeartbeatInterval);
        }

        /// <summary>The heartbeat: sealed in the audit and written in the journal (the control computes the coverage from the sealed one).</summary>
        public void Beat()
        {
            if (!_owner || !_started || _stopped) return;
            if (_audit(OperationAudit.DeviceMonitorHeartbeat, OperationAudit.DeviceMonitorRunDetail(_runId, "puls")) is { } refused)
                Problem = "Pulsul monitorului nu a putut fi trecut în jurnalul de audit: " + refused;
            lock (_journal) DeviceJournalMerge.Heartbeat(_journal, _clock());
            Save();
        }

        /// <summary>The monitor stops cleanly: the stop is sealed and the journal closes its running window.</summary>
        public void Stop(string reason = "oprire normală")
        {
            if (!_owner || !_started || _stopped) return;
            _stopped = true;
            _audit(OperationAudit.DeviceMonitorStopped, OperationAudit.DeviceMonitorRunDetail(_runId, reason));
            lock (_journal) DeviceJournalMerge.StopRun(_journal, _clock());
            Save();
        }

        /// <summary>Events available: a safety reading every 30 s; not available: the list is read every 2 s and compared.</summary>
        public void SetEventsAvailable(bool available)
        {
            if (!_started || !_owner) return;
            var period = available ? PollWithEvents : PollWithoutEvents;
            Mode = available
                ? "Monitor PORNIT: conectările și deconectările apar singure, în câteva secunde (evenimente Windows)."
                : "Monitor PORNIT: evenimentele Windows nu sunt disponibile, așa că lista dispozitivelor se citește la 2 secunde și se compară.";
            _timer?.Dispose();
            _timer = new Timer(_ => Post(() => _ = RefreshAsync(initial: false)), null, period, period);
        }

        /// <summary>
        /// The change source failed or lost events (watcher error, overflow, WMI service stop): the lost interval is sealed as a „device monitor gap” at the next
        /// reading, which is a full rescan compared with the last known state („observat la reluare”); until events are back the list is read every 2 seconds.
        /// </summary>
        public void ReportLoss(string reason)
        {
            if (!_owner || !_started || _stopped) return;
            lock (_journal) _pendingLoss ??= (_lastReadUtc ?? _clock(), reason);
            SetEventsAvailable(false);
            Post(() => _ = RefreshAsync(initial: false));
        }

        /// <summary>A Plug and Play device arrived or left (WMI event): one reading shortly after, for the whole burst of events of one device.</summary>
        public async void OnPnpEvent()
        {
            if (!_owner) return;
            _debounce?.Cancel();
            var cts = _debounce = new CancellationTokenSource();
            try { await Task.Delay(700, cts.Token); } catch (TaskCanceledException) { return; }
            await RefreshAsync(initial: false);
        }

        /// <summary>Runs on the window's thread when there is one (timer callbacks), directly otherwise (tests).</summary>
        private static void Post(Action a)
        {
            var d = System.Windows.Application.Current?.Dispatcher;
            if (d is null) a(); else d.BeginInvoke(a);
        }

        /// <summary>
        /// Reads the devices, merges them into the journal, updates the rows, raises the alerts and saves the journal when something changed. A reading after a lost
        /// interval is a rescan: what appeared or disappeared meanwhile is sealed as „observat la reluare”.
        /// </summary>
        public async Task RefreshAsync(bool initial)
        {
            if (!_owner) return;
            if (!await _gate.WaitAsync(0)) return;   // a reading is running: the next event or tick reads again
            try
            {
                List<PnpSnapshotEntry> now;
                try { now = (await Task.Run(() => _snapshot())).ToList(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Problem = "Lista dispozitivelor nu a putut fi citită: " + ex.Message;
                    return;
                }
                var when = _clock();
                // a lost interval: reported by the change source, or a silence between two readings (the station slept, the application was frozen)
                (DateTimeOffset From, string Reason)? loss = null;
                lock (_journal)
                {
                    if (_pendingLoss is { } p) { loss = p; _pendingLoss = null; }
                    else if (!initial && _lastReadUtc is { } lr && when - lr > LostIntervalThreshold) loss = (lr, "pauza monitorului (stația în repaus sau aplicația oprită din execuție)");
                }
                if (loss is { } l && _audit(OperationAudit.DeviceMonitorGap, OperationAudit.DeviceMonitorGapDetail(_runId, l.From, when, l.Reason)) is { } gapRefused)
                    Problem = "Intervalul pierdut nu a putut fi trecut în jurnalul de audit: " + gapRefused;
                bool resume = loss is not null;
                var diff = DeviceMonitorRules.Diff(_last, now);
                _last = now;
                var media = await ReadMediaAsync(now);
                var user = _consoleUser();
                if (user.Length == 0) user = "—";
                List<DeviceJournalChange> changes;
                var alerts = new List<(DeviceAlertRecord Record, bool Show)>();
                int before;
                lock (_journal)
                {
                    before = _journal.Devices.Count;
                    changes = DeviceJournalMerge.Apply(_journal, now, media, user, when, initial).ToList();
                    DeviceJournalMerge.NoteRadios(_journal, DeviceMonitorRules.BlindRadios(now), when);
                    _lastReadUtc = when;
                    DeviceJournalMerge.Heartbeat(_journal, when);
                }
                var gapFrom = loss?.From ?? (initial ? _resumeFrom : null);
                foreach (var c in changes)
                {
                    if (gapFrom is { } gf && (resume || initial))
                        _audit(OperationAudit.DeviceObservedOnResume, OperationAudit.DeviceObservedOnResumeDetail(c.Device.Identity, c.Device.Class.ToString(), c.Device.Name, c.Arrived, gf, when));
                    if (!c.Arrived) continue;
                    var decision = c.CloneText is not null
                        ? new DeviceAlertDecision(DeviceAlertSeverity.Critical, c.CloneText)
                        : DeviceMonitorRules.Decide(c.Device.Class, c.Device.InstanceId, c.Device.RegistrationNumber, _isolated, c.Device.ConfigManagerErrorCode);
                    var text = gapFrom is not null && (resume || initial) ? "observat la reluare: " + decision.Reason : decision.Reason;
                    alerts.Add(RaiseAlert(c.Device, decision.Severity, text, when));
                }
                if (initial && gapFrom is { } g0)
                    foreach (var e in _journal.Devices.Where(e => e.FirstSeenUtc == when && DeviceScope.IsInScope(e) && changes.All(c => c.Device != e)))
                        _audit(OperationAudit.DeviceObservedOnResume, OperationAudit.DeviceObservedOnResumeDetail(e.Identity, e.Class.ToString(), e.Name, true, g0, when));
                bool structural = initial || changes.Count > 0 || _journal.Devices.Count != before || resume;
                RefreshRows();
                if (structural || !diff.IsEmpty) ApplyFilter();
                if (structural || !diff.IsEmpty) Save();
                UpdateSummary();
                foreach (var (record, show) in alerts) if (show) Alert?.Invoke(record);
                RadioNotice = string.Join(" · ", _journal.Radios.Where(r => r.BlindNow).Select(r => r.Text));
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// Records one alert: the journal and the sealed audit always get it (critical ones included), whether or not the pop-up is shown; the rate limiter only
        /// decides the pop-up (per device and kind every 10 minutes, 20 per minute overall) and counts what it holds back. Returns the record and whether its
        /// pop-up is shown (<see cref="Alert"/> is raised by the caller only then).
        /// </summary>
        private (DeviceAlertRecord Record, bool Show) RaiseAlert(DeviceJournalEntry device, DeviceAlertSeverity severity, string text, DateTimeOffset when)
        {
            var record = new DeviceAlertRecord(when, device.Identity, device.Name, device.Class, severity, text);
            var gate = _limiter.Check(device.Identity, severity + "|" + text, when);
            lock (_journal) DeviceJournalMerge.AddAlert(_journal, record);
            var detail = OperationAudit.DeviceAlertCriticalDetail(device.Identity, device.Class.ToString(), device.Name, text) + "; popup=" + (gate == AlertGate.Show ? "afișat" : "suprimat");
            if (_audit(severity == DeviceAlertSeverity.Critical ? OperationAudit.DeviceAlertCritical : OperationAudit.DeviceAlertInfo, detail) is { } refused && severity == DeviceAlertSeverity.Critical)
                Problem = "Alerta critică nu a putut fi trecută în jurnalul de audit: " + refused;
            SuppressedText = _limiter.Suppressed > 0 ? $"{_limiter.Suppressed} alerte suprimate" : "";
            OnPropertyChanged(nameof(SuppressedAlerts));
            return (record, gate == AlertGate.Show);
        }

        /// <summary>Reads the medium of each drive when the set of volumes changed (or a drive is not known yet); a failed read leaves the insertion as a row of its own.</summary>
        private async Task<IReadOnlyDictionary<string, MediumInfo>> ReadMediaAsync(List<PnpSnapshotEntry> now)
        {
            var signature = string.Join("|", now.Where(e => e.SetupClass.Equals("Volume", StringComparison.OrdinalIgnoreCase) || e.SetupClass.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase)
                                                            || e.InstanceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)).Select(e => e.InstanceId.ToUpperInvariant()).OrderBy(x => x, StringComparer.Ordinal));
            var drives = DeviceScope.Classify(now, _mediaCache).Where(d => d.IsMedium && d.Kind != DeviceClass.Optical).Select(d => d.Drive!.InstanceId).ToList();
            if (signature != _mediaSignature || drives.Any(id => !_mediaCache.ContainsKey(id)))
            {
                _mediaSignature = signature;
                if (drives.Count > 0)
                {
                    var fresh = await Task.Run(() => _media(drives));
                    _mediaCache.Clear();
                    foreach (var kv in fresh) _mediaCache[kv.Key] = kv.Value;
                }
                else _mediaCache.Clear();
            }
            return new Dictionary<string, MediumInfo>(_mediaCache, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the registration number of a device: the sealed audit gets the old and the new value FIRST; when it cannot be written the change is refused (the
        /// row keeps the old value) and the reason is shown. Then the journal is saved. Returns whether the number was changed.
        /// </summary>
        public bool SetRegistrationNumber(DeviceMonitorRow row, string oldValue, string newValue)
        {
            if (!_owner)
            {
                Problem = "Numărul de înregistrare nu s-a schimbat: monitorul rulează în altă instanță a aplicației; scrieți numărul acolo.";
                return false;
            }
            DeviceJournalEntry? e;
            lock (_journal) e = _journal.Find(row.Identity);
            if (e is null) return false;
            var detail = OperationAudit.DeviceRegistrationChangedDetail(row.Identity, e.Class.ToString(), e.Name, oldValue, newValue);
            if (_audit(OperationAudit.DeviceRegistrationChanged, detail) is { } refused)
            {
                Problem = "Numărul de înregistrare nu s-a schimbat: jurnalul de audit nu poate fi scris — " + refused;
                return false;
            }
            lock (_journal) DeviceJournalMerge.SetRegistrationNumber(_journal, row.Identity, newValue);
            Problem = "";
            Save();
            UpdateSummary();
            return true;
        }

        private void Save()
        {
            string? error;
            lock (_journal) error = _save(_journal);
            if (error is not null) Problem = "Jurnalul monitorului nu a putut fi salvat: " + error;
        }

        private (string Text, bool Critical) RowAlert(DeviceJournalEntry e)
        {
            var parts = new List<string>();
            bool critical = false;
            if (e.DuplicateSerial) { parts.Add(DeviceMonitorRules.DuplicateSerialMark); critical = true; }
            if (e.Present || e.LastConnectedUtc is not null)
            {
                var d = DeviceMonitorRules.Decide(e.Class, e.InstanceId, e.RegistrationNumber, _isolated, e.ConfigManagerErrorCode);
                if (d.Severity == DeviceAlertSeverity.Critical) { parts.Add(d.Reason); critical = true; }
            }
            return (string.Join("; ", parts), critical);
        }

        internal void Refresh(DeviceMonitorRow row)
        {
            DeviceJournalEntry? e;
            lock (_journal) e = _journal.Find(row.Identity);
            if (e is null) return;
            var (text, critical) = RowAlert(e);
            row.Update(e, text, critical);
        }

        private void RefreshRows()
        {
            lock (_journal)
                foreach (var e in _journal.Devices.Where(DeviceScope.IsInScope))
                {
                    var (text, critical) = RowAlert(e);
                    if (_rows.TryGetValue(e.Identity, out var row)) row.Update(e, text, critical);
                    else { _rows[e.Identity] = row = new DeviceMonitorRow(e, this); row.Update(e, text, critical); }
                }
        }

        private void UpdateSummary()
        {
            var all = _rows.Values.ToList();
            int connected = all.Count(r => r.Present), noNumber = all.Count(r => r.NeedsRegistration);
            Summary = $"Conectate acum: {connected} · cunoscute de monitor: {all.Count}" +
                      (noNumber > 0 ? $" · {noNumber} {(noNumber == 1 ? "mediu de stocare" : "medii de stocare")} {DeviceMonitorReport.NoRegistration}" : "") + ".";
        }

        private bool Matches(DeviceMonitorRow r) => Filter switch
        {
            FilterConnected => r.Present,
            FilterStorage => r.IsStorageMedium,
            FilterNoNumber => r.NeedsRegistration,
            FilterAlert => r.AlertText.Length > 0,
            _ => true,
        };

        /// <summary>The rows in view: connected first, the most recently connected on top.</summary>
        private void ApplyFilter()
        {
            List<DeviceMonitorRow> rows;
            lock (_journal)
                rows = _rows.Values.Where(Matches).OrderByDescending(r => r.Present)
                    .ThenByDescending(r => _journal.Find(r.Identity)?.LastConnectedUtc ?? DateTimeOffset.MinValue).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
        }

        public void Dispose()
        {
            Stop();
            _timer?.Dispose();
            _timer = null;
            _beat?.Dispose();
            _beat = null;
            _debounce?.Cancel();
        }
    }
}
