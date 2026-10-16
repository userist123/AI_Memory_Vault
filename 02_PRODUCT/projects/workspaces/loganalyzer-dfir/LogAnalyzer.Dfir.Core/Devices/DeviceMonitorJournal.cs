using System.Globalization;
using System.Text.Json;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// Where the device monitor's journal lives (application data, next to the station's other settings) and how it is read. Writing goes through
/// <see cref="LogAnalyzer.Dfir.IO.Json.Write{T}"/> (temporary file, then replace), called by the application.
/// </summary>
public static class DeviceJournalStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogAnalyzer", "device_monitor.json");

    /// <summary>
    /// The journal at <paramref name="path"/>; a missing file is an empty journal without a problem. An unreadable or damaged file gives an empty journal and
    /// the reason; a damaged file is first set aside (renamed with ".deteriorat-" and the time) so its content is not overwritten by the next save.
    /// </summary>
    public static DeviceJournal Load(string path, out string problem)
    {
        problem = "";
        try
        {
            if (!File.Exists(path)) return new DeviceJournal();
            var j = LogAnalyzer.Dfir.IO.Json.Read<DeviceJournal>(path);
            j.Devices ??= [];
            j.Alerts ??= [];
            j.Devices.RemoveAll(d => d is null || d.Identity.Length == 0);
            return j;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            problem = "Jurnalul monitorului de dispozitive este deteriorat (" + ex.Message + ")";
            try
            {
                var aside = path + ".deteriorat-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                File.Move(path, aside);
                problem += $"; a fost păstrat ca „{Path.GetFileName(aside)}” și se începe un jurnal nou.";
            }
            catch (Exception mv) when (mv is IOException or UnauthorizedAccessException) { problem += "; nu a putut fi păstrat deoparte: " + mv.Message + "."; }
            return new DeviceJournal();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = "Jurnalul monitorului de dispozitive nu poate fi citit: " + ex.Message;
            return new DeviceJournal();
        }
    }
}

/// <summary>What the control report says about the monitor's journal for one period.</summary>
public sealed record DeviceMonitorSummary(IReadOnlyList<DeviceJournalEntry> Devices, IReadOnlyList<DeviceAlertRecord> Alerts, IReadOnlyList<DeviceJournalEntry> Offending)
{
    public int Critical => Alerts.Count(a => a.Severity == DeviceAlertSeverity.Critical);
}

/// <summary>The device section of the station control report (check D05 and the report section): pure, from the journal.</summary>
public static class DeviceMonitorReport
{
    public const string NoRegistration = "fără număr de înregistrare";

    /// <summary>
    /// Every device the monitor saw in the period (present now, connected, disconnected or first seen since <paramref name="fromUtc"/>), the alerts raised in
    /// it, and the devices that break the policy: a storage medium without a registration number, or a class forbidden on an isolated station.
    /// Order: external devices first by class and name, internal components last.
    /// </summary>
    public static DeviceMonitorSummary Build(DeviceJournal journal, DateTimeOffset fromUtc, DateTimeOffset toUtc, bool isolated)
    {
        bool InPeriod(DeviceJournalEntry e) => e.FirstSeenUtc <= toUtc &&
            (e.Present || e.FirstSeenUtc >= fromUtc || e.LastConnectedUtc >= fromUtc || e.LastDisconnectedUtc >= fromUtc);
        var devices = journal.Devices.Where(InPeriod)
            .OrderBy(e => e.Class is DeviceClass.Internal or DeviceClass.InternalDisk ? 1 : 0).ThenBy(e => e.Class).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var alerts = journal.Alerts.Where(a => a.TimeUtc >= fromUtc && a.TimeUtc <= toUtc).OrderBy(a => a.TimeUtc).ToList();
        var offending = devices.Where(e => Offends(e, isolated)).ToList();
        return new DeviceMonitorSummary(devices, alerts, offending);
    }

    public static bool Offends(DeviceJournalEntry e, bool isolated) =>
        (DeviceMonitorRules.IsStorageMedium(e.Class) && e.RegistrationNumber.Trim().Length == 0) || (isolated && DeviceMonitorRules.ForbiddenOnIsolated(e.Class, e.InstanceId));

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>One device in the controller's words: class, name, ids, driver, connected or when it was disconnected, and its registration number.</summary>
    public static string Line(DeviceJournalEntry e)
    {
        var parts = new List<string> { DeviceMonitorRules.ClassText(e.Class), e.Name.Length > 0 ? e.Name : "(fără nume)" };
        if (e.Manufacturer.Length > 0) parts.Add(e.Manufacturer);
        if (e.VidPid.Length > 0) parts.Add("VID/PID " + e.VidPid);
        parts.Add(e.Serial.Length > 0 ? "SN " + e.Serial : "fără serie unică");
        parts.Add("driver: " + (e.DriverText.Length > 0 ? e.DriverText : "necunoscut"));
        parts.Add(e.Present
            ? "conectat acum" + (e.LastConnectedUtc is { } c ? " (din " + Utc(c) + ")" : "")
            : "deconectat" + (e.LastDisconnectedUtc is { } d ? " la " + Utc(d) : "") + (e.LastConnectedUtc is { } lc ? ", ultima conectare " + Utc(lc) : ""));
        parts.Add("văzut prima dată " + Utc(e.FirstSeenUtc));
        parts.Add(e.RegistrationNumber.Trim().Length > 0 ? "nr. de înregistrare " + e.RegistrationNumber.Trim() : NoRegistration);
        return string.Join(" · ", parts);
    }

    public static string AlertLine(DeviceAlertRecord a) =>
        $"{(a.Severity == DeviceAlertSeverity.Critical ? "ALERTĂ CRITICĂ" : "conectare")} {Utc(a.TimeUtc)}: {a.Device} ({DeviceMonitorRules.ClassText(a.Class)}) — {a.Text}";
}
