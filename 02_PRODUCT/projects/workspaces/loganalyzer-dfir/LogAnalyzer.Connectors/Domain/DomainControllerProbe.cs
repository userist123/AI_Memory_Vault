using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.Connectors.Domain;

/// <summary>Where a resolved domain-controller address may receive the probe's TCP connection.</summary>
public enum DcAddressClass
{
    /// <summary>Private (RFC 1918) or IPv6 unique-local (fc00::/7): the probe may connect.</summary>
    Allowed,
    /// <summary>Loopback, link-local or unspecified: not a domain controller of the LAN; the probe never connects.</summary>
    Excluded,
    /// <summary>Anything else (public, carrier-grade NAT, documentation ranges): the probe never connects, and N13 is non-conform.</summary>
    Public,
}

/// <summary>
/// Active, read-only check N13 of the Network edition.
/// <para>
/// Locating the domain controller uses Windows' normal domain-locator traffic, the same the station sends at every logon: <c>DsGetDcName</c>
/// (Netlogon) queries the CONFIGURED DNS server for the SRV record _ldap._tcp.dc._msdcs.&lt;domain&gt; and pings the candidate controllers over
/// UDP 389, and the controller's name is then resolved through the same DNS server. Those packets leave before any address is known, so they
/// cannot be filtered by address. The probe's OWN traffic is a single TCP connection to LDAP (389), opened only to a private address (RFC 1918 or
/// fc00::/7), without credentials and without sending data, and closed at once. If any resolved address is public, the probe opens no TCP
/// connection and N13 is non-conform.
/// </para>
/// <para>
/// The whole probe (locator, name resolution, connection) runs under one overall deadline; a stalled locator or DNS server is abandoned on a
/// background task and never blocks the control. It runs only when the single network gate (<see cref="NetworkPolicy"/>) allows traffic.
/// This type lives in LogAnalyzer.Connectors, which the Standalone edition does not contain.
/// </para>
/// </summary>
public sealed class DomainControllerProbe : IDomainControllerProbe
{
    public const int LdapPort = 389;
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    /// <summary>Overall limit for locator + name resolution + connection.</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(10);
    private const string Feature = "Verificarea controlerului de domeniu (N13)";

    private readonly Func<string, (string Name, IReadOnlyList<IPAddress> Addresses, string Via)?> _locate;
    private readonly Func<IPAddress, CancellationToken, bool> _connect;
    private readonly Func<bool> _networkAllowed;
    private readonly TimeSpan _deadline;

    public DomainControllerProbe() : this(Locate, Connect, null) { }

    /// <summary>For tests: the location, the connection, the gate and the deadline can be replaced; by default the gate is <see cref="NetworkPolicy"/>.</summary>
    internal DomainControllerProbe(Func<string, (string Name, IReadOnlyList<IPAddress> Addresses, string Via)?> locate, Func<IPAddress, CancellationToken, bool> connect,
        Func<bool>? networkAllowed = null, TimeSpan? deadline = null)
    {
        _locate = locate; _connect = connect; _deadline = deadline ?? DefaultDeadline;
        _networkAllowed = networkAllowed ?? (() => { try { NetworkPolicy.EnsureAllowed(Feature); return true; } catch (NetworkBlockedException) { return false; } });
    }

    private static DomainControllerProbeResult Cancelled(string name, IReadOnlyList<string> addresses) =>
        new(DcProbeOutcome.Cancelled, name, addresses, "verificarea a fost anulată.");

