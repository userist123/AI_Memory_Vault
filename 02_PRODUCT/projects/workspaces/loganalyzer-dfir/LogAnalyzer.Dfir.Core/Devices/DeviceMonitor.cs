using System.Globalization;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// The device classes the real-time device monitor shows (owner decision 2026-10-16, second round): storage media and externally connected devices only.
/// The members after <see cref="Other"/>-style legacy values (Hid, Audio, Controller, InternalDisk, VirtualDisk, Internal, Other) are kept only so that
/// rows an older journal holds can still be read; they are out of scope (see <see cref="DeviceScope"/>) and never shown or counted.
/// </summary>
public enum DeviceClass
{
    UsbStorage, MemoryCard, Phone, Bluetooth, WiFi, NetworkAdapter, Printer, Scanner, Camera, Optical, Keyboard, Mouse, Hid, Audio, Controller,
    InternalDisk, VirtualDisk, Internal, Other,
    ExternalDisk, Modem, SmartCardReader, CardReader,
    /// <summary>A USB Bluetooth dongle that is not part of the station (outside the local machine container): „Adaptor Bluetooth USB”.</summary>
    BluetoothAdapter,
}

/// <summary>How strongly a connection is announced: an information banner, or a critical alert with a sound.</summary>
public enum DeviceAlertSeverity { Info, Critical }

/// <summary>The four states a row shows (no driver columns): connected, disconnected, not working (no driver) and disabled.</summary>
public enum DeviceStatus { Connected, Disconnected, NotWorking, Disabled }

/// <summary>
/// One present Plug and Play device read now (SetupAPI). <see cref="ConfigManagerErrorCode"/> is the Configuration Manager problem code of the device,
/// the same number Win32_PnPEntity.ConfigManagerErrorCode reports: 0 = works, 28 = no driver installed, 22 = disabled; -1 = not read.
/// <see cref="ContainerId"/> is DEVPKEY_Device_ContainerId (all the nodes of one physical device share it; the local machine's own container is
/// <see cref="DeviceScope.LocalMachineContainer"/>, also used when DEVPKEY_Device_InLocalMachineContainer is true); "" = not read. <see cref="Parent"/> is the
/// instance id of the parent node (DEVPKEY_Device_Parent; a USB Attached SCSI disk hangs off a USB node whose service is UASPStor); "" = not read.
/// </summary>
public sealed record PnpSnapshotEntry(string InstanceId, string Name, string SetupClass, string Manufacturer, string HardwareIds, string Service, int ConfigManagerErrorCode,
    string ContainerId = "", string Parent = "");

/// <summary>
/// What is known about the medium in a drive: the volume serial, the capacity of the disk, the drive letters and the volume label, and the disk signature (MBR
/// signature, "" = not readable) that identifies a medium whose volume cannot be read (encrypted or RAW).
/// </summary>
public sealed record MediumInfo(string VolumeSerial, long CapacityBytes, string Letters, string Label, string DiskSignature = "");

/// <summary>What changed between two snapshots: devices that appeared, devices that are gone, devices whose name or problem code changed.</summary>
public sealed record SnapshotDiff(IReadOnlyList<PnpSnapshotEntry> Arrived, IReadOnlyList<PnpSnapshotEntry> Removed, IReadOnlyList<PnpSnapshotEntry> Changed)
{
    public bool IsEmpty => Arrived.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}

/// <summary>The alert raised for one connection: how strong, and why (in the controller's words).</summary>
public sealed record DeviceAlertDecision(DeviceAlertSeverity Severity, string Reason);

