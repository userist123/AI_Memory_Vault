using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// The device classes of the real-time device monitor (owner decision 2026-10-16, modelled on Endpoint Protector's device control view): every Plug and
/// Play device of the station, not only storage. Observe and alert only; the monitor never blocks or changes anything.
/// </summary>
public enum DeviceClass
{
    UsbStorage, MemoryCard, Phone, Bluetooth, WiFi, NetworkAdapter, Printer, Scanner, Camera, Optical, Keyboard, Mouse, Hid, Audio, Controller,
    InternalDisk, VirtualDisk, Internal, Other,
}

/// <summary>Whether Windows has a working driver for the device.</summary>
public enum DriverState { Installed, Unsigned, Missing, Error, Unknown }

/// <summary>How strongly a connection is announced: an information banner, or a critical alert with a sound.</summary>
public enum DeviceAlertSeverity { Info, Critical }

/// <summary>
/// One present Plug and Play device read now (SetupAPI). <see cref="ConfigManagerErrorCode"/> is the Configuration Manager problem code of the device,
/// the same number Win32_PnPEntity.ConfigManagerErrorCode reports: 0 = works, 28 = no driver installed, any other value = an error; -1 = not read.
/// </summary>
public sealed record PnpSnapshotEntry(string InstanceId, string Name, string SetupClass, string Manufacturer, string HardwareIds, string Service, int ConfigManagerErrorCode);

/// <summary>The driver Windows installed for a device, from Win32_PnPSignedDriver (DriverVersion, DriverProviderName, DriverDate, IsSigned, Signer).</summary>
public sealed record SignedDriverInfo(string DriverVersion, string DriverProviderName, string DriverDate, bool? IsSigned, string Signer);

/// <summary>What changed between two snapshots: devices that appeared, devices that are gone, devices whose name or problem code changed.</summary>
public sealed record SnapshotDiff(IReadOnlyList<PnpSnapshotEntry> Arrived, IReadOnlyList<PnpSnapshotEntry> Removed, IReadOnlyList<PnpSnapshotEntry> Changed)
{
    public bool IsEmpty => Arrived.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}

/// <summary>The alert raised for one connection: how strong, and why (in the controller's words).</summary>
public sealed record DeviceAlertDecision(DeviceAlertSeverity Severity, string Reason);

