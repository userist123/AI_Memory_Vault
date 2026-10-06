using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Parsing;

public interface IEvidenceParser
{
    string Name { get; }
    string Version { get; }
    bool CanParse(EvidenceItem item);
    ParseResult Parse(EvidenceItem item, string fullPath, IEventSink sink, CancellationToken ct);
}

/// <summary>
/// Enforces the error semantics of spec §75 / bug class C: an exception is FAILED (never EMPTY),
/// cancellation is PARTIAL, zero records with no error is EMPTY.
/// </summary>
public abstract class EvidenceParserBase : IEvidenceParser
{
    public abstract string Name { get; }
    public abstract string Version { get; }
    public abstract bool CanParse(EvidenceItem item);

    public ParseResult Parse(EvidenceItem item, string fullPath, IEventSink sink, CancellationToken ct)
    {
        var r = new ParseResult { EvidenceId = item.EvidenceId, Parser = Name, ParserVersion = Version };
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
