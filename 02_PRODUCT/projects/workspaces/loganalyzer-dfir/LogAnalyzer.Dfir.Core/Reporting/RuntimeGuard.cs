namespace LogAnalyzer.Dfir.Reporting;

/// <summary>
/// SignedCms in .NET 10.0.0 – 10.0.12 has a denial-of-service flaw in signature checking (CVE-2026-84008, fixed in 10.0.13; see
/// docs/dfir/PROVENANCE_2026-10-16_PDF_SIGNING_LIBRARIES.md). The fix arrives with the station's .NET runtime, so signing and verification say so when it is older.
/// </summary>
public static class RuntimeGuard
{
    public static readonly Version Minimum = new(10, 0, 13);

    /// <summary>The warning to show for <paramref name="runtime"/> (normally <see cref="Environment.Version"/>); null when it is recent enough.</summary>
    public static string? Warning(Version runtime) =>
        runtime.Major == Minimum.Major && runtime < Minimum
            ? $"actualizați .NET la 10.0.13+ (remediere de securitate pentru verificarea semnăturilor); versiunea de acum: {runtime}"
            : null;
}
