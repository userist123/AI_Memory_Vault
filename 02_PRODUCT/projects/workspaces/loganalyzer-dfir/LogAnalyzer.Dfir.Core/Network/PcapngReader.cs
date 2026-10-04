using System.Buffers.Binary;

namespace LogAnalyzer.Dfir.Network;

/// <summary>One captured frame: UTC time (null if the block carried none), link type, bytes, 1-based frame number.</summary>
public readonly record struct CapturedFrame(long FrameNumber, DateTime? TimestampUtc, int LinkType, ReadOnlyMemory<byte> Data);

/// <summary>
/// Streaming pcapng reader (SHB/IDB/EPB/SPB). Handles both endiannesses and per-interface timestamp
/// resolution. Never loads the whole file (spec §77).
/// </summary>
public static class PcapngReader
{
    public static IEnumerable<CapturedFrame> Read(string path, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
        var hdr = new byte[8];
        bool little = true;
        var ifaces = new List<(int LinkType, double TsRes)>();
        long frame = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (ReadExactly(fs, hdr) < 8) yield break;
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(hdr);
            if (type == 0x0A0D0D0A)
            {
                var bom = new byte[4];
                if (ReadExactly(fs, bom) < 4) yield break;
                little = BinaryPrimitives.ReadUInt32LittleEndian(bom) == 0x1A2B3C4D;
                uint shbLen = U32(hdr, 4, little);
                fs.Seek(shbLen - 12, SeekOrigin.Current);
                ifaces.Clear();
                continue;
            }
            type = U32(hdr, 0, little);
            uint len = U32(hdr, 4, little);
            if (len < 12 || len > 64 * 1024 * 1024) throw new InvalidDataException($"Corrupt pcapng block length {len} at offset {fs.Position - 8}.");
            var body = new byte[len - 12];
            if (ReadExactly(fs, body) < body.Length) yield break;
            fs.Seek(4, SeekOrigin.Current); // trailing block length

            if (type == 1) // IDB
            {
                int linkType = U16(body, 0, little);
                double res = 1e-6;
                int o = 8;
                while (o + 4 <= body.Length)
                {
                    int code = U16(body, o, little), olen = U16(body, o + 2, little);
                    if (code == 0) break;
                    if (code == 9 && olen >= 1)
                    {
                        byte v = body[o + 4];
                        res = (v & 0x80) != 0 ? Math.Pow(2, -(v & 0x7F)) : Math.Pow(10, -v);
                    }
                    o += 4 + ((olen + 3) & ~3);
                }
                ifaces.Add((linkType, res));
            }
            else if (type == 6 && body.Length >= 20) // EPB
            {
                int ifid = (int)U32(body, 0, little);
                ulong ts = ((ulong)U32(body, 4, little) << 32) | U32(body, 8, little);
                int cap = (int)Math.Min(U32(body, 12, little), (uint)(body.Length - 20));
                var (lt, res) = ifid < ifaces.Count ? ifaces[ifid] : (1, 1e-6);
                DateTime? t = ts == 0 ? null : DateTime.UnixEpoch.AddTicks((long)(ts * res * TimeSpan.TicksPerSecond));
                yield return new CapturedFrame(++frame, t, lt, body.AsMemory(20, cap));
            }
            else if (type == 3 && body.Length >= 4) // SPB (no timestamp)
            {
                var (lt, _) = ifaces.Count > 0 ? ifaces[0] : (1, 1e-6);
                yield return new CapturedFrame(++frame, null, lt, body.AsMemory(4));
            }
        }
    }

    private static int ReadExactly(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length) { int n = s.Read(buf, total, buf.Length - total); if (n == 0) break; total += n; }
        return total;
    }

    private static uint U32(byte[] b, int o, bool le) => le ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
    private static int U16(byte[] b, int o, bool le) => le ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));
}
