using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Dfir.Tests.Synthetic;

/// <summary>
/// The synthetic, redistributable regression corpus (stage 2, owner decision 7). Every artifact is generated here, byte for byte
/// the same on every run and every platform: fixed timestamps, no randomness, no host APIs. Nothing in it comes from a real
/// case. Because the bytes are deterministic, <see cref="Manifest"/> pins their SHA-256 and a test fails when a generator drifts.
///
/// What it is NOT: it is not the real incident corpus (that stays with the owner and runs through the release gate script), and
/// a green synthetic run is not a forensic validation. It catches parser regressions on every pull request.
///
/// Artifacts covered: Prefetch (SCCA v30, uncompressed), shell link (.lnk), registry hives (NTUSER, SOFTWARE; written by a
/// minimal regf writer, not by DiscUtils, which needs Windows ACL APIs), scheduled-task XML, and the EVTX file/chunk container.
/// Artifacts NOT covered: EVTX records (the parser reads them through the Windows EventLog API, which needs real binary XML),
/// SRUM (ESE database through esent.dll), MAM-compressed Prefetch (Windows decompression API), browser history (SQLite).
/// </summary>
public static class SyntheticCorpus
{
    public const string TraitName = "Category", TraitValue = "SyntheticCorpus";

    public static readonly DateTime Run1 = new(2026, 9, 19, 14, 56, 0, DateTimeKind.Utc);
    public static readonly DateTime Run2 = new(2026, 9, 19, 9, 12, 30, DateTimeKind.Utc);
    public static readonly DateTime Run3 = new(2026, 9, 18, 17, 3, 5, DateTimeKind.Utc);
    public static readonly DateTime HiveWritten = new(2026, 9, 19, 14, 55, 1, DateTimeKind.Utc);

    public static string Sha256(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    /// <summary>name -> bytes of every artifact in the corpus.</summary>
    public static IReadOnlyDictionary<string, byte[]> All() => new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["prefetch/SETUP.EXE-0A1B2C3D.pf"] = Prefetch(),
        ["lnk/SETUP.EXE.lnk"] = Lnk(),
        ["registry/NTUSER.DAT"] = NtUserHive(),
        ["registry/SOFTWARE"] = SoftwareHive(),
        ["tasks/orchestratormaintain"] = TaskXml(),
        ["evtx/container.evtx"] = EvtxContainer(3),
    };

