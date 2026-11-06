using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public enum ObservedMediaStatus { Authorized, Registered, Unregistered, Unauthorized, Unknown }

public static partial class Wp14Rules
{
    /// <summary>One removable storage device as the sources saw it: every row that names it, merged by normalised serial.</summary>
    internal sealed class Observed(string key)
    {
        public string Key { get; } = key;
        public string Serial { get; set; } = "";
        public string VidPid { get; set; } = "";
        public string Label { get; set; } = "";
        public string Letter { get; set; } = "";
        public List<TimelineEvent> Events { get; } = [];
        public List<string> Channels { get; } = [];
        public HashSet<string> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Volume serials of the file systems on this medium, read from the boot records logged in Partition/Diagnostic 1006 (the serial LNK files carry).</summary>
        public HashSet<string> VolumeSerials { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DateTimeOffset> Times => Events.Select(T).Where(t => t is not null).Select(t => t!.Value).ToList();
    }

    private static string LetterOf(string s)
    {
        s = s.Trim();
        return s.Length >= 1 && char.IsAsciiLetter(s[0]) && (s.Length == 1 || s[1] == ':') ? char.ToUpperInvariant(s[0]).ToString() : "";
    }

    private static Observed AddObs(Dictionary<string, Observed> map, TimelineEvent e, string rawSerial, string vidPid, string label, string letter, string channel, string user = "")
    {
        var serial = MediaRegister.NormalizeSerial(rawSerial);
        var key = serial.Length > 0 ? serial : "nosn|" + label.Trim().ToLowerInvariant();
        if (!map.TryGetValue(key, out var o)) map[key] = o = new Observed(key) { Serial = serial };
        if (o.Label.Length == 0) o.Label = label.Trim();
        if (o.VidPid.Length == 0 && vidPid.Length > 0) o.VidPid = vidPid;
        if (letter.Length > 0) o.Letter = letter;
        o.Events.Add(e);
        if (!o.Channels.Contains(channel)) o.Channels.Add(channel);
        if (user.Length > 0) o.Users.Add(user);
        return o;
    }

    /// <summary>
    /// Removable storage seen by: the SYSTEM hive (USBSTOR), Partition/Diagnostic 1006 (bus type USB/SD/MMC only), Security 6416, Kernel-PnP 400/410 and
    /// DriverFrameworks-UserMode 2003/2100/2102 (USBSTOR instance ids). Internal disks and other device classes are not media.
    /// </summary>
    internal static List<Observed> ObservedUsb(IReadOnlyList<TimelineEvent> events)
    {
        var map = new Dictionary<string, Observed>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events)
        {
            if (e.Source == "USB")
                AddObs(map, e, F(e, "Serial"), F(e, "VidPid"), F(e, "FriendlyName"), LetterOf(F(e, "DriveLetter")), "USBSTOR (SYSTEM)");
            else if (Ev(e, "Microsoft-Windows-Partition/Diagnostic", 1006))
            {
                var bus = F(e, "BusType").Trim();
                if (bus is "7" or "12" or "13" && F(e, "Capacity") is not ("0" or ""))
                {
                    var o = AddObs(map, e, F(e, "SerialNumber"), "", $"{F(e, "Manufacturer")} {F(e, "Model")}".Trim(), "", "Partition/Diagnostic 1006");
                    foreach (var v in VolumeBootRecord.FromEvent(n => F(e, n))) o.VolumeSerials.Add(v.Serial);
                }
            }
            else if (Ev(e, "Security", 6416))
            {
                var id = F(e, "DeviceId");
                var serial = UsbstorSerial(id);
                if (serial.Length == 0) continue;
                var name = F(e, "SubjectUserName");
                var user = name.Length > 0 && !PlainPhrases.IsOwnMachineOrWindows(name, e.Host) ? Account(name, F(e, "SubjectDomainName")) : "";
                AddObs(map, e, serial, "", F(e, "DeviceDescription"), "", "Security 6416", user);
            }
            else if (InChannel(e, "Kernel-PnP", 400, 410) || InChannel(e, "DriverFrameworks-UserMode", 2003, 2100, 2102))
            {
                var inst = Instance(e);
                var serial = UsbstorSerial(inst.Length > 0 ? inst : AllText(e));
                if (serial.Length == 0) continue;
                AddObs(map, e, serial, "", "", "", (e.Source.Contains("Kernel-PnP", StringComparison.OrdinalIgnoreCase) ? "Kernel-PnP " : "DriverFrameworks-UserMode ") + e.EventId);
            }
        }
        return map.Values.ToList();
    }

