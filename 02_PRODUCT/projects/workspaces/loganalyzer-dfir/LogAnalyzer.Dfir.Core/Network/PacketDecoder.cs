using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace LogAnalyzer.Dfir.Network;

public readonly record struct L3L4(int IpVersion, string Src, string Dst, int Protocol, int SrcPort, int DstPort, int TcpFlags,
                                   int IpLength, ReadOnlyMemory<byte> Payload);

/// <summary>
/// Decodes Ethernet / VLAN / raw-IP and the native 802.11+LLC/SNAP frames that PktMon emits on Wi-Fi
/// while labelling them link type 1 (Ethernet). Supports IPv4/IPv6, TCP/UDP (spec §157).
/// </summary>
public static class PacketDecoder
{
    public static L3L4? Decode(int linkType, ReadOnlySpan<byte> p)
    {
        int et; int off;
        if (linkType == 1)
        {
            if (p.Length < 14) return null;
            et = BinaryPrimitives.ReadUInt16BigEndian(p[12..]);
            if (et is 0x0800 or 0x86DD or 0x0806 or 0x8100 or 0x88A8) { off = 14; }
            else
            {
                // native 802.11 data frame: FC type bits 0x0C == 0x08
                if ((p[0] & 0x0C) != 0x08) return null;
                int hl = 24 + ((p[1] & 0x03) == 0x03 ? 6 : 0) + ((p[0] & 0x80) != 0 ? 2 : 0);
                if (p.Length < hl + 8 || !p.Slice(hl, 6).SequenceEqual(new byte[] { 0xAA, 0xAA, 0x03, 0, 0, 0 })) return null;
                et = BinaryPrimitives.ReadUInt16BigEndian(p[(hl + 6)..]); off = hl + 8;
            }
            while (et is 0x8100 or 0x88A8 && p.Length >= off + 4) { et = BinaryPrimitives.ReadUInt16BigEndian(p[(off + 2)..]); off += 4; }
        }
        else if (linkType == 101 || linkType == 228 || linkType == 229)
        {
            if (p.Length < 1) return null;
            et = (p[0] >> 4) == 4 ? 0x0800 : 0x86DD; off = 0;
        }
        else return null;

        var ip = p[off..];
        int proto, ipLen; string src, dst; ReadOnlySpan<byte> l4; int ver;
        if (et == 0x0800 && ip.Length >= 20)
        {
            ver = 4; int ihl = (ip[0] & 0x0F) * 4; int tot = BinaryPrimitives.ReadUInt16BigEndian(ip[2..]);
            if (ihl < 20 || ip.Length < ihl) return null;
            proto = ip[9]; src = new IPAddress(ip.Slice(12, 4)).ToString(); dst = new IPAddress(ip.Slice(16, 4)).ToString();
            int end = tot >= ihl && tot <= ip.Length ? tot : ip.Length;
            l4 = ip[ihl..end]; ipLen = tot > 0 ? tot : ip.Length;
        }
        else if (et == 0x86DD && ip.Length >= 40)
        {
            ver = 6; int plen = BinaryPrimitives.ReadUInt16BigEndian(ip[4..]);
            proto = ip[6]; src = new IPAddress(ip.Slice(8, 16)).ToString(); dst = new IPAddress(ip.Slice(24, 16)).ToString();
            l4 = ip.Slice(40, Math.Min(plen, ip.Length - 40)); ipLen = plen + 40;
        }
        else return null;

        int sp = 0, dp = 0, flags = 0; ReadOnlySpan<byte> payload = default;
        if (proto == 6 && l4.Length >= 20)
        {
            sp = BinaryPrimitives.ReadUInt16BigEndian(l4); dp = BinaryPrimitives.ReadUInt16BigEndian(l4[2..]);
            int doff = (l4[12] >> 4) * 4; flags = l4[13] & 0x3F;
            payload = doff <= l4.Length ? l4[doff..] : default;
        }
        else if (proto == 17 && l4.Length >= 8)
        {
            sp = BinaryPrimitives.ReadUInt16BigEndian(l4); dp = BinaryPrimitives.ReadUInt16BigEndian(l4[2..]); payload = l4[8..];
        }
        return new L3L4(ver, src, dst, proto, sp, dp, flags, ipLen, payload.ToArray());
    }

    public sealed record DnsMessage(ushort Id, bool IsResponse, string RCode, IReadOnlyList<(string Name, string Type)> Questions, IReadOnlyList<string> Answers);

    private static readonly Dictionary<int, string> QTypes = new() { [1] = "A", [28] = "AAAA", [5] = "CNAME", [16] = "TXT", [15] = "MX", [12] = "PTR", [33] = "SRV", [65] = "HTTPS", [64] = "SVCB", [2] = "NS", [6] = "SOA" };
    private static readonly Dictionary<int, string> RCodes = new() { [0] = "NOERROR", [2] = "SERVFAIL", [3] = "NXDOMAIN", [5] = "REFUSED" };

