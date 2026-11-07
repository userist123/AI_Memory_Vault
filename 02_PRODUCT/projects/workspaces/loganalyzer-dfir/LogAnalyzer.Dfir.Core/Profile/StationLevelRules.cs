using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Profile;

/// <summary>
/// What HG 585 asks of a station depending on the highest classification level it processes (owner decision 2026-10-23, docs/dfir/MATRICE_HG585_ORNISS_2026-10-23.md
/// section 4). The levels covered start at "secret de serviciu" (rank 1) and go up to "strict secret de importanță deosebită" (rank 4); NATO and EU markings are
/// compared through the equivalence table of <see cref="SecrecyLevels"/>. Only the public rules are here: the unit's own thresholds (password, scan frequency)
/// come from its CSS / PrOpSec and are never invented.
/// </summary>
/// <param name="AccessLogRetentionYears">Minimum retention of the access records (HG 585 art. 291(2)-(3)); null = set by the accreditation documentation (secret de serviciu).</param>
/// <param name="InternetForbidden">The station may not be connected to the internet (owner decision, matrix row 26: at SSID).</param>
public sealed record StationLevelRules(SecrecyLevel Level, int? AccessLogRetentionYears, string RetentionBasis, string MediaInventory, bool DestructionOnly,
    bool TempestApplies, bool InternetForbidden)
{
    public const string UndefinedText = "nedefinit în profilul de proceduri";

    /// <summary>The level of the procedure profile, or null when it is empty or not a known classification level (an unknown text is never guessed).</summary>
    public static SecrecyLevel? Resolve(string? profileLevel) => SecrecyLevels.TryParse(profileLevel, out var l) ? l : null;

    /// <summary>The label printed on the report: the level's name and abbreviation, or "nedefinit".</summary>
    public static string Describe(string? profileLevel) => Resolve(profileLevel) is { } l ? $"{l.Name} ({l.Abbreviation})" : UndefinedText;

    public static StationLevelRules For(SecrecyLevel level) => level.Rank switch
    {
        <= 1 => new(level, null, "secret de serviciu: retenția fixată de documentația de acreditare a sistemului (modelul ORNISS)",
                    "controale punctuale ale suporturilor", false, false, false),
        2 => new(level, 3, "HG 585 art. 291(2): cel puțin 3 ani pentru secret și strict secret", "controale punctuale ale suporturilor", false, true, false),
        3 => new(level, 3, "HG 585 art. 291(2): cel puțin 3 ani pentru secret și strict secret", "inventar periodic al suporturilor", false, true, false),
        _ => new(level, 10, "HG 585 art. 291(3): cel puțin 10 ani pentru strict secret de importanță deosebită", "inventar al suporturilor cel puțin anual", true, true, true),
    };
}