    /// <summary>Writes the whole corpus under <paramref name="root"/> (for tools and the gate; tests build in memory).</summary>
    public static void Materialize(string root)
    {
        foreach (var (name, bytes) in All())
        {
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }

    // ---- Prefetch: SCCA v30, uncompressed ------------------------------------------------------------------------------------

    public const string PrefetchExe = "SETUP.EXE";
    public const uint PrefetchHash = 0x0A1B2C3D;
    public const int PrefetchRunCount = 7;
    public static readonly string[] PrefetchFiles =
    [
        @"\VOLUME{01dc1111aaaa2222-1234abcd}\WINDOWS\SYSTEM32\NTDLL.DLL",
        @"\VOLUME{01dc1111aaaa2222-1234abcd}\USERS\U\DOWNLOADS\TOOL_302044\SETUP.EXE",
        @"\VOLUME{01dc1111aaaa2222-1234abcd}\WINDOWS\SYSTEM32\KERNEL32.DLL",
    ];
    public const string PrefetchVolume = @"\VOLUME{01dc1111aaaa2222-1234abcd}";

    /// <summary>Header (version 30), 8 run times, metrics offset 0x130 (run count at 0xD0), file-name strings, one volume record.</summary>
    public static byte[] Prefetch(int version = 30)
    {
        var d = new byte[0x600];
        BinaryPrimitives.WriteInt32LittleEndian(d, version);
        Encoding.ASCII.GetBytes("SCCA").CopyTo(d, 4);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(12), d.Length);
        Encoding.Unicode.GetBytes(PrefetchExe).CopyTo(d, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(76), PrefetchHash);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(84), 0x130);               // metrics array offset (layout selector)
        var names = Encoding.Unicode.GetBytes(string.Join("\0", PrefetchFiles) + "\0");
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(100), 0x200);              // filename strings offset
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(104), names.Length);
        names.CopyTo(d, 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(108), 0x400);              // volume info offset
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(112), 1);
        var times = new[] { Run1, Run2, Run3 };
        for (int i = 0; i < times.Length; i++) BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(128 + 8 * i), times[i].ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0xD0), PrefetchRunCount);
        // Volume record (96 bytes): device path offset (relative to the volume block), length in characters, creation time, serial.
        var vol = d.AsSpan(0x400);
        BinaryPrimitives.WriteInt32LittleEndian(vol, 96);
        BinaryPrimitives.WriteInt32LittleEndian(vol[4..], PrefetchVolume.Length);
        BinaryPrimitives.WriteInt64LittleEndian(vol[8..], new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToFileTimeUtc());
        BinaryPrimitives.WriteUInt32LittleEndian(vol[16..], 0x1234ABCD);
        Encoding.Unicode.GetBytes(PrefetchVolume).CopyTo(d, 0x400 + 96);
        return d;
    }

    // ---- Shell link ------------------------------------------------------------------------------------------------------------

    public const string LnkTarget = @"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE";
    public const string LnkArgs = "/silent /x";
    public const string LnkWorkDir = @"C:\Users\u\Downloads\TOOL_302044";
    public const string LnkMachine = "synth-pc";

    public static byte[] Lnk() => LnkParserTests.BuildLnk(LnkTarget, LnkArgs, LnkWorkDir, LnkMachine);

    // ---- Scheduled task XML (UTF-16 LE with BOM, like System32\Tasks) --------------------------------------------------------

    public const string TaskCommand = @"C:\Users\u\AppData\Local\Synth\updater_core.exe";

    public static byte[] TaskXml()
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Date>2026-09-19T17:58:12.5+03:00</Date>
                <Author>SYNTH-PC\u</Author>
                <URI>\orchestratormaintain</URI>
              </RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>S-1-5-21-1-2-3-1001</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings><Hidden>true</Hidden><Enabled>true</Enabled></Settings>
              <Actions Context="Author">
                <Exec><Command>{TaskCommand}</Command><Arguments>-run silent</Arguments></Exec>
              </Actions>
            </Task>
            """;
        xml = xml.Replace("\r\n", "\n");   // raw-string literals take the source file's line endings; the bytes must not depend on them
        return [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(xml)];
    }

    // ---- EVTX container: file header + independent 64 KiB chunks with CRC32 -------------------------------------------------

    public const int EvtxRecordsPerChunk = 10;

    /// <summary>
    /// A structurally valid .evtx container: ElfFile header and <paramref name="chunks"/> ElfChnk chunks with correct header and
    /// record checksums. The record area is opaque filler, so the Windows EventLog API would not read it; EvtxRepair works on the
    /// container only, which is what this exercises.
    /// </summary>
    public static byte[] EvtxContainer(int chunks)
    {
        var f = new byte[4096 + 65536 * chunks];
        Encoding.ASCII.GetBytes("ElfFile\0").CopyTo(f, 0);
        BinaryPrimitives.WriteInt64LittleEndian(f.AsSpan(16), chunks - 1);
        BinaryPrimitives.WriteInt64LittleEndian(f.AsSpan(24), chunks * EvtxRecordsPerChunk + 1);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(42), (ushort)chunks);
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(124), Crc32.HashToUInt32(f.AsSpan(0, 120)));
        for (int i = 0; i < chunks; i++)
        {
            var c = f.AsSpan(4096 + 65536 * i, 65536);
            Encoding.ASCII.GetBytes("ElfChnk\0").CopyTo(c);
            long first = i * EvtxRecordsPerChunk + 1, last = first + EvtxRecordsPerChunk - 1;
            BinaryPrimitives.WriteInt64LittleEndian(c[8..], i);
            BinaryPrimitives.WriteInt64LittleEndian(c[16..], i);
            BinaryPrimitives.WriteInt64LittleEndian(c[24..], first);
            BinaryPrimitives.WriteInt64LittleEndian(c[32..], last);
            const int free = 512 + 2048;
            BinaryPrimitives.WriteUInt32LittleEndian(c[48..], free);
            for (int j = 512; j < free; j++) c[j] = (byte)(j * 31 + i);          // deterministic filler for the record area
            BinaryPrimitives.WriteUInt32LittleEndian(c[52..], Crc32.HashToUInt32(c[512..free]));
            var crc = new Crc32();
            crc.Append(c[..120]);
            crc.Append(c[128..512]);
            BinaryPrimitives.WriteUInt32LittleEndian(c[124..], crc.GetCurrentHashAsUInt32());
        }
        return f;
    }

    // ---- Registry hives (minimal regf writer) --------------------------------------------------------------------------------

    public static string Rot13(string s) => new(s.Select(c => c switch
    {
        >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
        >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
        _ => c,
    }).ToArray());

    private static byte[] UserAssistData(int runs, int focus, int focusMs, DateTime last)
    {
        var d = new byte[72];
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(4), runs);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(8), focus);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(12), focusMs);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(60), last.ToFileTimeUtc());
        return d;
    }

    public const string UserRunCommand = "\"C:\\Users\\u\\AppData\\Roaming\\synth\\agent.exe\" /quiet";
    public const string UserAssistProgram = @"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE";

    public static byte[] NtUserHive()
    {
        var root = new RegfBuilder.Key("ROOT", HiveWritten);
        var ua = root.Sub(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count");
        ua.Value(Rot13(UserAssistProgram), RegfBuilder.Binary, UserAssistData(4, 3, 61000, HiveWritten));
        ua.Value(Rot13(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe"), RegfBuilder.Binary, UserAssistData(2, 0, 0, HiveWritten.AddDays(-1)));
        ua.Value(Rot13("UEME_CTLSESSION"), RegfBuilder.Binary, new byte[1612]);
        root.Sub(@"Software\Microsoft\Windows\CurrentVersion\Run").Value("Updater", RegfBuilder.String, UserRunCommand);
        root.Sub(@"Software\Microsoft\Windows\CurrentVersion\RunOnce").Value("Once", RegfBuilder.String, @"C:\Windows\system32\cmd.exe /c echo");
        return RegfBuilder.Build(root, "NTUSER.DAT");
    }

    public static byte[] SoftwareHive()
    {
        var root = new RegfBuilder.Key("ROOT", HiveWritten);
        root.Sub(@"Microsoft\Windows\CurrentVersion\Run").Value("SecurityHealth", RegfBuilder.ExpandString, @"%windir%\system32\SecurityHealthSystray.exe");
        root.Sub(@"WOW6432Node\Microsoft\Windows\CurrentVersion\Run").Value("Agent", RegfBuilder.String, @"C:\ProgramData\agent\a.exe");
        var wl = root.Sub(@"Microsoft\Windows NT\CurrentVersion\Winlogon");
        wl.Value("Shell", RegfBuilder.String, "explorer.exe");
        wl.Value("Userinit", RegfBuilder.String, @"C:\Windows\system32\userinit.exe,C:\Users\Public\x.exe");
        root.Sub(@"Microsoft\Windows NT\CurrentVersion\Image File Execution Options\sethc.exe").Value("Debugger", RegfBuilder.String, @"C:\Windows\System32\cmd.exe");
        root.Sub(@"Microsoft\Windows NT\CurrentVersion\Image File Execution Options\notepad.exe").Value("UseFilter", RegfBuilder.Dword, 1);
        return RegfBuilder.Build(root, "SOFTWARE");
    }
}

/// <summary>
/// Smallest regf writer that Dfir.Core's RawRegistry reads: one hive bin, nk / lf / vk cells, inline or cell-stored values.
/// Deterministic (no clock, no random). Not a general hive writer: no security descriptors, no big data, ASCII key names only.
/// </summary>
public static class RegfBuilder
{
    public const int String = 1, ExpandString = 2, Binary = 3, Dword = 4;

    public sealed class Key(string name, DateTime lastWrite)
    {
        public string Name { get; } = name;
        public DateTime LastWrite { get; } = lastWrite;
        public List<Key> Children { get; } = [];
        public List<(string Name, int Type, byte[] Data)> Values { get; } = [];

        /// <summary>Creates (or finds) the keys of a backslash path below this one and returns the last.</summary>
        public Key Sub(string path)
        {
            var k = this;
            foreach (var part in path.Split('\\'))
            {
                var c = k.Children.Find(x => x.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (c is null) { c = new Key(part, LastWrite); k.Children.Add(c); }
                k = c;
            }
            return k;
        }

        public void Value(string name, int type, object data) => Values.Add((name, type, data switch
        {
            byte[] b => b,
            string s => Encoding.Unicode.GetBytes(s + "\0"),
            int i => BitConverter.GetBytes(i),
            _ => throw new ArgumentException("value data"),
        }));
    }

    public static byte[] Build(Key root, string fileName)
    {
        var bin = new List<byte>();
        bin.AddRange(Encoding.ASCII.GetBytes("hbin"));
        bin.AddRange(new byte[28]);                                    // offset, size (patched) and padding up to the first cell at 0x20
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

    /// <summary>Allocates a cell holding <paramref name="payload"/>; returns its offset relative to the first hive bin.</summary>
    private static int Alloc(List<byte> bin, ReadOnlySpan<byte> payload)
    {
        int offset = bin.Count, size = (payload.Length + 4 + 7) & ~7;
        var cell = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(cell, -size);          // negative: allocated
        payload.CopyTo(cell.AsSpan(4));
        bin.AddRange(cell);
        return offset;
    }

    private static int WriteKey(List<byte> bin, Key k, int parent, bool isRoot)
    {
        var name = Encoding.ASCII.GetBytes(k.Name);
        var nk = new byte[0x4C + name.Length];
        int self = Alloc(bin, nk);                                      // reserve the cell now, fill the payload when the children exist

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

        // Patch the reserved nk cell (payload starts 4 bytes after the cell start).
        var p = Span(bin, self + 4, nk.Length);
        Encoding.ASCII.GetBytes("nk").CopyTo(p);
        BinaryPrimitives.WriteUInt16LittleEndian(p[2..], (ushort)(isRoot ? 0x2C : 0x20));   // compressed (ASCII) name; root key flags
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
        BinaryPrimitives.WriteUInt16LittleEndian(vk.AsSpan(0x10), 1);   // name stored as ASCII
        n.CopyTo(vk, 0x14);
        return Alloc(bin, vk);
    }
}
