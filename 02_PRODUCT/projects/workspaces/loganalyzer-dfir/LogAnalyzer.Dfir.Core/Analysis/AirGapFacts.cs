using System.Text.Json;

namespace LogAnalyzer.Dfir.Analysis;

// Facts about the station that no event log carries: adapters, burn tooling, printers, screenshots, virtual machines, archiver histories and the
// camera / microphone / speaker inventory. Collected read-only by the Windows project (AirGapFactsCollector) into one JSON file stored as case evidence
// (source type "airgap_facts"); the WP14a rules read it next to the timeline. A list that is empty means "nothing was found by that read", never that the
// read worked: every read that failed is named in <see cref="AirGapFacts.Errors"/>.

/// <summary>A network adapter as Windows lists it (PnP class Net): the data the adapter-kind classifier needs.</summary>
public sealed class AdapterFact
{
    public string Name { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string PnpClass { get; set; } = "";
    public string Service { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public List<string> CompatibleIds { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? FirstInstallUtc { get; set; }
}

public sealed class OpticalWriterFact
{
    public string Name { get; set; } = "";
    public string Drive { get; set; } = "";
    /// <summary>True when the drive reports that it can write discs (WMI Win32_CDROMDrive.Capabilities contains "Supports Writing").</summary>
    public bool CanWrite { get; set; }
    public List<string> Capabilities { get; set; } = [];
}

public sealed class BurnStagingFile
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset? LastWriteUtc { get; set; }
}

public sealed class BurnToolLog
{
    public string Path { get; set; } = "";
    public DateTimeOffset? LastWriteUtc { get; set; }
    /// <summary>The first lines of the log that name a drive, an image or a result (bounded; the log itself is not copied).</summary>
    public List<string> Excerpt { get; set; } = [];
}

public sealed class BurnFacts
{
    /// <summary>NoCDBurning in the machine policy (Explorer); null = not set.</summary>
    public int? NoCdBurningMachine { get; set; }
    public int? NoCdBurningUser { get; set; }
    public List<OpticalWriterFact> Writers { get; set; } = [];
    public string StagingPath { get; set; } = "";
    public bool StagingFolderExists { get; set; }
    public List<BurnStagingFile> StagingFiles { get; set; } = [];
    public List<BurnToolLog> ToolLogs { get; set; } = [];
    /// <summary>True when the removable-storage policy denies write for the CD/DVD class (Deny_Write = 1).</summary>
    public bool? CdDvdDenyWrite { get; set; }
}

public sealed class PrinterFact
{
    public string Name { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Port { get; set; } = "";
    public bool IsNetwork { get; set; }
    /// <summary>A virtual printer that writes a file (PDF, XPS, "Print to file", OneNote): the output leaves no paper.</summary>
    public bool WritesToFile { get; set; }
}

public sealed class PrintFacts
{
    /// <summary>Whether the channel Microsoft-Windows-PrintService/Operational is enabled; null = not read.</summary>
    public bool? OperationalLogEnabled { get; set; }
    public List<PrinterFact> Printers { get; set; } = [];
}

public sealed class ScreenFile
{
    public string Path { get; set; } = "";
    public DateTimeOffset? CreatedUtc { get; set; }
}

public sealed class ScreenFacts
{
    public List<ScreenFile> Screenshots { get; set; } = [];
    /// <summary>Folders of the Snipping Tool / Screen Sketch packages that hold files (name of folder, newest file time).</summary>
    public List<ScreenFile> SnippingTraces { get; set; } = [];
    public int? ClipboardHistoryUser { get; set; }
    public int? ClipboardHistoryPolicy { get; set; }
    public bool? ActivitiesCachePresent { get; set; }
    public int? ActivityFeedPolicy { get; set; }
}

public sealed class DiskImageFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset? LastWriteUtc { get; set; }
}

public sealed class VirtualFacts
{
    public bool? HyperVServicePresent { get; set; }
    public List<string> HyperVMachines { get; set; } = [];
    public List<DiskImageFile> DiskImages { get; set; } = [];
    public bool DiskImageSearchTruncated { get; set; }
    public List<string> WslDistributions { get; set; } = [];
    public List<string> Hypervisors { get; set; } = [];
    /// <summary>D12: container engines installed or registered as services (Docker, Podman, Rancher Desktop, Windows Host Compute Service).</summary>
    public List<string> ContainerEngines { get; set; } = [];
    /// <summary>D12: Windows containers found in the Docker data folder (name or id), bounded.</summary>
    public List<string> Containers { get; set; } = [];
    /// <summary>D12: virtual networks on the host: Hyper-V virtual switches and host-side virtual adapters (Hyper-V, VirtualBox, VMware).</summary>
    public List<string> VirtualNetworks { get; set; } = [];
    /// <summary>D12: bridge drivers that can join a virtual machine to the physical network (VirtualBox NetLwf, VMware VMnetBridge).</summary>
    public List<string> BridgeDrivers { get; set; } = [];
    /// <summary>Operating-system entries of the boot configuration (bcdedit /enum all /v); null = not read.</summary>
    public int? BootOsEntries { get; set; }
    public List<string> BootEntryNames { get; set; } = [];
}

public sealed class ArchiverEntry
{
    /// <summary>"WinRAR" or "7-Zip".</summary>
    public string Tool { get; set; } = "";
    /// <summary>The history list the value came from (ArcHistory, DialogEditHistory, FolderHistory, CopyHistory, PanelPath).</summary>
    public string List { get; set; } = "";
    public string Value { get; set; } = "";
    /// <summary>Place in the most-recently-used list (0 = most recent; WinRAR values "0", "1" …); null when the list has no order (PanelPath).</summary>
    public int? Position { get; set; }
    /// <summary>Last write time of the registry key that holds only this list (WinRAR); null for 7-Zip, whose FM key also holds unrelated settings.</summary>
    public DateTimeOffset? ListLastWriteUtc { get; set; }
}

public sealed class HardwareFact
{
    public string Name { get; set; } = "";
    /// <summary>Camera / Image, MEDIA (sound) or AudioEndpoint.</summary>
    public string PnpClass { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Status { get; set; } = "";
}

/// <summary>One file or folder of a removable volume as listed read-only by <see cref="Devices.RemovableMediaInspector.List"/>; a shortcut carries its decoded target.</summary>
public sealed class MediaEntryFact
{
    /// <summary>Path relative to the volume root ("Docs\a.pdf").</summary>
    public string RelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool Hidden { get; set; }
    public bool System { get; set; }
    public long Size { get; set; }
    public DateTimeOffset? CreatedUtc { get; set; }
    public DateTimeOffset? LastWriteUtc { get; set; }
    /// <summary>SHA-256 (hex) for small files only; "" when not computed.</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>For a .lnk: the target, arguments and working directory decoded by LnkParser (nothing is run); "" otherwise or when undecodable.</summary>
    public string LnkTarget { get; set; } = "";
    public string LnkArguments { get; set; } = "";
    public string LnkWorkingDir { get; set; } = "";
}

/// <summary>The listing of one removable volume connected at collection time (root and a bounded number of folder levels, one for the facts collector; reparse points not followed).</summary>
public sealed class MediaListingFact
{
    /// <summary>Drive letter without colon ("E").</summary>
    public string Letter { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Volume serial (hex, as in LNK / Prefetch); "" when unknown.</summary>
    public string VolumeSerial { get; set; } = "";
    /// <summary>Device serial from the media inventory when known; "" otherwise.</summary>
    public string DeviceSerial { get; set; } = "";
    public bool Truncated { get; set; }
    public List<MediaEntryFact> Entries { get; set; } = [];
    /// <summary>A short identity for the reader: serial, else letter and label.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Identity => DeviceSerial.Length > 0 ? DeviceSerial : VolumeSerial.Length > 0 ? $"{Letter}: (volum {VolumeSerial})" : $"{Letter}: „{Label}”";
}

/// <summary>One entry of a Startup folder (per user or common), read from the live file system; shortcuts carry their decoded target.</summary>
public sealed class StartupEntryFact
{
    /// <summary>"utilizator" or "comun".</summary>
    public string Scope { get; set; } = "";
    /// <summary>Profile folder name for a per-user entry; "" for the common folder.</summary>
    public string Profile { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTimeOffset? CreatedUtc { get; set; }
    public DateTimeOffset? LastWriteUtc { get; set; }
    public long Size { get; set; }
    public string LnkTarget { get; set; } = "";
    public string LnkArguments { get; set; } = "";
    /// <summary>Authenticode result for the target (or the file itself): true signed, false not signed, null not checked.</summary>
    public bool? TargetSigned { get; set; }
}

/// <summary>A top-level file of a profile's Downloads or Desktop folder (for MEDIA-SAME-DROP).</summary>
public sealed class ProfileFileFact
{
    public string Profile { get; set; } = "";
    /// <summary>"Downloads" or "Desktop".</summary>
    public string Folder { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset? LastWriteUtc { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// One saved network profile (HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\{guid}) joined with its signature
/// (...\NetworkList\Signatures\Managed|Unmanaged\*, matched on ProfileGuid). Windows keeps the dates as SYSTEMTIME in LOCAL time; they are converted to UTC
/// with the collecting station's zone.
/// </summary>
public sealed class NetworkListProfileFact
{
    public string ProfileGuid { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>0 public, 1 private, 2 domain; null = not stored.</summary>
    public int? Category { get; set; }
    /// <summary>6 wired, 71 wireless, 23 VPN, 243 mobile broadband; null = not stored.</summary>
    public int? NameType { get; set; }
    public DateTimeOffset? DateCreatedUtc { get; set; }
    public DateTimeOffset? DateLastConnectedUtc { get; set; }
    /// <summary>"Managed" (domain network) or "Unmanaged"; "" when no signature names this profile.</summary>
    public string SignatureKind { get; set; } = "";
    /// <summary>MAC of the default gateway the network was identified by ("AA-BB-..."); "" when not stored.</summary>
    public string DefaultGatewayMac { get; set; } = "";
    public string DnsSuffix { get; set; } = "";
    public string FirstNetwork { get; set; } = "";

    public static string NameTypeLabel(int? t) => t switch { 6 => "cablu (Ethernet)", 71 => "Wi-Fi", 23 => "VPN", 243 => "bandă largă mobilă", null => "tip necunoscut", var x => $"tip {x}" };
    public static string CategoryLabel(int? c) => c switch { 0 => "publică", 1 => "privată", 2 => "domeniu", null => "categorie necunoscută", var x => $"categoria {x}" };
}

/// <summary>Logging settings of one Windows Firewall profile and the state of its log file.</summary>
public sealed class FirewallLogFact
{
    /// <summary>"Domain", "Private" or "Public".</summary>
    public string Profile { get; set; } = "";
    public string Path { get; set; } = "";
    public bool? LogAllowed { get; set; }
    public bool? LogDropped { get; set; }
    public bool FileExists { get; set; }
    public long Size { get; set; }
    /// <summary>Why the configured path was not read ("" = accepted): only a local absolute path on a fixed drive is read (no UNC, device or network-drive path).</summary>
    public string Rejected { get; set; } = "";
}

/// <summary>The configuration of one event-log channel (EventLogConfiguration: retention mode, maximum size) and the number of its oldest record.</summary>
public sealed class EventLogInfoFact
{
    public string Channel { get; set; } = "";
    public bool? Enabled { get; set; }
    /// <summary>"Circular" (overwrite oldest), "AutoBackup" (archive when full) or "Retain" (stop when full); "" when not read.</summary>
    public string LogMode { get; set; } = "";
    public long? MaxSizeBytes { get; set; }
    /// <summary>EventRecordID of the oldest record still in the log; greater than 1 means older records were overwritten or cleared.</summary>
    public long? OldestRecordNumber { get; set; }
    public string Error { get; set; } = "";
}

/// <summary>What the facts collector read about the network evidence sources (owner directive 2026-10-16). A read that failed is named in <see cref="AirGapFacts.Errors"/>.</summary>
public sealed class NetworkEvidenceFacts
{
    public bool ProfilesRead { get; set; }
    public List<NetworkListProfileFact> Profiles { get; set; } = [];
    /// <summary>HKLM\SYSTEM\CurrentControlSet\Services\NlaSvc\Parameters\Internet → EnableActiveProbing (0 = NCSI does not probe, so its "Internet" verdict is not tested); null = not read.</summary>
    public int? NcsiEnableActiveProbing { get; set; }
    /// <summary>Policy HKLM\SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator → NoActiveProbe (1 = probing disabled by policy); null = not set.</summary>
    public int? NcsiPolicyNoActiveProbe { get; set; }
    public bool FirewallRead { get; set; }
    public List<FirewallLogFact> FirewallLogs { get; set; } = [];
    /// <summary>Audit of "Filtering Platform Connection" (Security 5156 success / 5157 failure); null = not readable (needs an administrator).</summary>
    public bool? WfpAuditSuccess { get; set; }
    public bool? WfpAuditFailure { get; set; }
    public List<EventLogInfoFact> EventLogs { get; set; } = [];
}

/// <summary>Everything the facts collector read from the station, with the reads that failed.</summary>
public sealed class AirGapFacts
{
    public const string SourceType = "airgap_facts";
    public string SchemaVersion { get; set; } = "1.0";
    public DateTimeOffset CollectedUtc { get; set; }
    public bool IsAdministrator { get; set; }
    public List<AdapterFact> Adapters { get; set; } = [];
    public BurnFacts Burn { get; set; } = new();
    public PrintFacts Print { get; set; } = new();
    public ScreenFacts Screen { get; set; } = new();
    public VirtualFacts Virtual { get; set; } = new();
    public List<ArchiverEntry> Archivers { get; set; } = [];
    /// <summary>Drive letters ("E") that Windows has assigned to removable media (MountedDevices pointing at USB storage, or a removable / optical drive now).</summary>
    public List<string> RemovableLetters { get; set; } = [];
    public List<HardwareFact> Hardware { get; set; } = [];
    /// <summary>Listings of the removable volumes connected at collection time (P1 payload rules).</summary>
    public List<MediaListingFact> MediaListings { get; set; } = [];
    /// <summary>Entries of the per-user and common Startup folders.</summary>
    public List<StartupEntryFact> StartupEntries { get; set; } = [];
    /// <summary>Top-level files of the profiles' Downloads and Desktop folders.</summary>
    public List<ProfileFileFact> ProfileFiles { get; set; } = [];
    /// <summary>Machine-level consent for camera and microphone (CapabilityAccessManager): "Deny", "Allow" or "" when not set / not read. This is the value of control P19.</summary>
    public string WebcamPolicy { get; set; } = "";
    public string MicrophonePolicy { get; set; } = "";
    /// <summary>Network evidence sources: NetworkList profiles and signatures, NCSI probing, firewall logging, WFP audit, event-log retention.</summary>
    public NetworkEvidenceFacts Network { get; set; } = new();
    /// <summary>Installation date of the application on the station (first chained record of its audit chain); null = not known, see <see cref="AppInstallBasis"/>.</summary>
    public DateTimeOffset? AppInstalledUtc { get; set; }
    public string AppInstallBasis { get; set; } = "";
    /// <summary>What contradicts the installation date (store re-initialised, chain not starting at the store creation, other traces older than the chain): NECONFORM on Standalone.</summary>
    public List<string> AppInstallViolations { get; set; } = [];
    /// <summary>DE VERIFICAT notes about the installation date (mismatch that is not proof, missing Windows traces).</summary>
    public List<string> AppInstallToVerify { get; set; } = [];
    /// <summary>Time of the first chained record of the application's audit chain (printed next to the computed date).</summary>
    public DateTimeOffset? AppInstallChainFirstUtc { get; set; }
    public List<string> Errors { get; set; } = [];
    /// <summary>Milliseconds each read took (diagnostic: shows which read is slow on a given station).</summary>
    public Dictionary<string, long> StepMs { get; set; } = [];

    /// <summary>Set by the pipeline from the stored evidence item; findings built from the facts cite it. Not part of the file.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string EvidenceId { get; set; } = "";

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string ToJson() => JsonSerializer.Serialize(this, Opts);
    public static AirGapFacts FromJson(string json) => JsonSerializer.Deserialize<AirGapFacts>(json, Opts) ?? new();
}
