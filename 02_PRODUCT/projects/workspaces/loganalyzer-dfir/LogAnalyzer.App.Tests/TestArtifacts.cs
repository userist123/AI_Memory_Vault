using System.IO;
using System.Buffers.Binary;
using System.Text;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Byte-exact synthetic artifacts for App.Tests (copies of the Dfir.Tests generators, which are not built in this worktree because of Defender):
/// a shell link (.lnk) and the smallest regf hive that Dfir.Core's RawRegistry reads. Deterministic, no host APIs, no real paths.
/// </summary>
internal static class TestArtifacts
{
    public static readonly DateTime LinkCreated = new(2026, 9, 19, 14, 54, 10, DateTimeKind.Utc);

    /// <summary>Header + LinkInfo (local path, volume) + working dir + arguments (Unicode) + TrackerDataBlock.</summary>
    public static byte[] Lnk(string localPath, string args, string workDir = "", string machine = "ws-test", uint volumeSerial = 0x1234ABCD, int driveType = 3)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(0x4C);
        w.Write(new Guid("00021401-0000-0000-C000-000000000046").ToByteArray());
        w.Write(0x2 | 0x10 | 0x20 | 0x80);
        w.Write(0x20);
        w.Write(LinkCreated.ToFileTimeUtc()); w.Write(LinkCreated.AddMinutes(1).ToFileTimeUtc()); w.Write(LinkCreated.AddMinutes(2).ToFileTimeUtc());
        w.Write(4096);
        w.Write(0); w.Write(1); w.Write((short)0); w.Write(new byte[10]);

        var label = Encoding.ASCII.GetBytes("OS\0");
        var volume = new byte[16 + label.Length];
        BinaryPrimitives.WriteInt32LittleEndian(volume, volume.Length);
        BinaryPrimitives.WriteInt32LittleEndian(volume.AsSpan(4), driveType);
        BinaryPrimitives.WriteUInt32LittleEndian(volume.AsSpan(8), volumeSerial);
        BinaryPrimitives.WriteInt32LittleEndian(volume.AsSpan(12), 16);
        label.CopyTo(volume, 16);
        var basePath = Encoding.ASCII.GetBytes(localPath + "\0");
        int headerSize = 0x1C, volOff = headerSize, baseOff = volOff + volume.Length, suffixOff = baseOff + basePath.Length;
        int size = suffixOff + 1;
        w.Write(size); w.Write(headerSize); w.Write(1); w.Write(volOff); w.Write(baseOff); w.Write(0); w.Write(suffixOff);
        w.Write(volume); w.Write(basePath); w.Write((byte)0);
        foreach (var s in new[] { workDir, args })
        {
            w.Write((ushort)s.Length);
            w.Write(Encoding.Unicode.GetBytes(s));
        }
        var tracker = new byte[0x60];
        BinaryPrimitives.WriteInt32LittleEndian(tracker, 0x60);
        BinaryPrimitives.WriteUInt32LittleEndian(tracker.AsSpan(4), 0xA0000003);
        BinaryPrimitives.WriteInt32LittleEndian(tracker.AsSpan(8), 0x58);
        Encoding.ASCII.GetBytes(machine).CopyTo(tracker, 16);
        w.Write(tracker);
        w.Write(0);
        return ms.ToArray();
    }
}

/// <summary>Smallest regf writer that RawRegistry reads: one hive bin, nk / lf / vk cells, inline or cell-stored values; ASCII key names only.</summary>
internal static class Regf
{
    public const int String = 1, ExpandString = 2, Binary = 3, Dword = 4;

    public sealed class Key(string name, DateTime lastWrite)
    {
        public string Name { get; } = name;
        public DateTime LastWrite { get; } = lastWrite;
        public List<Key> Children { get; } = [];
        public List<(string Name, int Type, byte[] Data)> Values { get; } = [];

        public Key Sub(string path, DateTime? lastWrite = null)
        {
            var k = this;
            foreach (var part in path.Split('\\'))
            {
                var c = k.Children.Find(x => x.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (c is null) { c = new Key(part, lastWrite ?? LastWrite); k.Children.Add(c); }
                k = c;
            }
            return k;
        }

        public Key Value(string name, int type, object data)
        {
            Values.Add((name, type, data switch
            {
                byte[] b => b,
                string s => Encoding.Unicode.GetBytes(s + "\0"),
                int i => BitConverter.GetBytes(i),
                _ => throw new ArgumentException("value data"),
            }));
            return this;
        }
    }

