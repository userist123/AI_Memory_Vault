using System.Globalization;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>What the sources around one network connection show, and which source could not be read („sursă lipsă: …, de ce lipsește, cum se pornește”).</summary>
internal sealed class NetEvidence
{
    public List<string> Known { get; } = [];
    public List<string> Missing { get; } = [];
    public List<TimelineEvent> Refs { get; } = [];
}

/// <summary>
/// Owner directive 2026-10-16, item 2: instead of „ce nu putem demonstra” the network-connection finding collects, around every connection window, what Windows
/// keeps about it: the adapter (WLAN-AutoConfig 8001/8003/11001, DHCP-Client, NetworkList profiles and signatures), the traffic (Security 5156/5157, pfirewall.log,
/// SRUM Network Data Usage), the internet egress (NCSI 4042, NCSI active probing), the duration (10001, WLAN 8003, SRUM Network Connectivity, NetworkList
/// DateLastConnected) and the approval (none on a Standalone station). Only what really could not be read stays, worded as a missing source with its remedy;
/// a source the audit policy requires that is missing is itself NECONFORM on a Standalone station.
/// </summary>
public static partial class Wp14Rules
{
    private const string NetworkProfileChannel = "Microsoft-Windows-NetworkProfile/Operational";
    private const string WlanChannel = "Microsoft-Windows-WLAN-AutoConfig/Operational";
    private const string NcsiChannel = "Microsoft-Windows-NCSI/Operational";
    private const string DhcpOperational = "Microsoft-Windows-Dhcp-Client/Operational";
    private const string DhcpAdmin = "Microsoft-Windows-Dhcp-Client/Admin";
    private static readonly TimeSpan Slack = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AssumedWindow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan SrumBucket = TimeSpan.FromMinutes(60);

    internal const string HowToEnableFirewallLog = "cum se pornește: Set-NetFirewallProfile -All -LogAllowed True -LogBlocked True (sau GPO: Windows Defender Firewall → Logging), ca administrator";
    // The remedy is described, never given as a ready command line: the Standalone build contains no text that would change the station's audit policy.
    internal const string HowToEnableWfpAudit = "cum se pornește: administratorul activează auditul subcategoriei „Filtering Platform Connection” (succes și eșec) prin politica de grup (Advanced Audit Policy → Object Access) sau cu auditpol";

    private static string Hm(DateTimeOffset? t) => t is { } x ? x.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "oră necunoscută";

