using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Evaluation 2026-11-05: programs installed per user by their publisher (browser, AI clients, chat) under the profile were raised as high by
/// the user-writable-location rules, which on an ordinary PC buried the real signals. On a live collection the file named by such a finding is
/// checked now on the station; a valid Authenticode signature lowers it ONE level and the finding says so (limitation + audit trail).
/// Nothing is lowered when the file is gone, unsigned or badly signed, when any of the finding's evidence was imported or collected from
/// another host (review: \users\… of host B must not be checked on this PC), or for EXEC-USERPATH-CORRELATED, which already means "tied to a
/// serious finding": a signed program in AppData next to a DLL Defender flagged (side-loading, T1574.002) must stay high.
/// </summary>
public static class SignatureCalibration
{
    public static readonly IReadOnlySet<string> Rules = new HashSet<string>(StringComparer.Ordinal) { "EXEC-USERPATH", "NET-USERPATH-UPLOAD" };

    public static IEnumerable<Finding> Apply(IEnumerable<Finding> findings, Func<string, bool> exists, Func<string, SignatureInfo> verify) =>
        Apply(findings, exists, verify, p => IsLocalPath(p) ? p : null, _ => true);

    /// <param name="resolve">Turns an artifact path (\device\harddiskvolumeN\…, \VOLUME{…-serial}\…, \users\…) into a local one, or null.</param>
    /// <param name="fromThisStation">True only when every piece of evidence of the finding was collected live on this station.</param>
    public static IEnumerable<Finding> Apply(IEnumerable<Finding> findings, Func<string, bool> exists, Func<string, SignatureInfo> verify,
                                             Func<string, string?> resolve, Func<Finding, bool> fromThisStation)
    {
        var cache = new Dictionary<string, SignatureInfo?>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in findings)
        {
            if (!Rules.Contains(f.RuleId) || f.Severity < Severity.High || f.File.Length == 0 || !fromThisStation(f)) { yield return f; continue; }
            var local = resolve(f.File);
            if (local is null) { yield return f; continue; }
            if (!cache.TryGetValue(local, out var sig))
            {
                sig = null;
                try { if (exists(local)) sig = verify(local); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
                cache[local] = sig;
            }
            if (sig is { IsSigned: true, IsValid: true } && sig.Signer.Trim().Length > 0)
            {
                var lower = f.Severity == Severity.Critical ? Severity.High : Severity.Medium;
                yield return f.WithSeverity(lower,
                    $"Fișierul {local}, verificat la colectare pe această stație, are semnătură digitală validă ({sig.Kind}, {sig.Signer.Trim()}); severitatea a fost coborâtă cu un nivel. " +
                    "Revocarea certificatului nu a fost verificată (stație fără rețea), iar semnătura de acum nu dovedește că fișierul de la momentul execuției era același.");
            }
            else yield return f;
        }
    }

    /// <summary>A drive-letter path ("C:\…"); other forms go through <see cref="ArtifactPathResolver"/>.</summary>
    internal static bool IsLocalPath(string path) =>
        path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
}
