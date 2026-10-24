using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp14Rules
{
    // ---- D7: writer capability, NoCDBurning, the Windows burn staging folder, burn tool logs ----

    private static bool PeriodOrUnset(Ctx c, DateTimeOffset? t) => c.Scope.PeriodFromUtc is null && c.Scope.PeriodToUtc is null || t is null || c.InPeriod(t);

    /// <summary>
    /// AIRGAP-OPTICAL-WRITE-UNBLOCKED (a drive that can write discs while writing is not denied by the removable-storage policy; the Explorer-only block NoCDBurning
    /// is named as insufficient), MEDIA-OPTICAL-STAGING (files waiting in the Windows burn staging folder) and MEDIA-OPTICAL-TOOL-LOG (a burn tool's own log).
    /// </summary>
    private static void OpticalFacts(Ctx c, List<Finding> o)
    {
        var b = c.Facts?.Burn;
        if (b is null) return;
        var writers = b.Writers.Where(w => w.CanWrite).ToList();
        if (writers.Count > 0 && b.CdDvdDenyWrite != true)
        {
            bool explorerOnly = b.NoCdBurningMachine == 1 || b.NoCdBurningUser == 1;
            var names = string.Join(", ", writers.Take(3).Select(w => w.Name + (w.Drive.Length > 0 ? " (" + w.Drive + ")" : "")));
            var desc = $"Stația are {LogAnalyzer.Dfir.Language.RoText.Count(writers.Count, "unitate care poate inscripționa discuri", "unități care pot inscripționa discuri")}: {names}. " +
                       (explorerOnly
                           ? "Politica NoCDBurning oprește doar inscripționarea din Explorer; ea nu oprește un program de inscripționare (ImgBurn, Nero, un script care folosește serviciul IMAPI), deci scrierea pe disc rămâne posibilă. Blocarea reală este politica de stocare amovibilă „CD și DVD: refuzare scriere” (Deny_Write), care nu este setată. "
                           : "Nici NoCDBurning, nici politica de stocare amovibilă „CD și DVD: refuzare scriere” nu sunt setate, deci scrierea pe disc nu este oprită prin configurație. ") +
                       "Constatarea arată o posibilitate, nu că un disc a fost scris; pentru scriere se caută IMAPI, instrumente de inscripționare executate sau fișiere în folderul de pregătire.";
            o.Add(Chan(c, "AIRGAP-OPTICAL-WRITE-UNBLOCKED", explorerOnly ? "Inscripționare blocată doar în Explorer, nu la nivel de politică" : "Unitate de inscripționare fără blocare prin politică", c.AirGapScope ? Sev(c, Severity.High) : Sev(c, Severity.Medium),
                "OPTICAL MEDIA", "Win32_CDROMDrive / Explorer NoCDBurning / RemovableStorageDevices", $"{writers.Count} unități cu scriere; NoCDBurning={(b.NoCdBurningMachine?.ToString() ?? b.NoCdBurningUser?.ToString() ?? "nesetat")}; Deny_Write CD/DVD={(b.CdDvdDenyWrite is null ? "necitit" : "nesetat")}",
                c.Facts!.CollectedUtc, null, "necunoscut: configurația nu conține contul", names, "scriere pe disc posibilă", "disc CD/DVD", desc,
                "Unitate optică cu capacitate de scriere (WMI Capabilities) și politică de refuzare a scrierii pe CD/DVD nesetată; NoCDBurning citit separat.",
                ["Stația poate fi singura cale aprobată de transfer prin disc, iar inscripționarea să fie permisă prin procedură și înregistrată.", "Unele unități raportează capacitatea de scriere deși nu au disc de scris sau sunt doar cititoare de Blu-ray.", "Politica poate fi aplicată dintr-o altă sursă (GPO de domeniu) care nu s-a putut citi local."],
                [FactRef(c, "burn/writers", names)], [$"airgap_facts burn/writers {names}"], "", SemanticType.Configuration));
        }
        var staged = b.StagingFiles.Where(f => PeriodOrUnset(c, f.LastWriteUtc)).OrderBy(f => f.LastWriteUtc).ToList();
        if (staged.Count > 0)
        {
            var times = staged.Select(f => f.LastWriteUtc).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
            var desc = $"Folderul de pregătire a inscripționării din Windows (Burn) conține {LogAnalyzer.Dfir.Language.RoText.Count(staged.Count, "fișier", "fișiere")}: {string.Join(", ", staged.Take(6).Select(f => f.Name))}{(staged.Count > 6 ? " …" : "")} ({staged.Sum(f => f.Size) / 1_000_000.0:0.#} MB). " +
                       "Explorer copiază aici fișierele alese pentru un disc înainte de scriere și le păstrează până se termină sau se anulează inscripționarea; un fișier rămas în folder arată că cineva a pregătit conținutul pentru un disc. Nu arată dacă discul a fost scris, ce disc și cine l-a luat.";
            o.Add(Chan(c, "MEDIA-OPTICAL-STAGING", $"Fișiere pregătite pentru inscripționare: {staged.Count}", Sev(c, Severity.Medium), "OPTICAL MEDIA", "%LocalAppData%\\Microsoft\\Windows\\Burn\\Burn",
                $"{staged.Count} fișiere în folderul de pregătire", times.Count > 0 ? times[0] : null, times.Count > 0 ? times[^1] : null, "necunoscut: folderul este al unui profil, fișierele nu poartă contul", "folderul Burn", "scriere pe disc probabilă, neconfirmată", "disc CD/DVD", desc,
                "Fișiere în folderul de pregătire a inscripționării din Explorer, cu data ultimei modificări în perioada cazului sau necunoscută.",
                ["Fișierele pot fi rămase dintr-o inscripționare anulată sau terminată, curățate doar la ejectarea discului.", "Un utilizator poate fi pregătit un disc de instalare sau de arhivă aprobat."],
                staged.Take(10).Select(f => FactRef(c, $"burn/staging/{f.Name}", $"{f.Name}, {f.Size} octeți")).ToList(), staged.Take(5).Select(f => $"airgap_facts burn/staging/{f.Name}").ToList(), "T1052", SemanticType.Presence));
        }
        foreach (var log in b.ToolLogs.Where(l => PeriodOrUnset(c, l.LastWriteUtc)))
        {
            var leaf = LogAnalyzer.Dfir.FileSystem.WinPath.GetFileName(log.Path);
            var desc = $"Programul de inscripționare a lăsat jurnalul „{leaf}” (ultima modificare {Time(log.LastWriteUtc)}). Extras: {string.Join(" | ", log.Excerpt.Take(4).Select(Shorten))}. Jurnalul arată că programul a rulat și ce unitate sau imagine a folosit, dacă le notează; nu arată conținutul discului și nici dacă discul a fost predat.";
            o.Add(Chan(c, "MEDIA-OPTICAL-TOOL-LOG", $"Jurnal al unui program de inscripționare: {leaf}", Sev(c, Severity.Medium), "OPTICAL MEDIA", "Jurnal ImgBurn / program de inscripționare",
                $"jurnal {leaf}", log.LastWriteUtc, log.LastWriteUtc, "necunoscut: jurnalul nu conține contul", leaf, "scriere pe disc posibilă (programul a rulat)", "disc CD/DVD", desc,
                "Fișier jurnal al unui program de inscripționare cunoscut (de exemplu ImgBurn), cu data ultimei modificări în perioada cazului sau necunoscută.",
                ["Jurnalul poate descrie o simplă verificare a unei imagini sau a unei unități, fără scriere.", "Programul poate fi aprobat și folosit în procedura de transfer prin disc."],
                [FactRef(c, $"burn/log/{leaf}", $"jurnal {leaf}")], [$"airgap_facts burn/log/{leaf}"], "T1052", SemanticType.Observation));
        }
    }

    // ---- B10: round-trip media ----

    private sealed record NetWindow(DateTimeOffset Start, DateTimeOffset? End, string Label, TimelineEvent Event);

    /// <summary>Periods in which this station was on a network: NetworkProfile 10000 up to the next 10001 of the same network, and WLAN associations (a point in time).</summary>
    private static List<NetWindow> NetworkWindows(Ctx c)
    {
        var ignored = c.Data.Network.IgnoredNetworkNamePrefixes;
        var downs = c.Events.Where(e => Ev(e, "Microsoft-Windows-NetworkProfile/Operational", 10001)).ToList();
        var res = new List<NetWindow>();
        foreach (var u in c.Events.Where(e => Ev(e, "Microsoft-Windows-NetworkProfile/Operational", 10000) && T(e) is not null && F(e, "Name") is { Length: > 0 } n && !ignored.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
        {
            var d = downs.Where(x => F(x, "Name").Equals(F(u, "Name"), StringComparison.OrdinalIgnoreCase) && T(x) > T(u)).OrderBy(x => T(x)).FirstOrDefault();
            res.Add(new(T(u)!.Value, d is null ? null : T(d), $"rețeaua „{F(u, "Name")}”", u));
        }
        foreach (var w in c.Events.Where(e => Ev(e, "Microsoft-Windows-WLAN-AutoConfig/Operational", 8001, 11001, 11005) && T(e) is not null))
            res.Add(new(T(w)!.Value, null, $"Wi-Fi „{F(w, "SSID", "Name")}”", w));
        return res;
    }

    private static readonly TimeSpan SameMoment = TimeSpan.FromMinutes(10);

    /// <summary>
    /// MEDIA-ZONE-ROUNDTRIP: a registered medium of a restricted zone that (a) was connected while this station was on a network (NetworkProfile or Wi-Fi window
    /// of the same time, or within 10 minutes when the disconnect was not logged), or (b) is registered for a zone other than the zone declared for this station.
    /// Either way the medium may carry data across a boundary; the rule states which evidence it rests on and does not claim a transfer.
    /// </summary>
    private static void MediaZoneRoundTrip(Ctx c, List<Finding> o)
    {
        var reg = c.Input.Media;
        if (reg is not { IsDefined: true }) return;
        var windows = NetworkWindows(c);
        foreach (var m in ObservedUsb(c.Events))
        {
            if (m.Serial.Length == 0) continue;
            var rows = reg.Rows.Where(r => MediaRegister.NormalizeSerial(r.Serial) == m.Serial).ToList();
            var row = rows.FirstOrDefault(r => r.StatusValue == MediaStatus.Active) ?? rows.FirstOrDefault();
            if (row is null || row.Zone.Length == 0 && row.Level is null) continue;   // not registered for a restricted zone: nothing to carry across
            var times = m.Times.OrderBy(t => t).ToList();
            var onNet = new List<(DateTimeOffset T, NetWindow W)>();
            foreach (var t in times)
                foreach (var w in windows)
                    if (w.End is { } end ? t >= w.Start && t <= end : (t - w.Start).Duration() <= SameMoment) { onNet.Add((t, w)); break; }
            bool zoneMismatch = row.Zone.Length > 0 && c.Input.SystemZone.Length > 0 && !row.Zone.Equals(c.Input.SystemZone, StringComparison.OrdinalIgnoreCase);
            if (onNet.Count == 0 && !zoneMismatch) continue;
            var reasons = new List<string>();
            if (onNet.Count > 0) reasons.Add($"mediul a fost conectat la {Time(onNet[0].T)} în timp ce stația era pe {onNet[0].W.Label} (conectată la {Time(onNet[0].W.Start)}{(onNet[0].W.End is { } e ? ", deconectată la " + Time(e) : ", deconectarea nu a fost găsită")})");
            if (zoneMismatch) reasons.Add($"mediul este înregistrat pentru zona „{row.Zone}”, iar stația este declarată în zona „{c.Input.SystemZone}”");
            var desc = $"Mediul {row.RegistrationNumber} (seria {m.Serial}), înregistrat{(row.Zone.Length > 0 ? " pentru zona „" + row.Zone + "”" : "")}{(row.Level is { } lv ? ", nivel " + lv.Name : "")}: " + string.Join("; ", reasons) + ". " +
                       "Un mediu care circulă între o zonă protejată și o rețea conectată sau altă zonă poate aduce date dintr-o parte în alta (în ambele sensuri, pe același suport). Constatarea arată coincidența în timp sau diferența de zonă; nu arată ce s-a copiat și nu stabilește direcția.";
            var evs = m.Events.Take(3).Concat(onNet.Take(2).Select(x => x.W.Event)).ToList();
            o.Add(Chan(c, "MEDIA-ZONE-ROUNDTRIP", $"Mediu al zonei „{(row.Zone.Length > 0 ? row.Zone : "protejate")}” folosit și în afara ei: {m.Serial}", onNet.Count > 0 ? Sev(c, Severity.High) : Sev(c, Severity.Medium), "USB", "Registrul de medii (zonă) și NetworkProfile / WLAN-AutoConfig",
                string.Join("; ", reasons), times.Count > 0 ? times[0] : null, times.Count > 0 ? times[^1] : null, m.Users.Count > 0 ? string.Join(", ", m.Users) : UnknownWho, $"mediu {m.Serial} ({row.RegistrationNumber})", "necunoscută: coincidența nu arată direcția transferului", "între zona mediului și rețeaua / zona stației",
                desc, "Mediu înregistrat pentru o zonă, observat în aceeași fereastră de timp cu o conectare de rețea a stației sau pe o stație dintr-o zonă diferită.",
                ["Stația poate fi conectată temporar la o rețea aprobată pentru mentenanță, iar mediul poate fi folosit legitim în acel interval.", "Ora de conectare a mediului poate fi aproximativă (ultima asociere păstrată de Windows).", "Zona declarată a stației sau zona din registru poate fi introdusă greșit."],
                evs.Select(e => Ref(e, $"{e.Source} {e.EventId}".Trim())).ToList(), evs.Select(e => $"{e.Source} {e.EventId} {e.Locator}".Trim()).ToList(), onNet.Count > 0 ? "T1091" : "",
                onNet.Count > 0 ? SemanticType.Correlation : SemanticType.Observation, Confidence.Medium, onNet.Count > 0 ? Classification.Correlated : Classification.Direct));
        }
    }
}
