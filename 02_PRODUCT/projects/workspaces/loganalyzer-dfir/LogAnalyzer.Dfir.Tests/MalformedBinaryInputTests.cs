using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Parsing;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Smoke tests for the binary parsers on damaged and hostile input: zero-length, truncated at many points, random bytes, and
/// every length / offset / count field of a valid sample overwritten with extreme values. Whatever the input, a parser must
/// finish in bounded time and report a controlled outcome: a result (SUCCESS / EMPTY / PARTIAL / FAILED) whose error, if any,
/// is a parse error (<see cref="InvalidDataException"/>), never a hang, a process crash or an unrelated exception type
/// (index, argument, overflow, null) that would show the parser never checked what it read. Nothing here depends on Windows.
/// </summary>
public sealed class MalformedBinaryInputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_malformed_" + Guid.NewGuid().ToString("N"));
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private static readonly uint[] Extremes = [0xFFFFFFFF, 0x7FFFFFFF, 0x80000000, 0x00000000, 0x0000FFFF, 0x00010000];

    public MalformedBinaryInputTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    // ------------------------------------------------------------------ valid samples

    private static byte[] SampleLnk() => LnkParserTests.BuildLnk(@"C:\Users\u\Downloads\SETUP.EXE", "/x", @"C:\Users\u\Downloads", "marius-pc");

    private static byte[] SampleCfb() => JumpListParserTests.BuildCfb(("a", [1, 2, 3]), ("b", new byte[900]));

    private static byte[] SampleJumpList() => JumpListParserTests.BuildCfb(("1", SampleLnk()),
        ("DestList", JumpListParserTests.DestList((1, @"C:\Users\u\Downloads\SETUP.EXE", new DateTime(2026, 9, 19, 15, 1, 2, DateTimeKind.Utc), 3, false))));

    private static byte[] SampleUsn() => Encoding.UTF8.GetBytes(string.Join("\n",
        "USN Journal ID    : 0x01dc84b0d77fa447",
        "First USN         : 100",
        "Next USN          : 900",
        "",
        "Usn,File name,File name length,Reason #,Reason,Time stamp,File attributes #,File attributes,File ID,Parent file ID,Source info #,Source info,Security ID,Major version,Minor version,Record length",
        "100,\"Prefetch\",16,0x00000000,\"Close\",\"03-Oct-26 16:00:00\",0x00000010,\"Directory\",D1,ROOT,0x00000000,\"*NONE*\",0,3,0,96",
        "200,\"A.EXE-1.pf\",22,0x00000000,\"File create | Close\",\"03-Oct-26 16:54:27\",0x00000020,\"Archive\",F1,D1,0x00000000,\"*NONE*\",0,3,0,96",
        ""));

    /// <summary>Little-endian pcapng: SHB, IDB (with if_tsresol), two EPBs carrying an Ethernet/IPv4/UDP DNS query.</summary>
    private static byte[] SamplePcapng()
    {
        var dns = new List<byte> { 0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in new[] { "example", "com" }) { dns.Add((byte)label.Length); dns.AddRange(Encoding.ASCII.GetBytes(label)); }
        dns.AddRange([0, 0, 1, 0, 1]);
        var udp = new List<byte> { 0x9C, 0x40, 0, 53, 0, (byte)(8 + dns.Count), 0, 0 };
        udp.AddRange(dns);
        var ip = new List<byte> { 0x45, 0, 0, (byte)(20 + udp.Count), 0, 0, 0, 0, 64, 17, 0, 0, 10, 0, 0, 2, 10, 0, 0, 1 };
        ip.AddRange(udp);
        var frame = new List<byte> { 1, 2, 3, 4, 5, 6, 6, 5, 4, 3, 2, 1, 0x08, 0x00 };
        frame.AddRange(ip);

        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        void Block(uint type, byte[] body) { int len = 12 + body.Length; w.Write(type); w.Write(len); w.Write(body); w.Write(len); }
        var shb = new MemoryStream();
        using (var sw = new BinaryWriter(shb, Encoding.UTF8, true)) { sw.Write(0x1A2B3C4D); sw.Write((ushort)1); sw.Write((ushort)0); sw.Write(-1L); }
        Block(0x0A0D0D0A, shb.ToArray());
        var idb = new MemoryStream();
        using (var iw = new BinaryWriter(idb, Encoding.UTF8, true))
        {
            iw.Write((ushort)1); iw.Write((ushort)0); iw.Write(0);                          // Ethernet, reserved, snaplen
            iw.Write((ushort)9); iw.Write((ushort)1); iw.Write((byte)6); iw.Write(new byte[3]);   // if_tsresol = 10^-6
            iw.Write(0);                                                                        // opt_endofopt
        }
        Block(1, idb.ToArray());
        for (int i = 0; i < 2; i++)
        {
            var epb = new MemoryStream();
            using (var ew = new BinaryWriter(epb, Encoding.UTF8, true))
            {
                ulong ts = (ulong)new DateTimeOffset(2026, 9, 19, 15, 0, i, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000;
                ew.Write(0); ew.Write((uint)(ts >> 32)); ew.Write((uint)ts); ew.Write(frame.Count); ew.Write(frame.Count);
                ew.Write(frame.ToArray()); ew.Write(new byte[(4 - frame.Count % 4) % 4]);
            }
            Block(6, epb.ToArray());
        }
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ harness

    private sealed record Case(string Name, byte[] Data);

    private static IEnumerable<Case> Truncations(byte[] valid)
    {
        yield return new("zero-length", []);
        yield return new("1 byte", valid[..1]);
        yield return new("3 bytes", valid[..3]);
        foreach (var n in Enumerable.Range(1, 40).Select(i => (int)((long)valid.Length * i / 41)).Concat([valid.Length - 1, valid.Length - 4, 8, 16, 24, 32, 64, 76, 128, 512]).Distinct()
                     .Where(n => n > 0 && n < valid.Length).Order())
            yield return new($"truncated to {n} of {valid.Length}", valid[..n]);
    }

    private static IEnumerable<Case> RandomBytes(byte[] valid, int prefixKept)
    {
        for (int seed = 0; seed < 12; seed++)
        {
            var r = new Random(1000 + seed);
            var a = new byte[r.Next(16, 4096)];
            r.NextBytes(a);
            yield return new($"random #{seed}", a);
            var b = new byte[Math.Max(valid.Length, 600)];                                  // valid header, then noise
            r.NextBytes(b);
            valid.AsSpan(0, Math.Min(prefixKept, valid.Length)).CopyTo(b);
            yield return new($"valid header + random tail #{seed}", b);
        }
        yield return new("all zero 4 KB", new byte[4096]);
        yield return new("all 0xFF 4 KB", Enumerable.Repeat((byte)0xFF, 4096).ToArray());
    }

    /// <summary>Every aligned dword of the first <paramref name="span"/> bytes, overwritten with each extreme value in turn.</summary>
    private static IEnumerable<Case> DwordMutations(byte[] valid, int span, int align = 4)
    {
        for (int at = 0; at + 4 <= Math.Min(span, valid.Length); at += align)
            foreach (var v in Extremes)
            {
                var m = (byte[])valid.Clone();
                BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(at), v);
                yield return new($"dword at 0x{at:X} = 0x{v:X8}", m);
            }
    }

    /// <summary>Runs one read on a worker; a case that does not finish is reported by name instead of hanging the run.</summary>
    private static (bool Finished, Exception? Error, T? Value) Run<T>(Func<T> read)
    {
        var task = Task.Run(read);
        bool done;
        try { done = task.Wait(Limit); }
        catch (AggregateException ex) { return (true, ex.InnerException, default); }
        return done ? (true, null, task.Result) : (false, null, default);
    }

    private static bool Controlled(Exception? ex) => ex is InvalidDataException or EndOfStreamException;

    private static EvidenceItem Item(string type, string path) => new() { EvidenceId = "EV-M", CaseId = "C", Source = type, SourceType = type, StoredPath = path };

    /// <summary>Feeds every case to a parser through its public Parse and collects whatever breaks the contract.</summary>
    private void SmokeParser(IEvidenceParser parser, string type, string fileName, IEnumerable<Case> cases)
    {
        var path = Path.Combine(_dir, fileName);
        var broken = new List<string>();
        int n = 0;
        foreach (var c in cases)
        {
            n++;
            File.WriteAllBytes(path, c.Data);
            var (finished, error, result) = Run(() => parser.Parse(Item(type, path), path, new ListSink(), default));
            if (!finished) { broken.Add($"{c.Name}: HANG (peste {Limit.TotalSeconds:0} s)"); continue; }
            if (error is not null) { broken.Add($"{c.Name}: Parse a aruncat {error.GetType().Name}: {error.Message}"); continue; }
            if (result!.Status == EvidenceStatus.Success && result.Records == 0) broken.Add($"{c.Name}: SUCCESS fără nicio înregistrare");
            if (result.Status == EvidenceStatus.Failed && result.Error.Length == 0) broken.Add($"{c.Name}: FAILED fără mesaj de eroare");
            if (result.Error.Length > 0 && !result.Error.StartsWith("InvalidDataException:") && !result.Error.StartsWith("EndOfStreamException:"))
                broken.Add($"{c.Name}: eroare necontrolată -> {result.Error[..Math.Min(result.Error.Length, 160)]}");
        }
        Assert.True(n > 50, $"prea puține cazuri ({n})");
        Assert.True(broken.Count == 0, $"{broken.Count} din {n} cazuri încalcă contractul pentru {parser.Descriptor.ParserId}:\n  " + string.Join("\n  ", broken.Take(25)));
    }

    // ------------------------------------------------------------------ samples are valid (so the mutations mean something)

    [Fact]
    public void The_valid_samples_parse_to_events()
    {
        void Ok(IEvidenceParser p, string type, string file, byte[] data, int min)
        {
            var path = Path.Combine(_dir, file);
            File.WriteAllBytes(path, data);
            var sink = new ListSink();
            var r = p.Parse(Item(type, path), path, sink, default);
            Assert.True(r.Status == EvidenceStatus.Success && sink.Events.Count >= min, $"{p.Descriptor.ParserId}: {r.Status} {r.Error} events={sink.Events.Count}");
        }
        Ok(new LnkParser(), "lnk", "ok.lnk", SampleLnk(), 1);
        Ok(new JumpListParser(), "jumplist_auto", "ok.automaticDestinations-ms", SampleJumpList(), 1);
        Ok(new UsnJournalParser(TimeZoneInfo.Utc), "usn_journal", "ok.csv", SampleUsn(), 2);
        Ok(new PcapngParser(), "pcapng", "ok.pcapng", SamplePcapng(), 2);
        Assert.Equal(3, new CompoundFile(SampleCfb()).Entries.Count);
    }

    // ------------------------------------------------------------------ LNK

    [Fact]
    public void Lnk_parser_survives_truncated_random_and_corrupted_files()
    {
        var v = SampleLnk();
        SmokeParser(new LnkParser(), "lnk", "m.lnk", Truncations(v).Concat(RandomBytes(v, 0x4C)).Concat(DwordMutations(v, v.Length)));
    }

    [Fact]
    public void Lnk_decoder_rejects_garbage_with_InvalidDataException_only()
    {
        foreach (var c in Truncations(SampleLnk()).Concat(RandomBytes(SampleLnk(), 0x4C)))
        {
            var (finished, error, _) = Run(() => LnkParser.Decode(c.Data));
            Assert.True(finished, c.Name);
            Assert.True(error is null || Controlled(error), $"{c.Name}: {error?.GetType().Name}: {error?.Message}");
        }
    }

    // ------------------------------------------------------------------ CFB / Jump List

    [Fact]
    public void Compound_file_reader_throws_only_InvalidDataException_on_damaged_files()
    {
        var v = SampleCfb();
        var broken = new List<string>();
        foreach (var c in Truncations(v).Concat(RandomBytes(v, 0x4C)).Concat(DwordMutations(v, 0x200)))
        {
            var (finished, error, _) = Run(() =>
            {
                var cf = new CompoundFile(c.Data);
                foreach (var e in cf.Entries.Where(e => e.Type == 2)) cf.Read(e);          // every stream, as a Jump List parse would
                return cf.Entries.Count;
            });
            if (!finished) broken.Add($"{c.Name}: HANG");
            else if (error is not null && !Controlled(error)) broken.Add($"{c.Name}: {error.GetType().Name}: {error.Message}");
        }
        Assert.True(broken.Count == 0, $"{broken.Count} cazuri:\n  " + string.Join("\n  ", broken.Take(25)));
    }

    [Fact]
    public void Compound_file_with_a_cyclic_DIFAT_chain_is_an_error_not_a_hang()
    {
        var f = SampleCfb();
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(0x2C), 0x7FFFFFFF);        // fat sector count
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(0x44), 0);                  // first DIFAT sector: sector 0 ...
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(0x48), 0x7FFFFFFF);         // ... a very long DIFAT chain
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(512 + 508), 0);             // ... whose last link points back to sector 0 (the FAT itself)
        var (finished, error, _) = Run(() => new CompoundFile(f));
        Assert.True(finished, "CompoundFile nu s-a terminat (lanț DIFAT circular)");
        Assert.True(Controlled(error), $"{error?.GetType().Name}: {error?.Message}");
    }

    [Fact]
    public void Jump_list_parser_survives_truncated_random_and_corrupted_files()
    {
        var v = SampleJumpList();
        SmokeParser(new JumpListParser(), "jumplist_auto", "m.automaticDestinations-ms",
            Truncations(v).Concat(RandomBytes(v, 0x4C)).Concat(DwordMutations(v, 0x200)).Concat(DwordMutations(v, v.Length).Where((_, i) => i % 7 == 0)));
    }

    [Fact]
    public void Jump_list_with_a_DestList_that_claims_billions_of_entries_stops_at_the_data()
    {
        var destList = JumpListParserTests.DestList((1, @"C:\a.exe", new DateTime(2026, 9, 19, 15, 1, 2, DateTimeKind.Utc), 1, false));
        BinaryPrimitives.WriteInt32LittleEndian(destList.AsSpan(4), int.MaxValue);
        var path = Path.Combine(_dir, "big.automaticDestinations-ms");
        File.WriteAllBytes(path, JumpListParserTests.BuildCfb(("DestList", destList)));
        var (finished, error, r) = Run(() => new JumpListParser().Parse(Item("jumplist_auto", path), path, new ListSink(), default));
        Assert.True(finished && error is null);
        Assert.Equal(EvidenceStatus.Partial, r!.Status);                              // the one real entry was kept; the truncation is reported
        Assert.StartsWith("InvalidDataException:", r.Error);
    }

    // ------------------------------------------------------------------ USN journal (fsutil text export)

    [Fact]
    public void Usn_parser_survives_truncated_random_and_corrupted_exports()
    {
        var v = SampleUsn();
        var text = Encoding.UTF8.GetString(v);
        var hostile = new List<Case>
        {
            new("header numbers not numbers", Encoding.UTF8.GetBytes(text.Replace("First USN         : 100", "First USN         : zz").Replace("Next USN          : 900", "Next USN          : 99999999999999999999999"))),
            new("hex header number out of range", Encoding.UTF8.GetBytes(text.Replace("Next USN          : 900", "Next USN          : 0xFFFFFFFFFFFFFFFFFFFF"))),
            new("journal id garbage", Encoding.UTF8.GetBytes(text.Replace("0x01dc84b0d77fa447", "0xZZ"))),
            new("no CSV header", Encoding.UTF8.GetBytes(text[..text.IndexOf("Usn,File", StringComparison.Ordinal)])),
            new("CSV header only", Encoding.UTF8.GetBytes(text[..(text.IndexOf("100,", StringComparison.Ordinal))])),
            new("missing columns", Encoding.UTF8.GetBytes(text.Replace("File ID,Parent file ID,", ""))),
            new("unterminated quote", Encoding.UTF8.GetBytes(text + "300,\"unterminated,0x0,\"Close\",\"03-Oct-26 16:54:27\"")),
            new("timestamp garbage and a date out of range", Encoding.UTF8.GetBytes(text.Replace("03-Oct-26 16:54:27", "99-Zzz-99 99:99:99"))),
            new("file id parent loop", Encoding.UTF8.GetBytes(text.Replace("F1,D1", "F1,F1").Replace("D1,ROOT", "D1,F1"))),
            new("UTF-16 BOM then binary", [0xFF, 0xFE, 0, 0, 1, 2, 3, 0xD8, 0, 0xD8]),
            new("one enormous line", Encoding.UTF8.GetBytes(text + new string('x', 5_000_000))),
            new("NUL bytes", Encoding.UTF8.GetBytes(text.Replace("Close", "Cl\0se"))),
        };
        var cuts = Enumerable.Range(1, 60).Select(i => text.Length * i / 61).Distinct().Select(n => new Case($"cut at char {n}", Encoding.UTF8.GetBytes(text[..n])));
        SmokeParser(new UsnJournalParser(TimeZoneInfo.Utc), "usn_journal", "m.csv", Truncations(v).Concat(cuts).Concat(hostile).Concat(RandomBytes(v, 64)));
    }

    // ------------------------------------------------------------------ pcapng

    [Fact]
    public void Pcapng_parser_survives_truncated_random_and_corrupted_captures()
    {
        var v = SamplePcapng();
        SmokeParser(new PcapngParser(), "pcapng", "m.pcapng", Truncations(v).Concat(RandomBytes(v, 28)).Concat(DwordMutations(v, v.Length)));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(4u)]
    [InlineData(11u)]
    public void Pcapng_section_header_with_a_block_length_below_the_minimum_does_not_loop(uint blockLength)
    {
        // A length that rewinds the reader to the start of the same header used to make it read that header again, without end.
        var v = SamplePcapng();
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(4), blockLength);
        var path = Path.Combine(_dir, "loop.pcapng");
        File.WriteAllBytes(path, v);
        var (finished, error, frames) = Run(() => PcapngReader.Read(path).Count());
        Assert.True(finished, "PcapngReader nu s-a terminat");
        Assert.True(Controlled(error), $"{error?.GetType().Name}: {error?.Message} (frames={frames})");
    }

    [Fact]
    public void Pcapng_with_a_timestamp_beyond_the_calendar_keeps_the_frames()
    {
        var v = SamplePcapng();
        const int epb = 28 + 32;                                                       // after the 28-byte SHB and the 32-byte IDB: first EPB
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(epb)));
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(epb + 12), 0xFFFFFFFF);     // timestamp high
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(epb + 16), 0xFFFFFFFF);     // timestamp low
        var path = Path.Combine(_dir, "ts.pcapng");
        File.WriteAllBytes(path, v);
        var sink = new ListSink();
        var (finished, error, r) = Run(() => new PcapngParser().Parse(Item("pcapng", path), path, sink, default));
        Assert.True(finished && error is null);
        Assert.NotEqual(EvidenceStatus.Failed, r!.Status);
        Assert.NotEmpty(sink.Events);
    }
}
