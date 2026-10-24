using System.Text.Json;
using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Case;

public enum ClockMemoryState { Nedeterminat, Ok, Behind, BeforeInstall, ForwardJump }

/// <param name="Severity">"High" (clock behind the application's own record or before the Windows install date), "Info" (large forward jump), "" otherwise.</param>
public sealed record ClockMemoryResult(ClockMemoryState State, string Severity, string Message, TimeSpan? Delta)
{
    /// <summary>Set when an earlier record was made under a wrong clock and nobody has confirmed the correction yet (empty otherwise).</summary>
    public string SuspectNote { get; init; } = "";

    /// <summary>The station clock cannot order events: time-based findings lose one confidence level.</summary>
    public bool TimeUnreliable => State is ClockMemoryState.Behind or ClockMemoryState.BeforeInstall;
}

/// <summary>
/// The application's own time memory: compares the station clock with the last time the application itself recorded in its hash-chained
/// audit log (the "clock.seen" entries written at start and at each report, and the operator's "clock.ack"). Pure function; no I/O.
/// </summary>
public static class ClockMemoryCheck
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ForwardJump = TimeSpan.FromDays(30);

    public static ClockMemoryResult Evaluate(DateTimeOffset nowUtc, DateTimeOffset? latestRecordedUtc, DateTimeOffset? windowsInstallUtc = null)
    {
        if (latestRecordedUtc is { } latest && nowUtc < latest - Tolerance)
        {
            var d = latest - nowUtc;
            return new(ClockMemoryState.Behind, "High",
                $"Ceasul stației arată o oră dinaintea ultimei activități înregistrate de aplicație (cu {Span(d)}): ceasul a fost dat înapoi sau s-a resetat (de exemplu baterie CMOS descărcată).", d);
        }
        if (windowsInstallUtc is { } install && nowUtc < install)
            return new(ClockMemoryState.BeforeInstall, "High",
                "Ora stației este dinaintea instalării Windows: probabil bateria CMOS este descărcată sau ceasul a fost resetat.", install - nowUtc);
        if (latestRecordedUtc is not { } last)
            return new(ClockMemoryState.Nedeterminat, "", "Aplicația nu are încă nicio înregistrare anterioară a orei (prima rulare); ceasul nu poate fi comparat cu ea.", null);
        if (nowUtc > last + ForwardJump)
        {
            var d = nowUtc - last;
            return new(ClockMemoryState.ForwardJump, "Info",
                $"Ceasul a sărit înainte cu {(int)d.TotalDays} zile de la ultima folosire a aplicației. Nu este o eroare (stația poate fi oprită mult timp); verificați că data afișată este cea reală.", d);
        }
        return new(ClockMemoryState.Ok, "", "Ceasul stației este în ordine față de ultima activitate înregistrată de aplicație.", nowUtc - last);
    }

    private static string Span(TimeSpan d) =>
        d.TotalDays >= 1 ? $"{(int)d.TotalDays} zile {d.Hours} h" : d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes} min" : $"{Math.Max(1, (int)d.TotalMinutes)} min";
}

/// <summary>What the chained audit says about time: the baseline the clock is compared with, suspect records and the last operator confirmation.</summary>
public sealed record ClockBaseline(DateTimeOffset? Baseline, string SuspectNote, string AckText, int Records)
{
    public static readonly ClockBaseline Empty = new(null, "", "", 0);
}

/// <summary>Stores the "last seen" time as an entry of the existing hash-chained auth audit (no second, unprotected store).</summary>
public static class ClockMemory
{
    public const string Event = "clock.seen";
    public const string AckEvent = "clock.ack";

    private static string _startupWarning = "";

    /// <summary>Raised when <see cref="StartupWarning"/> changes (an acknowledgement, a re-evaluation).</summary>
    public static event Action? WarningChanged;

    /// <summary>
    /// Warning for the Home page and for investigation confidence; empty when the clock is fine, unknown or only informative.
    /// Refreshed by <see cref="Reevaluate"/> (at each report and when the operator confirms the correction), not fixed at start.
    /// </summary>
    public static string StartupWarning
    {
        get => _startupWarning;
        set { if (_startupWarning == value) return; _startupWarning = value; WarningChanged?.Invoke(); }
    }

    private sealed record Rec(bool Ack, DateTimeOffset When, string Who, string Reason);

