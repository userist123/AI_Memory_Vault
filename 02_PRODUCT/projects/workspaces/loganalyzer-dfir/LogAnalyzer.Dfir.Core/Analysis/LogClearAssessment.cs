using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>One log-clear event (Security 1102, System 104): which channel, when (UTC) and who cleared it.</summary>
public sealed record LogClearEvent(string Channel, DateTimeOffset TimeUtc, string SubjectUser, string SubjectDomain = "");

/// <summary>A planned maintenance window, inclusive at both ends (UTC).</summary>
public sealed record MaintenanceWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>
/// Accounts allowed to clear logs and the windows in which they may. Comes from the procedure profile (WP15). A <c>null</c>
/// policy means "not defined yet", which is different from an empty policy ("nobody is approved").
/// Accounts are written as <c>DOMAIN\user</c> or a bare <c>user</c>; comparison ignores case.
/// </summary>
public sealed record LogMaintenancePolicy(IReadOnlyCollection<string> ApprovedAccounts, IReadOnlyList<MaintenanceWindow> Windows);

/// <summary>Routine: planned and by an approved account. Unexpected: a policy exists and the clear does not match it. NotAssessed: no policy to compare with.</summary>
public enum LogClearLifecycle { Routine, Unexpected, NotAssessed }

public sealed record LogClearAssessmentItem(LogClearEvent Clear, LogClearLifecycle Lifecycle, IReadOnlyList<string> Factors, Severity Severity, string Reason);

/// <summary>
/// The single place where a cleared event log is given a severity. A clear is an observation, not proof of intent: it may be
/// planned maintenance. Severity therefore depends on whether the clear was planned (lifecycle) and on corroborating factors,
/// never on the hour of the day, and a clear on its own is never Critical.
/// </summary>
public static class LogClearAssessment
{
    public static readonly TimeSpan MultiChannelWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan CorrelationWindow = TimeSpan.FromMinutes(60);

    public static IReadOnlyList<LogClearAssessmentItem> Assess(IReadOnlyList<LogClearEvent> clears, LogMaintenancePolicy? policy, IReadOnlyList<DateTimeOffset> otherHighFindingTimesUtc)
    {
        var result = new List<LogClearAssessmentItem>(clears.Count);
        foreach (var c in clears)
        {
            var lifecycle = Lifecycle(c, policy);
            var factors = new List<string>();

            var channels = clears.Where(o => SameSubject(o, c) && (o.TimeUtc - c.TimeUtc).Duration() <= MultiChannelWindow)
                                 .Select(o => o.Channel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (channels.Count > 1)
                factors.Add($"mai multe canale golite de același utilizator în 10 min ({string.Join(", ", channels)})");

            if (otherHighFindingTimesUtc.Any(t => (t - c.TimeUtc).Duration() <= CorrelationWindow))
                factors.Add("golirea este la mai puțin de ±60 min de alt rezultat de severitate High/Critical din același caz");

            var severity = factors.Count > 0 || lifecycle == LogClearLifecycle.Unexpected ? Severity.High
                         : lifecycle == LogClearLifecycle.Routine ? Severity.Info
                         : Severity.Medium;
            result.Add(new LogClearAssessmentItem(c, lifecycle, factors, severity, ReasonFor(c, lifecycle, factors)));
        }
        return result;
    }

    /// <summary>
    /// For the single-event legacy engines, which have no subject, no policy and no other findings: always <see cref="LogClearLifecycle.NotAssessed"/>.
    /// </summary>
    public static LogClearAssessmentItem AssessUnscheduled(string channel, DateTimeOffset timeUtc) =>
        Assess([new LogClearEvent(channel, timeUtc, "")], null, [])[0];

    /// <summary>Highest severity of the assessed clears; Info when there is none.</summary>
    public static Severity Overall(IReadOnlyList<LogClearAssessmentItem> items) => items.Count == 0 ? Severity.Info : items.Max(i => i.Severity);

    public static string LifecycleText(LogClearLifecycle l) => l switch
    {
        LogClearLifecycle.Routine => "planificată (cont aprobat, în fereastra de mentenanță)",
        LogClearLifecycle.Unexpected => "neașteptată (nu se potrivește cu profilul de mentenanță)",
        _ => "neevaluată (nu există profil de procedură)",
    };

    private static LogClearLifecycle Lifecycle(LogClearEvent c, LogMaintenancePolicy? policy)
    {
        if (policy is null) return LogClearLifecycle.NotAssessed;
        bool approved = policy.ApprovedAccounts.Any(a => AccountMatches(a, c));
        bool inWindow = policy.Windows.Any(w => c.TimeUtc >= w.StartUtc && c.TimeUtc <= w.EndUtc);
        return approved && inWindow ? LogClearLifecycle.Routine : LogClearLifecycle.Unexpected;
    }

    private static bool AccountMatches(string approved, LogClearEvent c)
    {
        approved = approved.Trim();
        if (approved.Contains('\\'))
            return approved.Equals($"{c.SubjectDomain}\\{c.SubjectUser}", StringComparison.OrdinalIgnoreCase);
        return approved.Equals(c.SubjectUser, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameSubject(LogClearEvent a, LogClearEvent b) =>
        a.SubjectUser.Equals(b.SubjectUser, StringComparison.OrdinalIgnoreCase) && a.SubjectDomain.Equals(b.SubjectDomain, StringComparison.OrdinalIgnoreCase);

    private static string ReasonFor(LogClearEvent c, LogClearLifecycle lifecycle, IReadOnlyList<string> factors)
    {
        string who = c.SubjectUser.Length > 0 ? $"{(c.SubjectDomain.Length > 0 ? c.SubjectDomain + "\\" : "")}{c.SubjectUser}" : "utilizator necunoscut";
        string head = lifecycle == LogClearLifecycle.NotAssessed
            ? "necesită verificare: nu există profil de procedură care să arate dacă golirea a fost planificată"
            : $"golire {LifecycleText(lifecycle)}";
        return factors.Count == 0 ? $"{c.Channel} golit de {who}: {head}." : $"{c.Channel} golit de {who}: {head}; factori: {string.Join("; ", factors)}.";
    }
}