    /// <summary>The outcome of comparing one observed medium with the register.</summary>
    internal sealed record Verdict(ObservedMediaStatus Status, List<string> Reasons, MediaRow? Row);

    private static Verdict Compare(Ctx c, Observed o)
    {
        var reg = c.Input.Media;
        if (reg is null || !reg.IsDefined) return new(ObservedMediaStatus.Unknown, ["registrul de evidență a suporturilor nu este definit: nu există un registru cu rânduri, deci observația nu poate fi comparată (un registru gol nu dovedește autorizarea)"], null);
        if (o.Serial.Length == 0) return new(ObservedMediaStatus.Unknown, ["fără serie: suportul nu poate fi regăsit în registru"], null);
        var rows = reg.Rows.Where(r => MediaRegister.NormalizeSerial(r.Serial) == o.Serial).ToList();
        if (rows.Count == 0) return new(ObservedMediaStatus.Unregistered, [$"seria {o.Serial} nu este în registrul de evidență a suporturilor ({LogAnalyzer.Dfir.Language.RoText.Count(reg.Rows.Count, "rând", "rânduri")})"], null);
        var row = rows.FirstOrDefault(r => r.StatusValue == MediaStatus.Active) ?? rows[0];
        var times = o.Times;

        // UNAUTHORIZED: withdrawn / destroyed, or an observation outside the validity period.
        var bad = new List<string>();
        if (row.StatusValue == MediaStatus.Withdrawn) bad.Add($"mediul {row.RegistrationNumber} este retras în registru (registrul nu are data retragerii: observația poate fi anterioară retragerii)");
        if (row.StatusValue == MediaStatus.Destroyed) bad.Add($"mediul {row.RegistrationNumber} este distrus în registru (registrul nu are data distrugerii: observația poate fi anterioară)");
        var outside = times.Where(t => { var d = DateOnly.FromDateTime(t.UtcDateTime); return row.From is { } f && d < f || row.To is { } to && d > to; }).ToList();
        if (outside.Count > 0)
            bad.Add($"observație în afara perioadei de valabilitate {(row.ValidFrom.Length > 0 ? row.ValidFrom : "…")} – {(row.ValidTo.Length > 0 ? row.ValidTo : "…")} ({Time(outside.Min())})");
        if (bad.Count > 0) return new(ObservedMediaStatus.Unauthorized, bad, row);

        // REGISTERED: registered and active, but the time / user / zone / VID-PID / clearance cannot be matched.
        var why = new List<string>();
        if (times.Count == 0 && (row.From is not null || row.To is not null)) why.Add("ora observării este necunoscută, deci valabilitatea nu poate fi confirmată");
        if (row.VidPid.Length > 0 && o.VidPid.Length > 0 && RegisterRules.NormalizeVidPid(row.VidPid) is { } rv && RegisterRules.NormalizeVidPid(o.VidPid) is { } ov && rv != ov)
            why.Add($"VID/PID observat ({o.VidPid}) diferă de cel din registru ({row.VidPid})");
        if (row.AssignedUser.Length > 0)
        {
            if (o.Users.Count == 0) why.Add($"utilizatorul care a folosit mediul nu a putut fi stabilit din sursele colectate (registrul îl atribuie lui {row.AssignedUser})");
            else if (!o.Users.Any(u => RegisterRules.SameAccount(row.AssignedUser, u))) why.Add($"utilizatorul observat ({string.Join(", ", o.Users)}) diferă de utilizatorul atribuit ({row.AssignedUser})");
        }
        if (row.Zone.Length > 0)
        {
            if (c.Input.SystemZone.Length == 0) why.Add($"zona sistemului nu este declarată, deci zona mediului ({row.Zone}) nu poate fi confirmată");
            else if (!c.Input.SystemZone.Equals(row.Zone, StringComparison.OrdinalIgnoreCase)) why.Add($"zona sistemului ({c.Input.SystemZone}) diferă de zona mediului ({row.Zone})");
        }
        if (row.Level is { } need)
        {
            // Decision 24: a clearance that cannot be checked is never treated as confirmed.
            if (c.Input.Users is not { IsDefined: true } users)
                why.Add($"abilitarea pentru nivelul mediului ({need.Name}) nu poate fi verificată: registrul de utilizatori nu este definit");
            else if (o.Users.Count == 0)
                why.Add($"abilitarea pentru nivelul mediului ({need.Name}) nu poate fi verificată: utilizatorul care a folosit mediul nu a putut fi stabilit");
            else
            {
                bool Holds(string u) => users.ForAccount(u).Any(r => r.Level is { } held && SecrecyLevels.AtLeast(held, need) && ClearanceValidAt(r, times));
                if (!o.Users.Any(Holds)) why.Add($"abilitarea utilizatorului ({string.Join(", ", o.Users)}) nu acoperă nivelul mediului ({need.Name}) în registrul de utilizatori, la momentul observat");
            }
        }
        return new(why.Count > 0 ? ObservedMediaStatus.Registered : ObservedMediaStatus.Authorized, why, row);
    }

