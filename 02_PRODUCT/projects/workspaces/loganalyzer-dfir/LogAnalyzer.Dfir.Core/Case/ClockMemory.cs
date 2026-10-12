using System.Text.Json;
using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Case;

public enum ClockMemoryState { Nedeterminat, Ok, Behind, BeforeInstall, ForwardJump }

/// <param name="Severity">"High" (clock behind the application's own record or before the Windows install date), "Info" (large forward jump), "" otherwise.</param>
public sealed record ClockMemoryResult(ClockMemoryState State, string Severity, string Message, TimeSpan? Delta)
{
    /// <summary>The station clock cannot order events: time-based findings lose one confidence level.</summary>
    public bool TimeUnreliable => State is ClockMemoryState.Behind or ClockMemoryState.BeforeInstall;
}

/// <summary>
/// The application's own time memory: compares the station clock with the latest time the application itself recorded in its hash-chained
/// audit logs (the "clock.seen" entries written at start and at each report, the case custody log). Pure function; no I/O.
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

/// <summary>Stores the "last seen" time as an entry of the existing hash-chained auth audit (no second, unprotected store).</summary>
public static class ClockMemory
{
    public const string Event = "clock.seen";

    /// <summary>Warning for the Home page, set at application start; empty when the clock is fine, unknown or only informative.</summary>
    public static string StartupWarning { get; set; } = "";

    /// <summary>Latest time written in any of the given chained logs (every property whose name ends in "Utc" and holds a date); null when none.</summary>
    public static DateTimeOffset? LatestRecorded(params string[] chainPaths)
    {
        DateTimeOffset? max = null;
        foreach (var path in chainPaths)
        {
            if (!File.Exists(path)) continue;
            foreach (var line in ReadAllLinesShared(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (p.Name.EndsWith("Utc", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String
                            && p.Value.TryGetDateTimeOffset(out var t) && (max is null || t > max)) max = t;
                }
                catch (JsonException) { }
            }
        }
        return max;
    }

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

    /// <summary>Checks the clock against the records, then records the current time. Never throws on I/O problems: the check is advisory.</summary>
    public static ClockMemoryResult CheckAndRecord(string auditPath, DateTimeOffset nowUtc, DateTimeOffset? installUtc, string context, params string[] otherChains)
    {
        var result = Evaluate(auditPath, nowUtc, installUtc, otherChains);
        try { RecordSeen(auditPath, nowUtc, context); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result;
    }

    public static ClockMemoryResult Evaluate(string auditPath, DateTimeOffset nowUtc, DateTimeOffset? installUtc, params string[] otherChains)
    {
        DateTimeOffset? latest = null;
        try { latest = LatestRecorded([auditPath, .. otherChains]); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return ClockMemoryCheck.Evaluate(nowUtc, latest, installUtc);
    }

    /// <summary>App start: runs the check, keeps the Home warning (only for High) and records the time.</summary>
    public static ClockMemoryResult Startup(string auditPath, DateTimeOffset nowUtc, DateTimeOffset? installUtc)
    {
        var r = CheckAndRecord(auditPath, nowUtc, installUtc, "pornirea aplicației");
        StartupWarning = r.Severity == "High" ? r.Message : "";
        return r;
    }
}
