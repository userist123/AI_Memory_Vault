using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// One physical device (or one medium) in scope of the monitor: its kind, the node the name comes from (<see cref="Primary"/>), the node its serial and VID/PID
/// come from (<see cref="IdentityEntry"/>, the USB-level node of a phone or a card reader), every node of the physical device and, for a medium, the drive node.
/// </summary>
public sealed record ScopedDevice(DeviceClass Kind, PnpSnapshotEntry Primary, PnpSnapshotEntry IdentityEntry, IReadOnlyList<PnpSnapshotEntry> Members, PnpSnapshotEntry? Drive, MediumInfo? Medium)
{
    /// <summary>
    /// The kind the FIRST classifier (version 1) gave this device: with a keyboard and a mouse node and nothing of higher priority, the keyboard (priority order), where
    /// version 2 takes the kind of the first interface. Used to check that a kind change is exactly what the classifier change makes (<see cref="DeviceScope.IsKnownReclassification"/>).
    /// </summary>
    public DeviceClass LegacyV1Kind { get; init; } = Kind;

    /// <summary>The kind the classifier of <paramref name="version"/> gives this device now (a version above the first gives <see cref="Kind"/>).</summary>
    public DeviceClass KindAtVersion(int version) => version <= 1 ? LegacyV1Kind : version == 2 ? LegacyV2Kind : Kind;

    /// <summary>The kind the classifier of version 2 gave this device: a keyboard or mouse that hangs off Bluetooth was a "Tastatura externa" / "Mouse extern" then, a "Dispozitiv Bluetooth" since version 3.</summary>
    public DeviceClass LegacyV2Kind { get; init; } = Kind;

    /// <summary>For a keyboard or mouse seen through Bluetooth: the inner function ("tastatura prin Bluetooth", "mouse prin Bluetooth"); "" otherwise.</summary>
    public string BluetoothFunction { get; init; } = "";

    /// <summary>A storage medium (stick, disk, card, optical drive): the row is the drive node itself.</summary>
    public bool IsMedium => Drive is not null;
}

/// <summary>
/// THE scope of the device monitor (owner decision 2026-10-16, after the first test: the list showed every driver and internal component): storage media and
/// externally connected devices only. Pure and unit-tested; the live monitor page, check D05 and report section 3c all use it, so they list the same devices.
/// <list type="bullet">
/// <item>In scope: stick, memory card, external disk, phone / tablet (MTP, PTP, ADB), camera, scanner, printer, USB network / Wi-Fi / modem adapter, Bluetooth
/// device, optical drive, external keyboard, external mouse, smart card reader, card reader (its inserted medium is a row of its own).</item>
/// <item>Out of scope: hubs, host controllers, composite parents, HID child collections (collapsed into the device), software and virtual devices (ROOT, SWD
/// except portable devices, Hyper-V / VPN / TAP adapters), ACPI and system devices, internal disks, integrated card readers (the media in them still count),
/// the Bluetooth radio adapter itself, integrated webcams, keyboards and touchpads.</item>
/// </list>
/// Internal or external: the container id of the device (all nodes of one physical device share it; the local machine's own container means a component of the
/// station), and for disks the bus (USB, SD, MMC, 1394) and the removable media; without a container id the bus of the instance id decides.
/// One physical device is one row: nodes are grouped by container id and the kind is the one of highest priority (a phone seen as WPD + composite + ADB is one „Telefon”).
/// </summary>
public static class DeviceScope
{
    /// <summary>
    /// The version of the kind classification (what <see cref="DeviceJournalEntry.Class"/> is computed from). 1 = before 2026-10-22 (a device with several HID
    /// collections took the kind of any of them); 2 = the kind of the first interface (a Razer mouse with a keyboard collection is a mouse); 3 = a keyboard or mouse
    /// that hangs off Bluetooth (BLE HID, BTHENUM, a BTH* parent) is a "Dispozitiv Bluetooth", a forbidden class on an isolated station. Recorded in the
    /// journal row and in "device.first_seen"; a kind that changes under an older version is sealed as "device.reclassified" (check D05: informational).
    /// </summary>
    public const int ClassifierVersion = 3;

    /// <summary>DEVPKEY_Device_ContainerId of every component of the station itself (DEVPKEY_Device_InLocalMachineContainer).</summary>
    public const string LocalMachineContainer = "{00000000-0000-0000-FFFF-FFFFFFFFFFFF}";

    private static readonly DeviceClass[] InScopeClasses =
    [
        DeviceClass.UsbStorage, DeviceClass.MemoryCard, DeviceClass.ExternalDisk, DeviceClass.Phone, DeviceClass.Camera, DeviceClass.Scanner, DeviceClass.Printer,
        DeviceClass.NetworkAdapter, DeviceClass.WiFi, DeviceClass.Modem, DeviceClass.Bluetooth, DeviceClass.BluetoothAdapter, DeviceClass.Optical, DeviceClass.Keyboard,
        DeviceClass.Mouse, DeviceClass.SmartCardReader, DeviceClass.CardReader,
    ];

    /// <summary>The single scope predicate for a class (a journal row of any older class is not in scope).</summary>
    public static bool IsInScope(DeviceClass c) => Array.IndexOf(InScopeClasses, c) >= 0;

    /// <summary>The single scope predicate for a journal row.</summary>
    public static bool IsInScope(DeviceJournalEntry e) => IsInScope(e.Class);

