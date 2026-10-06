using System.IO.Hashing;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Network;

/// <summary>
/// PCAPNG → normalized network events (spec §34-§41, §56): DNS queries/responses, TLS ClientHello SNI,
/// cleartext HTTP requests, and one summary event per flow with packet/byte counts. Process attribution is
/// NOT inferred here (packets carry no PID); it is added by correlation with socket snapshots/ETW.
/// </summary>
public sealed class PcapngParser : EvidenceParserBase
{
    public override string Name => "PcapngParser";
    public override string Version => "1.0";
    public override bool CanParse(EvidenceItem item) => item.SourceType == "pcapng" || item.StoredPath.EndsWith(".pcapng", StringComparison.OrdinalIgnoreCase);

    private sealed class Flow
    {
        public DateTime? First, Last; public long Packets, Bytes; public int Syn, SynAck, Rst, Fin; public long FirstFrame;
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var flows = new Dictionary<(string Proto, string Client, int CPort, string Server, int SPort), Flow>();
        var recent = new Dictionary<ulong, DateTime?>();
        long duplicates = 0, undecoded = 0;
        try
        {
            ReadFrames(item, fullPath, sink, result, ct, flows, recent, ref duplicates, ref undecoded);
        }
        finally
        {
            // Flows accumulated so far are evidence even if reading stopped early (result becomes PARTIAL).
            EmitFlows(item, sink, result, flows);
            if (duplicates > 0 || undecoded > 0)
                result.Gaps.Add(new EvidenceGap("PCAP frames", EvidenceStatus.Partial, $"{duplicates} duplicate frames dropped (multi-component capture), {undecoded} frames not decodable (non-IP or unsupported link type)",
                                                "Byte counts exclude dropped duplicates", "", "n/a"));
        }
    }

    private static void ReadFrames(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct,
                                   Dictionary<(string Proto, string Client, int CPort, string Server, int SPort), Flow> flows,
                                   Dictionary<ulong, DateTime?> recent, ref long duplicates, ref long undecoded)
    {
        foreach (var f in PcapngReader.Read(fullPath, ct))
        {
            var span = f.Data.Span;
            ulong h = XxHash3.HashToUInt64(span);
            if (recent.TryGetValue(h, out var prev) && prev is not null && f.TimestampUtc is not null && Math.Abs((f.TimestampUtc.Value - prev.Value).TotalMilliseconds) < 2)
            { duplicates++; continue; } // PktMon may log the same frame at several components
            recent[h] = f.TimestampUtc;
            if (recent.Count > 50_000) recent.Clear();

            var d = PacketDecoder.Decode(f.LinkType, span);
            if (d is not { } p) { undecoded++; continue; }
            string proto = p.Protocol switch { 6 => "TCP", 17 => "UDP", 1 => "ICMP", 58 => "ICMPv6", _ => p.Protocol.ToString() };
            bool fromClient = (p.Protocol == 6 && (p.TcpFlags & 0x12) == 0x02) || p.SrcPort > p.DstPort || (p.SrcPort == p.DstPort && string.CompareOrdinal(p.Src, p.Dst) > 0);
            var key = fromClient ? (proto, p.Src, p.SrcPort, p.Dst, p.DstPort) : (proto, p.Dst, p.DstPort, p.Src, p.SrcPort);
            if (!flows.TryGetValue(key, out var fl)) flows[key] = fl = new Flow { FirstFrame = f.FrameNumber };
            fl.First ??= f.TimestampUtc; fl.Last = f.TimestampUtc ?? fl.Last; fl.Packets++; fl.Bytes += p.IpLength;
            if (p.Protocol == 6)
            {
                if ((p.TcpFlags & 0x12) == 0x02) fl.Syn++;
                if ((p.TcpFlags & 0x12) == 0x12) fl.SynAck++;
                if ((p.TcpFlags & 0x04) != 0) fl.Rst++;
                if ((p.TcpFlags & 0x01) != 0) fl.Fin++;
            }

            var payload = p.Payload.Span;
            if ((p.SrcPort == 53 || p.DstPort == 53) && payload.Length > 0)
            {
                var dns = PacketDecoder.ParseDns(p.Protocol == 6 && payload.Length > 2 ? payload[2..] : payload);
                if (dns is not null)
                    foreach (var (name, type) in dns.Questions)
                    {
                        Emit(sink, result, item, f, "DNS", p, dns.IsResponse
                            ? $"DNS response {name} {type} → {dns.RCode} {string.Join(", ", dns.Answers)}"
                            : $"DNS query {name} {type}",
                            dnsName: name, fields: new() { ["Kind"] = dns.IsResponse ? "response" : "query", ["QType"] = type, ["RCode"] = dns.IsResponse ? dns.RCode : "", ["Answers"] = string.Join(";", dns.Answers), ["TxId"] = dns.Id.ToString() });
                    }
            }
            else if (p.Protocol == 6 && payload.Length > 0)
            {
                if (PacketDecoder.ParseTlsClientHello(payload) is { } tls)
                    Emit(sink, result, item, f, "TLS", p, $"TLS ClientHello SNI={(tls.Sni.Length > 0 ? tls.Sni : "(none)")} → {p.Dst}:{p.DstPort}",
                         dnsName: tls.Sni, fields: new() { ["SNI"] = tls.Sni, ["RecordVersion"] = tls.RecordVersion, ["ALPN"] = tls.Alpn });
                else if (PacketDecoder.ParseHttpRequest(payload) is { } http)
                    Emit(sink, result, item, f, "HTTP", p, $"HTTP {http.RequestLine} Host={http.Host}",
                         dnsName: http.Host, fields: new() { ["RequestLine"] = http.RequestLine, ["HTTPHost"] = http.Host, ["UserAgent"] = http.UserAgent });
            }
            // QUIC (UDP/443): the SNI sits inside encrypted Initial packets, so QUIC is reported at flow level only.
        }
    }