    public DomainControllerProbeResult Probe(string domain, CancellationToken ct)
    {
        if (!_networkAllowed())
            return new(DcProbeOutcome.Skipped, "", [], "modul de lucru este AirGapped (politica semnată), deci nu se trimite trafic.");
        if (string.IsNullOrWhiteSpace(domain))
            return new(DcProbeOutcome.Skipped, "", [], "numele domeniului nu este cunoscut.");
        if (ct.IsCancellationRequested) return Cancelled("", []);

        var started = DateTime.UtcNow;
        string TimedOutText() => $"controlerul de domeniu nu a răspuns în {_deadline.TotalSeconds:0.#} s (localizare, rezolvare de nume și conexiune).";

        (string Name, IReadOnlyList<IPAddress> Addresses, string Via)? found;
        try { found = Task.Run(() => _locate(domain)).WaitAsync(_deadline, ct).GetAwaiter().GetResult(); }
        catch (TimeoutException) { return new(DcProbeOutcome.TimedOut, "", [], TimedOutText()); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Cancelled("", []); }
        catch (Exception ex) when (ex is SocketException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        { return new(DcProbeOutcome.NotResolved, "", [], ex.Message); }
        if (found is not { } dc || dc.Addresses.Count == 0)
            return new(DcProbeOutcome.NotResolved, found?.Name ?? "", [], "Localizatorul de domeniu al Windows (DsGetDcName) nu a găsit un controler." + Via(found?.Via));

        var text = dc.Addresses.Select(a => a.ToString()).ToList();
        if (dc.Addresses.Any(a => Classify(a) == DcAddressClass.Public))
            return new(DcProbeOutcome.PublicAddress, dc.Name, text,
                "Sonda nu a deschis nicio conexiune TCP; localizarea a folosit doar traficul obișnuit al Windows (interogare DNS la serverul DNS configurat și pingul UDP Netlogon)." + Via(dc.Via));
        var target = dc.Addresses.FirstOrDefault(a => Classify(a) == DcAddressClass.Allowed);
        if (target is null)
            return new(DcProbeOutcome.NotResolved, dc.Name, text, "S-au întors doar adrese locale sau de legătură (loopback, link-local), la care sonda nu se conectează." + Via(dc.Via));

        var remaining = _deadline - (DateTime.UtcNow - started);
        if (remaining <= TimeSpan.Zero) return new(DcProbeOutcome.TimedOut, dc.Name, text, TimedOutText());
        bool ok;
        try { ok = Task.Run(() => _connect(target, ct)).WaitAsync(remaining, ct).GetAwaiter().GetResult(); }
        catch (TimeoutException) { return new(DcProbeOutcome.TimedOut, dc.Name, text, TimedOutText()); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Cancelled(dc.Name, text); }
        if (ct.IsCancellationRequested) return Cancelled(dc.Name, text);
        return ok
            ? new(DcProbeOutcome.Reachable, dc.Name, text, $"Conexiune TCP la {target}:{LdapPort} reușită (fără autentificare, închisă imediat)." + Via(dc.Via))
            : new(DcProbeOutcome.Unreachable, dc.Name, text, $"conexiunea TCP la {target}:{LdapPort} nu a reușit în {ConnectTimeout.TotalSeconds:0} secunde." + Via(dc.Via));
    }

    private static string Via(string? via) => string.IsNullOrWhiteSpace(via) ? "" : $" ({via.Trim()})";

    /// <summary>Only RFC 1918 and fc00::/7 may receive the TCP connection; loopback, link-local and unspecified are excluded; everything else is public.</summary>
    public static DcAddressClass Classify(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any)) return DcAddressClass.Excluded;
        var b = a.GetAddressBytes();
        if (a.AddressFamily == AddressFamily.InterNetwork)
        {
            if (b[0] == 169 && b[1] == 254) return DcAddressClass.Excluded;
            return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) ? DcAddressClass.Allowed : DcAddressClass.Public;
        }
        if (a.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (a.IsIPv6LinkLocal) return DcAddressClass.Excluded;
            return (b[0] & 0xFE) == 0xFC ? DcAddressClass.Allowed : DcAddressClass.Public;
        }
        return DcAddressClass.Public;
    }

    // ---- real location and connection --------------------------------------------------------------------------------------

    private static (string Name, IReadOnlyList<IPAddress> Addresses, string Via)? Locate(string domain)
    {
        const uint DS_DIRECTORY_SERVICE_REQUIRED = 0x10, DS_IS_DNS_NAME = 0x20000, DS_RETURN_DNS_NAME = 0x40000000;
        var dnsServers = ConfiguredDnsServers();
        int rc = DsGetDcName(null, domain, IntPtr.Zero, null, DS_DIRECTORY_SERVICE_REQUIRED | DS_IS_DNS_NAME | DS_RETURN_DNS_NAME, out var buf);
        if (rc != 0 || buf == IntPtr.Zero) return ("", [], dnsServers);
        try
        {
            var info = Marshal.PtrToStructure<DOMAIN_CONTROLLER_INFO>(buf);
            var name = (info.DomainControllerName ?? "").TrimStart('\\');
            var given = (info.DomainControllerAddress ?? "").TrimStart('\\');
            var addrs = new List<IPAddress>();
            if (IPAddress.TryParse(given, out var g)) addrs.Add(g);
            if (name.Length > 0)
                foreach (var a in Dns.GetHostAddresses(name))
                    if (!addrs.Contains(a)) addrs.Add(a);
            var via = string.Join("; ", new[] { given.Length > 0 ? $"adresa dată de localizator: {given}" : "", dnsServers }.Where(x => x.Length > 0));
            return (name, addrs, via);
        }
        finally { NetApiBufferFree(buf); }
    }

    /// <summary>The DNS servers configured on the adapters that are up (read locally, no traffic).</summary>
    private static string ConfiguredDnsServers()
    {
        try
        {
            var list = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().DnsAddresses).Select(a => a.ToString()).Distinct().ToList();
            return list.Count == 0 ? "" : "servere DNS configurate: " + string.Join(", ", list);
        }
        catch (NetworkInformationException) { return ""; }
    }

    private static bool Connect(IPAddress target, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ConnectTimeout);
        using var client = new TcpClient(target.AddressFamily);
        try { client.ConnectAsync(target, LdapPort, cts.Token).AsTask().GetAwaiter().GetResult(); return client.Connected; }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }   // own timeout; a cancelled run propagates
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOMAIN_CONTROLLER_INFO
    {
        public string? DomainControllerName;
        public string? DomainControllerAddress;
        public uint DomainControllerAddressType;
        public Guid DomainGuid;
        public string? DomainName;
        public string? DnsForestName;
        public uint Flags;
        public string? DcSiteName;
        public string? ClientSiteName;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int DsGetDcName(string? computerName, string domainName, IntPtr domainGuid, string? siteName, uint flags, out IntPtr domainControllerInfo);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
