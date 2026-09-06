using EventLogAnalyzer.Core.Models;

namespace EventLogAnalyzer.Core.Detection;

public interface IIssueDetector
{
    string Category { get; }
    IReadOnlyList<Issue> Detect(IReadOnlyList<EventRecordModel> events);
}

/// <summary>
/// Runs the registered detectors over a full event set and returns every
/// Issue found, sorted by severity/impact for the Dashboard. Composition
/// root wires the detector list once at startup.
/// </summary>
public sealed class DetectionEngine
{
    private readonly IReadOnlyList<IIssueDetector> _detectors;

    public DetectionEngine(IReadOnlyList<IIssueDetector> detectors) => _detectors = detectors;

    public IReadOnlyList<Issue> Run(IReadOnlyList<EventRecordModel> events) =>
        _detectors
            .SelectMany(d => d.Detect(events))
            .OrderByDescending(i => i.Impact)
            .ThenByDescending(i => i.Severity)
            .ToList();
}

/// <summary>
/// Flags a service that crashed (EventID 7031/7034 from Service Control
/// Manager) 3+ times within a rolling 30-minute window - the classic
/// "crash loop" pattern that's easy to miss scrolling through a raw log.
/// </summary>
public sealed class RepeatedServiceCrashDetector : IIssueDetector
{
    public string Category => "RepeatedServiceCrash";

    private static readonly int[] CrashEventIds = { 7031, 7034 };
    private const int MinOccurrences = 3;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public IReadOnlyList<Issue> Detect(IReadOnlyList<EventRecordModel> events)
    {
        var crashes = events
            .Where(e => CrashEventIds.Contains(e.EventId)
                        && e.Provider.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.TimeCreated)
            .ToList();

        var issues = new List<Issue>();

        // Group by the service name embedded in the message (MVP heuristic:
        // SCM messages start with "The <Name> service ..."). A production
        // version would parse EventData fields instead of the formatted string.
        var byService = crashes.GroupBy(ExtractServiceName);

        foreach (var group in byService)
        {
            var ordered = group.OrderBy(e => e.TimeCreated).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var windowEvents = ordered
                    .Skip(i)
                    .TakeWhile(e => e.TimeCreated - ordered[i].TimeCreated <= Window)
                    .ToList();

                if (windowEvents.Count >= MinOccurrences)
                {
                    issues.Add(new Issue
                    {
                        Category = Category,
                        Title = $"'{group.Key}' service is crash-looping",
                        Severity = Severity.Error,
                        Impact = windowEvents.Count >= 5 ? Impact.High : Impact.Medium,
                        Count = windowEvents.Count,
                        FirstSeen = windowEvents.First().TimeCreated,
                        LastSeen = windowEvents.Last().TimeCreated,
                        RelatedEvents = windowEvents,
                        Summary = $"{windowEvents.Count} crashes of '{group.Key}' between " +
                                  $"{windowEvents.First().TimeCreated:t} and {windowEvents.Last().TimeCreated:t}.",
                        Recommendation = new Recommendation
                        {
                            Title = $"Investigate and stabilize '{group.Key}'",
                            Steps = new[]
                            {
                                $"Run 'sc qc {group.Key}' to confirm the binary path and dependencies.",
                                $"Check the '{group.Key}' service's own event source for the underlying exception.",
                                "Review recent Windows Update / driver changes around FirstSeen.",
                                "If a third-party service, check for a vendor update addressing crashes."
                            },
                            CanGenerateScript = true
                        }
                    });
                    break; // one issue per service is enough for the MVP dashboard
                }
            }
        }

        return issues;
    }

    private static string ExtractServiceName(EventRecordModel e)
    {
        const string marker = "The ";
        var idx = e.Message.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return "Unknown service";
        var rest = e.Message[(idx + marker.Length)..];
        var end = rest.IndexOf(" service", StringComparison.Ordinal);
        return end > 0 ? rest[..end] : "Unknown service";
    }
}

