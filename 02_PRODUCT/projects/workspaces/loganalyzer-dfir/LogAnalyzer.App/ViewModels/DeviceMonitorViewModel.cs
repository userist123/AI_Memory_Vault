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
        [ObservableProperty] private string _vid = "";
        [ObservableProperty] private string _pid = "";
        [ObservableProperty] private string _serial = "";
        [ObservableProperty] private string _capacityText = "";
        [ObservableProperty] private string _letter = "";
        [ObservableProperty] private string _label = "";
        [ObservableProperty] private string _connectedUser = "";
        [ObservableProperty] private string _instanceId = "";
        [ObservableProperty] private string _firstSeenText = "";
        [ObservableProperty] private string _lastConnectedText = "";
        [ObservableProperty] private string _lastDisconnectedText = "";
        /// <summary>The raw values the columns sort by (the text of a date is dd.MM.yyyy, which does not sort as a date).</summary>
        [ObservableProperty] private long _capacityBytes;
        [ObservableProperty] private DateTimeOffset _firstSeenUtc;
        [ObservableProperty] private DateTimeOffset? _lastConnectedUtc;
        [ObservableProperty] private DateTimeOffset? _lastDisconnectedUtc;
        [ObservableProperty] private string _alertText = "";
        [ObservableProperty] private bool _alertIsCritical;

        public string ClassText => DeviceMonitorRules.ClassText(Class);
        public string StatusText => DeviceMonitorRules.StatusText(Status);
        public bool IsStorageMedium => DeviceMonitorRules.IsStorageMedium(Class);
        /// <summary>„Registru”, from the SEALED audit (never from the journal alone): „Înregistrat”, „Neînregistrat” or „Neconfirmat (nesigilat)”.</summary>
        public RegistryState Registry => _mediumMismatch ? RegistryState.NotRegistered : _owner.RegistryStateOf(Identity, _registrationNumber);
        private bool _mediumMismatch;
        public string RegistryText => DeviceMonitorReport.RegistryTextOf(Registry);
        /// <summary>A storage medium whose registration the sealed audit does not confirm (no number, a different one, or one still pending): the row is shown in red.</summary>
        public bool NeedsRegistration => IsStorageMedium && Registry != RegistryState.Registered;
        /// <summary>A storage medium with no registration number at all (the filter and the summary count these).</summary>
        public bool HasNoNumber => IsStorageMedium && (_registrationNumber.Trim().Length == 0 || _mediumMismatch);

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
                OnPropertyChanged(nameof(RegistryText));
                _owner.Refresh(this);
            }
        }

        partial void OnStatusChanged(DeviceStatus value) => OnPropertyChanged(nameof(StatusText));
        partial void OnClassChanged(DeviceClass value) { OnPropertyChanged(nameof(ClassText)); OnPropertyChanged(nameof(IsStorageMedium)); OnPropertyChanged(nameof(NeedsRegistration)); }

        public void Update(DeviceJournalEntry e, string alertText = "", bool alertCritical = false)
        {
            Present = e.Present; Class = DeviceMonitorRules.DisplayClass(e); Status = e.Status;
            StatusTip = StatusText + (e.Present && e.ConfigManagerErrorCode > 0 ? $" (cod {e.ConfigManagerErrorCode}: {DeviceMonitorRules.ProblemText(e.ConfigManagerErrorCode)})" : "") + (DeviceMonitorRules.DisplayDetail(e) is { Length: > 0 } dd ? " – " + dd : "");
            // a row an older version named after the drive letter („F:\”) shows the model or the manufacturer instead
            var name = DeviceMonitorRules.FriendlyName([e.Name], e.Model.Length > 0 ? e.Model : LogAnalyzer.Dfir.Devices.DeviceJournalMerge.KnownManufacturer(e.Manufacturer).Length > 0 ? e.Manufacturer.Trim() : "");
            Name = name.Length > 0 ? name : "(fără nume)"; Manufacturer = e.Manufacturer; Model = e.Model; VidPid = e.VidPid;
            Serial = DeviceMonitorRules.SerialText(e.Serial, e.NoUniqueSerial, e.GenericSerial);
            (Vid, Pid) = DeviceMonitorRules.SplitVidPid(e.VidPid);
            // capacity, letter and label belong to storage media only
            bool storage = DeviceMonitorRules.IsStorageMedium(e.Class);
            CapacityBytes = storage ? e.CapacityBytes : 0;
            CapacityText = storage ? DeviceText.CapacityRo(e.CapacityBytes) : "";
            Letter = storage ? e.VolumeLetters : ""; Label = storage ? e.VolumeLabel : "";
            ConnectedUser = e.ConnectedUser.Length > 0 ? e.ConnectedUser : "—";
            InstanceId = e.InstanceId;
            FirstSeenUtc = e.FirstSeenUtc; LastConnectedUtc = e.LastConnectedUtc; LastDisconnectedUtc = e.LastDisconnectedUtc;
            FirstSeenText = DeviceMonitorViewModel.When(e.FirstSeenUtc);
            LastConnectedText = DeviceMonitorViewModel.When(e.LastConnectedUtc);
            LastDisconnectedText = DeviceMonitorViewModel.When(e.LastDisconnectedUtc);
            AlertText = alertText; AlertIsCritical = alertCritical;
            if (e.RegistrationMediumMismatch != _mediumMismatch) { _mediumMismatch = e.RegistrationMediumMismatch; OnPropertyChanged(nameof(NeedsRegistration)); }
            if (e.RegistrationNumber != _registrationNumber) { _registrationNumber = e.RegistrationNumber; OnPropertyChanged(nameof(RegistrationNumber)); OnPropertyChanged(nameof(NeedsRegistration)); }
            OnPropertyChanged(nameof(RegistryText));
        }

        /// <summary>The cells of the CSV row, in the order of <see cref="DeviceCsv.Headers"/>.</summary>
        public IReadOnlyList<string> CsvCells() =>
        [
            StatusText, Name, ClassText, Manufacturer, Model, Vid, Pid, Serial, CapacityText, Letter, Label, ConnectedUser,
            FirstSeenText, LastConnectedText, LastDisconnectedText, RegistrationNumber, RegistryText, AlertText,
        ];
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
        private readonly Func<string, string, AuditRefusal?> _audit;
        private readonly Func<DeviceAuditReading?>? _auditReader;
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
        /// <summary>
        /// The dispatcher of the thread that created the monitor (the window's thread in the application; none in tests). Timer work is posted there and
        /// never to whatever <see cref="System.Windows.Application.Current"/> happens to be: a process-wide application can belong to another thread.
        /// </summary>
        private readonly System.Windows.Threading.Dispatcher? _dispatcher = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);
        /// <summary>First-seen records whose audit write was refused: retried at every reading (without its sealed record a row is a tampering finding in D05).</summary>
        private readonly List<(string Ev, string Detail)> _unsealed = [];
        /// <summary>The problem code and active state last sealed for each present device (a change, a first sight or a reconnection is sealed).</summary>
        private readonly Dictionary<string, (int Code, bool Active)> _sealedStates = new(StringComparer.OrdinalIgnoreCase);
        private bool _pendingDirty, _inPlaceNoted;
        private long _inPlaceSeen;

        /// <param name="snapshot">Reads the present devices (SetupAPI).</param>
        /// <param name="media">Reads the medium of each drive (capacity, volume serial, letters, label) for the drive ids asked.</param>
        /// <param name="audit">Writes one entry to the sealed operation audit: (event, detail) → null when written, otherwise the reason.</param>
        /// <param name="save">Writes the journal: null when written, otherwise the reason.</param>
        /// <param name="isolated">The station posture is isolated (Standalone or isolated LAN): forbidden classes raise a critical alert.</param>
        /// <param name="consoleUser">The account of the active console session ("" = nobody logged on).</param>
        /// <param name="owner">This process runs the monitor; false = another instance does (or the monitor could not start) and this one only shows the journal.</param>
        public DeviceMonitorViewModel(Func<IReadOnlyList<PnpSnapshotEntry>> snapshot, Func<IReadOnlyCollection<string>, IReadOnlyDictionary<string, MediumInfo>> media,
            Func<string, string, AuditRefusal?> audit, DeviceJournal journal, Func<DeviceJournal, string?> save, bool isolated, Func<DateTimeOffset>? clock = null,
            string loadProblem = "", Func<string>? consoleUser = null, bool owner = true, string notRunning = "", Func<DeviceAuditReading?>? auditReader = null)
        {
            _snapshot = snapshot; _media = media; _auditReader = auditReader;
            // every write to the sealed audit goes through here: a refusal because the sealing key cannot be read (not started as administrator) is noted
            _audit = (ev, detail) => { var refused = audit(ev, detail); NoteSealResult(refused); return refused; }; _journal = journal; _save = save; _isolated = isolated; _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _consoleUser = consoleUser ?? (() => ""); _owner = owner;
            foreach (var e in _journal.Devices.Where(DeviceScope.IsShown)) _rows[e.Identity] = new DeviceMonitorRow(DeviceJournalMerge.ShownView(_journal, e), this);
            // records a previous run could not seal (a refused write) were saved in the journal as pending: they are written at the first reading
            _journal.PendingSeals ??= [];
            if (owner) foreach (var p in _journal.PendingSeals) _unsealed.Add((p.Event, p.Detail));
            _inPlaceSeen = DeviceJournalStore.InPlaceSaves;
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
            // the running monitor (the owner) seals the reset of a damaged journal and sets it aside; anyone else only reads it
            string loadProblem;
            var journal = own.State == MonitorOwnership.Owner
                ? DeviceJournalStore.LoadForMonitor(path, LogAnalyzer.UI.Services.AuthApp.TryRecord, out loadProblem, LogAnalyzer.UI.Services.AuthApp.ReadDeviceAudit)
                : DeviceJournalStore.Load(path, out loadProblem);
            if (loadProblem.Length > 0) problems.Add(loadProblem);
            return new DeviceMonitorViewModel(
                LogAnalyzer.Dfir.Windows.Devices.PnpEnumerator.Snapshot,
                LogAnalyzer.Dfir.Windows.Devices.MediumReader.For,
                LogAnalyzer.UI.Services.AuthApp.TryRecordTyped,
                journal,
                j => DeviceJournalStore.Save(path, j),
                LogAnalyzer.Core.Services.Edition.StationPostureContext.Current != LogAnalyzer.Core.Services.Edition.StationPosture.Internet,
                loadProblem: string.Join(" ", problems),
                consoleUser: LogAnalyzer.Dfir.Windows.Devices.ConsoleUser.Current,
                owner: own.State == MonitorOwnership.Owner, notRunning: notRunning, auditReader: LogAnalyzer.UI.Services.AuthApp.ReadDeviceAudit);
        }

        /// <summary>Raised for every connection seen after the first reading whose pop-up is not suppressed (information or critical).</summary>
        public event Action<DeviceAlertRecord>? Alert;

        public ObservableCollection<DeviceMonitorRow> Rows { get; } = new();

        public string[] Filters { get; } = { FilterAll, FilterConnected, FilterStorage, FilterNoNumber, FilterAlert };
        public const string FilterAll = "Toate dispozitivele", FilterConnected = "Conectate acum", FilterStorage = "Medii de stocare",
                            FilterNoNumber = "Medii fără număr de înregistrare", FilterAlert = "Cu alertă";
        /// <summary>Owner decision 2026-10-22 (supersedes „connected only”): the list opens on the devices that were plugged into the station as external devices, connected or disconnected, one row per physical device.</summary>
        [ObservableProperty] private string _filter = FilterAll;
        partial void OnFilterChanged(string value) => ApplyFilter();

        [ObservableProperty] private string _summary = "Monitorul pornește singur la deschiderea aplicației.";
        [ObservableProperty] private string _mode = "";
        [ObservableProperty] private string _problem = "";
        /// <summary>
        /// Persistent red banner while the sealed audit refuses the monitor's records because its sealing key cannot be read (the application was not started as
        /// administrator); empty otherwise. The manifest and the elevation are not touched: the operator restarts the application as administrator.
        /// </summary>
        [ObservableProperty] private string _sealingBanner = "";
        partial void OnSealingBannerChanged(string value) => OnPropertyChanged(nameof(SealingBlocked));
        public bool SealingBlocked => SealingBanner.Length > 0;
        /// <summary>The result of the last "Export CSV" (where it went is not recorded: the sealed note has the hash, not the path).</summary>
        [ObservableProperty] private string _exportNote = "";

        private void NoteSealResult(AuditRefusal? refused)
        {
            if (refused is null) { if (SealingBanner.Length > 0) SealingBanner = ""; return; }
            if (!refused.KeyUnavailable) return;
            SealingBanner = SealingHealth.Banner;
            bool changed;
            lock (_journal) changed = SealingHealth.Note(_journal, _clock());
            if (changed) _pendingDirty = true;
        }

        /// <summary>Radios the monitor cannot see through (Bluetooth off or absent, Wi-Fi disabled): never presented as „no devices”.</summary>
        [ObservableProperty] private string _radioNotice = "";
        /// <summary>„N alerte suprimate”: pop-ups held back by the rate limit (the records are all written).</summary>
        [ObservableProperty] private string _suppressedText = "";

        /// <summary>Pop-ups held back by the rate limit since the start.</summary>
        public int SuppressedAlerts => _limiter.Suppressed;

        /// <summary>The journal as it is now (a copy, for the control report and the tests).</summary>
        public DeviceJournal JournalCopy() { lock (_journal) return _journal.Copy(); }

        /// <summary>Local time, dd.MM.yyyy HH:mm:ss; empty when unknown.</summary>
        public static string When(DateTimeOffset? t) => DeviceText.LocalTime(t);

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
            // the heartbeat runs on the window's thread like every reading (the journal and the rows are touched there)
            _beat = new Timer(_ => Post(Beat), null, DeviceJournalMerge.HeartbeatInterval, DeviceJournalMerge.HeartbeatInterval);
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
            // a loss reported but not yet rescanned (the posted reading did not run before the stop) is still sealed as a gap, up to the stop
            (DateTimeOffset From, string Reason)? loss;
            lock (_journal) { loss = _pendingLoss; _pendingLoss = null; }
            if (loss is { } l && _audit(OperationAudit.DeviceMonitorGap, OperationAudit.DeviceMonitorGapDetail(_runId, l.From, _clock(), l.Reason)) is { } gapRefused)
                Problem = "Intervalul pierdut nu a putut fi trecut în jurnalul de audit: " + gapRefused;
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

        /// <summary>Runs on the thread that created the monitor when it has a dispatcher (timer callbacks), directly otherwise (tests).</summary>
        private void Post(Action a)
        {
            // a dispatcher whose thread is gone or shutting down never runs what is queued: the work is done directly then (it is serialised by the gate)
            var d = _dispatcher;
            if (d is null || d.HasShutdownStarted || !d.Thread.IsAlive) a(); else d.BeginInvoke(a);
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
                RetryUnsealed();
                var diff = DeviceMonitorRules.Diff(_last, now);
                _last = now;
                var media = await ReadMediaAsync(now);
                var user = _consoleUser();
                if (user.Length == 0) user = "—";
                List<DeviceJournalChange> changes;
                var alerts = new List<(DeviceAlertRecord Record, bool Show)>();
                var rekeys = new List<MediumRekey>();
                var reclassified = new List<DeviceReclassification>();
                IReadOnlyList<RadioChange> radios;
                List<DeviceJournalEntry> firstSeen;
                int before;
                lock (_journal)
                {
                    before = _journal.Devices.Count;
                    var known = new HashSet<string>(_journal.Devices.Select(d => d.Identity), StringComparer.OrdinalIgnoreCase);
                    changes = DeviceJournalMerge.Apply(_journal, now, media, user, when, initial, rekeys, reclassified, ReadSealedKinds).ToList();
                    radios = DeviceJournalMerge.NoteRadios(_journal, DeviceMonitorRules.BlindRadios(now), when);
                    _lastReadUtc = when;
                    DeviceJournalMerge.Heartbeat(_journal, when);
                    // a new row (not a card row that only got its real identity: that link is sealed as a re-key)
                    firstSeen = _journal.Devices.Where(d => !known.Contains(d.Identity) && DeviceScope.IsInScope(d)
                                                            && !rekeys.Any(r => !r.Merged && r.NewIdentity.Equals(d.Identity, StringComparison.OrdinalIgnoreCase))).ToList();
                }
                // review finding B1: every device is sealed on first sight (identity, class, serial, first-seen), a re-keyed card links its two identities, and a
                // radio state change is sealed, so D05 compares the plain journal with the sealed audit both ways
                foreach (var r in rekeys)
                    Seal(OperationAudit.DeviceMediumRekeyed, OperationAudit.DeviceMediumRekeyedDetail(r.OldIdentity, r.NewIdentity, r.Device.Class.ToString(), r.Device.Name));
                foreach (var e in firstSeen)
                    Seal(OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail(e.Identity, e.Class.ToString(), e.Name, e.Serial, e.FirstSeenUtc, user: e.ConnectedUser, classifier: DeviceScope.ClassifierVersion));
                // the kind of a known device changed because the classifier changed (not the device): one sealed note, informational for check D05
                foreach (var c in reclassified)
                    Seal(OperationAudit.DeviceReclassified, OperationAudit.DeviceReclassifiedDetail(c.Identity, c.OldClass.ToString(), c.NewClass.ToString(), c.OldVersion, c.NewVersion, c.Device.Name));
                foreach (var r in radios)
                    _audit(OperationAudit.DeviceRadioState, OperationAudit.DeviceRadioStateDetail(r.Kind, r.Blind, r.Text, user));
                // review round 2: every disconnection (who was logged on) and the set of devices present at each run start are sealed, so a row that claims
                // absence, or dates outside the period, for a device the sealed audit saw is a finding; the problem code and the active state are sealed when they change
                foreach (var c in changes.Where(c => !c.Arrived))
                {
                    Seal(OperationAudit.DeviceDisconnected, OperationAudit.DeviceDisconnectedDetail(c.Device.Identity, c.Device.Class.ToString(), c.Device.Name, user));
                    _sealedStates.Remove(c.Device.Identity);
                }
                if (initial)
                {
                    List<DeviceJournalEntry> presentNow;
                    lock (_journal) presentNow = _journal.Devices.Where(d => d.Present && DeviceScope.IsInScope(d)).ToList();
                    foreach (var d in presentNow)
                        Seal(OperationAudit.DevicePresent, OperationAudit.DevicePresentDetail(_runId, d.Identity, d.Class.ToString(), d.Name, user));
                }
                SealStates();
                var gapFrom = loss?.From ?? (initial ? _resumeFrom : null);
                foreach (var c in changes)
                {
                    if (gapFrom is { } gf && (resume || initial))
                        _audit(OperationAudit.DeviceObservedOnResume, OperationAudit.DeviceObservedOnResumeDetail(c.Device.Identity, c.Device.Class.ToString(), c.Device.Name, c.Arrived, gf, when));
                    if (!c.Arrived) continue;
                    var decision = c.CloneText is not null
                        ? new DeviceAlertDecision(DeviceAlertSeverity.Critical, c.CloneText)
                        : DeviceMonitorRules.Decide(c.Device.Class, c.Device.InstanceId, DeviceMonitorRules.EffectiveNumber(c.Device), _isolated, c.Device.ConfigManagerErrorCode);
                    var text = gapFrom is not null && (resume || initial) ? "observat la reluare: " + decision.Reason : decision.Reason;
                    alerts.Add(RaiseAlert(c.Device, decision.Severity, text, when));
                    // the same device back with another volume (reformatted, or a clone of its serial): its own sealed record, never a silent take-over
                    if (c.InfoText is { } info) alerts.Add(RaiseAlert(c.Device, DeviceAlertSeverity.Info, info, when));
                }
                if (initial && gapFrom is { } g0)
                    foreach (var e in _journal.Devices.Where(e => e.FirstSeenUtc == when && DeviceScope.IsInScope(e) && changes.All(c => c.Device != e)))
                        _audit(OperationAudit.DeviceObservedOnResume, OperationAudit.DeviceObservedOnResumeDetail(e.Identity, e.Class.ToString(), e.Name, true, g0, when));
                bool structural = initial || changes.Count > 0 || _journal.Devices.Count != before || resume;
                RefreshRows();
                if (structural || !diff.IsEmpty) ApplyFilter();
                if (structural || !diff.IsEmpty || _pendingDirty) Save();
                UpdateSummary();
                foreach (var (record, show) in alerts) if (show) Alert?.Invoke(record);
                RadioNotice = string.Join(" · ", _journal.Radios.Where(r => r.BlindNow).Select(r => r.Text));
            }
            finally { _gate.Release(); }
        }

        /// <summary>Writes a record that D05 needs for every row; a refused write is kept and retried at the next reading (first the older ones).</summary>
        private void Seal(string ev, string detail)
        {
            _unsealed.Add((ev, detail));
            RetryUnsealed();
        }

        private void RetryUnsealed()
        {
            while (_unsealed.Count > 0)
            {
                if (_audit(_unsealed[0].Ev, _unsealed[0].Detail) is { } refused)
                {
                    Problem = $"{_unsealed.Count} înregistrări ale dispozitivelor nu au putut fi trecute în jurnalul de audit (se reîncearcă): " + refused;
                    SyncPending();
                    return;
                }
                _unsealed.RemoveAt(0);
            }
            SyncPending();
        }

        /// <summary>The records not sealed yet are kept in the journal, flagged pending, so they survive a restart (they stay untrusted until sealed; check D05 says DE VERIFICAT meanwhile).</summary>
        private void SyncPending()
        {
            lock (_journal)
            {
                var cur = _journal.PendingSeals;
                if (cur.Count == _unsealed.Count && cur.Select(p => (p.Event, p.Detail)).SequenceEqual(_unsealed)) return;
                _journal.PendingSeals = _unsealed.Select(u => new PendingSeal { Event = u.Ev, Detail = u.Detail }).ToList();
                _pendingDirty = true;
            }
        }

        /// <summary>Seals the problem code and the active state of every present in-scope device that is new, reconnected or changed since it was last sealed.</summary>
        private void SealStates()
        {
            List<DeviceJournalEntry> present;
            lock (_journal) present = _journal.Devices.Where(d => d.Present && DeviceScope.IsInScope(d)).ToList();
            foreach (var d in present)
            {
                var state = (d.ConfigManagerErrorCode, Active: !DeviceMonitorRules.IsDisabledRadio(d.Class, d.ConfigManagerErrorCode));
                if (_sealedStates.TryGetValue(d.Identity, out var last) && last == state) continue;
                _sealedStates[d.Identity] = state;
                Seal(OperationAudit.DeviceStateChanged, OperationAudit.DeviceStateChangedDetail(d.Identity, d.Class.ToString(), state.ConfigManagerErrorCode, state.Active));
            }
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
            var detail = OperationAudit.DeviceAlertCriticalDetail(device.Identity, device.Class.ToString(), device.Name, text) + (device.ConnectedUser.Length > 0 ? "; user=" + OperationAudit.DeviceField(device.ConnectedUser) : "") + "; popup=" + (gate == AlertGate.Show ? "afișat" : "suprimat");
            // a refused write (any alert, critical or not) is kept and retried like a first-seen record, and saved in the journal as pending
            Seal(severity == DeviceAlertSeverity.Critical ? OperationAudit.DeviceAlertCritical : OperationAudit.DeviceAlertInfo, detail);
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
            lock (_sealedGate) { _sealedByMe[OperationAudit.DeviceField(row.Identity)] = OperationAudit.DeviceField(newValue); _sealedRead = null; }
            Problem = "";
            Save();
            UpdateSummary();
            return true;
        }

        private readonly object _sealedGate = new();
        private readonly Dictionary<string, string> _sealedByMe = new(StringComparer.OrdinalIgnoreCase);
        private (DeviceAuditReading? Audit, DateTimeOffset At)? _sealedRead;
        private static readonly TimeSpan SealedReadEvery = TimeSpan.FromSeconds(30);

        /// <summary>The sealed audit as the page relies on it: the reader's reading (verified chain, kept 30 s and dropped after a change of a number); without a reader, only what this instance sealed itself.</summary>
        private DeviceAuditReading? SealedAudit()
        {
            lock (_sealedGate)
            {
                if (_auditReader is null) return new DeviceAuditReading(true, new Dictionary<string, string>(_sealedByMe, StringComparer.OrdinalIgnoreCase), [], 0);
                var now = _clock();
                if (_sealedRead is { } c && now - c.At < SealedReadEvery && now >= c.At) return c.Audit;
                DeviceAuditReading? a = null;
                try { a = _auditReader(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { a = null; }
                _sealedRead = (a, now);
                return a;
            }
        }

        /// <summary>
        /// The kind and classifier version of every device as the SEALED audit holds them, read fresh (the reclassification must be sealed at the very reading that
        /// sees the change); null when there is no reader or the chain does not verify: then nothing is a reclassification and check D05 keeps its finding.
        /// </summary>
        private IReadOnlyDictionary<string, SealedKind>? ReadSealedKinds()
        {
            try { return _auditReader?.Invoke() is { ChainValid: true } a ? DeviceMonitorAuditCheck.SealedKinds(a) : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
        }

        /// <summary>„Registru” of a row: the number in the journal against the number the sealed audit holds (and a change still waiting to be sealed).</summary>
        internal RegistryState RegistryStateOf(string identity, string journalNumber)
        {
            List<PendingSeal> pending;
            lock (_journal) pending = (_journal.PendingSeals ?? []).ToList();
            return DeviceMonitorReport.RegistryStateOf(identity, journalNumber, SealedAudit(), pending);
        }

        /// <summary>
        /// „Istoric”: the events of one device from the SEALED audit (read now, verified chain), with the records still waiting to be sealed marked as such.
        /// <c>Notice</c> says when the sealed audit could not be read or does not verify.
        /// </summary>
        public (IReadOnlyList<DeviceHistoryEvent> Events, string Notice) HistoryFor(DeviceMonitorRow row)
        {
            DeviceAuditReading? audit = null;
            try { audit = _auditReader?.Invoke(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { audit = null; }
            List<PendingSeal> pending;
            IReadOnlyList<string> identities;
            lock (_journal)
            {
                pending = _journal.PendingSeals.Select(p => new PendingSeal { Event = p.Event, Detail = p.Detail }).ToList();
                identities = DeviceJournalMerge.FoldedIdentities(_journal, row.Identity);   // the history of every row folded into this one
            }
            return (DeviceHistory.Build(identities.ToList(), audit, pending), DeviceHistory.Notice(audit));
        }

        /// <summary>The CSV of the rows in view: the bytes of the file (UTF-8 with a byte-order mark), the number of rows and the SHA-256 of the bytes.</summary>
        public sealed record CsvExport(byte[] Bytes, int Rows, string Sha256);

        /// <summary>„Export CSV”, step 1: the file as it would be written, for the rows in view in the order the operator sorted them.</summary>
        public static CsvExport PrepareCsv(IEnumerable<DeviceMonitorRow> rows)
        {
            var list = rows.ToList();
            var bytes = DeviceCsv.ToBytes(list.Select(r => r.CsvCells()));
            return new CsvExport(bytes, list.Count, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        }

        /// <summary>
        /// „Export CSV”, step 2: seals "device.export_csv" (operator, number of rows, SHA-256 of the file, never its path) BEFORE the file is written; returns null
        /// when sealed, otherwise the reason (the caller then writes nothing). The caller wraps both in the application's audit-first export (<c>ExportAudit.Run</c>).
        /// </summary>
        public AuditRefusal? SealCsvExport(CsvExport export) =>
            _audit(OperationAudit.DeviceExportCsv, OperationAudit.DeviceExportCsvDetail(OperatorIdentity.Who, export.Rows, export.Sha256));

        private void Save()
        {
            string? error;
            lock (_journal) error = _save(_journal);
            if (error is not null) Problem = "Jurnalul monitorului nu a putut fi salvat: " + error;
            _pendingDirty = false;
            // accepted residual: an account that cannot create the temporary file rewrites the journal in place; noted once per run in the sealed audit
            if (!_inPlaceNoted && DeviceJournalStore.InPlaceSaves != _inPlaceSeen)
            {
                _inPlaceNoted = true;
                _inPlaceSeen = DeviceJournalStore.InPlaceSaves;
                Seal(OperationAudit.DeviceJournalInPlaceSave, "journal=device_monitor.json; save=in-place (the account cannot create the temporary file in the folder)");
            }
        }

        private (string Text, bool Critical) RowAlert(DeviceJournalEntry e)
        {
            var parts = new List<string>();
            bool critical = false;
            if (e.DuplicateSerial) { parts.Add(DeviceMonitorRules.DuplicateSerialMark); critical = true; }
            if (e.Present || e.LastConnectedUtc is not null)
            {
                var d = DeviceMonitorRules.Decide(DeviceMonitorRules.DisplayClass(e), e.InstanceId, DeviceMonitorRules.EffectiveNumber(e), _isolated, e.ConfigManagerErrorCode);
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
            lock (_journal) e = DeviceJournalMerge.ShownView(_journal, e);
            row.Update(e, text, critical);
        }

        private void RefreshRows()
        {
            lock (_journal)
            {
                // one physical device is one row: rows folded into another one (interfaces, the bridge of a disk, an old clone mark) are not listed while absent
                foreach (var e in _journal.Devices.Where(DeviceScope.IsShown))
                {
                    var (text, critical) = RowAlert(e);
                    var v = DeviceJournalMerge.ShownView(_journal, e);
                    if (_rows.TryGetValue(e.Identity, out var row)) row.Update(v, text, critical);
                    else { _rows[e.Identity] = row = new DeviceMonitorRow(v, this); row.Update(v, text, critical); }
                }
                // a card row re-keyed once its medium became readable: the row of the old identity goes (the journal no longer has it)
                var ids = new HashSet<string>(_journal.Devices.Where(DeviceScope.IsShown).Select(e => e.Identity), StringComparer.OrdinalIgnoreCase);
                foreach (var stale in _rows.Keys.Where(k => !ids.Contains(k)).ToList()) _rows.Remove(stale);
            }
        }

        private void UpdateSummary()
        {
            var all = _rows.Values.ToList();
            int connected = all.Count(r => r.Present), noNumber = all.Count(r => r.HasNoNumber);
            Summary = $"Conectate acum (stocare și externe): {connected} · văzute de monitor (inclusiv deconectate): {all.Count}" +
                      (noNumber > 0 ? $" · {noNumber} {(noNumber == 1 ? "mediu de stocare" : "medii de stocare")} {DeviceMonitorReport.NoRegistration}" : "") + ".";
        }

        private bool Matches(DeviceMonitorRow r) => Filter switch
        {
            FilterConnected => r.Present,
            FilterStorage => r.IsStorageMedium,
            FilterNoNumber => r.HasNoNumber,
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
