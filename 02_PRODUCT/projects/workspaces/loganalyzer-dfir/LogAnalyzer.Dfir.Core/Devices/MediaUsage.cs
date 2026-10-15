using System.Globalization;

namespace LogAnalyzer.Dfir.Devices;

/// <summary>
/// How sure the record is about who used a medium at one connection. Ordered from the strongest attribution to none; ambiguity is never collapsed into one name.
/// </summary>
public enum UsageStatus
{
    /// <summary>Exactly one user profile mounted this medium's volume (NTUSER MountPoints2). Says who mounted it, not the exact minute of this connection.</summary>
    Direct,
    /// <summary>No profile mounted the volume, but exactly one interactive logon session was open when the medium arrived. An inference, not a proof.</summary>
    Inferred,
    /// <summary>Several profiles mounted the volume, or several interactive sessions were open. Every candidate is listed.</summary>
    Ambiguous,
    /// <summary>No data links any account to this connection.</summary>
    NotAttributable,
}

/// <summary>MountPoints2 of one user profile: the profile mounted this volume GUID; <paramref name="LastWriteUtc"/> is the key's last write (its last use).</summary>
public sealed record VolumeMount(string Account, string VolumeGuid, DateTimeOffset? LastWriteUtc);

/// <summary>An interactive logon session (Security 4624 until 4634/4647). <paramref name="LogoffUtc"/> is null when no end was seen.</summary>
public sealed record LogonSession(string Account, string LogonType, DateTimeOffset LogonUtc, DateTimeOffset? LogoffUtc);

/// <summary>One logon or logoff record before pairing.</summary>
public sealed record LogonRecord(string LogonId, string Account, string LogonType, DateTimeOffset TimeUtc, bool IsLogon);

/// <summary>One arrival of a medium and the time it left, when known. Null <paramref name="DisconnectUtc"/> = no removal was recorded after this arrival.</summary>
public sealed record MediumConnection(DateTimeOffset ConnectUtc, DateTimeOffset? DisconnectUtc);

/// <summary>The attribution of one connection, with the Romanian reason shown to the controller.</summary>
public sealed record ConnectionUsage(MediumConnection Connection, UsageStatus Status, IReadOnlyList<string> Accounts, string Reason);

/// <summary>A file reference (LNK or Jump List entry) with the volume it points to, when the file has its own timestamp.</summary>
public sealed record LinkedFile<T>(T Item, string Path, string VolumeSerial, string DriveType, DateTimeOffset? WhenUtc);

public enum FileLinkKind
{
    /// <summary>The link names the medium's volume serial and was written while the medium was connected: the file was opened from this medium.</summary>
    OpenedFromMedium,
    /// <summary>Same drive letter while connected, but the serial does not match or is missing. A hint only.</summary>
    SameLetterHint,
}

public sealed record FileLink<T>(T Item, string Path, FileLinkKind Kind, DateTimeOffset WhenUtc, MediumConnection Connection, string Reason);

/// <summary>
/// Who used a medium (WP14 usage). Pure rules over already-parsed facts; no registry or event log access here. Ambiguity is always reported;
/// a missing record is "not attributable", never a guess.
/// </summary>
public static class MediaUsageRules
{
    /// <summary>Interactive logon types: 2 = console, 10 = remote interactive (RDP), 11 = cached interactive. Service, batch and network logons are not people at the keyboard.</summary>
    public static readonly string[] InteractiveLogonTypes = ["2", "10", "11"];

