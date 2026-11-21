using System.Text.Json;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Planner 14 E1: the one plain sentence "Ce faci" per rule, for the owner's first page and the "Greșeli găsite" block. The finding's own first
/// recommended step wins; then the exact rule id in Data/owner_actions.json; then the longest matching prefix (key ending in *); then the general sentence.
/// </summary>
public static class OwnerActions
{
    private sealed record Table(string Fallback, IReadOnlyDictionary<string, string> Exact, IReadOnlyList<(string Prefix, string Text)> Prefixes);

    private static readonly Lazy<Table> Shipped = new(() => Parse(ShippedData.ReadEmbedded("owner_actions.json")));

    private static Table Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("exact", out var e)) foreach (var p in e.EnumerateObject()) exact[p.Name] = p.Value.GetString() ?? "";
        var prefixes = new List<(string, string)>();
        if (root.TryGetProperty("prefix", out var pf))
            foreach (var p in pf.EnumerateObject()) prefixes.Add((p.Name.TrimEnd('*'), p.Value.GetString() ?? ""));
        prefixes.Sort((a, b) => b.Item1.Length.CompareTo(a.Item1.Length));   // the most specific prefix first
        return new Table(root.GetProperty("fallback").GetString() ?? "", exact, prefixes);
    }

    /// <summary>The general sentence used when no rule entry exists.</summary>
    public static string Fallback => Shipped.Value.Fallback;

    /// <summary>The table's sentence for a rule id, or null when only the general sentence applies.</summary>
    public static string? ForRule(string ruleId)
    {
        var t = Shipped.Value;
        if (t.Exact.TryGetValue(ruleId, out var s) && s.Length > 0) return s;
        foreach (var (prefix, text) in t.Prefixes)
            if (text.Length > 0 && ruleId.StartsWith(prefix, StringComparison.Ordinal)) return text;
        return null;
    }

    /// <summary>The sentence for a finding: its own first recommended step, else the table, else the general sentence.</summary>
    public static string For(Finding f)
    {
        var own = f.RecommendedNextSteps.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        return own is not null ? own.Trim() : ForRule(f.RuleId) ?? Fallback;
    }
}
