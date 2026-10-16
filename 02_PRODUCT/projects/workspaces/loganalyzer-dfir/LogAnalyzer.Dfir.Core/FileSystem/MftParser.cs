using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// The NTFS master file table exported as a file ($MFT, comparison row E8). Every FILE record is checked against its update sequence array (a record
/// whose sector ends do not carry the sequence number is a torn or damaged write and is not read), then its resident $STANDARD_INFORMATION (0x10) and
/// $FILE_NAME (0x30) attributes are decoded. Three kinds of events come out:
/// <list type="bullet">
/// <item>"deleted": a record no longer in use that still holds a name. The name, the parent folder and the last times are what NTFS left in the record;
/// the record says nothing about when or by whom it was freed.</item>
/// <item>"timestomp": a record in use whose $STANDARD_INFORMATION creation time is earlier than its $FILE_NAME creation time. Programs can set the first
/// through the ordinary Windows API; the second is written by the file system itself, so the first being older is the classic sign that the times were
/// set back. It is an inference, not proof: some installers and copy tools also leave this pattern.</item>
/// <item>"summary": one per file, with the counts, so an analysis that found nothing can say the table was read.</item>
/// </list>
/// Layout used (NTFS 3.1, Windows XP and later): FILE header with the update sequence offset (0x04) and size (0x06), sequence number (0x10), first
/// attribute (0x14), flags (0x16: 1 = in use, 2 = directory), allocated record size (0x1C) and record number (0x2C).
/// </summary>
public sealed class MftParser : EvidenceParserBase
{
    public const string SourceTypeName = "ntfs_mft";
    public const string Fingerprint = "ntfs_mft";
    private const int MaxNamesPerRecord = 16;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "MftParser", Version = "1.0", Artifact = "Tabela de fișiere NTFS ($MFT), exportată ca fișier",
        SourceTypes = [SourceTypeName], FileNames = ["$MFT"], Fingerprints = [Fingerprint],
        MaxInputBytes = 0,   // read record by record; only names and parents are kept for path reconstruction
        SupportedOs = "Volume NTFS 3.1 (Windows XP și mai noi), înregistrări de 1024 sau 4096 de octeți",
        FormatVersions = ["NTFS 3.1 FILE record (antet cu numărul înregistrării la 0x2C)"],
        Limitations =
        [
            "Se citesc doar atributele rezidente $STANDARD_INFORMATION și $FILE_NAME; un fișier cu multe nume poate avea numele într-o înregistrare de extensie (lista de atribute nu este urmată).",
            "O înregistrare liberă arată numele și orele rămase în ea; momentul ștergerii și cine a șters nu sunt înregistrate în $MFT (se caută în jurnalul USN sau în jurnalele Windows).",
            "O înregistrare liberă poate fi deja refolosită de alt fișier; atunci fișierul vechi nu mai apare deloc.",
            "Calea unui fișier șters se reconstruiește din folderele părinte; dacă înregistrarea unui folder a fost refolosită între timp (alt număr de secvență), calea începe cu „…”.",
            "Timestomp: ora de creare din $STANDARD_INFORMATION mai veche decât cea din $FILE_NAME este un indiciu, nu o dovadă; unele programe de instalare și de copiere lasă același tipar.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "MftParserTests: înregistrări FILE construite octet cu octet (secvența de actualizare, atribute rezidente), cu valori exacte; pe un $MFT real (achiziție live, cu drepturi de administrator) s-au verificat doar numărul de înregistrări și ponderea celor deteriorate",
    };

    private sealed record Entry(uint Number, ushort Sequence, bool InUse, bool Directory, long[]? Si, string Name, ulong ParentRef, long[]? FnTimes);

