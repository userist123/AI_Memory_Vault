using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// One physical device (or one medium) in scope of the monitor: its kind, the node the name comes from (<see cref="Primary"/>), the node its serial and VID/PID
/// come from (<see cref="IdentityEntry"/>, the USB-level node of a phone or a card reader), every node of the physical device and, for a medium, the drive node.
/// </summary>
public sealed record ScopedDevice(DeviceClass Kind, PnpSnapshotEntry Primary, PnpSnapshotEntry IdentityEntry, IReadOnlyList<PnpSnapshotEntry> Members, PnpSnapshotEntry? Drive, MediumInfo? Medium)
{
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
    /// <summary>DEVPKEY_Device_ContainerId of every component of the station itself (DEVPKEY_Device_InLocalMachineContainer).</summary>
    public const string LocalMachineContainer = "{00000000-0000-0000-FFFF-FFFFFFFFFFFF}";

    private static readonly DeviceClass[] InScopeClasses =
    [
        DeviceClass.UsbStorage, DeviceClass.MemoryCard, DeviceClass.ExternalDisk, DeviceClass.Phone, DeviceClass.Camera, DeviceClass.Scanner, DeviceClass.Printer,
        DeviceClass.NetworkAdapter, DeviceClass.WiFi, DeviceClass.Modem, DeviceClass.Bluetooth, DeviceClass.Optical, DeviceClass.Keyboard, DeviceClass.Mouse,
        DeviceClass.SmartCardReader, DeviceClass.CardReader,
    ];

    /// <summary>The single scope predicate for a class (a journal row of any older class is not in scope).</summary>
    public static bool IsInScope(DeviceClass c) => Array.IndexOf(InScopeClasses, c) >= 0;

