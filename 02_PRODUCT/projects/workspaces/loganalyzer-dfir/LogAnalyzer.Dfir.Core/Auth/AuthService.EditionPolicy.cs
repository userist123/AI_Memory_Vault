using System.Globalization;
using System.Text.Json;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>
/// Security audit #18: the anti-rollback high-water mark of the signed edition policy is kept in the hash-chained auth audit, next to the plain
/// <c>policy.highwater</c> file. Removing an audit line breaks the (sealed) chain, which sign-in reports; deleting the file alone no longer lowers
/// the mark, because the effective mark is the maximum of both.
/// </summary>
public sealed partial class AuthService
{
    public const string EditionPolicyVersionEvent = "edition.policy_version";
    private const string VersionPrefix = "version=";

    /// <summary>Highest edition-policy version recorded in the audit at <paramref name="auditPath"/> (0 when none). Unreadable lines are skipped.</summary>
    public static long EditionPolicyHighWater(string auditPath)
    {
        long max = 0;
        IEnumerable<string> lines;
        try { lines = File.Exists(auditPath) ? File.ReadAllLines(auditPath) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        foreach (var line in lines)
        {
            if (!line.Contains(EditionPolicyVersionEvent, StringComparison.Ordinal)) continue;
            AuthAuditEntry? e;
            try { e = JsonSerializer.Deserialize<AuthAuditEntry>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException) { continue; }
            if (e is null || e.Event != EditionPolicyVersionEvent || !e.Reason.StartsWith(VersionPrefix, StringComparison.Ordinal)) continue;
            if (long.TryParse(e.Reason.AsSpan(VersionPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > max) max = v;
        }
        return max;
    }

    /// <summary>Records <paramref name="version"/> in the audit when it is higher than the recorded mark; returns whether an entry was written.</summary>
    public bool RecordEditionPolicyVersion(long version, string detail = "")
    {
        lock (_gate)
        {
            if (version <= EditionPolicyHighWater(AuditPath)) return false;
            Audit(EditionPolicyVersionEvent, "aplicație", "", "ok", VersionPrefix + version.ToString(CultureInfo.InvariantCulture),
                  detail.Length > 0 ? detail : "politica de ediție semnată acceptată; versiunile mai vechi sunt refuzate de acum înainte");
            return true;
        }
    }
}
