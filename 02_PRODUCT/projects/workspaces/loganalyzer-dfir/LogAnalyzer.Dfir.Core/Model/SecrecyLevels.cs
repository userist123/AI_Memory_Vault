using System.Globalization;
using System.Text;

namespace LogAnalyzer.Dfir.Model;

public enum SecrecyScheme { Nato, Eu, National }

/// <summary>One classification level. <see cref="Rank"/> (1 = lowest .. 4 = highest) exists only to compare levels of different schemes.</summary>
public sealed record SecrecyLevel(string Id, SecrecyScheme Scheme, string Name, string Abbreviation, int Rank, IReadOnlyList<string> Aliases);

/// <summary>
/// Classification levels (owner decision 21): classified levels only, NO "unclassified" level (an item without a marking is "nemarcat", which is
/// not a level). NATO: COSMIC TOP SECRET, NATO SECRET, NATO CONFIDENTIAL, NATO RESTRICTED. EU: TRES SECRET UE/EU TOP SECRET, SECRET UE/EU SECRET,
/// CONFIDENTIEL UE/EU CONFIDENTIAL, RESTREINT UE/EU RESTRICTED. National: strict secret de importanță deosebită, strict secret, secret, secret de serviciu.
/// This is configuration data, not detection: a level comes only from a real marking entered by the operator, never from a file name.
/// </summary>
/// <remarks>
/// The equivalence (rank) table is used ONLY to compare levels across schemes (for example "is the user's clearance at least the medium's level").
/// It never converts a marking into another. Source of the national levels and of their equivalence with NATO / EU: Legea 182/2002 art. 15 and
/// HG 585/2002 (Standardele naționale de protecție a informațiilor clasificate în România, the table of equivalence with NATO and EU markings).
/// The owner confirmed the national levels and the HG 585 equivalences in decision 21; the table must be checked against the text in force when the
/// law changes.
/// </remarks>
public static class SecrecyLevels
{
    public const string Unmarked = "nemarcat";
    public const string EquivalenceSource = "Legea 182/2002 art. 15; HG 585/2002 (echivalarea nivelurilor naționale cu NATO și UE); decizia 21 a proprietarului";

    private static SecrecyLevel L(string id, SecrecyScheme s, string name, string abbr, int rank, params string[] aliases) => new(id, s, name, abbr, rank, aliases);

    public static IReadOnlyList<SecrecyLevel> All { get; } =
    [
        L("NATO_COSMIC_TOP_SECRET", SecrecyScheme.Nato, "COSMIC TOP SECRET", "CTS", 4),
        L("NATO_SECRET", SecrecyScheme.Nato, "NATO SECRET", "NS", 3),
        L("NATO_CONFIDENTIAL", SecrecyScheme.Nato, "NATO CONFIDENTIAL", "NC", 2),
        L("NATO_RESTRICTED", SecrecyScheme.Nato, "NATO RESTRICTED", "NR", 1),
        L("EU_TOP_SECRET", SecrecyScheme.Eu, "TRÈS SECRET UE/EU TOP SECRET", "TS-UE", 4, "TRES SECRET UE", "EU TOP SECRET", "TRES SECRET UE EU TOP SECRET"),
        L("EU_SECRET", SecrecyScheme.Eu, "SECRET UE/EU SECRET", "S-UE", 3, "SECRET UE", "EU SECRET", "SECRET UE EU SECRET"),
        L("EU_CONFIDENTIAL", SecrecyScheme.Eu, "CONFIDENTIEL UE/EU CONFIDENTIAL", "C-UE", 2, "CONFIDENTIEL UE", "EU CONFIDENTIAL", "CONFIDENTIEL UE EU CONFIDENTIAL"),
        L("EU_RESTRICTED", SecrecyScheme.Eu, "RESTREINT UE/EU RESTRICTED", "R-UE", 1, "RESTREINT UE", "EU RESTRICTED", "RESTREINT UE EU RESTRICTED"),
        L("RO_STRICT_SECRET_DE_IMPORTANTA_DEOSEBITA", SecrecyScheme.National, "STRICT SECRET DE IMPORTANȚĂ DEOSEBITĂ", "SSID", 4),
        L("RO_STRICT_SECRET", SecrecyScheme.National, "STRICT SECRET", "SS", 3),
        L("RO_SECRET", SecrecyScheme.National, "SECRET", "S", 2),
        L("RO_SECRET_DE_SERVICIU", SecrecyScheme.National, "SECRET DE SERVICIU", "SDS", 1),
    ];

    private static readonly string[] UnclassifiedMarkings = ["UNCLASSIFIED", "NATO UNCLASSIFIED", "NECLASIFICAT", "NESECRET", "NON CLASSIFIE", "NON-CLASSIFIE"];

    private static readonly Dictionary<string, SecrecyLevel> ByKey = BuildIndex();

    private static Dictionary<string, SecrecyLevel> BuildIndex()
    {
        var d = new Dictionary<string, SecrecyLevel>(StringComparer.Ordinal);
        foreach (var l in All)
            foreach (var k in new[] { l.Id, l.Name, l.Abbreviation }.Concat(l.Aliases))
                d[Normalize(k)] = l;
        return d;
    }

    /// <summary>Upper case, no diacritics, punctuation and underscores as single spaces.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in (s ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>True for a marking that names one of the levels above. Unclassified markings, empty text and anything else are not levels.</summary>
    public static bool TryParse(string? text, out SecrecyLevel? level)
    {
        level = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ByKey.TryGetValue(Normalize(text), out level);
    }

    public static SecrecyLevel Parse(string text) => TryParse(text, out var l) ? l! : throw new FormatException($"„{text}” nu este un nivel de clasificare cunoscut.");

    /// <summary>"UNCLASSIFIED" / "NATO UNCLASSIFIED" / "NECLASIFICAT": markings the register refuses (decision 21 has no such level).</summary>
    public static bool IsUnclassifiedMarking(string? text) => !string.IsNullOrWhiteSpace(text) && UnclassifiedMarkings.Contains(Normalize(text), StringComparer.Ordinal);

    /// <summary>No marking at all (empty, or the word "nemarcat"). This is not a classification level.</summary>
    public static bool IsUnmarked(string? text) => string.IsNullOrWhiteSpace(text) || Normalize(text) == Normalize(Unmarked);

    /// <summary>The display name of a marking, or "nemarcat" when there is none.</summary>
    public static string Describe(string? text) => TryParse(text, out var l) ? l!.Name : IsUnmarked(text) ? Unmarked : text!.Trim();

    /// <summary>Every level of any scheme with the same rank as <paramref name="level"/> (including itself).</summary>
    public static IReadOnlyList<SecrecyLevel> Equivalents(SecrecyLevel level) => All.Where(l => l.Rank == level.Rank).ToList();

    /// <summary>True when <paramref name="held"/> is at least as high as <paramref name="required"/>, compared through the equivalence table.</summary>
    public static bool AtLeast(SecrecyLevel held, SecrecyLevel required) => held.Rank >= required.Rank;
}
