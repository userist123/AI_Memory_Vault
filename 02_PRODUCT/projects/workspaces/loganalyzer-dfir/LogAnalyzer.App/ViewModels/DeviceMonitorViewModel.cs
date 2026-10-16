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
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _manufacturer = "";
        [ObservableProperty] private string _vidPid = "";
        [ObservableProperty] private string _serial = "";
        [ObservableProperty] private string _instanceId = "";
        [ObservableProperty] private DriverState _driverState;
        [ObservableProperty] private string _driverText = "";
        [ObservableProperty] private string _firstSeenText = "";
        [ObservableProperty] private string _lastConnectedText = "";
        [ObservableProperty] private string _lastDisconnectedText = "";

        public string ClassText => DeviceMonitorRules.ClassText(Class);
        public string StatusText => Present ? "conectat" : "deconectat";
        public bool IsStorageMedium => DeviceMonitorRules.IsStorageMedium(Class);
        /// <summary>A storage medium without a registration number: the row is shown in red until the number is written.</summary>
        public bool NeedsRegistration => IsStorageMedium && RegistrationNumber.Trim().Length == 0;
        public bool DriverProblem => DriverState is DriverState.Missing or DriverState.Error or DriverState.Unsigned;
        public bool IsInternal => Class is DeviceClass.Internal or DeviceClass.InternalDisk;
        public string DriverStateText => DriverState switch
        {
            DriverState.Installed => "instalat",
            DriverState.Unsigned => "NESEMNAT",
            DriverState.Missing => "fără driver",
            DriverState.Error => "eroare",
            _ => "necunoscut",
        };

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
            }
        }

        partial void OnPresentChanged(bool value) => OnPropertyChanged(nameof(StatusText));
        partial void OnClassChanged(DeviceClass value) { OnPropertyChanged(nameof(ClassText)); OnPropertyChanged(nameof(IsStorageMedium)); OnPropertyChanged(nameof(NeedsRegistration)); OnPropertyChanged(nameof(IsInternal)); }
        partial void OnDriverStateChanged(DriverState value) { OnPropertyChanged(nameof(DriverStateText)); OnPropertyChanged(nameof(DriverProblem)); }

        public void Update(DeviceJournalEntry e)
        {
            Present = e.Present; Class = e.Class; Name = e.Name.Length > 0 ? e.Name : "(fără nume)"; Manufacturer = e.Manufacturer; VidPid = e.VidPid; Serial = e.Serial;
            InstanceId = e.InstanceId; DriverState = e.DriverState; DriverText = e.DriverText;
            FirstSeenText = DeviceMonitorViewModel.When(e.FirstSeenUtc);
            LastConnectedText = DeviceMonitorViewModel.When(e.LastConnectedUtc);
            LastDisconnectedText = DeviceMonitorViewModel.When(e.LastDisconnectedUtc);
            if (e.RegistrationNumber != _registrationNumber) { _registrationNumber = e.RegistrationNumber; OnPropertyChanged(nameof(RegistrationNumber)); OnPropertyChanged(nameof(NeedsRegistration)); }
        }
    }

    /// <summary>
    /// Real-time device monitor (owner decision 2026-10-16, modelled on Endpoint Protector's device control view; observe and alert only): every device of every
    /// class appears as soon as it is connected and turns red when it is disconnected, without any button. The device list is read through SetupAPI and diffed by
    /// Plug and Play id; a change is noticed from the WMI device events of <see cref="LogAnalyzer.Dfir.Windows.Devices.DeviceWatcher"/>, or by reading the list
    /// every 2 seconds when the events are not available. Rows are kept in a JSON journal under the application data folder, so they survive a restart.
    /// </summary>
    public partial class DeviceMonitorViewModel : ObservableObject, IDisposable
    {
        public static readonly TimeSpan PollWithoutEvents = TimeSpan.FromSeconds(2), PollWithEvents = TimeSpan.FromSeconds(30);

        private readonly Func<IReadOnlyList<PnpSnapshotEntry>> _snapshot;
        private readonly Func<IReadOnlyCollection<string>?, IReadOnlyDictionary<string, SignedDriverInfo>> _drivers;
        private readonly Func<string, string, string?> _audit;
        private readonly Func<DeviceJournal, string?> _save;
        private readonly Func<DateTimeOffset> _clock;
        private readonly bool _isolated;
        private readonly DeviceJournal _journal;
        private readonly Dictionary<string, DeviceMonitorRow> _rows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SignedDriverInfo> _driverCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _gate = new(1, 1);
        private List<PnpSnapshotEntry> _last = [];
        private bool _started;
        private volatile bool _driversDirty;
        private Timer? _timer;
        private CancellationTokenSource? _debounce;

        /// <param name="snapshot">Reads the present devices (SetupAPI).</param>
        /// <param name="drivers">Reads the signed-driver data: null = every device, otherwise only those ids (Win32_PnPSignedDriver).</param>
        /// <param name="audit">Writes one entry to the sealed operation audit: (event, detail) → null when written, otherwise the reason.</param>
        /// <param name="save">Writes the journal: null when written, otherwise the reason.</param>
        /// <param name="isolated">The station posture is isolated (Standalone or isolated LAN): forbidden classes raise a critical alert.</param>
        public DeviceMonitorViewModel(Func<IReadOnlyList<PnpSnapshotEntry>> snapshot, Func<IReadOnlyCollection<string>?, IReadOnlyDictionary<string, SignedDriverInfo>> drivers,
            Func<string, string, string?> audit, DeviceJournal journal, Func<DeviceJournal, string?> save, bool isolated, Func<DateTimeOffset>? clock = null, string loadProblem = "")
        {
            _snapshot = snapshot; _drivers = drivers; _audit = audit; _journal = journal; _save = save; _isolated = isolated; _clock = clock ?? (() => DateTimeOffset.UtcNow);
            foreach (var e in _journal.Devices) _rows[e.Identity] = new DeviceMonitorRow(e, this);
            if (loadProblem.Length > 0) Problem = loadProblem;
            ApplyFilter();
        }

        /// <summary>The application's monitor: the station's SetupAPI list, WMI driver data, the sealed audit and the journal in the application data folder.</summary>
        public static DeviceMonitorViewModel CreateForStation()
        {
            var path = DeviceJournalStore.DefaultPath;
            var journal = DeviceJournalStore.Load(path, out var problem);
            return new DeviceMonitorViewModel(
                LogAnalyzer.Dfir.Windows.Devices.PnpEnumerator.Snapshot,
                ids => ids is null ? LogAnalyzer.Dfir.Windows.Devices.SignedDriverReader.All() : LogAnalyzer.Dfir.Windows.Devices.SignedDriverReader.For(ids),
                LogAnalyzer.UI.Services.AuthApp.TryRecord,
                journal,
                j =>
                {
                    try { LogAnalyzer.Dfir.IO.Json.Write(path, j); return null; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
                },
                LogAnalyzer.Core.Services.Edition.StationPostureContext.Current != LogAnalyzer.Core.Services.Edition.StationPosture.Internet,
                loadProblem: problem);
        }

        /// <summary>Raised for every connection seen after the first reading (information or critical).</summary>
        public event Action<DeviceAlertRecord>? Alert;

        public ObservableCollection<DeviceMonitorRow> Rows { get; } = new();

        public string[] Filters { get; } = { FilterAll, FilterExternal, FilterConnected, FilterStorage, FilterNoNumber, FilterDriver };
        public const string FilterAll = "Toate dispozitivele", FilterExternal = "Fără componentele interne", FilterConnected = "Conectate acum",
                            FilterStorage = "Medii de stocare", FilterNoNumber = "Medii fără număr de înregistrare", FilterDriver = "Fără driver sau cu eroare de driver";
        [ObservableProperty] private string _filter = FilterAll;
        partial void OnFilterChanged(string value) => ApplyFilter();

        [ObservableProperty] private string _summary = "Monitorul pornește singur la deschiderea aplicației.";
        [ObservableProperty] private string _mode = "";
        [ObservableProperty] private string _problem = "";

        /// <summary>The journal as it is now (a copy, for the control report and the tests).</summary>
        public DeviceJournal JournalCopy() => _journal.Copy();

        /// <summary>Local time with the UTC time next to it; empty when unknown.</summary>
        public static string When(DateTimeOffset? t) =>
            t is { } v ? $"{v.ToLocalTime():yyyy-MM-dd HH:mm:ss} (UTC {v.UtcDateTime:HH:mm:ss})" : "";

        /// <summary>
        /// First reading (no alerts: what is connected was there when the monitor started), then the change source: events when <paramref name="eventsAvailable"/>,
        /// otherwise the list every 2 seconds. A safety reading every 30 seconds catches an event that was missed.
        /// </summary>
        public async Task StartAsync(bool eventsAvailable)
        {
            if (_started) return;
            _started = true;
            await RefreshAsync(initial: true);
            SetEventsAvailable(eventsAvailable);
            _ = Task.Run(() => _drivers(null)).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion) return;
                lock (_driverCache) foreach (var kv in t.Result) _driverCache[kv.Key] = kv.Value;
                _driversDirty = true;
                Post(() => _ = RefreshAsync(initial: false));
            }, TaskScheduler.Default);
        }

        /// <summary>Events available: a safety reading every 30 s; not available: the list is read every 2 s and compared.</summary>
        public void SetEventsAvailable(bool available)
        {
            if (!_started) return;
            var period = available ? PollWithEvents : PollWithoutEvents;
            Mode = available
                ? "Monitor PORNIT: conectările și deconectările apar singure, în câteva secunde (evenimente Windows)."
                : "Monitor PORNIT: evenimentele Windows nu sunt disponibile, așa că lista dispozitivelor se citește la 2 secunde și se compară.";
            _timer?.Dispose();
            _timer = new Timer(_ => Post(() => _ = RefreshAsync(initial: false)), null, period, period);
        }

        /// <summary>A Plug and Play device arrived or left (WMI event): one reading shortly after, for the whole burst of events of one device.</summary>
        public async void OnPnpEvent()
        {
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

        /// <summary>Reads the devices, merges them into the journal, updates the rows, raises the alerts and saves the journal when something changed.</summary>
        public async Task RefreshAsync(bool initial)
        {
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
                var diff = DeviceMonitorRules.Diff(_last, now);
                _last = now;
                if (!initial)
                {
                    // the driver of a device that just arrived (or whose problem code changed, e.g. after its driver was installed)
                    var ids = diff.Arrived.Concat(diff.Changed).Select(e => e.InstanceId).ToList();
                    if (ids.Count > 0)
                    {
                        var fresh = await Task.Run(() => _drivers(ids));
                        lock (_driverCache) foreach (var kv in fresh) _driverCache[kv.Key] = kv.Value;
                    }
                }
                Dictionary<string, SignedDriverInfo> drivers;
                lock (_driverCache) drivers = new(_driverCache, StringComparer.OrdinalIgnoreCase);
                int before = _journal.Devices.Count;
                var changes = DeviceJournalMerge.Apply(_journal, now, drivers, _clock(), initial);
                var alerts = new List<DeviceAlertRecord>();
                foreach (var c in changes.Where(c => c.Arrived))
                {
                    var decision = DeviceMonitorRules.Decide(c.Device.Class, c.Device.InstanceId, c.Device.RegistrationNumber, _isolated);
                    var a = new DeviceAlertRecord(_clock(), c.Device.Identity, c.Device.Name, c.Device.Class, decision.Severity, decision.Reason);
                    DeviceJournalMerge.AddAlert(_journal, a);
                    alerts.Add(a);
                }
                bool structural = initial || changes.Count > 0 || _journal.Devices.Count != before;
                foreach (var e in _journal.Devices)
                {
                    if (_rows.TryGetValue(e.Identity, out var row)) row.Update(e);
                    else _rows[e.Identity] = new DeviceMonitorRow(e, this);
                }
                if (structural || !diff.IsEmpty) ApplyFilter();
                if (structural || !diff.IsEmpty || _driversDirty) { _driversDirty = false; Save(); }
                UpdateSummary();
                foreach (var a in alerts) Alert?.Invoke(a);
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// Sets the registration number of a device: the sealed audit gets the old and the new value FIRST; when it cannot be written the change is refused (the
        /// row keeps the old value) and the reason is shown. Then the journal is saved. Returns whether the number was changed.
        /// </summary>
        public bool SetRegistrationNumber(DeviceMonitorRow row, string oldValue, string newValue)
        {
            if (_journal.Find(row.Identity) is not { } e) return false;
            var detail = OperationAudit.DeviceRegistrationChangedDetail(row.Identity, e.Class.ToString(), e.Name, oldValue, newValue);
            if (_audit(OperationAudit.DeviceRegistrationChanged, detail) is { } refused)
            {
                Problem = "Numărul de înregistrare nu s-a schimbat: jurnalul de audit nu poate fi scris — " + refused;
                return false;
            }
            DeviceJournalMerge.SetRegistrationNumber(_journal, row.Identity, newValue);
            Problem = "";
            Save();
            UpdateSummary();
            return true;
        }

        private void Save()
        {
            if (_save(_journal) is { } error) Problem = "Jurnalul monitorului nu a putut fi salvat: " + error;
        }

        private void UpdateSummary()
        {
            var all = _rows.Values.ToList();
            int connected = all.Count(r => r.Present), noNumber = all.Count(r => r.NeedsRegistration), driver = all.Count(r => r.DriverProblem);
            Summary = $"Conectate acum: {connected} · cunoscute de monitor: {all.Count}" +
                      (noNumber > 0 ? $" · {noNumber} {(noNumber == 1 ? "mediu de stocare" : "medii de stocare")} {DeviceMonitorReport.NoRegistration}" : "") +
                      (driver > 0 ? $" · {driver} fără driver sau cu eroare de driver" : "") + ".";
        }

        private bool Matches(DeviceMonitorRow r) => Filter switch
        {
            FilterExternal => !r.IsInternal,
            FilterConnected => r.Present,
            FilterStorage => r.IsStorageMedium,
            FilterNoNumber => r.NeedsRegistration,
            FilterDriver => r.DriverProblem,
            _ => true,
        };

        /// <summary>The rows in view: external devices before internal components, connected first, the most recently connected on top.</summary>
        private void ApplyFilter()
        {
            Rows.Clear();
            foreach (var r in _rows.Values.Where(Matches).OrderBy(r => r.IsInternal).ThenByDescending(r => r.Present)
                         .ThenByDescending(r => _journal.Find(r.Identity)?.LastConnectedUtc ?? DateTimeOffset.MinValue).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
                Rows.Add(r);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
            _debounce?.Cancel();
        }
    }
}
