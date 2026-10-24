using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>Which station the evidence comes from, as the evidence itself says (senior review I1).</summary>
public sealed record StationIdentityResult(string Station, string Line, IReadOnlyList<(string Host, int Events)> Hosts);

public static class StationIdentity
{
    /// <summary>
    /// The station named by the events (their Computer / host field), not the computer the analysis ran on: for imported evidence the case host is the
    /// examiner's machine. Several stations in the evidence are all listed; with no host in the evidence, the case host is used and the line says so.
    /// </summary>
    public static StationIdentityResult FromEvidence(IEnumerable<TimelineEvent> timeline, string analysisHost)
    {
        static string Short(string h) => (h.Contains('.') ? h[..h.IndexOf('.')] : h).Trim().ToUpperInvariant();
        var hosts = timeline.Select(e => e.Host).Where(h => !string.IsNullOrWhiteSpace(h) && h != "-")
                            .GroupBy(Short).Select(g => (Host: g.Key, Events: g.Count())).OrderByDescending(x => x.Events).ToList();
        if (hosts.Count == 0)
            return new(analysisHost, $"stația {analysisHost} (numele nu apare în probe; s-a folosit numele calculatorului pe care s-a făcut colectarea)", hosts);
        var main = hosts[0].Host;
        var same = main.Equals(Short(analysisHost), StringComparison.OrdinalIgnoreCase);
        var line = hosts.Count == 1
            ? $"stația {main}" + (same ? "" : $" (din probe; analiza a rulat pe {analysisHost})")
            : $"stațiile din probe: {string.Join(", ", hosts.Take(5).Select(h => $"{h.Host} ({LogAnalyzer.Dfir.Language.RoText.Count(h.Events, "eveniment", "evenimente")})"))}" + (hosts.Count > 5 ? "…" : "") + $"; analiza a rulat pe {analysisHost}";
        return new(main, line, hosts);
    }
}
