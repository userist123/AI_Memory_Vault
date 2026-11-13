using System.Numerics;

namespace LogAnalyzer.Dfir.FileSystem.Ntfs;

public enum NtfsRecordState
{
    /// <summary>Header valid, fixup verified and applied; attributes readable.</summary>
    Ok,
    /// <summary>Not a record: no FILE/BAAD signature (zeros, slack, other data).</summary>
    NotRecord,
    /// <summary>Signature "BAAD": marked damaged by chkdsk. Header read, attributes not trusted.</summary>
    Baad,
    /// <summary>Fixup mismatch (torn write). Header read, nothing restored, attributes not trusted.</summary>
    Torn,
    /// <summary>Signature present but the header is inconsistent (USA bounds, count, offsets, sizes).</summary>
    Invalid,
}

/// <summary>One attribute header (format card §3). Offsets are relative to the record.</summary>
public sealed record NtfsAttribute(uint Type, int Offset, int Length, bool NonResident, string Name, bool NameHadInvalidUtf16, ushort Flags, ushort Instance,
                                   int ValueOffset, int ValueLength, long LowestVcn, long HighestVcn, int MappingPairsOffset, long AllocatedSize, long RealSize, long ValidDataLength)
{
    public bool IsSparse => (Flags & 0x8000) != 0;
    public bool IsCompressed => (Flags & 0x00FF) != 0;
    public bool IsEncrypted => (Flags & 0x4000) != 0;
}

/// <summary>
/// An MFT record (format card §2–§3). Works on a private copy of the bytes so the caller's buffer is never modified.
/// Anything wrong with the input is reported through <see cref="State"/> / <see cref="AttributeProblems"/>, never as an exception.
/// </summary>
public sealed class NtfsFileRecord
{
    public const uint EndMarker = 0xFFFFFFFF;
    public const int MaxRecordSize = 65536;

    public NtfsRecordState State { get; private set; }
    public string Problem { get; private set; } = "";
    public int RecordSize { get; }
    private byte[] _data = [];

    public string Signature { get; private set; } = "";
    public ulong Lsn { get; private set; }
    public ushort Sequence { get; private set; }
    public ushort LinkCount { get; private set; }
    public int FirstAttributeOffset { get; private set; }
    public ushort Flags { get; private set; }
    public uint Used { get; private set; }
    public uint Allocated { get; private set; }
    public MftReference BaseReference { get; private set; }
    public ushort NextAttributeId { get; private set; }
    /// <summary>The record's own number (NTFS 3.1, offset 44), null when the header is too short to carry it.</summary>
    public uint? RecordNumberField { get; private set; }

    public bool InUse => (Flags & 0x1) != 0;
    public bool IsDirectory => (Flags & 0x2) != 0;
    public bool IsBaseRecord => BaseReference.Raw == 0;

    private List<NtfsAttribute>? _attributes;
    public int AttributeProblems { get; private set; }

    private NtfsFileRecord(int recordSize) => RecordSize = recordSize;

    public ReadOnlySpan<byte> Bytes => _data;

