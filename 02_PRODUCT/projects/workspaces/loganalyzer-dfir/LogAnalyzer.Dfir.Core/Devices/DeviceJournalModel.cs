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
    /// <summary>A card whose medium had neither a volume serial nor a disk signature (encrypted or RAW): each insertion is its own row, marked „mediu neidentificabil”.</summary>
    [JsonPropertyName("unidentifiable_medium")] public bool UnidentifiableMedium { get; set; }
    [JsonPropertyName("disk_signature")] public string DiskSignature { get; set; } = "";
    /// <summary>The account of the active console session when the device was connected; empty = nobody was logged on (shown as „—”).</summary>
    [JsonPropertyName("connected_user")] public string ConnectedUser { get; set; } = "";

    /// <summary>
    /// The identity of the row this one is folded into on the monitor page (an interface row of an older version, the USB-SATA bridge of a disk, an old clone
    /// mark that no longer holds); "" = a row of its own. Never stored: recomputed on every reading (<see cref="DeviceJournalMerge.MarkSuperseded"/>).
    /// </summary>
    [JsonIgnore] public string SupersededBy { get; set; } = "";

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
/// A record for the sealed audit whose write was refused (disk full, audit unavailable): kept in the journal so it survives an application restart and is
/// written at the next reading. While it is pending it is NOT sealed: the journal is untrusted for it and check D05 never says CONFORM.
/// </summary>
public sealed class PendingSeal
{
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
}

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

/// <summary>
/// The monitor ran without the right to seal: the sealed audit refused its records because the sealing key cannot be read (the application was not started as
/// administrator). Kept in the journal (the audit cannot take it): the first and the last refusal. Untrusted like the rest of the journal,
/// but it can only make check D05 more careful.
/// </summary>
public sealed class SealingRefusal
{
    [JsonPropertyName("first_utc")] public DateTimeOffset FirstUtc { get; set; }
    [JsonPropertyName("last_utc")] public DateTimeOffset LastUtc { get; set; }

    public SealingRefusal Copy() => (SealingRefusal)MemberwiseClone();
}

/// <summary>
/// A refused write to the sealed audit: the reason, and whether it is because the sealing key cannot be read (a typed signal from the audit, never a guess from the text).
/// A plain string converts to a refusal of another kind.
/// </summary>
public sealed record AuditRefusal(string Reason, bool KeyUnavailable = false)
{
    public override string ToString() => Reason;
    public static implicit operator AuditRefusal?(string? reason) => reason is null ? null : new AuditRefusal(reason);
}

/// <summary>When the monitor cannot seal (the key of the sealed audit cannot be read), what the page and check D05 say.</summary>
public static class SealingHealth
{
    public const string Banner = "Monitorul nu poate sigila evenimentele: porniți LogAnalyzer ca administrator. Până atunci D05 rămâne DE VERIFICAT.";

    /// <summary>How often the last refusal is moved forward (a refused record is retried at every reading; the journal is not rewritten for each retry).</summary>
    public static readonly TimeSpan NoteEvery = TimeSpan.FromMinutes(1);

    /// <summary>Notes a refusal in the journal (first and last time); returns whether the journal changed and has to be saved.</summary>
    public static bool Note(DeviceJournal journal, DateTimeOffset nowUtc)
    {
        if (journal.SealingRefused is null) { journal.SealingRefused = new SealingRefusal { FirstUtc = nowUtc, LastUtc = nowUtc }; return true; }
        if (nowUtc - journal.SealingRefused.LastUtc < NoteEvery) return false;
        journal.SealingRefused.LastUtc = nowUtc;
        return true;
    }

    /// <summary>Whether the refusals overlap the period.</summary>
    public static bool InPeriod(DeviceJournal journal, DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        journal.SealingRefused is { } r && r.FirstUtc <= toUtc && r.LastUtc >= fromUtc;

    /// <summary>The line check D05 lists as evidence.</summary>
    public static string Evidence(SealingRefusal r) =>
        $"MONITORUL A RULAT FĂRĂ DREPT DE SIGILARE: între {DeviceMonitorReport.UtcText(r.FirstUtc)} și {DeviceMonitorReport.UtcText(r.LastUtc)} jurnalul de audit a refuzat înregistrările monitorului " +
        "(cheia de sigilare nu poate fi citită: aplicația nu a fost pornită ca administrator); ce a văzut monitorul atunci nu este dovedit de jurnalul sigilat";
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

    /// <summary>Sealed-audit records not written yet (a refused write): flagged pending, retried at every reading, also after a restart.</summary>
    [JsonPropertyName("pending_seals")] public List<PendingSeal> PendingSeals { get; set; } = [];

    /// <summary>The windows in which the monitor itself was running (start, heartbeat, stop): a cross-check of the sealed ones.</summary>
    [JsonPropertyName("runs")] public List<MonitorRun> Runs { get; set; } = [];

    /// <summary>Set when the monitor ran without the right to seal (see <see cref="SealingRefusal"/>); null = never refused.</summary>
    [JsonPropertyName("sealing_refused")] public SealingRefusal? SealingRefused { get; set; }

    /// <summary>The radios the monitor could not see through (by kind).</summary>
    [JsonPropertyName("radios")] public List<RadioObservation> Radios { get; set; } = [];

    /// <summary>Most run windows kept.</summary>
    public const int MaxRuns = 5000;

    public DeviceJournalEntry? Find(string identity) => Devices.FirstOrDefault(d => d.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase));

