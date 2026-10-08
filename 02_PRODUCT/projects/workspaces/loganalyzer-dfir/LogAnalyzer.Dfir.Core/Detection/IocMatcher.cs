using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Detection;

public enum DetectionKind { Ioc, Hash, Yara, Sigma, Behavior, Correlation }

/// <summary>A rule or rule set as loaded: identity, version and the SHA-256 of its text, so a result names exactly what matched.</summary>
public sealed record DetectionRule(string RuleId, string Version, DetectionKind Kind, string Title, string Sha256, string Source,
                                   string MitreTechniqueId = "", string Level = "");

/// <summary>Spec §10 DetectionResult: which rule (and version / hash) matched which evidence, and how.</summary>
public sealed record DetectionResult(
    string RuleId, string RuleVersion, string RuleSha256, DetectionKind Kind, string Title,
    string EvidenceId, string Locator, string EvidenceSha256, string Match,
    Classification Classification, Confidence Confidence, string Explanation, DateTimeOffset? TimeUtc = null);

public sealed record Ioc(string Type, string Value, string Context, Confidence Confidence, string Source);

/// <summary>
/// Indicators of compromise from a CSV list — either the investigation inventory format (Type,Value,Context,
/// Classification,Confidence,Source) or a plain (type,value,description). Matches hashes against evidence items and
/// timeline hash fields, IPs, domains (and subdomains), paths (prefix for "…\", wildcards * and ?), file names, tasks and
/// registry keys. A match proves the indicator is present in the evidence, not that the activity was malicious.
/// </summary>
public sealed class IocMatcher
{
    public IReadOnlyList<Ioc> Indicators { get; }
    public DetectionRule RuleSet { get; }

    private IocMatcher(IReadOnlyList<Ioc> iocs, DetectionRule ruleSet) => (Indicators, RuleSet) = (iocs, ruleSet);

    public static IocMatcher LoadCsv(string path)
    {
        var text = File.ReadAllText(path);
        var rows = CsvReader.ReadRows(new StringReader(text)).ToList();
        if (rows.Count == 0) throw new InvalidDataException("Lista de IOC este goală.");
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int iType = header.IndexOf("type"), iValue = header.IndexOf("value");
        if (iType < 0 || iValue < 0) throw new InvalidDataException("Lista de IOC trebuie să aibă coloanele Type și Value.");
        int iContext = Math.Max(header.IndexOf("context"), header.IndexOf("description"));
        int iConf = header.IndexOf("confidence"), iSource = header.IndexOf("source");
        var list = new List<Ioc>();
        foreach (var r in rows.Skip(1))
        {
            string Col(int i) => i >= 0 && i < r.Length ? r[i].Trim() : "";
            var type = Col(iType).ToLowerInvariant();
            if (type.Length == 0 || Col(iValue).Length == 0) continue;
            var conf = Col(iConf).ToUpperInvariant() switch { "HIGH" => Confidence.High, "LOW" => Confidence.Low, _ => Confidence.Medium };
            list.Add(new Ioc(type, Col(iValue), Col(iContext), conf, Col(iSource)));
        }
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new IocMatcher(list, new DetectionRule($"IOC:{Path.GetFileName(path)}", sha[..12], DetectionKind.Ioc, $"Listă IOC {Path.GetFileName(path)} ({list.Count} indicatori)", sha, path));
    }

    /// <summary>Hash indicators against acquired evidence files (their SHA-256 recorded at acquisition).</summary>
    public IEnumerable<DetectionResult> MatchEvidence(IEnumerable<EvidenceItem> evidence)
    {
        var sha256 = Indicators.Where(i => i.Type == "sha256").ToLookup(i => i.Value.ToUpperInvariant());
        foreach (var e in evidence)
            foreach (var ioc in sha256[e.Sha256.ToUpperInvariant()])
                yield return Result(ioc, e.EvidenceId, "file", e.Sha256, $"SHA-256 al fișierului {e.OriginalName} = {ioc.Value}", null, DetectionKind.Hash);
    }

