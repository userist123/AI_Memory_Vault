using System.Globalization;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// One device in the monitor's journal: what it is, when it was seen, who was logged on when it was connected and its registration number. Times are UTC, when
/// the monitor saw them. There are no driver fields: the status word („Nu funcționează (fără driver)”) comes from the problem code alone.
/// </summary>
public sealed class DeviceJournalEntry
{
    [JsonPropertyName("identity")] public string Identity { get; set; } = "";
    [JsonPropertyName("instance_id")] public string InstanceId { get; set; } = "";
    [JsonPropertyName("class")] public DeviceClass Class { get; set; } = DeviceClass.Other;
    [JsonPropertyName("setup_class")] public string SetupClass { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("manufacturer")] public string Manufacturer { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("vid_pid")] public string VidPid { get; set; } = "";
    [JsonPropertyName("serial")] public string Serial { get; set; } = "";
    /// <summary>The instance id carries no serial of its own (Windows made one up like 7&amp;1A2B&amp;0): shown as „fără serie unică”.</summary>
    [JsonPropertyName("no_unique_serial")] public bool NoUniqueSerial { get; set; }
    /// <summary>The same serial was held by another device (simultaneously, or with another capacity or manufacturer at another time): „serie duplicată – posibilă clonă”.</summary>
    [JsonPropertyName("duplicate_serial")] public bool DuplicateSerial { get; set; }
    [JsonPropertyName("config_manager_error_code")] public int ConfigManagerErrorCode { get; set; } = -1;
    [JsonPropertyName("present")] public bool Present { get; set; }
    [JsonPropertyName("first_seen_utc")] public DateTimeOffset FirstSeenUtc { get; set; }
    [JsonPropertyName("last_connected_utc")] public DateTimeOffset? LastConnectedUtc { get; set; }
    [JsonPropertyName("last_disconnected_utc")] public DateTimeOffset? LastDisconnectedUtc { get; set; }
    [JsonPropertyName("registration_number")] public string RegistrationNumber { get; set; } = "";
    /// <summary>The last time a radio or network adapter was seen enabled (not problem code 22); a device that is disabled now but was active in the period still counts.</summary>
    [JsonPropertyName("active_seen_utc")] public DateTimeOffset? ActiveSeenUtc { get; set; }
    /// <summary>Media: the capacity of the disk, the drive letters, the volume label and serial.</summary>
    [JsonPropertyName("capacity_bytes")] public long CapacityBytes { get; set; }
    [JsonPropertyName("volume_letters")] public string VolumeLetters { get; set; } = "";
    [JsonPropertyName("volume_label")] public string VolumeLabel { get; set; } = "";
    [JsonPropertyName("volume_serial")] public string VolumeSerial { get; set; } = "";
    /// <summary>The account of the active console session when the device was connected; empty = nobody was logged on (shown as „—”).</summary>
    [JsonPropertyName("connected_user")] public string ConnectedUser { get; set; } = "";

    public DeviceStatus Status => DeviceMonitorRules.Status(Present, ConfigManagerErrorCode);

    public DeviceJournalEntry Copy() => (DeviceJournalEntry)MemberwiseClone();
}

/// <summary>One alert the monitor raised (kept in the journal for the control report).</summary>
public sealed record DeviceAlertRecord(
    [property: JsonPropertyName("time_utc")] DateTimeOffset TimeUtc,
    [property: JsonPropertyName("identity")] string Identity,
    [property: JsonPropertyName("device")] string Device,
    [property: JsonPropertyName("class")] DeviceClass Class,
    [property: JsonPropertyName("severity")] DeviceAlertSeverity Severity,
    [property: JsonPropertyName("text")] string Text);

/// <summary>
/// One window in which the monitor was running: when it started, the last heartbeat it wrote and, after a clean stop, when it ended. A window without an end is
/// a monitor still running or one that died: its coverage ends at the last heartbeat. The journal keeps these only as a cross-check: the coverage the control
/// uses is computed from the SEALED records of the same events (<see cref="DeviceAuditReading.Runs"/>).
/// </summary>
public sealed class MonitorRun
{
    [JsonPropertyName("run_id")] public string RunId { get; set; } = "";
    [JsonPropertyName("start_utc")] public DateTimeOffset StartUtc { get; set; }
    [JsonPropertyName("last_beat_utc")] public DateTimeOffset LastBeatUtc { get; set; }
    [JsonPropertyName("end_utc")] public DateTimeOffset? EndUtc { get; set; }