    private static List<Rec> ReadRecords(string path)
    {
        var list = new List<Rec>();
        if (!File.Exists(path)) return list;
        foreach (var line in ReadAllLinesShared(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                string ev = "", who = "", reason = ""; DateTimeOffset? when = null;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) continue;
                    if (p.Name.Equals("event", StringComparison.OrdinalIgnoreCase)) ev = p.Value.GetString() ?? "";
                    else if (p.Name.Equals("who", StringComparison.OrdinalIgnoreCase)) who = p.Value.GetString() ?? "";
                    else if (p.Name.Equals("reason", StringComparison.OrdinalIgnoreCase)) reason = p.Value.GetString() ?? "";
                    else if (p.Name.Equals("whenUtc", StringComparison.OrdinalIgnoreCase) && p.Value.TryGetDateTimeOffset(out var t)) when = t;
                }
                if (when is { } w && (ev == Event || ev == AckEvent)) list.Add(new Rec(ev == AckEvent, w, who, reason));
            }
            catch (JsonException) { }
        }
        return list;
    }

    /// <summary>
    /// Baseline in chain order: starts at the last "clock.ack" (if any); a record whose time lies after the time of the NEXT appended record
    /// was made under a wrong clock and is skipped. The baseline is the last record that is not suspect. Null baseline = no clock records.
    /// </summary>
    public static ClockBaseline ReadBaseline(string auditPath)
    {
        var all = ReadRecords(auditPath);
        if (all.Count == 0) return ClockBaseline.Empty;
        var lastAck = all.FindLastIndex(r => r.Ack);
        var recs = lastAck >= 0 ? all.GetRange(lastAck, all.Count - lastAck) : all;
        DateTimeOffset? baseline = null;
        var suspect = "";
        for (var i = 0; i < recs.Count; i++)
        {
            var isSuspect = i + 1 < recs.Count && recs[i].When > recs[i + 1].When + ClockMemoryCheck.Tolerance;
            if (isSuspect)
            {
                if (suspect.Length == 0)
                    suspect = $"S-a detectat o înregistrare făcută cu ceasul greșit la {recs[i].When.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC (mai târziu decât înregistrarea următoare); nu este folosită ca reper.";
            }
            else baseline = recs[i].When;
        }
        var ackText = lastAck >= 0
            ? $"Ora a fost corectată de {all[lastAck].Who} la {all[lastAck].When.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC, motiv: {all[lastAck].Reason}"
            : "";
        return new(baseline, suspect, ackText, all.Count);
    }

    /// <summary>The time the clock is compared with (see <see cref="ReadBaseline"/>, first path only); null when the chain has no clock records.</summary>
    public static DateTimeOffset? LatestRecorded(params string[] chainPaths) => chainPaths.Length == 0 ? null : ReadBaseline(chainPaths[0]).Baseline;

    private static IEnumerable<string> ReadAllLinesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } l) yield return l;
    }

    /// <summary>Appends a "clock.seen" entry to the chained audit at <paramref name="auditPath"/>.</summary>
    public static void RecordSeen(string auditPath, DateTimeOffset nowUtc, string context)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(auditPath))!);
        new HashChain(auditPath).Append(new AuthAuditEntry(Event, "aplicație", "", "", "", "ok", context, nowUtc, "", ""));
    }

    /// <summary>
    /// The operator confirms that the clock was corrected: appends "clock.ack" (operator, reason, previous recorded time, current time).
    /// Later comparisons start from this entry and the warning is re-evaluated at once.
    /// </summary>
    public static void Acknowledge(string auditPath, DateTimeOffset nowUtc, string who, string reason, DateTimeOffset? installUtc = null)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Motivul este obligatoriu.", nameof(reason));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(auditPath))!);
        var prev = ReadBaseline(auditPath).Baseline;
        var detail = $"anterior={(prev is { } p ? p.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") : "niciuna")}; acum={nowUtc.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}";
        new HashChain(auditPath).Append(new AuthAuditEntry(AckEvent, string.IsNullOrWhiteSpace(who) ? "operator" : who.Trim(), "", "", "", "ok", reason.Trim(), nowUtc, detail, ""));
        Reevaluate(auditPath, nowUtc, installUtc);
    }

    /// <summary>Checks the clock against the records, then records the current time. Never throws on I/O problems: the check is advisory.</summary>
    public static ClockMemoryResult CheckAndRecord(string auditPath, DateTimeOffset nowUtc, DateTimeOffset? installUtc, string context)
    {
        var result = Evaluate(auditPath, nowUtc, installUtc);
        try { RecordSeen(auditPath, nowUtc, context); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result;
    }

    public static ClockMemoryResult Evaluate(string auditPath, DateTimeOffset nowUtc, DateTimeOffset? installUtc)
    {
        var b = ClockBaseline.Empty;
        try { b = ReadBaseline(auditPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return ClockMemoryCheck.Evaluate(nowUtc, b.Baseline, installUtc) with { SuspectNote = b.SuspectNote };
    }

    /// <summary>Re-evaluates without recording and refreshes <see cref="StartupWarning"/> (a High verdict, or a suspect record not yet confirmed).</summary>
    public static ClockMemoryResult Reevaluate(string auditPath, DateTimeOffset nowUtc, DateTimeOffset? installUtc)
    {
        var r = Evaluate(auditPath, nowUtc, installUtc);
        StartupWarning = r.Severity == "High" ? r.Message : r.SuspectNote;
        return r;
    }

    private static string? _auditPath;
    private static DateTimeOffset? _installUtc;

    /// <summary>
    /// Re-evaluates the warning from the chain of the audit used at start (no-op when there is none), so a report or an investigation made
    /// after the operator confirmed the correction is not lowered by a stale start-up verdict.
    /// </summary>
    public static void Refresh(DateTimeOffset? nowUtc = null)
    {
        var p = _auditPath;
        if (p is null || !File.Exists(p)) return;
        try { Reevaluate(p, nowUtc ?? DateTimeOffset.UtcNow, _installUtc); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>App start: runs the check, keeps the Home warning and records the time.</summary>
    public static ClockMemoryResult Startup(string auditPath, DateTimeOffset nowUtc, DateTimeOffset? installUtc)
    {
        _auditPath = auditPath; _installUtc = installUtc;
        var r = CheckAndRecord(auditPath, nowUtc, installUtc, "pornirea aplicației");
        StartupWarning = r.Severity == "High" ? r.Message : r.SuspectNote;
        return r;
    }
}
