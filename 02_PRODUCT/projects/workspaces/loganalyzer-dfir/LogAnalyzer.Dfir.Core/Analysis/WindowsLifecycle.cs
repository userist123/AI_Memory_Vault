using System.Globalization;
using System.Text.Json;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>How a Windows build stands against its end-of-servicing date, on the COLLECTION date (not today).</summary>
public enum LifecycleState { Past, Near, Current, UnknownBuild, UnknownEdition }

/// <summary><paramref name="EndOfServicing"/> is null for an unknown build or edition; <paramref name="DaysToEnd"/> is negative once the date has passed.</summary>
public sealed record LifecycleResult(LifecycleState State, string Name, string Family, DateOnly? EndOfServicing, int DaysToEnd);

/// <summary>
/// Planner #14 E4: the DATED table Data/windows_lifecycle.json (build, edition family, end-of-servicing date, copied by hand from Microsoft's public lifecycle pages).
/// The table has a date and a source note that every finding prints, because the table can be out of date. A build that is not in it (Insider, Canary, a newer release)
/// gives <see cref="LifecycleState.UnknownBuild"/>, never a verdict; an edition outside the two families (LTSC, Server) gives <see cref="LifecycleState.UnknownEdition"/>.
/// </summary>
public static class WindowsLifecycle
{
    private sealed record Row(int Build, string Name, DateOnly HomePro, DateOnly EnterpriseEducation);
    private sealed record Table(string TableDate, string Source, int NearDays, HashSet<string> HomePro, HashSet<string> EnterpriseEducation, IReadOnlyList<Row> Builds);

    private static readonly Lazy<Table> Shipped = new(() => Parse(ShippedData.ReadEmbedded("windows_lifecycle.json")));

    public static string TableDate => Shipped.Value.TableDate;
    public static string Source => Shipped.Value.Source;
    public static int NearDays => Shipped.Value.NearDays;
    public static IReadOnlyCollection<int> KnownBuilds => Shipped.Value.Builds.Select(b => b.Build).ToList();

    private static DateOnly D(JsonElement e, string n) => DateOnly.ParseExact(e.GetProperty(n).GetString() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Table Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        HashSet<string> Set(string n) => new(r.GetProperty("families").GetProperty(n).EnumerateArray().Select(x => x.GetString() ?? ""), StringComparer.OrdinalIgnoreCase);
        var builds = r.GetProperty("builds").EnumerateArray().Select(b => new Row(b.GetProperty("build").GetInt32(), b.GetProperty("name").GetString() ?? "", D(b, "homePro"), D(b, "enterpriseEducation"))).ToList();
        return new Table(r.GetProperty("tableDate").GetString() ?? "", r.GetProperty("source").GetString() ?? "", r.TryGetProperty("nearDays", out var nd) ? nd.GetInt32() : 60,
            Set("homePro"), Set("enterpriseEducation"), builds);
    }

    /// <summary>Looks one build and edition up. <paramref name="collectedUtc"/> is the collection time; the comparison uses its UTC calendar date.</summary>
    public static LifecycleResult Evaluate(int build, string editionId, DateTimeOffset collectedUtc)
    {
        var t = Shipped.Value;
        var row = t.Builds.FirstOrDefault(b => b.Build == build);
        if (row is null) return new LifecycleResult(LifecycleState.UnknownBuild, "", "", null, 0);
        string edition = (editionId ?? "").Trim();
        DateOnly end; string family;
        if (t.HomePro.Contains(edition)) { end = row.HomePro; family = "Home/Pro"; }
        else if (t.EnterpriseEducation.Contains(edition)) { end = row.EnterpriseEducation; family = "Enterprise/Education"; }
        else return new LifecycleResult(LifecycleState.UnknownEdition, row.Name, "", null, 0);
        int days = end.DayNumber - DateOnly.FromDateTime(collectedUtc.UtcDateTime).DayNumber;
        var state = days < 0 ? LifecycleState.Past : days <= t.NearDays ? LifecycleState.Near : LifecycleState.Current;
        return new LifecycleResult(state, row.Name, family, end, days);
    }
}