    public MonitorRun Copy() => (MonitorRun)MemberwiseClone();
}

/// <summary>A radio the monitor cannot see through (Bluetooth off or absent, Wi-Fi disabled): when it was last seen blind, and whether it still is.</summary>
public sealed class RadioObservation
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("blind_now")] public bool BlindNow { get; set; }
    [JsonPropertyName("first_blind_utc")] public DateTimeOffset? FirstBlindUtc { get; set; }
    [JsonPropertyName("last_blind_utc")] public DateTimeOffset? LastBlindUtc { get; set; }

    public RadioObservation Copy() => (RadioObservation)MemberwiseClone();
}

/// <summary>The monitor's journal, persisted as JSON in the machine-wide folder so rows survive a restart.</summary>
public sealed class DeviceJournal
{
    public const int CurrentVersion = 2;
    [JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("devices")] public List<DeviceJournalEntry> Devices { get; set; } = [];
    [JsonPropertyName("alerts")] public List<DeviceAlertRecord> Alerts { get; set; } = [];

    /// <summary>Most informational alerts kept (the oldest are dropped first); critical alerts are never pushed out by them.</summary>
    public const int MaxInfoAlerts = 2000;

    /// <summary>Most critical alerts kept: a generous bound (they are the evidence of the control), the oldest dropped first only beyond it.</summary>
    public const int MaxCriticalAlerts = 10_000;

    /// <summary>The windows in which the monitor itself was running (start, heartbeat, stop): a cross-check of the sealed ones.</summary>
    [JsonPropertyName("runs")] public List<MonitorRun> Runs { get; set; } = [];

    /// <summary>The radios the monitor could not see through (by kind).</summary>
    [JsonPropertyName("radios")] public List<RadioObservation> Radios { get; set; } = [];

    /// <summary>Most run windows kept.</summary>
    public const int MaxRuns = 5000;

    public DeviceJournalEntry? Find(string identity) => Devices.FirstOrDefault(d => d.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase));

    public DeviceJournal Copy() => new()
    {
        Version = Version, Devices = Devices.Select(d => d.Copy()).ToList(), Alerts = [.. Alerts], Runs = Runs.Select(r => r.Copy()).ToList(),
        Radios = Radios.Select(r => r.Copy()).ToList(),
    };
}

/// <summary>One connection or disconnection the merge found; <see cref="CloneText"/> is the critical clone alert text when the row is a possible clone.</summary>
public sealed record DeviceJournalChange(DeviceJournalEntry Device, bool Arrived, string? CloneText = null);

/// <summary>Merges a snapshot of the present devices into the journal (pure: the caller reads the station and saves the journal).</summary>
public static class DeviceJournalMerge
{
    private static readonly IReadOnlyDictionary<string, MediumInfo> NoMedia = new Dictionary<string, MediumInfo>();