    private static string Top(IEnumerable<string> values, int n = 5)
    {
        var g = values.Where(v => v.Length > 0).GroupBy(v => v, StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        return g.Count == 0 ? "" : string.Join(", ", g.Take(n).Select(x => x.Count() > 1 ? $"{x.Key} ({x.Count()}×)" : x.Key)) + (g.Count > n ? $" și încă {g.Count - n}" : "");
    }

    private static string Bytes(long b) => b >= 1_000_000 ? (b / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : b >= 1_000 ? (b / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " kB" : $"{b} B";

    /// <summary>The window of each connection: from the 10000 record to the first disconnect (10001 of the same network or WLAN 8003), else an assumed hour.</summary>
    internal static List<(DateTimeOffset Start, DateTimeOffset End, bool EndObserved, string EndSource)> Windows(Ctx c, string name, IReadOnlyList<TimelineEvent> ups)
    {
        var downs = c.Events.Where(e => Ev(e, NetworkProfileChannel, 10001) && F(e, "Name").Equals(name, StringComparison.OrdinalIgnoreCase)).Select(e => (T: T(e), Src: "NetworkProfile 10001")).ToList();
        var wlanDowns = c.Events.Where(e => Ev(e, WlanChannel, 8003)).Select(e => (T: T(e), Src: "WLAN-AutoConfig 8003")).ToList();
        var list = new List<(DateTimeOffset, DateTimeOffset, bool, string)>();
        foreach (var u in ups)
        {
            if (T(u) is not { } s) continue;
            var next = ups.Select(T).Where(t => t > s).DefaultIfEmpty(null).Min();
            var end = downs.Concat(wlanDowns).Where(d => d.T > s && (next is null || d.T <= next)).OrderBy(d => d.T).FirstOrDefault();
            list.Add(end.T is { } e ? (s, e, true, end.Src) : (s, s + AssumedWindow, false, "presupus: o oră"));
        }
        return list;
    }

    private static bool In(List<(DateTimeOffset Start, DateTimeOffset End, bool EndObserved, string EndSource)> w, DateTimeOffset? t, TimeSpan before, TimeSpan after) =>
        t is { } x && w.Any(v => x >= v.Start - before && x <= v.End + after);

    internal static NetEvidence GatherNetworkEvidence(Ctx c, string name, IReadOnlyList<TimelineEvent> ups)
    {
        var ev = new NetEvidence();
        var win = Windows(c, name, ups);
        var facts = c.Facts;

        // ---- adapter ----
        var first = ups[0];
        var cat = F(first, "Category");
        if (cat.Length > 0 || F(first, "Description").Length > 0)
            ev.Known.Add($"Profilul NetworkProfile: „{name}”{(F(first, "Description") is { Length: > 0 } d && !d.Equals(name, StringComparison.OrdinalIgnoreCase) ? $" („{d}”)" : "")}, " +
                         $"categoria {(int.TryParse(cat, out var ci) ? NetworkListProfileFact.CategoryLabel(ci) : cat.Length > 0 ? cat : "necunoscută")}.");
        var wlan = c.Events.Where(e => Ev(e, WlanChannel, 8001, 8003, 11001) && In(win, T(e), Slack, Slack)).OrderBy(e => T(e)).ToList();
        foreach (var w in wlan.Where(e => e.EventId == "8001").Take(3))
            ev.Known.Add($"Wi-Fi (WLAN-AutoConfig 8001, {Hm(T(w))}): SSID „{F(w, "SSID", "ProfileName")}”, adaptor „{F(w, "InterfaceDescription")}”" +
                         (F(w, "InterfaceGuid") is { Length: > 0 } g ? $" ({g})" : "") + (F(w, "BSSID", "PeerMac", "Bssid", "APMacAddress") is { Length: > 0 } b ? $", BSSID {b}" : ", BSSID neînregistrat în eveniment") +
                         (F(w, "AuthenticationAlgorithm", "Authentication") is { Length: > 0 } a ? $", autentificare {a}" : "") + ".");
        ev.Refs.AddRange(wlan.Take(5));
        if (wlan.Count == 0)
        {
            if (c.Has("EventLog:" + WlanChannel)) ev.Known.Add("Nicio asociere Wi-Fi (WLAN-AutoConfig 8001/8003/11001) în fereastra conectării, deși jurnalul este în caz: conexiunea nu a fost prin Wi-Fi (cablu, USB, telefon prin cablu sau alt adaptor).");
            else ev.Missing.Add($"sursă lipsă: {WlanChannel} (8001/8003/11001: SSID, BSSID, adaptor), de ce lipsește: canalul nu este în caz (lipsește pe stațiile fără Wi-Fi sau nu a fost colectat), cum se pornește: colectarea jurnalelor de evenimente pe stație (canalul este activ implicit unde există Wi-Fi).");
        }
        var dhcp = c.Events.Where(e => (InChannel(e, "Dhcp-Client")) && In(win, T(e), Slack, Slack)).OrderBy(e => T(e)).ToList();
        if (dhcp.Count > 0)
        {
            ev.Known.Add($"DHCP ({string.Join("/", dhcp.Select(e => e.EventId).Distinct())}): interfața {(Top(dhcp.Select(e => F(e, "InterfaceGuid", "InterfaceName", "Interface", "AdapterName")), 2) is { Length: > 0 } i ? i : "nespecificată în eveniment")}" +
                         (Top(dhcp.Select(e => F(e, "IPAddress", "IpAddress", "Address", "LeasedAddress"))) is { Length: > 0 } ip ? $", adresa {ip}" : "") +
                         (Top(dhcp.Select(e => F(e, "Gateway", "DefaultGateway", "Router"))) is { Length: > 0 } gw ? $", gateway {gw}" : "") +
                         (Top(dhcp.Select(e => F(e, "DhcpServer", "ServerAddress", "DhcpServerAddress", "Server"))) is { Length: > 0 } sv ? $", server DHCP {sv}" : "") +
                         (Top(dhcp.Select(e => F(e, "LeaseTime", "LeaseDuration", "LeaseObtainedTime"))) is { Length: > 0 } lt ? $", lease {lt}" : "") + ".");
            ev.Refs.AddRange(dhcp.Take(5));
        }
        else if (c.Has("EventLog:" + DhcpOperational) || c.Has("EventLog:" + DhcpAdmin))
            ev.Known.Add("Clientul DHCP nu a înregistrat nimic în fereastra conectării: adresa a fost fixă (configurată manual) sau a fost primită fără eveniment jurnalizat.");
        else ev.Missing.Add($"sursă lipsă: {DhcpOperational} și {DhcpAdmin} (interfața, adresa, gateway-ul, lease-ul), de ce lipsește: canalele nu sunt în caz (Operational este oprit implicit pe unele versiuni), cum se pornește: wevtutil sl {DhcpOperational} /e:true și colectarea jurnalelor de evenimente.");
        if (facts is null)
            ev.Missing.Add("sursă lipsă: registrul NetworkList (Profiles și Signatures: tipul rețelei, data creării, ultima conectare, MAC-ul gateway-ului, sufixul DNS), de ce lipsește: faptele stației (AirGapFacts) nu sunt în acest caz, cum se pornește: colectarea pe stație (colectorul AirGapFacts).");
        else if (!facts.Network.ProfilesRead)
            ev.Missing.Add("sursă lipsă: registrul NetworkList, de ce lipsește: citirea a eșuat la colectare (vezi erorile faptelor stației), cum se pornește: colectarea din nou ca administrator.");
        else if (facts.Network.Profiles.FirstOrDefault(p => p.ProfileName.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } p)
            ev.Known.Add($"NetworkList: profilul „{p.ProfileName}” — {NetworkListProfileFact.NameTypeLabel(p.NameType)}, categoria {NetworkListProfileFact.CategoryLabel(p.Category)}, creat {Hm(p.DateCreatedUtc)}, ultima conectare {Hm(p.DateLastConnectedUtc)}" +
                         (p.DefaultGatewayMac.Length > 0 ? $"; MAC gateway {p.DefaultGatewayMac}" : "") + (p.DnsSuffix.Length > 0 && p.DnsSuffix != "<none>" ? $"; sufix DNS {p.DnsSuffix}" : "") +
                         (p.FirstNetwork.Length > 0 ? $"; prima rețea identificată „{p.FirstNetwork}”" : "") + (p.SignatureKind.Length > 0 ? $" (semnătură {(p.SignatureKind == "Managed" ? "de domeniu" : "fără domeniu")})" : "") + ".");
        else ev.Known.Add($"NetworkList nu mai conține profilul „{name}” (citit la colectare): profilul a fost șters după conectare sau evenimentul provine din altă copie a sistemului.");

        // ---- traffic ----
        var wfpAll = c.Events.Where(e => Ev(e, "Security", 5156, 5157)).ToList();
        var wfp = wfpAll.Where(e => In(win, T(e), TimeSpan.Zero, TimeSpan.Zero)).ToList();
        bool standaloneRequired = c.Standalone;
        if (wfp.Count > 0)
        {
            ev.Known.Add($"Firewall (Security 5156/5157) în fereastra conectării: {wfp.Count(e => e.EventId == "5156")} conexiuni permise, {wfp.Count(e => e.EventId == "5157")} blocate; " +
                         $"destinații: {Top(wfp.Select(e => F(e, "DestAddress", "DestinationAddress")))}; programe: {Top(wfp.Select(e => WinPath.GetFileName(F(e, "Application", "ApplicationName"))))}.");
            ev.Refs.AddRange(wfp.Take(5));
        }
        else if (!c.Has("EventLog:Security"))
            ev.Missing.Add("sursă lipsă: jurnalul Security (5156/5157: conexiunile permise și blocate de firewall, cu programul și destinația), de ce lipsește: nu este în caz (se exportă doar cu drepturi de administrator), cum se pornește: colectarea ca administrator.");
        else if (wfpAll.Count == 0)
        {
            var why = facts?.Network.WfpAuditSuccess == false ? "auditul „Filtering Platform Connection” este oprit (citit din politica de audit a stației)"
                    : "niciun eveniment 5156/5157 în tot jurnalul Security: auditul „Filtering Platform Connection” este oprit (sau jurnalul a fost suprascris)";
            ev.Missing.Add($"sursă lipsă: Security 5156/5157{(standaloneRequired ? " — sursă obligatorie (politica de audit a controlului cere „Filtering Platform Connection”), deci NECONFORM în postura Standalone" : "")}, de ce lipsește: {why}, {HowToEnableWfpAudit}.");
        }
        else ev.Known.Add("Firewall (Security 5156/5157): nicio conexiune în fereastra conectării, deși auditul WFP a produs evenimente în alte momente (era pornit).");

        var fw = c.Events.Where(e => e.Source.Equals(FirewallLogParser.SourceName, StringComparison.OrdinalIgnoreCase) && In(win, T(e), TimeSpan.Zero, TimeSpan.Zero)).ToList();
        if (fw.Count > 0)
        {
            ev.Known.Add($"pfirewall.log în fereastra conectării: {fw.Count(e => e.EventId.Equals("ALLOW", StringComparison.OrdinalIgnoreCase))} permise, {fw.Count(e => e.EventId.Equals("DROP", StringComparison.OrdinalIgnoreCase))} blocate; adrese la distanță: {Top(fw.Select(e => e.RemoteIp))}.");
            ev.Refs.AddRange(fw.Take(5));
        }
        else if (c.Has(FirewallLogParser.SourceName))
            ev.Known.Add("pfirewall.log nu are rânduri în fereastra conectării (fișierul este circular: rândurile vechi se pierd; sau nicio conexiune jurnalizată).");
        else
        {
            var logs = facts?.Network.FirewallLogs ?? [];
            var why = facts is null ? "nu este în caz (faptele stației lipsesc, deci nici setarea nu se cunoaște)"
                    : logs.Count > 0 && logs.All(l => l.LogAllowed != true && l.LogDropped != true) ? "jurnalizarea firewall-ului este oprită pe toate profilurile (implicit în Windows)"
                    : logs.Any(l => l.FileExists) ? "fișierul există pe stație, dar nu a fost colectat (cere drepturi de administrator)"
                    : "jurnalizarea este pornită, dar fișierul nu există (nicio conexiune jurnalizată)";
            ev.Missing.Add($"sursă lipsă: jurnalul firewall-ului pfirewall.log (%SystemRoot%\\System32\\LogFiles\\Firewall), de ce lipsește: {why}, {HowToEnableFirewallLog}.");
        }

        var srum = c.Events.Where(e => e.Source == "SRUM" && e.Provider == "SRUM Network Data Usage" && In(win, T(e), TimeSpan.Zero, SrumBucket)).ToList();
        if (srum.Count > 0)
        {
            long sent = srum.Sum(e => long.TryParse(F(e, "BytesSent"), out var x) ? x : 0), recv = srum.Sum(e => long.TryParse(F(e, "BytesRecvd"), out var x) ? x : 0);
            ev.Known.Add($"Volum transferat (SRUM Network Data Usage, intervale orare care acoperă conectarea): {Bytes(sent)} trimiși, {Bytes(recv)} primiți; programe: {Top(srum.Select(e => e.Process))}; " +
                         $"interfețe (LUID): {Top(srum.Select(e => F(e, "InterfaceLuid")), 3)}.");
            ev.Refs.AddRange(srum.OrderByDescending(e => (long.TryParse(F(e, "BytesSent"), out var a) ? a : 0) + (long.TryParse(F(e, "BytesRecvd"), out var b) ? b : 0)).Take(5));
        }
        else if (c.Has("SRUM")) ev.Known.Add("SRUM nu are trafic înregistrat în intervalele orare ale conectării (SRUM scrie pe disc o dată pe oră; ultima oră dinaintea unei opriri bruște poate lipsi).");
        else ev.Missing.Add("sursă lipsă: SRUM (C:\\Windows\\System32\\sru\\SRUDB.dat: octeții trimiși și primiți per program, per oră, per interfață, și durata conectării), de ce lipsește: baza nu este în caz (se copiază doar cu drepturi de administrator, în colectarea Standard sau Completă), cum se pornește: colectarea Standard ca administrator (copie prin VSS cu esentutl).");

        // ---- internet egress ----
        var ncsi = c.Events.Where(e => Ev(e, NcsiChannel, 4042) && In(win, T(e), Slack, TimeSpan.Zero)).OrderBy(e => T(e)).ToList();
        bool probingOff = facts is { } fa && (fa.Network.NcsiEnableActiveProbing == 0 || fa.Network.NcsiPolicyNoActiveProbe == 1);
        if (ncsi.Count > 0)
        {
            var caps = ncsi.Select(e => (T: T(e), Cap: F(e, "Capability", "NewCapability", "CapabilityName", "InternetCapability", "Connectivity"))).ToList();
            bool internet = caps.Any(x => x.Cap.Contains("Internet", StringComparison.OrdinalIgnoreCase));
            ev.Known.Add($"Ieșire spre internet (NCSI 4042): {string.Join("; ", caps.Take(5).Select(x => $"{Hm(x.T)} capacitate „{(x.Cap.Length > 0 ? x.Cap : "nespecificată în eveniment")}”"))}" +
                         (internet ? " — rețeaua AVEA ieșire la internet." : " — NCSI nu a raportat acces la internet (doar local sau fără conectivitate).") +
                         (probingOff ? " Sondarea activă NCSI este oprită pe stație, deci verdictul este pasiv (nu a fost testat cu o cerere)." : ""));
            ev.Refs.AddRange(ncsi.Take(3));
        }
        else if (c.Has("EventLog:" + NcsiChannel))
            ev.Known.Add("NCSI (4042) nu a înregistrat nicio schimbare a capacității de acces în fereastra conectării: ieșirea spre internet nu a fost observată de Windows." +
                         (probingOff ? " Sondarea activă NCSI este oprită pe stație, deci NCSI nu ar fi testat oricum accesul." : ""));
        else ev.Missing.Add($"sursă lipsă: {NcsiChannel} (4042: dacă rețeaua avea internet, doar acces local sau nimic), de ce lipsește: canalul nu este în caz (aplicațiile mai vechi nu îl colectau), cum se pornește: colectarea jurnalelor de evenimente cu această versiune a aplicației.");

        // ---- duration ----
        var parts = new List<string>();
        var observed = win.Where(w => w.EndObserved).ToList();
        if (observed.Count > 0)
            parts.Add($"{Math.Round(observed.Sum(w => (w.End - w.Start).TotalMinutes))} min din {observed.Count} conectări cu deconectare înregistrată ({string.Join(", ", observed.Select(w => w.EndSource).Distinct())})");
        var conn = c.Events.Where(e => e.Source == "SRUM" && e.Provider == "SRUM Network Connectivity" && In(win, T(e), TimeSpan.Zero, SrumBucket)).ToList();
        if (conn.Count > 0)
        {
            long secs = conn.Sum(e => long.TryParse(F(e, "ConnectedTime"), out var x) ? x : 0);
            parts.Add($"SRUM Network Connectivity: {Math.Round(secs / 60.0)} min conectat în intervalele orare ale conectării (interfețe {Top(conn.Select(e => F(e, "InterfaceLuid")), 3)})");
            ev.Refs.AddRange(conn.Take(3));
        }
        if (facts?.Network.Profiles.FirstOrDefault(p => p.ProfileName.Equals(name, StringComparison.OrdinalIgnoreCase)) is { DateLastConnectedUtc: { } last })
            parts.Add($"NetworkList: ultima conectare {Hm(last)}");
        if (parts.Count > 0) ev.Known.Add("Durată: " + string.Join("; ", parts) + ".");
        if (observed.Count < win.Count && conn.Count == 0)
            ev.Missing.Add($"sursă lipsă: momentul deconectării pentru {win.Count - observed.Count} din {win.Count} conectări, de ce lipsește: nu există NetworkProfile 10001, WLAN-AutoConfig 8003 sau SRUM Network Connectivity pentru ele în sursele citite (fereastra a fost presupusă de o oră), " +
                            "cum se pornește: colectarea jurnalelor NetworkProfile și WLAN-AutoConfig și a bazei SRUM (Standard, ca administrator).");

        // ---- approval ----
        if (c.Standalone)
            ev.Known.Add("Aprobare: în postura Standalone nu există nicio conectare aprobată — nicio rețea nu este permisă pe o stație autonomă după instalarea aplicației.");
        return ev;
    }
}