    /// <summary>Parses one record. <paramref name="recordSize"/> is a power of two from 512 to 65536 (checked); <paramref name="raw"/> must hold at least that many bytes.</summary>
    public static NtfsFileRecord Parse(ReadOnlySpan<byte> raw, int recordSize)
    {
        var r = new NtfsFileRecord(recordSize);
        if (recordSize < 512 || recordSize > MaxRecordSize || !BitOperations.IsPow2(recordSize)) return r.Fail(NtfsRecordState.Invalid, $"mărime de înregistrare neacceptată ({recordSize})");
        if (raw.Length < recordSize) return r.Fail(NtfsRecordState.NotRecord, "înregistrare trunchiată");
        raw = raw[..recordSize];
        if (raw[..4].SequenceEqual("FILE"u8)) r.Signature = "FILE";
        else if (raw[..4].SequenceEqual("BAAD"u8)) r.Signature = "BAAD";
        else return r.Fail(NtfsRecordState.NotRecord, "fără semnătură FILE");

        // P1: the update sequence array must be where the card says it can be, with the right count.
        int usaOffset = Le.U16(raw, 4), usaCount = Le.U16(raw, 6);
        int expectedCount = recordSize / 512 + 1;
        if (usaCount != expectedCount) return r.Fail(NtfsRecordState.Invalid, $"USA count {usaCount} ≠ {expectedCount} (mărime/512 + 1)");
        if (usaOffset < 42 || usaOffset + 2 * usaCount > 510) return r.Fail(NtfsRecordState.Invalid, $"USA offset {usaOffset} / count {usaCount} în afara limitelor");

        r.Lsn = Le.U64(raw, 8); r.Sequence = Le.U16(raw, 16); r.LinkCount = Le.U16(raw, 18);
        r.FirstAttributeOffset = Le.U16(raw, 20); r.Flags = Le.U16(raw, 22);
        r.Used = Le.U32(raw, 24); r.Allocated = Le.U32(raw, 28);
        r.BaseReference = new MftReference(Le.U64(raw, 32)); r.NextAttributeId = Le.U16(raw, 40);
        if (usaOffset >= 0x30 && r.FirstAttributeOffset >= 0x30) r.RecordNumberField = Le.U32(raw, 44);

        if (r.Signature == "BAAD") return r.Fail(NtfsRecordState.Baad, "marcată BAAD");

        if (r.Allocated > (uint)recordSize || r.Used > r.Allocated || r.Used < 8)
            return r.Fail(NtfsRecordState.Invalid, $"used {r.Used} / allocated {r.Allocated} incoerente cu mărimea {recordSize}");
        if (r.FirstAttributeOffset < usaOffset + 2 * usaCount || r.FirstAttributeOffset % 8 != 0 || r.FirstAttributeOffset + 4 > r.Used)
            return r.Fail(NtfsRecordState.Invalid, $"offset primul atribut {r.FirstAttributeOffset} nevalid");

        // P2: verify EVERY sector tail against USA[0] before restoring any of them.
        var copy = raw.ToArray();
        ushort usn = Le.U16(copy, usaOffset);
        for (int i = 1; i < usaCount; i++)
            if (Le.U16(copy, i * 512 - 2) != usn) return r.Fail(NtfsRecordState.Torn, $"fixup nepotrivit la sectorul {i}");
        for (int i = 1; i < usaCount; i++)
        {
            copy[i * 512 - 2] = copy[usaOffset + 2 * i];
            copy[i * 512 - 1] = copy[usaOffset + 2 * i + 1];
        }
        r._data = copy;
        r.State = NtfsRecordState.Ok;
        return r;
    }

    /// <summary>How many 1,024-byte positions at the start of a file are searched for a valid FILE header (record 0 may be zeroed or BAAD).</summary>
    public const int DetectPositions = 64;

    /// <summary>
    /// Record size deduced from the first valid FILE header among the first <see cref="DetectPositions"/> positions of 1,024 bytes: signature "FILE",
    /// USA count = size/512 + 1 for a power-of-two size from 512 to 65536, USA offset inside the first sector, and the header at a multiple of that size.
    /// Returns 0 when there is none. <paramref name="offset"/> is where that header was found.
    /// </summary>
    public static int DetectRecordSize(ReadOnlySpan<byte> head, out int offset) => DetectRecordSize(head, out offset, out _);

    /// <summary>
    /// Every valid FILE header in the first <see cref="DetectPositions"/> × 1,024 bytes (scanned at 512-byte steps) votes for the size its own USA
    /// count gives; the size with the most votes wins (ties: the smaller size). One hostile or damaged header cannot impose its size on the whole
    /// file (review 2026-11-13, round 2). <paramref name="votes"/> lists every size that received a vote, so a caller can report a conflict.
    /// </summary>
    public static int DetectRecordSize(ReadOnlySpan<byte> head, out int offset, out IReadOnlyDictionary<int, int> votes)
    {
        offset = 0;
        var tally = new SortedDictionary<int, (int Votes, int First)>();
        for (int pos = 0; pos < DetectPositions * 1024 && pos + 8 <= head.Length; pos += 512)
        {
            if (!head.Slice(pos, 4).SequenceEqual("FILE"u8)) continue;
            int usaOffset = Le.U16(head, pos + 4), count = Le.U16(head, pos + 6);
            if (count < 2 || count > 129) continue;
            int size = (count - 1) * 512;
            if (!BitOperations.IsPow2(size) || size > MaxRecordSize) continue;
            if (usaOffset < 42 || usaOffset + 2 * count > 510) continue;
            if (pos % size != 0) continue;
            tally[size] = tally.TryGetValue(size, out var t) ? (t.Votes + 1, t.First) : (1, pos);
        }
        votes = tally.ToDictionary(kv => kv.Key, kv => kv.Value.Votes);
        if (tally.Count == 0) return 0;
        var best = tally.OrderByDescending(kv => kv.Value.Votes).ThenBy(kv => kv.Key).First();
        offset = best.Value.First;
        return best.Key;
    }