/// <summary>Pure rules of the device monitor: identity, the diff of two snapshots, the status and the alert decision. Scope and kind: <see cref="DeviceScope"/>.</summary>
public static class DeviceMonitorRules
{
    public static string ClassText(DeviceClass c) => c switch
    {
        DeviceClass.UsbStorage => "Stick USB",
        DeviceClass.MemoryCard => "Card de memorie",
        DeviceClass.ExternalDisk => "Disc extern",
        DeviceClass.Phone => "Telefon / tabletă",
        DeviceClass.Camera => "Cameră foto/video",
        DeviceClass.Scanner => "Scaner",
        DeviceClass.Printer => "Imprimantă",
        DeviceClass.NetworkAdapter => "Adaptor de rețea USB",
        DeviceClass.WiFi => "Adaptor Wi-Fi USB",
        DeviceClass.Modem => "Modem USB",
        DeviceClass.Bluetooth => "Dispozitiv Bluetooth",
        DeviceClass.Optical => "Unitate optică",
        DeviceClass.Keyboard => "Tastatură externă",
        DeviceClass.Mouse => "Mouse extern",
        DeviceClass.SmartCardReader => "Cititor de carduri smart",
        DeviceClass.CardReader => "Cititor de carduri",
        DeviceClass.BluetoothAdapter => "Adaptor Bluetooth USB",
        _ => "În afara domeniului (componentă internă)",
    };

    /// <summary>Storage media: the classes that carry a registration number („Nr. de înregistrare”) and are alerted without one.</summary>
    public static bool IsStorageMedium(DeviceClass c) => c is DeviceClass.UsbStorage or DeviceClass.MemoryCard or DeviceClass.ExternalDisk;

