using System.Diagnostics.Eventing.Reader;
using EventLogAnalyzer.Core.Models;

namespace EventLogAnalyzer.Core.Parsers;

/// <summary>
/// Parses a .evtx file offline using EventLogReader against PathType.FilePath -
/// this reads the exported file directly, it does not touch the live event log
/// service or require the file to be on the local "C:\Windows\System32\winevt" store.
/// </summary>
public sealed class EvtxParser : IArtifactParser<EventRecordModel>
{
    public bool CanParse(string filePath) =>
        filePath.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<EventRecordModel> Parse(string filePath)
    {
        var results = new List<EventRecordModel>();

        var query = new EventLogQuery(filePath, PathType.FilePath)
        {
            ReverseDirection = false
        };

        using var reader = new EventLogReader(query);

        for (var native = reader.ReadEvent(); native != null; native = reader.ReadEvent())
        {
            using (native)
            {
                results.Add(Map(native, filePath));
            }
        }

        return results;
    }

    /// <summary>
    /// Internal so it's unit-testable directly against a hand-built EventRecord-shaped
    /// object at the boundary, without needing a real .evtx file on a non-Windows CI box.
    /// </summary>
    internal static EventRecordModel Map(EventRecord native, string sourceFile)
    {
        string message;
        try
        {
            // FormatDescription can throw if the provider's message-table DLL
            // isn't registered on this machine (common when analyzing an .evtx
            // exported from a *different* host). Fall back to raw XML in that case.
            message = native.FormatDescription() ?? native.ToXml();
        }
        catch (EventLogException)
        {
            message = native.ToXml();
        }

        return new EventRecordModel
        {
            EventId = native.Id,
            Provider = native.ProviderName ?? "Unknown",
            Level = MapLevel(native.Level),
            TimeCreated = native.TimeCreated is { } t
                ? new DateTimeOffset(t)
                : DateTimeOffset.MinValue,
            UserSid = native.UserId?.Value,
            Computer = native.MachineName ?? "Unknown",
            Message = message,
            Keywords = native.KeywordsDisplayNames?.ToList() ?? new List<string>(),
            SourceFile = sourceFile
        };
    }

    /// <summary>Internal (not private) so it can be unit tested in isolation without
    /// needing to construct a real, sealed EventRecord from the OS reader.</summary>
    internal static Severity MapLevel(byte? level) => level switch
    {
        1 => Severity.Critical,
        2 => Severity.Error,
        3 => Severity.Warning,
        4 => Severity.Info,
        5 => Severity.Info, // Verbose
        _ => Severity.Info
    };
}
