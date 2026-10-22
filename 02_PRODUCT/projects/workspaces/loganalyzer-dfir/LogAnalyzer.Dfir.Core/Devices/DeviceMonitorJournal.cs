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
/// gives Authenticated Users modify, so a second account can save its changes (a save replaces the file and keeps its ACL; see <see cref="Save"/>). A folder or file that already
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
    private static long _inPlaceSaves;

    /// <summary>How many saves had to rewrite the journal in place in this process (the account cannot create a temporary file in the folder).</summary>
    public static long InPlaceSaves => Interlocked.Read(ref _inPlaceSaves);

    /// <summary>Notes an in-place save (called by <see cref="Save"/>; public so a test can simulate it).</summary>
    public static void NoteInPlaceSave() => Interlocked.Increment(ref _inPlaceSaves);
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
    /// Writes the journal crash-safely: a temporary file in the same folder, flushed to disk, then <see cref="File.Replace(string, string, string?)"/> over the
    /// journal (the journal keeps its ACL, so any account the file allows can still save it; a crash leaves the old or the new file, never half of one). An account
    /// that may modify the file but not create one in the folder (a standard user: the folder gives Authenticated Users list and traverse only) cannot make the
    /// temporary file; it rewrites the journal in place, flushed, as before. Serialised inside the process. Returns null when written, otherwise the reason (the
    /// caller shows it: a journal that could not be saved is never reported as saved).
    /// </summary>
    public static string? Save(string path, DeviceJournal journal)
    {
        lock (SaveGate)
        {
            try
            {
                var full = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, LogAnalyzer.Dfir.IO.Json.Options);
                var temp = full + ".tmp-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
                bool tempWritten = false;
                try
                {
                    using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        fs.Write(bytes);
                        fs.Flush(true);
                    }
                    tempWritten = true;
                    if (File.Exists(full)) File.Replace(temp, full, null, ignoreMetadataErrors: true);
                    else File.Move(temp, full);
                    return null;
                }
                catch (UnauthorizedAccessException) when (!tempWritten && File.Exists(full))
                {
                    // cannot create a file in the folder: rewrite in place (flushed); the journal is a plain file anyway, the sealed audit is the record
                    NoteInPlaceSave();
                    using var fs = new FileStream(full, FileMode.Open, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
                    fs.Write(bytes);
                    fs.SetLength(bytes.Length);
                    fs.Flush(true);
                    return null;
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        }
    }

    /// <summary>
    /// The journal at <paramref name="path"/>, READ-ONLY (the control collector uses it: it never renames, moves or writes the journal). A missing file is an empty
    /// journal without a problem; an unreadable or damaged file gives an empty journal and the reason.
    /// </summary>
    public static DeviceJournal Load(string path, out string problem) => Load(path, out problem, out _);

    /// <summary>A row the JSON gave a null text field (a hand-edited or damaged file): the rules would throw on it.</summary>
    private static bool HasNullText(DeviceJournalEntry d) =>
        d.Identity is null || d.InstanceId is null || d.SetupClass is null || d.Name is null || d.Manufacturer is null || d.Model is null || d.VidPid is null
        || d.Serial is null || d.RegistrationNumber is null || d.VolumeLetters is null || d.VolumeLabel is null || d.VolumeSerial is null || d.DiskSignature is null
        || d.ConnectedUser is null;

    private static DeviceJournal Load(string path, out string problem, out bool damaged)
    {
        problem = "";
        damaged = false;
        try
        {
            if (!File.Exists(path)) return new DeviceJournal();
            var j = LogAnalyzer.Dfir.IO.Json.Read<DeviceJournal>(path);
            if (j is null) throw new JsonException("fișier gol");
            j.Devices ??= [];
            j.Alerts ??= [];
            j.Runs ??= [];
            j.Radios ??= [];
            j.PendingSeals ??= [];
            // review finding 3 (2026-10-22): a row with a missing (null) text field is damaged: it is left out and reported, the rest of the journal is read; the
            // rules below never see a null (they threw NullReferenceException / ArgumentNullException past this method before)
            int damagedRows = j.Devices.RemoveAll(d => d is null || HasNullText(d));
            damagedRows += j.Alerts.RemoveAll(a => a is null || a.Identity is null || a.Device is null || a.Text is null);
            damagedRows += j.Runs.RemoveAll(r => r is null || r.RunId is null);
            damagedRows += j.Radios.RemoveAll(r => r is null || r.Kind is null || r.Text is null);
            damagedRows += j.PendingSeals.RemoveAll(p => p is null || p.Event is null || p.Detail is null);
            j.Devices.RemoveAll(d => d.Identity.Length == 0);
            // the stored clone marks are not trusted: recomputed from the rows (older versions marked the nodes of one stick as clones of each other)
            DeviceJournalMerge.RecomputeDuplicateSerials(j);
            // rows of one physical device are folded for the page (display only, recomputed on every reading)
            DeviceJournalMerge.MarkSuperseded(j);
            if (damagedRows > 0)
                problem = $"Jurnalul monitorului de dispozitive este parțial deteriorat: {damagedRows} {(damagedRows == 1 ? "înregistrare cu câmpuri lipsă a fost ignorată" : "înregistrări cu câmpuri lipsă au fost ignorate")} (cauza: câmp null în fișier); restul jurnalului a fost citit.";
            return j;
        }
        catch (Exception ex) when (ex is NullReferenceException or ArgumentNullException)
        {
            // defence in depth: a field the checks above do not know of; the file is treated as damaged, never an exception for the caller
            damaged = true;
            problem = "Jurnalul monitorului de dispozitive este deteriorat (câmp lipsă: " + ex.GetType().Name + ")";
            return new DeviceJournal();
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            damaged = true;
            problem = "Jurnalul monitorului de dispozitive este deteriorat (" + ex.Message + ")";
            return new DeviceJournal();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = "Jurnalul monitorului de dispozitive nu poate fi citit: " + ex.Message;
            return new DeviceJournal();
        }
    }

    /// <summary>
    /// The journal for the RUNNING monitor (its owner): as <see cref="Load(string, out string)"/>, but a damaged file is first sealed ("device.journal_reset" with
    /// the SHA-256 of the damaged file, through <paramref name="audit"/>) and then set aside (renamed ".deteriorat-" and the time) so its content is kept and not
    /// overwritten by the next save; an empty journal starts. Check D05 shows the reset (DE VERIFICAT) instead of a tampering finding for every lost row.
    /// </summary>
    public static DeviceJournal LoadForMonitor(string path, Func<string, string, string?> audit, out string problem, Func<DeviceAuditReading?>? history = null)
    {
        var j = Load(path, out problem, out var damaged);
        if (!damaged) return j;
        string sha = "";
        try { sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem += "; amprenta lui nu a putut fi calculată: " + ex.Message; }
        var aside = path + ".deteriorat-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        if (audit(LogAnalyzer.Dfir.Auth.OperationAudit.DeviceJournalReset,
                LogAnalyzer.Dfir.Auth.OperationAudit.DeviceJournalResetDetail(Path.GetFileName(aside), sha, "jurnal deteriorat: " + problem)) is { } refused)
            problem += "; resetarea nu a putut fi trecută în jurnalul de audit (" + refused + ")";
        try
        {
            File.Move(path, aside);
            problem += $"; a fost păstrat ca „{Path.GetFileName(aside)}” (SHA-256 {sha}) și se începe un jurnal nou.";
        }
        catch (Exception mv) when (mv is IOException or UnauthorizedAccessException) { problem += "; nu a putut fi păstrat deoparte: " + mv.Message + "."; }
        // the new journal is not empty of what is known: the rows, numbers, first-seen times and alerts are rebuilt from the whole sealed audit (the reset stays sealed)
        try
        {
            if (history?.Invoke() is { ChainValid: true } reading && DeviceJournalRebuild.FromAudit(j, reading) is > 0 and var rebuilt)
                problem += $" {rebuilt} rânduri au fost refăcute din jurnalul sigilat (numere de înregistrare, prima apariție, alerte).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { problem += "; rândurile nu au putut fi refăcute din jurnalul sigilat: " + ex.Message; }
        return j;
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
            // every migrated device gets its sealed first-seen record, so check D05 can compare the migrated rows with the audit like any other
            foreach (var d in legacy.Devices.Where(DeviceScope.IsInScope))
                audit(LogAnalyzer.Dfir.Auth.OperationAudit.DeviceFirstSeen,
                    LogAnalyzer.Dfir.Auth.OperationAudit.DeviceFirstSeenDetail(d.Identity, d.Class.ToString(), d.Name, d.Serial, d.FirstSeenUtc, "migrat din jurnalul vechi"));
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
/// <summary>The register state of a device as the sealed audit proves it.</summary>
public enum RegistryState { NotRegistered, Registered, Unconfirmed }

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

    /// <summary>
    /// One device in the controller's words, with the fields of the monitor page in the same order: state, type, name, manufacturer and model, VID/PID, serial, capacity,
    /// letter and label (storage media only), user at connection, first seen, last connection and disconnection, registration number and the register state
    /// („Înregistrat” / „Neînregistrat”). Times are UTC, as everywhere in the report.
    /// </summary>
    public static string Line(DeviceJournalEntry e, DeviceAuditReading? audit = null, IEnumerable<PendingSeal>? pending = null)
    {
        var parts = new List<string>
        {
            "Stare: " + DeviceMonitorRules.StatusText(e.Status).ToLowerInvariant(),
            DeviceMonitorRules.ClassText(e.Class),
            e.Name.Length > 0 ? e.Name : "(fără nume)",
        };
        if (e.Manufacturer.Length > 0) parts.Add(e.Manufacturer);
        if (e.Model.Length > 0) parts.Add("model " + e.Model);
        if (e.VidPid.Length > 0) parts.Add("VID/PID " + e.VidPid);
        parts.Add(e.Serial.Length > 0 ? "SN " + e.Serial : DeviceMonitorRules.NoUniqueSerialText);
        if (e.DuplicateSerial) parts.Add(DeviceMonitorRules.DuplicateSerialMark.ToUpperInvariant());
        if (e.UnidentifiableMedium) parts.Add(DeviceMonitorRules.UnidentifiableMediumText);
        if (DeviceMonitorRules.IsStorageMedium(e.Class))
        {
            if (e.CapacityBytes > 0) parts.Add("capacitate " + DeviceText.CapacityRo(e.CapacityBytes));
            if (e.VolumeLetters.Length > 0) parts.Add("litera " + e.VolumeLetters);
            if (e.VolumeLabel.Length > 0) parts.Add("etichetă „" + e.VolumeLabel + "”");
        }
        parts.Add("utilizator la conectare: " + (e.ConnectedUser.Length > 0 ? e.ConnectedUser : "—"));
        parts.Add("văzut prima dată " + Utc(e.FirstSeenUtc));
        if (e.LastConnectedUtc is { } lc) parts.Add("ultima conectare " + Utc(lc));
        if (e.LastDisconnectedUtc is { } ld) parts.Add("ultima deconectare " + Utc(ld));
        bool hasNumber = e.RegistrationNumber.Trim().Length > 0;
        var state = RegistryStateOf(e.Identity, e.RegistrationNumber, audit, pending);
        parts.Add(hasNumber ? "nr. de înregistrare " + e.RegistrationNumber.Trim() : NoRegistration);
        parts.Add("registru: " + RegistryTextOf(state));
        return string.Join(" · ", parts);
    }

    public const string RegisteredText = "Înregistrat", NotRegisteredText = "Neînregistrat", UnconfirmedText = "Neconfirmat (nesigilat)";

    /// <summary>The words for a register state.</summary>
    public static string RegistryTextOf(RegistryState s) => s switch { RegistryState.Registered => RegisteredText, RegistryState.Unconfirmed => UnconfirmedText, _ => NotRegisteredText };

    /// <summary>
    /// „Registru”, from the SEALED audit and never from the journal alone (the journal is a plain file any user can edit): <c>Registered</c> only when the sealed audit
    /// (chain verified) holds the same number as the journal and no change of it is still waiting to be sealed; <c>NotRegistered</c> when neither the journal nor the
    /// sealed audit has a number; <c>Unconfirmed</c> in every other case (the numbers differ, the number is only pending, or the sealed audit is missing or does not verify).
    /// </summary>
    public static RegistryState RegistryStateOf(string identity, string journalNumber, DeviceAuditReading? audit, IEnumerable<PendingSeal>? pending = null)
    {
        var key = LogAnalyzer.Dfir.Auth.OperationAudit.DeviceField(identity);
        var inJournal = LogAnalyzer.Dfir.Auth.OperationAudit.DeviceField((journalNumber ?? "").Trim());
        bool isPending = (pending ?? []).Any(p => p.Event == LogAnalyzer.Dfir.Auth.OperationAudit.DeviceRegistrationChanged
            && Fields(p.Detail).TryGetValue("identity", out var id) && (audit?.Follow(id) ?? id).Equals(key, StringComparison.OrdinalIgnoreCase));
        if (isPending) return RegistryState.Unconfirmed;
        if (audit is null || !audit.ChainValid) return inJournal.Length == 0 && audit is null ? RegistryState.NotRegistered : RegistryState.Unconfirmed;
        var inAudit = audit.LastRegistration.TryGetValue(key, out var v) ? v : "";
        if (inAudit.Length == 0 && inJournal.Length == 0) return RegistryState.NotRegistered;
        return string.Equals(inJournal, inAudit, StringComparison.Ordinal) ? RegistryState.Registered : RegistryState.Unconfirmed;
    }

    private static Dictionary<string, string> Fields(string detail)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in detail.Split("; "))
            if (kv.IndexOf('=') is var eq and > 0) d[kv[..eq]] = kv[(eq + 1)..];
        return d;
    }

    public static string AlertLine(DeviceAlertRecord a) =>
        $"{(a.Severity == DeviceAlertSeverity.Critical ? "ALERTĂ CRITICĂ" : "conectare")} {Utc(a.TimeUtc)}: {a.Device} ({DeviceMonitorRules.ClassText(a.Class)}) — {a.Text}";
}

