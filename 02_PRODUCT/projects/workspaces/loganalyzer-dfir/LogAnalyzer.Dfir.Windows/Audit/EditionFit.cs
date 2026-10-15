using LogAnalyzer.Core.Services.Edition;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// Owner decision: the Internet edition run on a station that looks isolated is the wrong edition. The test uses the same PASSIVE facts as N11
/// (no socket, no name resolution): the station looks isolated when it has no ACTIVE way out of any kind (no gateway on an adapter that is up,
/// no active proxy, VPN, tunnel, radio or cellular modem). An internal gateway counts as a way out, because a NAT router behind it may reach the
/// internet. Unknown facts give no warning (never guessed). The warning is information only: it never changes a check result.
/// </summary>
public static class EditionFit
{
    public const string WrongEditionText =
        "Ediție nepotrivită: stația pare izolată. Pentru o stație fără rețea folosiți LogAnalyzer.Standalone, pentru o rețea internă fără internet LogAnalyzer.Retea.";

    /// <summary>The warning for these facts, or null. Only the Internet edition is ever warned; <paramref name="paths"/> null = not collected.</summary>
    public static string? Warning(StationPosture posture, IReadOnlyList<EgressPathFact>? paths)
    {
        if (posture != StationPosture.Internet || paths is null) return null;
        return paths.Any(p => p.Active) ? null : WrongEditionText;
    }

    /// <summary>Collects the passive facts and returns the warning; only on the Internet edition (nothing is read on the other editions).</summary>
    public static string? WarningForThisStation(StationPosture posture)
    {
        if (posture != StationPosture.Internet) return null;
        var unread = new List<string>();
        var paths = EgressPathCollector.Collect(unread);
        return unread.Count > 0 ? null : Warning(posture, paths);   // a source that could not be read: no guess
    }
}
