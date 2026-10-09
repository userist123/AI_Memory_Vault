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
        public List<DateTimeOffset> Times => Events.Select(T).Where(t => t is not null).Select(t => t!.Value).ToList();
    }

    private static string LetterOf(string s)
    {
        s = s.Trim();
        return s.Length >= 1 && char.IsAsciiLetter(s[0]) && (s.Length == 1 || s[1] == ':') ? char.ToUpperInvariant(s[0]).ToString() : "";
    }

    private static void AddObs(Dictionary<string, Observed> map, TimelineEvent e, string rawSerial, string vidPid, string label, string letter, string channel, string user = "")
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
                    AddObs(map, e, F(e, "SerialNumber"), "", $"{F(e, "Manufacturer")} {F(e, "Model")}".Trim(), "", "Partition/Diagnostic 1006");
            }
            else if (Ev(e, "Security", 6416))
            {
                var id = F(e, "DeviceId");
                var serial = UsbstorSerial(id);
                if (serial.Length == 0) continue;
                var name = F(e, "SubjectUserName");
                var user = name.Length > 0 && !IsMachineAccount(name) ? Account(name, F(e, "SubjectDomainName")) : "";
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
        if (reg is null || !reg.IsDefined) return new(ObservedMediaStatus.Unknown, ["registru nedefinit: nu există un registru de medii cu rânduri, deci observația nu poate fi comparată (un registru gol nu dovedește autorizarea)"], null);
        if (o.Serial.Length == 0) return new(ObservedMediaStatus.Unknown, ["fără serie: mediul nu poate fi regăsit în registru"], null);
        var rows = reg.Rows.Where(r => MediaRegister.NormalizeSerial(r.Serial) == o.Serial).ToList();
        if (rows.Count == 0) return new(ObservedMediaStatus.Unregistered, [$"seria {o.Serial} nu este în registrul de medii ({reg.Rows.Count} rânduri)"], null);
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

    private sealed record Activity(TimelineEvent Event, bool Write, string What);

    private static bool Write4663(TimelineEvent e)
    {
        var mask = F(e, "AccessMask").Trim();
        if (mask.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(mask[2..], System.Globalization.NumberStyles.HexNumber, null, out var m) && (m & 0x6) != 0) return true;
        var list = F(e, "AccessList", "Accesses");
        return list.Contains("%%4417") || list.Contains("%%4418") || list.Contains("WriteData", StringComparison.OrdinalIgnoreCase) || list.Contains("AppendData", StringComparison.OrdinalIgnoreCase) || list.Contains("AddFile", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] UsnWrite = ["FILE_CREATE", "DATA_EXTEND", "DATA_OVERWRITE", "DATA_TRUNCATION", "RENAME_NEW_NAME"];

    private static List<Activity> ActivityOn(Ctx c, string letter)
    {
        var prefix = letter + ":\\";
        var res = new List<Activity>();
        foreach (var e in c.Events)
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
                ObservedMediaStatus.Authorized => "AUTHORIZED (înregistrat, activ, valabil la momentul observat; utilizatorul și zona se potrivesc sau registrul nu le cere)",
                ObservedMediaStatus.Registered => "REGISTERED (înregistrat, dar utilizatorul, zona, valabilitatea sau VID/PID nu se potrivesc)",
                ObservedMediaStatus.Unregistered => "UNREGISTERED (seria nu este în registru)",
                ObservedMediaStatus.Unauthorized => "UNAUTHORIZED (retras / distrus sau în afara valabilității)",
                _ => "UNKNOWN (fără serie sau registru nedefinit)",
            };
            var regLine = v.Row is { } r ? $"Registru: {r.RegistrationNumber} (tip {r.Type}, nivel {SecrecyLevels.Describe(r.Classification)}, atribuit {(r.AssignedUser.Length > 0 ? r.AssignedUser : "nespecificat")}, zona {(r.Zone.Length > 0 ? r.Zone : "nespecificată")})."
                : c.Input.Media is { IsDefined: true } ? "Registru: niciun rând pentru această serie." : "Registru: nedefinit.";
            var copyLine = m.Letter.Length == 0
                ? "Prezență, nu copiere: litera de unitate nu este cunoscută, deci activitatea pe volum nu poate fi atribuită acestui mediu; prezență, nu copiere."
                : activity.Count == 0
                    ? $"Prezența mediului nu este o copiere: prezență, nu copiere — nu există dovezi de scriere sau de acces la fișiere (LNK/JumpList, 4663, USN) pe unitatea {m.Letter}: în sursele colectate."
                    : $"Există urme de activitate pe unitatea {m.Letter}: (vezi MEDIA-FILE-ACTIVITY); ele nu stabilesc singure o copiere.";
            var clearanceNote = v.Row?.Level is not null && c.Input.Users is not { IsDefined: true } ? " Registrul de utilizatori este nedefinit: abilitarea utilizatorului nu a putut fi verificată." : "";
            var desc = $"{Kind(m)} (seria {(m.Serial.Length > 0 ? m.Serial : "necunoscută")}{(m.VidPid.Length > 0 ? ", " + m.VidPid : "")}): {m.Events.Count} observații în {string.Join(", ", m.Channels)}; " +
                       $"{(times.Count > 0 ? Time(times[0]) + " – " + Time(times[^1]) : "oră necunoscută")}. Stare: {word}. " +
                       (v.Reasons.Count > 0 ? string.Join("; ", v.Reasons) + ". " : "") + regLine + clearanceNote + " " + copyLine;
            var users = m.Users.Count > 0 ? string.Join(", ", m.Users) : "";
            var auth = status == ObservedMediaStatus.Authorized ? AirGapAuthorization.Authorized : status == ObservedMediaStatus.Unknown ? AirGapAuthorization.Undefined : AirGapAuthorization.NotAuthorized;
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = status switch { ObservedMediaStatus.Authorized => "MEDIA-AUTHORIZED", ObservedMediaStatus.Registered => "MEDIA-REGISTERED", ObservedMediaStatus.Unregistered => "MEDIA-UNREGISTERED", ObservedMediaStatus.Unauthorized => "MEDIA-UNAUTHORIZED", _ => "MEDIA-UNKNOWN" },
                Title = $"Mediu amovibil {(m.Serial.Length > 0 ? m.Serial : Kind(m))}: {status.ToString().ToUpperInvariant()}",
                Severity = sev, Category = c.FindingCategory, Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1052.001",
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
                    $"{Kind(m)} prezent pe stație ({m.Events.Count} observații)", times.Count > 0 ? times[0] : null,
                    users.Length > 0 ? users : "necunoscut: sursele colectate pentru acest mediu nu conțin contul care l-a folosit",
                    $"mediu {(m.Serial.Length > 0 ? m.Serial : "fără serie")}" + (v.Row is { } r2 ? $" ({r2.RegistrationNumber})" : ""),
                    c.CaseClassText, v.Row is { } r3 ? SecrecyLevels.Describe(r3.Classification) : "nu se aplică (mediul nu este în registru)",
                    write ? "scriere pe mediu (vezi MEDIA-FILE-ACTIVITY)" : "necunoscută: prezența mediului nu arată direcția transferului",
                    $"mediu amovibil {(m.Letter.Length > 0 ? m.Letter + ":" : "(literă necunoscută)")}",
                    m.Events.Take(5).Select(e => $"{e.Source} {e.EventId} {e.Locator}".Trim()).ToList()),
            });

            if (activity.Count > 0)
            {
                bool strong = write && status is ObservedMediaStatus.Unregistered or ObservedMediaStatus.Unauthorized && c.Classified;
                var actTimes = activity.Select(a => T(a.Event)).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
                o.Add(new Finding
                {
                    FindingId = c.NextId(), RuleId = "MEDIA-FILE-ACTIVITY",
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
                        write ? "scriere pe mediu" : "necunoscută: acces fără dovadă de scriere", $"mediu amovibil {m.Letter}:", activity.Take(5).Select(a => a.What).ToList()),
                });
            }
        }
    }
}
