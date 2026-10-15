using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>The recorded form "officer_state=store:&lt;sha|none&gt;,trust:&lt;sha|none&gt;,files:&lt;a|b&gt;" and its parts.</summary>
/// <summary>A13: whether it could be assessed, files changed outside the application, and the changes made by the application (for review).</summary>
public sealed record OfficerStateAssessment(bool Determinable, List<string> Outside, List<string> Changes, string Since);

public sealed record OfficerStateSnapshot(string Detail, string Store, string Trust, IReadOnlyList<string> TrustFiles)
{
    public bool IsEmpty => Store == "none" && Trust == "none";
}

/// <summary>
/// The state of what decides who may sign as security officer: the SHA-256 of the officer store file and of the qualified-signature trust folder (CA
/// certificates and CRLs, by relative name and content). The application appends it to every officer / trust event and to every anchor of the sealed audit;
/// check A13 recomputes it and compares with the last recorded one, so a file edited or dropped by hand, outside the application, shows as NECONFORM.
/// </summary>
public static class OfficerState
{
    public const string Prefix = "officer_state=";

    public static OfficerStateSnapshot Compute(string storePath, string trustDir)
    {
        string store = "none";
        try { if (File.Exists(storePath)) store = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(storePath))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { store = "unreadable"; }
        var files = new List<string>();
        var lines = new StringBuilder();
        try
        {
            if (Directory.Exists(trustDir))
                foreach (var f in Directory.EnumerateFiles(trustDir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(trustDir, f).Replace('\\', '/')).Order(StringComparer.Ordinal).Take(500))
                {
                    files.Add(f);
                    lines.Append(f).Append(':').Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(trustDir, f))))).Append('\n');
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { lines.Append("unreadable"); }
        var trust = lines.Length == 0 ? "none" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString())));
        var detail = $"{Prefix}store:{store},trust:{trust},files:{string.Join('|', files.Select(Clean))}";
        return new OfficerStateSnapshot(detail, store, trust, files);
    }

    /// <summary>The state now, at the default places (%PROGRAMDATA%\LogAnalyzer\officers and \officer_trust).</summary>
    public static OfficerStateSnapshot Current() => Compute(OfficerStore.Shared.Path, QualifiedSignatureTrust.DefaultDir);

    private static string Clean(string name) => Regex.Replace(name, @"[,;|\s]", "_");

    public static OfficerStateSnapshot? Parse(string? detail)
    {
        if (detail is null) return null;
        var m = Regex.Match(detail, Prefix + @"store:([^,]*),trust:([^,]*),files:(.*)$");
        if (!m.Success) return null;
        var files = m.Groups[3].Value.Length == 0 ? [] : m.Groups[3].Value.Split('|').ToList();
        return new OfficerStateSnapshot(m.Value, m.Groups[1].Value, m.Groups[2].Value, files);
    }

    /// <summary>Events that legitimately change the officer list or the trust folder (sealed, made by the application's own flows).</summary>
    public static readonly IReadOnlySet<string> ChangeEvents = new HashSet<string> { LogAnalyzer.Dfir.Auth.OperationAudit.OfficerRegistered, LogAnalyzer.Dfir.Auth.OperationAudit.OfficerRequested, LogAnalyzer.Dfir.Auth.OperationAudit.OfficerRemoved, LogAnalyzer.Dfir.Auth.OperationAudit.OfficerTrustImported };

    /// <summary>
    /// Check A13. The baseline is the state recorded with the anchor whose code was TYPED from the signed paper (entry <paramref name="paperAnchorSeq"/>); after
    /// it, a new state is accepted only from an officer / trust event of the application. A state that changes on any other event (an anchor, an import that
    /// was refused…) or differs now from the last accepted one was made outside the application. Without a typed code nothing can be asserted.
    /// </summary>
    public static OfficerStateAssessment Assess(string auditPath, long? paperAnchorSeq, string? nowDetail)
    {
        if (paperAnchorSeq is not { } n)
            return new(false, [], [], "nu s-a introdus codul de ancorare de pe procesul-verbal, deci nu există o stare de referință sigură");
        var events = LogAnalyzer.Dfir.Case.AuditAnchor.Events(auditPath);
        // the baseline: the anchor event written right after entry N (it carries the state at N); otherwise the last state recorded up to N
        var anchorEvent = events.FirstOrDefault(e => e.Seq > n && e.Event == LogAnalyzer.Dfir.Auth.OperationAudit.AnchorRecorded && System.Text.RegularExpressions.Regex.IsMatch(e.Detail, $@"(^|; )seq={n};"));
        string expected; long from;
        if (anchorEvent is not null && StateOf(anchorEvent.Detail) is { } st0) { expected = st0; from = anchorEvent.Seq; }
        else
        {
            var before = events.LastOrDefault(e => e.Seq <= n && StateOf(e.Detail) is not null);
            expected = before is null ? $"{Prefix}store:none,trust:none,files:" : StateOf(before.Detail)!;
            from = n;
        }
        var outside = new List<string>();
        var changes = new List<string>();
        foreach (var e in events.Where(e => e.Seq > from))
        {
            var st = StateOf(e.Detail);
            if (ChangeEvents.Contains(e.Event))
            {
                changes.Add($"înregistrarea nr. {e.Seq}, {e.WhenUtc}, {e.Event}: {Strip(e.Detail)} (de {e.Who})");
                if (st is not null) expected = st;
                continue;
            }
            if (st is not null && st != expected && outside.Count < 20)
                foreach (var d in Differences(expected, st)) outside.Add($"la înregistrarea nr. {e.Seq} ({e.Event}) starea nu mai corespunde, fără o operație a aplicației: {d}");
        }
        if (nowDetail is not null) outside.AddRange(Differences(expected, nowDetail).Select(d => "acum: " + d));
        return new(true, outside, changes, $"de la înregistrarea nr. {n} (codul de pe procesul-verbal)");
    }

    private static string? StateOf(string detail)
    {
        int i = detail.IndexOf(Prefix, StringComparison.Ordinal);
        return i < 0 ? null : detail[i..];
    }

    private static string Strip(string detail)
    {
        int i = detail.IndexOf("; " + Prefix, StringComparison.Ordinal);
        return i < 0 ? detail : detail[..i];
    }

    /// <summary>The differences between the recorded and the current state, in words; empty when they are the same.</summary>
    public static List<string> Differences(string? recorded, string? now)
    {
        var diff = new List<string>();
        var n = Parse(now);
        if (n is null) return diff;
        var r = Parse(recorded) ?? Parse($"{Prefix}store:none,trust:none,files:")!;
        if (r.Store != n.Store) diff.Add($"lista ofițerilor de securitate (security_officers.json) diferă de ultima stare înregistrată de aplicație (înregistrat {Short(r.Store)}, acum {Short(n.Store)})");
        if (r.Trust != n.Trust)
        {
            var added = n.TrustFiles.Except(r.TrustFiles).ToList();
            var removed = r.TrustFiles.Except(n.TrustFiles).ToList();
            diff.Add("autoritățile de încredere sau listele de revocare diferă de ultima stare înregistrată" +
                     (added.Count > 0 ? "; fișiere apărute: " + string.Join(", ", added) : "") +
                     (removed.Count > 0 ? "; fișiere dispărute: " + string.Join(", ", removed) : "") +
                     (added.Count == 0 && removed.Count == 0 ? "; același nume, conținut schimbat: " + string.Join(", ", n.TrustFiles) : ""));
        }
        return diff;
    }

    private static string Short(string h) => h.Length > 16 ? h[..16] : h;
}