    private NtfsFileRecord Fail(NtfsRecordState s, string why) { State = s; Problem = why; return this; }

    /// <summary>Attributes in order. Iteration stops at 0xFFFFFFFF, used or allocated (first one), or at the first header that cannot be trusted to advance.</summary>
    public IReadOnlyList<NtfsAttribute> Attributes => _attributes ??= ReadAttributes();

    private List<NtfsAttribute> ReadAttributes()
    {
        var list = new List<NtfsAttribute>();
        if (State != NtfsRecordState.Ok) return list;
        var d = (ReadOnlySpan<byte>)_data;
        long limit = Math.Min(Used, Allocated);
        long p = FirstAttributeOffset;
        while (p + 4 <= limit)
        {
            uint type = Le.U32(d, (int)p);
            if (type == EndMarker) return list;
            if (p + 16 > limit) { AttributeProblems++; return list; }
            uint len = Le.U32(d, (int)p + 4);
            // P3: zero, unaligned or too long lengths cannot be advanced over.
            if (len < 16 || len % 8 != 0 || p + len > limit) { AttributeProblems++; return list; }
            int o = (int)p, length = (int)len;
            bool nonRes = d[o + 8] != 0 && d[o + 8] == 1;
            if (d[o + 8] > 1) { AttributeProblems++; p += len; continue; }
            int headerMin = nonRes ? 64 : 24;
            if (length < headerMin) { AttributeProblems++; p += len; continue; }
            int nameLen = d[o + 9], nameOff = Le.U16(d, o + 10);
            string name = ""; bool nameBad = false;
            if (nameLen > 0)
            {
                // P4: name length × 2 must lie inside this attribute (and after its header).
                if (nameOff < headerMin || (long)nameOff + nameLen * 2L > length) { AttributeProblems++; p += len; continue; }
                name = NtfsText.DecodeUtf16(d.Slice(o + nameOff, nameLen * 2), out nameBad);
            }
            ushort flags = Le.U16(d, o + 12), inst = Le.U16(d, o + 14);
            NtfsAttribute a;
            if (!nonRes)
            {
                uint vlen = Le.U32(d, o + 16); int voff = Le.U16(d, o + 20);
                if (voff < headerMin && vlen > 0 || voff > length || (long)voff + vlen > length) { AttributeProblems++; p += len; continue; }
                a = new NtfsAttribute(type, o, length, false, name, nameBad, flags, inst, voff, (int)vlen, 0, 0, 0, vlen, vlen, vlen);
            }
            else
            {
                long low = Le.I64(d, o + 16), high = Le.I64(d, o + 24);
                int mp = Le.U16(d, o + 32);
                if (mp < 64 || mp > length || low < 0 || high < -1) { AttributeProblems++; p += len; continue; }
                a = new NtfsAttribute(type, o, length, true, name, nameBad, flags, inst, 0, 0, low, high, mp,
                                      Le.I64(d, o + 40), Le.I64(d, o + 48), Le.I64(d, o + 56));
            }
            list.Add(a);
            p += len;   // strict advance: len ≥ 16
        }
        return list;
    }

    /// <summary>The bytes of a resident attribute's value (empty for non-resident ones).</summary>
    public ReadOnlySpan<byte> ValueOf(NtfsAttribute a)
        => a.NonResident ? [] : ((ReadOnlySpan<byte>)_data).Slice(a.Offset + a.ValueOffset, a.ValueLength);

    /// <summary>The mapping pairs area of a non-resident attribute.</summary>
    public ReadOnlySpan<byte> MappingPairsOf(NtfsAttribute a)
        => !a.NonResident ? [] : ((ReadOnlySpan<byte>)_data).Slice(a.Offset + a.MappingPairsOffset, a.Length - a.MappingPairsOffset);
}