    private static void EmitFlows(EvidenceItem item, IEventSink sink, ParseResult result,
                                  Dictionary<(string Proto, string Client, int CPort, string Server, int SPort), Flow> flows)
    {
        foreach (var (k, v) in flows)
        {
            sink.Add(new TimelineEvent
            {
                Time = v.First is DateTime t ? Timestamp.FromUtc(t, t.ToString("o"), "pcapng EPB timestamp") : Timestamp.Unknown(),
                Source = "PCAP:flow",
                EvidenceId = item.EvidenceId,
                Provider = "PcapngParser",
                RemoteIp = k.Server,
                RemotePort = k.SPort,
                Summary = $"{k.Proto} flow {k.Client}:{k.CPort} → {k.Server}:{k.SPort} ({IpClassifier.Classify(k.Server)}) packets={v.Packets} bytes={v.Bytes}",
                TimeSemantics = "flow first seen",
                TemporalType = item.TemporalType == TemporalType.Unknown ? TemporalType.Live : item.TemporalType,
                Classification = Classification.Direct,
                Confidence = Confidence.High,
                Locator = $"frame={v.FirstFrame}",
                Fields =
                {
                    ["Protocol"] = k.Proto, ["ClientIP"] = k.Client, ["ClientPort"] = k.CPort.ToString(),
                    ["ServerIP"] = k.Server, ["ServerPort"] = k.SPort.ToString(), ["ServerScope"] = IpClassifier.Classify(k.Server).ToString(),
                    ["FirstSeenUtc"] = v.First?.ToString("o") ?? "", ["LastSeenUtc"] = v.Last?.ToString("o") ?? "",
                    ["Packets"] = v.Packets.ToString(), ["Bytes"] = v.Bytes.ToString(),
                    ["SYN"] = v.Syn.ToString(), ["SYNACK"] = v.SynAck.ToString(), ["RST"] = v.Rst.ToString(), ["FIN"] = v.Fin.ToString(),
                },
            });
            result.Records++;
        }
    }

    private static void Emit(IEventSink sink, ParseResult result, EvidenceItem item, CapturedFrame f, string kind, L3L4 p, string summary, string dnsName, Dictionary<string, string> fields)
    {
        fields["SrcIP"] = p.Src; fields["SrcPort"] = p.SrcPort.ToString(); fields["DstIP"] = p.Dst; fields["DstPort"] = p.DstPort.ToString();
        var e = new TimelineEvent
        {
            Time = f.TimestampUtc is DateTime t ? Timestamp.FromUtc(t, t.ToString("o"), "pcapng EPB timestamp") : Timestamp.Unknown(),
            Source = "PCAP:" + kind,
            EvidenceId = item.EvidenceId,
            Provider = "PcapngParser",
            RemoteIp = p.Dst,
            RemotePort = p.DstPort,
            Dns = dnsName,
            Summary = summary,
            TimeSemantics = "packet captured",
            TemporalType = item.TemporalType == TemporalType.Unknown ? TemporalType.Live : item.TemporalType,
            Classification = Classification.Direct,
            Confidence = Confidence.High,
            Locator = $"frame={f.FrameNumber}",
        };
        foreach (var kv in fields) e.Fields[kv.Key] = kv.Value;
        sink.Add(e);
        result.Records++;
    }
}