/// <summary>Pure rules of the device monitor: class of a device, its stable identity, the diff of two snapshots, the driver state and the alert decision.</summary>
public static class DeviceMonitorRules
{
    /// <summary>
    /// The class of a Plug and Play device from its instance id, setup class, hardware ids and name; null for entries that only repeat a device already listed
    /// (logical volumes, the portable-device view of a disk). Storage classes reuse <see cref="DeviceRules.Classify"/> so the media page and the monitor agree.
    /// </summary>
    public static DeviceClass? Classify(string instanceId, string setupClass, string hardwareIds, string name)
    {
        var id = instanceId.ToUpperInvariant();
        var cls = setupClass.ToUpperInvariant();
        var n = name.ToUpperInvariant();
        if (id.Length == 0) return null;
        if (cls is "VOLUME" or "VOLUMESNAPSHOT") return null;                           // a volume of a disk already listed
        if (cls == "WPD" && id.StartsWith(@"SWD\WPDBUSENUM\", StringComparison.Ordinal)) return null;   // the portable-device view of a disk already listed
        if (cls is "DISKDRIVE" or "CDROM" || id.StartsWith(@"USBSTOR\", StringComparison.Ordinal) || id.StartsWith(@"SD\", StringComparison.Ordinal)
            || id.StartsWith(@"SDBUS\", StringComparison.Ordinal) || id.StartsWith(@"MMC\", StringComparison.Ordinal))
            return DeviceRules.Classify(instanceId, setupClass, hardwareIds, name) switch
            {
                DeviceKind.UsbStorage => DeviceClass.UsbStorage,
                DeviceKind.MemoryCard => DeviceClass.MemoryCard,
                DeviceKind.Optical => DeviceClass.Optical,
                DeviceKind.VirtualDisk => DeviceClass.VirtualDisk,
                DeviceKind.InternalDisk => DeviceClass.InternalDisk,
                _ => cls == "SDHOST" ? DeviceClass.Internal : DeviceClass.Other,
            };
        bool Software() => id.StartsWith(@"ROOT\", StringComparison.Ordinal) || id.StartsWith(@"SWD\", StringComparison.Ordinal) || id.StartsWith(@"SW\", StringComparison.Ordinal);
        switch (cls)
        {
            case "WPD": return DeviceClass.Phone;
            case "BLUETOOTH": return DeviceClass.Bluetooth;
            case "NET":
                if (id.StartsWith("BTH", StringComparison.Ordinal)) return DeviceClass.Bluetooth;                 // Bluetooth personal area network
                if (Software() || n.Contains("WAN MINIPORT") || n.Contains("KERNEL DEBUG") || n.Contains("VIRTUAL") || n.Contains("LOOPBACK")) return DeviceClass.Internal;
                return n.Contains("WI-FI") || n.Contains("WIFI") || n.Contains("WIRELESS") || n.Contains("WLAN") || n.Contains("802.11") ? DeviceClass.WiFi : DeviceClass.NetworkAdapter;
            case "PRINTER":
            case "PRINTQUEUE":
                // the queues Windows creates for itself (print to PDF / XPS, OneNote, fax) are not printers
                return n.Contains("PDF") || n.Contains("XPS") || n.Contains("ONENOTE") || n.Contains("FAX") || n.Contains("ROOT PRINT QUEUE") ? DeviceClass.Internal : DeviceClass.Printer;
            case "IMAGE": return n.Contains("CAM") ? DeviceClass.Camera : DeviceClass.Scanner;
            case "CAMERA": return DeviceClass.Camera;
            case "KEYBOARD": return DeviceClass.Keyboard;
            case "MOUSE": return DeviceClass.Mouse;
            case "HIDCLASS": return n.Contains("GAME CONTROLLER") || n.Contains("GAMEPAD") || n.Contains("JOYSTICK") ? DeviceClass.Controller : DeviceClass.Hid;
            case "XNACOMPOSITE": return DeviceClass.Controller;
            case "MEDIA":
            case "AUDIOENDPOINT": return DeviceClass.Audio;
            case "USB":
            case "USBDEVICE": return DeviceClass.Controller;
        }
        if (id.StartsWith(@"BTHENUM\", StringComparison.Ordinal) || id.StartsWith("BTHLE", StringComparison.Ordinal)) return DeviceClass.Bluetooth;
        if (id.StartsWith(@"USB\", StringComparison.Ordinal)) return DeviceClass.Other;
        return cls.Length > 0 || Software() || id.StartsWith(@"ACPI\", StringComparison.Ordinal) || id.StartsWith(@"PCI\", StringComparison.Ordinal)
            ? DeviceClass.Internal : DeviceClass.Other;
    }

    public static string ClassText(DeviceClass c) => c switch
    {
        DeviceClass.UsbStorage => "Stocare USB (stick / disc extern)",
        DeviceClass.MemoryCard => "Card de memorie",
        DeviceClass.Phone => "Telefon / dispozitiv portabil (MTP)",
        DeviceClass.Bluetooth => "Bluetooth",
        DeviceClass.WiFi => "Adaptor Wi-Fi",
        DeviceClass.NetworkAdapter => "Adaptor de rețea",
        DeviceClass.Printer => "Imprimantă",
        DeviceClass.Scanner => "Scaner / dispozitiv de imagine",
        DeviceClass.Camera => "Cameră",
        DeviceClass.Optical => "Unitate CD/DVD",
        DeviceClass.Keyboard => "Tastatură",
        DeviceClass.Mouse => "Mouse",
        DeviceClass.Hid => "Dispozitiv de intrare (HID)",
        DeviceClass.Audio => "Audio",
        DeviceClass.Controller => "Controler (USB, hub sau de joc)",
        DeviceClass.InternalDisk => "Disc intern",
        DeviceClass.VirtualDisk => "Disc virtual (VHD/ISO)",
        DeviceClass.Internal => "Componentă internă",
        _ => "Alt dispozitiv",
    };

    /// <summary>Storage media: the classes that carry a registration number („Nr. de înregistrare”) and are alerted without one.</summary>
    public static bool IsStorageMedium(DeviceClass c) => c is DeviceClass.UsbStorage or DeviceClass.MemoryCard;

    /// <summary>The serial of a device from its instance id, only where the last segment is a real device serial (USB, USB storage, USB Attached SCSI).</summary>
    public static string Serial(string instanceId)
    {
        var id = instanceId.ToUpperInvariant();
        return id.StartsWith(@"USB\", StringComparison.Ordinal) || id.StartsWith(@"USBSTOR\", StringComparison.Ordinal) || id.StartsWith(@"SCSI\", StringComparison.Ordinal)
            ? DeviceRules.Serial(instanceId) : "";
    }

    /// <summary>VID:PID from the instance id or the hardware ids; for USB storage (which carries vendor and product names instead) "Ven Prod".</summary>
    public static string VidPid(string instanceId, string hardwareIds)
    {
        var v = DeviceRules.VidPid(instanceId + " " + hardwareIds);
        if (v.Length > 0) return v;
        var (ven, prod) = DeviceRules.VendorProduct(instanceId);
        return ven.Length + prod.Length > 0 ? $"{ven} {prod}".Trim() : "";
    }

    /// <summary>
    /// The stable identity a device is remembered by across connections and restarts: serial + VID/PID when the device reports a unique serial (the same stick
    /// on another port is the same device), otherwise the instance id (a device without a serial is told apart only by where it is connected).
    /// </summary>
    public static string Identity(string instanceId, string serial, string vidPid) =>
        serial.Length > 0 ? $"SN|{vidPid.ToUpperInvariant()}|{serial.ToUpperInvariant()}" : "ID|" + instanceId.ToUpperInvariant();

    public static string Identity(PnpSnapshotEntry e) => Identity(e.InstanceId, Serial(e.InstanceId), VidPid(e.InstanceId, e.HardwareIds));

    /// <summary>Arrivals, removals and changes between two snapshots, by Plug and Play device id (case-insensitive).</summary>
    public static SnapshotDiff Diff(IEnumerable<PnpSnapshotEntry> previous, IEnumerable<PnpSnapshotEntry> current)
    {
        var before = new Dictionary<string, PnpSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in previous) before.TryAdd(e.InstanceId, e);
        var now = new Dictionary<string, PnpSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in current) now.TryAdd(e.InstanceId, e);
        var arrived = now.Values.Where(e => !before.ContainsKey(e.InstanceId)).ToList();
        var removed = before.Values.Where(e => !now.ContainsKey(e.InstanceId)).ToList();
        var changed = now.Values.Where(e => before.TryGetValue(e.InstanceId, out var b) && (b.ConfigManagerErrorCode != e.ConfigManagerErrorCode || b.Name != e.Name)).ToList();
        return new SnapshotDiff(arrived, removed, changed);
    }

    /// <summary>
    /// The driver state from the Configuration Manager problem code (Win32_PnPEntity.ConfigManagerErrorCode: 28 = no driver, other non-zero = error) and the
    /// signed-driver data (Win32_PnPSignedDriver). A working device with an unsigned driver is <see cref="DriverState.Unsigned"/>.
    /// </summary>
    public static (DriverState State, string Text) Driver(int configManagerErrorCode, SignedDriverInfo? d)
    {
        if (configManagerErrorCode == 28) return (DriverState.Missing, "fără driver (cod 28: driverul nu este instalat)");
        if (configManagerErrorCode > 0) return (DriverState.Error, $"eroare (cod {configManagerErrorCode}: {ProblemText(configManagerErrorCode)})");
        if (configManagerErrorCode < 0) return (DriverState.Unknown, "starea driverului nu a putut fi citită");
        if (d is null || d.DriverProviderName.Length + d.DriverVersion.Length == 0)
            return (DriverState.Installed, "funcționează (detaliile driverului nu sunt disponibile)");
        var who = $"{d.DriverProviderName} {d.DriverVersion}".Trim() + (d.DriverDate.Length > 0 ? $" din {d.DriverDate}" : "");
        if (d.IsSigned == false) return (DriverState.Unsigned, $"driver NESEMNAT: {who}");
        return (DriverState.Installed, $"driver instalat: {who}" + (d.Signer.Length > 0 ? $", semnat de {d.Signer}" : d.IsSigned == true ? ", semnat" : ""));
    }

    /// <summary>The meaning of the common Configuration Manager problem codes; others are named by number only.</summary>
    public static string ProblemText(int code) => code switch
    {
        1 => "dispozitivul nu este configurat corect",
        3 => "driverul este deteriorat sau lipsește memorie",
        10 => "dispozitivul nu poate porni",
        12 => "nu are resurse libere",
        14 => "cere repornirea stației",
        18 => "driverele trebuie reinstalate",
        19 => "configurația din registru este deteriorată",
        21 => "Windows îl elimină",
        22 => "dispozitivul este dezactivat",
        24 => "dispozitivul lipsește sau nu funcționează",
        28 => "driverul nu este instalat",
        29 => "dezactivat din firmware",
        31 => "driverul nu s-a putut încărca",
        37 => "driverul a întors o eroare",
        39 => "driverul lipsește sau este deteriorat",
        43 => "Windows l-a oprit pentru că a raportat probleme",
        45 => "dispozitivul nu este conectat acum",
        47 => "pregătit pentru scoatere în siguranță",
        48 => "driverul este blocat de Windows",
        52 => "semnătura driverului nu poate fi verificată",
        _ => "problemă raportată de Windows",
    };

    /// <summary>
    /// The classes the station policy forbids on an isolated posture (Standalone and isolated LAN): phones (N05), Bluetooth (N04), Wi-Fi (N03) and network
    /// adapters connected by USB (N05: they give the station a way to another network). A built-in wired adapter is not a connection by itself (N01 judges
    /// the connections). Removable storage is permitted only with a registration number, which <see cref="Decide"/> checks for every posture.
    /// </summary>
    public static bool ForbiddenOnIsolated(DeviceClass c, string instanceId) => c switch
    {
        DeviceClass.Phone or DeviceClass.Bluetooth or DeviceClass.WiFi => true,
        DeviceClass.NetworkAdapter => instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>
    /// The alert for a connection: CRITICAL for a storage medium without a registration number (every posture) and for a class the policy forbids on an
    /// isolated station; information otherwise.
    /// </summary>
    public static DeviceAlertDecision Decide(DeviceClass c, string instanceId, string registrationNumber, bool isolated)
    {
        if (IsStorageMedium(c) && registrationNumber.Trim().Length == 0)
            return new(DeviceAlertSeverity.Critical, "mediu de stocare fără număr de înregistrare");
        if (isolated && ForbiddenOnIsolated(c, instanceId))
            return new(DeviceAlertSeverity.Critical, $"{ClassText(c).ToLowerInvariant()}: clasă interzisă pe o stație izolată");
        return new(DeviceAlertSeverity.Info, "dispozitiv conectat");
    }
}

/// <summary>One device in the monitor's journal: what it is, its driver, when it was seen and its registration number. Times are UTC, when the monitor saw them.</summary>
public sealed class DeviceJournalEntry
{
    [JsonPropertyName("identity")] public string Identity { get; set; } = "";
    [JsonPropertyName("instance_id")] public string InstanceId { get; set; } = "";
    [JsonPropertyName("class")] public DeviceClass Class { get; set; } = DeviceClass.Other;
    [JsonPropertyName("setup_class")] public string SetupClass { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("manufacturer")] public string Manufacturer { get; set; } = "";
    [JsonPropertyName("vid_pid")] public string VidPid { get; set; } = "";
    [JsonPropertyName("serial")] public string Serial { get; set; } = "";
    [JsonPropertyName("config_manager_error_code")] public int ConfigManagerErrorCode { get; set; } = -1;
    [JsonPropertyName("driver_state")] public DriverState DriverState { get; set; } = DriverState.Unknown;
    [JsonPropertyName("driver_text")] public string DriverText { get; set; } = "";
    [JsonPropertyName("driver_version")] public string DriverVersion { get; set; } = "";
    [JsonPropertyName("driver_provider")] public string DriverProvider { get; set; } = "";
    [JsonPropertyName("driver_date")] public string DriverDate { get; set; } = "";
    [JsonPropertyName("driver_signed")] public bool? DriverSigned { get; set; }
    [JsonPropertyName("driver_signer")] public string DriverSigner { get; set; } = "";
    [JsonPropertyName("present")] public bool Present { get; set; }
    [JsonPropertyName("first_seen_utc")] public DateTimeOffset FirstSeenUtc { get; set; }
    [JsonPropertyName("last_connected_utc")] public DateTimeOffset? LastConnectedUtc { get; set; }
    [JsonPropertyName("last_disconnected_utc")] public DateTimeOffset? LastDisconnectedUtc { get; set; }
    [JsonPropertyName("registration_number")] public string RegistrationNumber { get; set; } = "";

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

/// <summary>The monitor's journal, persisted as JSON under the application data folder so rows survive a restart.</summary>
public sealed class DeviceJournal
{
    public const int CurrentVersion = 1;
    [JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("devices")] public List<DeviceJournalEntry> Devices { get; set; } = [];
    [JsonPropertyName("alerts")] public List<DeviceAlertRecord> Alerts { get; set; } = [];

    /// <summary>Most alerts kept: the oldest are dropped first.</summary>
    public const int MaxAlerts = 2000;

    public DeviceJournalEntry? Find(string identity) => Devices.FirstOrDefault(d => d.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase));

    public DeviceJournal Copy() => new() { Version = Version, Devices = Devices.Select(d => d.Copy()).ToList(), Alerts = [.. Alerts] };
}

/// <summary>One connection or disconnection the merge found.</summary>
public sealed record DeviceJournalChange(DeviceJournalEntry Device, bool Arrived);

/// <summary>Merges a snapshot of the present devices into the journal (pure: the caller reads the station and saves the journal).</summary>
public static class DeviceJournalMerge
{
    /// <summary>
    /// Every present device is added or updated (present, last connected when it was absent); every device of the journal that is not present any more is
    /// marked disconnected at <paramref name="nowUtc"/>. Registration numbers are kept by identity. Returns the connections and disconnections; on the first
    /// snapshot of a session (<paramref name="initial"/>) devices already present are not connections (they were there when the monitor started), only new
    /// ones are recorded as first seen.
    /// </summary>
    public static IReadOnlyList<DeviceJournalChange> Apply(DeviceJournal journal, IEnumerable<PnpSnapshotEntry> present, IReadOnlyDictionary<string, SignedDriverInfo> drivers,
        DateTimeOffset nowUtc, bool initial)
    {
        var changes = new List<DeviceJournalChange>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in present)
        {
            if (DeviceMonitorRules.Classify(p.InstanceId, p.SetupClass, p.HardwareIds, p.Name) is not { } cls) continue;
            var identity = DeviceMonitorRules.Identity(p);
            if (!seen.Add(identity)) continue;
            var e = journal.Find(identity);
            if (e is null)
            {
                e = new DeviceJournalEntry { Identity = identity, FirstSeenUtc = nowUtc };
                journal.Devices.Add(e);
            }
            e.InstanceId = p.InstanceId;
            e.Class = cls;
            e.SetupClass = p.SetupClass;
            if (p.Name.Length > 0) e.Name = p.Name;
            if (p.Manufacturer.Length > 0) e.Manufacturer = p.Manufacturer;
            e.Serial = DeviceMonitorRules.Serial(p.InstanceId);
            e.VidPid = DeviceMonitorRules.VidPid(p.InstanceId, p.HardwareIds);
            e.ConfigManagerErrorCode = p.ConfigManagerErrorCode;
            drivers.TryGetValue(p.InstanceId, out var d);
            if (d is not null)
            {
                e.DriverVersion = d.DriverVersion; e.DriverProvider = d.DriverProviderName; e.DriverDate = d.DriverDate; e.DriverSigned = d.IsSigned; e.DriverSigner = d.Signer;
            }
            var known = d ?? (e.DriverProvider.Length + e.DriverVersion.Length > 0 ? new SignedDriverInfo(e.DriverVersion, e.DriverProvider, e.DriverDate, e.DriverSigned, e.DriverSigner) : null);
            (e.DriverState, e.DriverText) = DeviceMonitorRules.Driver(p.ConfigManagerErrorCode, known);
            if (!e.Present)
            {
                e.Present = true;
                e.LastConnectedUtc = nowUtc;
                if (!initial) changes.Add(new(e, true));
            }
        }
        foreach (var e in journal.Devices.Where(e => e.Present && !seen.Contains(e.Identity)))
        {
            e.Present = false;
            e.LastDisconnectedUtc = nowUtc;
            changes.Add(new(e, false));
        }
        return changes;
    }

    /// <summary>Sets the registration number of a device; returns the previous value, or null when the identity is not in the journal.</summary>
    public static string? SetRegistrationNumber(DeviceJournal journal, string identity, string number)
    {
        if (journal.Find(identity) is not { } e) return null;
        var old = e.RegistrationNumber;
        e.RegistrationNumber = number.Trim();
        return old;
    }

    /// <summary>Appends an alert, keeping at most <see cref="DeviceJournal.MaxAlerts"/>.</summary>
    public static void AddAlert(DeviceJournal journal, DeviceAlertRecord alert)
    {
        journal.Alerts.Add(alert);
        if (journal.Alerts.Count > DeviceJournal.MaxAlerts) journal.Alerts.RemoveRange(0, journal.Alerts.Count - DeviceJournal.MaxAlerts);
    }
}