    /// <summary>The single scope predicate for a journal row.</summary>
    public static bool IsInScope(DeviceJournalEntry e) => IsInScope(e.Class);

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
    /// media page and the monitor agree.
    /// </summary>
    private static DeviceClass? StorageKind(PnpSnapshotEntry e)
    {
        var id = U(e.InstanceId);
        var cls = U(e.SetupClass);
        if (cls is not ("DISKDRIVE" or "CDROM") && !id.StartsWith(@"USBSTOR\", StringComparison.Ordinal) && !id.StartsWith(@"SD\", StringComparison.Ordinal)
            && !id.StartsWith(@"SDBUS\", StringComparison.Ordinal) && !id.StartsWith(@"MMC\", StringComparison.Ordinal))
            return null;
        return DeviceRules.Classify(e.InstanceId, e.SetupClass, e.HardwareIds, e.Name) switch
        {
            DeviceKind.UsbStorage => DeviceClass.UsbStorage,
            DeviceKind.MemoryCard => DeviceClass.MemoryCard,
            DeviceKind.Optical => DeviceClass.Optical,
            DeviceKind.VirtualDisk => DeviceClass.VirtualDisk,
            DeviceKind.Thunderbolt => DeviceClass.ExternalDisk,
            _ => DeviceClass.InternalDisk,
        };
    }

    private static bool LooksLikeDisk(PnpSnapshotEntry e, MediumInfo? m)
    {
        if (m is { CapacityBytes: >= 480_000_000_000 }) return true;
        var n = U(e.Name + " " + e.HardwareIds + " " + e.InstanceId);
        foreach (var w in new[] { "HDD", "SSD", "PORTABLE", "EXPANSION", "ELEMENTS", "PASSPORT", "EXTERNAL", "BACKUP", "MY BOOK", "CANVIO", "SOLID STATE", "BARRACUDA", "MY_PASSPORT" })
            if (n.Contains(w)) return true;
        return false;
    }

    /// <summary>The kind a non-storage node stands for (ignoring where it is connected); null when the node is only part of a device or is not in scope at all.</summary>
    private static DeviceClass? NodeKind(PnpSnapshotEntry e)
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
                return id.StartsWith(@"BTHENUM\DEV_", StringComparison.Ordinal) || id.StartsWith(@"BTHLE\DEV_", StringComparison.Ordinal) || id.StartsWith(@"BTHLEDEVICE\DEV_", StringComparison.Ordinal)
                    ? DeviceClass.Bluetooth : null;   // the radio adapter and the service entries of a device are not devices
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
            case "KEYBOARD": return DeviceClass.Keyboard;
            case "MOUSE": return DeviceClass.Mouse;
            case "SMARTCARDREADER": return DeviceClass.SmartCardReader;
        }
        return null;
    }

    /// <summary>Without a container id: the bus of the instance id says whether a node hangs off the outside of the station (USB, Bluetooth, USB-HID, USB printer).</summary>
    private static bool LooksExternalById(PnpSnapshotEntry e)
    {
        var id = U(e.InstanceId);
        return id.StartsWith(@"USB\", StringComparison.Ordinal) || id.StartsWith(@"USBPRINT\", StringComparison.Ordinal) || id.StartsWith(@"BTHENUM\", StringComparison.Ordinal)
               || id.StartsWith(@"BTHLE\", StringComparison.Ordinal) || id.StartsWith(@"BTHLEDEVICE\", StringComparison.Ordinal) || id.StartsWith(@"HID\VID_", StringComparison.Ordinal)
               || id.StartsWith(@"HID\{", StringComparison.Ordinal) || id.StartsWith(@"SWD\WPDBUSENUM\", StringComparison.Ordinal) || id.StartsWith(@"SCFILTER\", StringComparison.Ordinal);
    }

    private static string GroupKey(PnpSnapshotEntry e)
    {
        if (IsDeviceContainer(e.ContainerId)) return "C|" + NormContainer(e.ContainerId);
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

    private static int Priority(DeviceClass c) => c switch
    {
        DeviceClass.Phone => 100, DeviceClass.Camera => 90, DeviceClass.Scanner => 85, DeviceClass.Printer => 80, DeviceClass.SmartCardReader => 75,
        DeviceClass.CardReader => 70, DeviceClass.Modem => 65, DeviceClass.WiFi => 62, DeviceClass.NetworkAdapter => 60, DeviceClass.Keyboard => 50,
        DeviceClass.Mouse => 45, DeviceClass.Bluetooth => 30, _ => 0,
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
        // 1. storage media: the drive node is the row
        foreach (var p in snapshot)
        {
            if (StorageKind(p) is not { } sk) continue;
            driveIds.Add(p.InstanceId);
            if (sk is DeviceClass.InternalDisk or DeviceClass.VirtualDisk) continue;
            MediumInfo? mi = null;
            media?.TryGetValue(p.InstanceId, out mi);
            if (sk == DeviceClass.MemoryCard)
            {
                if (U(p.InstanceId).StartsWith(@"USBSTOR\", StringComparison.Ordinal) && !IsLocalContainer(p.ContainerId)) readerKeys.Add(GroupKey(p));
                if (!DeviceMonitorRules.HasVolume(p, snapshot)) continue;     // an empty slot is not a medium
            }
            else if (sk == DeviceClass.UsbStorage && LooksLikeDisk(p, mi)) sk = DeviceClass.ExternalDisk;
            result.Add(new ScopedDevice(sk, p, p, [p], p, mi));
        }
        // 2. everything else, one row per physical device
        foreach (var g in snapshot.Where(e => !driveIds.Contains(e.InstanceId) && !IsLocalContainer(e.ContainerId)).GroupBy(GroupKey))
        {
            var members = g.ToList();
            bool container = g.Key.StartsWith("C|", StringComparison.Ordinal);
            // nodes of the station itself are out of scope; with a container id the container decides, without one the bus of the instance id does
            if (!container && !members.Any(LooksExternalById)) continue;
            var candidates = new List<(DeviceClass Kind, PnpSnapshotEntry Node)>();
            foreach (var m in members)
            {
                if (!container && !LooksExternalById(m)) continue;
                if (NodeKind(m) is { } k) candidates.Add((k, m));
            }
            bool reader = readerKeys.Contains(g.Key);
            if (candidates.Count == 0 && !reader) continue;
            if (reader && candidates.All(c => Priority(c.Kind) < Priority(DeviceClass.CardReader))) candidates.Add((DeviceClass.CardReader, BestIdentity(members, members[0])));
            var best = candidates.OrderByDescending(c => Priority(c.Kind)).ThenBy(c => U(c.Node.InstanceId).Contains("&MI_", StringComparison.Ordinal) ? 1 : 0).First();
            var identity = BestIdentity(members, best.Node);
            result.Add(new ScopedDevice(best.Kind, best.Node, identity, members, null, null));
        }
        return result;
    }
}