    public IEnumerable<DetectionResult> MatchEvents(IEnumerable<TimelineEvent> events)
    {
        var hashes = Indicators.Where(i => i.Type is "sha256" or "sha1" or "md5").ToLookup(i => i.Value.ToUpperInvariant());
        var ips = Indicators.Where(i => i.Type == "ip").ToLookup(i => i.Value);
        var domains = Indicators.Where(i => i.Type == "domain").ToList();
        var paths = Indicators.Where(i => i.Type == "path").Select(i => (Ioc: i, Pattern: PathPattern(i.Value))).ToList();
        var names = Indicators.Where(i => i.Type == "filename").ToLookup(i => i.Value.ToUpperInvariant());
        var tasks = Indicators.Where(i => i.Type == "task").ToLookup(i => i.Value.ToUpperInvariant());
        var regs = Indicators.Where(i => i.Type == "registry").ToList();

        foreach (var e in events)
        {
            var seen = new HashSet<Ioc>();
            IEnumerable<DetectionResult> Hit(Ioc ioc, string what)
            {
                if (seen.Add(ioc)) yield return Result(ioc, e.EvidenceId, e.Locator, e.SourceSha256, what, e.Time.Utc, DetectionKind.Ioc);
            }
            foreach (var h in new[] { e.Hash, e.Fields.GetValueOrDefault("SHA1", ""), e.Fields.GetValueOrDefault("SHA256", ""), e.Fields.GetValueOrDefault("Sha256", "") }.Where(h => h.Length >= 32))
                foreach (var ioc in hashes[h.ToUpperInvariant()]) foreach (var r in Hit(ioc, $"hash {h}")) yield return r;
            foreach (var ip in new[] { e.RemoteIp, e.Fields.GetValueOrDefault("ServerIP", ""), e.Fields.GetValueOrDefault("IpAddress", "") }.Where(x => x.Length > 0))
                foreach (var ioc in ips[ip]) foreach (var r in Hit(ioc, $"IP {ip}")) yield return r;
            if (e.Dns.Length > 0)
                foreach (var ioc in domains.Where(d => e.Dns.Equals(d.Value, StringComparison.OrdinalIgnoreCase) || e.Dns.EndsWith("." + d.Value, StringComparison.OrdinalIgnoreCase)))
                    foreach (var r in Hit(ioc, $"domeniu {e.Dns}")) yield return r;
            var candidatePaths = PathsOf(e).ToList();
            foreach (var (ioc, pattern) in paths)
                if (candidatePaths.FirstOrDefault(p => pattern.IsMatch(Correlation.Normalize(p))) is { } p)
                    foreach (var r in Hit(ioc, $"cale {p}")) yield return r;
            foreach (var p in candidatePaths)
                foreach (var ioc in names[Path.GetFileName(p).ToUpperInvariant()]) foreach (var r in Hit(ioc, $"fișier {p}")) yield return r;
            foreach (var t in new[] { e.Task, e.Fields.GetValueOrDefault("TaskName", "") }.Where(t => t.Length > 0))
                foreach (var ioc in tasks[t.ToUpperInvariant()]) foreach (var r in Hit(ioc, $"task {t}")) yield return r;
            foreach (var ioc in regs.Where(g => KeyOf(g.Value) is { Length: > 0 } k && e.Locator.Contains(k, StringComparison.OrdinalIgnoreCase)))
                foreach (var r in Hit(ioc, $"cheie {e.Locator}")) yield return r;
        }
    }

    private static IEnumerable<string> PathsOf(TimelineEvent e)
    {
        if (e.Path.Length > 0) yield return e.Path;
        foreach (var k in new[] { "ImagePath", "ServiceDll", "Command", "TargetFilename", "Image" })
            if (e.Fields.TryGetValue(k, out var v) && v.Length > 2) yield return v.Trim('"');
        if (e.Fields.TryGetValue("Path", out var dp) && dp.Length > 0) yield return Correlation.DefenderContainer(dp);
        if (e.Fields.TryGetValue("ReferencedFiles", out var refs))
            foreach (var r in refs.Split('|', StringSplitOptions.RemoveEmptyEntries)) yield return r;
    }

    /// <summary>"C:\x\" = folder prefix; * and ? wildcards; compared on the normalised path (no volume / drive letter).</summary>
    private static Regex PathPattern(string value)
    {
        var norm = Correlation.Normalize(value);
        var body = Regex.Escape(norm).Replace(@"\*", "[^\\\\]*").Replace(@"\?", "[^\\\\]");
        return new Regex("^" + body + (norm.EndsWith('\\') ? "" : "$"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>"HKLM\SOFTWARE\Microsoft\…" → "Microsoft\…" (the part found in a SOFTWARE-hive locator).</summary>
    private static string KeyOf(string value)
    {
        var v = value.Replace('/', '\\');
        foreach (var prefix in new[] { @"HKLM\SOFTWARE\", @"HKEY_LOCAL_MACHINE\SOFTWARE\", @"HKLM\SYSTEM\", @"HKCU\", @"HKEY_CURRENT_USER\" })
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return v[prefix.Length..];
        return v;
    }

    private DetectionResult Result(Ioc ioc, string evidenceId, string locator, string evidenceSha, string what, DateTimeOffset? time, DetectionKind kind) =>
        new($"{RuleSet.RuleId}:{ioc.Type}:{ioc.Value}", RuleSet.Version, RuleSet.Sha256, kind, $"IOC {ioc.Type}: {ioc.Context}",
            evidenceId, locator, evidenceSha, what, Classification.Direct, ioc.Confidence,
            $"Indicatorul „{ioc.Value}” ({ioc.Type}) apare în probă. Context din listă: {ioc.Context}. Sursa indicatorului: {ioc.Source}. " +
            "Prezența indicatorului nu dovedește singură o activitate malițioasă.", time);

    public static bool IsIp(string s) => IPAddress.TryParse(s, out _);
}
