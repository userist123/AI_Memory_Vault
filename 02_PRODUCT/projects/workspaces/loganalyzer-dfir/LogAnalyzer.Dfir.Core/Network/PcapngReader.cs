using System.Buffers.Binary;

namespace LogAnalyzer.Dfir.Network;

/// <summary>One captured frame: UTC time (null if the block carried none), link type, bytes, 1-based frame number.</summary>
public readonly record struct CapturedFrame(long FrameNumber, DateTime? TimestampUtc, int LinkType, ReadOnlyMemory<byte> Data);

/// <summary>
/// Streaming pcapng reader (SHB/IDB/EPB/SPB). Handles both endiannesses and per-interface timestamp
/// resolution. Never loads the whole file (spec §77). A capture that ends inside a block, or a block whose lengths contradict
/// its content, is an <see cref="InvalidDataException"/> raised after the frames before it were yielded (the caller keeps them
/// as a PARTIAL result); it is never read past silently and never loops.
/// </summary>
public static class PcapngReader
{
    private const uint MaxBlockLength = 64 * 1024 * 1024;

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
            int got = ReadExactly(fs, hdr);
            if (got == 0) yield break;                                                  // clean end: the previous block was the last
            if (got < 8) throw new InvalidDataException($"pcapng truncated: {got} bytes of a block header at offset {fs.Position - got}.");
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(hdr);
            if (type == 0x0A0D0D0A)
            {
                var bom = new byte[4];
                if (ReadExactly(fs, bom) < 4) throw new InvalidDataException("pcapng truncated inside a section header.");
                little = BinaryPrimitives.ReadUInt32LittleEndian(bom) == 0x1A2B3C4D;
                uint shbLen = U32(hdr, 4, little);
                // A length below the 12 bytes already consumed would move the reader backwards, onto this very header, and loop for ever.
                if (shbLen < 12 || shbLen > MaxBlockLength) throw new InvalidDataException($"Corrupt pcapng section header length {shbLen} at offset {fs.Position - 12}.");
                fs.Seek(shbLen - 12, SeekOrigin.Current);
                ifaces.Clear();
                continue;
            }
            type = U32(hdr, 0, little);
            uint len = U32(hdr, 4, little);
            if (len < 12 || len > MaxBlockLength) throw new InvalidDataException($"Corrupt pcapng block length {len} at offset {fs.Position - 8}.");
            var body = new byte[len - 12];
            if (ReadExactly(fs, body) < body.Length) throw new InvalidDataException($"pcapng truncated: block type {type} declares {len} bytes, the file ends inside it.");
            fs.Seek(4, SeekOrigin.Current); // trailing block length

            if (type == 1) // IDB
            {
                if (body.Length < 8) throw new InvalidDataException($"pcapng interface block of {len} bytes is too short.");
                int linkType = U16(body, 0, little);
                double res = 1e-6;
                int o = 8;
                while (o + 4 <= body.Length)
                {
                    int code = U16(body, o, little), olen = U16(body, o + 2, little);
                    if (code == 0) break;
                    if (o + 4 + olen > body.Length) throw new InvalidDataException($"pcapng interface option {code} of {olen} bytes runs past its block.");
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
                uint ifid = U32(body, 0, little);
                ulong ts = ((ulong)U32(body, 4, little) << 32) | U32(body, 8, little);
                int cap = (int)Math.Min(U32(body, 12, little), (uint)(body.Length - 20));
                var (lt, res) = ifid < ifaces.Count ? ifaces[(int)ifid] : (1, 1e-6);
                // A timestamp beyond the calendar is damaged data: the frame is kept, without a time.
                double ticks = ts * res * TimeSpan.TicksPerSecond;
                DateTime? t = ts == 0 || !(ticks >= 0 && ticks < DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks) ? null : DateTime.UnixEpoch.AddTicks((long)ticks);
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