    public static DnsMessage? ParseDns(ReadOnlySpan<byte> m)
    {
        if (m.Length < 12) return null;
        ushort id = BinaryPrimitives.ReadUInt16BigEndian(m); ushort flags = BinaryPrimitives.ReadUInt16BigEndian(m[2..]);
        int qd = BinaryPrimitives.ReadUInt16BigEndian(m[4..]), an = BinaryPrimitives.ReadUInt16BigEndian(m[6..]);
        var qs = new List<(string, string)>(); var ans = new List<string>();
        int o = 12;
        try
        {
            for (int i = 0; i < qd; i++)
            {
                var name = DnsName(m, ref o, 0); int qt = BinaryPrimitives.ReadUInt16BigEndian(m[o..]); o += 4;
                qs.Add((name, QTypes.GetValueOrDefault(qt, qt.ToString())));
            }
            for (int i = 0; i < an; i++)
            {
                DnsName(m, ref o, 0);
                int t = BinaryPrimitives.ReadUInt16BigEndian(m[o..]); int rl = BinaryPrimitives.ReadUInt16BigEndian(m[(o + 8)..]); o += 10;
                var rd = m.Slice(o, rl);
                if (t == 1 && rl == 4) ans.Add(new IPAddress(rd).ToString());
                else if (t == 28 && rl == 16) ans.Add(new IPAddress(rd).ToString());
                else if (t == 5) { int co = o; ans.Add("CNAME:" + DnsName(m, ref co, 0)); }
                o += rl;
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException) { /* truncated DNS message: keep what was decoded */ }
        return new DnsMessage(id, (flags & 0x8000) != 0, RCodes.GetValueOrDefault(flags & 0xF, (flags & 0xF).ToString()), qs, ans);
    }

    private static string DnsName(ReadOnlySpan<byte> m, ref int o, int depth)
    {
        var labels = new List<string>();
        while (o < m.Length && depth < 16)
        {
            int len = m[o];
            if (len == 0) { o++; return string.Join('.', labels); }
            if ((len & 0xC0) == 0xC0)
            {
                int ptr = BinaryPrimitives.ReadUInt16BigEndian(m[o..]) & 0x3FFF; o += 2;
                labels.Add(DnsName(m, ref ptr, depth + 1));
                return string.Join('.', labels);
            }
            labels.Add(Encoding.Latin1.GetString(m.Slice(o + 1, len))); o += 1 + len;
        }
        return string.Join('.', labels);
    }

    /// <summary>SNI (and ALPN) from a TLS ClientHello at the start of a TCP payload.</summary>
    public static (string Sni, string RecordVersion, string Alpn)? ParseTlsClientHello(ReadOnlySpan<byte> p)
    {
        if (p.Length < 43 || p[0] != 0x16 || p[1] != 3 || p[5] != 1) return null;
        try
        {
            int o = 9 + 2 + 32;
            o += 1 + p[o];
            o += 2 + BinaryPrimitives.ReadUInt16BigEndian(p[o..]);
            o += 1 + p[o];
            int end = o + 2 + BinaryPrimitives.ReadUInt16BigEndian(p[o..]); o += 2;
            string sni = "", alpn = "";
            while (o + 4 <= Math.Min(end, p.Length))
            {
                int et = BinaryPrimitives.ReadUInt16BigEndian(p[o..]), el = BinaryPrimitives.ReadUInt16BigEndian(p[(o + 2)..]); o += 4;
                if (et == 0 && el > 5) { int ln = BinaryPrimitives.ReadUInt16BigEndian(p[(o + 3)..]); sni = Encoding.Latin1.GetString(p.Slice(o + 5, ln)); }
                else if (et == 16 && el > 3) alpn = Encoding.Latin1.GetString(p.Slice(o + 3, p[o + 2]));
                o += el;
            }
            return (sni, $"0x{p[9]:X2}{p[10]:X2}", alpn);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException) { return null; } // truncated/segmented ClientHello
    }

    private static readonly string[] HttpMethods = ["GET ", "POST ", "PUT ", "HEAD ", "DELETE ", "OPTIONS ", "PATCH ", "CONNECT "];

    public static (string RequestLine, string Host, string UserAgent)? ParseHttpRequest(ReadOnlySpan<byte> p)
    {
        if (p.Length < 16) return null;
        var head = Encoding.Latin1.GetString(p[..Math.Min(p.Length, 4096)]);
        if (!HttpMethods.Any(m => head.StartsWith(m, StringComparison.Ordinal))) return null;
        var lines = head.Split("\r\n");
        string host = "", ua = "";
        foreach (var l in lines.Skip(1))
        {
            if (l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) host = l[5..].Trim();
            else if (l.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase)) ua = l[11..].Trim();
        }
        return (lines[0].Length > 300 ? lines[0][..300] : lines[0], host, ua);
    }
}
