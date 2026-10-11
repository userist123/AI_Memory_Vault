using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>Parts of a storage-medium label <c>xxxx-yyyyL</c> (unit code, registration number, classification letters). Letters are kept raw: no meaning is invented.</summary>
public sealed record MediaLabelParts(string UnitCode, string RegistrationNumber, string Classification, string Raw);

/// <summary>Owner's medium label convention: <c>xxxx-yyyyL</c>, e.g. <c>1234-56SV</c>.</summary>
public static partial class MediaLabel
{
    [GeneratedRegex(@"^(?<unit>\d+)-(?<reg>\d+)(?<cls>[A-Za-z]{1,3})$", RegexOptions.CultureInvariant)] private static partial Regex Rx();

    public static bool TryParse(string? label, out MediaLabelParts parts)
    {
        var raw = (label ?? "").Trim();
        var m = Rx().Match(raw);
        if (!m.Success) { parts = new MediaLabelParts("", "", "", raw); return false; }
        parts = new MediaLabelParts(m.Groups["unit"].Value, m.Groups["reg"].Value, m.Groups["cls"].Value.ToUpperInvariant(), raw);
        return true;
    }
}

public enum StationNameKind { Standalone, Networked, Unknown }

/// <summary>A parsed station name. Standalone: <c>1234DOC5143SV</c>; networked: <c>NETWS01</c>. Unused parts are empty.</summary>
public sealed partial record StationName(StationNameKind Kind, string Raw, string UnitCode, string Compartment, string RegistrationNumber, string Classification, string Network, string Number)
{
    [GeneratedRegex(@"^(?<unit>\d+)(?<cmp>[A-Za-z]+)(?<reg>\d+)(?<cls>[A-Za-z]{1,3})$", RegexOptions.CultureInvariant)] private static partial Regex StandaloneRx();
    [GeneratedRegex(@"^(?<net>[A-Za-z]+?)WS(?<num>\d+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)] private static partial Regex NetworkedRx();

    public static StationName Parse(string? name)
    {
        var raw = (name ?? "").Trim();
        var s = StandaloneRx().Match(raw);
        if (s.Success)
            return new StationName(StationNameKind.Standalone, raw, s.Groups["unit"].Value, s.Groups["cmp"].Value.ToUpperInvariant(), s.Groups["reg"].Value, s.Groups["cls"].Value.ToUpperInvariant(), "", "");
        var n = NetworkedRx().Match(raw);
        if (n.Success)
            return new StationName(StationNameKind.Networked, raw, "", "", "", "", n.Groups["net"].Value.ToUpperInvariant(), n.Groups["num"].Value);
        return new StationName(StationNameKind.Unknown, raw, "", "", "", "", "", "");
    }

    /// <summary>One Romanian line describing what the name says.</summary>
    public string Describe() => Kind switch
    {
        StationNameKind.Standalone => $"Stație izolată: cod unitate {UnitCode}, compartiment {Compartment}, număr de înregistrare {RegistrationNumber}, clasificare {Classification}",
        StationNameKind.Networked => $"Stație în rețea: rețeaua {Network}, stația de lucru {Number}",
        _ => "Numele stației nu urmează convenția",
    };
}
