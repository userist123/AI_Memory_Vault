using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp14Rules
{
    // ---- D4: the kind of every adapter on the station, from the facts collector (the event-log side is AIRGAP-NIC-ADDED) ----

    /// <summary>
    /// AIRGAP-ADAPTER-KIND: an adapter that can reach a network outside the station without the station's own cable (a phone used as a modem, a cellular modem, a USB
    /// Ethernet card) and that was installed in the case period (or whose install time is unknown). Virtual adapters are excluded. An adapter whose
    /// Kernel-PnP 400 event is in the timeline is already reported by AIRGAP-NIC-ADDED, with the same kind named.
    /// </summary>
    private static void AdapterKindsFromFacts(Ctx c, List<Finding> o)
    {
        var f = c.Facts;
        if (f is null) return;
        var seen = c.Events.Where(e => InChannel(e, "Kernel-PnP", 400)).Select(Instance).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var a in f.Adapters.OrderBy(x => x.FirstInstallUtc))
        {
            var (kind, why) = AdapterKinds.Classify(a.Name, a.InstanceId, a.CompatibleIds, a.Service, a.PnpClass, c.Data.Network.VirtualAdapterWords);
            if (kind is not (AdapterKind.PhoneTethering or AdapterKind.Cellular or AdapterKind.UsbEthernet)) continue;
            if (seen.Contains(a.InstanceId)) continue;
            bool periodSet = c.Scope.PeriodFromUtc is not null || c.Scope.PeriodToUtc is not null;
            if (periodSet && a.FirstInstallUtc is not null && !c.InPeriod(a.FirstInstallUtc)) continue;
            var timeNote = a.FirstInstallUtc is { } t ? $"primul instalat: {Time(t)}" : "momentul instalării nu este înregistrat de Windows, deci nu se poate spune dacă adaptorul este nou";
            var sev = kind == AdapterKind.UsbEthernet ? Sev(c, Severity.Medium) : Sev(c, Severity.High);
            var desc = $"Pe sistemul {ScopeWord(c)} este listat adaptorul „{a.Name}” ({a.InstanceId}): {why}; {timeNote}; stare: {(a.Enabled ? "activat" : "dezactivat")}. " +
                       (kind == AdapterKind.UsbEthernet ? "Un adaptor Ethernet pe USB poate lega stația la o altă rețea fără a atinge placa de rețea integrată. " : "Un telefon sau un modem celular dă stației acces direct la internet, independent de rețeaua stației. ") +
                       "Lista arată că adaptorul există, nu că a fost folosit: pentru folosire se citesc NetworkProfile, DHCP-Client și SRUM.";
            o.Add(Chan(c, "AIRGAP-ADAPTER-KIND", $"{AdapterKinds.Describe(kind)}: {a.Name}", sev, kind == AdapterKind.PhoneTethering ? "PHONE" : "NETWORK INTERFACE", "Inventar adaptoare (Win32_NetworkAdapter / PnP)",
                $"{AdapterKinds.Describe(kind)} „{a.Name}” ({(a.Enabled ? "activat" : "dezactivat")})", a.FirstInstallUtc ?? f.CollectedUtc, null, "necunoscut: inventarul nu conține contul", a.InstanceId, "necunoscută: adaptorul nu indică o destinație", "rețea externă posibilă", desc,
                $"Adaptor din clasa Net clasificat ca {AdapterKinds.Describe(kind)} după clasa PnP, identificatorii compatibili, serviciul și descrierea, instalat în perioada cazului sau cu moment necunoscut.",
                ["Un telefon conectat doar pentru încărcare poate lăsa un adaptor RNDIS în lista de dispozitive, fără să fi fost folosit ca modem.", "Un adaptor USB Ethernet poate fi cel aprobat pentru mentenanța unei stații fără placă integrată.", "Un adaptor rămas din configurarea inițială nu mai este conectat."],
                [FactRef(c, $"adapters/{a.InstanceId}", $"{a.Name}: {AdapterKinds.Describe(kind)}")], [$"airgap_facts adapters/{a.InstanceId}"], "T1599", SemanticType.Presence));
        }
    }

    // ---- D11: BadUSB ----

    /// <summary>
    /// AIRGAP-BADUSB-PATTERN: a new external keyboard (HID keyboard class, not PS/2 or built in) connected and a shell started within the window (30 s by default).
    /// A device that acts as a keyboard and types a command is the way a programmable USB stick works; a person who plugs a keyboard in and opens a console
    /// produces the same pair, so the finding states the order in time and nothing more.
    /// </summary>
    private static void BadUsb(Ctx c, List<Finding> o)
    {
        var l = c.Data.Channels;
        bool IsExternal(string inst) => inst.StartsWith("HID\\VID_", StringComparison.OrdinalIgnoreCase) || inst.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) || inst.StartsWith("HID\\{", StringComparison.OrdinalIgnoreCase) || inst.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase);
        var arrivals = new List<(TimelineEvent E, string Inst, string Name)>();
        foreach (var e in c.Events)
        {
            bool pnp = InChannel(e, "Kernel-PnP", 400) || InChannel(e, "DriverFrameworks-UserMode", 2003, 2100) || Ev(e, "Security", 6416);
            if (!pnp) continue;
            var inst = Instance(e);
            var cls = F(e, "ClassGuid", "ClassId");
            var name = F(e, "DriverDescription", "DeviceDescription");
            if (!IsExternal(inst)) continue;
            bool kb = l.KeyboardClassGuids.Any(g => g.Equals(cls, StringComparison.OrdinalIgnoreCase)) || l.KeyboardWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (kb && T(e) is not null) arrivals.Add((e, inst, name));
        }
        if (arrivals.Count == 0) return;
        bool IsShell(string image) => image.Length > 0 && l.ShellImages.Contains(WinPath.GetFileName(image.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
        var shells = ProcessStarts(c.Events).Where(p => IsShell(p.Image) && T(p.Event) is not null).Select(p => (p.Event, Name: WinPath.GetFileName(p.Image), Kind: $"{p.Event.Source[9..]} {p.Event.EventId}"))
            .Concat(c.Events.Where(e => e.Source == "Prefetch" && IsShell(e.Process) && T(e) is not null).Select(e => (e, e.Process, "Prefetch"))).ToList();
        if (shells.Count == 0) return;
        foreach (var g in arrivals.GroupBy(a => a.Inst, StringComparer.OrdinalIgnoreCase))
        {
            (TimelineEvent Key, TimelineEvent Shell, string ShellName, string ShellKind, double Seconds)? best = null;
            foreach (var a in g.OrderBy(x => T(x.E)))
                foreach (var s in shells.OrderBy(x => T(x.Item1)))
                {
                    var dt = (T(s.Item1)!.Value - T(a.E)!.Value).TotalSeconds;
                    if (dt < 0 || dt > l.BadUsbSeconds) continue;
                    best = (a.E, s.Item1, s.Item2, s.Item3, dt);
                    break;
                }
            if (best is not { } b) continue;
            var name = g.Select(x => x.Name).FirstOrDefault(n => n.Length > 0) ?? "tastatură";
            var desc = $"Tastatura „{name}” ({g.Key}) a fost configurată de Windows la {Time(T(b.Key))}, iar {b.ShellName} a pornit la {Time(T(b.Shell))}, la {b.Seconds:0} secunde după ({b.ShellKind}), într-un interval de cel mult {l.BadUsbSeconds} secunde, pe un sistem {ScopeWord(c)}. " +
                       "Un dispozitiv USB programat să se prezinte drept tastatură poate scrie și porni o comandă fără intervenția utilizatorului în câteva secunde. Ordinea în timp nu dovedește că dispozitivul a tastat comanda: un utilizator care conectează o tastatură și deschide o consolă produce același șir de evenimente.";
            o.Add(Chan(c, "AIRGAP-BADUSB-PATTERN", $"Tastatură nouă urmată de o consolă în {b.Seconds:0} secunde: {name}", Sev(c, Severity.High), "USB", "Kernel-PnP 400 / DriverFrameworks / 6416 și 4688 / Sysmon 1 / Prefetch",
                $"tastatură nouă, {b.ShellName} pornit după {b.Seconds:0} secunde", T(b.Key), T(b.Shell), UnknownWho, g.Key, "execuție de comenzi posibilă, comandă necunoscută", "necunoscută: evenimentul nu arată ce s-a tastat",
                desc, "Corelație în timp între conectarea unui dispozitiv din clasa tastatură (extern, HID / USB) și pornirea unui interpretor de comenzi în fereastra din fișierul de date.",
                ["Un utilizator conectează o tastatură externă și deschide o consolă, ca la o intervenție obișnuită.", "Un comutator KVM sau o stație de andocare reîntoarce tastatura și produce evenimentul de configurare fără un dispozitiv nou.", "Un administrator conectează o tastatură și rulează un script de mentenanță aprobat."],
                [Ref(b.Key, $"{b.Key.EventId} tastatură {g.Key}"), Ref(b.Shell, $"{b.ShellKind} {b.ShellName}")], [$"{b.Key.Source} {b.Key.EventId} {b.Key.Locator}".Trim(), $"{b.Shell.Source} {b.Shell.EventId} {b.Shell.Locator}".Trim()], "T1200", SemanticType.Correlation,
                Confidence.Medium, Classification.Correlated));
        }
    }

    // ---- D15: camera, microphone, speakers ----


    /// <summary>
    /// AIRGAP-COVERT-HARDWARE: cameras, microphones and sound devices that are present and enabled. Compared with the camera / microphone policy (control P19)
    /// and the manual check M09 (the camera is covered). A microphone and a speaker together are also an acoustic channel; a camera aimed at the screen is an optical one.
    /// </summary>
    private static void CovertHardware(Ctx c, List<Finding> o)
    {
        var f = c.Facts;
        if (f is null || f.Hardware.Count == 0) return;
        var enabled = f.Hardware.Where(h => h.Enabled).Select(h => (H: h, K: HardwareKinds.Of(h))).ToList();
        var cams = enabled.Where(x => x.K == HardwareKind.Camera).Select(x => x.H).ToList();
        var mics = enabled.Where(x => x.K == HardwareKind.Microphone).Select(x => x.H).ToList();
        var spk = enabled.Where(x => x.K == HardwareKind.Speaker).Select(x => x.H).ToList();
        if (cams.Count + mics.Count + spk.Count == 0) return;
        bool camBlocked = f.WebcamPolicy.Equals("Deny", StringComparison.OrdinalIgnoreCase), micBlocked = f.MicrophonePolicy.Equals("Deny", StringComparison.OrdinalIgnoreCase);
        bool exposed = cams.Count > 0 && !camBlocked || mics.Count > 0 && !micBlocked;
        string Pol(string v) => v.Length == 0 ? "nesetată" : v;
        var parts = new List<string>();
        if (cams.Count > 0) parts.Add($"{LogAnalyzer.Dfir.Language.RoText.Count(cams.Count, "cameră activă", "camere active")} ({string.Join(", ", cams.Take(3).Select(h => h.Name))}); politica de acces la cameră: {Pol(f.WebcamPolicy)}");
        if (mics.Count > 0) parts.Add($"{LogAnalyzer.Dfir.Language.RoText.Count(mics.Count, "microfon activ", "microfoane active")} ({string.Join(", ", mics.Take(3).Select(h => h.Name))}); politica de acces la microfon: {Pol(f.MicrophonePolicy)}");
        if (spk.Count > 0) parts.Add($"{LogAnalyzer.Dfir.Language.RoText.Count(spk.Count, "ieșire audio activă", "ieșiri audio active")} ({string.Join(", ", spk.Take(3).Select(h => h.Name))})");
        var desc = $"Pe sistemul {ScopeWord(c)}: {string.Join("; ", parts)}. " +
                   (exposed ? "Politica nu blochează accesul aplicațiilor la cel puțin unul dintre ele (control P19). " : "Politica P19 blochează accesul aplicațiilor, dar dispozitivele rămân prezente și pornite la nivel de driver. ") +
                   "O cameră poate fotografia ecranul, un microfon poate înregistra conversații din încăpere, iar difuzoarele pot transmite sunete în afara stației (canal acoustic). Lista arată ce echipament există, nu că a fost folosit; acoperirea fizică a camerei se verifică la control (verificarea manuală M09).";
        var evidence = enabled.Where(x => x.K != HardwareKind.Sound).Take(10).Select(x => FactRef(c, $"hardware/{x.H.InstanceId}", $"{x.H.Name} ({x.H.PnpClass}), {(x.H.Enabled ? "activat" : "dezactivat")}")).ToList();
        o.Add(Chan(c, "AIRGAP-COVERT-HARDWARE", $"Cameră, microfon sau difuzoare active pe un sistem izolat: {cams.Count + mics.Count + spk.Count}", exposed ? Sev(c, Severity.Medium) : Sev(c, Severity.Low), "USB", "Inventar dispozitive (clase Camera, Image, MEDIA, AudioEndpoint)",
            string.Join("; ", parts), f.CollectedUtc, null, "necunoscut: inventarul nu conține contul", "camere, microfoane, difuzoare", "necunoscută: este echipament, nu un transfer", "mediul fizic din jurul stației",
            desc, "Dispozitive din clasele Camera, Image și AudioEndpoint (și din MEDIA, după nume) cu stare activată, comparate cu politica de acces la cameră și microfon.",
            ["Majoritatea laptopurilor și monitoarelor au cameră și microfon integrate; prezența lor este normală, iar acoperirea fizică și politica sunt măsurile.", "Difuzoarele sau căștile pot fi necesare pentru lucru (alarme, apeluri de serviciu).", "Un dispozitiv listat poate fi dezactivat în BIOS fără să dispară din listă."],
            evidence.Count > 0 ? evidence : [FactRef(c, "hardware", "inventar echipamente")], enabled.Take(5).Select(x => $"airgap_facts hardware/{x.H.InstanceId}").ToList(), "", SemanticType.Configuration));
    }
}
