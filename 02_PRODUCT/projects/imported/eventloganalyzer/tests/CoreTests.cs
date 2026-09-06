using EventLogAnalyzer.Core.Detection;
using EventLogAnalyzer.Core.KnowledgeBase;
using EventLogAnalyzer.Core.Models;
using EventLogAnalyzer.Core.Parsers;
using Xunit;

namespace EventLogAnalyzer.Tests;

public class EvtxParserTests
{
    // Testing EvtxParser.Map(EventRecord,...) end-to-end needs a real, sealed
    // EventRecord instance, which only EventLogReader can construct (from an
    // actual .evtx file). That's covered by an integration test with a
    // fixture .evtx on the Windows CI agent. Here we unit test the one piece
    // of pure logic inside Map that's worth isolating: the Level->Severity
    // mapping table, via the internal MapLevel helper (see AssemblyInfo.cs
    // InternalsVisibleTo).
    [Theory]
    [InlineData((byte)1, Severity.Critical)]
    [InlineData((byte)2, Severity.Error)]
    [InlineData((byte)3, Severity.Warning)]
    [InlineData((byte)4, Severity.Info)]
    [InlineData((byte)5, Severity.Info)]
    [InlineData(null, Severity.Info)]
    public void MapLevel_ReturnsExpectedSeverity(byte? rawLevel, Severity expected)
    {
        var actual = EvtxParser.MapLevel(rawLevel);
        Assert.Equal(expected, actual);
    }
}

public class EventKnowledgeBaseTests
{
    private const string SampleJson = """
    [
      {
        "eventId": 7031,
        "provider": "Service Control Manager",
        "title": "Service terminated unexpectedly",
        "severity": "Error",
        "explanation": "A service crashed and is being restarted.",
        "commonCauses": ["Unhandled exception", "Missing dependency"],
        "recommendedAction": "Check the service's own log source.",
        "docsUrl": "https://example.invalid/7031"
      }
    ]
    """;

    private static EventRecordModel MakeEvent(int id, string provider) => new()
    {
        EventId = id,
        Provider = provider,
        Level = Severity.Error,
        TimeCreated = DateTimeOffset.UtcNow,
        Computer = "PC01",
        Message = "raw message",
        SourceFile = "System.evtx"
    };

    [Fact]
    public void Explain_KnownSignature_ResolvesTitleAndSeverityFields()
    {
        var kb = EventKnowledgeBase.LoadFromJson(SampleJson);
        var raw = MakeEvent(7031, "Service Control Manager");

        var explained = kb.Explain(raw);

        Assert.Equal("Service terminated unexpectedly", explained.HumanTitle);
        Assert.Equal(2, explained.CommonCauses!.Count);
        Assert.Equal("Check the service's own log source.", explained.RecommendedAction);
    }

    [Fact]
    public void Explain_ProviderMatchIsCaseInsensitive()
    {
        var kb = EventKnowledgeBase.LoadFromJson(SampleJson);
        var raw = MakeEvent(7031, "SERVICE CONTROL MANAGER");

        var explained = kb.Explain(raw);

        Assert.Equal("Service terminated unexpectedly", explained.HumanTitle);
    }

    [Fact]
    public void Explain_UnmappedEvent_FallsBackToGenericExplanation()
    {
        var kb = EventKnowledgeBase.LoadFromJson(SampleJson);
        var raw = MakeEvent(9999, "Some Unknown Provider");

        var explained = kb.Explain(raw);

        Assert.Contains("No knowledge-base signature", explained.Explanation);
        Assert.Empty(explained.CommonCauses!);
    }
}

public class RepeatedServiceCrashDetectorTests
{
    private static EventRecordModel CrashEvent(string service, DateTimeOffset time) => new()
    {
        EventId = 7031,
        Provider = "Service Control Manager",
        Level = Severity.Error,
        TimeCreated = time,
        Computer = "PC01",
        Message = $"The {service} service terminated unexpectedly.",
        SourceFile = "System.evtx"
    };

    [Fact]
    public void Detect_FiveCrashesWithinWindow_RaisesHighImpactIssue()
    {
        var baseTime = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 5)
            .Select(i => CrashEvent("Spooler", baseTime.AddMinutes(i * 2)))
            .ToList();

        var detector = new RepeatedServiceCrashDetector();
        var issues = detector.Detect(events);

        var issue = Assert.Single(issues);
        Assert.Equal("RepeatedServiceCrash", issue.Category);
        Assert.Equal(Impact.High, issue.Impact);
        Assert.Equal(5, issue.Count);
        Assert.Contains("Spooler", issue.Title);
    }

    [Fact]
    public void Detect_TwoIsolatedCrashesHoursApart_RaisesNoIssue()
    {
        var baseTime = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var events = new List<EventRecordModel>
        {
            CrashEvent("Spooler", baseTime),
            CrashEvent("Spooler", baseTime.AddHours(3))
        };

        var detector = new RepeatedServiceCrashDetector();
        var issues = detector.Detect(events);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_CrashesFromDifferentServices_AreTrackedIndependently()
    {
        var baseTime = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var events = new List<EventRecordModel>();
        events.AddRange(Enumerable.Range(0, 3).Select(i => CrashEvent("Spooler", baseTime.AddMinutes(i))));
        events.AddRange(Enumerable.Range(0, 3).Select(i => CrashEvent("BITS", baseTime.AddMinutes(i))));

        var detector = new RepeatedServiceCrashDetector();
        var issues = detector.Detect(events);

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Title.Contains("Spooler"));
        Assert.Contains(issues, i => i.Title.Contains("BITS"));
    }
}