/// <summary>
/// Flags NTFS/disk errors (Ntfs / Disk sources, EventID 55, 7, 51, 153) -
/// early indicators of failing storage hardware or filesystem corruption.
/// </summary>
public sealed class DiskErrorDetector : IIssueDetector
{
    public string Category => "DiskError";
    private static readonly int[] DiskEventIds = { 7, 51, 55, 153 };

    public IReadOnlyList<Issue> Detect(IReadOnlyList<EventRecordModel> events)
    {
        var diskEvents = events
            .Where(e => DiskEventIds.Contains(e.EventId)
                        && (e.Provider.Contains("disk", StringComparison.OrdinalIgnoreCase)
                            || e.Provider.Contains("ntfs", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.TimeCreated)
            .ToList();

        if (diskEvents.Count == 0) return Array.Empty<Issue>();

        return new[]
        {
            new Issue
            {
                Category = Category,
                Title = "Disk / filesystem errors detected",
                Severity = Severity.Critical,
                Impact = diskEvents.Count >= 3 ? Impact.High : Impact.Medium,
                Count = diskEvents.Count,
                FirstSeen = diskEvents.First().TimeCreated,
                LastSeen = diskEvents.Last().TimeCreated,
                RelatedEvents = diskEvents,
                Summary = $"{diskEvents.Count} disk/NTFS error(s) between " +
                          $"{diskEvents.First().TimeCreated:d} and {diskEvents.Last().TimeCreated:d}.",
                Recommendation = new Recommendation
                {
                    Title = "Verify storage hardware health",
                    Steps = new[]
                    {
                        "Run 'chkdsk C: /scan' (read-only) to check for filesystem corruption.",
                        "Check S.M.A.R.T. status of the affected physical disk.",
                        "Correlate with recent power-loss or improper shutdown events (6008)."
                    },
                    CanGenerateScript = true
                }
            }
        };
    }
}

/// <summary>
/// Flags a burst of failed logons (EventID 4625) that could indicate
/// brute-force or lockout-policy issues.
/// </summary>
public sealed class FailedLogonDetector : IIssueDetector
{
    public string Category => "FailedLogonStorm";
    private const int FailedLogonEventId = 4625;
    private const int MinOccurrences = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public IReadOnlyList<Issue> Detect(IReadOnlyList<EventRecordModel> events)
    {
        var failures = events
            .Where(e => e.EventId == FailedLogonEventId)
            .OrderBy(e => e.TimeCreated)
            .ToList();

        var issues = new List<Issue>();
        for (var i = 0; i < failures.Count; i++)
        {
            var windowEvents = failures
                .Skip(i)
                .TakeWhile(e => e.TimeCreated - failures[i].TimeCreated <= Window)
                .ToList();

            if (windowEvents.Count >= MinOccurrences)
            {
                issues.Add(new Issue
                {
                    Category = Category,
                    Title = "Burst of failed logon attempts",
                    Severity = Severity.Suspicious,
                    Impact = Impact.High,
                    Count = windowEvents.Count,
                    FirstSeen = windowEvents.First().TimeCreated,
                    LastSeen = windowEvents.Last().TimeCreated,
                    RelatedEvents = windowEvents,
                    Summary = $"{windowEvents.Count} failed logons within " +
                              $"{Window.TotalMinutes:0} minutes starting {windowEvents.First().TimeCreated:t}.",
                    Recommendation = new Recommendation
                    {
                        Title = "Review for brute-force / lockout misconfiguration",
                        Steps = new[]
                        {
                            "Identify the target account(s) and source workstation/IP from the event details.",
                            "Confirm whether this matches a known scheduled task or service using a stale credential.",
                            "If unexplained, consider the account compromised and rotate its credential."
                        },
                        CanGenerateScript = false
                    }
                });
                break;
            }
        }

        return issues;
    }
}
