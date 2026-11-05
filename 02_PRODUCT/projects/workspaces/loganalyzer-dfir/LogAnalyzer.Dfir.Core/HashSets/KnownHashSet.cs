using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.HashSets;

/// <summary>The two categories of a fingerprint set. A hash in both categories is reported under both (visible conflict).</summary>
public static class HashSetCategory
{
    public const string KnownGood = "cunoscut-bun";
    public const string KnownBad = "cunoscut-rău";
    public static bool IsValid(string? c) => c is KnownGood or KnownBad;
    public static string Normalize(string? c) => (c ?? "").Trim().ToLowerInvariant() switch
    {
        "cunoscut-bun" or "bun" or "good" => KnownGood,
        "cunoscut-rău" or "cunoscut-rau" or "rău" or "rau" or "bad" => KnownBad,
        _ => "",
    };
}

/// <summary>Metadata of an imported set (what the list shows and what a match cites as the set's source). Hashes are kept beside it, not in this record.</summary>
public sealed class KnownHashSetInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>Subset of SHA-256, SHA-1, MD5 present in the set.</summary>
    public List<string> Algorithms { get; set; } = [];
    public int EntryCount { get; set; }
    public string SourceFileName { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public DateTimeOffset ImportedUtc { get; set; }
    /// <summary>The signed-in account that imported the set; empty when nobody is signed in.</summary>
    public string ImportedBy { get; set; } = "";
    [JsonIgnore] public string Version => SourceSha256.Length >= 12 ? SourceSha256[..12] : SourceSha256;
}

/// <summary>A loaded set: metadata plus upper-case hex hashes (32 = MD5, 40 = SHA-1, 64 = SHA-256).</summary>
public sealed class KnownHashSet(KnownHashSetInfo info, HashSet<string> hashes)
{
    public KnownHashSetInfo Info { get; } = info;
    public HashSet<string> Hashes { get; } = hashes;
    public bool Contains(string hexHash) => hexHash.Length is 32 or 40 or 64 && Hashes.Contains(hexHash.ToUpperInvariant());

    public static string? AlgorithmOf(string hex) => hex.Length switch { 64 => "SHA-256", 40 => "SHA-1", 32 => "MD5", _ => null };
}