/// <summary>
/// Rebuilds the rows of a journal that was reset (damaged, set aside) from the WHOLE sealed audit: a registered medium keeps its registration number, its
/// first-seen time and its alerts, so it does not become „fără număr de înregistrare” and check D05 finds nothing missing. The rows are not connected (the next
/// reading marks what is plugged in); the sealed reset record stays and keeps check D05 at DE VERIFICAT.
/// </summary>
public static class DeviceJournalRebuild
{
    /// <summary>Adds a row for every device the audit sealed as first seen (class, name, serial, first-seen, registration number, last connection and disconnection, last state) and every sealed alert; returns the number of rows.</summary>
    public static int FromAudit(DeviceJournal journal, DeviceAuditReading audit)
    {
        static string K(string identity) => LogAnalyzer.Dfir.Auth.OperationAudit.DeviceField(identity);
        int rows = 0;
        foreach (var sd in audit.Devices.OrderBy(d => d.FirstSeenUtc))
        {
            if (!Enum.TryParse<DeviceClass>(sd.Class, ignoreCase: true, out var cls) || journal.Devices.Any(d => K(d.Identity).Equals(sd.Identity, StringComparison.OrdinalIgnoreCase))) continue;
            var e = new DeviceJournalEntry { Identity = sd.Identity, Class = cls, Name = sd.Device, Serial = sd.Serial, FirstSeenUtc = sd.FirstSeenUtc };
            if (audit.LastRegistration.TryGetValue(sd.Identity, out var number)) e.RegistrationNumber = number;
            var connects = audit.InfoAlerts.Concat(audit.CriticalAlerts).Where(a => a.Identity.Equals(sd.Identity, StringComparison.OrdinalIgnoreCase) && !DeviceMonitorRules.IsVolumeChangedAlert(a.Text)).Select(a => a.TimeUtc)
                .Concat(audit.Presence.Where(p => p.Present && p.Identity.Equals(sd.Identity, StringComparison.OrdinalIgnoreCase)).Select(p => p.TimeUtc)).ToList();
            if (connects.Count > 0) e.LastConnectedUtc = connects.Max();
            if (audit.Presence.Where(p => !p.Present && p.Identity.Equals(sd.Identity, StringComparison.OrdinalIgnoreCase)).Select(p => (DateTimeOffset?)p.TimeUtc).Max() is { } gone) e.LastDisconnectedUtc = gone;
            var states = audit.States.Where(x => x.Identity.Equals(sd.Identity, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.TimeUtc).ToList();
            if (states.Count > 0) e.ConfigManagerErrorCode = states[^1].Code;
            if (states.LastOrDefault(x => x.Active) is { } act) e.ActiveSeenUtc = act.TimeUtc;
            journal.Devices.Add(e);
            rows++;
        }
        foreach (var a in audit.CriticalAlerts.Select(a => (a, DeviceAlertSeverity.Critical)).Concat(audit.InfoAlerts.Select(a => (a, DeviceAlertSeverity.Info))).OrderBy(x => x.a.TimeUtc))
        {
            if (!Enum.TryParse<DeviceClass>(a.a.Class, ignoreCase: true, out var cls) || !journal.Devices.Any(d => K(d.Identity).Equals(a.a.Identity, StringComparison.OrdinalIgnoreCase))) continue;
            DeviceJournalMerge.AddAlert(journal, new DeviceAlertRecord(a.a.TimeUtc, a.a.Identity, a.a.Device, cls, a.Item2, a.a.Text));
        }
        return rows;
    }
}
