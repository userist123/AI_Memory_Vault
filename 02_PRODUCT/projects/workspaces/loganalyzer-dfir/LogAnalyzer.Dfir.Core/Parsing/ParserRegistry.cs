using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Parsing;

/// <summary>Outcome of choosing a parser for one evidence item.</summary>
public sealed record ParserSelection(IReadOnlyList<IEvidenceParser> Parsers, PreflightResult? Preflight, string Problem)
{
    public bool HasCandidate => Parsers.Count > 0 || Preflight is not null;
}

/// <summary>
/// The parsers known to the application, each with its descriptor. Selection is by declared type, then confirmed by
/// content: the source is preflighted against the candidates' formats and only a parser that reads that format runs.
/// </summary>
public sealed class ParserRegistry
{
    private readonly IReadOnlyList<IEvidenceParser> _parsers;

    public ParserRegistry(IEnumerable<IEvidenceParser> parsers)
    {
        _parsers = parsers.ToList();
        var dup = _parsers.GroupBy(p => p.Descriptor.ParserId).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new ArgumentException($"Parser înregistrat de două ori: {dup.Key}", nameof(parsers));
    }

    public IReadOnlyList<ParserDescriptor> Descriptors => _parsers.Select(p => p.Descriptor).ToList();

    public IReadOnlyList<IEvidenceParser> Candidates(EvidenceItem item) => _parsers.Where(p => p.Descriptor.Accepts(item)).ToList();

    /// <summary>
    /// Picks the parsers for an item. No candidate: no parsers, no preflight. Candidates but the source fails preflight
    /// (missing, mutated, wrong format…): no parsers and Preflight says why. Otherwise every candidate whose formats include
    /// the source's content format — one source can feed several parsers (a SYSTEM hive: execution artifacts and services),
    /// each producing its own result.
    /// </summary>
    public ParserSelection Select(EvidenceItem item, string fullPath)
    {
        var candidates = Candidates(item);
        if (candidates.Count == 0) return new([], null, $"niciun parser înregistrat pentru tipul „{item.SourceType}”");
        var formats = candidates.SelectMany(p => p.Descriptor.Fingerprints).Distinct().ToList();
        var pre = EvidencePreflight.Check(item, fullPath, formats);
        if (!pre.CanParse) return new([], pre, $"{pre.Code}: {pre.Detail}");
        return new(candidates.Where(p => p.Descriptor.Fingerprints.Contains(pre.Fingerprint)).ToList(), pre, "");
    }
}
