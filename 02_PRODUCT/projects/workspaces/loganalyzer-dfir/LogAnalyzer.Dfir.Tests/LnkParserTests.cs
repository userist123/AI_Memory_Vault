using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Shell link (.lnk, MS-SHLLINK): a synthetic link with exact values, and the real Recent links against the Windows shell.</summary>
public sealed class LnkParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_lnk_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Created = new(2026, 9, 19, 14, 54, 10, DateTimeKind.Utc);
    private static readonly DateTime Modified = new(2026, 9, 19, 14, 54, 11, DateTimeKind.Utc);

    public LnkParserTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-L", CaseId = "C", Source = "lnk", SourceType = "lnk", StoredPath = path };

    /// <summary>Header + LinkInfo (local path, volume) + working dir + arguments (Unicode) + TrackerDataBlock.</summary>
    public static byte[] BuildLnk(string localPath, string args, string workDir, string machine)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(0x4C);
        w.Write(new Guid("00021401-0000-0000-C000-000000000046").ToByteArray());
        w.Write(0x2 | 0x10 | 0x20 | 0x80);                 // HasLinkInfo, HasWorkingDir, HasArguments, IsUnicode
        w.Write(0x20);                                      // FILE_ATTRIBUTE_ARCHIVE
        w.Write(Created.ToFileTimeUtc()); w.Write(Created.AddMinutes(1).ToFileTimeUtc()); w.Write(Modified.ToFileTimeUtc());
        w.Write(4_587_520);                                 // target size
        w.Write(0); w.Write(1); w.Write((short)0); w.Write(new byte[10]);

        var label = Encoding.ASCII.GetBytes("OS\0");
        var volume = new byte[16 + label.Length];
        BinaryPrimitives.WriteInt32LittleEndian(volume, volume.Length);
        BinaryPrimitives.WriteInt32LittleEndian(volume.AsSpan(4), 3);            // DRIVE_FIXED
        BinaryPrimitives.WriteUInt32LittleEndian(volume.AsSpan(8), 0x1234ABCD);
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
        w.Write(0);                                          // TerminalBlock
        return ms.ToArray();
    }

    [Fact]
    public void Link_is_recognised_by_content()
    {
        var p = Path.Combine(_dir, "SETUP.EXE.lnk");
        File.WriteAllBytes(p, BuildLnk(@"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE", "", "", "marius-pc"));
        Assert.Equal("lnk", EvidenceFingerprint.Detect(p));
    }

    [Fact]
    public void Target_volume_arguments_times_and_machine_are_decoded()
    {
        var p = Path.Combine(_dir, "SETUP.EXE.lnk");
        File.WriteAllBytes(p, BuildLnk(@"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE", "/silent /x", @"C:\Users\u\Downloads\TOOL_302044", "marius-pc"));
        var sink = new ListSink();
        var r = new LnkParser().Parse(Item(p), p, sink, default);

        Assert.Equal(EvidenceStatus.Success, r.Status);
        var e = Assert.Single(sink.Events);
        Assert.Equal(@"C:\Users\u\Downloads\TOOL_302044\SETUP.EXE", e.Path);
        Assert.Equal("SETUP.EXE", e.Process);
        Assert.Equal("/silent /x", e.Fields["Arguments"]);
        Assert.Equal(@"C:\Users\u\Downloads\TOOL_302044", e.Fields["WorkingDirectory"]);
        Assert.Equal(new DateTimeOffset(Modified), e.Time.Utc);
        Assert.Contains("target", e.TimeSemantics);
        Assert.Equal(Created.ToString("o"), e.Fields["TargetCreatedUtc"]);
        Assert.Equal("4587520", e.Fields["TargetSize"]);
        Assert.Equal("FIXED", e.Fields["DriveType"]);
        Assert.Equal("1234ABCD", e.Fields["VolumeSerial"]);
        Assert.Equal("OS", e.Fields["VolumeLabel"]);
        Assert.Equal("marius-pc", e.Fields["MachineId"]);
    }

    [Fact]
    public void Truncated_or_foreign_files_fail()
    {
        var p = Path.Combine(_dir, "bad.lnk");
        File.WriteAllBytes(p, BuildLnk(@"C:\a.exe", "", "", "m")[..90]);
        Assert.Equal(EvidenceStatus.Failed, new LnkParser().Parse(Item(p), p, new ListSink(), default).Status);
        File.WriteAllBytes(p, new byte[200]);
        Assert.Equal(EvidenceStatus.Failed, new LnkParser().Parse(Item(p), p, new ListSink(), default).Status);
    }

    /// <summary>
    /// Every real Recent link with a LinkInfo path, compared with what the Windows shell reads from the same file
    /// (WScript.Shell CreateShortcut: read only, never saved or resolved).
    /// </summary>
    [CorpusFact("lnk")]
    public void Real_links_match_the_windows_shell()
    {
        var folder = Path.Combine(Corpus.Root, Corpus.S("lnk", "folder"));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        int compared = 0, parsed = 0, zeroFilled = 0, ansiLossy = 0;
        var mismatch = new List<string>();
        foreach (var f in Directory.EnumerateFiles(folder, "*.lnk"))
        {
            var sink = new ListSink();
            var r = new LnkParser().Parse(Item(f), f, sink, default);
            if (r.Status != EvidenceStatus.Success)
            {
                // The only acceptable failure: a file with no link in it (the corpus has zero-filled .lnk files).
                Assert.True(r.Status == EvidenceStatus.Failed && File.ReadAllBytes(f).All(b => b == 0), $"{Path.GetFileName(f)}: {r.Status} {r.Error}");
                zeroFilled++;
                continue;
            }
            parsed++;
            var e = Assert.Single(sink.Events);
            if (e.Fields["TargetSource"] != "LinkInfo") continue;
            var sc = shell.CreateShortcut(f);
            string target = sc.TargetPath, args = sc.Arguments, wd = sc.WorkingDirectory;
            compared++;
            if (!string.Equals(target, e.Path, StringComparison.OrdinalIgnoreCase))
            {
                // WshShortcut.TargetPath goes through the ANSI code page: "ș" comes back as "?", and a path with a character it
                // cannot represent at all comes back empty. Accept only those two lossy forms, against a non-ASCII parser path.
                bool lossyQ = target.Length == e.Path.Length && target.Zip(e.Path).All(p => char.ToUpperInvariant(p.First) == char.ToUpperInvariant(p.Second) || (p.First == '?' && p.Second > 0x7F));
                bool lossyEmpty = target.Length == 0 && e.Path.Any(ch => ch > 0x7F);
                if (lossyQ || lossyEmpty) ansiLossy++;
                else mismatch.Add($"{Path.GetFileName(f)}: țintă shell „{target}” vs parser „{e.Path}”");
            }
            if (args != e.Fields["Arguments"]) mismatch.Add($"{Path.GetFileName(f)}: argumente shell „{args}” vs parser „{e.Fields["Arguments"]}”");
            if (!string.Equals(wd, e.Fields["WorkingDirectory"], StringComparison.OrdinalIgnoreCase)) mismatch.Add($"{Path.GetFileName(f)}: folder shell „{wd}” vs parser „{e.Fields["WorkingDirectory"]}”");
        }
        Assert.True(parsed > 300, $"{parsed} linkuri");
        Assert.True(zeroFilled < 5, $"{zeroFilled} fișiere .lnk umplute cu zerouri");
        Assert.True(ansiLossy < compared / 4, $"{ansiLossy} ținte redate cu pierderi de shell (ANSI)");
        Assert.True(compared > 150, $"{compared} linkuri comparate");
        Assert.True(mismatch.Count == 0, $"{mismatch.Count} diferențe:{Environment.NewLine}{string.Join(Environment.NewLine, mismatch.Take(20))}");
    }
}
