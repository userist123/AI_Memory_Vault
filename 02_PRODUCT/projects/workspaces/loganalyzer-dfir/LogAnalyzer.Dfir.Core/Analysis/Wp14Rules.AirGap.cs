using System.Net;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp14Rules
{
    private const string UnknownWho = "necunoscut: evenimentul nu conține contul";
    private const string UnknownDirection = "necunoscută: evenimentul arată conectivitatea, nu transferul de date";

    /// <summary>
    /// Whether the procedure profile (owner decision 19) approves this connectivity: a destination in the authorised list (name, address, CIDR, trailing
    /// wildcard) or an approved transfer channel whose medium names this kind of channel. Without the zones section, nothing is approved:
    /// "conectivitate observată, necesită explicație".
    /// </summary>
    internal static (AirGapAuthorization Auth, string Basis) Authorize(Ctx c, IEnumerable<string> destinations, IReadOnlyList<string> channelKeywords)
    {
        var p = c.Profile;
        if (p is null || (p.NetworkDestinations.Count == 0 && p.TransferChannels.Count == 0))
            return (AirGapAuthorization.Undefined, "profilul de proceduri nu definește destinații autorizate sau canale de transfer: conectivitate observată, necesită explicație (decizia 19)");
        foreach (var dest in destinations.Where(x => x.Length > 0))
            foreach (var row in p.NetworkDestinations)
                if (DestinationMatches(row.Address, dest))
                    return (AirGapAuthorization.Authorized, $"destinația „{dest}” corespunde intrării „{row.Address}” din lista de destinații autorizate (zona {(row.Zone.Length > 0 ? row.Zone : "nespecificată")})");
        foreach (var ch in p.TransferChannels)
            if (channelKeywords.Any(k => ch.Medium.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return (AirGapAuthorization.Authorized, $"canalul de transfer aprobat „{ch.Name}” ({ch.FromZone} → {ch.ToZone}, mediu: {ch.Medium})");
        return (AirGapAuthorization.NotAuthorized, "nicio destinație autorizată și niciun canal de transfer aprobat din profil nu corespunde acestei conectivități");
    }

    private static bool DestinationMatches(string pattern, string value)
    {
        pattern = pattern.Trim(); value = value.Trim();
        if (pattern.Length == 0 || value.Length == 0) return false;
        if (pattern.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
        if (pattern.EndsWith('*') && value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)) return true;
        return pattern.Contains('/') && IPNetwork.TryParse(pattern, out var net) && IPAddress.TryParse(value, out var ip) && net.Contains(ip);
    }

    private static string WhoOf(IEnumerable<TimelineEvent> events)
    {
        var users = events.Select(e => e.User).Where(u => u.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return users.Count > 0 ? string.Join(", ", users) : UnknownWho;
    }

    private static Severity NetSeverity(Ctx c, AirGapAuthorization auth, Severity classified) => auth == AirGapAuthorization.Authorized ? Severity.Info : Sev(c, classified);

    private static Finding Net(Ctx c, string ruleId, string title, Severity sev, string subcategory, string channel, AirGapAuthorization auth, string basis, string observed, List<TimelineEvent> evs,
        string obj, string destination, string description, string reason, string[] alternatives)
    {
        var times = evs.Select(T).Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
        return new Finding
        {
            FindingId = c.NextId(), RuleId = ruleId, Title = title, Severity = sev, Category = Category, Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1599",
            FirstSeenUtc = times.Count > 0 ? times[0] : null, LastSeenUtc = times.Count > 0 ? times[^1] : null, User = WhoOf(evs) == UnknownWho ? "" : WhoOf(evs),
            Description = description, ClassificationReason = reason, SemanticType = SemanticType.Observation,
            SupportingEvidence = evs.Take(20).Select(e => Ref(e, $"{e.EventId} {e.Summary}".Trim())).ToList(),
            AlternativeExplanations = [.. alternatives],
            AirGap = new AirGapDetail(subcategory, channel, auth, basis, observed, times.Count > 0 ? times[0] : null, WhoOf(evs), obj, c.CaseClassText,
                "nu se aplică (nu este un mediu registrat)", UnknownDirection, destination, evs.Take(5).Select(e => $"{e.Source} {e.EventId} {e.Locator}".Trim()).ToList()),
        };
    }

    /// <summary>AIRGAP-* rules; only for a case whose scope network category is AirGappedNetwork or StandalonePc.</summary>
    private static void AirGap(Ctx c, List<Finding> o)
    {
        if (!c.AirGapScope) return;
        NetworkConnected(c, o);
        Wifi(c, o);
        Bluetooth(c, o);
        Dhcp(c, o);
        NicAdded(c, o);
    }

    // ---- network profile connected (NetworkProfile 10000, with 10001 for the duration) ----

    private static void NetworkConnected(Ctx c, List<Finding> o)
    {
        var ignored = c.Data.Network.IgnoredNetworkNamePrefixes;
        var ups = c.Events.Where(e => Ev(e, "Microsoft-Windows-NetworkProfile/Operational", 10000) && F(e, "Name") is { Length: > 0 } n && !ignored.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(e => F(e, "Name"), StringComparer.OrdinalIgnoreCase);
        var downs = c.Events.Where(e => Ev(e, "Microsoft-Windows-NetworkProfile/Operational", 10001)).ToList();
        foreach (var g in ups)
        {
            var items = g.OrderBy(e => T(e)).ToList();
            var (auth, basis) = Authorize(c, [g.Key], c.Data.Network.NetworkKeywords);
            double minutes = 0; int paired = 0;
            foreach (var u in items)
            {
                var d = downs.Where(x => F(x, "Name").Equals(g.Key, StringComparison.OrdinalIgnoreCase) && T(x) > T(u)).OrderBy(x => T(x)).FirstOrDefault();
                if (d is not null && T(u) is { } a && T(d) is { } b) { minutes += (b - a).TotalMinutes; paired++; }
            }
            var dur = paired > 0 ? $" Durată observată: {Math.Round(minutes)} min ({paired} conectări cu deconectare înregistrată)." : " Deconectarea nu a fost găsită în sursele colectate: durata este necunoscută.";
            var desc = $"Profilul de rețea „{g.Key}” a fost conectat de {items.Count} ori ({Time(T(items[0]))} – {Time(T(items[^1]))}) pe un sistem {(c.Scope.Network == NetworkCategory.StandalonePc ? "autonom" : "air-gapped")}." + dur +
                       (auth == AirGapAuthorization.Authorized ? $" Autorizat: {basis}." : $" Conectivitate observată, necesită explicație: {basis}.") + " Evenimentul arată conectarea, nu ce s-a transferat.";
            o.Add(Net(c, "AIRGAP-NETWORK-CONNECTED", $"Conectare la rețea pe un sistem izolat: {g.Key}", NetSeverity(c, auth, Severity.High), "NETWORK INTERFACE", "NetworkProfile/Operational 10000", auth, basis,
                $"conectare la profilul de rețea „{g.Key}” ({items.Count}×)", items, $"rețea „{g.Key}”", g.Key, desc,
                "Evenimente NetworkProfile 10000 (rețea conectată) pe un sistem a cărui categorie de rețea din domeniul cazului este air-gapped sau autonom.",
                ["Sistemul poate fi legat temporar la o rețea aprobată de procedură (mentenanță, transfer autorizat) neînregistrată în profil.", "Evenimentul 10000 apare și pentru rețele fără acces în afară (rețea locală, adaptor virtual)."]));
        }
    }

    // ---- Wi-Fi: WLAN-AutoConfig 8001 / 11001 / 11005 = associated; 11000-11005 other = attempts (counted, not reported alone) ----

    private static void Wifi(Ctx c, List<Finding> o)
    {
        const string Ch = "Microsoft-Windows-WLAN-AutoConfig/Operational";
        var all = c.Events.Where(e => Ev(e, Ch, 8000, 8001, 8003, 11000, 11001, 11002, 11003, 11004, 11005)).ToList();
        var groups = all.Where(e => Ev(e, Ch, 8001, 11001, 11005)).GroupBy(e => F(e, "SSID", "Name"), StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0);
        foreach (var g in groups)
        {
            var items = g.OrderBy(e => T(e)).ToList();
            var attempts = all.Count(e => Ev(e, Ch, 11000, 11002, 11003, 11004) && F(e, "SSID", "Name").Equals(g.Key, StringComparison.OrdinalIgnoreCase));
            var (auth, basis) = Authorize(c, [g.Key], c.Data.Network.WifiKeywords);
            var desc = $"Asociere Wi-Fi la „{g.Key}” ({string.Join(", ", items.Select(e => e.EventId).Distinct())}; {items.Count} evenimente, {attempts} încercări/etape asociate) {Time(T(items[0]))} – {Time(T(items[^1]))} pe un sistem " +
                       (c.Scope.Network == NetworkCategory.StandalonePc ? "autonom" : "air-gapped") + "." +
                       (auth == AirGapAuthorization.Authorized ? $" Autorizat: {basis}." : $" Conectivitate observată, necesită explicație: {basis}.") + " Evenimentul arată asocierea la rețeaua radio, nu traficul.";
            o.Add(Net(c, "AIRGAP-WIFI-ASSOCIATED", $"Asociere Wi-Fi pe un sistem izolat: {g.Key}", NetSeverity(c, auth, Severity.High), "WIRELESS", "WLAN-AutoConfig/Operational " + string.Join("/", items.Select(e => e.EventId).Distinct()), auth, basis,
                $"asociere la SSID „{g.Key}” ({items.Count}×)", items, $"SSID „{g.Key}”", g.Key, desc,
                "Evenimente WLAN-AutoConfig 8001 (conectat) sau 11001/11005 (asociere / securitate reușite) pe un sistem air-gapped sau autonom. Încercările eșuate singure nu sunt raportate ca asociere.",
                ["Adaptorul Wi-Fi poate fi folosit legitim într-o procedură aprobată (de exemplu rețea dedicată).", "SSID-ul poate aparține unei rețele locale fără ieșire în afară."]));
        }
    }

    // ---- Bluetooth: device nodes BTHENUM / BTHLE and Bluetooth pairing providers; the radio itself is a NIC-ADDED ----

    private static bool IsBluetoothRadio(Ctx c, TimelineEvent e)
    {
        var guid = F(e, "ClassGuid");
        return c.Data.Network.BluetoothClassGuids.Any(g => g.Equals(guid, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBluetoothDeviceNode(string inst) => inst.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) || inst.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase) || inst.StartsWith("BTH\\", StringComparison.OrdinalIgnoreCase);

    private static readonly Regex MacRx = new(@"DEV_(?<m>[0-9A-Fa-f]{12})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static void Bluetooth(Ctx c, List<Finding> o)
    {
        var hits = new List<(TimelineEvent E, string Key, string Name)>();
        foreach (var e in c.Events)
        {
            if (InChannel(e, "Kernel-PnP", 400))
            {
                var inst = Instance(e);
                if (IsBluetoothDeviceNode(inst) && !IsBluetoothRadio(c, e))
                    hits.Add((e, MacRx.Match(inst) is { Success: true } m ? m.Groups["m"].Value.ToUpperInvariant() : inst, F(e, "DriverDescription", "DeviceDescription")));
            }
            else if (e.Source.StartsWith("EventLog:", StringComparison.OrdinalIgnoreCase) && e.Source.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) &&
                     !e.Source.Contains("Kernel-PnP", StringComparison.OrdinalIgnoreCase))
                hits.Add((e, F(e, "DeviceAddress", "Address", "DeviceName", "Name") is { Length: > 0 } k ? k : "(dispozitiv nespecificat)", F(e, "DeviceName", "Name")));
        }
        foreach (var g in hits.GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
        {
            var items = g.OrderBy(h => T(h.E)).ToList();
            var evs = items.Select(h => h.E).ToList();
            var (auth, basis) = Authorize(c, [g.Key, .. items.Select(h => h.Name).Where(n => n.Length > 0).Distinct()], c.Data.Network.BluetoothKeywords);
            var desc = $"Dispozitiv Bluetooth {g.Key}{(items[0].Name.Length > 0 ? " (" + items[0].Name + ")" : "")}: {evs.Count} evenimente ({string.Join(", ", evs.Select(e => e.Source.StartsWith("EventLog:") ? e.Source[9..] + " " + e.EventId : e.Source).Distinct())}), {Time(T(evs[0]))} – {Time(T(evs[^1]))} " +
                       "pe un sistem " + (c.Scope.Network == NetworkCategory.StandalonePc ? "autonom" : "air-gapped") + ". Nodul de dispozitiv Bluetooth sau evenimentul de asociere arată că dispozitivul a fost asociat sau descoperit; nu arată ce s-a transmis." +
                       (auth == AirGapAuthorization.Authorized ? $" Autorizat: {basis}." : $" Conectivitate observată, necesită explicație: {basis}.");
            o.Add(Net(c, "AIRGAP-BLUETOOTH-PAIRED", $"Dispozitiv Bluetooth asociat pe un sistem izolat: {g.Key}", NetSeverity(c, auth, Severity.Medium), "BLUETOOTH", "Kernel-PnP/Configuration 400 / Bluetooth", auth, basis,
                $"dispozitiv Bluetooth {g.Key} asociat/descoperit ({evs.Count} evenimente)", evs, $"dispozitiv Bluetooth {g.Key}", g.Key, desc,
                "Nod de dispozitiv Bluetooth (BTHENUM / BTHLE) creat de Kernel-PnP sau eveniment al furnizorilor Bluetooth, pe un sistem air-gapped sau autonom.",
                ["Dispozitive de intrare (tastatură, mouse) asociate legitim produc același nod.", "Nodul poate rămâne în urma unei descoperiri fără asociere finalizată."]));
        }
    }

    // ---- DHCP-Client: 50036 / 50037 / 50065 / 1103 ----

    private static readonly int[] DhcpIds = [50036, 50037, 50065, 1103];

    private static void Dhcp(Ctx c, List<Finding> o)
    {
        var evs = c.Events.Where(e => InChannel(e, "Dhcp-Client", DhcpIds)).OrderBy(e => T(e)).ToList();
        foreach (var g in evs.GroupBy(e => F(e, "InterfaceGuid", "InterfaceName", "Interface"), StringComparer.OrdinalIgnoreCase))
        {
            var items = g.ToList();
            var ips = items.Select(e => F(e, "IPAddress", "IpAddress", "Address", "LeasedAddress")).Where(x => x.Length > 0).Distinct().ToList();
            var servers = items.Select(e => F(e, "DhcpServer", "ServerAddress", "DhcpServerAddress", "Server")).Where(x => x.Length > 0).Distinct().ToList();
            var gws = items.Select(e => F(e, "Gateway", "DefaultGateway", "Router")).Where(x => x.Length > 0).Distinct().ToList();
            var (auth, basis) = Authorize(c, [.. ips, .. servers, .. gws], c.Data.Network.NetworkKeywords.Where(_ => false).ToList());
            var desc = $"Clientul DHCP a înregistrat {items.Count} evenimente ({string.Join(", ", items.Select(e => e.EventId).Distinct())}) {Time(T(items[0]))} – {Time(T(items[^1]))} pe un sistem " +
                       (c.Scope.Network == NetworkCategory.StandalonePc ? "autonom" : "air-gapped") + ". " +
                       (ips.Count > 0 ? $"Adresă: {string.Join(", ", ips)}" : "detalii: adresa nu este în eveniment") + (servers.Count > 0 ? $"; server DHCP: {string.Join(", ", servers)}" : "") + (gws.Count > 0 ? $"; gateway: {string.Join(", ", gws)}" : "") +
                       ". Semnificația exactă a ID-ului depinde de versiunea Windows; evenimentul arată activitatea clientului DHCP, nu un transfer de date." +
                       (auth == AirGapAuthorization.Authorized ? $" Autorizat: {basis}." : $" Conectivitate observată, necesită explicație: {basis}.");
            o.Add(Net(c, "AIRGAP-DHCP-LEASE", $"Activitate DHCP pe un sistem izolat{(ips.Count > 0 ? ": " + string.Join(", ", ips) : "")}", NetSeverity(c, auth, Severity.Medium), "NETWORK INTERFACE", "Dhcp-Client/Operational " + string.Join("/", items.Select(e => e.EventId).Distinct()),
                auth, basis, $"{items.Count} evenimente DHCP-Client{(ips.Count > 0 ? ", adresă " + string.Join(", ", ips) : "")}", items,
                ips.Count > 0 ? $"adresă {string.Join(", ", ips)}" : "adresă nespecificată în eveniment", servers.Count > 0 ? string.Join(", ", servers) : "necunoscută: serverul DHCP nu este în eveniment", desc,
                "Evenimente DHCP-Client (50036, 50037, 50065, 1103) pe un sistem air-gapped sau autonom: clientul DHCP a fost activ pe o interfață.",
                ["Un server DHCP local al zonei poate atribui legitim adrese și într-un segment izolat.", "Unele ID-uri DHCP-Client raportează eșecuri sau reînnoiri, nu un lease obținut."]));
        }
    }

    // ---- NIC added: Kernel-PnP 400 with the Net class (or a Bluetooth radio) ----

    private static void NicAdded(Ctx c, List<Finding> o)
    {
        var netGuid = c.Data.Network.NetClassGuid;
        foreach (var g in c.Events.Where(e => InChannel(e, "Kernel-PnP", 400) && (F(e, "ClassGuid").Equals(netGuid, StringComparison.OrdinalIgnoreCase) || IsBluetoothRadio(c, e)))
                                   .GroupBy(e => Instance(e), StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0))
        {
            var items = g.OrderBy(e => T(e)).ToList();
            bool radio = IsBluetoothRadio(c, items[0]);
            var name = F(items[0], "DriverDescription", "DeviceDescription", "DriverName");
            bool virt = c.Data.Network.VirtualAdapterWords.Any(w => (name + " " + g.Key).Contains(w, StringComparison.OrdinalIgnoreCase)) || g.Key.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) || g.Key.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase);
            var sev = virt ? Sev(c, Severity.Low) : Sev(c, Severity.Medium);
            var kind = radio ? "adaptor Bluetooth" : virt ? "adaptor de rețea virtual" : "adaptor de rețea fizic";
            var desc = $"Kernel-PnP a configurat {kind} {(name.Length > 0 ? "„" + name + "” " : "")}({g.Key}) {Time(T(items[0]))}{(items.Count > 1 ? " – " + Time(T(items[^1])) : "")} pe un sistem " +
                       (c.Scope.Network == NetworkCategory.StandalonePc ? "autonom" : "air-gapped") + ". Evenimentul 400 apare la prima instalare și la schimbări de driver: nu dovedește un adaptor nou fizic și nu arată dacă a fost activat sau folosit. " +
                       (virt ? "Adaptorul pare virtual (listă de date), deci nu este neapărat o ieșire din sistem. " : "") + "Activarea/dezactivarea adaptorului nu este înregistrată separat în sursele colectate.";
            o.Add(Net(c, "AIRGAP-NIC-ADDED", $"{(radio ? "Adaptor Bluetooth" : "Adaptor de rețea")} configurat pe un sistem izolat: {(name.Length > 0 ? name : g.Key)}", sev, radio ? "BLUETOOTH" : "NETWORK INTERFACE", "Kernel-PnP/Configuration 400", AirGapAuthorization.Undefined,
                "profilul de proceduri nu conține o listă de adaptoare autorizate", $"{kind} configurat ({items.Count}×)", items, g.Key, "necunoscută: adaptorul nu indică o destinație", desc,
                "Eveniment Kernel-PnP 400 pentru un dispozitiv din clasa Net (sau radio Bluetooth) pe un sistem air-gapped sau autonom.",
                ["Reinstalarea driverului, o actualizare Windows sau prima pornire a unui adaptor integrat produc același eveniment.", "Adaptorul poate fi dezactivat în BIOS sau în Windows fără a lăsa alte urme în aceste surse."]));
        }
    }
}