    /// <summary>
    /// Every in-scope device (<see cref="DeviceScope"/>) is added or updated (present, last connected and the user at the time when it was absent); every
    /// device of the journal that is not present any more is marked disconnected at <paramref name="nowUtc"/>. Registration numbers are kept by identity.
    /// Returns the connections and disconnections; on the first snapshot of a session (<paramref name="initial"/>) devices already present are not connections
    /// (they were there when the monitor started), only new ones are recorded as first seen.
    /// <list type="bullet">
    /// <item>Two present devices with the same serial and VID/PID (a cloned serial) keep separate rows, told apart by their instance id.</item>
    /// <item>A serial that returns with another capacity or manufacturer (seen at a different time) is a separate row marked „serie duplicată – posibilă clonă”.</item>
    /// <item>A card in a reader or slot is identified by the medium (volume serial + capacity), not by the reader: the same card is the same row however often it
    /// is inserted; an empty slot is not a medium.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<DeviceJournalChange> Apply(DeviceJournal journal, IEnumerable<PnpSnapshotEntry> present, IReadOnlyDictionary<string, MediumInfo>? media,
        string connectedUser, DateTimeOffset nowUtc, bool initial)
    {
        media ??= NoMedia;
        var all = present as IReadOnlyList<PnpSnapshotEntry> ?? present.ToList();
        var scoped = DeviceScope.Classify(all, media);
        var cloneInstances = new HashSet<string>(DeviceMonitorRules.CloneGroups(scoped).SelectMany(g => g).Select(d => d.IdentityEntry.InstanceId), StringComparer.OrdinalIgnoreCase);
        var changes = new List<DeviceJournalChange>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in scoped)
        {
            var p = d.IdentityEntry;
            var mi = d.Medium;
            var (ven, prod) = d.IsMedium ? DeviceRules.VendorProduct(p.InstanceId) : ("", "");
            var mfr = ven.Length > 0 ? ven : d.Primary.Manufacturer.Length > 0 ? d.Primary.Manufacturer : p.Manufacturer;
            string identity;
            bool duplicate = false;
            if (d.Kind == DeviceClass.MemoryCard)
            {
                var drive = d.Drive!;
                if (mi is { } m && (m.VolumeSerial.Length > 0 || m.CapacityBytes > 0)) identity = DeviceMonitorRules.MediumIdentity(drive, m);
                else
                {
                    // the medium could not be read: the insertion is its own row (never merged with another card), kept while it stays present
                    var baseId = DeviceMonitorRules.Identity(drive) + "|MEDIUM|T";
                    var open = initial ? null : journal.Devices.FirstOrDefault(x => x.Present && x.InstanceId.Equals(drive.InstanceId, StringComparison.OrdinalIgnoreCase)
                                                                                  && x.Identity.StartsWith(baseId, StringComparison.OrdinalIgnoreCase));
                    identity = open?.Identity ?? baseId + nowUtc.UtcTicks.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                identity = cloneInstances.Contains(p.InstanceId) ? DeviceMonitorRules.IdentityWithInstance(p) : DeviceMonitorRules.Identity(p);
                if (DeviceMonitorRules.Serial(p.InstanceId).Length > 0 && !cloneInstances.Contains(p.InstanceId))
                {
                    var candidates = journal.Devices.Where(x => x.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase)
                                                                || x.Identity.StartsWith(identity + "|DUP|", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (candidates.Count > 0)
                    {
                        long cap = mi?.CapacityBytes ?? 0;
                        var match = candidates.FirstOrDefault(x => SameDevice(x, cap, mfr));
                        if (match is not null) identity = match.Identity;
                        else
                        {
                            identity += "|DUP|" + candidates.Count.ToString(CultureInfo.InvariantCulture);
                            duplicate = true;
                            foreach (var c in candidates) c.DuplicateSerial = true;
                        }
                    }
                }
            }
            if (!seen.Add(identity)) continue;
            var e = journal.Find(identity);
            bool isNew = e is null;
            if (e is null)
            {
                e = new DeviceJournalEntry { Identity = identity, FirstSeenUtc = nowUtc, ConnectedUser = connectedUser };
                journal.Devices.Add(e);
            }
            if (duplicate || cloneInstances.Contains(p.InstanceId)) e.DuplicateSerial = true;
            e.InstanceId = p.InstanceId;
            e.Class = d.Kind;
            e.SetupClass = d.Primary.SetupClass;
            var name = d.Primary.Name.Length > 0 ? d.Primary.Name : p.Name;
            if (name.Length > 0) e.Name = name;
            if (mfr.Length > 0) e.Manufacturer = mfr;
            if (prod.Length > 0) e.Model = prod;
            e.Serial = DeviceMonitorRules.Serial(p.InstanceId);
            e.NoUniqueSerial = DeviceMonitorRules.HasNoUniqueSerial(p.InstanceId);
            e.VidPid = DeviceMonitorRules.VidPid(p.InstanceId, p.HardwareIds);
            e.ConfigManagerErrorCode = d.Primary.ConfigManagerErrorCode > 0 ? d.Primary.ConfigManagerErrorCode : p.ConfigManagerErrorCode;
            if (mi is { } mm)
            {
                if (mm.CapacityBytes > 0) e.CapacityBytes = mm.CapacityBytes;
                e.VolumeLetters = mm.Letters;
                e.VolumeLabel = mm.Label;
                e.VolumeSerial = mm.VolumeSerial;
            }
            if (d.Kind is DeviceClass.WiFi or DeviceClass.Bluetooth or DeviceClass.NetworkAdapter or DeviceClass.Modem && !DeviceMonitorRules.IsDisabledRadio(d.Kind, e.ConfigManagerErrorCode)) e.ActiveSeenUtc = nowUtc;
            string? cloneText = duplicate ? DeviceMonitorRules.DuplicateSerialAlertText : cloneInstances.Contains(p.InstanceId) && (isNew || !e.Present) ? DeviceMonitorRules.CloneAlertText : null;
            if (!e.Present)
            {
                e.Present = true;
                e.LastConnectedUtc = nowUtc;
                e.ConnectedUser = connectedUser;
                if (!initial || cloneText is not null) changes.Add(new(e, true, cloneText));
            }
        }
        foreach (var e in journal.Devices.Where(e => e.Present && !seen.Contains(e.Identity)))
        {
            e.Present = false;
            e.LastDisconnectedUtc = nowUtc;
            if (DeviceScope.IsInScope(e)) changes.Add(new(e, false));
        }
        return changes;
    }

    private static bool SameDevice(DeviceJournalEntry x, long capacity, string manufacturer)
    {
        if (x.CapacityBytes > 0 && capacity > 0 && x.CapacityBytes != capacity) return false;
        if (x.Manufacturer.Length > 0 && manufacturer.Length > 0 && !x.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>Sets the registration number of a device; returns the previous value, or null when the identity is not in the journal.</summary>
    public static string? SetRegistrationNumber(DeviceJournal journal, string identity, string number)
    {
        if (journal.Find(identity) is not { } e) return null;
        var old = e.RegistrationNumber;
        e.RegistrationNumber = number.Trim();
        return old;
    }

    /// <summary>
    /// Appends an alert. Informational alerts are capped at <see cref="DeviceJournal.MaxInfoAlerts"/> (the oldest dropped); critical alerts are kept up to a
    /// generous <see cref="DeviceJournal.MaxCriticalAlerts"/>, so a flood of connections never pushes the evidence of a critical one out.
    /// </summary>
    public static void AddAlert(DeviceJournal journal, DeviceAlertRecord alert)
    {
        journal.Alerts.Add(alert);
        bool critical = alert.Severity == DeviceAlertSeverity.Critical;
        int limit = critical ? DeviceJournal.MaxCriticalAlerts : DeviceJournal.MaxInfoAlerts;
        int count = journal.Alerts.Count(a => (a.Severity == DeviceAlertSeverity.Critical) == critical);
        while (count-- > limit) journal.Alerts.RemoveAt(journal.Alerts.FindIndex(a => (a.Severity == DeviceAlertSeverity.Critical) == critical));
    }

    /// <summary>Notes the radios the monitor cannot see through now (from <see cref="DeviceMonitorRules.BlindRadios"/>): a radio that is back is kept with the last time it was blind.</summary>
    public static void NoteRadios(DeviceJournal journal, IReadOnlyList<(string Kind, string Text)> blind, DateTimeOffset nowUtc)
    {
        foreach (var r in journal.Radios) r.BlindNow = false;
        foreach (var (kind, text) in blind)
        {
            var r = journal.Radios.FirstOrDefault(x => x.Kind == kind);
            if (r is null) journal.Radios.Add(r = new RadioObservation { Kind = kind, FirstBlindUtc = nowUtc });
            r.Text = text; r.BlindNow = true; r.LastBlindUtc = nowUtc;
        }
    }

    // ---- the monitor's own running windows (journal copy; the control uses the sealed records) ---------------------------------------------------

    /// <summary>How often the heartbeat of the running monitor is written (to the sealed audit and the journal).</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The monitor starts: a window left open by a monitor that died is closed at its last heartbeat (that is as far as it is known to have watched), and a new
    /// window opens.
    /// </summary>
    public static void StartRun(DeviceJournal journal, string runId, DateTimeOffset nowUtc)
    {
        foreach (var r in journal.Runs.Where(r => r.EndUtc is null)) r.EndUtc = r.LastBeatUtc < r.StartUtc ? r.StartUtc : r.LastBeatUtc;
        journal.Runs.Add(new MonitorRun { RunId = runId, StartUtc = nowUtc, LastBeatUtc = nowUtc });
        if (journal.Runs.Count > DeviceJournal.MaxRuns) journal.Runs.RemoveRange(0, journal.Runs.Count - DeviceJournal.MaxRuns);
    }

    /// <summary>The running monitor is alive at <paramref name="nowUtc"/>.</summary>
    public static void Heartbeat(DeviceJournal journal, DateTimeOffset nowUtc)
    {
        if (journal.Runs.LastOrDefault() is { EndUtc: null } r && nowUtc > r.LastBeatUtc) r.LastBeatUtc = nowUtc;
    }

    /// <summary>The monitor stops cleanly.</summary>
    public static void StopRun(DeviceJournal journal, DateTimeOffset nowUtc)
    {
        if (journal.Runs.LastOrDefault() is not { EndUtc: null } r) return;
        if (nowUtc > r.LastBeatUtc) r.LastBeatUtc = nowUtc;
        r.EndUtc = r.LastBeatUtc;
    }
}
