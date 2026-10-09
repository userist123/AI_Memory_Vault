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

/// <summary>A System shutdown / restart record (1074 initiated, 6006 event log stopped, 6005 event log started = next boot) used to pair a Security 1100.</summary>
public sealed record SystemLifecycleEvent(DateTimeOffset TimeUtc, string Channel, int EventId);

/// <summary>Security 1100: the event logging service was shut down. The event carries no subject, so the user is usually empty.</summary>
public sealed record ServiceStopEvent(DateTimeOffset TimeUtc, string SubjectUser = "", string SubjectDomain = "");

/// <summary>Security 4719: the system audit policy was changed. <see cref="AuditPolicyChanges"/> is the raw value (%%8448 success removed, %%8449 success added, %%8450 failure removed, %%8451 failure added).</summary>
public sealed record AuditPolicyChangeEvent(DateTimeOffset TimeUtc, string SubjectUser, string SubjectDomain, string SubjectSid, string AuditPolicyChanges);

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
            bool escalating = false;

            var channels = clears.Where(o => SameSubject(o, c) && (o.TimeUtc - c.TimeUtc).Duration() <= MultiChannelWindow)
                                 .Select(o => o.Channel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (channels.Count > 1)
            {
                factors.Add($"mai multe canale golite de același utilizator în 10 min ({string.Join(", ", channels)})");
                // Planned maintenance often clears several channels together: reported, but it escalates only an unplanned clear.
                escalating |= lifecycle != LogClearLifecycle.Routine;
            }

            if (otherHighFindingTimesUtc.Any(t => (t - c.TimeUtc).Duration() <= CorrelationWindow))
            {
                factors.Add("golirea este la mai puțin de ±60 min de alt rezultat de severitate High/Critical din același caz");
                escalating = true;
            }

            var severity = escalating || lifecycle == LogClearLifecycle.Unexpected ? Severity.High
                         : lifecycle == LogClearLifecycle.Routine ? Severity.Info
                         : Severity.Medium;
            result.Add(new LogClearAssessmentItem(c, lifecycle, factors, severity, ReasonFor(c, lifecycle, factors)));
        }
        return result;
    }

    /// <summary>
    /// For the single-event legacy engines, which have no other findings to corroborate with. Without a <paramref name="policy"/> the result is always
    /// <see cref="LogClearLifecycle.NotAssessed"/> (as before the procedure profile existed); with the profile's policy the clear is Routine or Unexpected.
    /// </summary>
    public static LogClearAssessmentItem AssessUnscheduled(string channel, DateTimeOffset timeUtc, LogMaintenancePolicy? policy = null, string subjectUser = "", string subjectDomain = "") =>
        Assess([new LogClearEvent(channel, timeUtc, subjectUser, subjectDomain)], policy, [])[0];

    /// <summary>SubjectUserName / SubjectDomainName from the &lt;EventData&gt; XML of a Security 1102 or System 104 event ("" when absent).</summary>
    public static (string User, string Domain) SubjectFromXml(string? xml)
    {
        static string Field(string? x, string name)
        {
            if (string.IsNullOrEmpty(x)) return "";
            var m = System.Text.RegularExpressions.Regex.Match(x, $"(?:<Data\\s+Name=\"{name}\"\\s*>|<{name}>)([^<]*)<", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";
        }
        return (Field(xml, "SubjectUserName"), Field(xml, "SubjectDomainName"));
    }

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
        return ApprovedInWindow(c.TimeUtc, c.SubjectUser, c.SubjectDomain, policy) ? LogClearLifecycle.Routine : LogClearLifecycle.Unexpected;
    }

    private static bool ApprovedInWindow(DateTimeOffset timeUtc, string user, string domain, LogMaintenancePolicy policy)
    {
        if (user.Length == 0) return false;   // an event without a subject cannot be matched to an approved account
        var who = new LogClearEvent("", timeUtc, user, domain);
        return policy.ApprovedAccounts.Any(a => AccountMatches(a, who)) && policy.Windows.Any(w => timeUtc >= w.StartUtc && timeUtc <= w.EndUtc);
    }

    // ---- owner decision 27: Security 1100 (logging service stopped) and 4719 (audit policy changed) ----

    /// <summary>A shutdown / restart record must be this close to a 1100 for the stop to be an ordinary shutdown (inclusive).</summary>
    public static readonly TimeSpan ShutdownPairingWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Security 1100. Routine when System 1074 / 6006 lies within 10 minutes of it (either side: the restart is announced just before the
    /// service stops), or the first System 6005 (next boot) follows within 10 minutes, or an approved account does it inside a maintenance
    /// window. Otherwise NotAssessed (Medium) without a procedure profile and Unexpected (High) with one.
    /// </summary>
    public static LogClearAssessmentItem AssessServiceStop(ServiceStopEvent stop, LogMaintenancePolicy? policy, IReadOnlyList<SystemLifecycleEvent> systemEvents)
    {
        var clear = new LogClearEvent("Security (1100)", stop.TimeUtc, stop.SubjectUser, stop.SubjectDomain);
        var pair = systemEvents.Where(e => e.EventId is 1074 or 6006 && (e.TimeUtc - stop.TimeUtc).Duration() <= ShutdownPairingWindow)
                               .OrderBy(e => (e.TimeUtc - stop.TimeUtc).Duration()).FirstOrDefault()
                   ?? systemEvents.Where(e => e.EventId == 6005 && e.TimeUtc >= stop.TimeUtc && e.TimeUtc - stop.TimeUtc <= ShutdownPairingWindow)
                                  .OrderBy(e => e.TimeUtc).FirstOrDefault();
        if (pair is not null)
            return new(clear, LogClearLifecycle.Routine, [], Severity.Info,
                $"serviciul de jurnalizare oprit (Security 1100) la {stop.TimeUtc:yyyy-MM-dd HH:mm} UTC, în cadrul unei opriri/reporniri a sistemului (System {pair.EventId} la {pair.TimeUtc:yyyy-MM-dd HH:mm} UTC): planificată.");
        if (policy is not null && ApprovedInWindow(stop.TimeUtc, stop.SubjectUser, stop.SubjectDomain, policy))
            return new(clear, LogClearLifecycle.Routine, [], Severity.Info,
                $"serviciul de jurnalizare oprit (Security 1100) la {stop.TimeUtc:yyyy-MM-dd HH:mm} UTC de un cont aprobat, în fereastra de mentenanță: planificată.");
        return policy is null
            ? new(clear, LogClearLifecycle.NotAssessed, [], Severity.Medium,
                $"serviciul de jurnalizare oprit (Security 1100) la {stop.TimeUtc:yyyy-MM-dd HH:mm} UTC fără o oprire/repornire a sistemului în ±10 min; necesită verificare: nu există profil de procedură care să arate dacă oprirea a fost planificată.")
            : new(clear, LogClearLifecycle.Unexpected, [], Severity.High,
                $"serviciul de jurnalizare oprit (Security 1100) la {stop.TimeUtc:yyyy-MM-dd HH:mm} UTC fără o oprire/repornire a sistemului în ±10 min și fără un cont aprobat în fereastra de mentenanță: neașteptată.");
    }

    /// <summary>
    /// Security 4719. Routine when applied by Group Policy (SYSTEM / the computer account / S-1-5-18) or by an approved account inside a window.
    /// A non-routine change that REMOVES success or failure auditing is High (also without a profile); any other non-routine change is Medium.
    /// </summary>
    public static LogClearAssessmentItem AssessAuditPolicyChange(AuditPolicyChangeEvent change, LogMaintenancePolicy? policy)
    {
        var clear = new LogClearEvent("Security (4719)", change.TimeUtc, change.SubjectUser, change.SubjectDomain);
        string who = change.SubjectUser.Length > 0 ? $"{(change.SubjectDomain.Length > 0 ? change.SubjectDomain + "\\" : "")}{change.SubjectUser}" : "utilizator necunoscut";
        string head = $"politica de audit modificată (Security 4719) la {change.TimeUtc:yyyy-MM-dd HH:mm} UTC de {who}";
        bool removes = change.AuditPolicyChanges.Contains("%%8448", StringComparison.Ordinal) || change.AuditPolicyChanges.Contains("%%8450", StringComparison.Ordinal);
        string what = removes ? " (elimină auditarea de succes/eșec)" : "";
        bool gpo = change.SubjectUser.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || change.SubjectUser.EndsWith('$')
                   || change.SubjectSid.Equals("S-1-5-18", StringComparison.OrdinalIgnoreCase);
        if (gpo)
            return new(clear, LogClearLifecycle.Routine, [], Severity.Info, head + what + ": aplicată prin Group Policy (SYSTEM / contul calculatorului): planificată.");
        if (policy is not null && ApprovedInWindow(change.TimeUtc, change.SubjectUser, change.SubjectDomain, policy))
            return new(clear, LogClearLifecycle.Routine, [], Severity.Info, head + what + ": cont aprobat, în fereastra de mentenanță: planificată.");
        var lifecycle = policy is null ? LogClearLifecycle.NotAssessed : LogClearLifecycle.Unexpected;
        string verdict = policy is null ? "necesită verificare: nu există profil de procedură care să arate dacă schimbarea a fost planificată" : "nu se potrivește cu profilul de mentenanță";
        return new(clear, lifecycle, [], removes ? Severity.High : Severity.Medium, head + what + ": " + verdict + ".");
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