    /// <summary>
    /// Whether the monitor page lists a journal row (real-PC walkthrough round 3, 2026-10-22): an in-scope row, except, while it is not connected, a row folded
    /// into another one (<see cref="DeviceJournalEntry.SupersededBy"/>: an interface of a device, the USB-SATA bridge of a disk, an old clone mark that no longer
    /// holds) and a row of a component of the station that an older version listed (<see cref="IsInternalRow"/>). Display only: check D05 and the report compare
    /// every in-scope row with the sealed audit (<see cref="IsInScope(DeviceJournalEntry)"/>), so nothing is hidden from the control.
    /// </summary>
    public static bool IsShown(DeviceJournalEntry e) => IsInScope(e) && (e.Present || e.SupersededBy.Length == 0 && !IsInternalRow(e));

    /// <summary>
    /// A row of a component of the station itself, from an older version of the monitor that judged every node on its own: an integrated camera, keyboard or
    /// touchpad (by its name), or a node that does not hang off the outside of the station (ACPI, PCI, an I2C touchpad „HID\ELAN…”). Storage media, optical
    /// drives and card readers are judged by their drive, never by this.
    /// </summary>
    public static bool IsInternalRow(DeviceJournalEntry e)
    {
        if (DeviceMonitorRules.IsStorageMedium(e.Class) || e.Class is DeviceClass.Optical or DeviceClass.CardReader) return false;
        // the radio of the station: a live reading saw this very node in the local machine container (never decided from a name; see DeviceJournalEntry.StationInternal)
        if (e.StationInternal) return true;
        if (IsIntegratedName(e.Name) && e.Class is not (DeviceClass.Bluetooth or DeviceClass.BluetoothAdapter)) return true;   // never by the name of a remote Bluetooth device
        return e.InstanceId.Length > 0 && !LooksExternalById(e.InstanceId);
    }

    /// <summary>
    /// The ONLY attested internal group: every node of the device reports, from the PnP stack (the removable capability the hub or port - ACPI _UPC/_PLD - gives), that it
    /// is NOT removable. Never a friendly name or a product string (a driverless interface takes its name from the device's own strings: an injector can call itself
    /// „Integrated Camera”). Unknown (null) counts as external: such a device is journaled.
    /// </summary>
    public static bool IsAttestedInternal(IReadOnlyList<PnpSnapshotEntry> members) => members.Count > 0 && members.All(m => m.Removable == false);

    /// <summary>
    /// The kind changes a classifier version bump is KNOWN to make (explicit table, nothing else is accepted as a reclassification): 1 → 2 turns the keyboard of a
    /// device whose first interface is a mouse into a mouse. The monitor additionally re-runs the old rule on the live snapshot (<see cref="ScopedDevice.KindAtVersion"/>).
    /// </summary>
    private static readonly Dictionary<(int From, int To), (DeviceClass Old, DeviceClass New)[]> KnownReclassifications = new()
    {
        [(1, 2)] = [(DeviceClass.Keyboard, DeviceClass.Mouse)],
        [(1, 3)] = [(DeviceClass.Keyboard, DeviceClass.Mouse), (DeviceClass.Keyboard, DeviceClass.Bluetooth), (DeviceClass.Mouse, DeviceClass.Bluetooth)],
        [(2, 3)] = [(DeviceClass.Keyboard, DeviceClass.Bluetooth), (DeviceClass.Mouse, DeviceClass.Bluetooth)],
    };

    public static bool IsKnownReclassification(int fromVersion, int toVersion, DeviceClass oldKind, DeviceClass newKind) =>
        KnownReclassifications.TryGetValue((fromVersion, toVersion), out var t) && t.Contains((oldKind, newKind));

    public static bool IsKnownReclassification(int fromVersion, int toVersion, string oldKind, string newKind) =>
        Enum.TryParse<DeviceClass>(oldKind, ignoreCase: true, out var o) && Enum.TryParse<DeviceClass>(newKind, ignoreCase: true, out var n) && IsKnownReclassification(fromVersion, toVersion, o, n);

    /// <summary>A name Windows gives a built-in component („Integrated Camera”, „Integrated Webcam”, „Built-in Keyboard”).</summary>
    public static bool IsIntegratedName(string name) =>
        U(name) is var n && (n.Contains("INTEGRATED", StringComparison.Ordinal) || n.Contains("BUILT-IN", StringComparison.Ordinal) || n.Contains("BUILTIN", StringComparison.Ordinal));

    /// <summary>Order of the list: storage media first, then communication and peripherals.</summary>
    public static int SortOrder(DeviceClass c) => IsInScope(c) ? Array.IndexOf(InScopeClasses, c) : 100;

    // ---- containers -------------------------------------------------------------------------------------------------------------------------

    private static string NormContainer(string c)
    {
        var t = c.Trim().Trim('{', '}').ToUpperInvariant();
        return t.Length == 0 ? "" : "{" + t + "}";
    }

    public static bool IsLocalContainer(string container) => NormContainer(container) == LocalMachineContainer;

    /// <summary>A container id that identifies a physical device (not empty, not all zeros, not the local machine).</summary>
    public static bool IsDeviceContainer(string container)
    {
        var c = NormContainer(container);
        return c.Length > 0 && c != LocalMachineContainer && c != "{00000000-0000-0000-0000-000000000000}";
    }

    // ---- node classification ------------------------------------------------------------------------------------------------------------------

    private static string U(string s) => s.ToUpperInvariant();

    internal static bool LooksWifi(PnpSnapshotEntry e)
    {
        var n = U(e.Name);
        return n.Contains("WI-FI") || n.Contains("WIFI") || n.Contains("WIRELESS") || n.Contains("WLAN") || n.Contains("802.11");
    }

