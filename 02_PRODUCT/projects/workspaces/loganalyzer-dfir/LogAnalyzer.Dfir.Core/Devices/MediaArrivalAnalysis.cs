using System.Globalization;
using LogAnalyzer.Dfir.Analysis;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>A file whose Modified time is earlier than its Created time on the medium (typical of a copy: the copy keeps the modification time, creation is the copy time).</summary>
public sealed record MediaCopiedFile(string Path, DateTimeOffset CreatedUtc, DateTimeOffset ModifiedUtc)
{
    /// <summary>Plain-language observation; not a suspicion.</summary>
    public string Text => $"„{Path}” pare copiat pe mediu la {CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC (conținutul modificat la {ModifiedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC; aceeași formă apare și la dezarhivare sau la unelte care păstrează data modificării)";
}

/// <summary>Files that arrived on the medium in one UTC day, by creation time.</summary>
public sealed record MediaArrivalDay(DateOnly Day, int Files, DateTimeOffset FirstUtc, DateTimeOffset LastUtc);

/// <summary>What the file times on a removable medium say about when files arrived on it (research 2026-10-12, row E6).</summary>
public sealed class MediaArrivalReport
{
    public string Letter { get; init; } = "";
    public int Files { get; init; }
    public bool Truncated { get; init; }
    public int AccessErrors { get; init; }
    public int LinksNotFollowed { get; init; }
    /// <summary>File system of the medium (DriveInfo.DriveFormat); empty when unknown. FAT/exFAT store local time, so a time-zone caveat is added.</summary>
    public string FileSystem { get; init; } = "";
    public bool StoresLocalTime => FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase) || FileSystem.Equals("exFAT", StringComparison.OrdinalIgnoreCase);
    public List<MediaCopiedFile> Copied { get; init; } = [];
    /// <summary>Arrival timeline, oldest day first.</summary>
    public List<MediaArrivalDay> Timeline { get; init; } = [];

    /// <summary>Two or three plain sentences for the medium card; never labelled suspicious.</summary>
    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (Files == 0) parts.Add("Listare suport: niciun fișier găsit (în limita adâncimii citite)");
            else
            {
                var span = Timeline.Count == 0 ? "" : $", create între {Timeline[0].FirstUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} și {Timeline[^1].LastUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} UTC, în {Timeline.Count} zile distincte";
                parts.Add($"Listare suport: {Files} fișiere{span}");
            }
            if (Copied.Count > 0)
                parts.Add($"{Copied.Count} fișiere par copiate pe mediu (data modificării este mai veche decât data creării; de exemplu {Copied[0].Text}); asta este obișnuit la copiere și nu este un semn de rău");
            if (Files > 0 && StoresLocalTime)
                parts.Add("sistemul de fișiere (" + FileSystem + ") păstrează ora locală a calculatorului care a scris fișierul, nu ora UTC; dacă acel calculator avea alt fus orar, orele pot fi decalate cu ore întregi");
            if (Files > 0) parts.Add("ora ultimului acces poate reflecta chiar inspecția făcută de aplicație");
            if (Truncated) parts.Add("listarea este trunchiată (limită de intrări sau adâncime)");
            if (AccessErrors > 0) parts.Add($"{AccessErrors} elemente nu au putut fi citite");
            if (LinksNotFollowed > 0) parts.Add($"{LinksNotFollowed} legături nu au fost urmărite");
            return string.Join("; ", parts) + ".";
        }
    }

    /// <summary>Note for NTFS media: the change journal can show what the listing cannot (deleted and renamed files).</summary>
    public static string UsnNote(string fileSystem, string letter) =>
        fileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase)
            ? $"Suportul {letter}: este NTFS; jurnalul USN se poate exporta (administrator) cu „fsutil usn readjournal {letter}: csv” și se încarcă în analiză ca sursă USN."
            : "";
}

/// <summary>Pure analysis of a medium listing (no I/O): which files look copied onto the medium and when files arrived.</summary>
public static class MediaArrivalAnalysis
{
    /// <summary>FAT stores times in 2-second steps; a smaller gap between Modified and Created is not a copy sign.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    public static MediaArrivalReport Analyze(MediaListingFact listing, string fileSystem = "")
    {
        var files = listing.Entries.Where(e => !e.IsDirectory).ToList();
        var copied = files.Where(e => e.CreatedUtc is { } c && e.LastWriteUtc is { } m && m + Tolerance < c)
            .Select(e => new MediaCopiedFile(e.RelativePath, e.CreatedUtc!.Value, e.LastWriteUtc!.Value))
            .OrderBy(x => x.CreatedUtc).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var days = files.Where(e => e.CreatedUtc is not null).GroupBy(e => DateOnly.FromDateTime(e.CreatedUtc!.Value.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new MediaArrivalDay(g.Key, g.Count(), g.Min(e => e.CreatedUtc!.Value), g.Max(e => e.CreatedUtc!.Value))).ToList();
        return new MediaArrivalReport
        {
            FileSystem = fileSystem ?? "", Letter = listing.Letter, Files = files.Count, Truncated = listing.Truncated, AccessErrors = listing.AccessErrors,
            LinksNotFollowed = listing.LinksNotFollowed, Copied = copied, Timeline = days,
        };
    }
}
