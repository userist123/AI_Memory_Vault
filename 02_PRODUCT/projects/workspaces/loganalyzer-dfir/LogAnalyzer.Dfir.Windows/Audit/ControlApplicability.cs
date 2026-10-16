using LogAnalyzer.Core.Services.Edition;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>What one posture expects of a check: whether the rule applies and, if so, the expected value (English, as in docs/dfir/EDITIONS_AND_POSTURE.md).</summary>
public sealed record PostureRule(bool Applies, string Expected);

/// <summary>One row of the applicability table. <see cref="Reason"/> says, in one line, why the rule does not apply where it does not (null when it applies everywhere).</summary>
public sealed record ApplicabilityEntry(string Id, string Name, PostureRule Standalone, PostureRule IsolatedNetwork, PostureRule Internet, string? Reason)
{
    public PostureRule For(StationPosture posture) => posture switch
    {
        StationPosture.Standalone => Standalone,
        StationPosture.IsolatedNetwork => IsolatedNetwork,
        StationPosture.Internet => Internet,
        _ => throw new ArgumentOutOfRangeException(nameof(posture)),
    };
}

/// <summary>
/// Which control checks apply to which station posture (docs/dfir/EDITIONS_AND_POSTURE.md is the same table; a test keeps them equal).
/// Every check id the evaluator can produce has an explicit entry for all three postures: there is no default. A check that does not apply
/// is reported as <see cref="ControlStatus.NuSeAplica"/>, never as compliant or non-compliant.
/// The compile-time posture of the executable selects the column; a signed policy cannot change it (see StationPostures.ClampMode).
/// </summary>
public static class ControlApplicability
{
    private static PostureRule Yes(string expected) => new(true, expected);
    private static readonly PostureRule No = new(false, "-");
    private static ApplicabilityEntry Row(string id, string name, PostureRule standalone, PostureRule network, PostureRule internet, string? reason) =>
        new(id, name, standalone, network, internet, reason);