    /// <summary>
    /// Pairs logon and logoff records by logon id. A logoff pairs with the latest logon of the same id that precedes it.
    /// </summary>
    public static IReadOnlyList<LogonSession> PairSessions(IEnumerable<LogonRecord> records)
    {
        var sessions = new List<LogonSession>();
        var open = new Dictionary<string, Stack<LogonRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records.OrderBy(r => r.TimeUtc))
        {
            if (r.LogonId.Length == 0) continue;
            if (!open.TryGetValue(r.LogonId, out var stack)) open[r.LogonId] = stack = new Stack<LogonRecord>();
            if (r.IsLogon) { stack.Push(r); continue; }
            if (stack.Count == 0) continue;
            var on = stack.Pop();
            sessions.Add(new LogonSession(on.Account, on.LogonType, on.TimeUtc, r.TimeUtc));
        }
        foreach (var stack in open.Values)
            foreach (var on in stack)
                sessions.Add(new LogonSession(on.Account, on.LogonType, on.TimeUtc, null));
        return sessions.OrderBy(s => s.LogonUtc).ToList();
    }

    /// <summary>
    /// Attribution of every connection of one medium. <paramref name="volumeToSerial"/> maps a volume GUID (with braces) to the normalised serial of the disk it
    /// belongs to (MountedDevices). Connections are judged in time order.
    /// </summary>
    public static IReadOnlyList<ConnectionUsage> Attribute(string serial, IReadOnlyList<MediumConnection> connections,
        IReadOnlyDictionary<string, string> volumeToSerial, IReadOnlyList<VolumeMount> mounts, IReadOnlyList<LogonSession> sessions)
    {
        var volumes = volumeToSerial.Where(kv => kv.Value.Equals(serial, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mountedBy = mounts.Where(m => volumes.Contains(m.VolumeGuid)).ToList();
        var mountUsers = mountedBy.Select(m => m.Account).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        var lastUse = mountedBy.Where(m => m.LastWriteUtc is not null).OrderByDescending(m => m.LastWriteUtc).FirstOrDefault();

        var result = new List<ConnectionUsage>();
        foreach (var c in connections.OrderBy(c => c.ConnectUtc))
        {
            if (mountUsers.Count == 1)
            {
                var who = mountUsers[0];
                var when = lastUse?.LastWriteUtc is { } w ? $" Ultima folosire a volumului în acest profil: {Fmt(w)}." : "";
                result.Add(new(c, UsageStatus.Direct, mountUsers,
                    $"Singurul profil de utilizator care a montat acest volum este „{who}” (registrul profilului, MountPoints2).{when} " +
                    "Registrul nu arată ora conectării, deci nu dovedește că persoana era la stație în acel moment."));
                continue;
            }
            if (mountUsers.Count > 1)
            {
                result.Add(new(c, UsageStatus.Ambiguous, mountUsers,
                    $"Mai multe profile de utilizator au montat acest volum: {string.Join(", ", mountUsers.Select(u => $"„{u}”"))}. " +
                    "Nu se poate stabili care a conectat stick-ul; toate conturile sunt trecute mai jos."));
                continue;
            }

            var open = sessions.Where(s => InteractiveLogonTypes.Contains(s.LogonType) && s.LogonUtc <= c.ConnectUtc && (s.LogoffUtc is null || s.LogoffUtc >= c.ConnectUtc))
                               .Select(s => s.Account).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            if (open.Count == 1)
            {
                result.Add(new(c, UsageStatus.Inferred, open,
                    $"Nu există niciun profil care să fi montat volumul, dar doar contul „{open[0]}” avea o sesiune interactivă deschisă la conectare. " +
                    "Este o presupunere probabilă, nu o dovadă."));
            }
            else if (open.Count > 1)
            {
                result.Add(new(c, UsageStatus.Ambiguous, open,
                    $"Nu există profil care să fi montat volumul și mai multe conturi aveau sesiune interactivă deschisă la conectare: {string.Join(", ", open.Select(u => $"„{u}”"))}. " +
                    "Nu se poate stabili cine a conectat stick-ul."));
            }
            else
            {
                result.Add(new(c, UsageStatus.NotAttributable, [],
                    volumes.Count == 0
                        ? "Windows nu a păstrat legătura dintre volumul stick-ului și seria lui (MountedDevices) și nu există sesiune interactivă deschisă la conectare. Nu există date pentru atribuire."
                        : "Nu există profil care să fi montat volumul și nicio sesiune interactivă deschisă la conectare. Nu există date pentru atribuire."));
            }
        }
        return result;
    }

    /// <summary>
    /// Files that name this medium. Strong: the link's volume serial equals the medium's volume serial and the file was written while the medium was connected.
    /// Hint: same drive letter while connected but no matching serial. Files written outside every connection window are not linked at all.
    /// Only removable (or unknown) drive types count. A window runs from an arrival to its recorded removal, or to the next arrival of the same medium.
    /// </summary>
    public static IReadOnlyList<FileLink<T>> Link<T>(string volumeSerial, string driveLetter, IReadOnlyList<MediumConnection> connections, IEnumerable<LinkedFile<T>> files) =>
        Link(volumeSerial.Trim().Length > 0 ? [volumeSerial.Trim()] : [], driveLetter, connections, files);

    /// <summary>
    /// Same, for a medium with several known volume serials (Partition/Diagnostic 1006 logs the boot record of every volume, and a reformat changes the serial).
    /// A link names the medium when its serial equals any of them.
    /// </summary>
    public static IReadOnlyList<FileLink<T>> Link<T>(IReadOnlyCollection<string> volumeSerials, string driveLetter, IReadOnlyList<MediumConnection> connections, IEnumerable<LinkedFile<T>> files)
    {
        var ordered = connections.OrderBy(c => c.ConnectUtc).ToList();
        var result = new List<FileLink<T>>();
        var letter = NormalizeLetter(driveLetter);
        var serials = volumeSerials.Select(v => v.Trim()).Where(v => v.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (f.WhenUtc is not { } when) continue;
            if (f.DriveType.Length > 0 && !f.DriveType.Equals("REMOVABLE", StringComparison.OrdinalIgnoreCase)) continue;
            int idx = WindowOf(ordered, when);
            if (idx < 0) continue;   // written before the first arrival or after the removal: not this medium's time
            var conn = ordered[idx];
            var openEnded = ConnectionEnd(ordered, idx) is null;
            var timeNote = openEnded ? " Ora deconectării nu este cunoscută: fișierul ar putea fi scris și după ce stick-ul a fost scos." : "";
            var fileLetter = NormalizeLetter(f.Path.Length >= 2 && f.Path[1] == ':' ? f.Path[..1] : "");
            if (f.VolumeSerial.Length > 0 && serials.Contains(f.VolumeSerial.Trim()))
            {
                result.Add(new(f.Item, f.Path, FileLinkKind.OpenedFromMedium, when, conn,
                    $"Scurtătura indică volumul cu seria {f.VolumeSerial}, aceeași cu a suportului, și a fost scrisă cât timp suportul era conectat: fișierul a fost deschis de pe acest suport.{timeNote}"));
            }
            else if (letter.Length > 0 && fileLetter == letter)
            {
                var why = f.VolumeSerial.Length == 0 ? "scurtătura nu are serie de volum" : $"seria volumului din scurtătură ({f.VolumeSerial}) nu este a suportului";
                result.Add(new(f.Item, f.Path, FileLinkKind.SameLetterHint, when, conn,
                    $"Doar aceeași literă de unitate ({letter}:) în timpul conectării; {why}. Este un indiciu, nu o dovadă că fișierul este de pe acest suport.{timeNote}"));
            }
        }
        return result;
    }

    /// <summary>End of a window: the recorded removal, else the next arrival of the same medium, else null (open-ended).</summary>
    private static DateTimeOffset? ConnectionEnd(IReadOnlyList<MediumConnection> ordered, int i) =>
        ordered[i].DisconnectUtc ?? (i + 1 < ordered.Count ? ordered[i + 1].ConnectUtc : null);

    /// <summary>Index of the connection whose window contains <paramref name="when"/>, or -1.</summary>
    private static int WindowOf(IReadOnlyList<MediumConnection> ordered, DateTimeOffset when)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].ConnectUtc > when) break;
            var end = ConnectionEnd(ordered, i);
            if (end is null || when <= end) return i;
        }
        return -1;
    }

    private static string NormalizeLetter(string s)
    {
        s = s.Trim().TrimEnd(':');
        return s.Length == 1 && char.IsAsciiLetter(s[0]) ? char.ToUpperInvariant(s[0]).ToString() : "";
    }

    private static string Fmt(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
