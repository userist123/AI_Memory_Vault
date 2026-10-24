using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// One line of a device's history: <c>TimeUtc</c> null = the time is not known (a record whose write was refused and is still pending). <c>Pending</c> = the record is NOT
/// sealed yet: it comes from the monitor's journal, which is untrusted, and its <c>Kind</c> says so.
/// </summary>
public sealed record DeviceHistoryEvent(DateTimeOffset? TimeUtc, string Kind, string User, string Details, bool Pending)
{
    /// <summary>True when the sealed record cannot be relied on (the audit chain does not verify, or the record comes after one with an earlier time): its <c>Kind</c> says so.</summary>
    public bool Unverified { get; init; }
}

/// <summary>
/// The history of one device as the SEALED operation audit holds it (pure, from <see cref="DeviceAuditReading.Events"/>): first seen, connections, disconnections, changes of the
/// registration number (old → new, operator), alerts and state changes, in time order. Records still waiting to be sealed (<see cref="PendingSeal"/>) are added at the end,
/// marked „în așteptare (nesigilat)”.
/// </summary>
public static class DeviceHistory
{
    public const string Title = "din jurnalul sigilat";
    public const string PendingMark = "în așteptare (nesigilat)";
    public const string UnverifiedMark = "neverificat", BackdatedMark = "neverificat (dată anterioară)";
    public const string FirstSeen = "Prima apariție", Connected = "Conectare", Disconnected = "Deconectare", PresentAtStart = "Prezent la pornirea monitorului",
                        RegistrationChanged = "Schimbare nr. de înregistrare", Alert = "Alertă", StateChanged = "Schimbare stare", Reclassified = "Tip reclasificat";
    public const string NoUser = "—";

    /// <summary>The audit records that belong in a device's history.</summary>
    public static bool IsHistoryEvent(string ev) =>
        ev is OperationAudit.DeviceFirstSeen or OperationAudit.DeviceDisconnected or OperationAudit.DevicePresent or OperationAudit.DeviceRegistrationChanged
            or OperationAudit.DeviceAlertCritical or OperationAudit.DeviceAlertInfo or OperationAudit.DeviceStateChanged or OperationAudit.DeviceReclassified;

    /// <summary>The history of the device named <paramref name="identity"/>: sealed events by time (the audit order for equal times), then the pending ones.</summary>
    public static IReadOnlyList<DeviceHistoryEvent> Build(string identity, DeviceAuditReading? audit, IEnumerable<PendingSeal>? pending = null) =>
        Build([identity], audit, pending);

    /// <summary>
    /// The history of one device shown as one row although the journal has several rows for it (rows folded on the page, <see cref="DeviceJournalMerge.MarkSuperseded"/>):
    /// the sealed events of every identity, merged by time.
    /// </summary>
    public static IReadOnlyList<DeviceHistoryEvent> Build(IReadOnlyCollection<string> identities, DeviceAuditReading? audit, IEnumerable<PendingSeal>? pending = null)
    {
        var keys = new HashSet<string>(identities.Select(OperationAudit.DeviceField), StringComparer.OrdinalIgnoreCase);
        var sealedLines = new List<(DateTimeOffset Time, int Order, DeviceHistoryEvent Event)>();
        int order = 0;
        if (audit is not null)
            foreach (var e in audit.Events.Where(e => keys.Contains(e.Identity)))
            {
                // a chain that does not verify proves nothing; a record after one with an earlier time has a time that cannot be relied on
                var mark = !audit.ChainValid ? UnverifiedMark : e.Index >= audit.FirstRegression ? BackdatedMark : null;
                foreach (var (kind, user, details) in Map(e.Event, e.Fields, e.Operator))
                    sealedLines.Add((e.TimeUtc, order++, new DeviceHistoryEvent(e.TimeUtc, mark is null ? kind : kind + " — " + mark, user, details, false) { Unverified = mark is not null }));
            }
        var result = sealedLines.OrderBy(x => x.Time).ThenBy(x => x.Order).Select(x => x.Event).ToList();
        foreach (var p in pending ?? [])
        {
            var f = Fields(p.Detail);
            // a pending record carries the identity of its time: a medium re-keyed since keeps its pending history (the sealed re-key map is followed)
            if (!f.TryGetValue("identity", out var id) || !keys.Contains(audit?.Follow(id) ?? id)) continue;
            if (!IsHistoryEvent(p.Event)) continue;
            DateTimeOffset? when = f.TryGetValue("first_seen", out var fs) && DateTimeOffset.TryParse(fs, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;
            foreach (var (kind, user, details) in Map(p.Event, f, ""))
                result.Add(new DeviceHistoryEvent(when, kind + " — " + PendingMark, user, details, true));
        }
        return result;
    }

    /// <summary>What to tell the operator about the source of the history when the sealed audit cannot be relied on; empty when it can.</summary>
    public static string Notice(DeviceAuditReading? audit) =>
        audit is null ? "Jurnalul de audit sigilat nu a putut fi citit: se arată doar înregistrările în așteptare."
        : !audit.ChainValid ? "Lanțul jurnalului de audit sigilat nu se verifică: istoricul de mai jos nu este de încredere."
        : "";

    private static IEnumerable<(string Kind, string User, string Details)> Map(string ev, IReadOnlyDictionary<string, string> f, string op)
    {
        string V(string k) => f.TryGetValue(k, out var v) ? v : "";
        string User() => V("user").Length > 0 ? V("user") : NoUser;
        switch (ev)
        {
            case OperationAudit.DeviceFirstSeen:
                yield return (FirstSeen, User(), Join(V("device"), V("serial").Length > 0 ? "serie " + V("serial") : "", V("note")));
                break;
            case OperationAudit.DeviceReclassified:
                yield return (Reclassified, V("operator").Length > 0 ? V("operator") : NoUser, DeviceMonitorAuditCheck.ReclassifiedText(f));
                break;
            case OperationAudit.DevicePresent:
                yield return (PresentAtStart, User(), V("device"));
                break;
            case OperationAudit.DeviceDisconnected:
                yield return (Disconnected, User(), V("device"));
                break;
            case OperationAudit.DeviceRegistrationChanged:
                yield return (RegistrationChanged, op.Length > 0 ? op : NoUser, $"{V("old")} → {V("new")}");
                break;
            case OperationAudit.DeviceStateChanged:
            {
                int.TryParse(V("code"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var code);
                var status = DeviceMonitorRules.StatusText(DeviceMonitorRules.Status(true, code));
                yield return (StateChanged, NoUser, status + (code > 0 ? $" (cod {code}: {DeviceMonitorRules.ProblemText(code)})" : "") + (V("active") == "nu" ? "; nu este activ" : ""));
                break;
            }
            case OperationAudit.DeviceAlertCritical:
            case OperationAudit.DeviceAlertInfo:
            {
                var text = V("text");
                bool critical = ev == OperationAudit.DeviceAlertCritical;
                // every alert is raised at a connection; the connection is a line of its own, an alert proper (critical, or a volume that changed) is another
                if (!DeviceMonitorRules.IsVolumeChangedAlert(text)) yield return (Connected, User(), V("device"));
                if (critical || DeviceMonitorRules.IsVolumeChangedAlert(text)) yield return (Alert, User(), (critical ? "critică: " : "informare: ") + text);
                break;
            }
        }
    }

    private static string Join(params string[] parts) => string.Join("; ", parts.Where(p => p.Length > 0));

    private static Dictionary<string, string> Fields(string detail)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in detail.Split("; "))
            if (kv.IndexOf('=') is var eq and > 0) d[kv[..eq]] = kv[(eq + 1)..];
        return d;
    }
}
