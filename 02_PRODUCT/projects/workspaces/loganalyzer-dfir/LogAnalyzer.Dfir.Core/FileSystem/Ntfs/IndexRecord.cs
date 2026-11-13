using System.Numerics;

namespace LogAnalyzer.Dfir.FileSystem.Ntfs;

public enum IndexRecordState { Ok, Blank, NotIndex, Baad, Torn, Invalid }

/// <summary>An $I30 entry: file reference, flags and the $FILE_NAME key (null for the last-entry marker). <see cref="Offset"/> is absolute in the block.</summary>
public sealed record IndexEntry(MftReference Reference, int Offset, int Length, ushort Flags, FileNameAttribute? Key)
{
    public bool IsLast => (Flags & 0x2) != 0;
    public bool HasChild => (Flags & 0x1) != 0;
}

/// <summary>
/// One INDX block of $INDEX_ALLOCATION (format card section 8): "INDX", USA, VCN at +16, node header at +24 (offsets relative to +24).
/// Entries start at <see cref="EntriesStart"/>, the allocated ones end at <see cref="UsedEnd"/>, the slack runs from there to <see cref="AllocatedEnd"/>.
/// </summary>
public sealed class IndexRecord
{
    public const int NodeHeaderOffset = 24, MaxBlockSize = 65536, MinBlockSize = 512, DetectPositions = 64;   // above 64 KiB the update sequence array (count = size/512 + 1) no longer fits under byte 510
    public IndexRecordState State { get; private set; }
    public string Problem { get; private set; } = "";
    public long Vcn { get; private set; }
    public int EntriesStart { get; private set; }
    public int UsedEnd { get; private set; }
    public int AllocatedEnd { get; private set; }
    private byte[] _data = [];

    private IndexRecord Fail(IndexRecordState s, string why) { State = s; Problem = why; return this; }

    public static IndexRecord Parse(ReadOnlySpan<byte> raw, int blockSize)
    {
        var r = new IndexRecord();
        if (blockSize < MinBlockSize || blockSize > MaxBlockSize || !BitOperations.IsPow2(blockSize)) return r.Fail(IndexRecordState.Invalid, $"mărime de bloc neacceptată ({blockSize})");
        if (raw.Length < blockSize) return r.Fail(IndexRecordState.NotIndex, "bloc trunchiat");
        raw = raw[..blockSize];
        if (raw[..4].SequenceEqual(new byte[4])) return r.Fail(IndexRecordState.Blank, "bloc zerorizat");
        if (raw[..4].SequenceEqual("BAAD"u8)) return r.Fail(IndexRecordState.Baad, "marcat BAAD");
        if (!raw[..4].SequenceEqual("INDX"u8)) return r.Fail(IndexRecordState.NotIndex, "fără semnătură INDX");
        int usaOffset = Le.U16(raw, 4), usaCount = Le.U16(raw, 6);
        if (usaCount != blockSize / 512 + 1) return r.Fail(IndexRecordState.Invalid, $"USA count {usaCount} ≠ {blockSize / 512 + 1} (mărime/512 + 1)");
        // the array sits after the node header (24 + 16 = 40) and inside the first sector
        if (usaOffset < 40 || usaOffset % 2 != 0 || usaOffset + 2 * usaCount > 510) return r.Fail(IndexRecordState.Invalid, $"USA offset {usaOffset} / count {usaCount} în afara limitelor");
        long first = Le.U32(raw, 24), used = Le.U32(raw, 28), alloc = Le.U32(raw, 32);
        long firstAbs = NodeHeaderOffset + first, usedAbs = NodeHeaderOffset + used, allocAbs = NodeHeaderOffset + alloc;
        if (allocAbs > blockSize || usedAbs > allocAbs || firstAbs > usedAbs || first % 8 != 0 || firstAbs < usaOffset + 2 * usaCount)
            return r.Fail(IndexRecordState.Invalid, $"node header incoerent (first {first}, used {used}, allocated {alloc}, bloc {blockSize})");
        r.Vcn = Le.I64(raw, 16);
        var copy = NtfsFixup.Apply(raw, usaOffset, usaCount, out int bad);
        if (copy is null) return r.Fail(IndexRecordState.Torn, $"fixup nepotrivit la sectorul {bad}");
        r._data = copy; r.EntriesStart = (int)firstAbs; r.UsedEnd = (int)usedAbs; r.AllocatedEnd = (int)allocAbs;
        r.State = IndexRecordState.Ok;
        return r;
    }

    /// <summary>
    /// Block size deduced from the headers: every valid-looking INDX header (checked at 512-byte steps among the first 64 positions) votes for
    /// allocated + 24; the size with most votes wins (ties: smaller). 0 when none. A damaged header cannot impose its size.
    /// </summary>
    public static int DetectBlockSize(ReadOnlySpan<byte> head, out IReadOnlyDictionary<int, int> votes)
    {
        var tally = new SortedDictionary<int, int>();
        for (int pos = 0; pos < DetectPositions * 512 && pos + 36 <= head.Length; pos += 512)
        {
            var h = head[pos..];
            if (!h[..4].SequenceEqual("INDX"u8)) continue;
            long size = (long)Le.U32(h, 32) + NodeHeaderOffset;
            if (size < MinBlockSize || size > MaxBlockSize || !BitOperations.IsPow2(size)) continue;
            if (Le.U16(h, 6) != size / 512 + 1) continue;
            int usa = Le.U16(h, 4);
            if (usa < 40 || usa + 2 * Le.U16(h, 6) > 510) continue;
            if (pos % size != 0) continue;
            tally[(int)size] = tally.GetValueOrDefault((int)size) + 1;
        }
        votes = tally;
        return tally.Count == 0 ? 0 : tally.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }

    /// <summary>An entry of the allocated area at <paramref name="pos"/>, bounds checked against the used end. Null with a reason when it cannot be trusted to advance.</summary>
    public IndexEntry? TryReadAllocated(int pos, out string problem) => ReadAllocatedEntry(_data, pos, UsedEnd, out problem);

    /// <summary>Shared by INDX blocks and the resident $INDEX_ROOT: one entry at <paramref name="pos"/> that must end at or before <paramref name="usedEnd"/>.</summary>
    internal static IndexEntry? ReadAllocatedEntry(byte[] d, int pos, int usedEnd, out string problem)
    {
        problem = "";
        if (pos < 0 || pos + 16 > usedEnd || pos + 16 > d.Length) { problem = "intrare sub 16 octeți înainte de used"; return null; }
        int el = Le.U16(d, pos + 8), kl = Le.U16(d, pos + 10); ushort fl = Le.U16(d, pos + 12);
        if (el < 16 || el % 8 != 0) { problem = $"lungime de intrare {el} nevalidă"; return null; }
        if (pos + el > usedEnd) { problem = $"intrarea ({el} octeți) depășește used"; return null; }
        if ((fl & ~3) != 0) { problem = $"flaguri nevalide 0x{fl:X}"; return null; }
        var reference = new MftReference(Le.U64(d, pos));
        if ((fl & 2) != 0) return new IndexEntry(reference, pos, el, fl, null);
        int need = 16 + kl + ((fl & 1) != 0 ? 8 : 0);
        if (kl < 66) { problem = $"lungime cheie {kl} sub 66"; return null; }
        if (need > el) { problem = "cheia depășește intrarea"; return null; }
        var key = FileNameAttribute.TryParse(d.AsSpan(pos + 16, kl));
        if (key is null) { problem = "cheia nu este un $FILE_NAME valid (lungime nume sau namespace)"; return null; }
        return new IndexEntry(reference, pos, el, fl, key);
    }

    /// <summary>
    /// A deleted-entry residue at <paramref name="pos"/> in the slack: strict, because the bytes are leftovers. Coherent lengths (key = 66 + 2 x name length,
    /// entry = key rounded up to 8), namespace at most 3, name of 1..255 characters without control characters or lone surrogates, at least 2 plausible FILETIMEs,
    /// reference with sequence not 0.
    /// </summary>
    public IndexEntry? TryReadSlack(int pos)
    {
        var d = _data;
        if (pos < EntriesStart || pos % 8 != 0 || pos + 16 + 66 > AllocatedEnd) return null;
        int el = Le.U16(d, pos + 8), kl = Le.U16(d, pos + 10); ushort fl = Le.U16(d, pos + 12);
        if (fl != 0 || kl < 68 || kl > 66 + 2 * 255 || (kl - 66) % 2 != 0) return null;
        if (el != ((16 + kl + 7) & ~7) || pos + el > AllocatedEnd) return null;
        var reference = new MftReference(Le.U64(d, pos));
        if (reference.Sequence == 0 || reference.Number == 0) return null;
        int nameLen = d[pos + 16 + 64];
        if (nameLen == 0 || kl != 66 + 2 * nameLen) return null;
        var key = FileNameAttribute.TryParse(d.AsSpan(pos + 16, kl));
        if (key is null || key.NameHadInvalidUtf16) return null;
        foreach (char c in key.Name) if (char.IsControl(c)) return null;
        int plausible = 0;
        foreach (var t in new[] { key.Created, key.Modified, key.MftModified, key.Accessed }) if (NtfsTime.IsPlausible(t)) plausible++;
        if (plausible < 2) return null;
        return new IndexEntry(reference, pos, el, fl, key);
    }
}

/// <summary>The resident $INDEX_ROOT value (format card section 8): root header (16 bytes), node header at +16, entries. Its entries are allocated entries; no slack carving here.</summary>
public static class IndexRootNode
{
    /// <summary>Entries up to the last-entry marker. <paramref name="problem"/> is set when the node or an entry could not be trusted; what was read before is returned. Offsets are relative to the value.</summary>
    public static List<IndexEntry> Read(ReadOnlySpan<byte> value, out string? problem)
    {
        problem = null;
        var list = new List<IndexEntry>();
        if (value.Length < 32) { problem = "valoare mai scurtă de 32 de octeți"; return list; }
        var d = value.ToArray();
        long first = Le.U32(d, 16), used = Le.U32(d, 20), alloc = Le.U32(d, 24);
        long firstAbs = 16 + first, usedAbs = 16 + used, allocAbs = 16 + alloc;
        if (allocAbs > d.Length || usedAbs > allocAbs || firstAbs > usedAbs || first % 8 != 0) { problem = $"node header incoerent (first {first}, used {used}, allocated {alloc}, valoare {d.Length})"; return list; }
        // A node always ends with a last-entry marker (16 bytes at least); used < 16 or no marker before used = a damaged root that could hide names.
        if (used < 16) { problem = $"node header fără loc pentru intrarea de sfârșit (used {used})"; return list; }
        int pos = (int)firstAbs;
        while (pos < usedAbs)
        {
            var e = IndexRecord.ReadAllocatedEntry(d, pos, (int)usedAbs, out var why);
            if (e is null) { problem = why; return list; }
            if (e.IsLast) return list;
            list.Add(e);
            pos += e.Length;
        }
        problem = "rădăcina indexului se termină fără intrarea de sfârșit";
        return list;
    }
}
