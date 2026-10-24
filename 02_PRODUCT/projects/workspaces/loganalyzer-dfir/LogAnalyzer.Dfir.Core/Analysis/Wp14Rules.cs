using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>What the WP14a rules compare the timeline with: the case scope, the registers, the procedure profile and the system's zone.</summary>
public sealed class Wp14Input
{
    public CaseScope Scope { get; init; } = new();
    /// <summary>The media register; null or without rows = "registru nedefinit".</summary>
    public MediaRegister? Media { get; init; }
    public UsersRegister? Users { get; init; }
    /// <summary>The approved transfer register; null or without rows = no exit of a classified file can be shown as approved.</summary>
    public TransferRegister? Transfers { get; init; }
    /// <summary>The station's declared time zone (case info); null = not declared, so the transfer register's times cannot be compared.</summary>
    public TimeZoneInfo? StationZone { get; init; }
    public ProcedureProfile? Profile { get; init; }
    public Wp14Data? Data { get; init; }
    /// <summary>The zone the analysed system is in, as entered by the operator; "" = not stated.</summary>
    public string SystemZone { get; init; } = "";
    /// <summary>What the facts collector read from the station (adapters, burn tooling, printers, screenshots, virtual machines, archivers, camera / microphone); null = not collected.</summary>
    public AirGapFacts? Facts { get; init; }
    /// <summary>E1: evidence id → origin label for sources that are not the current system (archived log, Windows.old, shadow copy); see <see cref="EvidenceOrigin"/>.</summary>
    public IReadOnlyDictionary<string, string>? EvidenceOrigins { get; init; }
    /// <summary>Installation date of the application on the analysed station (first chained record of its audit chain); null = take it from <see cref="Facts"/>, else unknown.</summary>
    public DateTimeOffset? AppInstalledUtc { get; init; }
    public string AppInstallBasis { get; init; } = "";
    /// <summary>What contradicts the installation date; null = take it from <see cref="Facts"/>.</summary>
    public IReadOnlyList<string>? AppInstallViolations { get; init; }
    /// <summary>DE VERIFICAT notes about the installation date; null = take them from <see cref="Facts"/>.</summary>
    public IReadOnlyList<string>? AppInstallToVerify { get; init; }
    /// <summary>The sources present in the case ("EventLog:&lt;channel&gt;", "SRUM", "Firewall:pfirewall.log", "AirGapFacts"); null = not known, then a source counts as present when the timeline has a row from it.</summary>
    public IReadOnlyCollection<string>? CollectedSources { get; init; }
}

/// <summary>
/// WP14a rules over the unified timeline: removable media against the media register (MEDIA-*), CD/DVD as a separate artifact, and NIC / Wi-Fi /
/// Bluetooth / DHCP on an air-gapped or standalone scope (AIRGAP-*), in the "Air-gap integrity" category. Every rule states what the record shows and
/// what it does not: presence is not copying, an empty register is "registru nedefinit" (never "conform"), the absence of observations is never a clean result.
/// </summary>
public static partial class Wp14Rules
{
    public const string Category = "Air-gap integrity";
    public const string RemovableCategory = "Removable media";

    public static List<Finding> Run(IReadOnlyList<TimelineEvent> events, Func<string> nextId, Wp14Input input)
    {
        var c = new Ctx(events, nextId, input, input.Data ?? Wp14Data.Default);
        var found = new List<Finding>();
        Media(c, found);
        MediaUsageFindings(c, found);
        DeletedTraceFindings(c, found);
        HistoryFindings(c, found);        // E1: every source, earliest evidence first
        AfterConnectFindings(c, found);   // E5: what followed each connection (linked to the medium's findings above)
        Optical(c, found);
        AirGap(c, found);
        FirewallLogPathFindings(c, found);   // review of PR #288: a refused firewall-log path is reported in every scope
        Channels(c, found);
        PayloadFindings(c, found);        // P1: shortcuts and hidden folders on media, execution from media, Startup folder, Run-dialog commands
        ClassifiedFindings(c, found);     // classified folders: accesses and exits against the transfer register
        UserActivityFindings(c, found);   // E7: ShellBags / RecentDocs / Office MRU / Recycle Bin against the classified folders and removable volumes
        return found;
    }

