using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.Connectors.Domain;

/// <summary>Where a resolved domain-controller address may be contacted.</summary>
public enum DcAddressClass
{
    /// <summary>Private (RFC 1918) or IPv6 unique-local (fc00::/7): may be contacted.</summary>
    Allowed,
    /// <summary>Loopback, link-local or unspecified: not a domain controller of the LAN; never contacted.</summary>
    Excluded,
    /// <summary>Anything else (public, carrier-grade NAT, documentation ranges): never contacted, and N13 is non-conform.</summary>
    Public,
}

/// <summary>
/// Active, read-only check N13 of the Network edition: locates the domain controller of the joined domain (DsGetDcName, which uses the DNS SRV
/// record _ldap._tcp.dc._msdcs.&lt;domain&gt;), resolves its addresses and opens ONE TCP connection to LDAP (389) at the first private address,
/// with a short timeout. Nothing is sent on the connection (no bind, no credentials); it is closed at once. If any resolved address is public,
/// nothing is contacted. The probe runs only when the single network gate (<see cref="NetworkPolicy"/>) allows it.
/// This type lives in LogAnalyzer.Connectors, which the Standalone edition does not contain.
/// </summary>
public sealed class DomainControllerProbe : IDomainControllerProbe
{
    public const int LdapPort = 389;
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private const string Feature = "Verificarea controlerului de domeniu (N13)";

    private readonly Func<string, (string Name, IReadOnlyList<IPAddress> Addresses)?> _locate;
    private readonly Func<IPAddress, CancellationToken, bool> _connect;
    private readonly Func<bool> _networkAllowed;

    public DomainControllerProbe() : this(Locate, Connect, null) { }

    /// <summary>For tests: the location, the connection and the gate can be replaced; by default the gate is <see cref="NetworkPolicy"/>.</summary>
    internal DomainControllerProbe(Func<string, (string Name, IReadOnlyList<IPAddress> Addresses)?> locate, Func<IPAddress, CancellationToken, bool> connect,
        Func<bool>? networkAllowed = null)
    {
        _locate = locate; _connect = connect;
        _networkAllowed = networkAllowed ?? (() => { try { NetworkPolicy.EnsureAllowed(Feature); return true; } catch (NetworkBlockedException) { return false; } });
    }

    public DomainControllerProbeResult Probe(string domain, CancellationToken ct)
    {
        if (!_networkAllowed())
            return new(DcProbeOutcome.Skipped, "", [], "modul de lucru este AirGapped (politica semnată), deci nu se trimite trafic.");
        if (string.IsNullOrWhiteSpace(domain))
            return new(DcProbeOutcome.Skipped, "", [], "numele domeniului nu este cunoscut.");

        (string Name, IReadOnlyList<IPAddress> Addresses)? found;
        try { found = _locate(domain); }
        catch (Exception ex) when (ex is SocketException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
        { return new(DcProbeOutcome.NotResolved, "", [], ex.Message); }
        if (found is not { } dc || dc.Addresses.Count == 0)
            return new(DcProbeOutcome.NotResolved, found?.Name ?? "", [], "DsGetDcName nu a găsit un controler de domeniu.");

        var text = dc.Addresses.Select(a => a.ToString()).ToList();
        if (dc.Addresses.Any(a => Classify(a) == DcAddressClass.Public))
            return new(DcProbeOutcome.PublicAddress, dc.Name, text, "Din motive de siguranță, nicio adresă nu a fost contactată.");
        var target = dc.Addresses.FirstOrDefault(a => Classify(a) == DcAddressClass.Allowed);
        if (target is null)
            return new(DcProbeOutcome.NotResolved, dc.Name, text, "S-au întors doar adrese locale sau de legătură (loopback, link-local), care nu sunt contactate.");
        return _connect(target, ct)
            ? new(DcProbeOutcome.Reachable, dc.Name, text, $"Conexiune TCP la {target}:{LdapPort} reușită (fără autentificare, închisă imediat).")
            : new(DcProbeOutcome.Unreachable, dc.Name, text, $"conexiunea TCP la {target}:{LdapPort} nu a reușit în {ConnectTimeout.TotalSeconds:0} secunde.");
    }

    /// <summary>Only RFC 1918 and fc00::/7 may be contacted; loopback, link-local and unspecified are excluded; everything else is public.</summary>
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

    private static (string Name, IReadOnlyList<IPAddress> Addresses)? Locate(string domain)
    {
        const uint DS_DIRECTORY_SERVICE_REQUIRED = 0x10, DS_IS_DNS_NAME = 0x20000, DS_RETURN_DNS_NAME = 0x40000000;
        int rc = DsGetDcName(null, domain, IntPtr.Zero, null, DS_DIRECTORY_SERVICE_REQUIRED | DS_IS_DNS_NAME | DS_RETURN_DNS_NAME, out var buf);
        if (rc != 0 || buf == IntPtr.Zero) return null;
        try
        {
            var info = Marshal.PtrToStructure<DOMAIN_CONTROLLER_INFO>(buf);
            var name = (info.DomainControllerName ?? "").TrimStart('\\');
            var addrs = new List<IPAddress>();
            if (IPAddress.TryParse((info.DomainControllerAddress ?? "").TrimStart('\\'), out var given)) addrs.Add(given);
            if (name.Length > 0)
                foreach (var a in Dns.GetHostAddresses(name))
                    if (!addrs.Contains(a)) addrs.Add(a);
            return (name, addrs);
        }
        finally { NetApiBufferFree(buf); }
    }

    private static bool Connect(IPAddress target, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ConnectTimeout);
        using var client = new TcpClient(target.AddressFamily);
        try { client.ConnectAsync(target, LdapPort, cts.Token).AsTask().GetAwaiter().GetResult(); return client.Connected; }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return false; }
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
