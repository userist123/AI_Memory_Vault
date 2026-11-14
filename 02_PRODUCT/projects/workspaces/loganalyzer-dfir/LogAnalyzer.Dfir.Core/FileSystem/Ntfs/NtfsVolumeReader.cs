using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Dfir.FileSystem.Ntfs;

/// <summary>Any problem with the volume bytes or the device: hostile structure, short read, I/O error. Nothing is ever reported as copied after it.</summary>
public sealed class NtfsReadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A finished copy. <see cref="Size"/> is the $DATA / $INDEX_ALLOCATION real size; <see cref="Sha256"/> is computed on the bytes written.</summary>
public sealed record NtfsCopyResult(long Size, string Sha256, long ValidDataLength, int Runs, int SparseRuns);

/// <summary>One $LogFile restart page as found in the COPIED bytes: its offset and the 4-byte magic (ASCII when printable, else <c>0x</c> + hex).</summary>
public sealed record NtfsRestartPage(long Offset, string Magic)
{
    /// <summary>"RSTR" (written by the driver) and "CHKD" (rewritten by chkdsk) are the two magics of a usable restart page.</summary>
    public bool IsValid => Magic is "RSTR" or "CHKD";
}

/// <summary>
/// A finished $LogFile copy. The restart header values (system page size, log page size, version) are read from the bytes that were written, not inferred.
/// <see cref="RestartPages"/> has page 0 and, when <see cref="SystemPageSize"/> is a power of two in 512..65536 below the size, the page at that offset;
/// otherwise <see cref="SecondPageLocated"/> is false and only page 0 is listed. A wrong magic is an observation, never an exception.
/// </summary>
public sealed record NtfsLogFileResult(NtfsCopyResult Copy, uint SystemPageSize, uint LogPageSize, short VersionMajor, short VersionMinor, IReadOnlyList<NtfsRestartPage> RestartPages, bool SecondPageLocated);

public enum NtfsDirectoryStatus { Copied, NotFound, NoIndexAllocation, ReparsePoint }

public sealed record NtfsDirectoryResult(string Path, NtfsDirectoryStatus Status, ulong Record, ushort Sequence, NtfsCopyResult? Copy, string Detail);

/// <summary>
/// Pure reader over a seekable stream holding an NTFS volume (a shadow copy device or an image). Read-only by construction: the stream is only
/// Seek/Read; nothing is ever written to it. Boot sector, record 0, data runs and $ATTRIBUTE_LIST are hostile input (threat T3): the first run of $MFT
/// must start at the boot sector's MFT LCN, every LCN lies inside the volume, non-sparse runs do not overlap, the covered bytes reach the real size, a
/// sparse run needs the sparse flag, extension records are checked for base record and sequence, and a loop in the attribute lists is refused.
/// A short read (end of stream before the bytes are there) or an I/O error is an <see cref="NtfsReadException"/>, never zero padding (T6).
/// Size choice: the copy has the $DATA real size ("data size"); bytes at or past the valid data length are written as zeros, which is what NTFS itself
/// returns for them (for $MFT valid = real, so nothing is replaced). Sparse runs are zeros only when the format says so (sparse flag + run without LCN).
/// </summary>
public sealed class NtfsVolumeReader
{
    public const long MaxMftBytes = 64L << 30, MaxIndexBytes = 1L << 30, MaxAttributeListBytes = 1L << 20;
    /// <summary>$LogFile plafon (1 GiB): the real one is tens of MiB; above it the record is hostile.</summary>
    public const long MaxLogFileBytes = 1L << 30;
    public const int MaxDirectoryEntries = 4_000_000;
    /// <summary>Largest volume accepted from a boot sector (256 TiB): above it, or a product that overflows, the sector is hostile.</summary>
    public const long MaxVolumeBytes = 1L << 48;
    private const int Chunk = 1 << 20;
    private const uint ReparseAttr = 0xC0;
    private const uint FileAttrReparse = 0x400;
    private const ulong RootRecord = 5;