    /// <summary>The serial of a device from its instance id, only where the last segment is a real device serial (USB, USB storage, USB Attached SCSI).</summary>
    public static string Serial(string instanceId)
    {
        var id = instanceId.ToUpperInvariant();
        return id.StartsWith(@"USB\", StringComparison.Ordinal) || id.StartsWith(@"USBSTOR\", StringComparison.Ordinal) || id.StartsWith(@"SCSI\", StringComparison.Ordinal)
            ? DeviceRules.Serial(instanceId) : "";
    }

    /// <summary>
    /// A USB or USB-storage device whose instance id has no serial of its own: Windows generated the last segment (like 7&amp;1A2B3C4D&amp;0), so the device
    /// is told apart only by the port it sits in.
    /// </summary>
    public static bool HasNoUniqueSerial(string instanceId)
    {
        var id = instanceId.ToUpperInvariant();
        bool usb = id.StartsWith(@"USB\", StringComparison.Ordinal) || id.StartsWith(@"USBSTOR\", StringComparison.Ordinal);
        return usb && DeviceRules.Serial(instanceId).Length == 0;
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

    /// <summary>
    /// The identity of a device in the journal when several present devices report the same serial and VID/PID (a cloned serial): the plug-and-play instance
    /// id is added, so each one keeps its own row (and its own registration number) instead of sharing one.
    /// </summary>
    public static string IdentityWithInstance(PnpSnapshotEntry e) => Identity(e) + "|" + e.InstanceId.ToUpperInvariant();

    /// <summary>
    /// The identity of a medium in a drive: the drive's identity plus the volume serial and the capacity (one card = one row, however often it is inserted). Without
    /// a volume serial (an encrypted or RAW card) the disk signature is the key; without either the medium cannot be identified (see <see cref="CanIdentify"/>).
    /// </summary>
    public static string MediumIdentity(PnpSnapshotEntry drive, MediumInfo m) =>
        Identity(drive) + "|MEDIUM|" + (m.VolumeSerial.Length > 0 ? m.VolumeSerial.ToUpperInvariant() : "SIG-" + m.DiskSignature.ToUpperInvariant()) + "-" + m.CapacityBytes.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether a medium can be told apart from another card: it has a volume serial or a disk signature (a capacity alone is shared by many cards).</summary>
    public static bool CanIdentify(MediumInfo? m) => m is { } x && (x.VolumeSerial.Length > 0 || x.DiskSignature.Length > 0);

    /// <summary>The mark of a card whose medium could not be identified (no volume serial, no disk signature): each insertion is a row of its own.</summary>
    public const string UnidentifiableMediumText = "mediu neidentificabil (criptat/RAW)";

    /// <summary>
    /// Text of the critical alert raised for a device without a unique serial whose VID/PID matches a device with a serial present at the same time: Windows gives
    /// the second device with a duplicated serial an instance id generated from the port, so a cloned serial looks like a device without one.
    /// </summary>
    public const string PortCloneAlertText = "dispozitiv fără serie unică, cu același VID/PID ca un dispozitiv cu serie conectat acum (posibilă clonă)";

    /// <summary>Text of the informational alert raised when a device comes back with the same VID/PID, serial and capacity but another volume serial.</summary>
    public const string VolumeChangedAlertText = "volum diferit (reformatat sau clonă)";

    /// <summary>Whether an alert text is the „volum diferit” alert.</summary>
    public static bool IsVolumeChangedAlert(string text) => text.Contains(VolumeChangedAlertText, StringComparison.OrdinalIgnoreCase);

    /// <summary>Text of the critical alert raised when two present devices carry the same serial and VID/PID.</summary>
    public const string CloneAlertText = "aceeași serie pe două dispozitive (posibilă clonare)";

    /// <summary>Text of the critical alert raised when a serial comes back with another capacity or manufacturer (seen at different times).</summary>
    public const string DuplicateSerialAlertText = "serie duplicată – posibilă clonă (aceeași serie, altă capacitate sau alt producător)";

    /// <summary>The mark a row carries when its serial is held by two different devices.</summary>
    public const string DuplicateSerialMark = "serie duplicată – posibilă clonă";

    /// <summary>The Serie column text for a device whose serial Windows made up.</summary>
    public const string NoUniqueSerialText = "fără serie unică";

    /// <summary>Whether an alert text is a clone alert (simultaneous clones or a duplicate serial over time).</summary>
    public static bool IsCloneAlert(string text) => text.Contains("posibilă clon", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A card in a card reader or slot: the drive is a memory-card kind (the slot has the reader's serial for every card put in it), so each card is told apart
    /// by its own volume serial and capacity (see <see cref="MediumIdentity"/>).
    /// </summary>
    public static bool IsReaderSlot(DeviceClass c, string instanceId) =>
        c == DeviceClass.MemoryCard && (instanceId.StartsWith(@"USBSTOR", StringComparison.OrdinalIgnoreCase) || instanceId.StartsWith(@"SD", StringComparison.OrdinalIgnoreCase)
                                        || instanceId.StartsWith(@"MMC", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a volume is present for a disk (a card is in the reader): the volume's id carries the disk's id with "#" for the backslash.</summary>
    public static bool HasVolume(PnpSnapshotEntry disk, IEnumerable<PnpSnapshotEntry> present)
    {
        var key = disk.InstanceId.Replace('\\', '#').ToUpperInvariant();
        return present.Any(v => v.SetupClass.Equals("Volume", StringComparison.OrdinalIgnoreCase) && v.InstanceId.ToUpperInvariant().Contains(key, StringComparison.Ordinal));
    }

    /// <summary>
    /// Groups of present, in-scope devices that share a serial and VID/PID but are different plug-and-play devices (different instance ids): a possible clone.
    /// Media in a card slot are not compared (the slot's serial is the reader's).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ScopedDevice>> CloneGroups(IEnumerable<ScopedDevice> present)
    {
        var groups = new Dictionary<string, Dictionary<string, ScopedDevice>>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in present)
        {
            if (d.Kind == DeviceClass.MemoryCard) continue;
            if (Serial(d.IdentityEntry.InstanceId).Length == 0) continue;
            var key = Identity(d.IdentityEntry);
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new(StringComparer.OrdinalIgnoreCase);
            g.TryAdd(d.IdentityEntry.InstanceId, d);
        }
        return groups.Values.Where(g => g.Count > 1).Select(g => (IReadOnlyList<ScopedDevice>)g.Values.OrderBy(e => e.IdentityEntry.InstanceId, StringComparer.OrdinalIgnoreCase).ToList()).ToList();
    }

    /// <summary>
    /// Present devices without a unique serial (instance id generated from the port) whose kind and VID/PID match a present device WITH a serial: the second of two
    /// devices reporting the same serial is given a port-generated id by Windows, so this is a possible clone. Returns the instance ids of both sides (memory
    /// cards and card readers are not compared: the slot's serial is the reader's).
    /// </summary>
    public static (IReadOnlySet<string> NoSerial, IReadOnlySet<string> Serialled) PortGeneratedClones(IEnumerable<ScopedDevice> present)
    {
        var list = present.Where(d => d.Kind is not (DeviceClass.MemoryCard or DeviceClass.CardReader)).ToList();
        static string Vp(ScopedDevice d) => VidPid(d.IdentityEntry.InstanceId, d.IdentityEntry.HardwareIds).ToUpperInvariant();
        var withSerial = list.Where(d => Serial(d.IdentityEntry.InstanceId).Length > 0 && Vp(d).Length > 0).ToList();
        var noSerial = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var serialled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in list.Where(d => HasNoUniqueSerial(d.IdentityEntry.InstanceId) && Vp(d).Length > 0))
            foreach (var twin in withSerial.Where(t => t.Kind == d.Kind && Vp(t) == Vp(d)))
            {
                noSerial.Add(d.IdentityEntry.InstanceId);
                serialled.Add(twin.IdentityEntry.InstanceId);
            }
        return (noSerial, serialled);
    }

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
    /// The status a row shows: disconnected when the device is not present; disabled for problem code 22 (or 29, disabled in firmware); not working
    /// („fără driver”) for any other problem code (28 = the driver is not installed); connected otherwise.
    /// </summary>
    public static DeviceStatus Status(bool present, int configManagerErrorCode)
    {
        if (!present) return DeviceStatus.Disconnected;
        if (configManagerErrorCode is 22 or 29) return DeviceStatus.Disabled;
        return configManagerErrorCode > 0 ? DeviceStatus.NotWorking : DeviceStatus.Connected;
    }

    public static string StatusText(DeviceStatus s) => s switch
    {
        DeviceStatus.Connected => "Conectat",
        DeviceStatus.Disconnected => "Deconectat",
        DeviceStatus.NotWorking => "Nu funcționează (fără driver)",
        _ => "Dezactivat",
    };

    /// <summary>The meaning of the common Configuration Manager problem codes (shown in the tooltip of the status); others are named by number only.</summary>
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
    /// A Wi-Fi, Bluetooth or network adapter that Windows reports as disabled (problem code 22): switched off, which is what the isolated-station policy asks
    /// for (consistent with N03), so it is not a forbidden ACTIVE device.
    /// </summary>
    public static bool IsDisabledRadio(DeviceClass c, int configManagerErrorCode) =>
        configManagerErrorCode == 22 && c is DeviceClass.WiFi or DeviceClass.Bluetooth or DeviceClass.BluetoothAdapter or DeviceClass.NetworkAdapter or DeviceClass.Modem;

    /// <summary>
    /// The classes the station policy forbids on an isolated posture (Standalone and isolated LAN): phones (N05), Bluetooth (N04), Wi-Fi (N03) and network
    /// adapters connected by USB (N05: they give the station a way to another network). Removable storage is permitted only with a registration number,
    /// which <see cref="Decide"/> checks for every posture.
    /// </summary>
    public static bool ForbiddenOnIsolated(DeviceClass c, string instanceId) => c switch
    {
        DeviceClass.Phone or DeviceClass.Bluetooth or DeviceClass.BluetoothAdapter or DeviceClass.WiFi or DeviceClass.NetworkAdapter or DeviceClass.Modem => true,
        _ => false,
    };

    /// <summary>The ending of the critical alert of a class the policy forbids on an isolated station (the sealed alert is compared with it by the control).</summary>
    public const string ForbiddenClassAlertSuffix = "clasă interzisă pe o stație izolată";

    public static bool IsForbiddenClassAlert(string text) => text.Contains(ForbiddenClassAlertSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A radio or network kind: it can be switched on and off (problem code 22), so its "active" state is recorded.</summary>
    public static bool IsRadioKind(DeviceClass c) => c is DeviceClass.WiFi or DeviceClass.Bluetooth or DeviceClass.BluetoothAdapter or DeviceClass.NetworkAdapter or DeviceClass.Modem;

    /// <summary>
    /// The alert for a connection: CRITICAL for a storage medium without a registration number (every posture) and for a class the policy forbids on an
    /// isolated station; information otherwise.
    /// </summary>
    public static DeviceAlertDecision Decide(DeviceClass c, string instanceId, string registrationNumber, bool isolated, int configManagerErrorCode = 0)
    {
        if (IsStorageMedium(c) && registrationNumber.Trim().Length == 0)
            return new(DeviceAlertSeverity.Critical, "mediu de stocare fără număr de înregistrare");
        if (IsDisabledRadio(c, configManagerErrorCode))
            return new(DeviceAlertSeverity.Info, "dispozitiv dezactivat (cod 22), deci nu este activ");
        if (isolated && ForbiddenOnIsolated(c, instanceId))
            return new(DeviceAlertSeverity.Critical, $"{ClassText(c).ToLowerInvariant()}: {ForbiddenClassAlertSuffix}");
        return new(DeviceAlertSeverity.Info, "dispozitiv conectat");
    }

    // ---- radios that cannot be observed -----------------------------------------------------------------------------------------------------

    public const string BluetoothOffText = "Bluetooth oprit – dispozitivele Bluetooth nu pot fi observate";
    public const string BluetoothAbsentText = "Bluetooth absent – dispozitivele Bluetooth nu pot fi observate";
    public const string WifiOffText = "Wi-Fi oprit – dispozitivele Wi-Fi nu pot fi observate";

    /// <summary>
    /// The radios the monitor cannot see through, from one snapshot: the Bluetooth radio adapter is disabled (problem code 22) or there is none, or every Wi-Fi
    /// adapter is disabled. Returns (kind, text): never shown or reported as „no devices”.
    /// </summary>
    public static IReadOnlyList<(string Kind, string Text)> BlindRadios(IEnumerable<PnpSnapshotEntry> present)
    {
        var all = present as IReadOnlyList<PnpSnapshotEntry> ?? present.ToList();
        var res = new List<(string, string)>();
        var bt = all.Where(e => e.SetupClass.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase) && !IsRemoteBluetooth(e.InstanceId) && !IsBluetoothSoftware(e.InstanceId)).ToList();
        if (bt.Count == 0) res.Add(("Bluetooth", BluetoothAbsentText));
        else if (bt.All(e => e.ConfigManagerErrorCode is 22 or 29)) res.Add(("Bluetooth", BluetoothOffText));
        var wifi = all.Where(e => e.SetupClass.Equals("Net", StringComparison.OrdinalIgnoreCase) && DeviceScope.LooksWifi(e) && !DeviceScope.IsVirtualAdapter(e)).ToList();
        if (wifi.Count > 0 && wifi.All(e => e.ConfigManagerErrorCode is 22 or 29)) res.Add(("Wi-Fi", WifiOffText));
        return res;
    }

    private static bool IsRemoteBluetooth(string id) =>
        id.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"BTHLEDEVICE\", StringComparison.OrdinalIgnoreCase);

    private static bool IsBluetoothSoftware(string id) => id.StartsWith(@"BTH\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)
                                                            || id.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase);
}
