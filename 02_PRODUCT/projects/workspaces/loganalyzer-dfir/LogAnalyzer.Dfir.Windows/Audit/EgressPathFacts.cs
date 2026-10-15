using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// One configured or present way for the station to reach beyond itself, found by READING local configuration only.
/// Internal: true = inside the internal ranges (RFC 1918, link-local, unique-local, loopback), false = outside, null = cannot be told without
/// resolving a name (which would send traffic, so it is not done). Active: false = configured or present but not up now.
/// </summary>
public sealed record EgressPathFact(string Kind, string Description, bool? Internal, bool Active);

/// <summary>
/// PASSIVE collection for check N11 ("no path to the internet"). It opens no socket, sends no packet and resolves no name: it reads
/// adapter properties (the managed wrapper over the IP Helper read APIs), the registry, a local file and WMI. The Standalone edition runs
/// it too, so it must stay free of anything the edition build test bans (sockets, HTTP, Ping, Dns, NetworkChange, ws2_32/dnsapi P/Invoke).
/// </summary>
internal static class EgressPathCollector
{
    public static List<EgressPathFact> Collect()
    {
        var paths = new List<EgressPathFact>();
        Guard(() => Adapters(paths));
        Guard(() => StaticConfiguration(paths));
        Guard(() => Proxies(paths));
        Guard(() => RasProfiles(paths));
        return paths;
    }

