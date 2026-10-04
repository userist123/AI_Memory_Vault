using System.Net;
using System.Net.Sockets;

namespace LogAnalyzer.Dfir.Analysis;

public enum IpScope { Invalid, Unspecified, Loopback, LinkLocal, Private, Multicast, Broadcast, Documentation, Cgnat, External }

/// <summary>Context-free IP classification used to suppress false-positive IOCs (spec §38, §50, bug class E).</summary>
public static class IpClassifier
{
    public static IpScope Classify(string text)
    {
        if (!IPAddress.TryParse(text, out var ip)) return IpScope.Invalid;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return IpScope.Unspecified;
        if (IPAddress.IsLoopback(ip)) return IpScope.Loopback;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 255 && b[1] == 255 && b[2] == 255 && b[3] == 255) return IpScope.Broadcast;
            if (b[0] >= 224 && b[0] <= 239) return IpScope.Multicast;
            if (b[0] == 169 && b[1] == 254) return IpScope.LinkLocal;
            if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)) return IpScope.Private;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return IpScope.Cgnat;
            if ((b[0] == 192 && b[1] == 0 && b[2] == 2) || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113)) return IpScope.Documentation;
            if (b[0] == 0) return IpScope.Unspecified;
            return IpScope.External;
        }

        if (ip.IsIPv6Multicast) return IpScope.Multicast;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return IpScope.LinkLocal;
        var v6 = ip.GetAddressBytes();
        if ((v6[0] & 0xFE) == 0xFC) return IpScope.Private;                      // fc00::/7 unique local
        if (v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x0D && v6[3] == 0xB8) return IpScope.Documentation; // 2001:db8::/32
        return IpScope.External;
    }

    public static bool IsExternal(string text) => Classify(text) == IpScope.External;
}