    internal sealed class Ctx(IReadOnlyList<TimelineEvent> events, Func<string> nextId, Wp14Input input, Wp14Data data)
    {
        public IReadOnlyList<TimelineEvent> Events { get; } = events;
        public Func<string> NextId { get; } = nextId;
        public Wp14Input Input { get; } = input;
        public CaseScope Scope => Input.Scope;
        public Wp14Data Data { get; } = data;
        public AirGapFacts? Facts => Input.Facts;
        /// <summary>True when the time is inside the case period (an open end is open); an unknown time is not inside.</summary>
        public bool InPeriod(DateTimeOffset? t) => t is { } x && (Scope.PeriodFromUtc is not { } a || x >= a) && (Scope.PeriodToUtc is not { } b || x <= b);
        public bool Classified => Scope.Classification == ClassificationLevel.Classified;
        public bool AirGapScope => Scope.Network is NetworkCategory.AirGappedNetwork or NetworkCategory.StandalonePc;
        /// <summary>The Standalone posture (owner directive 2026-10-16): no network is ever authorised after the application was installed.</summary>
        public bool Standalone => Scope.Network == NetworkCategory.StandalonePc;
        public DateTimeOffset? AppInstalledUtc => Input.AppInstalledUtc ?? Facts?.AppInstalledUtc;
        public string AppInstallBasis =>
            Input.AppInstalledUtc is not null && Input.AppInstallBasis.Length > 0 ? Input.AppInstallBasis
            : Facts is { AppInstallBasis.Length: > 0 } f ? f.AppInstallBasis
            : Input.AppInstallBasis.Length > 0 ? Input.AppInstallBasis
            : "sursă lipsă: data instalării aplicației, de ce lipsește: faptele stației (AirGapFacts) nu sunt în acest caz, cum se pornește: colectarea pe stația pe care este instalată aplicația (colectorul AirGapFacts citește jurnalul de audit al aplicației)";
        public IReadOnlyList<string> AppInstallToVerify => Input.AppInstallToVerify ?? (IReadOnlyList<string>?)Facts?.AppInstallToVerify ?? [];
        public IReadOnlyList<string> AppInstallViolations => Input.AppInstallViolations ?? (IReadOnlyList<string>?)Facts?.AppInstallViolations ?? [];
        /// <summary>True when the source is in the case (or, when the case did not say, when the timeline has a row from it).</summary>
        public bool Has(string source) => Input.CollectedSources is { } cs
            ? cs.Contains(source, StringComparer.OrdinalIgnoreCase)
            : Events.Any(e => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        public ProcedureProfile? Profile => Input.Profile;
        public string CaseClassText => Scope.Classification switch
        {
            ClassificationLevel.Classified => "sistem clasificat (domeniul cazului)",
            ClassificationLevel.Unclassified => "sistem neclasificat (domeniul cazului)",
            _ => "clasificarea sistemului nu este specificată în domeniul cazului",
        };
        public string FindingCategory => AirGapScope ? Category : RemovableCategory;
    }

    // ---- helpers ----

    /// <summary>The severity on a classified scope, one level lower on any other (owner rule of WP14a, item 4).</summary>
    internal static Severity Sev(Ctx c, Severity classified) => c.Classified ? classified : classified > Severity.Info ? classified - 1 : Severity.Info;

    /// <summary>True when the row came from an event log whose channel name contains <paramref name="part"/> and has one of the ids.</summary>
    internal static bool InChannel(TimelineEvent e, string part, params int[] ids) =>
        e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) && e.Source.Contains(part, StringComparison.OrdinalIgnoreCase) &&
        (ids.Length == 0 || (int.TryParse(e.EventId, out var id) && ids.Contains(id)));

    internal static string Instance(TimelineEvent e) => F(e, "DeviceInstanceId", "InstanceId", "DeviceId", "InstanceID");

    private static readonly Regex UsbstorRx = new(@"USBSTOR\\[^\\\s""]+\\(?<s>[^\\\s""]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>Serial segment of a USBSTOR instance id ("USBSTOR\DISK&amp;VEN_…\AA001&amp;0" -> "AA001&amp;0"); "" when the text is not a USBSTOR instance.</summary>
    internal static string UsbstorSerial(string text) => UsbstorRx.Match(text) is { Success: true } m ? m.Groups["s"].Value : "";
}