    private static bool ClearanceValidAt(UserRow r, List<DateTimeOffset> times)
    {
        DateOnly? from = RegisterRules.TryDate(r.ClearanceValidFrom, out var f0) ? f0 : null, to = RegisterRules.TryDate(r.ClearanceValidTo, out var t0) ? t0 : null;
        if (from is null && to is null) return true;
        if (times.Count == 0) return false;
        return times.All(t => { var d = DateOnly.FromDateTime(t.UtcDateTime); return (from is null || d >= from) && (to is null || d <= to); });
    }

    // ---- file activity on the volume of a medium ----

    internal sealed record Activity(TimelineEvent Event, bool Write, string What);

    internal static bool Write4663(TimelineEvent e)
    {
        var mask = F(e, "AccessMask").Trim();
        if (mask.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(mask[2..], System.Globalization.NumberStyles.HexNumber, null, out var m) && (m & 0x6) != 0) return true;
        var list = F(e, "AccessList", "Accesses");
        return list.Contains("%%4417") || list.Contains("%%4418") || list.Contains("WriteData", StringComparison.OrdinalIgnoreCase) || list.Contains("AppendData", StringComparison.OrdinalIgnoreCase) || list.Contains("AddFile", StringComparison.OrdinalIgnoreCase);
    }

    internal static readonly string[] UsnWrite = ["FILE_CREATE", "DATA_EXTEND", "DATA_OVERWRITE", "DATA_TRUNCATION", "RENAME_NEW_NAME"];

    private static List<Activity> ActivityOn(Ctx c, string letter) => ActivityOn(c.Events, letter);