    public DeviceJournal Copy() => new()
    {
        Version = Version, Devices = Devices.Select(d => d.Copy()).ToList(), Alerts = [.. Alerts], Runs = Runs.Select(r => r.Copy()).ToList(),
        Radios = Radios.Select(r => r.Copy()).ToList(), PendingSeals = PendingSeals.Select(p => new PendingSeal { Event = p.Event, Detail = p.Detail }).ToList(), SealingRefused = SealingRefused?.Copy(),
    };
}

/// <summary>
/// One connection or disconnection the merge found; <see cref="CloneText"/> is the critical clone alert text when the row is a possible clone, <see cref="InfoText"/>
/// an informational alert to raise as well (the same device back with another volume: „volum diferit”).
/// </summary>
public sealed record DeviceJournalChange(DeviceJournalEntry Device, bool Arrived, string? CloneText = null, string? InfoText = null);

/// <summary>A radio state change found by <see cref="DeviceJournalMerge.NoteRadios"/> (the caller seals it): <c>Blind</c> = off, absent or disabled from now on.</summary>
public sealed record RadioChange(string Kind, string Text, bool Blind);

/// <summary>A card row whose identity changed once its medium became readable (encrypted card unlocked): <c>Merged</c> = the medium was known before, its row is used.</summary>
public sealed record MediumRekey(string OldIdentity, string NewIdentity, DeviceJournalEntry Device, bool Merged);

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
    /// <item>A serial that returns with another KNOWN capacity or manufacturer (seen at a different time) is a separate row marked „serie duplicată – posibilă clonă”;
    /// a serial seen under a new identity with another known VID (or PID of a storage medium) or capacity of the same LUN is marked too (see <see cref="SerialConflict"/>).
    /// The nodes of one stick (USB node, USBSTOR disk, placeholders, capacity 0) are never clones of each other.</item>
    /// <item>A card in a reader or slot is identified by the medium (volume serial, else disk signature, + capacity), not by the reader: the same card is the same
    /// row however often it is inserted; an empty slot is not a medium. A card with neither (encrypted or RAW) is one row per insertion, marked „mediu
    /// neidentificabil (criptat/RAW)”; once its volume becomes readable the row is re-keyed (<paramref name="rekeyed"/> gets the link, the caller seals it).</item>
    /// <item>A device without a unique serial whose VID/PID matches a present device with a serial is a possible clone (Windows gave it a port-generated id).</item>
    /// <item>The same device back with the same capacity but another volume serial raises the informational „volum diferit (reformatat sau clonă)”.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<DeviceJournalChange> Apply(DeviceJournal journal, IEnumerable<PnpSnapshotEntry> present, IReadOnlyDictionary<string, MediumInfo>? media,
        string connectedUser, DateTimeOffset nowUtc, bool initial, ICollection<MediumRekey>? rekeyed = null)
    {
        media ??= NoMedia;
        var all = present as IReadOnlyList<PnpSnapshotEntry> ?? present.ToList();
        var scoped = DeviceScope.Classify(all, media);
        var cloneInstances = new HashSet<string>(DeviceMonitorRules.CloneGroups(scoped).SelectMany(g => g).Select(d => d.IdentityEntry.InstanceId), StringComparer.OrdinalIgnoreCase);
        var (portClones, portTwins) = DeviceMonitorRules.PortGeneratedClones(scoped);
        var changes = new List<DeviceJournalChange>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in scoped)
        {
            var p = d.IdentityEntry;
            var mi = d.Medium;
            var (ven, prod) = d.IsMedium ? DeviceRules.VendorProduct(d.Primary.InstanceId) : ("", "");
            var mfr = ven.Length > 0 ? ven : d.Primary.Manufacturer.Length > 0 ? d.Primary.Manufacturer : p.Manufacturer;
            string identity;
            bool duplicate = false, unidentifiable = false;
            string? infoText = null;
            if (d.Kind == DeviceClass.MemoryCard)
            {
                var drive = d.Drive!;
                var baseId = DeviceMonitorRules.Identity(drive) + "|MEDIUM|";
                // the row of this insertion, while the card stays in the slot
                var open = initial ? null : journal.Devices.FirstOrDefault(x => x.Present && x.InstanceId.Equals(drive.InstanceId, StringComparison.OrdinalIgnoreCase)
                                                                              && x.Identity.StartsWith(baseId, StringComparison.OrdinalIgnoreCase));
                if (DeviceMonitorRules.CanIdentify(mi))
                {
                    identity = DeviceMonitorRules.MediumIdentity(drive, mi!);
                    // the medium was not identifiable at insertion (or only by its disk signature) and is readable now (BitLocker unlocked): re-key the insertion's row
                    if (open is not null && !open.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase)
                        && (open.UnidentifiableMedium || open.VolumeSerial.Length == 0 && mi!.VolumeSerial.Length > 0))
                    {
                        var old = open.Identity;
                        var known = journal.Find(identity);
                        if (known is null) { open.Identity = identity; open.UnidentifiableMedium = false; }
                        else journal.Devices.Remove(open);   // the card was known: its own row continues (the link is sealed by the caller)
                        rekeyed?.Add(new MediumRekey(old, identity, known ?? open, known is not null));
                    }
                }
                else
                {
                    // the medium cannot be identified: the insertion is its own row (never merged with another card), kept while it stays present
                    identity = open is not null && open.UnidentifiableMedium ? open.Identity : baseId + "T" + nowUtc.UtcTicks.ToString(CultureInfo.InvariantCulture);
                    unidentifiable = true;
                }
            }
            else
            {
                identity = cloneInstances.Contains(p.InstanceId) ? DeviceMonitorRules.IdentityWithInstance(p) : DeviceMonitorRules.Identity(p);
                if (DeviceMonitorRules.Serial(p.InstanceId).Length > 0 && !cloneInstances.Contains(p.InstanceId))
                {
                    var candidates = journal.Devices.Where(x => x.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase)
                                                                || x.Identity.StartsWith(identity + "|DUP|", StringComparison.OrdinalIgnoreCase))
                                                    .OrderBy(x => x.SupersededBy.Length > 0 ? 1 : 0).ToList();   // a folded old clone row last
                    if (candidates.Count > 0)
                    {
                        long cap = mi?.CapacityBytes ?? 0;
                        var match = candidates.FirstOrDefault(x => SameDevice(x, cap, mfr, p.InstanceId));
                        if (match is not null)
                        {
                            identity = match.Identity;
                            // back with the same capacity but another volume serial: reformatted, or a clone of the serial
                            if (!match.Present && match.VolumeSerial.Length > 0 && mi is { VolumeSerial.Length: > 0 } vm && cap > 0 && match.CapacityBytes == cap
                                && !match.VolumeSerial.Equals(vm.VolumeSerial, StringComparison.OrdinalIgnoreCase))
                                infoText = DeviceMonitorRules.VolumeChangedAlertText;
                        }
                        else
                        {
                            identity += "|DUP|" + candidates.Count.ToString(CultureInfo.InvariantCulture);
                            duplicate = true;
                            foreach (var c in candidates) c.DuplicateSerial = true;
                        }
                    }
                    // the same serial under another identity (another VID/PID, or the same device seen through another node): a clone only when a KNOWN
                    // attribute differs (VID, the PID of a storage medium, or the capacity of the same LUN); raised once, when that identity is new
                    if (!duplicate && journal.Find(identity) is null)
                    {
                        var probe = new DeviceJournalEntry
                        {
                            Identity = identity, InstanceId = p.InstanceId, Class = d.Kind, Serial = DeviceMonitorRules.Serial(p.InstanceId),
                            VidPid = DeviceMonitorRules.VidPid(p.InstanceId, p.HardwareIds), CapacityBytes = mi?.CapacityBytes ?? 0, Manufacturer = mfr,
                        };
                        var clashing = journal.Devices.Where(x => !SameBase(x.Identity, identity) && SerialConflict(probe, x)).ToList();
                        if (clashing.Count > 0)
                        {
                            duplicate = true;
                            foreach (var c in clashing) c.DuplicateSerial = true;
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
            // review finding 1: a known capacity is never overwritten by another one (the row keeps the first); a row seen with two capacities is a clone
            if (mi is { CapacityBytes: > 0 } mc && e.CapacityBytes > 0 && mc.CapacityBytes != e.CapacityBytes && d.Kind != DeviceClass.MemoryCard) duplicate = true;
            if (duplicate || cloneInstances.Contains(p.InstanceId) || portClones.Contains(p.InstanceId) || portTwins.Contains(p.InstanceId)) e.DuplicateSerial = true;
            if (d.Kind == DeviceClass.MemoryCard) e.UnidentifiableMedium = unidentifiable;
            e.InstanceId = p.InstanceId;
            e.Class = d.Kind;
            e.SetupClass = d.Primary.SetupClass;
            // the device's friendly name, never the drive letter Windows gave the portable-device view of a phone's storage („F:\”)
            var name = DeviceMonitorRules.FriendlyName(new[] { d.Primary.Name, p.Name }.Concat(d.Members.Select(m => m.Name)), prod.Length > 0 ? prod : KnownManufacturer(mfr).Length > 0 ? mfr.Trim() : "");
            if (name.Length > 0 && !(DeviceMonitorRules.IsDriveLetterName(name) && e.Name.Length > 0 && !DeviceMonitorRules.IsDriveLetterName(e.Name))) e.Name = name;
            // a real manufacturer, once known, is never overwritten (review finding 2: „Samsung” must not become „S”): a real one replaces only a placeholder
            // („(Standard disk drives)”) or nothing, and a placeholder only fills an empty field
            if (mfr.Length > 0 && KnownManufacturer(e.Manufacturer).Length == 0 && (KnownManufacturer(mfr).Length > 0 || e.Manufacturer.Length == 0)) e.Manufacturer = mfr;
            if (prod.Length > 0) e.Model = prod;
            e.Serial = DeviceMonitorRules.Serial(p.InstanceId);
            e.NoUniqueSerial = DeviceMonitorRules.HasNoUniqueSerial(p.InstanceId);
            e.VidPid = DeviceMonitorRules.VidPid(p.InstanceId, p.HardwareIds);
            e.ConfigManagerErrorCode = d.Primary.ConfigManagerErrorCode > 0 ? d.Primary.ConfigManagerErrorCode : p.ConfigManagerErrorCode;
            if (mi is { } mm)
            {
                if (mm.CapacityBytes > 0 && e.CapacityBytes == 0) e.CapacityBytes = mm.CapacityBytes;
                e.VolumeLetters = mm.Letters;
                e.VolumeLabel = mm.Label;
                e.VolumeSerial = mm.VolumeSerial;
                e.DiskSignature = mm.DiskSignature;
            }
            if (DeviceMonitorRules.IsRadioKind(d.Kind) && !DeviceMonitorRules.IsDisabledRadio(d.Kind, e.ConfigManagerErrorCode)) e.ActiveSeenUtc = nowUtc;
            string? cloneText = duplicate ? DeviceMonitorRules.DuplicateSerialAlertText
                : cloneInstances.Contains(p.InstanceId) && (isNew || !e.Present) ? DeviceMonitorRules.CloneAlertText
                : portClones.Contains(p.InstanceId) && (isNew || !e.Present) ? DeviceMonitorRules.PortCloneAlertText : null;
            if (!e.Present)
            {
                e.Present = true;
                e.LastConnectedUtc = nowUtc;
                e.ConnectedUser = connectedUser;
                if (!initial || cloneText is not null || infoText is not null) changes.Add(new(e, true, cloneText, infoText));
            }
        }
        foreach (var e in journal.Devices.Where(e => e.Present && !seen.Contains(e.Identity)))
        {
            e.Present = false;
            e.LastDisconnectedUtc = nowUtc;
            if (DeviceScope.IsInScope(e)) changes.Add(new(e, false));
        }
        MarkSuperseded(journal);
        return changes;
    }

    // ---- one physical device or a clone of its serial ----------------------------------------------------------------------------------------------
    // One stick shows up as several Windows nodes that share its serial: the USB node (USB\VID_x&PID_y\<serial>, manufacturer „Compatible USB storage
    // device”, no capacity), the USBSTOR disk (USBSTOR\Disk&Ven_..\<serial>&0, „(Standard disk drives)”, the capacity only once the medium was read) and,
    // for a UAS disk, the USB node with the vendor of the SCSI disk („Fakeven ”, padded). Placeholders and a capacity of 0 are UNKNOWN, never different.

    // Review finding 1 (2026-10-22): the LUN number and type in a USBSTOR id are reported by the device itself, and the serial drops the „&N”, so a clone that
    // reports „…\SERIAL&1” or „CdRom&…” has the same identity as the original. Under ONE identity (same serial + VID/PID or vendor/product) a different known
    // capacity on any LUN is therefore a clone; the LUN only separates capacities between different identities of one stick.
    private static bool SameDevice(DeviceJournalEntry x, long capacity, string manufacturer, string instanceId) =>
        SameManufacturer(x.Manufacturer, manufacturer) && !CapacityDiffers(x.CapacityBytes, capacity);

    private static bool CapacityDiffers(long a, long b) => a > 0 && b > 0 && a != b;

    /// <summary>
    /// The manufacturer a node really reported, normalised (upper case, letters and digits only); empty for a placeholder Windows puts on a class driver
    /// („(Standard disk drives)”, „Compatible USB storage device”, „(Standard USB Host Controller)”, „Microsoft”, „Generic”, …) or an empty one.
    /// </summary>
    public static string KnownManufacturer(string manufacturer)
    {
        var t = manufacturer.Trim().ToUpperInvariant();
        if (t.Length == 0 || t.StartsWith('(') || t.StartsWith("STANDARD ", StringComparison.Ordinal) || t.StartsWith("COMPATIBLE ", StringComparison.Ordinal)
            || t.StartsWith("GENERIC", StringComparison.Ordinal) || t is "USB" or "USB2.0" or "USB3.0" or "MASS STORAGE" or "GENERAL" or "MULTIPLE" or "MICROSOFT" or "NONE" or "UNKNOWN")
            return "";
        return new string(t.Where(char.IsLetterOrDigit).ToArray());
    }

    /// <summary>Company-form words dropped from the end of a manufacturer before two are compared („Samsung Electronics Co., Ltd.” → „SAMSUNG”).</summary>
    private static readonly HashSet<string> CompanySuffixes = new(StringComparer.Ordinal)
    {
        "INC", "INCORPORATED", "LTD", "LIMITED", "CORP", "CORPORATION", "CO", "COMPANY", "LLC", "GMBH", "AG", "SA", "SRL", "BV", "KK", "PLC",
        "ELECTRONICS", "ELECTRONIC", "TECHNOLOGY", "TECHNOLOGIES", "INTERNATIONAL", "INTL",
    };

    /// <summary>
    /// The words of a real manufacturer (upper case, punctuation dropped, company-form suffixes Inc / Ltd / Corp / Co / LLC / GmbH / Electronics / Technology /…
    /// dropped from the end); empty for a placeholder (<see cref="KnownManufacturer"/>).
    /// </summary>
    public static IReadOnlyList<string> ManufacturerWords(string manufacturer)
    {
        if (KnownManufacturer(manufacturer).Length == 0) return [];
        var words = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var ch in manufacturer.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { cur.Append(ch); continue; }
            if (cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) words.Add(cur.ToString());
        while (words.Count > 1 && CompanySuffixes.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        return words;
    }

    /// <summary>
    /// Whether two manufacturers can be the same company (review finding 2, 2026-10-22; a substring match let „Samsung” → „S” → „SanDisk” pass). A placeholder or
    /// an empty value is UNKNOWN and matches anything. Two real ones match only when their words are equal after normalisation („Kingston” = „KINGSTON ”,
    /// „Fakeven” = „Fakeven Electronics”), or when the words of one are the first whole words of the other and are at least 4 characters long („Razer Inc” =
    /// „Razer USA Ltd.”: „RAZER” is the first word of „RAZER USA”). „S” never matches „Samsung”, „WD” never matches „Western Digital”.
    /// </summary>
    public static bool SameManufacturer(string a, string b)
    {
        var x = ManufacturerWords(a);
        var y = ManufacturerWords(b);
        if (x.Count == 0 || y.Count == 0) return true;
        if (x.SequenceEqual(y)) return true;
        var (shorter, longer) = x.Count <= y.Count ? (x, y) : (y, x);
        return shorter.Count < longer.Count && longer.Take(shorter.Count).SequenceEqual(shorter) && string.Concat(shorter).Length >= 4;
    }

    /// <summary>The logical unit a capacity belongs to: „DISK|0” for a USBSTOR disk LUN 0 (a CD-ROM LUN of the same stick is „CDROM|1”), „DISK|0” for any other node.</summary>
    private static string LunKey(string instanceId)
    {
        var parts = instanceId.ToUpperInvariant().Split('\\');
        if (parts.Length < 3 || parts[0] != "USBSTOR") return "DISK|0";
        var type = parts[1].Split('&')[0];
        var last = parts[^1];
        var amp = last.LastIndexOf('&');
        var lun = amp > 0 && last[(amp + 1)..].All(char.IsDigit) ? last[(amp + 1)..] : "0";
        return type + "|" + lun;
    }

    private static bool CapacityConflict(long a, string idA, long b, string idB) =>
        a > 0 && b > 0 && a != b && LunKey(idA) == LunKey(idB);

    /// <summary>The identity without the row suffix (a „|DUP|n” row or a „|&lt;instance id&gt;” row of a simultaneous clone): serial + VID/PID.</summary>
    private static string BaseIdentity(string identity)
    {
        if (!identity.StartsWith("SN|", StringComparison.OrdinalIgnoreCase)) return identity;
        var parts = identity.Split('|');
        return parts.Length > 3 ? string.Join('|', parts.Take(3)) : identity;
    }

    private static bool SameBase(string a, string b) => BaseIdentity(a).Equals(BaseIdentity(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>A row the serial comparison applies to: a real serial (not one Windows made up), not a card in a slot nor a card reader (their serial is the reader's).</summary>
    private static bool SerialComparable(DeviceJournalEntry e) =>
        e.Serial.Length > 0 && !e.NoUniqueSerial && e.Class is not (DeviceClass.MemoryCard or DeviceClass.CardReader) && !e.Identity.Contains("|MEDIUM|", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether two rows with the same serial are two different physical devices (a clone of the serial), from KNOWN attributes only: another VID (or another PID
    /// for a storage medium: a phone changes its PID with its USB mode), another capacity of the same logical unit, or, under the same serial + VID/PID, another
    /// real manufacturer. Rows of one stick (its USB node, its USBSTOR disk, its UAS node) never conflict: their placeholders and capacity 0 are unknown.
    /// </summary>
    public static bool SerialConflict(DeviceJournalEntry a, DeviceJournalEntry b)
    {
        if (ReferenceEquals(a, b) || !SerialComparable(a) || !SerialComparable(b) || !a.Serial.Equals(b.Serial, StringComparison.OrdinalIgnoreCase)) return false;
        var (va, pa) = DeviceMonitorRules.SplitVidPid(a.VidPid);
        var (vb, pb) = DeviceMonitorRules.SplitVidPid(b.VidPid);
        if (va.Length > 0 && vb.Length > 0)
        {
            if (va != vb) return true;
            if (pa != pb && DeviceMonitorRules.IsStorageMedium(a.Class) && DeviceMonitorRules.IsStorageMedium(b.Class)) return true;
        }
        // under one identity any two known capacities must agree (whatever LUN the device claims); between two identities of one stick only the same LUN's
        if (SameBase(a.Identity, b.Identity) ? CapacityDiffers(a.CapacityBytes, b.CapacityBytes) : CapacityConflict(a.CapacityBytes, a.InstanceId, b.CapacityBytes, b.InstanceId)) return true;
        // the manufacturer is compared only between nodes of the same kind (same serial + VID/PID or vendor/product): a USBSTOR vendor is cut to 8 characters
        // and need not match the USB node's manufacturer
        return SameBase(a.Identity, b.Identity) && !SameManufacturer(a.Manufacturer, b.Manufacturer);
    }

    /// <summary>
    /// Recomputes „serie duplicată – posibilă clonă” on every row when a journal is read: the stored flag is not trusted (older versions marked the nodes of one
    /// stick as clones of each other). A row is marked when its serial conflicts with another row (<see cref="SerialConflict"/>); the marks of the rules that need
    /// the devices present at the same time are kept as stored: a simultaneous clone (its row carries the instance id), and a port-generated clone (a row without
    /// a unique serial) with its twin (same class and VID/PID). Returns how many rows changed.
    /// </summary>
    public static int RecomputeDuplicateSerials(DeviceJournal journal)
    {
        var rows = journal.Devices;
        var portClones = rows.Where(r => r.DuplicateSerial && r.NoUniqueSerial && r.VidPid.Length > 0).ToList();
        int changed = 0;
        foreach (var r in rows)
        {
            bool simultaneous = r.Identity.StartsWith("SN|", StringComparison.OrdinalIgnoreCase) && r.Identity.Split('|') is { Length: 4 } parts && parts[3].Contains('\\');
            bool portRule = r.NoUniqueSerial || portClones.Any(c => c.Class == r.Class && c.VidPid.Equals(r.VidPid, StringComparison.OrdinalIgnoreCase));
            bool flag = r.DuplicateSerial && (simultaneous || portRule) || rows.Any(o => SerialConflict(r, o));
            if (flag != r.DuplicateSerial) { r.DuplicateSerial = flag; changed++; }
        }
        return changed;
    }

    // ---- one physical device, one row on the page (real-PC walkthrough round 3, 2026-10-22) ------------------------------------------------------
    // The station's journal showed one device as several rows: rows of an older version that judged every HID interface / collection on its own
    // („ID|HID\VID_xxxx&PID_yyyy&MI_01&COL01\…”), rows „…|DUP|1” the earlier clone rule made for the nodes of one device (a stick, an SSD, a mouse: one
    // „Conectat” and one „Deconectat” row each), and the USB-SATA bridge of an SSD (USB\VID_xxxx&PID_yyyy\<the SSD's serial>, „USB Mass Storage Device”).
    // They are FOLDED for the page, never deleted or re-keyed: the row stays in the journal with its own class and dates, so check D05 still compares it with
    // its sealed records; the page shows the device once, with the earliest first sight, the latest connection and the history of every folded row.

    private static readonly System.Text.RegularExpressions.Regex InterfaceNode = new(@"^(USB|HID)\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}(&MI_[0-9A-F]{2}|&COL[0-9A-F]{2}|\\)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A row of an older version for one interface or HID collection of a USB device (never the identity the monitor gives a device now).</summary>
    private static bool IsInterfaceRow(DeviceJournalEntry e) =>
        e.Identity.StartsWith("ID|", StringComparison.OrdinalIgnoreCase) && e.VidPid.Length > 0
        && (e.InstanceId.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) || e.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) && InterfaceNode.IsMatch(e.InstanceId);

    private static (int Interface, int Collection) InterfaceOrderOf(string instanceId)
    {
        var mi = System.Text.RegularExpressions.Regex.Match(instanceId, "&MI_([0-9A-Fa-f]{2})");
        var col = System.Text.RegularExpressions.Regex.Match(instanceId, "&COL([0-9A-Fa-f]{2})");
        return (mi.Success ? Convert.ToInt32(mi.Groups[1].Value, 16) : 0, col.Success ? Convert.ToInt32(col.Groups[1].Value, 16) : 0);
    }

    /// <summary>Two different registration numbers: the rows are not folded (the number of each is evidence; the operator decides).</summary>
    private static bool NumbersDiffer(DeviceJournalEntry a, DeviceJournalEntry b) =>
        a.RegistrationNumber.Length > 0 && b.RegistrationNumber.Length > 0 && !a.RegistrationNumber.Equals(b.RegistrationNumber, StringComparison.OrdinalIgnoreCase);

    private static int Chronological(DeviceJournalEntry a, DeviceJournalEntry b)
    {
        int c = a.FirstSeenUtc.CompareTo(b.FirstSeenUtc);
        return c != 0 ? c : string.Compare(a.Identity, b.Identity, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Recomputes <see cref="DeviceJournalEntry.SupersededBy"/> on every row (deterministic, whatever the order of the rows). Folded, when the two rows carry no
    /// different registration numbers:
    /// <list type="bullet">
    /// <item>a „…|DUP|n” row into its base row when the two do not conflict (<see cref="SerialConflict"/>; same class) and the row conflicts with no other row;</item>
    /// <item>an interface / HID-collection row of an older version into the one current row with the same VID/PID (when there is none or several, the interface
    /// rows of that VID/PID into the one of the first interface: lowest MI_xx and COLxx, then the earliest);</item>
    /// <item>the USB node of a storage enclosure („USB Mass Storage Device”, or a name that is only a drive letter) with a serial into the earliest storage-medium row
    /// with that serial.</item>
    /// </list>
    /// Chains are followed to the final row. Returns the number of folded rows.
    /// </summary>
    public static int MarkSuperseded(DeviceJournal journal)
    {
        var rows = journal.Devices;
        foreach (var r in rows) r.SupersededBy = "";
        var into = new Dictionary<DeviceJournalEntry, DeviceJournalEntry>(ReferenceEqualityComparer.Instance);
        // 1. an old clone mark that no longer holds
        foreach (var r in rows)
        {
            int i = r.Identity.IndexOf("|DUP|", StringComparison.OrdinalIgnoreCase);
            if (i < 0 || journal.Find(r.Identity[..i]) is not { } b || b.Class != r.Class || NumbersDiffer(r, b)) continue;
            if (rows.Any(o => SerialConflict(r, o))) continue;
            into[r] = b;
        }
        // 2. interface rows of an older version
        var legacy = rows.Where(r => !into.ContainsKey(r) && IsInterfaceRow(r)).ToList();
        foreach (var g in legacy.GroupBy(r => r.VidPid.ToUpperInvariant()))
        {
            var group = g.ToList();
            var targets = rows.Where(t => !IsInterfaceRow(t) && !into.ContainsKey(t) && t.VidPid.Equals(g.Key, StringComparison.OrdinalIgnoreCase) && DeviceScope.IsInScope(t)
                                          && !DeviceMonitorRules.IsStorageMedium(t.Class)).ToList();
            DeviceJournalEntry target;
            if (targets.Count == 1) target = targets[0];
            else
            {
                var ordered = group.ToList();
                ordered.Sort((a, b) =>
                {
                    var (ia, ca) = InterfaceOrderOf(a.InstanceId);
                    var (ib, cb) = InterfaceOrderOf(b.InstanceId);
                    return ia != ib ? ia.CompareTo(ib) : ca != cb ? ca.CompareTo(cb) : Chronological(a, b);
                });
                target = ordered[0];
            }
            foreach (var r in group)
                if (!ReferenceEquals(r, target) && !NumbersDiffer(r, target)) into[r] = target;
        }
        // 3. the USB node of a storage enclosure (a USB-SATA bridge with the disk's serial)
        foreach (var r in rows)
        {
            if (into.ContainsKey(r) || DeviceMonitorRules.IsStorageMedium(r.Class) || r.Serial.Length == 0 || !r.InstanceId.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)) continue;
            if (!(r.Name.Contains("MASS STORAGE", StringComparison.OrdinalIgnoreCase) || DeviceMonitorRules.IsDriveLetterName(r.Name))) continue;
            var disks = rows.Where(t => DeviceMonitorRules.IsStorageMedium(t.Class) && !into.ContainsKey(t) && t.Serial.Equals(r.Serial, StringComparison.OrdinalIgnoreCase)).ToList();
            if (disks.Count == 0) continue;
            disks.Sort(Chronological);
            if (!NumbersDiffer(r, disks[0])) into[r] = disks[0];
        }
        // every folded row points at the final row of its chain
        int folded = 0;
        foreach (var (r, _) in into)
        {
            var t = r;
            for (int i = 0; i < 16 && into.TryGetValue(t, out var next); i++) t = next;
            if (ReferenceEquals(t, r) || into.ContainsKey(t)) continue;   // a cycle: left as rows of their own
            r.SupersededBy = t.Identity;
            folded++;
        }
        return folded;
    }

    /// <summary>The identities whose history the page shows for a row: its own and every row folded into it (in journal order).</summary>
    public static IReadOnlyList<string> FoldedIdentities(DeviceJournal journal, string identity) =>
        [identity, .. journal.Devices.Where(d => d.SupersededBy.Equals(identity, StringComparison.OrdinalIgnoreCase)).Select(d => d.Identity)];

    /// <summary>
    /// What the page shows for a row: the row itself, with the earliest first sight and the latest connection / disconnection of the rows folded into it (a copy;
    /// the journal row is not changed, its dates stay the ones the sealed audit has).
    /// </summary>
    public static DeviceJournalEntry ShownView(DeviceJournal journal, DeviceJournalEntry e)
    {
        var folded = journal.Devices.Where(d => d.SupersededBy.Length > 0 && d.SupersededBy.Equals(e.Identity, StringComparison.OrdinalIgnoreCase)).ToList();
        if (folded.Count == 0) return e;
        var v = e.Copy();
        foreach (var f in folded)
        {
            if (f.FirstSeenUtc < v.FirstSeenUtc) v.FirstSeenUtc = f.FirstSeenUtc;
            if (f.LastConnectedUtc is { } c && (v.LastConnectedUtc is null || c > v.LastConnectedUtc)) v.LastConnectedUtc = c;
            if (f.LastDisconnectedUtc is { } x && (v.LastDisconnectedUtc is null || x > v.LastDisconnectedUtc)) v.LastDisconnectedUtc = x;
        }
        return v;
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

    /// <summary>
    /// Notes the radios the monitor cannot see through now (from <see cref="DeviceMonitorRules.BlindRadios"/>): a radio that is back is kept with the last time it was
    /// blind. Returns the state changes (a radio that went blind, or with another text, and one that is observable again) for the caller to seal.
    /// </summary>
    public static IReadOnlyList<RadioChange> NoteRadios(DeviceJournal journal, IReadOnlyList<(string Kind, string Text)> blind, DateTimeOffset nowUtc)
    {
        var changes = new List<RadioChange>();
        var before = journal.Radios.ToDictionary(r => r.Kind, r => (r.BlindNow, r.Text), StringComparer.Ordinal);
        foreach (var r in journal.Radios) r.BlindNow = false;
        foreach (var (kind, text) in blind)
        {
            var r = journal.Radios.FirstOrDefault(x => x.Kind == kind);
            if (r is null) journal.Radios.Add(r = new RadioObservation { Kind = kind, FirstBlindUtc = nowUtc });
            else if (!(before.TryGetValue(kind, out var b0) && b0.BlindNow)) r.FirstBlindUtc = nowUtc;   // blind again: a new blind stretch starts
            r.Text = text; r.BlindNow = true; r.LastBlindUtc = nowUtc;
            if (!(before.TryGetValue(kind, out var b) && b.BlindNow && b.Text == text)) changes.Add(new(kind, text, true));
        }
        foreach (var (kind, (wasBlind, _)) in before)
            if (wasBlind && !blind.Any(x => x.Kind == kind)) changes.Add(new(kind, kind + " observabil din nou", false));
        return changes;
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
