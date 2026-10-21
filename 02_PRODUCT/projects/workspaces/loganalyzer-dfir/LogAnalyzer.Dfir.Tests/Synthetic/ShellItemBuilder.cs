using System.Buffers.Binary;
using System.Text;

namespace LogAnalyzer.Dfir.Tests.Synthetic;

/// <summary>Writes synthetic shell items and ItemID lists (no real paths or user names): enough of the format for the decoder tests.</summary>
public static class ShellItemBuilder
{
    public static readonly Guid ThisPc = new("20D04FE0-3AEA-1069-A2D8-08002B30309D");

    /// <summary>Extension block 0xBEEF0004: <paramref name="Version"/> 3, 7, 8 or 9.</summary>
    public sealed record Ext(int Version, string LongName, DateTime Created, DateTime Accessed, long Mft = 0, int Seq = 0);

    public static byte[] Wrap(byte type, byte[] body)
    {
        var b = new byte[3 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)b.Length);
        b[2] = type;
        body.CopyTo(b, 3);
        return b;
    }

    public static byte[] Root(Guid g, byte type = 0x1F)
    {
        var body = new byte[17];
        body[0] = 0x50;
        g.ToByteArray().CopyTo(body, 1);
        return Wrap(type, body);
    }

    public static byte[] Volume(string name)
    {
        var body = new byte[22];   // 25 bytes in total, as Windows writes it
        Encoding.ASCII.GetBytes(name).CopyTo(body, 0);
        return Wrap(0x2F, body);
    }

    public static ushort DosDate(DateTime d) => (ushort)(((d.Year - 1980) << 9) | (d.Month << 5) | d.Day);
    public static ushort DosTime(DateTime d) => (ushort)((d.Hour << 11) | (d.Minute << 5) | (d.Second / 2));

    private static void PutDos(List<byte> l, DateTime d)
    {
        l.AddRange(BitConverter.GetBytes(DosDate(d)));
        l.AddRange(BitConverter.GetBytes(DosTime(d)));
    }

    /// <summary>File entry (0x31 / 0x32 ASCII name, 0x35 / 0x36 UTF-16 name) with an optional extension block.</summary>
    public static byte[] FileEntry(byte type, string shortName, uint size, DateTime modified, Ext? ext = null)
    {
        var l = new List<byte> { 0, 0, type, 0 };
        l.AddRange(BitConverter.GetBytes(size));
        PutDos(l, modified);
        l.AddRange(new byte[2]);   // attributes
        if ((type & 0x04) != 0) { l.AddRange(Encoding.Unicode.GetBytes(shortName)); l.AddRange(new byte[2]); }
        else { l.AddRange(Encoding.ASCII.GetBytes(shortName)); l.Add(0); }
        if ((l.Count & 1) != 0) l.Add(0);
        if (ext is not null)
        {
            int start = l.Count;
            l.AddRange(new byte[2]);                                   // block size, patched below
            l.AddRange(BitConverter.GetBytes((ushort)ext.Version));
            l.AddRange([0x04, 0x00, 0xEF, 0xBE]);
            PutDos(l, ext.Created);
            PutDos(l, ext.Accessed);
            if (ext.Version == 3) l.AddRange(new byte[2]);             // name at +0x12
            else
            {
                l.AddRange(new byte[4]);                               // +0x10, +0x12
                var fileRef = new byte[8];
                BitConverter.GetBytes(ext.Mft).AsSpan(0, 6).CopyTo(fileRef);
                BitConverter.GetBytes((ushort)ext.Seq).CopyTo(fileRef, 6);
                l.AddRange(fileRef);                                   // +0x14
                l.AddRange(new byte[8]);                               // +0x1C
                l.AddRange(BitConverter.GetBytes((ushort)(ext.LongName.Length + 1)));   // +0x24
                l.AddRange(new byte[ext.Version switch { 8 => 4, 9 => 8, _ => 0 }]);
            }
            l.AddRange(Encoding.Unicode.GetBytes(ext.LongName));
            l.AddRange(new byte[2]);
            l.AddRange(new byte[2]);                                   // trailing offset field of the block
            int blockSize = l.Count - start;
            l[start] = (byte)blockSize; l[start + 1] = (byte)(blockSize >> 8);
            l.AddRange(BitConverter.GetBytes((ushort)start));          // item tail: offset of the extension block
        }
        var b = l.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)b.Length);
        return b;
    }

    /// <summary>ItemID list: the items, then the 0x0000 terminator (omitted with <paramref name="terminate"/> false).</summary>
    public static byte[] List(bool terminate, params byte[][] items)
    {
        var l = new List<byte>();
        foreach (var i in items) l.AddRange(i);
        if (terminate) l.AddRange(new byte[2]);
        return l.ToArray();
    }

    public static byte[] List(params byte[][] items) => List(true, items);

    public static byte[] Mru(params uint[] indexes)
    {
        var b = new byte[4 * indexes.Length];
        for (int i = 0; i < indexes.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4 * i), indexes[i]);
        return b;
    }
}
