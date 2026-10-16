using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>Whether this process runs the monitor: the first instance on the machine does, a second one only shows the journal, and a failure to even ask is its own state.</summary>
public enum MonitorOwnership { Owner, OtherInstance, Failed }

/// <summary>The outcome of <see cref="DeviceJournalStore.TryOwnMonitor"/>; <c>Reason</c> is the exception text when the state is <see cref="MonitorOwnership.Failed"/>.</summary>
public sealed record OwnResult(MonitorOwnership State, string Reason = "");

/// <summary>The result of preparing the journal folder: <c>Trusted</c> is false when the folder or the file belongs to an account other than SYSTEM or Administrators.</summary>
public sealed record JournalFolderState(bool Trusted, string Warning);

/// <summary>
/// Where the device monitor's journal lives and how it is read and written. The journal is machine-wide (%ProgramData%\LogAnalyzer\device_monitor): a per-user
/// folder would give every Windows account its own journal, and the control run by one account would not see what another account's monitor recorded.
/// <para>
/// The folder is created with an explicit ACL: SYSTEM and Administrators have full control, Authenticated Users may only list and traverse it. The journal FILE
/// gives Authenticated Users modify, so a second account can save its changes (the file is rewritten in place, so its ACL stays). A folder or file that already
/// exists with an owner other than SYSTEM or Administrators is not trusted: the journal is shown with a warning and the sealed audit wins (check D05 compares
/// the registration numbers and the monitor's running windows with it).
/// </para>
/// </summary>
public static class DeviceJournalStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "device_monitor", "device_monitor.json");

    /// <summary>The per-user journal of the first version of the monitor (moved once to <see cref="DefaultPath"/>).</summary>
    public static string LegacyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogAnalyzer", "device_monitor.json");

    private static readonly object SaveGate = new();
    private static Mutex? _ownerMutex;

    /// <summary>
    /// Only one application instance on the machine runs the monitor (writes the journal, its running windows and the audit entries): the first to ask owns it
    /// for the life of the process; a second instance shows the journal without writing it. When the mutex cannot even be created the state is
    /// <see cref="MonitorOwnership.Failed"/> with the reason: the caller shows „monitor nepornit” and logs it, the monitor never silently runs nowhere.
    /// </summary>
    public static OwnResult TryOwnMonitor()
    {
        lock (SaveGate)
        {
            if (_ownerMutex is not null) return new(MonitorOwnership.Owner);
            try
            {
                var m = new Mutex(true, @"Global\LogAnalyzer.DeviceMonitor", out var created);
                if (!created) { m.Dispose(); return new(MonitorOwnership.OtherInstance); }
                _ownerMutex = m;
                return new(MonitorOwnership.Owner);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException or ArgumentException or NotSupportedException)
            {
                return new(MonitorOwnership.Failed, ex.Message);
            }
        }
    }

    /// <summary>The SIDs whose ownership of the journal folder and file is trusted: SYSTEM, Administrators, TrustedInstaller.</summary>
    public static bool IsTrustedOwnerSid(string sid) =>
        sid is "S-1-5-18" or "S-1-5-32-544" || sid.StartsWith("S-1-5-80-956008885-", StringComparison.Ordinal);

    /// <summary>
    /// Creates the journal folder with its ACL (and an empty journal file that Authenticated Users may modify) when they do not exist; checks the owner when they
    /// do. Never throws: a problem comes back as an untrusted state with the reason.
    /// </summary>
    public static JournalFolderState EnsureSecure(string path, bool create = true)
    {
        if (!OperatingSystem.IsWindows()) return new(true, "");
        try { return EnsureSecureWindows(path, create); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PrivilegeNotHeldException or SystemException)
        {
            return new(false, "Folderul jurnalului monitorului nu a putut fi pregătit (" + ex.Message + "); jurnalul nu este de încredere, contează jurnalul sigilat.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static JournalFolderState EnsureSecureWindows(string path, bool create)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full)!;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var authenticated = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        string warning = "";
        if (!Directory.Exists(folder))
        {
            if (!create) return new(true, "");
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            // list and traverse only, on the folder itself (not inherited by the journal file)
            sec.AddAccessRule(new FileSystemAccessRule(authenticated, FileSystemRights.ReadAndExecute, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            // An elevated process (the folder is then owned by Administrators) needs no rule of its own. A process that is not elevated cannot make Administrators the
            // owner and would lock itself out of the protected folder, so it gets an explicit rule for itself; the folder is then owned by that account, which the
            // owner rule below reports as untrusted (the sealed audit wins).
            using var me = WindowsIdentity.GetCurrent();
            bool elevated = new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator);
            if (elevated)
            {
                try { sec.SetOwner(admins); } catch (Exception ex) when (ex is InvalidOperationException or IdentityNotMappedException or UnauthorizedAccessException) { }
            }
            else if (me.User is { } self)
                sec.AddAccessRule(new FileSystemAccessRule(self, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(folder).Create(sec);
        }
        var ownerSid = new DirectoryInfo(folder).GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (ownerSid is null || !IsTrustedOwnerSid(ownerSid.Value))
            warning = "Folderul jurnalului monitorului aparține altui cont decât SYSTEM sau Administrators (" + (ownerSid?.Value ?? "necunoscut") + "); jurnalul nu este de încredere, contează jurnalul sigilat.";
        if (!File.Exists(full))
        {
            if (!create) return new(warning.Length == 0, warning);
            try
            {
                File.WriteAllText(full, JsonSerializer.Serialize(new DeviceJournal(), LogAnalyzer.Dfir.IO.Json.Options));
                var fi = new FileInfo(full);
                var fsec = fi.GetAccessControl();
                fsec.AddAccessRule(new FileSystemAccessRule(authenticated, FileSystemRights.Modify, AccessControlType.Allow));
                fi.SetAccessControl(fsec);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a non-owner cannot create it: the first save reports the reason */ }
        }
        else
        {
            var fileOwner = new FileInfo(full).GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (fileOwner is null || !IsTrustedOwnerSid(fileOwner.Value))
                warning = (warning.Length > 0 ? warning + " " : "") + "Fișierul jurnalului monitorului aparține altui cont decât SYSTEM sau Administrators (" + (fileOwner?.Value ?? "necunoscut") + "); jurnalul nu este de încredere, contează jurnalul sigilat.";
        }
        return new(warning.Length == 0, warning);
    }

    /// <summary>
    /// Writes the journal in place (the file keeps its ACL, so any account the file allows can save it). Serialised inside the process. Returns null when
    /// written, otherwise the reason (the caller shows it: a journal that could not be saved is never reported as saved).
    /// </summary>
    public static string? Save(string path, DeviceJournal journal)
    {
        lock (SaveGate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(journal, LogAnalyzer.Dfir.IO.Json.Options));
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        }
    }

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
            j.Runs ??= [];
            j.Radios ??= [];
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

    /// <summary>
    /// The one-time move of the old per-user journal (<see cref="LegacyPath"/>) to <paramref name="newPath"/>: only when the new journal does not exist yet. The
    /// migration is sealed first (<paramref name="audit"/> writes "device.journal_migrated" with the number of devices and the SHA-256 of the old file); when the
    /// seal cannot be written nothing is moved and the reason is returned. The old file is renamed ".migrat" afterwards. Returns null when nothing had to be done
    /// or the move succeeded.
    /// </summary>
    public static string? MigrateLegacy(string legacyPath, string newPath, Func<string, string, string?> audit)
    {
        try
        {
            if (!File.Exists(legacyPath) || File.Exists(newPath) && new FileInfo(newPath).Length > 0 && Load(newPath, out _).Devices.Count > 0) return null;
            var legacy = Load(legacyPath, out var problem);
            if (problem.Length > 0) return "Jurnalul vechi al monitorului nu a putut fi mutat: " + problem;
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacyPath))).ToLowerInvariant();
            if (audit(LogAnalyzer.Dfir.Auth.OperationAudit.DeviceJournalMigrated,
                    LogAnalyzer.Dfir.Auth.OperationAudit.DeviceJournalMigratedDetail(@"%APPDATA%\LogAnalyzer\device_monitor.json", legacy.Devices.Count, sha)) is { } refused)
                return "Jurnalul vechi al monitorului nu a fost mutat: mutarea nu a putut fi trecută în jurnalul de audit (" + refused + ").";
            if (Save(newPath, legacy) is { } saveError) return "Jurnalul vechi al monitorului nu a putut fi mutat: " + saveError;
            try { File.Move(legacyPath, legacyPath + ".migrat", overwrite: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Jurnalul vechi al monitorului nu a putut fi citit: " + ex.Message; }
    }
}

/// <summary>What the control report says about the monitor's journal for one period: only the devices in scope (<see cref="DeviceScope"/>).</summary>
public sealed record DeviceMonitorSummary(IReadOnlyList<DeviceJournalEntry> Devices, IReadOnlyList<DeviceAlertRecord> Alerts, IReadOnlyList<DeviceJournalEntry> Offending)
{
    public int Critical => Alerts.Count(a => a.Severity == DeviceAlertSeverity.Critical);
}

/// <summary>The device section of the station control report (check D05 and the report section): pure, from the journal.</summary>
public static class DeviceMonitorReport
{
    public const string NoRegistration = "fără număr de înregistrare";

    /// <summary>
    /// Every in-scope device the monitor saw in the period (present now, connected, disconnected or first seen since <paramref name="fromUtc"/>), the alerts
    /// raised in it, and the devices that break the policy: a storage medium without a registration number, or a class forbidden on an isolated station.
    /// Order: storage media first, then the other classes, by name.
    /// </summary>
    public static DeviceMonitorSummary Build(DeviceJournal journal, DateTimeOffset fromUtc, DateTimeOffset toUtc, bool isolated)
    {
        bool InPeriod(DeviceJournalEntry e) => e.FirstSeenUtc <= toUtc &&
            (e.Present || e.FirstSeenUtc >= fromUtc || e.LastConnectedUtc >= fromUtc || e.LastDisconnectedUtc >= fromUtc);
        var devices = journal.Devices.Where(DeviceScope.IsInScope).Where(InPeriod)
            .OrderBy(e => DeviceScope.SortOrder(e.Class)).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var inScope = new HashSet<string>(devices.Select(d => d.Identity), StringComparer.OrdinalIgnoreCase);
        var alerts = journal.Alerts.Where(a => a.TimeUtc >= fromUtc && a.TimeUtc <= toUtc && (DeviceScope.IsInScope(a.Class) || inScope.Contains(a.Identity))).OrderBy(a => a.TimeUtc).ToList();
        var offending = devices.Where(e => Offends(e, isolated, fromUtc)).ToList();
        return new DeviceMonitorSummary(devices, alerts, offending);
    }

    public static bool Offends(DeviceJournalEntry e, bool isolated, DateTimeOffset? fromUtc = null) =>
        (DeviceMonitorRules.IsStorageMedium(e.Class) && e.RegistrationNumber.Trim().Length == 0) || ForbiddenSeen(e, isolated, fromUtc);

    /// <summary>
    /// A class the policy forbids on an isolated station, active in the period. A Wi-Fi, Bluetooth or network adapter that is disabled (problem code 22) is what
    /// the policy asks for and does not count, unless it was seen enabled since <paramref name="fromUtc"/>.
    /// </summary>
    public static bool ForbiddenSeen(DeviceJournalEntry e, bool isolated, DateTimeOffset? fromUtc = null) =>
        isolated && DeviceMonitorRules.ForbiddenOnIsolated(e.Class, e.InstanceId)
        && !(DeviceMonitorRules.IsDisabledRadio(e.Class, e.ConfigManagerErrorCode) && !(e.ActiveSeenUtc is { } a && a >= (fromUtc ?? DateTimeOffset.MinValue)));

    public static string UtcText(DateTimeOffset t) => Utc(t);

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>One device in the controller's words: type, name, ids, medium, state, user at connection, times and its registration number.</summary>
    public static string Line(DeviceJournalEntry e)
    {
        var parts = new List<string> { DeviceMonitorRules.ClassText(e.Class), e.Name.Length > 0 ? e.Name : "(fără nume)" };
        if (e.Manufacturer.Length > 0) parts.Add(e.Manufacturer);
        if (e.Model.Length > 0) parts.Add("model " + e.Model);
        if (e.VidPid.Length > 0) parts.Add("VID/PID " + e.VidPid);
        parts.Add(e.Serial.Length > 0 ? "SN " + e.Serial : DeviceMonitorRules.NoUniqueSerialText);
        if (e.DuplicateSerial) parts.Add(DeviceMonitorRules.DuplicateSerialMark.ToUpperInvariant());
        if (e.CapacityBytes > 0) parts.Add("capacitate " + DeviceText.Bytes(e.CapacityBytes));
        if (e.VolumeLetters.Length > 0) parts.Add("litera " + e.VolumeLetters);
        if (e.VolumeLabel.Length > 0) parts.Add("etichetă „" + e.VolumeLabel + "”");
        parts.Add(DeviceMonitorRules.StatusText(e.Status).ToLowerInvariant() + (e.Present
            ? (e.LastConnectedUtc is { } c ? " (din " + Utc(c) + ")" : "")
            : (e.LastDisconnectedUtc is { } d ? " la " + Utc(d) : "") + (e.LastConnectedUtc is { } lc ? ", ultima conectare " + Utc(lc) : "")));
        parts.Add("utilizator la conectare: " + (e.ConnectedUser.Length > 0 ? e.ConnectedUser : "—"));
        parts.Add("văzut prima dată " + Utc(e.FirstSeenUtc));
        parts.Add(e.RegistrationNumber.Trim().Length > 0 ? "nr. de înregistrare " + e.RegistrationNumber.Trim() : NoRegistration);
        return string.Join(" · ", parts);
    }

    public static string AlertLine(DeviceAlertRecord a) =>
        $"{(a.Severity == DeviceAlertSeverity.Critical ? "ALERTĂ CRITICĂ" : "conectare")} {Utc(a.TimeUtc)}: {a.Device} ({DeviceMonitorRules.ClassText(a.Class)}) — {a.Text}";
}
