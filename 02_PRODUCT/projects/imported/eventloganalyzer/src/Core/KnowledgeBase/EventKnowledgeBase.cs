using System.Text.Json;
using System.Text.Json.Serialization;
using EventLogAnalyzer.Core.Models;

namespace EventLogAnalyzer.Core.KnowledgeBase;

public sealed record EventSignature
{
    [JsonPropertyName("eventId")] public required int EventId { get; init; }
    [JsonPropertyName("provider")] public required string Provider { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("severity")] public required string Severity { get; init; }
    [JsonPropertyName("explanation")] public required string Explanation { get; init; }
    [JsonPropertyName("commonCauses")] public List<string> CommonCauses { get; init; } = new();
    [JsonPropertyName("recommendedAction")] public string? RecommendedAction { get; init; }
    [JsonPropertyName("docsUrl")] public string? DocsUrl { get; init; }
}

/// <summary>
/// Loads data/event-knowledge-base.json and resolves (EventId, Provider) to
/// a human explanation. Designed to be trivially extensible - see README
/// section "Adding a new event signature". Matching is exact on EventId and
/// case-insensitive substring on Provider (many providers get renamed with
/// version suffixes across Windows builds, e.g. "Microsoft-Windows-Kernel-
/// General" vs "Kernel-General").
/// </summary>
public sealed class EventKnowledgeBase
{
    private readonly Dictionary<(int EventId, string ProviderKey), EventSignature> _index;

    private EventKnowledgeBase(IEnumerable<EventSignature> signatures)
    {
        _index = new Dictionary<(int, string), EventSignature>();
        foreach (var sig in signatures)
        {
            var key = (sig.EventId, NormalizeProvider(sig.Provider));
            // Last one wins on purpose: lets an operator "override" a
            // shipped signature by appending a corrected entry at the
            // end of the JSON file without touching the original line.
            _index[key] = sig;
        }
    }

    public static EventKnowledgeBase LoadFromFile(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        return LoadFromJson(json);
    }

    public static EventKnowledgeBase LoadFromJson(string json)
    {
        var signatures = JsonSerializer.Deserialize<List<EventSignature>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        return new EventKnowledgeBase(signatures);
    }

    /// <summary>
    /// Returns an enriched copy of the event with HumanTitle/Explanation/etc
    /// populated. Falls back to a generic "unmapped event" explanation so
    /// the UI never shows a blank details pane.
    /// </summary>
    public EventRecordModel Explain(EventRecordModel evt)
    {
        var key = (evt.EventId, NormalizeProvider(evt.Provider));
        if (_index.TryGetValue(key, out var sig))
        {
            return evt with
            {
                HumanTitle = sig.Title,
                Explanation = sig.Explanation,
                CommonCauses = sig.CommonCauses,
                RecommendedAction = sig.RecommendedAction,
                DocsUrl = sig.DocsUrl
            };
        }

        return evt with
        {
            HumanTitle = $"Event {evt.EventId} from {evt.Provider}",
            Explanation = "No knowledge-base signature is registered for this EventID/Provider " +
                           "combination yet. The raw message below is the only information available; " +
                           "consider adding a signature to event-knowledge-base.json.",
            CommonCauses = Array.Empty<string>(),
            RecommendedAction = null,
            DocsUrl = null
        };
    }

    private static string NormalizeProvider(string provider) =>
        provider.Trim().ToLowerInvariant();
}
