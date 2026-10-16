using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// Station setting for the recognition of removable media (finding B7), stored like the other per-user station settings (report signing): a small JSON file
/// %APPDATA%\LogAnalyzer\media_matching.json, read locally (no network, no configuration server). "capacity_tolerance" is the relative difference accepted
/// between the capacity in the signed register and the one observed (default 0.02, accepted 0..0.10). The default is NOT lab-verified: a value that gives a
/// false "posibilă clonare" on the station's media must be set after a test in the lab. An out-of-range or unreadable value is replaced by the default and
/// the warning is shown in check D04; it never widens the matching.
/// </summary>
public sealed class MediaMatchSettings
{
    [JsonPropertyName("capacity_tolerance")] public double CapacityTolerance { get; set; } = SignedMediaMatcher.DefaultCapacityTolerance;

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogAnalyzer", "media_matching.json");

    /// <summary>The tolerance to use and the warning to show (null = none). A missing file is the default without a warning.</summary>
    public static (double Tolerance, string? Warning) Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return (SignedMediaMatcher.DefaultCapacityTolerance, null);
            var s = JsonSerializer.Deserialize<MediaMatchSettings>(File.ReadAllText(path));
            if (s is null) return (SignedMediaMatcher.DefaultCapacityTolerance, "Setarea toleranței de capacitate nu poate fi citită; se folosește valoarea implicită " + SignedMediaMatcher.DefaultCapacityTolerance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".");
            var t = SignedMediaMatcher.ResolveTolerance(s.CapacityTolerance, out var warning);
            return (t, warning);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (SignedMediaMatcher.DefaultCapacityTolerance, "Setarea toleranței de capacitate nu poate fi citită (" + ex.Message + "); se folosește valoarea implicită " + SignedMediaMatcher.DefaultCapacityTolerance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".");
        }
    }
}