    private readonly Stream _vol;
    public NtfsBootSector Boot { get; }
    public byte[] BootSectorBytes { get; }
    public long VolumeBytes { get; }
    private readonly long _cluster;
    private readonly int _recSize;

    private StreamMap? _mft;
    private Dictionary<ulong, DirInfo>? _dirs;
    private Dictionary<ulong, List<ChildName>>? _children;

    private sealed record DirInfo(ushort Sequence, bool Reparse);
    private readonly record struct ChildName(string Name, ulong Number, ushort ParentSequence, byte Namespace);

    private NtfsVolumeReader(Stream vol, NtfsBootSector boot, byte[] bootBytes)
    {
        _vol = vol; Boot = boot; BootSectorBytes = bootBytes;
        _cluster = boot.ClusterSize; _recSize = boot.MftRecordSize;
        VolumeBytes = VolumeBytesOf(boot) ?? throw new NtfsReadException("sector de boot NTFS nevalid: numărul de sectoare iese din limite");
    }

    /// <summary>TotalSectors times bytes per sector with every step checked; null when it is 0, overflows or is above <see cref="MaxVolumeBytes"/>.</summary>
    private static long? VolumeBytesOf(NtfsBootSector boot)
    {
        if (boot.TotalSectors == 0 || boot.BytesPerSector == 0 || boot.TotalSectors > (ulong)MaxVolumeBytes / (ulong)boot.BytesPerSector) return null;
        return (long)boot.TotalSectors * boot.BytesPerSector;
    }

    public static NtfsVolumeReader Open(Stream volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        if (!volume.CanRead || !volume.CanSeek) throw new NtfsReadException("fluxul volumului trebuie să poată fi citit și poziționat");
        var b = new byte[512];
        ReadExact(volume, 0, b, long.MaxValue);
        var boot = NtfsBootSector.TryParse(b, out var err) ?? throw new NtfsReadException("sector de boot NTFS nevalid: " + err);
        if (VolumeBytesOf(boot) is null) throw new NtfsReadException($"sector de boot NTFS nevalid: {boot.TotalSectors} sectoare de {boot.BytesPerSector} octeți (0 sau peste {MaxVolumeBytes:N0} octeți)");
        var r = new NtfsVolumeReader(volume, boot, b);
        // the MFT record 0 must also fit in the volume
        if ((long)boot.MftLcn * r._cluster + r._recSize > r.VolumeBytes) throw new NtfsReadException("înregistrarea 0 a $MFT depășește volumul");
        return r;
    }

    // ------------------------------------------------------------------ low-level reads

    private static void ReadExact(Stream s, long offset, Span<byte> buf, long limit)
    {
        if (offset < 0 || offset > limit || buf.Length > limit - offset) throw new NtfsReadException($"citire în afara volumului (offset {offset}, {buf.Length} octeți)");
        try
        {
            s.Seek(offset, SeekOrigin.Begin);
            int got = 0;
            while (got < buf.Length)
            {
                int n = s.Read(buf[got..]);
                if (n <= 0) throw new NtfsReadException($"citire scurtă la offsetul {offset + got}: s-au cerut {buf.Length - got} octeți, fluxul s-a terminat");
                got += n;
            }
        }
        catch (NtfsReadException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException)
        {
            throw new NtfsReadException($"eroare de I/O la offsetul {offset}: {ex.Message}", ex);
        }
    }

    private void ReadVolume(long offset, Span<byte> buf) => ReadExact(_vol, offset, buf, VolumeBytes);

    /// <summary>The runs of one stream, ordered by VCN, with the sizes that bound reading.</summary>
    private sealed class StreamMap(IReadOnlyList<DataRun> runs, long readable, long valid)
    {
        public IReadOnlyList<DataRun> Runs { get; } = runs;
        public long Readable { get; } = readable;   // bytes that may be read (real size)
        public long Valid { get; } = valid;         // bytes from here on are zeros
    }