    internal static bool IsVirtualAdapter(PnpSnapshotEntry e)
    {
        var id = U(e.InstanceId);
        var n = U(e.Name);
        return id.StartsWith(@"ROOT\", StringComparison.Ordinal) || id.StartsWith(@"SWD\", StringComparison.Ordinal) || id.StartsWith(@"SW\", StringComparison.Ordinal)
               || id.StartsWith(@"BTH\", StringComparison.Ordinal) || id.StartsWith(@"BTHENUM\", StringComparison.Ordinal) || id.StartsWith(@"VMBUS\", StringComparison.Ordinal)
               || n.Contains("HYPER-V") || n.Contains("VPN") || n.Contains("TAP-") || n.Contains("TAP ") || n.Contains("WINTUN") || n.Contains("WIREGUARD") || n.Contains("VIRTUAL")
               || n.Contains("VMWARE") || n.Contains("VIRTUALBOX") || n.Contains("WAN MINIPORT") || n.Contains("KERNEL DEBUG") || n.Contains("LOOPBACK") || n.Contains("PERSONAL AREA NETWORK")
               || n.Contains("TEREDO") || n.Contains("6TO4") || n.Contains("ISATAP") || n.Contains("PSEUDO");
    }

    private static bool LooksCamera(string name) =>
        U(name) is var n && (n.Contains("CAM") || n.Contains("DSC") || n.Contains("EOS") || n.Contains("NIKON") || n.Contains("CANON") || n.Contains("GOPRO") || n.Contains("LUMIX")
                             || n.Contains("PTP") && !n.Contains("PHONE"));

    /// <summary>
    /// A storage node (disk, optical drive, SD / MMC slot): the kind of the medium, or null for any other node. Reuses <see cref="DeviceRules.Classify"/> so the
    /// media page and the monitor agree. A disk drive enumerated by SCSI (USB Attached SCSI, a USB-NVMe bridge) that is attached from outside the station
    /// (<see cref="AttachedOutside"/>) is an external disk, never an internal one.
    /// </summary>
    /// <summary>
    /// The kind the media page gives a node: the SAME classification as the monitor (<see cref="StorageKind"/>), including <see cref="AttachedOutside"/>, so a disk
    /// attached from outside the station that is not behind USBSTOR (UAS, USB-NVMe, an external NVMe over Thunderbolt / USB4 with a PCI parent and a container of
    /// its own) is <see cref="DeviceKind.ExternalDisk"/> („Disc extern”) on both pages, never an internal disk and never a stick. Null for a node that is no medium.
    /// </summary>
    public static DeviceKind? InventoryKind(PnpSnapshotEntry e, IReadOnlyDictionary<string, PnpSnapshotEntry>? byId = null) =>
        InventoryKind(e.InstanceId, e.SetupClass, e.HardwareIds, e.Name, U(e.SetupClass) == "DISKDRIVE" && AttachedOutside(e, byId));

    /// <summary>The same decision for a caller that reads the node itself (the media page): <paramref name="attachedOutside"/> from <see cref="AttachedOutside"/>.</summary>
    public static DeviceKind? InventoryKind(string instanceId, string setupClass, string hardwareIds, string name, bool attachedOutside)
    {
        var kind = DeviceRules.Classify(instanceId, setupClass, hardwareIds, name, attachedOutside);
        if (kind == DeviceKind.UsbStorage && !U(instanceId).StartsWith(@"USBSTOR\", StringComparison.Ordinal)) return DeviceKind.ExternalDisk;   // UAS / USB-NVMe / external NVMe
        return kind;
    }

    private static DeviceClass? StorageKind(PnpSnapshotEntry e, IReadOnlyDictionary<string, PnpSnapshotEntry>? byId = null)
    {
        var id = U(e.InstanceId);
        var cls = U(e.SetupClass);
        if (cls is not ("DISKDRIVE" or "CDROM") && !id.StartsWith(@"USBSTOR\", StringComparison.Ordinal) && !id.StartsWith(@"SD\", StringComparison.Ordinal)
            && !id.StartsWith(@"SDBUS\", StringComparison.Ordinal) && !id.StartsWith(@"MMC\", StringComparison.Ordinal))
            return null;
        var kind = InventoryKind(e.InstanceId, e.SetupClass, e.HardwareIds, e.Name, cls == "DISKDRIVE" && AttachedOutside(e, byId));
        return kind switch
        {
            DeviceKind.ExternalDisk => DeviceClass.ExternalDisk,
            DeviceKind.UsbStorage => DeviceClass.UsbStorage,
            DeviceKind.MemoryCard => DeviceClass.MemoryCard,
            DeviceKind.Optical => DeviceClass.Optical,
            DeviceKind.VirtualDisk => DeviceClass.VirtualDisk,
            DeviceKind.Thunderbolt => DeviceClass.ExternalDisk,
            _ => DeviceClass.InternalDisk,
        };
    }

    /// <summary>
    /// A disk attached from outside the station: its container is a device container (not the local machine's), its own service or its parent's is UASPStor
    /// (USB Attached SCSI), or its parent is a USB node (USB bus). Decided before a SCSI disk is taken as internal.
    /// </summary>
    public static bool AttachedOutside(PnpSnapshotEntry e, IReadOnlyDictionary<string, PnpSnapshotEntry>? byId)
    {
        if (IsDeviceContainer(e.ContainerId)) return true;
        if (U(e.Service) == "UASPSTOR") return true;
        var parent = U(e.Parent);
        if (parent.StartsWith(@"USB\", StringComparison.Ordinal)) return true;
        return parent.Length > 0 && byId is not null && byId.TryGetValue(e.Parent, out var p) && U(p.Service) == "UASPSTOR";
    }

    /// <summary>The USB node a UAS / USB-NVMe disk hangs off, when it carries the device's own serial (the disk node's id is generated by Windows).</summary>
    private static PnpSnapshotEntry? UsbParentWithSerial(PnpSnapshotEntry e, IReadOnlyDictionary<string, PnpSnapshotEntry> byId) =>
        e.Parent.Length > 0 && byId.TryGetValue(e.Parent, out var p) && U(p.InstanceId).StartsWith(@"USB\VID_", StringComparison.Ordinal) && DeviceMonitorRules.Serial(p.InstanceId).Length > 0 ? p : null;

    private static bool LooksLikeDisk(PnpSnapshotEntry e, MediumInfo? m)
    {
        if (m is { CapacityBytes: >= 480_000_000_000 }) return true;
        var n = U(e.Name + " " + e.HardwareIds + " " + e.InstanceId);
        foreach (var w in new[] { "HDD", "SSD", "PORTABLE", "EXPANSION", "ELEMENTS", "PASSPORT", "EXTERNAL", "BACKUP", "MY BOOK", "CANVIO", "SOLID STATE", "BARRACUDA", "MY_PASSPORT" })
            if (n.Contains(w)) return true;
        return false;
    }

    /// <summary>The kind a non-storage node stands for (ignoring where it is connected); null when the node is only part of a device or is not in scope at all.</summary>
    private static DeviceClass? NodeKind(PnpSnapshotEntry e, IReadOnlyDictionary<string, PnpSnapshotEntry>? byId = null)
    {
        var id = U(e.InstanceId);
        var cls = U(e.SetupClass);
        var n = U(e.Name);
        if (id.Length == 0) return null;
        if (cls == "WPD")
        {
            // SWD\WPDBUSENUM entries are the portable-device view of a disk or volume (their id names the storage path) or of an MTP / PTP device (a USB path)
            if (id.StartsWith(@"SWD\WPDBUSENUM\", StringComparison.Ordinal) && (id.Contains("USBSTOR#") || id.Contains("STORAGE#VOLUME") || id.Contains("_??_SCSI#") || id.Contains("_??_SD#")
                                                                                 || id.Contains("_??_IDE#") || id.Contains("_??_NVME#") || id.Contains("_??_SDBUS#"))) return null;
            return LooksCamera(e.Name) && !n.Contains("PHONE") ? DeviceClass.Camera : DeviceClass.Phone;
        }
        if (id.StartsWith(@"ROOT\", StringComparison.Ordinal) || id.StartsWith(@"SW\", StringComparison.Ordinal) || id.StartsWith(@"SWD\", StringComparison.Ordinal)) return null;
        switch (cls)
        {
            case "ANDROIDUSBDEVICECLASS": return DeviceClass.Phone;
            case "USBDEVICE" when n.Contains("ADB") || n.Contains("ANDROID") || n.Contains("MTP"): return DeviceClass.Phone;
            case "BLUETOOTH":
                if (id.StartsWith(@"BTHLEDEVICE\{", StringComparison.Ordinal) && id.Contains("_DEV_", StringComparison.Ordinal)) return DeviceClass.Bluetooth;   // the GATT device of a BLE peripheral
                if (id.StartsWith(@"BTHENUM\DEV_", StringComparison.Ordinal) || id.StartsWith(@"BTHLE\DEV_", StringComparison.Ordinal) || id.StartsWith(@"BTHLEDEVICE\DEV_", StringComparison.Ordinal))
                    return DeviceClass.Bluetooth;
                // a USB radio: a dongle when it is in a container of its own (the caller checks), the station's own radio otherwise; service entries are not devices
                return id.StartsWith(@"USB\", StringComparison.Ordinal) ? DeviceClass.BluetoothAdapter : null;
            case "NET":
                if (IsVirtualAdapter(e) || !id.StartsWith(@"USB\", StringComparison.Ordinal)) return null;   // a PCI, ACPI or virtual adapter is a component of the station
                if (Regex.IsMatch(n, @"(?<![A-Z0-9])(MODEM|WWAN|LTE|HSPA|[45]G)(?![A-Z0-9])") || n.Contains("MOBILE BROADBAND")) return DeviceClass.Modem;
                return LooksWifi(e) ? DeviceClass.WiFi : DeviceClass.NetworkAdapter;
            case "MODEM":
                return id.StartsWith(@"USB\", StringComparison.Ordinal) || id.StartsWith(@"BTHENUM\", StringComparison.Ordinal) ? DeviceClass.Modem : null;
            case "PRINTER":
                return id.StartsWith(@"USBPRINT\", StringComparison.Ordinal) || id.StartsWith(@"USB\", StringComparison.Ordinal) || id.StartsWith(@"BTHENUM\", StringComparison.Ordinal) ? DeviceClass.Printer : null;
            case "IMAGE": return LooksCamera(e.Name) ? DeviceClass.Camera : DeviceClass.Scanner;
            case "CAMERA": return DeviceClass.Camera;
            case "KEYBOARD": return IsBluetoothHid(e, byId) ? DeviceClass.Bluetooth : DeviceClass.Keyboard;
            case "MOUSE": return IsBluetoothHid(e, byId) ? DeviceClass.Bluetooth : DeviceClass.Mouse;
            case "SMARTCARDREADER": return DeviceClass.SmartCardReader;
        }
        return null;
    }

    private const string BluetoothBaseUuid = "-0000-1000-8000-00805F9B34FB}";

    /// <summary>An instance id on a Bluetooth bus: BTHENUM, BTHLE, BTHLEDEVICE or BTH.</summary>
    private static bool IsBluetoothBus(string id) => id.StartsWith(@"BTHENUM\", StringComparison.Ordinal) || id.StartsWith(@"BTHLE\", StringComparison.Ordinal)
                                                     || id.StartsWith(@"BTHLEDEVICE\", StringComparison.Ordinal) || id.StartsWith(@"BTH\", StringComparison.Ordinal);

    /// <summary>
    /// The one source of truth for "this instance id is Bluetooth": a BTH* bus id, or a HID node whose own id is a Bluetooth HID service
    /// (HID\{0000xxxx-0000-1000-8000-00805F9B34FB}_DEV_VID&amp;...). Used by the live classifier, the page and the report rows of an older stored row.
    /// </summary>
    public static bool IsBluetoothInstanceId(string instanceId)
    {
        var id = U(instanceId);
        return IsBluetoothBus(id) || id.StartsWith(@"HID\{", StringComparison.Ordinal) && id.Contains(BluetoothBaseUuid, StringComparison.Ordinal);
    }

    /// <summary>
    /// A HID node that is Bluetooth: its own id is a Bluetooth HID service (HID\{0000xxxx-0000-1000-8000-00805F9B34FB}_DEV_VID&amp;..., BLE or classic) or BTHENUM / BTHLE /
    /// BTHLEDEVICE, or one of its parents (at most four levels) is a BTH* node. Decided from the id and the PnP tree, never from a name.
    /// </summary>
    public static bool IsBluetoothHid(PnpSnapshotEntry e, IReadOnlyDictionary<string, PnpSnapshotEntry>? byId)
    {
        var id = U(e.InstanceId);
        if (IsBluetoothInstanceId(id)) return true;
        var cur = e;
        for (int i = 0; i < 4 && cur.Parent.Length > 0; i++)
        {
            if (IsBluetoothBus(U(cur.Parent))) return true;
            if (byId is null || !byId.TryGetValue(cur.Parent, out var p)) break;
            cur = p;
        }
        return false;
    }

    /// <summary>Without a container id: the bus of the instance id says whether a node hangs off the outside of the station (USB, Bluetooth, USB-HID, USB printer).</summary>
    private static bool LooksExternalById(PnpSnapshotEntry e) => LooksExternalById(e.InstanceId);

    private static bool LooksExternalById(string instanceId)
    {
        var id = U(instanceId);
        return id.StartsWith(@"USB\", StringComparison.Ordinal) || id.StartsWith(@"USBPRINT\", StringComparison.Ordinal) || id.StartsWith(@"BTHENUM\", StringComparison.Ordinal)
               || id.StartsWith(@"BTHLE\", StringComparison.Ordinal) || id.StartsWith(@"BTHLEDEVICE\", StringComparison.Ordinal) || id.StartsWith(@"HID\VID_", StringComparison.Ordinal)
               || id.StartsWith(@"HID\{", StringComparison.Ordinal) || id.StartsWith(@"SWD\WPDBUSENUM\", StringComparison.Ordinal) || id.StartsWith(@"SCFILTER\", StringComparison.Ordinal);
    }

    /// <summary>
    /// The USB device a node belongs to: an interface (USB\VID_..&amp;MI_xx) or a HID collection (HID\VID_..&amp;MI_xx&amp;COLyy) is followed up its parents to the
    /// USB node without „&amp;MI_” (at most four levels); the node itself when it has no such parent.
    /// </summary>
    private static PnpSnapshotEntry UsbRoot(PnpSnapshotEntry e, IReadOnlyDictionary<string, PnpSnapshotEntry>? byId)
    {
        var cur = e;
        for (int i = 0; i < 4 && byId is not null && cur.Parent.Length > 0 && byId.TryGetValue(cur.Parent, out var p); i++)
        {
            var pid = U(p.InstanceId);
            if (!(pid.StartsWith(@"USB\VID_", StringComparison.Ordinal) || pid.StartsWith(@"HID\VID_", StringComparison.Ordinal))) break;
            cur = p;
            if (pid.StartsWith(@"USB\VID_", StringComparison.Ordinal) && !pid.Contains("&MI_", StringComparison.Ordinal)) break;
        }
        return cur;
    }

    // ---- Bluetooth: one remote device is many PnP nodes (device node, services, profiles, GATT services, HID children) ----------------------------
    // Their instance ids carry the 48-bit Bluetooth device address of the remote device; the PnP tree (parents) leads to the device node. Matching is strict
    // (anchored, exactly 12 hex digits, no extra suffix) and case-insensitive (Windows instance ids are); anything else has no address and stays a separate row.

    private const string ServiceGuid = @"\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}";
    private const RegexOptions IdOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>BTHENUM\DEV_mac\x, BTHLE\DEV_mac\x, BTHLEDEVICE\DEV_mac\x: the node of the remote device itself.</summary>
    private static readonly Regex BtDeviceNodeId = new(@"^BTH(?:ENUM|LE|LEDEVICE)\\DEV_([0-9A-F]{12})\\[^\\]+$", IdOptions);

    /// <summary>BTHENUM\{service uuid}[_VID&amp;_PID&amp;]\...&amp;mac_C0000000: a classic profile / service node.</summary>
    private static readonly Regex BtEnumServiceId = new(@"^BTHENUM\\" + ServiceGuid + @"[^\\]*\\[^\\]*&([0-9A-F]{12})_C[0-9A-F]{8}$", IdOptions);

    /// <summary>BTHLEDEVICE\{uuid}_DEV_[VID&amp;_PID&amp;_REV&amp;_]mac\... and HID\{uuid}_DEV_...mac[&amp;COLnn]\...: a GATT service or its HID child.</summary>
    private static readonly Regex BtGattServiceId = new(
        @"^(?:BTHLEDEVICE|HID)\\" + ServiceGuid + @"_DEV_(?:VID&[0-9A-F]{4,8}_PID&[0-9A-F]{4}(?:_REV&[0-9A-F]{4})?_)?([0-9A-F]{12})(?:&COL[0-9A-F]{2})?\\[^\\]+$", IdOptions);

    /// <summary>
    /// The 12-hex Bluetooth device address in the instance id of a Bluetooth node of a remote device (its device node, a classic service, a GATT service or its
    /// HID child), upper case; "" when the id has none or is malformed (short or long address, extra suffix, no instance part). Never read from a name.
    /// </summary>
    public static string BluetoothAddress(string instanceId)
    {
        var id = instanceId.Trim();
        foreach (var rx in new[] { BtDeviceNodeId, BtEnumServiceId, BtGattServiceId })
            if (rx.Match(id) is { Success: true } m && m.Groups[1].Value.ToUpperInvariant() is var mac && mac.Any(c => c != '0') && mac.Any(c => c != 'F')) return mac;   // 000000000000 / FFFFFFFFFFFF are placeholders (LOCALMFG), not an address
        return "";
    }

    /// <summary>The node of the remote device itself (BTHENUM\DEV_, BTHLE\DEV_, BTHLEDEVICE\DEV_ with a valid address): the row a Bluetooth device is named and keyed by.</summary>
    public static bool IsBluetoothDeviceNode(string instanceId) => BtDeviceNodeId.IsMatch(instanceId.Trim());

    /// <summary>The address of a node: its own id's, else the one of the nearest ancestor that has one (at most four levels up the PnP tree); "" = none.</summary>
    private static string BluetoothAddressOf(PnpSnapshotEntry node, IReadOnlyDictionary<string, PnpSnapshotEntry>? byId)
    {
        var cur = node;
        for (int i = 0; i < 5; i++)
        {
            if (BluetoothAddress(cur.InstanceId) is { Length: > 0 } mac) return mac;
            if (cur.Parent.Length == 0) break;
            if (byId is null || !byId.TryGetValue(cur.Parent, out var p)) return BluetoothAddress(cur.Parent);   // a parent outside the snapshot may still carry the address
            cur = p;
        }
        return "";
    }

    private static string GroupKey(PnpSnapshotEntry node, IReadOnlyDictionary<string, PnpSnapshotEntry>? byId = null, IReadOnlyDictionary<string, string>? btContainers = null)
    {
        if (IsDeviceContainer(node.ContainerId)) return "C|" + NormContainer(node.ContainerId);
        // a Bluetooth node without a container id of its own: the container of the other nodes of the same remote address, else the address itself
        if (BluetoothAddressOf(node, byId) is { Length: > 0 } mac)
            return btContainers is not null && btContainers.TryGetValue(mac, out var bc) ? bc : "B|" + mac;
        // without a container id the interfaces and HID collections of one USB device are grouped with it through their parents
        var e = UsbRoot(node, byId);
        if (!ReferenceEquals(e, node) && IsDeviceContainer(e.ContainerId)) return "C|" + NormContainer(e.ContainerId);
        var id = U(e.InstanceId);
        var serial = DeviceMonitorRules.Serial(e.InstanceId);
        if (serial.Length > 0) return "S|" + serial;
        if (id.Contains("VID_", StringComparison.Ordinal) && (id.StartsWith(@"HID\", StringComparison.Ordinal) || id.StartsWith(@"USB\", StringComparison.Ordinal)))
        {
            var vp = DeviceRules.VidPid(id);
            if (vp.Length > 0) return "V|" + vp;   // the HID collections of one USB device (no container id read)
        }
        return "I|" + id;
    }

    /// <summary>A USB mass-storage interface node: driver USBSTOR or UASPStor, interface class 08, or Windows' name for it („USB Mass Storage Device”).</summary>
    private static bool IsMassStorageInterface(PnpSnapshotEntry e) =>
        U(e.Service) is "USBSTOR" or "UASPSTOR" || U(e.HardwareIds).Contains("CLASS_08", StringComparison.Ordinal) || U(e.Name).Contains("MASS STORAGE", StringComparison.Ordinal);

    /// <summary>The interface number of a node (MI_xx; 0 without one) and its HID collection (COLxx; 0 without one): the primary function comes first.</summary>
    private static (int Interface, int Collection) InterfaceOrder(string instanceId)
    {
        var id = U(instanceId);
        var mi = Regex.Match(id, "&MI_([0-9A-F]{2})");
        var col = Regex.Match(id, "&COL([0-9A-F]{2})");
        return (mi.Success ? Convert.ToInt32(mi.Groups[1].Value, 16) : 0, col.Success ? Convert.ToInt32(col.Groups[1].Value, 16) : 0);
    }

    /// <summary>
    /// Keyboard or mouse (owner correction 2026-10-22): one physical device is the kind of its PRIMARY function, the keyboard or mouse node on its first interface
    /// (lowest MI_xx, then lowest COLxx), not of any interface. A gaming mouse exposes keyboard and consumer collections for its macros on later interfaces: its
    /// first interface is the mouse, so it is one „Mouse extern”; a keyboard whose mouse collection sits on a later interface stays a keyboard. On equal order the
    /// mouse wins. The nodes of the other kind are dropped from the candidates.
    /// </summary>
    private static void PrimaryInputKind(List<(DeviceClass Kind, PnpSnapshotEntry Node)> candidates)
    {
        var input = candidates.Where(c => c.Kind is DeviceClass.Keyboard or DeviceClass.Mouse).ToList();
        if (input.Select(c => c.Kind).Distinct().Count() < 2) return;
        var primary = input.OrderBy(c => InterfaceOrder(c.Node.InstanceId).Interface).ThenBy(c => InterfaceOrder(c.Node.InstanceId).Collection)
            .ThenBy(c => c.Kind == DeviceClass.Mouse ? 0 : 1).ThenBy(c => c.Node.InstanceId, StringComparer.OrdinalIgnoreCase).First().Kind;
        candidates.RemoveAll(c => c.Kind is DeviceClass.Keyboard or DeviceClass.Mouse && c.Kind != primary);
    }

    private static DeviceClass? NodeInputKind(PnpSnapshotEntry e) => U(e.SetupClass) switch { "KEYBOARD" => DeviceClass.Keyboard, "MOUSE" => DeviceClass.Mouse, _ => null };

    private static (DeviceClass Kind, PnpSnapshotEntry Node) Best(List<(DeviceClass Kind, PnpSnapshotEntry Node)> candidates) =>
        candidates.OrderByDescending(c => Priority(c.Kind)).ThenBy(c => IsBluetoothDeviceNode(c.Node.InstanceId) ? 0 : 1)   // a Bluetooth device is named and keyed by its device node, never by a service
            .ThenBy(c => U(c.Node.InstanceId).Contains("&MI_", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(c => InterfaceOrder(c.Node.InstanceId).Interface).ThenBy(c => InterfaceOrder(c.Node.InstanceId).Collection).First();

    private static int Priority(DeviceClass c) => c switch
    {
        DeviceClass.Phone => 100, DeviceClass.Camera => 90, DeviceClass.Scanner => 85, DeviceClass.Printer => 80, DeviceClass.SmartCardReader => 75,
        DeviceClass.CardReader => 70, DeviceClass.Modem => 65, DeviceClass.WiFi => 62, DeviceClass.NetworkAdapter => 60, DeviceClass.Keyboard => 50,
        DeviceClass.Mouse => 45, DeviceClass.BluetoothAdapter => 32, DeviceClass.Bluetooth => 30, _ => 0,
    };

    /// <summary>The USB-level node of a group: the device's own serial and VID/PID, not a HID or interface child of it.</summary>
    private static PnpSnapshotEntry BestIdentity(IReadOnlyList<PnpSnapshotEntry> members, PnpSnapshotEntry fallback)
    {
        bool Top(PnpSnapshotEntry e) => U(e.InstanceId).StartsWith(@"USB\VID_", StringComparison.Ordinal) && !U(e.InstanceId).Contains("&MI_", StringComparison.Ordinal);
        return members.FirstOrDefault(e => Top(e) && DeviceMonitorRules.Serial(e.InstanceId).Length > 0)
               ?? members.FirstOrDefault(Top)
               ?? members.FirstOrDefault(e => DeviceMonitorRules.Serial(e.InstanceId).Length > 0)
               ?? fallback;
    }

    /// <summary>
    /// The kind of one node as the monitor sees it (null = out of scope or only part of another device), judged inside the whole snapshot (a node is part of its
    /// container). Convenient for tests and single-node questions; the monitor uses <see cref="Classify(IReadOnlyList{PnpSnapshotEntry}, IReadOnlyDictionary{string, MediumInfo}?)"/>.
    /// </summary>
    public static DeviceClass? KindOf(PnpSnapshotEntry node, IReadOnlyList<PnpSnapshotEntry> snapshot, IReadOnlyDictionary<string, MediumInfo>? media = null)
    {
        foreach (var d in Classify(snapshot, media))
            if (d.Members.Any(m => m.InstanceId.Equals(node.InstanceId, StringComparison.OrdinalIgnoreCase)) && (d.Primary.InstanceId.Equals(node.InstanceId, StringComparison.OrdinalIgnoreCase) || !d.IsMedium))
                return d.Kind;
        return null;
    }

    /// <summary>
    /// Everything in scope in one snapshot: one <see cref="ScopedDevice"/> per physical device, plus one per storage medium (a stick, an external disk, an optical
    /// drive, and every card present in a reader or slot). <paramref name="media"/> (by drive instance id) improves the disc / stick decision with the capacity.
    /// </summary>
    public static IReadOnlyList<ScopedDevice> Classify(IReadOnlyList<PnpSnapshotEntry> snapshot, IReadOnlyDictionary<string, MediumInfo>? media = null)
    {
        var result = new List<ScopedDevice>();
        var driveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readerKeys = new HashSet<string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, PnpSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in snapshot) byId.TryAdd(p.InstanceId, p);
        // the container and the serial of every external storage medium: the USB node of its enclosure (a USB-SATA bridge, „USB Mass Storage Device”) and the
        // portable-device view of its volume are parts of that medium, never a phone of their own
        var storageGroups = new HashSet<string>(StringComparer.Ordinal);
        var storageSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 1. storage media: the drive node is the row
        foreach (var p in snapshot)
        {
            if (StorageKind(p, byId) is not { } sk) continue;
            driveIds.Add(p.InstanceId);
            if (sk is DeviceClass.InternalDisk or DeviceClass.VirtualDisk) continue;
            MediumInfo? mi = null;
            media?.TryGetValue(p.InstanceId, out mi);
            if (sk == DeviceClass.MemoryCard)
            {
                if (U(p.InstanceId).StartsWith(@"USBSTOR\", StringComparison.Ordinal) && !IsLocalContainer(p.ContainerId)) readerKeys.Add(GroupKey(p, byId));
                if (!DeviceMonitorRules.HasVolume(p, snapshot)) continue;     // an empty slot is not a medium
            }
            else if (sk == DeviceClass.UsbStorage && LooksLikeDisk(p, mi)) sk = DeviceClass.ExternalDisk;
            // a UAS / USB-NVMe disk: its serial and VID/PID are on the USB node it hangs off
            var identity = sk == DeviceClass.ExternalDisk && !U(p.InstanceId).StartsWith(@"USBSTOR\", StringComparison.Ordinal) && UsbParentWithSerial(p, byId) is { } up ? up : p;
            if (sk != DeviceClass.MemoryCard)
            {
                if (IsDeviceContainer(p.ContainerId)) storageGroups.Add(GroupKey(p, byId));
                foreach (var n in new[] { p, identity })
                    if (DeviceMonitorRules.Serial(n.InstanceId) is { Length: > 0 } s) storageSerials.Add(s);
            }
            result.Add(new ScopedDevice(sk, p, identity, [p], p, mi));
        }
        // the container of every remote Bluetooth address (when its nodes agree on one): a service whose container id was not read joins that group
        var btContainers = snapshot.Where(e => IsDeviceContainer(e.ContainerId) && BluetoothAddressOf(e, byId).Length > 0)
            .GroupBy(e => BluetoothAddressOf(e, byId), StringComparer.Ordinal).Where(g => g.Select(e => NormContainer(e.ContainerId)).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => "C|" + NormContainer(g.First().ContainerId), StringComparer.Ordinal);
        // 2. everything else, one row per physical device
        foreach (var g in snapshot.Where(e => !driveIds.Contains(e.InstanceId) && !IsLocalContainer(e.ContainerId)).GroupBy(e => GroupKey(e, byId, btContainers)))
        {
            var members = g.ToList();
            bool container = g.Key.StartsWith("C|", StringComparison.Ordinal);
            // nodes of the station itself are out of scope; with a container id the container decides, without one the bus of the instance id does
            if (!container && !members.Any(LooksExternalById)) continue;
            var candidates = new List<(DeviceClass Kind, PnpSnapshotEntry Node)>();
            foreach (var m in members)
            {
                if (!container && !LooksExternalById(m)) continue;
                if (NodeKind(m, byId) is not { } k) continue;
                if (k == DeviceClass.BluetoothAdapter && !container) continue;   // without a device container a USB radio may be the station's own
                candidates.Add((k, m));
            }
            bool reader = readerKeys.Contains(g.Key);
            // the enclosure of a storage medium (same container or serial as its drive, or a USB mass-storage interface): the portable-device view of its volume
            // is no phone or camera, the device is the medium's row (real-PC walkthrough round 3: a USB-SATA bridge listed as „Telefon / tabletă”)
            if (storageGroups.Contains(g.Key) || members.Any(m => IsMassStorageInterface(m) || DeviceMonitorRules.Serial(m.InstanceId) is { Length: > 0 } s && storageSerials.Contains(s)))
                candidates.RemoveAll(c => c.Kind is DeviceClass.Phone or DeviceClass.Camera && U(c.Node.InstanceId).StartsWith(@"SWD\WPDBUSENUM\", StringComparison.Ordinal));
            // a built-in camera, keyboard or touchpad that Windows put in a container of its own is still a component of the station: decided ONLY by what the PnP stack
            // attests (every node non-removable), never by a name the device itself can set (review 2026-10-22: a keyboard injector with an „Integrated Camera” interface)
            if (IsAttestedInternal(members)) candidates.RemoveAll(c => c.Kind is DeviceClass.Camera or DeviceClass.Keyboard or DeviceClass.Mouse);
            var inputKinds = candidates.Where(c => c.Kind is DeviceClass.Keyboard or DeviceClass.Mouse).Select(c => c.Kind).Distinct().Count();
            // version 2 judged a Bluetooth keyboard or mouse as a keyboard or mouse: the candidates as that version saw them, for the reclassification check
            var btInput = members.Where(m => NodeInputKind(m) is not null && IsBluetoothHid(m, byId)).ToList();
            var v2Candidates = candidates.Select(c => (Kind: c.Kind == DeviceClass.Bluetooth && btInput.Contains(c.Node) ? NodeInputKind(c.Node)!.Value : c.Kind, c.Node)).ToList();
            var v2Inputs = v2Candidates.Where(c => c.Kind is DeviceClass.Keyboard or DeviceClass.Mouse).Select(c => c.Kind).Distinct().Count();
            PrimaryInputKind(candidates);
            if (candidates.Count == 0 && !reader) continue;
            if (reader && candidates.All(c => Priority(c.Kind) < Priority(DeviceClass.CardReader))) candidates.Add((DeviceClass.CardReader, BestIdentity(members, members[0])));
            var best = Best(candidates);
            var identity = BestIdentity(members, best.Node);
            DeviceClass v2Kind = best.Kind;
            if (btInput.Count > 0)
            {
                PrimaryInputKind(v2Candidates);
                v2Kind = Best(v2Candidates).Kind;
            }
            // version 1 took the keyboard when a keyboard and a mouse node were both there (higher priority), where version 2 takes the first interface's kind
            var v1 = v2Inputs == 2 && v2Kind is DeviceClass.Keyboard or DeviceClass.Mouse ? DeviceClass.Keyboard : v2Kind;
            var fn = best.Kind == DeviceClass.Bluetooth && btInput.Count > 0
                ? (btInput.Select(NodeInputKind).Distinct().Count() > 1 ? "tastatur\u0103/mouse prin Bluetooth" : NodeInputKind(btInput[0]) == DeviceClass.Keyboard ? "tastatur\u0103 prin Bluetooth" : "mouse prin Bluetooth") : "";
            result.Add(new ScopedDevice(best.Kind, best.Node, identity, members, null, null) { LegacyV1Kind = v1, LegacyV2Kind = v2Kind, BluetoothFunction = fn });
        }
        return result;
    }
}
