using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>The recorded form "officer_state=store:&lt;sha|none&gt;,trust:&lt;sha|none&gt;,files:&lt;a|b&gt;" and its parts.</summary>
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

    /// <summary>The last state recorded in the sealed audit (the "officer_state=…" tail of the newest event that carries one); null when none.</summary>
    public static string? LastRecorded(string auditPath)
    {
        string? last = null;
        foreach (var d in LogAnalyzer.Dfir.Case.AuditAnchor.AllDetails(auditPath))
        {
            int i = d.IndexOf(Prefix, StringComparison.Ordinal);
            if (i >= 0) last = d[i..];
        }
        return last;
    }

    /// <summary>The differences between the recorded and the current state, in words; empty when they are the same.</summary>
    public static List<string> Differences(string? recorded, string? now)
    {
        var diff = new List<string>();
        var n = Parse(now);
        if (n is null) return diff;
        var r = Parse(recorded);
        if (r is null)
        {
            if (!n.IsEmpty) diff.Add("lista ofițerilor sau autoritățile de încredere există, dar aplicația nu a înregistrat niciodată starea lor: au fost puse în afara aplicației");
            return diff;
        }
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