    // BEGIN GENERATED TABLE (kept equal to docs/dfir/EDITIONS_AND_POSTURE.md by ControlApplicabilityTests)
    public static readonly IReadOnlyList<ApplicabilityEntry> All =
    [
        Row("C01", "Guest account", Yes("Disabled"), Yes("Disabled"), Yes("Disabled"), null),
        Row("C02", "Active local administrators", Yes("Review: only approved accounts"), Yes("Review: only approved accounts"), Yes("Review: only approved accounts"), null),
        Row("C03", "Built-in Administrator account", Yes("Disabled or controlled"), Yes("Disabled or controlled"), Yes("Disabled or controlled"), null),
        Row("C04", "Active accounts without required password", Yes("None"), Yes("None"), Yes("None"), null),
        Row("C05", "Passwords that never expire", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("C06", "Remote Desktop Users members", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("P01", "Minimum password length", Yes("At least 12"), Yes("At least 12 (domain policy may supersede)"), Yes("At least 12"), null),
        Row("P02", "Account lockout", Yes("Enabled"), Yes("Enabled"), Yes("Enabled"), null),
        Row("P03", "Password expiry", Yes("Defined"), Yes("Defined"), Yes("Defined"), null),
        Row("P04", "UAC enabled", Yes("Enabled"), Yes("Enabled"), Yes("Enabled"), null),
        Row("P05", "Automatic logon without password", Yes("Absent"), Yes("Absent"), Yes("Absent"), null),
        Row("P06", "Remote Desktop connections", Yes("Disabled; non-conform if enabled"), Yes("Disabled unless the administration procedure approves it; review if enabled"), Yes("Review if enabled"), null),
        Row("P07", "Windows Firewall on all profiles", Yes("Active on all profiles"), Yes("Active on all profiles, Domain profile included"), Yes("Active on all profiles"), null),
        Row("P08", "Microsoft Defender real-time protection", Yes("Active"), Yes("Active"), Yes("Active"), null),
        Row("P09", "Antivirus signatures up to date", Yes("Offline update procedure followed"), Yes("Updated from the internal update source"), Yes("Updated within 7 days"), null),
        Row("P10", "Defender exclusions", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("P11", "System disk encryption (BitLocker)", Yes("Enabled"), Yes("Enabled"), Yes("Enabled"), null),
        Row("P12", "SMBv1 disabled", Yes("Disabled"), Yes("Disabled"), Yes("Disabled"), null),
        Row("P13", "WDigest does not keep passwords in memory", Yes("Off"), Yes("Off"), Yes("Off"), null),
        Row("P14", "USB storage blocked by policy", Yes("Review (blocked expected)"), Yes("Review (blocked expected)"), Yes("Review (blocked expected)"), "Owner decision: data leakage through removable media is a risk on every station, connected ones included."),
        Row("P15", "Secure Boot", Yes("Enabled"), Yes("Enabled"), Yes("Enabled"), null),
        Row("P16", "Automatic screen lock", Yes("Configured"), Yes("Configured"), Yes("Configured"), null),
        Row("P17", "Application allow-list (AppLocker)", Yes("Enforced or documented alternative"), Yes("Enforced or documented alternative"), Yes("Enforced or documented alternative"), null),
        Row("P18", "AutoPlay / AutoRun", Yes("Disabled"), Yes("Disabled"), Yes("Disabled"), null),
        Row("P19", "Camera and microphone blocked by policy", Yes("Blocked"), Yes("Blocked"), No, "Blocking capture devices is a rule for controlled rooms; connected office stations legitimately use them."),
        Row("P20", "Windows Recall and Copilot off", Yes("Off"), Yes("Off"), Yes("Off"), null),
        Row("P21", "LSA protection", Yes("Enabled"), Yes("Enabled"), Yes("Enabled"), null),
        Row("P22", "Credential Guard", Yes("Running"), Yes("Running"), Yes("Running"), null),
        Row("P23", "PowerShell logging", Yes("Enabled"), Yes("Enabled"), Yes("Enabled"), null),
        Row("P24", "PowerShell 2.0 removed", Yes("Removed"), Yes("Removed"), Yes("Removed"), null),
        Row("P25", "Residual data on disk (paging, hibernation, dumps)", Yes("Cleared or disabled per procedure"), Yes("Cleared or disabled per procedure"), No, "Residual-data handling is a classified-material rule."),
        Row("P26", "Logon warning banner with classification level", Yes("Present"), Yes("Present"), No, "The classification banner exists only where classified material is handled."),
        Row("P27", "Station time source", Yes("Local source documented"), Yes("Domain time hierarchy (internal NTP)"), Yes("Review"), null),
        Row("P28", "CD/DVD burning blocked", Yes("Blocked; non-conform if a burner is writable"), Yes("Blocked; review if a burner is writable"), No, "Burning media is an exfiltration channel only on controlled stations."),
        Row("P29", "Printing is logged", Yes("Logged; non-conform if not"), Yes("Logged; review if not"), No, "Print logging is a data-handling rule of controlled stations."),
        Row("P30", "Clipboard and activity history off", Yes("Off; non-conform if on"), Yes("Off; review if on"), No, "History features are a leak path only on controlled stations."),
        Row("P31", "Camera, microphone and speakers present and active", Yes("None exposed; non-conform if exposed"), Yes("None exposed; review if exposed"), No, "Capture and audio devices are a rule for controlled rooms."),
        Row("P32", "BitLocker on all fixed volumes with boot PIN", Yes("Enabled with PIN"), Yes("Enabled with PIN"), Yes("Enabled"), null),
        Row("P33", "TPM present and ready", Yes("Ready"), Yes("Ready"), Yes("Ready"), null),
        Row("P34", "Kernel DMA Protection", Yes("On; non-conform if off"), Yes("On; review if off"), Yes("On; review if off"), null),
        Row("P35", "Virtualization-based security", Yes("Running"), Yes("Running"), Yes("Running"), null),
        Row("P36", "Password required after sleep", Yes("Required"), Yes("Required"), Yes("Required"), null),
        Row("P37", "Application allow-list (App Control / WDAC)", Yes("Enforced or documented alternative"), Yes("Enforced or documented alternative"), Yes("Enforced or documented alternative"), null),
        Row("P38", "Last full and quick antivirus scan", Yes("Recent"), Yes("Recent"), Yes("Recent"), null),
        Row("P39", "Antivirus registered in Security Center", Yes("Registered"), Yes("Registered"), Yes("Registered"), null),
        Row("A01", "Audit policy records the required activity", Yes("Required categories on"), Yes("Required categories on"), Yes("Required categories on"), null),
        Row("A02", "Security log size", Yes("At least 128 MB"), Yes("At least 128 MB"), Yes("At least 128 MB"), null),
        Row("A03", "Security log covers the audited period", Yes("Covers the period"), Yes("Covers the period"), Yes("Covers the period"), null),
        Row("A04", "Other logs cover the audited period", Yes("Cover the period"), Yes("Cover the period"), Yes("Cover the period"), null),
        Row("A05", "Logs cleared in the period", Yes("None"), Yes("None"), Yes("None"), null),
        Row("A06", "Audit policy changed", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("A07", "System time changed manually", Yes("None or explained"), Yes("None or explained"), Yes("None or explained"), null),
        Row("A08", "File copies to removable media are audited", Yes("Audited"), Yes("Audited"), Yes("Audited"), "Owner decision: data leakage through removable media is a risk on every station, connected ones included."),
        Row("A09", "Programs stopped or flagged by the allow-list", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("A10", "Access auditing (SACL) on classified folders", Yes("SACL present"), Yes("SACL present"), No, "Classified folders exist only in the controlled editions."),
        Row("A11", "Application audit log continuous since the previous control (anchor code)", Yes("Previous anchor found"), Yes("Previous anchor found"), Yes("Previous anchor found"), null),
        Row("A12", "Windows event log service stopped outside a shutdown", Yes("None or explained"), Yes("None or explained"), Yes("None or explained"), null),
        Row("A13", "Security officer list changed since the previous control", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("U01", "Changes to the Administrators group", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("U02", "Accounts created, deleted, enabled, disabled or reset", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("U03", "Remote (RDP) logons", Yes("Review"), Yes("Review (administration over the LAN is normal)"), Yes("Review"), null),
        Row("U04", "Series of wrong passwords (5+ in an hour)", Yes("None"), Yes("None"), Yes("None"), null),
        Row("U05", "Who worked with administrator rights", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("U06", "PowerShell scripts with commonly abused techniques", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("D01", "USB storage devices used on the station", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("D02", "Connected removable media encrypted (BitLocker To Go)", Yes("Encrypted"), Yes("Encrypted"), Yes("Encrypted"), "Owner decision: data leakage through removable media is a risk on every station, connected ones included."),
        Row("D03", "Media scanned with antivirus at connection", Yes("Scanned"), Yes("Scanned"), No, "Scan-at-connection is a controlled-station procedure."),
        Row("N01", "Isolated station never connected to any network", Yes("Never connected; non-conform on any connection"), No, No, "Only a standalone station must never touch a network; a LAN station is connected by design."),
        Row("N02", "Networks known to the station (history)", Yes("Review (none expected)"), Yes("Review (only the domain LAN expected)"), Yes("Review"), null),
        Row("N03", "Wi-Fi adapter", Yes("No active adapter; non-conform if a radio is up"), Yes("Wired LAN expected; non-conform if a radio is up"), No, "A radio is a second path out of an isolated station or LAN, but normal on an internet station."),
        Row("N04", "Bluetooth", Yes("No active radio; non-conform if the service is on"), Yes("No active radio; non-conform if the service is on"), No, "Bluetooth sends files without cable out of an isolated station or LAN."),
        Row("N05", "Phone, cellular modem or USB Ethernet adapters", Yes("None; non-conform on phone or cellular"), Yes("None; non-conform on phone or cellular (internet path)"), No, "They give an isolated station or LAN a way to the internet."),
        Row("N06", "Shared folders", Yes("None; non-conform if any"), Yes("Review (file shares may be intended)"), Yes("Review"), null),
        Row("N07", "Remote Assistance", Yes("Disabled; non-conform if allowed"), Yes("Disabled; review if allowed"), Yes("Review if allowed"), null),
        Row("N08", "Remote administration (WinRM)", Yes("Not listening; non-conform if running"), Yes("Review (domain administration may use it)"), Yes("Review"), null),
        Row("N09", "Port forwarding (portproxy)", Yes("None; non-conform if any"), Yes("Review if any"), Yes("Review if any"), null),
        Row("N10", "Internet Connection Sharing (ICS)", Yes("Off; non-conform if on"), Yes("Off; non-conform if on (a bridge to another network)"), Yes("Review if on"), null),
        Row("N11", "No path to the internet (passive configuration check)", Yes("No gateway, DNS, proxy, VPN, radio, cellular or ICS configuration found; non-conform on any"), Yes("Only internal gateway and DNS; non-conform on proxy, VPN, radio, cellular or ICS leading outside the internal ranges"), No, "Reads local configuration only (registry, adapter properties, Plug and Play device list, local files; no WMI) and never opens a socket. Any source it cannot read makes it NEDETERMINAT (never CONFORM) and is named in the detail. An internet station is connected by design."),
        Row("N12", "Domain membership", No, Yes("Member of the domain"), No, "Only a station on an isolated domain LAN is expected to be a domain member."),
        Row("N13", "Domain controller reachable (active, read-only check)", No, Yes("Located at an internal address and LDAP answers; non-conform on a public address"), No, "Active check: Windows' domain locator (DNS SRV query to the configured DNS server, Netlogon UDP ping) plus one TCP connection to LDAP 389 at a private address only, no credentials; it sends traffic, so it exists only in the Network edition."),
        Row("S01", "Programs installed or uninstalled", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("S02", "New services and scheduled tasks", Yes("Review"), Yes("Review"), Yes("Review"), null),
        Row("S03", "Windows version and last update", Yes("Known and documented"), Yes("Known and documented"), Yes("Recent"), null),
        Row("S04", "Offline updates applied and update register", Yes("Register matches installed updates"), Yes("Register matches installed updates"), No, "The offline update register exists only where updates cannot come online."),
        Row("S05", "Station clock versus the application's time memory", Yes("Consistent"), Yes("Consistent"), Yes("Consistent"), null),
    ];
    // END GENERATED TABLE

    private static readonly Dictionary<string, ApplicabilityEntry> ById = All.ToDictionary(e => e.Id, StringComparer.Ordinal);

    public static bool TryGet(string id, out ApplicabilityEntry entry) => ById.TryGetValue(id, out entry!);

    /// <summary>The rule for one check in one posture. An id without an entry is a programming error and throws: there is no silent default.</summary>
    public static PostureRule Rule(string id, StationPosture posture) =>
        ById.TryGetValue(id, out var e) ? e.For(posture) : throw new InvalidOperationException($"Verificarea {id} nu are o intrare în tabelul de aplicabilitate pe posturi.");

    public static bool Applies(string id, StationPosture posture) => Rule(id, posture).Applies;

    /// <summary>Replaces the checks that do not apply to the posture by a NuSeAplica row that says why. Applicable checks are returned untouched.</summary>
    public static ControlCheck Resolve(ControlCheck check, StationPosture posture)
    {
        if (Applies(check.Id, posture)) return check;
        var entry = ById[check.Id];
        var why = entry.Reason is { Length: > 0 } r ? " " + r : "";
        return check with
        {
            Status = ControlStatus.NuSeAplica,
            Detail = $"Nu se aplică în ediția „{StationPostures.EditionLabel(posture)}”.{why}",
            Evidence = [],
            Recommendation = "",
            Subjects = null,
        };
    }
}