    private static void Guard(Action a)
    {
        try { a(); }
        catch (Exception ex) when (ex is NetworkInformationException or UnauthorizedAccessException or System.Security.SecurityException or IOException
                                      or ManagementException or System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }

    // ---- adapters (active gateways and DNS servers; radios and tunnels even when down) -------------------------------

    private static void Adapters(List<EgressPathFact> paths)
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
            bool up = ni.OperationalStatus == OperationalStatus.Up;
            var label = $"{ni.Name} ({ni.Description})";
            switch (ni.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Wireless80211:
                    paths.Add(new("Wi-Fi", $"adaptor Wi-Fi {label}, {(up ? "pornit" : "prezent, oprit")}", false, up)); break;
                case NetworkInterfaceType.Wwanpp:
                case NetworkInterfaceType.Wwanpp2:
                    paths.Add(new("Date mobile (WWAN)", $"modem celular {label}, {(up ? "pornit" : "prezent, oprit")}", false, up)); break;
                case NetworkInterfaceType.Ppp:
                    paths.Add(new("VPN / acces telefonic (PPP)", $"{label}, {(up ? "conectat" : "prezent, deconectat")}", false, up)); break;
                case NetworkInterfaceType.Tunnel when up && !ni.Description.Contains("isatap", StringComparison.OrdinalIgnoreCase):
                    paths.Add(new("Tunel", $"interfață tunel {label}, pornită", false, true)); break;
            }
            if (IsVpnAdapterName(ni.Description) && ni.NetworkInterfaceType is not (NetworkInterfaceType.Ppp or NetworkInterfaceType.Wireless80211))
                paths.Add(new("VPN", $"adaptor VPN {label}, {(up ? "pornit" : "prezent, oprit")}", false, up));
            if (!up) continue;
            var ip = ni.GetIPProperties();
            foreach (var gw in ip.GatewayAddresses)
                if (gw.Address is { } g && !IsUnspecified(g)) paths.Add(new("Gateway", $"{g} pe {ni.Name}", IsInternal(g), true));
            foreach (var dns in ip.DnsAddresses)
                paths.Add(new("Server DNS", $"{dns} pe {ni.Name}", IsInternal(dns), true));
        }
    }

    internal static bool IsVpnAdapterName(string description)
    {
        foreach (var k in new[] { "VPN", "WireGuard", "OpenVPN", "TAP-Windows", "Wintun", "AnyConnect", "GlobalProtect", "Fortinet", "WAN Miniport (IKEv2)", "WAN Miniport (SSTP)", "WAN Miniport (L2TP)", "WAN Miniport (PPTP)" })
            if (description.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ---- registry: static and leftover DHCP configuration of every interface (also adapters that are disabled now), persistent routes --

    private static void StaticConfiguration(List<EgressPathFact> paths)
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
        if (root is not null)
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                if (k is null) continue;
                foreach (var gw in Strings(k.GetValue("DefaultGateway")).Concat(Strings(k.GetValue("DhcpDefaultGateway"))))
                    if (IPAddress.TryParse(gw, out var g) && !IsUnspecified(g) && !paths.Any(p => p.Kind == "Gateway" && p.Description.StartsWith(g + " ", StringComparison.Ordinal)))
                        paths.Add(new("Gateway", $"{g} (configurat pe interfața {name})", IsInternal(g), false));
                foreach (var dns in Strings(k.GetValue("NameServer")).Concat(Strings(k.GetValue("DhcpNameServer"))).SelectMany(s => s.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)))
                    if (IPAddress.TryParse(dns, out var d) && !paths.Any(p => p.Kind == "Server DNS" && p.Description.StartsWith(d + " ", StringComparison.Ordinal)))
                        paths.Add(new("Server DNS", $"{d} (configurat pe interfața {name})", IsInternal(d), false));
            }
        using var routes = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\PersistentRoutes");
        if (routes is not null)
            foreach (var v in routes.GetValueNames())
            {
                var parts = v.Split(',');   // destination,mask,gateway,metric
                if (parts.Length >= 3 && IPAddress.TryParse(parts[2], out var gw))
                {
                    bool internalRoute = IsInternal(gw) == true && IPAddress.TryParse(parts[0], out var dst) && IsInternal(dst) == true;
                    paths.Add(new("Rută permanentă", v, internalRoute, false));
                }
            }
    }

    private static IEnumerable<string> Strings(object? v) => v switch
    {
        string[] a => a.Where(s => s.Length > 0),
        string s when s.Length > 0 => [s],
        _ => [],
    };

    // ---- proxies: WinINet (machine and current user) and WinHTTP ---------------------------------------------------------

    private static void Proxies(List<EgressPathFact> paths)
    {
        foreach (var (hive, label) in new[] { (Registry.LocalMachine, "mașină"), (Registry.CurrentUser, "utilizatorul curent") })
        {
            using var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (k is null) continue;
            if (Convert.ToString(k.GetValue("ProxyEnable")) == "1" && k.GetValue("ProxyServer") is string server && server.Length > 0)
                paths.Add(new("Proxy", $"WinINet ({label}): {server}", ProxyIsInternal(server), true));
            if (k.GetValue("AutoConfigURL") is string pac && pac.Length > 0)
                paths.Add(new("Proxy", $"script de configurare automată a proxy-ului ({label}): {pac}", null, true));
        }
        using var wh = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections");
        if (wh?.GetValue("WinHttpSettings") is byte[] blob && ParseWinHttpProxy(blob) is { } winHttp)
            paths.Add(new("Proxy", $"WinHTTP: {winHttp}", ProxyIsInternal(winHttp), true));
    }

    /// <summary>The proxy server string of a WinHttpSettings blob, or null when the blob says "direct access" or is not understood.</summary>
    internal static string? ParseWinHttpProxy(byte[] blob)
    {
        // Layout: DWORD version, DWORD counter, DWORD flags (0x1 direct, 0x2 proxy, 0x8 auto-detect), DWORD length, proxy string, DWORD length, bypass string.
        if (blob.Length < 16 || (blob[8] & 0x02) == 0) return null;
        int len = BitConverter.ToInt32(blob, 12);
        if (len <= 0 || 16 + len > blob.Length) return null;
        return Encoding.ASCII.GetString(blob, 16, len);
    }

    /// <summary>
    /// Proxy string ("host:port" or "http=host:port;https=host:port"). True when every host is an IP literal inside the internal ranges, false when
    /// any is an IP literal outside them, null when a host is a name (resolving it would send traffic).
    /// </summary>
    internal static bool? ProxyIsInternal(string proxy)
    {
        bool? result = true;
        foreach (var part in proxy.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var hostPort = part.Contains('=') ? part[(part.IndexOf('=') + 1)..] : part;
            hostPort = hostPort.Contains("://", StringComparison.Ordinal) ? hostPort[(hostPort.IndexOf("://", StringComparison.Ordinal) + 3)..] : hostPort;
            var host = hostPort.StartsWith('[') ? hostPort[1..hostPort.IndexOf(']')] : (hostPort.Count(c => c == ':') == 1 ? hostPort[..hostPort.IndexOf(':')] : hostPort);
            if (!IPAddress.TryParse(host, out var ip)) { result = null; continue; }
            if (IsInternal(ip) == false) return false;
        }
        return result;
    }

    // ---- RAS / VPN phonebooks ---------------------------------------------------------------------------------------------

    private static void RasProfiles(List<EgressPathFact> paths)
    {
        foreach (var file in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk"),
        })
            if (File.Exists(file))
                foreach (var name in PhonebookEntries(File.ReadAllText(file)))
                    paths.Add(new("Profil VPN / acces la distanță", $"{name} ({(file.Contains("ProgramData", StringComparison.OrdinalIgnoreCase) ? "toți utilizatorii" : "utilizatorul curent")})", false, false));
    }

    /// <summary>Entry names of a rasphone.pbk text: the lines of the form [Name].</summary>
    internal static IEnumerable<string> PhonebookEntries(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 2 && line[0] == '[' && line[^1] == ']') yield return line[1..^1];
        }
    }

    // ---- address classes --------------------------------------------------------------------------------------------------

    private static bool IsUnspecified(IPAddress a) => IPAddress.Any.Equals(a) || IPAddress.IPv6Any.Equals(a);

    /// <summary>True for loopback, RFC 1918, link-local, IPv6 unique-local and site-local addresses; false for anything else (public addresses, carrier-grade NAT included).</summary>
    internal static bool? IsInternal(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a)) return true;
        var b = a.GetAddressBytes();
        if (a.AddressFamily == AddressFamily.InterNetwork)
            return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        if (a.AddressFamily == AddressFamily.InterNetworkV6)
            return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || (b[0] & 0xFE) == 0xFC;
        return null;
    }
}
