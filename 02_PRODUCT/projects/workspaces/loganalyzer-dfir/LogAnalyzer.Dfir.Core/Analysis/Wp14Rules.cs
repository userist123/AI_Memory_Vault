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
    public ProcedureProfile? Profile { get; init; }
    public Wp14Data? Data { get; init; }
    /// <summary>The zone the analysed system is in, as entered by the operator; "" = not stated.</summary>
    public string SystemZone { get; init; } = "";
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
        Optical(c, found);
        AirGap(c, found);
        return found;
    }

    internal sealed class Ctx(IReadOnlyList<TimelineEvent> events, Func<string> nextId, Wp14Input input, Wp14Data data)
    {
        public IReadOnlyList<TimelineEvent> Events { get; } = events;
        public Func<string> NextId { get; } = nextId;
        public Wp14Input Input { get; } = input;
        public CaseScope Scope => Input.Scope;
        public Wp14Data Data { get; } = data;
        public bool Classified => Scope.Classification == ClassificationLevel.Classified;
        public bool AirGapScope => Scope.Network is NetworkCategory.AirGappedNetwork or NetworkCategory.StandalonePc;
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
