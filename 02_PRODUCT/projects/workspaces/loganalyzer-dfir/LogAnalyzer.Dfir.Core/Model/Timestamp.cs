namespace LogAnalyzer.Dfir.Model;

/// <summary>
/// A normalized point in time that never destroys the original value (spec §61, §113, §153).
/// <see cref="Utc"/> is null when the source carried no usable time: callers must not substitute "now".
/// </summary>
public sealed record Timestamp(DateTimeOffset? Utc, string Raw, string ConversionMethod)
{
    public static Timestamp Unknown(string raw = "") => new(null, raw, "none");

    public static Timestamp FromUtc(DateTime utc, string raw, string method)
    {
        if (utc.Kind == DateTimeKind.Local) throw new ArgumentException("Expected UTC or unspecified-as-UTC time.", nameof(utc));
        return new(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), raw, method);
    }

    public static Timestamp FromFileTime(long fileTime, string method)
        => fileTime <= 0 ? Unknown(fileTime.ToString(System.Globalization.CultureInfo.InvariantCulture))
                         : FromUtc(DateTime.FromFileTimeUtc(fileTime), fileTime.ToString(System.Globalization.CultureInfo.InvariantCulture), method);

    public bool IsKnown => Utc is not null;

    /// <summary>Local rendering for a given zone; UTC stays the source of truth.</summary>
    public DateTimeOffset? ToLocal(TimeZoneInfo zone) => Utc is { } u ? TimeZoneInfo.ConvertTime(u, zone) : null;

    public string UtcIso => Utc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public string LocalIso(TimeZoneInfo zone) => ToLocal(zone)?.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
