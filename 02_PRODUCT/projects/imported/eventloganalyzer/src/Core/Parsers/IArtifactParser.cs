namespace EventLogAnalyzer.Core.Parsers;

/// <summary>
/// Common contract for the three artifact parsers. Kept generic over TOut
/// so EvtxParser, NtUserHiveParser and HklmHiveParser can all implement it
/// while returning their own domain type.
/// </summary>
public interface IArtifactParser<out TOut>
{
    /// <summary>True if this parser can handle the given file path (by extension/naming convention).</summary>
    bool CanParse(string filePath);

    /// <summary>Parse a single artifact file. Never mutates or writes to filePath.</summary>
    IReadOnlyList<TOut> Parse(string filePath);
}
