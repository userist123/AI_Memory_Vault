using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Parsing;

/// <summary>
/// How far a parser has been validated. Set from the tests that exist, never from what the parser claims:
/// Validated = regression test on a real corpus; Tested = exact-value tests on synthetic input only; Experimental = neither.
/// </summary>
public enum ParserMaturity { Validated, Tested, Experimental }

/// <summary>Identity and limits of a parser (master spec §5, prompt P2). Stored with every case in Analysis/parsers.json.</summary>
public sealed record ParserDescriptor
{
    public required string ParserId { get; init; }
    public required string Version { get; init; }
    /// <summary>Artifact read, in plain words.</summary>
    public required string Artifact { get; init; }
    /// <summary>EvidenceItem.SourceType values this parser reads; an entry ending in '*' is a prefix.</summary>
    public required IReadOnlyList<string> SourceTypes { get; init; }
    /// <summary>File-name rules for items without a known type: ".ext" suffix, an exact file name, or a pattern with '*'.</summary>
    public IReadOnlyList<string> FileNames { get; init; } = [];
    /// <summary>Content formats (<see cref="EvidenceFingerprint"/>) this parser can read.</summary>
    public required IReadOnlyList<string> Fingerprints { get; init; }
    public required string SupportedOs { get; init; }
    public required IReadOnlyList<string> FormatVersions { get; init; }
    public required IReadOnlyList<string> Limitations { get; init; }
    public required ParserMaturity Status { get; init; }
    /// <summary>The tests behind <see cref="Status"/>.</summary>
    public required string Validation { get; init; }

    public string StatusName => Status.ToString().ToUpperInvariant();

    public bool Accepts(EvidenceItem item)
    {
        foreach (var t in SourceTypes)
            if (t.EndsWith('*') ? item.SourceType.StartsWith(t[..^1], StringComparison.Ordinal) : item.SourceType == t)
                return true;
        var name = Path.GetFileName(item.StoredPath);
        return FileNames.Any(n => n.Contains('*') ? Glob(n, name)
                                : n.StartsWith('.') ? name.EndsWith(n, StringComparison.OrdinalIgnoreCase)
                                : name.Equals(n, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Glob(string pattern, string name)
    {
        var parts = pattern.Split('*');
        if (!name.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) || !name.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase)) return false;
        int pos = parts[0].Length;
        for (int i = 1; i < parts.Length - 1; i++)
        {
            int at = name.IndexOf(parts[i], pos, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;
            pos = at + parts[i].Length;
        }
        return pos <= name.Length - parts[^1].Length;
    }
}

public interface IEvidenceParser
{
    ParserDescriptor Descriptor { get; }
    /// <summary>Source check before parsing: integrity against acquisition and content format against <see cref="ParserDescriptor.Fingerprints"/>.</summary>
    PreflightResult Preflight(EvidenceItem item, string fullPath);
    ParseResult Parse(EvidenceItem item, string fullPath, IEventSink sink, CancellationToken ct);
}

/// <summary>
/// Enforces the error semantics of spec §75 / bug class C: an exception is FAILED (never EMPTY),
/// cancellation is PARTIAL, zero records with no error is EMPTY.
/// </summary>
public abstract class EvidenceParserBase : IEvidenceParser
{
    public abstract ParserDescriptor Descriptor { get; }
    public string Name => Descriptor.ParserId;
    public string Version => Descriptor.Version;
    public bool CanParse(EvidenceItem item) => Descriptor.Accepts(item);

    public PreflightResult Preflight(EvidenceItem item, string fullPath) => EvidencePreflight.Check(item, fullPath, Descriptor.Fingerprints);

    public ParseResult Parse(EvidenceItem item, string fullPath, IEventSink sink, CancellationToken ct)
    {
        var r = new ParseResult { EvidenceId = item.EvidenceId, Parser = Name, ParserVersion = Version, ParserStatus = Descriptor.StatusName };
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            r.Status = EvidenceStatus.NotAvailable;
            r.Error = $"Evidence file not found: {fullPath}";
            return r;
        }
        try
        {
            ParseCore(item, fullPath, sink, r, ct);
            return r.Finish();
        }
        catch (OperationCanceledException)
        {
            r.Status = EvidenceStatus.Partial;
            r.Error = "Cancelled by user; records up to the cancellation point were kept.";
            return r;
        }
        catch (Exception ex)
        {
            r.Status = r.Records > 0 ? EvidenceStatus.Partial : EvidenceStatus.Failed;
            r.Error = $"{ex.GetType().Name}: {ex.Message}";
            return r;
        }
    }

    protected abstract void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct);
}