    /// <summary>The traces on the volume of a drive letter (LNK / JumpList, Security 4663, USN), each marked as a write or not. Shared with the WP14b sequence rules.</summary>
    internal static List<Activity> ActivityOn(IReadOnlyList<TimelineEvent> events, string letter)
    {
        var prefix = letter + ":\\";
        var res = new List<Activity>();
        foreach (var e in events)
        {
            if (e.Source is "LNK" or "JumpList")
            {
                var path = e.Path.Length > 0 ? e.Path : F(e, "LnkTarget");
                var dt = F(e, "DriveType");
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (dt.Length == 0 || dt.Equals("REMOVABLE", StringComparison.OrdinalIgnoreCase)))
                    res.Add(new(e, false, $"{e.Source} către {path}"));
            }
            else if (Ev(e, "Security", 4663) && F(e, "ObjectName").StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                res.Add(new(e, Write4663(e), $"4663 {F(e, "ObjectName")} ({(Write4663(e) ? "acces de scriere" : "acces fără scriere")})"));
            else if (e.Source == "USN" && (e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                var reason = F(e, "Reason");
                bool w = UsnWrite.Any(x => reason.Contains(x, StringComparison.OrdinalIgnoreCase));
                res.Add(new(e, w, $"USN {e.Path} ({reason})"));
            }
        }
        return res;
    }

    // ---- the plain sentence (owner 2026-11-06: „X a băgat un stick și a copiat de acolo acolo documentul …”, no codes) ----

    /// <summary>„SanDisk Cruzer Blade USB Device” → „SanDisk Cruzer Blade”.</summary>
    internal static string PlainName(string label) => System.Text.RegularExpressions.Regex.Replace(label, @"\s+(?:USB\s+)?Device$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    /// <param name="openAccounts">Real accounts open when the stick was connected, when the traces do not say who connected it: the sentence then is a clause of its own, never „cineva”.</param>
    private static string MediaStory(Observed m, ObservedMediaStatus status, List<Activity> activity, IReadOnlyList<TimelineEvent> events, IReadOnlyList<string>? openAccounts = null)
    {
        var name = PlainName(Kind(m));
        var head = openAccounts is { Count: > 0 }
            // neutral: the accounts come from a profile that mounted it or from the sessions open then; neither proves who connected it (round-2 review)
            ? $"Stick-ul {PlainPhrases.Q(name)} a fost conectat; probele îl leagă de {(openAccounts.Count == 1 ? "contul " : "conturile ")}{PlainPhrases.List(openAccounts.Select(PlainPhrases.Q).ToList(), 5, ("cont", "conturi"))}, fără să arate sigur cine l-a conectat"
            : $"a conectat stick-ul {PlainPhrases.Q(name)}";
        var s = head + (m.Letter.Length > 0 ? $" (apărut în calculator ca unitatea {m.Letter}:)" : "");
        s += status switch
        {
            ObservedMediaStatus.Unregistered => "; acest stick NU este trecut în registrul de medii",
            ObservedMediaStatus.Unauthorized => "; acest stick NU are voie să fie folosit (în registru este retras, distrus sau cu termenul expirat)",
            ObservedMediaStatus.Registered => "; stick-ul este în registru, dar nu pentru această persoană, zonă sau perioadă",
            ObservedMediaStatus.Authorized => "; stick-ul este în registru și are voie să fie folosit",
            _ => "; nu se poate verifica în registrul de medii (stick fără serie sau registru necompletat)",
        };
        if (m.Letter.Length == 0) return s + "; nu se poate spune ce fișiere s-au folosit pe el (nu se știe ce literă a primit)";
        var files = FilesStory(name, m.Letter, activity, events);
        return s + (files.Length > 0 ? "; apoi " + files : "; nu se vede să fi copiat sau deschis fișiere de pe el");
    }

    /// <summary>The file named by a trace on the medium (LNK target, 4663 object, USN path).</summary>
    private static string PathOf(TimelineEvent e) => e.Source is "LNK" or "JumpList" ? (e.Path.Length > 0 ? e.Path : F(e, "LnkTarget")) : Ev(e, "Security", 4663) ? F(e, "ObjectName") : e.Path;

    /// <summary>
    /// What was written on the stick and what was opened from it, and where a file of the same name is on the computer. A matching name does NOT prove a copy:
    /// the sentence says „un fișier cu același nume se află și în …”; only when both sides are writes with known times, and the order fits (the stick write
    /// after the disk write: onto the stick; the disk write after the stick open: from the stick), it says „probabil copiat din/în … (același nume, scris după)”.
    /// Folder-shell files (desktop.ini, thumbs.db) are not content.
    /// </summary>
    /// <summary>The two writes of a „probabil copiat” must be this close: a file of the same name written days apart is not a copy (round-2 review).</summary>
    internal static readonly TimeSpan CopyWindow = TimeSpan.FromMinutes(30);

    internal static string FilesStory(string stick, string letter, List<Activity> activity, IReadOnlyList<TimelineEvent> events)
    {
        var prefix = letter + ":\\";
        string Name(string p) => WinPath.GetFileName(p.TrimEnd('\\'));
        bool IsWrite(TimelineEvent e) => (e.Source == "USN" && UsnWrite.Any(x => F(e, "Reason").Contains(x, StringComparison.OrdinalIgnoreCase))) || (Ev(e, "Security", 4663) && Write4663(e));
        // traces of the same file names on the computer (not on the stick): where, when, and whether the trace is a write
        var disk = events.Where(e => e.Source is "LNK" or "JumpList" or "USN" || Ev(e, "Security", 4663))
            .Select(e => (Path: PathOf(e), Time: T(e), Write: IsWrite(e)))
            .Where(t => t.Path.Length > 3 && t.Path[1] == ':' && !t.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !IsFolderShellFile(Name(t.Path)))
            .Select(t => (Name: Name(t.Path), Dir: WinPath.GetDirectoryName(t.Path) ?? "", t.Time, t.Write))
            .Where(t => t.Name.Length > 0 && t.Dir.Length > 0).ToList();
        string Same(string file, DateTimeOffset? stickTime, bool stickWrite)
        {
            var hits = disk.Where(d => d.Name.Equals(file, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) return $"„{file}”";
            if (stickTime is { } st)
            {
                if (stickWrite)
                {
                    // onto the stick: a write on the computer BEFORE the write on the stick
                    var before = hits.Where(h => h.Write && h.Time is { } t && t < st && st - t <= CopyWindow).OrderByDescending(h => h.Time).FirstOrDefault();
                    if (before.Dir is { Length: > 0 }) return $"„{file}”, probabil copiat din {before.Dir} (același nume, scris după)";
                }
                else
                {
                    // from the stick: a write on the computer AFTER the open on the stick
                    var after = hits.Where(h => h.Write && h.Time is { } t && t > st && t - st <= CopyWindow).OrderBy(h => h.Time).FirstOrDefault();
                    if (after.Dir is { Length: > 0 }) return $"„{file}”, probabil copiat în {after.Dir} (același nume, scris după)";
                }
            }
            return $"„{file}” (un fișier cu același nume se află și în {hits[0].Dir})";
        }
        var written = activity.Where(a => a.Write).Select(a => (Name: Name(PathOf(a.Event)), Time: T(a.Event))).Where(n => n.Name.Length > 0 && !IsFolderShellFile(n.Name))
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Select(g => (Name: g.Key, Time: g.Select(x => x.Time).Where(t => t is not null).OrderBy(t => t).FirstOrDefault())).ToList();
        var opened = activity.Where(a => !a.Write).Select(a => (Name: Name(PathOf(a.Event)), Time: T(a.Event))).Where(n => n.Name.Length > 0 && !IsFolderShellFile(n.Name)
                && !written.Any(w => w.Name.Equals(n.Name, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Select(g => (Name: g.Key, Time: g.Select(x => x.Time).Where(t => t is not null).OrderBy(t => t).FirstOrDefault())).ToList();
        var parts = new List<string>();
        if (written.Count > 0) parts.Add($"a scris pe stick {Files(written.Select(w => Same(w.Name, w.Time, true)).ToList())}");
        if (opened.Count > 0) parts.Add($"a deschis de pe stick {Files(opened.Select(w => Same(w.Name, w.Time, false)).ToList())}");
        return string.Join(" și ", parts);
    }

    private static string Files(List<string> names) =>
        names.Count == 1 ? $"fișierul {names[0]}" : $"fișierele {string.Join(", ", names.Take(8))}{(names.Count > 8 ? $" și încă {LogAnalyzer.Dfir.Language.RoText.Count(names.Count - 8, "fișier", "fișiere")}" : "")}";

    // ---- findings ----

    private static string Kind(Observed o) => o.Label.Length > 0 ? o.Label : "dispozitiv de stocare amovibil";

    private static void Media(Ctx c, List<Finding> o)
    {
        foreach (var m in ObservedUsb(c.Events).OrderBy(x => x.Times.DefaultIfEmpty(DateTimeOffset.MaxValue).Min()))
        {
            var v = Compare(c, m);
            var times = m.Times.OrderBy(t => t).ToList();
            var activity = m.Letter.Length > 0 ? ActivityOn(c, m.Letter) : [];
            bool write = activity.Any(a => a.Write);
            var status = v.Status;
            var sev = status switch
            {
                ObservedMediaStatus.Unauthorized => Sev(c, Severity.High),
                ObservedMediaStatus.Unregistered => Sev(c, Severity.Medium),
                ObservedMediaStatus.Registered or ObservedMediaStatus.Unknown => Sev(c, Severity.Low),
                _ => Severity.Info,
            };
            var word = status switch
            {
                ObservedMediaStatus.Authorized => "AUTORIZAT (înregistrat, activ, valabil la momentul observat; utilizatorul și zona se potrivesc sau registrul nu le cere)",
                ObservedMediaStatus.Registered => "ÎNREGISTRAT (înregistrat, dar utilizatorul, zona, valabilitatea sau VID/PID nu se potrivesc)",
                ObservedMediaStatus.Unregistered => "NEÎNREGISTRAT (seria nu este în registru)",
                ObservedMediaStatus.Unauthorized => "NEAUTORIZAT (retras / distrus sau în afara valabilității)",
                _ => "NEDETERMINAT (fără serie sau registru nedefinit)",
            };
            var regLine = v.Row is { } r ? $"Registru: {r.RegistrationNumber} (tip {r.Type}, nivel {SecrecyLevels.Describe(r.Classification)}, atribuit {(r.AssignedUser.Length > 0 ? r.AssignedUser : "nespecificat")}, zona {(r.Zone.Length > 0 ? r.Zone : "nespecificată")})."
                : c.Input.Media is { IsDefined: true } ? "Registru: niciun rând pentru această serie." : "Registru: nedefinit.";
            var copyLine = m.Letter.Length == 0
                ? "Prezență, nu copiere: litera de unitate nu este cunoscută, deci activitatea pe volum nu poate fi atribuită acestui suport."
                : activity.Count == 0
                    ? $"Prezență, nu copiere: nu există dovezi de scriere sau de acces la fișiere (LNK/JumpList, 4663, USN) pe unitatea {m.Letter}: în sursele colectate."
                    : $"Există urme de activitate pe unitatea {m.Letter}: (vezi MEDIA-FILE-ACTIVITY); ele nu stabilesc singure o copiere.";
            var clearanceNote = v.Row?.Level is not null && c.Input.Users is not { IsDefined: true } ? " Registrul de utilizatori este nedefinit: abilitarea utilizatorului nu a putut fi verificată." : "";
            var desc = $"{Kind(m)} (seria {(m.Serial.Length > 0 ? m.Serial : "necunoscută")}{(m.VidPid.Length > 0 ? ", " + m.VidPid : "")}): {LogAnalyzer.Dfir.Language.RoText.Count(m.Events.Count, "observație", "observații")} în {string.Join(", ", m.Channels)}; " +
                       $"{(times.Count > 0 ? Time(times[0]) + " – " + Time(times[^1]) : "oră necunoscută")}. Stare: {word}. " +
                       (v.Reasons.Count > 0 ? string.Join("; ", v.Reasons) + ". " : "") + regLine + clearanceNote + " " + copyLine;
            // who: the accounts the traces of the medium itself carry; when none is a real account, the accounts MEDIA-USAGE lists for the same stick
            var hostName = PlainPhrases.HostOf(c.Events);
            var realOwn = m.Users.Where(u => !PlainPhrases.IsStationAccount(u, hostName)).ToList();
            var (usageAccounts, usageDirect) = realOwn.Count == 0 ? UsageAccounts(c, m) : ([], false);
            var realUsage = usageAccounts.Where(u => !PlainPhrases.IsStationAccount(u, hostName)).ToList();
            var openAccounts = realOwn.Count == 0 && realUsage.Count > 0 && !(usageDirect && realUsage.Count == 1) ? realUsage.Select(PlainPhrases.ShortAccount).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : null;
            var users = realOwn.Count == 0 && usageDirect && realUsage.Count == 1 ? realUsage[0]
                : string.Join(", ", m.Users.Concat(openAccounts is null ? [] : usageAccounts).Distinct(StringComparer.OrdinalIgnoreCase));
            var auth = status == ObservedMediaStatus.Authorized ? AirGapAuthorization.Authorized : status == ObservedMediaStatus.Unknown ? AirGapAuthorization.Undefined : AirGapAuthorization.NotAuthorized;
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = status switch { ObservedMediaStatus.Authorized => "MEDIA-AUTHORIZED", ObservedMediaStatus.Registered => "MEDIA-REGISTERED", ObservedMediaStatus.Unregistered => "MEDIA-UNREGISTERED", ObservedMediaStatus.Unauthorized => "MEDIA-UNAUTHORIZED", _ => "MEDIA-UNKNOWN" },
                Title = $"Suport de stocare amovibil {(m.Serial.Length > 0 ? m.Serial : Kind(m))}: {status switch { ObservedMediaStatus.Authorized => "AUTORIZAT", ObservedMediaStatus.Registered => "ÎNREGISTRAT", ObservedMediaStatus.Unregistered => "NEÎNREGISTRAT", ObservedMediaStatus.Unauthorized => "NEAUTORIZAT", _ => "NEDETERMINAT" }}",
                Severity = sev, Category = c.FindingCategory, Classification = Classification.Direct, Confidence = Confidence.High,
                PlainWhat = MediaStory(m, status, activity, c.Events, openAccounts),
                // T1052.001 (exfiltration over a physical medium) only for statuses that indicate it; presence of an UNKNOWN / AUTHORIZED medium is not exfiltration.
                MitreTechniqueId = status is ObservedMediaStatus.Registered or ObservedMediaStatus.Unregistered or ObservedMediaStatus.Unauthorized ? "T1052.001" : "",
                FirstSeenUtc = times.Count > 0 ? times[0] : null, LastSeenUtc = times.Count > 0 ? times[^1] : null, User = users,
                Description = desc,
                ClassificationReason = "Dispozitivul de stocare amovibil apare în sursele colectate (USBSTOR, Partition/Diagnostic, Security 6416, Kernel-PnP, DriverFrameworks) și a fost comparat cu registrul de medii; starea vine din comparație, nu dintr-o dovadă de utilizare.",
                SemanticType = SemanticType.Presence,
                SupportingEvidence = m.Events.Take(20).Select(e => Ref(e, $"{(e.EventId.Length > 0 ? e.EventId + " " : "")}{e.Summary}".Trim())).ToList(),
                AlternativeExplanations = ["Dispozitivul poate fi un mediu legitim al unității a cărui înregistrare lipsește sau nu a fost actualizată.", "Prezența în registrul Windows nu arată dacă s-au citit sau scris fișiere."],
                AirGap = new AirGapDetail("USB", string.Join("; ", m.Channels), auth,
                    status switch
                    {
                        ObservedMediaStatus.Authorized => v.Row is { } rr ? $"registrul de medii: {rr.RegistrationNumber} activ și valabil" : "registrul de medii",
                        ObservedMediaStatus.Unknown => c.Input.Media is { IsDefined: true } ? "mediul nu are serie: nu poate fi comparat cu registrul" : "registru nedefinit: autorizarea nu poate fi stabilită",
                        _ => string.Join("; ", v.Reasons),
                    },
                    $"{Kind(m)} prezent pe stație ({LogAnalyzer.Dfir.Language.RoText.Count(m.Events.Count, "observație", "observații")})", times.Count > 0 ? times[0] : null,
                    users.Length > 0 ? users : "necunoscut: sursele colectate pentru acest mediu nu conțin contul care l-a folosit",
                    $"mediu {(m.Serial.Length > 0 ? m.Serial : "fără serie")}" + (v.Row is { } r2 ? $" ({r2.RegistrationNumber})" : ""),
                    c.CaseClassText, v.Row is { } r3 ? SecrecyLevels.Describe(r3.Classification) : "nu se aplică (mediul nu este în registru)",
                    write ? "scriere pe mediu (vezi MEDIA-FILE-ACTIVITY)" : "necunoscută: prezența mediului nu arată direcția transferului",
                    $"suport de stocare amovibil {(m.Letter.Length > 0 ? m.Letter + ":" : "(literă necunoscută)")}",
                    m.Events.Take(5).Select(e => $"{e.Source} {e.EventId} {e.Locator}".Trim()).ToList()),
            });

            if (activity.Count > 0)
            {
                bool strong = write && status is ObservedMediaStatus.Unregistered or ObservedMediaStatus.Unauthorized && c.Classified;
                var actTimes = activity.Select(a => T(a.Event)).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
                o.Add(new Finding
                {
                    FindingId = c.NextId(), RuleId = "MEDIA-FILE-ACTIVITY",
                    // who: the account of the 4663 accesses (the file trace that names the account that acted), and only when no trace carries one the medium's session users
                    User = string.Join(", ", activity.Select(a => F(a.Event, "SubjectUserName")).Where(u => u.Length > 0 && u != "-" && !PlainPhrases.IsStationAccount(u, hostName)).Distinct(StringComparer.OrdinalIgnoreCase)) is { Length: > 0 } fileUsers ? fileUsers : users,
                    PlainWhat = FilesStory(Kind(m), m.Letter, activity, c.Events) is { Length: > 0 } fs ? fs : $"a lucrat cu fișiere pe stick-ul „{PlainName(Kind(m))}”",
                    Title = $"Urme de activitate pe unitatea {m.Letter}: a mediului {(m.Serial.Length > 0 ? m.Serial : Kind(m))}",
                    Severity = strong ? Severity.High : write ? Severity.Medium : Severity.Low,
                    Category = c.FindingCategory, Classification = Classification.Correlated, Confidence = Confidence.Medium, MitreTechniqueId = "T1052.001",
                    FirstSeenUtc = actTimes.Count > 0 ? actTimes[0] : null, LastSeenUtc = actTimes.Count > 0 ? actTimes[^1] : null,
                    Description = $"{activity.Count} urme pe unitatea {m.Letter}: legată de mediul {m.Serial} prin litera de unitate din MountedDevices (ultima asociere): " +
                                  string.Join("; ", activity.Take(8).Select(a => a.What)) + ". " +
                                  (write ? "Cel puțin o urmă arată scriere (acces de scriere 4663 sau înregistrare USN de creare/extindere): scriere pe volum, nu dovada conținutului sau a unei copieri anume."
                                         : "Urmele (LNK/JumpList/4663 fără scriere) arată că fișiere de pe volum au fost referite sau accesate; nu arată scriere și nu stabilesc o copiere."),
                    ClassificationReason = "Corelație între litera de unitate a mediului și artefacte de acces la fișiere pe acea literă; litera poate fi reatribuită altor volume.",
                    SemanticType = SemanticType.Observation,
                    SupportingEvidence = activity.Take(20).Select(a => Ref(a.Event, a.What)).ToList(),
                    AlternativeExplanations = ["Litera de unitate poate fi folosită de alt volum în alt moment (MountedDevices păstrează ultima asociere).", "Un LNK sau o intrare JumpList apare și la deschiderea fișierelor, nu doar la copiere."],
                    AirGap = new AirGapDetail("USB", "LNK / JumpList / Security 4663 / USN", auth, "vezi starea mediului în constatarea MEDIA-*", $"activitate pe unitatea {m.Letter}: ({(write ? "cu scriere" : "fără scriere dovedită")})",
                        actTimes.Count > 0 ? actTimes[0] : null, users.Length > 0 ? users : "necunoscut: urmele de fișier nu conțin contul decât în 4663",
                        $"mediu {m.Serial}", c.CaseClassText, v.Row is { } r4 ? SecrecyLevels.Describe(r4.Classification) : "nu se aplică (mediul nu este în registru)",
                        write ? "scriere pe mediu" : "necunoscută: acces fără dovadă de scriere", $"suport de stocare amovibil {m.Letter}:", activity.Take(5).Select(a => a.What).ToList()),
                });
            }
        }
    }
}