    /// <summary>True when the bytes look like the first FILE record of an $MFT: signature "FILE" and an allocated record size of 1024 or 4096.</summary>
    public static bool LooksLikeMft(ReadOnlySpan<byte> head) =>
        head.Length >= 0x20 && head[..4].SequenceEqual("FILE"u8) && BinaryPrimitives.ReadUInt32LittleEndian(head[0x1C..]) is 1024 or 4096;

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        var head = new byte[0x20];
        if (fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length || !LooksLikeMft(head))
            throw new InvalidDataException("Fișierul nu începe cu o înregistrare FILE NTFS (semnătura „FILE” și dimensiunea de 1024 sau 4096 de octeți).");
        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(0x1C));
        fs.Position = 0;

        var entries = new List<Entry>();
        var buf = new byte[size];
        for (long index = 0; ; index++)
        {
            ct.ThrowIfCancellationRequested();
            int n = fs.ReadAtLeast(buf, size, throwOnEndOfStream: false);
            if (n < size) break;
            if (!buf.AsSpan(0, 4).SequenceEqual("FILE"u8))
            {
                // a slot never used is zeroes; anything else (BAAD, garbage) is a damaged record
                if (buf.AsSpan(0, 4).IndexOfAnyExcept((byte)0) >= 0) result.MalformedRecords++;
                continue;
            }
            if (Read(buf, index) is { } e) { entries.Add(e); result.Records++; }
            else result.MalformedRecords++;
        }

        var byNumber = new Dictionary<uint, Entry>();
        foreach (var e in entries) byNumber[e.Number] = e;
        string PathOf(Entry e)
        {
            var parts = new List<string> { e.Name };
            var cur = e;
            for (int depth = 0; depth < 256; depth++)
            {
                uint parent = (uint)(cur.ParentRef & 0xFFFFFFFFFFFF);
                ushort seq = (ushort)(cur.ParentRef >> 48);
                if (parent == cur.Number) { parts.Reverse(); return string.Join("\\", parts.Select(p => p == "." ? "" : p)); }   // the root points at itself
                // a parent record freed or reused since (different sequence number) is not the folder the file was in
                if (!byNumber.TryGetValue(parent, out var p) || p.Sequence != seq) break;
                parts.Add(p.Name);
                cur = p;
            }
            parts.Reverse();
            return "…\\" + string.Join("\\", parts);
        }

        int deleted = 0, stomped = 0;
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.Name.Length == 0 || e.Si is null) continue;
            if (!e.InUse)
            {
                deleted++;
                sink.Add(Event(item, e, PathOf(e), "deleted", Timestamp.FromFileTime(e.Si[2], "FILETIME UTC ($STANDARD_INFORMATION, schimbarea înregistrării)"),
                    $"MFT: {(e.Directory ? "folder" : "fișier")} șters (înregistrare liberă): {e.Name}",
                    "ultima schimbare a înregistrării cunoscută înainte de ștergere; momentul ștergerii nu este în $MFT",
                    Classification.Direct, Confidence.High));
                continue;
            }
            if (e.FnTimes is { } fn && e.Si[0] < fn[0] - 10_000_000)   // more than one second earlier
            {
                stomped++;
                bool whole = e.Si[0] % 10_000_000 == 0 && e.Si[1] % 10_000_000 == 0 && fn[0] % 10_000_000 != 0;
                var ev = Event(item, e, PathOf(e), "timestomp", Timestamp.FromFileTime(e.Si[0], "FILETIME UTC ($STANDARD_INFORMATION, creare)"),
                    $"MFT: ora de creare a fișierului {e.Name} este mai veche în $STANDARD_INFORMATION ({Iso(e.Si[0])}) decât în $FILE_NAME ({Iso(fn[0])})",
                    "ora de creare declarată prin API ($STANDARD_INFORMATION); ora scrisă de sistemul de fișiere este în câmpul FnCreatedUtc",
                    Classification.Candidate, whole ? Confidence.Medium : Confidence.Low);
                ev.Fields["SiWholeSeconds"] = whole ? "true" : "false";
                sink.Add(ev);
            }
        }

        sink.Add(new TimelineEvent
        {
            Time = Timestamp.Unknown(), Source = "MFT", EvidenceId = item.EvidenceId, Path = "", Process = "",
            Summary = $"MFT: {result.Records} înregistrări citite, {deleted} libere cu nume (fișiere șterse), {stomped} candidați la modificarea orelor",
            TimeSemantics = "rezumatul tabelei", TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High,
            Locator = "MFT summary",
            Fields =
            {
                ["Kind"] = "summary", ["Records"] = result.Records.ToString(CultureInfo.InvariantCulture), ["Deleted"] = deleted.ToString(CultureInfo.InvariantCulture),
                ["TimestompCandidates"] = stomped.ToString(CultureInfo.InvariantCulture), ["RecordSize"] = size.ToString(CultureInfo.InvariantCulture),
                ["Malformed"] = result.MalformedRecords.ToString(CultureInfo.InvariantCulture),
            },
        });
    }

    private static string Iso(long ft) => ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc() ? DateTime.FromFileTimeUtc(ft).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) : ft.ToString(CultureInfo.InvariantCulture);

    private static TimelineEvent Event(EvidenceItem item, Entry e, string path, string kind, Timestamp time, string summary, string semantics, Classification cls, Confidence conf)
    {
        var ev = new TimelineEvent
        {
            Time = time, Source = "MFT", EvidenceId = item.EvidenceId, Path = path, Process = "", Summary = summary, TimeSemantics = semantics,
            TemporalType = TemporalType.Historical, Classification = cls, Confidence = conf, Locator = $"MFT record {e.Number} seq {e.Sequence}",
            Fields =
            {
                ["Kind"] = kind, ["Record"] = e.Number.ToString(CultureInfo.InvariantCulture), ["Sequence"] = e.Sequence.ToString(CultureInfo.InvariantCulture),
                ["FileName"] = e.Name, ["Directory"] = e.Directory ? "true" : "false",
                ["ParentRecord"] = (e.ParentRef & 0xFFFFFFFFFFFF).ToString(CultureInfo.InvariantCulture), ["ParentSequence"] = (e.ParentRef >> 48).ToString(CultureInfo.InvariantCulture),
                ["SiCreatedUtc"] = Utc(e.Si![0]), ["SiModifiedUtc"] = Utc(e.Si[1]), ["SiRecordChangedUtc"] = Utc(e.Si[2]), ["SiAccessedUtc"] = Utc(e.Si[3]),
            },
        };
        if (e.FnTimes is { } fn) { ev.Fields["FnCreatedUtc"] = Utc(fn[0]); ev.Fields["FnModifiedUtc"] = Utc(fn[1]); }
        return ev;
    }

    private static string Utc(long ft) => ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc() ? DateTime.FromFileTimeUtc(ft).ToString("o", CultureInfo.InvariantCulture) : "";

    /// <summary>
    /// Applies the update sequence fixup to one FILE record in place: checks that each sector ends with the sequence number and puts the saved bytes back.
    /// False when the record is torn or its array is inconsistent. Shared with the live acquisition, which reads record 0 of the volume.
    /// </summary>
    public static bool ApplyFixup(Span<byte> b)
    {
        if (b.Length < 0x30) return false;
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(b[0x04..]);
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(b[0x06..]);
        int sectors = b.Length / 512;
        if (usaCount != sectors + 1 || usaOffset < 0x28 || usaOffset + 2 * usaCount > b.Length) return false;
        ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(b[usaOffset..]);
        for (int s = 0; s < sectors; s++)
        {
            int end = 512 * (s + 1) - 2;
            if (BinaryPrimitives.ReadUInt16LittleEndian(b[end..]) != usn) return false;
            b[end] = b[usaOffset + 2 + 2 * s];
            b[end + 1] = b[usaOffset + 3 + 2 * s];
        }
        return true;
    }

    /// <summary>One record, or null when its update sequence or its attribute chain is not consistent.</summary>
    private static Entry? Read(byte[] rec, long index)
    {
        var b = rec.AsSpan();
        if (!ApplyFixup(b)) return null;
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(b[0x04..]);
        ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(b[0x10..]);
        int attr = BinaryPrimitives.ReadUInt16LittleEndian(b[0x14..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(b[0x16..]);
        uint used = BinaryPrimitives.ReadUInt32LittleEndian(b[0x18..]);
        // the record number is in the header since Windows XP; older volumes do not have it, then the position in the file is the number
        uint number = usaOffset >= 0x30 ? BinaryPrimitives.ReadUInt32LittleEndian(b[0x2C..]) : (uint)index;
        if (used > b.Length || attr < 0x18 || attr >= used) return null;

        long[]? si = null, fnTimes = null;
        string name = ""; ulong parent = 0; int bestNs = -1, names = 0;
        while (attr + 8 <= used)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(b[attr..]);
            if (type == 0xFFFFFFFF) break;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(b[(attr + 4)..]);
            if (len < 0x18 || (long)attr + len > used) return null;
            bool resident = b[attr + 8] == 0;
            if (resident && type is 0x10 or 0x30)
            {
                int vlen = (int)BinaryPrimitives.ReadUInt32LittleEndian(b[(attr + 0x10)..]);
                int voff = BinaryPrimitives.ReadUInt16LittleEndian(b[(attr + 0x14)..]);
                if (vlen < 0 || voff < 0x18 || (long)voff + vlen > len) return null;
                var v = b.Slice(attr + voff, vlen);
                if (type == 0x10 && vlen >= 0x20)
                    si = [BinaryPrimitives.ReadInt64LittleEndian(v), BinaryPrimitives.ReadInt64LittleEndian(v[8..]), BinaryPrimitives.ReadInt64LittleEndian(v[16..]), BinaryPrimitives.ReadInt64LittleEndian(v[24..])];
                else if (type == 0x30 && vlen >= 0x42 && ++names <= MaxNamesPerRecord)
                {
                    int chars = v[0x40], ns = v[0x41];
                    if (0x42 + 2 * chars > vlen) return null;
                    // prefer the long name: Win32 (1) or Win32 & DOS (3), then POSIX (0), then the short DOS name (2)
                    int rank = ns switch { 1 or 3 => 3, 0 => 2, _ => 1 };
                    if (rank > bestNs)
                    {
                        bestNs = rank;
                        name = Encoding.Unicode.GetString(v.Slice(0x42, 2 * chars));
                        parent = BinaryPrimitives.ReadUInt64LittleEndian(v);
                        fnTimes = [BinaryPrimitives.ReadInt64LittleEndian(v[8..]), BinaryPrimitives.ReadInt64LittleEndian(v[16..]), BinaryPrimitives.ReadInt64LittleEndian(v[24..]), BinaryPrimitives.ReadInt64LittleEndian(v[32..])];
                    }
                }
            }
            attr += len;
        }
        return new Entry(number, seq, (flags & 1) != 0, (flags & 2) != 0, si, name, parent, fnTimes);
    }
}
