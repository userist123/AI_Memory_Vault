using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Parsing;

/// <summary>Outcome of choosing a parser for one evidence item.</summary>
public sealed record ParserSelection(IEvidenceParser? Parser, PreflightResult? Preflight, string Problem)
{
    public bool HasCandidate => Parser is not null || Preflight is not null;
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
    /// Picks the parser for an item. No candidate: Parser and Preflight are null. Candidates but the source fails preflight
    /// (missing, mutated, wrong format…): Parser is null and Preflight says why. Otherwise the single parser whose formats
    /// include the source's content format.
    /// </summary>
    public ParserSelection Select(EvidenceItem item, string fullPath)
    {
        var candidates = Candidates(item);
        if (candidates.Count == 0) return new(null, null, $"niciun parser înregistrat pentru tipul „{item.SourceType}”");
        var formats = candidates.SelectMany(p => p.Descriptor.Fingerprints).Distinct().ToList();
        var pre = EvidencePreflight.Check(item, fullPath, formats);
        if (!pre.CanParse) return new(null, pre, $"{pre.Code}: {pre.Detail}");
        var readers = candidates.Where(p => p.Descriptor.Fingerprints.Contains(pre.Fingerprint)).ToList();
        return readers.Count == 1
            ? new(readers[0], pre, "")
            : new(null, pre, $"mai multe parsere citesc formatul „{pre.Fingerprint}” pentru tipul „{item.SourceType}”: {string.Join(", ", readers.Select(p => p.Descriptor.ParserId))}");
    }
}