    private void ReadMapped(StreamMap m, long offset, Span<byte> buf)
    {
        if (offset < 0 || buf.Length > m.Readable - offset) throw new NtfsReadException("citire dincolo de mărimea fluxului");
        int done = 0;
        while (done < buf.Length)
        {
            long pos = offset + done;
            long vcn = pos / _cluster;
            // binary search of the run holding vcn
            int lo = 0, hi = m.Runs.Count - 1, idx = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1; var r = m.Runs[mid];
                if (vcn < r.Vcn) hi = mid - 1; else if (vcn >= r.Vcn + r.Length) lo = mid + 1; else { idx = mid; break; }
            }
            if (idx < 0) throw new NtfsReadException($"offsetul {pos} nu este acoperit de niciun run");
            var run = m.Runs[idx];
            long inRun = pos - run.Vcn * _cluster;
            long runBytes = run.Length * _cluster;
            int n = (int)Math.Min(buf.Length - done, runBytes - inRun);
            var dst = buf.Slice(done, n);
            if (run.Lcn is long lcn) ReadVolume(lcn * _cluster + inRun, dst); else dst.Clear();   // sparse by format: zeros
            // bytes at or past the valid data length read as zero
            if (pos + n > m.Valid) dst[(int)Math.Max(0, m.Valid - pos)..].Clear();
            done += n;
        }
    }

    private NtfsCopyResult CopyMap(StreamMap m, long size, Stream output, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[Chunk];
        long off = 0;
        while (off < size)
        {
            ct.ThrowIfCancellationRequested();
            int n = (int)Math.Min(Chunk, size - off);
            ReadMapped(m, off, buf.AsSpan(0, n));
            output.Write(buf, 0, n);
            sha.AppendData(buf, 0, n);
            off += n;
        }
        output.Flush();
        return new NtfsCopyResult(size, Convert.ToHexString(sha.GetHashAndReset()), Math.Min(m.Valid, size), m.Runs.Count, m.Runs.Count(r => r.IsSparse));
    }

    // ------------------------------------------------------------------ records

    private NtfsFileRecord ParseRecord(byte[] raw, ulong number, string what)
    {
        var rec = NtfsFileRecord.Parse(raw, _recSize);
        if (rec.State != NtfsRecordState.Ok) throw new NtfsReadException($"{what}: înregistrarea {number} nu este validă ({rec.State}: {rec.Problem})");
        if (rec.RecordNumberField is uint f && f != number) throw new NtfsReadException($"{what}: înregistrarea {number} spune că este {f}");
        return rec;
    }

    private NtfsFileRecord ReadRecordVia(StreamMap map, ulong number, string what)
    {
        long off = checked((long)number * _recSize);
        var raw = new byte[_recSize];
        if (off + _recSize > map.Readable) throw new NtfsReadException($"{what}: înregistrarea {number} este în afara $MFT");
        ReadMapped(map, off, raw);
        return ParseRecord(raw, number, what);
    }

    // ------------------------------------------------------------------ segments of one attribute

    private sealed record Segments(IReadOnlyList<DataRun> Runs, long RealSize, long ValidLength, long TotalClusters);

    private ReadOnlyMemory<byte> AttributeListBytes(NtfsFileRecord rec, NtfsAttribute a)
    {
        if (!a.NonResident) return rec.ValueOf(a).ToArray();
        if (a.LowestVcn != 0 || a.IsCompressed || a.IsEncrypted || a.RealSize < 0 || a.RealSize > MaxAttributeListBytes) throw new NtfsReadException("$ATTRIBUTE_LIST nerezident nevalid sau prea mare");
        var segs = BuildSegments([(rec, a)], "$ATTRIBUTE_LIST", MaxAttributeListBytes);
        var buf = new byte[segs.RealSize];
        ReadMapped(new StreamMap(segs.Runs, segs.RealSize, segs.ValidLength), 0, buf);
        return buf;
    }

    /// <summary>
    /// The non-resident attribute (type, name) of a base record, including the segments held by extension records named in its $ATTRIBUTE_LIST.
    /// <paramref name="readRecord"/> reads another record of the same $MFT. Returns null when the attribute does not exist at all.
    /// </summary>
    private Segments? CollectSegments(NtfsFileRecord baseRec, ulong baseNumber, uint type, string name, Func<ulong, NtfsFileRecord> readRecord, long maxBytes, string what, bool mft = false, bool noSparse = false)
    {
        bool Match(NtfsAttribute a) => a.Type == type && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase);
        var found = new List<(NtfsFileRecord Rec, NtfsAttribute A)>();
        foreach (var a in baseRec.Attributes.Where(Match))
        {
            if (!a.NonResident) throw new NtfsReadException($"{what}: atributul este rezident, nu poate fi citit din volum");
            found.Add((baseRec, a));
        }
        var listAttr = baseRec.Attributes.FirstOrDefault(a => a.Type == NtfsAttributeType.AttributeList);
        if (listAttr is not null)
        {
            var entries = AttributeList.Parse(AttributeListBytes(baseRec, listAttr).Span, out bool malformed);
            if (malformed) throw new NtfsReadException($"{what}: $ATTRIBUTE_LIST malformat");
            var cache = new Dictionary<ulong, NtfsFileRecord>();
            NtfsFileRecord Get(ulong n) => cache.TryGetValue(n, out var r) ? r : cache[n] = readRecord(n);
            IReadOnlyList<AttributeListEntry>? ListOf(ulong n)
            {
                if (n == baseNumber) return entries;
                var r = Get(n);
                var la = r.Attributes.FirstOrDefault(a => a.Type == NtfsAttributeType.AttributeList);
                if (la is null) return null;
                var e = AttributeList.Parse(AttributeListBytes(r, la).Span, out bool bad);
                if (bad) throw new NtfsReadException($"{what}: $ATTRIBUTE_LIST malformat în înregistrarea {n}");
                return e;
            }
            var reachable = AttributeList.Walk(baseNumber, ListOf, out bool cycle, out bool truncated);
            if (cycle) throw new NtfsReadException($"{what}: lista de atribute este ciclică");
            if (truncated) throw new NtfsReadException($"{what}: lista de atribute depășește {AttributeList.MaxRecordsWalked} de înregistrări");
            foreach (var e in entries.Where(e => e.Type == type && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) && e.Record.Number != baseNumber))
            {
                var ext = Get(e.Record.Number);
                if (ext.Sequence != e.Record.Sequence) throw new NtfsReadException($"{what}: secvența înregistrării de extensie {e.Record.Number} ({ext.Sequence}) diferă de cea din listă ({e.Record.Sequence})");
                if (ext.BaseReference.Number != baseNumber || ext.BaseReference.Sequence != baseRec.Sequence) throw new NtfsReadException($"{what}: înregistrarea de extensie {e.Record.Number} nu aparține înregistrării de bază {baseNumber}");
                foreach (var a in ext.Attributes.Where(Match))
                {
                    if (!a.NonResident) throw new NtfsReadException($"{what}: segment rezident în înregistrarea de extensie");
                    if (!found.Any(f => ReferenceEquals(f.A, a))) found.Add((ext, a));
                }
            }
        }
        if (found.Count == 0) return null;
        return BuildSegments(found, what, maxBytes, mft, noSparse);
    }

    private Segments BuildSegments(List<(NtfsFileRecord Rec, NtfsAttribute A)> found, string what, long maxBytes, bool mft = false, bool noSparse = false)
    {
        var ordered = found.OrderBy(f => f.A.LowestVcn).ToList();
        if (ordered[0].A.LowestVcn != 0) throw new NtfsReadException($"{what}: lipsește segmentul de la VCN 0");
        var first = ordered[0].A;
        var runs = new List<DataRun>();
        long expectedVcn = 0;
        foreach (var (rec, a) in ordered)
        {
            if (a.IsCompressed) throw new NtfsReadException($"{what}: atribut comprimat, neacceptat");
            if (a.IsEncrypted) throw new NtfsReadException($"{what}: atribut criptat, neacceptat");
            if (a.LowestVcn != expectedVcn) throw new NtfsReadException($"{what}: lanț VCN întrerupt sau dublat (se aștepta VCN {expectedVcn}, segmentul începe la {a.LowestVcn})");
            var dec = DataRuns.Decode(rec.MappingPairsOf(a), a.LowestVcn, a.HighestVcn, (long)Boot.TotalClusters);
            if (dec.Error is not null) throw new NtfsReadException($"{what}: {dec.Error}");
            if (!a.IsSparse && dec.Runs.Any(r => r.IsSparse)) throw new NtfsReadException($"{what}: run sparse într-un atribut fără steagul sparse");
            if (mft && (a.IsSparse || dec.Runs.Any(r => r.IsSparse))) throw new NtfsReadException($"{what}: atribut sparse, refuzat (un $MFT nu are zone fără LCN; copia ar scrie zero-uri)");
            if (noSparse && (a.IsSparse || dec.Runs.Any(r => r.IsSparse))) throw new NtfsReadException($"{what}: atribut sparse, refuzat (copia ar scrie zero-uri în locul unor date)");
            runs.AddRange(dec.Runs);
            expectedVcn = a.HighestVcn + 1;
        }
        long clusters = expectedVcn;
        // no copy is bigger than the volume it comes from (sparse runs included: they are counted in the clusters)
        long cap = Math.Min(maxBytes, VolumeBytes);
        if (clusters > cap / _cluster + 1) throw new NtfsReadException($"{what}: {clusters} clustere, peste plafonul de {maxBytes:N0} octeți");
        long real = first.RealSize, valid = first.ValidDataLength;
        if (real < 0 || real > cap) throw new NtfsReadException($"{what}: mărime reală {real} în afara 0..{cap:N0} (plafon, mărimea volumului)");
        if (valid < 0 || valid > real) throw new NtfsReadException($"{what}: valid data length {valid} în afara 0..{real}");
        if (mft && valid != real) throw new NtfsReadException($"{what}: valid data length {valid} diferă de mărimea reală {real}; octeții de după ar fi zero-uri nemarcate");
        if (real > clusters * _cluster) throw new NtfsReadException($"{what}: run-urile acoperă {clusters * _cluster:N0} octeți, mai puțin decât mărimea reală {real:N0}");
        // physical runs inside the volume and without overlap
        var phys = runs.Where(r => r.Lcn is not null).OrderBy(r => r.Lcn).ToList();
        for (int i = 0; i < phys.Count; i++)
        {
            if (phys[i].Lcn!.Value + phys[i].Length > (long)Boot.TotalClusters) throw new NtfsReadException($"{what}: run în afara volumului");
            if (i > 0 && phys[i].Lcn!.Value < phys[i - 1].Lcn!.Value + phys[i - 1].Length) throw new NtfsReadException($"{what}: run-uri suprapuse la LCN {phys[i].Lcn}");
        }
        if (phys.Sum(r => r.Length) > (long)Boot.TotalClusters) throw new NtfsReadException($"{what}: run-urile depășesc mărimea volumului");
        return new Segments(runs, real, valid, clusters);
    }

    // ------------------------------------------------------------------ $MFT

    private StreamMap LoadMft()
    {
        if (_mft is not null) return _mft;
        long off0 = (long)Boot.MftLcn * _cluster;
        var raw = new byte[_recSize];
        ReadVolume(off0, raw);
        var rec0 = ParseRecord(raw, 0, "$MFT");
        if (!rec0.InUse || !rec0.IsBaseRecord || rec0.IsDirectory) throw new NtfsReadException("$MFT: înregistrarea 0 nu este o înregistrare de bază de fișier în uz");
        var data0 = rec0.Attributes.FirstOrDefault(a => a.Type == NtfsAttributeType.Data && a.Name.Length == 0 && a.LowestVcn == 0)
            ?? throw new NtfsReadException("$MFT: înregistrarea 0 nu are atributul $DATA nerezident de la VCN 0");
        if (!data0.NonResident) throw new NtfsReadException("$MFT: $DATA este rezident");
        // bootstrap: extension records are located through the runs of the VCN 0 segment alone
        var seg0 = DataRuns.Decode(rec0.MappingPairsOf(data0), 0, data0.HighestVcn, (long)Boot.TotalClusters);
        if (seg0.Error is not null) throw new NtfsReadException("$MFT: " + seg0.Error);
        if (data0.IsSparse || seg0.Runs.Any(r => r.IsSparse)) throw new NtfsReadException("$MFT: atribut sparse, refuzat (un $MFT nu are zone fără LCN; copia ar scrie zero-uri)");
        if (data0.IsCompressed || data0.IsEncrypted) throw new NtfsReadException("$MFT: atribut comprimat sau criptat, refuzat");
        var firstPhys = seg0.Runs.FirstOrDefault();
        if (firstPhys is null || firstPhys.Lcn != (long)Boot.MftLcn) throw new NtfsReadException($"$MFT: primul run nu începe la LCN-ul din boot sector ({Boot.MftLcn})");
        var bootstrap = new StreamMap(seg0.Runs, (data0.HighestVcn + 1) * _cluster, (data0.HighestVcn + 1) * _cluster);
        var segs = CollectSegments(rec0, 0, NtfsAttributeType.Data, "", n => ReadRecordVia(bootstrap, n, "$MFT"), MaxMftBytes, "$MFT", mft: true)
            ?? throw new NtfsReadException("$MFT: lipsește $DATA");
        if (segs.RealSize < _recSize) throw new NtfsReadException("$MFT: mai mic decât o înregistrare");
        return _mft = new StreamMap(segs.Runs, segs.RealSize, segs.ValidLength);
    }

    /// <summary>Copies $MFT:$DATA (real size, see class remarks) to <paramref name="output"/> and returns size and SHA-256 of the bytes written.</summary>
    public NtfsCopyResult CopyMft(Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var m = LoadMft();
        return CopyMap(m, m.Readable, output, ct);
    }

    // ------------------------------------------------------------------ $LogFile

    private const int LogFileRecord = 2, LogPageMin = 4096, RestartCapture = 65536 + 16;

    /// <summary>Forwards writes and keeps the first bytes, so restart pages are read from what was really written.</summary>
    private sealed class HeadCapture(Stream inner, int keep) : Stream
    {
        private readonly byte[] _head = new byte[keep];
        private long _written;
        public ReadOnlySpan<byte> Head => _head.AsSpan(0, (int)Math.Min(_written, _head.Length));
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            if (_written < _head.Length) Array.Copy(buffer, offset, _head, (int)_written, (int)Math.Min(count, _head.Length - _written));
            _written += count;
        }
    }

    private static string MagicText(ReadOnlySpan<byte> m)
    {
        foreach (var b in m) if (b < 0x21 || b > 0x7E) return "0x" + Convert.ToHexString(m);
        return Encoding.ASCII.GetString(m);
    }

    /// <summary>
    /// Copies $LogFile (record 2, unnamed $DATA) to <paramref name="output"/>. Record 2 must be in use, a base file record, not a directory, with a non-DOS $FILE_NAME
    /// "$LogFile" whose parent is record 5; the $DATA must be non-resident, not sparse, compressed or encrypted, between two 4096-byte pages and <see cref="MaxLogFileBytes"/>.
    /// Anything else is an <see cref="NtfsReadException"/> raised before a byte is written. No semantic parsing: the restart header is only reported.
    /// </summary>
    public NtfsLogFileResult CopyLogFile(Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var mft = LoadMft();
        var rec = ReadRecordVia(mft, LogFileRecord, "$LogFile");
        if (!rec.InUse || !rec.IsBaseRecord || rec.IsDirectory) throw new NtfsReadException("$LogFile: înregistrarea 2 nu este o înregistrare de bază de fișier în uz");
        var root = ReadRecordVia(mft, RootRecord, "$LogFile");   // the parent's sequence in $FILE_NAME must be the real one of record 5
        if (!root.InUse || !root.IsBaseRecord || !root.IsDirectory) throw new NtfsReadException("$LogFile: înregistrarea 5 (rădăcina) nu este un director de bază în uz; părintele lui $LogFile nu poate fi confirmat");
        bool named = rec.Attributes.Any(a => a.Type == NtfsAttributeType.FileName && !a.NonResident
            && FileNameAttribute.TryParse(rec.ValueOf(a)) is { } fn && fn.Namespace != 2 && fn.Parent.Number == RootRecord && fn.Parent.Sequence == root.Sequence && string.Equals(fn.Name, "$LogFile", StringComparison.Ordinal));
        if (!named) throw new NtfsReadException("$LogFile: înregistrarea 2 nu are numele „$LogFile” cu părintele în înregistrarea 5 (număr și secvență); nu se copiază altceva sub acest nume");
        var segs = CollectSegments(rec, LogFileRecord, NtfsAttributeType.Data, "", n => ReadRecordVia(mft, n, "$LogFile"), MaxLogFileBytes, "$LogFile", noSparse: true)
            ?? throw new NtfsReadException("$LogFile: lipsește $DATA");
        if (segs.RealSize > MaxLogFileBytes) throw new NtfsReadException($"$LogFile: mărime {segs.RealSize:N0} peste plafonul de {MaxLogFileBytes:N0} octeți");
        if (segs.RealSize < 2 * LogPageMin) throw new NtfsReadException($"$LogFile: mărime {segs.RealSize:N0} sub două pagini de {LogPageMin} octeți");
        var cap = new HeadCapture(output, RestartCapture);
        var copy = CopyMap(new StreamMap(segs.Runs, segs.RealSize, segs.ValidLength), segs.RealSize, cap, ct);
        var head = cap.Head;
        if (head.Length < 0x20) throw new NtfsReadException("$LogFile: copia are mai puțin de 32 de octeți");   // unreachable after the size check
        uint sysPage = BitConverter.ToUInt32(head[0x10..0x14]), logPage = BitConverter.ToUInt32(head[0x14..0x18]);
        short minor = BitConverter.ToInt16(head[0x1A..0x1C]), major = BitConverter.ToInt16(head[0x1C..0x1E]);
        var pages = new List<NtfsRestartPage> { new(0, MagicText(head[..4])) };
        bool second = sysPage is >= 512 and <= 65536 && (sysPage & (sysPage - 1)) == 0 && sysPage + 4 <= copy.Size && sysPage + 4 <= head.Length;
        if (second) pages.Add(new(sysPage, MagicText(head.Slice((int)sysPage, 4))));
        return new NtfsLogFileResult(copy, sysPage, logPage, major, minor, pages, second);
    }

    // ------------------------------------------------------------------ directories

    private void LoadDirectories()
    {
        if (_dirs is not null) return;
        var mft = LoadMft();
        var dirs = new Dictionary<ulong, DirInfo>();
        var children = new Dictionary<ulong, List<ChildName>>();
        long records = mft.Readable / _recSize;
        int perChunk = Math.Max(1, Chunk / _recSize);
        var buf = new byte[perChunk * _recSize];
        int names = 0;
        for (long first = 0; first < records; first += perChunk)
        {
            int n = (int)Math.Min(perChunk, records - first);
            ReadMapped(mft, first * _recSize, buf.AsSpan(0, n * _recSize));
            for (int i = 0; i < n; i++)
            {
                var raw = buf.AsSpan(i * _recSize, _recSize);
                if (!raw[..4].SequenceEqual("FILE"u8)) continue;
                ushort flags = (ushort)(raw[22] | raw[23] << 8);
                if ((flags & 3) != 3) continue;                                   // in use and a directory
                if (BitConverter.ToUInt64(raw[32..40]) != 0) continue;            // base records only
                ulong number = (ulong)(first + i);
                var rec = NtfsFileRecord.Parse(raw, _recSize);
                if (rec.State != NtfsRecordState.Ok || rec.RecordNumberField is uint f && f != number) continue;
                bool reparse = rec.Attributes.Any(a => a.Type == ReparseAttr);
                dirs[number] = new DirInfo(rec.Sequence, reparse);
                foreach (var a in rec.Attributes.Where(a => a.Type == NtfsAttributeType.FileName && !a.NonResident))
                {
                    var fn = FileNameAttribute.TryParse(rec.ValueOf(a));
                    if (fn is null || fn.Namespace == 2 || fn.Parent.Number == number) continue;   // DOS aliases and the root's own "." name
                    if (++names > MaxDirectoryEntries) throw new NtfsReadException($"mai mult de {MaxDirectoryEntries:N0} de nume de directoare în $MFT");
                    if (!children.TryGetValue(fn.Parent.Number, out var l)) children[fn.Parent.Number] = l = [];
                    l.Add(new ChildName(fn.Name, number, fn.Parent.Sequence, fn.Namespace));
                }
            }
        }
        _dirs = dirs; _children = children;
    }

    private static string[] Components(string path)
        => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Resolves a directory path to (record, sequence). Names compare case-insensitively; the parent's sequence in $FILE_NAME must equal the parent's real one.</summary>
    private bool TryResolve(string path, out ulong number, out ushort seq, out bool reparse)
    {
        LoadDirectories();
        number = RootRecord; seq = 0; reparse = false;
        if (!_dirs!.TryGetValue(RootRecord, out var cur)) return false;
        seq = cur.Sequence;
        foreach (var part in Components(path))
        {
            ushort wantSeq = seq;
            var next = _children!.TryGetValue(number, out var l)
                ? l.FirstOrDefault(c => c.ParentSequence == wantSeq && c.Name.Equals(part, StringComparison.OrdinalIgnoreCase) && _dirs.ContainsKey(c.Number))
                : default;
            if (next.Name is null) return false;
            number = next.Number; cur = _dirs[number]; seq = cur.Sequence;
            if (cur.Reparse) { reparse = true; return true; }   // never go through (or into) a link
        }
        return true;
    }

    /// <summary>Names of the child directories (Win32 names, not links) of a directory; empty when the path does not exist.</summary>
    public IReadOnlyList<string> ListChildDirectories(string path)
    {
        if (!TryResolve(path, out var number, out var seq, out bool reparse) || reparse) return [];
        if (!_children!.TryGetValue(number, out var l)) return [];
        return [.. l.Where(c => c.ParentSequence == seq && _dirs!.TryGetValue(c.Number, out var d) && !d.Reparse).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Copies the raw $INDEX_ALLOCATION "$I30" of a directory. Not found / link / root-only directories are reported and nothing is written.
    /// </summary>
    public NtfsDirectoryResult CopyDirectoryIndex(string path, Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!TryResolve(path, out var number, out var seq, out bool reparse))
            return new(path, NtfsDirectoryStatus.NotFound, 0, 0, null, "directorul nu există în $MFT");
        if (reparse) return new(path, NtfsDirectoryStatus.ReparsePoint, number, seq, null, "legătură (reparse point), nu este urmată");
        var mft = LoadMft();
        var rec = ReadRecordVia(mft, number, "director");
        if (!rec.InUse || !rec.IsDirectory || rec.Sequence != seq) throw new NtfsReadException($"directorul {path}: înregistrarea {number} s-a schimbat în timpul citirii");
        var segs = CollectSegments(rec, number, NtfsAttributeType.IndexAllocation, "$I30", n => ReadRecordVia(mft, n, "director"), MaxIndexBytes, "$INDEX_ALLOCATION");
        if (segs is null) return new(path, NtfsDirectoryStatus.NoIndexAllocation, number, seq, null, "fără $INDEX_ALLOCATION: indexul este doar în $INDEX_ROOT");
        var copy = CopyMap(new StreamMap(segs.Runs, segs.RealSize, segs.ValidLength), segs.RealSize, output, ct);
        return new(path, NtfsDirectoryStatus.Copied, number, seq, copy, "");
    }
}
