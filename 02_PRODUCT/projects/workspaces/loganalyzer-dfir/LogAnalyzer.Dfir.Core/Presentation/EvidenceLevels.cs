using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>One line of the evidence list (level 2): where it comes from, when, and what it shows.</summary>
public sealed record EvidenceLine(string EvidenceId, string Source, string TimeText, string Description);

/// <summary>Level 3: everything needed to find the record again and to know how it was read (UX contract §6).</summary>
public sealed record EvidenceTechnical(string EvidenceId, string Sha256, string SourcePath, string Parser, string ParserVersion, string Timestamp, string Locator);

/// <summary>
/// The evidence of a finding in the three levels of the UX contract §6 (U6): a summary line („4 probe din 2 surse”), the list (source, time, short
/// description) and the technical rows (EvidenceId, SHA-256, source path, parser + version, timestamp, locator). Built from the finding's
/// <see cref="EvidenceRef"/>s and the case data; nothing is copied into a new data model.
/// </summary>
public sealed record EvidenceLevels(string Summary, IReadOnlyList<EvidenceLine> List, IReadOnlyList<EvidenceTechnical> Technical)
{
    /// <summary>Shown where the case does not give a detail. Never blank, never a guess.</summary>
    public const string Unknown = "necunoscut în caz";

    public static EvidenceLevels Build(Finding f, EvidenceContext ctx)
    {
        var list = new List<EvidenceLine>();
        var tech = new List<EvidenceTechnical>();
        foreach (var r in f.SupportingEvidence)
        {
            var item = ctx.ItemOf(r.EvidenceId);
            var ev = ctx.EventOf(r);
            var parse = ctx.ParseOf(r.EvidenceId);
            string source = item is { Source.Length: > 0 } ? item.Source : r.EvidenceId;
            string time = ev?.Time.Utc is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " UTC" : Unknown;
            list.Add(new EvidenceLine(r.EvidenceId, source, time, r.Description));

            string sha = First(r.Sha256, item?.Sha256, ev?.SourceSha256);
            string path = First(item?.OriginalPath, item?.StoredPath);
            string parser = First(item?.Parser, ev?.ParserId, parse?.Parser);
            string version = First(item?.ParserVersion, ev?.ParserVersion, parse?.ParserVersion);
            string stamp = ev?.Time.Utc is not null ? ev.Time.UtcIso : Unknown;
            tech.Add(new EvidenceTechnical(r.EvidenceId, sha, path, parser, version, stamp, First(r.Locator)));
        }
        int sources = f.SupportingEvidence.Select(r => r.EvidenceId).Distinct(StringComparer.Ordinal).Count();
        return new EvidenceLevels(SummaryLine(f.SupportingEvidence.Count, sources), list, tech);
    }

    private static string First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? Unknown;

    /// <summary>„4 probe din 2 surse”, „1 probă dintr-o sursă”, „Nicio probă atașată”; the Romanian „de” is used from 20 up (20 de probe, 119 probe, 120 de probe).</summary>
    public static string SummaryLine(int pieces, int sources)
    {
        if (pieces <= 0) return "Nicio probă atașată";
        string p = pieces == 1 ? "1 probă" : $"{pieces}{De(pieces)} probe";
        string s = sources == 1 ? "dintr-o sursă" : $"din {sources}{De(sources)} surse";
        return $"{p} {s}";
    }

    private static string De(int n) { int m = n % 100; return m == 0 || m >= 20 ? " de" : ""; }
}