    public static byte[] Build(Key root, string fileName)
    {
        var bin = new List<byte>();
        bin.AddRange(Encoding.ASCII.GetBytes("hbin"));
        bin.AddRange(new byte[28]);
        int rootCell = WriteKey(bin, root, -1, isRoot: true);
        while ((bin.Count & 0xFFF) != 0) bin.Add(0);
        BinaryPrimitives.WriteInt32LittleEndian(Span(bin, 8, 4), bin.Count);
        var file = new byte[0x1000 + bin.Count];
        Encoding.ASCII.GetBytes("regf").CopyTo(file, 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8), 1);
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(12), root.LastWrite.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x14), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x18), 5);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x20), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x24), rootCell);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x28), bin.Count);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x2C), 1);
        Encoding.Unicode.GetBytes(fileName).CopyTo(file, 0x30);
        uint sum = 0;
        for (int i = 0; i < 0x1FC; i += 4) sum ^= BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(i));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x1FC), sum);
        bin.CopyTo(file, 0x1000);
        return file;
    }

    private static Span<byte> Span(List<byte> l, int at, int len) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(l).Slice(at, len);

    private static int Alloc(List<byte> bin, ReadOnlySpan<byte> payload)
    {
        int offset = bin.Count, size = (payload.Length + 4 + 7) & ~7;
        var cell = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(cell, -size);
        payload.CopyTo(cell.AsSpan(4));
        bin.AddRange(cell);
        return offset;
    }

    private static int WriteKey(List<byte> bin, Key k, int parent, bool isRoot)
    {
        var name = Encoding.ASCII.GetBytes(k.Name);
        var nk = new byte[0x4C + name.Length];
        int self = Alloc(bin, nk);
        var childOffsets = k.Children.Select(c => (c.Name, Off: WriteKey(bin, c, self, false))).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        int listOff = -1;
        if (childOffsets.Count > 0)
        {
            var lf = new byte[4 + 8 * childOffsets.Count];
            Encoding.ASCII.GetBytes("lf").CopyTo(lf, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(lf.AsSpan(2), (ushort)childOffsets.Count);
            for (int i = 0; i < childOffsets.Count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(lf.AsSpan(4 + 8 * i), childOffsets[i].Off);
                Encoding.ASCII.GetBytes(childOffsets[i].Name.PadRight(4)[..4]).CopyTo(lf, 8 + 8 * i);
            }
            listOff = Alloc(bin, lf);
        }
        int valListOff = -1;
        if (k.Values.Count > 0)
        {
            var offs = k.Values.Select(v => WriteValue(bin, v.Name, v.Type, v.Data)).ToArray();
            var list = new byte[4 * offs.Length];
            for (int i = 0; i < offs.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(list.AsSpan(4 * i), offs[i]);
            valListOff = Alloc(bin, list);
        }
        var p = Span(bin, self + 4, nk.Length);
        Encoding.ASCII.GetBytes("nk").CopyTo(p);
        BinaryPrimitives.WriteUInt16LittleEndian(p[2..], (ushort)(isRoot ? 0x2C : 0x20));
        BinaryPrimitives.WriteInt64LittleEndian(p[4..], k.LastWrite.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(p[0x10..], parent);
        BinaryPrimitives.WriteInt32LittleEndian(p[0x14..], childOffsets.Count);
        BinaryPrimitives.WriteInt32LittleEndian(p[0x1C..], listOff);
        BinaryPrimitives.WriteInt32LittleEndian(p[0x20..], -1);
        BinaryPrimitives.WriteInt32LittleEndian(p[0x24..], k.Values.Count);
        BinaryPrimitives.WriteInt32LittleEndian(p[0x28..], valListOff);
        BinaryPrimitives.WriteInt32LittleEndian(p[0x2C..], -1);
        BinaryPrimitives.WriteInt32LittleEndian(p[0x30..], -1);
        BinaryPrimitives.WriteUInt16LittleEndian(p[0x48..], (ushort)name.Length);
        name.CopyTo(p[0x4C..]);
        return self;
    }

    private static int WriteValue(List<byte> bin, string name, int type, byte[] data)
    {
        var n = Encoding.Latin1.GetBytes(name);
        var vk = new byte[0x14 + n.Length];
        Encoding.ASCII.GetBytes("vk").CopyTo(vk, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(vk.AsSpan(2), (ushort)n.Length);
        if (data.Length <= 4)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(vk.AsSpan(4), 0x80000000u | (uint)data.Length);
            data.CopyTo(vk, 8);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(vk.AsSpan(4), data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(vk.AsSpan(8), Alloc(bin, data));
        }
        BinaryPrimitives.WriteInt32LittleEndian(vk.AsSpan(0xC), type);
        BinaryPrimitives.WriteUInt16LittleEndian(vk.AsSpan(0x10), 1);
        n.CopyTo(vk, 0x14);
        return Alloc(bin, vk);
    }
}
