using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Analysis;

public sealed record Ioc(string Type, string Value, string Context, bool SuppressedAsNoise, string SuppressionReason);

/// <summary>
/// Extracts IOCs from evidence-derived text (spec §38, §50). Noise is classified, never deleted:
/// suppressed items are returned with <see cref="Ioc.SuppressedAsNoise"/> = true and a reason.
/// </summary>
public static partial class IocExtractor
{
    [GeneratedRegex(@"\b[A-Fa-f0-9]{64}\b")] private static partial Regex Sha256Rx();
    [GeneratedRegex(@"\b[A-Fa-f0-9]{40}\b")] private static partial Regex Sha1Rx();
    [GeneratedRegex(@"(?<![\w.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\w.])")] private static partial Regex Ipv4Rx();
    [GeneratedRegex(@"\bhttps?://[^\s""'<>]+", RegexOptions.IgnoreCase)] private static partial Regex UrlRx();
    [GeneratedRegex(@"\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:com|net|org|io|ro|ru|cn|xyz|top|cyou|site|online|info|biz|cc|me|app|dev|link|shop|club|live|pro|icu|bond|sbs|cfd|lat|click)\b", RegexOptions.IgnoreCase)] private static partial Regex DomainRx();

    private static readonly HashSet<string> NoiseIps = ["0.0.0.0", "255.255.255.255", "255.255.255.0", "255.255.0.0", "255.0.0.0", "127.0.0.1"];

    public static IEnumerable<Ioc> Extract(string text, string context)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (Match m in Sha256Rx().Matches(text)) yield return new Ioc("sha256", m.Value.ToUpperInvariant(), context, false, "");
        foreach (Match m in Sha1Rx().Matches(text))
            if (!IsInsideLongerHex(text, m)) yield return new Ioc("sha1", m.Value.ToUpperInvariant(), context, false, "");
        foreach (Match m in UrlRx().Matches(text)) yield return new Ioc("url", m.Value.TrimEnd('.', ',', ')', ';'), context, false, "");
        foreach (Match m in Ipv4Rx().Matches(text))
        {
            var v = m.Value;
            if (LooksLikeVersion(text, m)) { yield return new Ioc("ipv4", v, context, true, "version-number context"); continue; }
            if (NoiseIps.Contains(v)) { yield return new Ioc("ipv4", v, context, true, "netmask/unspecified/loopback"); continue; }
            var scope = IpClassifier.Classify(v);
            yield return scope == IpScope.External
                ? new Ioc("ipv4", v, context, false, "")
                : new Ioc("ipv4", v, context, true, scope.ToString());
        }
        foreach (Match m in DomainRx().Matches(text)) yield return new Ioc("domain", m.Value.ToLowerInvariant(), context, false, "");
    }

    private static bool IsInsideLongerHex(string text, Match m)
    {
        bool before = m.Index > 0 && Uri.IsHexDigit(text[m.Index - 1]);
        bool after = m.Index + m.Length < text.Length && Uri.IsHexDigit(text[m.Index + m.Length]);
        return before || after;
    }

    private static bool LooksLikeVersion(string text, Match m)
    {
        int start = Math.Max(0, m.Index - 12);
        var prefix = text[start..m.Index].ToLowerInvariant();
        return prefix.Contains("version") || prefix.EndsWith('v') || prefix.Contains("ver ") || prefix.Contains("build");
    }
}
