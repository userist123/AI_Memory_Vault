namespace LogAnalyzer.Dfir.FileSystem.Ntfs;

public static class NtfsAttributeType
{
    public const uint StandardInformation = 0x10, AttributeList = 0x20, FileName = 0x30, Data = 0x80, IndexRoot = 0x90, IndexAllocation = 0xA0;
}

/// <summary>$STANDARD_INFORMATION (format card §5): 48 bytes (NTFS 1.2) or 72 (3.0+). Times are raw FILETIME.</summary>
public sealed record StandardInformation(long Created, long Modified, long MftModified, long Accessed, uint Attributes, uint? SecurityId, ulong? Usn)
{
    public static StandardInformation? TryParse(ReadOnlySpan<byte> v)
    {
        if (v.Length < 48) return null;
        bool ext = v.Length >= 72;
        return new StandardInformation(Le.I64(v, 0), Le.I64(v, 8), Le.I64(v, 16), Le.I64(v, 24), Le.U32(v, 32),
                                       ext ? Le.U32(v, 52) : null, ext ? Le.U64(v, 64) : null);
    }
}

/// <summary>$FILE_NAME (format card §6). Sizes in it can be stale: the real size comes from $DATA.</summary>
public sealed record FileNameAttribute(MftReference Parent, long Created, long Modified, long MftModified, long Accessed,
                                       long AllocatedSize, long RealSize, uint Flags, string Name, byte Namespace, bool NameHadInvalidUtf16)
{
    public static FileNameAttribute? TryParse(ReadOnlySpan<byte> v)
    {
        if (v.Length < 66) return null;
        int nameLen = v[64];
        byte ns = v[65];
        if (ns > 3) return null;
        if (66 + nameLen * 2 > v.Length) return null;   // P4
        var name = NtfsText.DecodeUtf16(v.Slice(66, nameLen * 2), out bool bad);
        return new FileNameAttribute(new MftReference(Le.U64(v, 0)), Le.I64(v, 8), Le.I64(v, 16), Le.I64(v, 24), Le.I64(v, 32),
                                     Le.I64(v, 40), Le.I64(v, 48), Le.U32(v, 56), name, ns, bad);
    }

    /// <summary>Win32 names first, then POSIX, DOS last (an 8.3 alias is the worst label for a file).</summary>
    public int Rank => Namespace switch { 3 => 3, 1 => 3, 0 => 2, _ => 1 };
}

public sealed record AttributeListEntry(uint Type, ushort Length, ulong StartingVcn, MftReference Record, ushort AttributeId, string Name);

/// <summary>$ATTRIBUTE_LIST (format card §7) and a bounded, cycle-safe walk over the records it points to.</summary>
public static class AttributeList
{
    public const int MaxEntries = 4096;
    public const int MaxRecordsWalked = 64;

    /// <summary>Entries of a (resident or already-read) list. Stops at the first entry that cannot be trusted to advance; <paramref name="malformed"/> says so.</summary>
    public static List<AttributeListEntry> Parse(ReadOnlySpan<byte> v, out bool malformed)
    {
        malformed = false;
        var list = new List<AttributeListEntry>();
        int p = 0;
        while (p < v.Length)
        {
            if (list.Count >= MaxEntries) { malformed = true; break; }
            if (p + 26 > v.Length) { malformed = true; break; }
            int len = Le.U16(v, p + 4);
            if (len < 26 || len % 8 != 0 || p + len > v.Length) { malformed = true; break; }   // length 0 would never advance
            int nameLen = v[p + 6], nameOff = v[p + 7];
            string name = "";
            if (nameLen > 0)
            {
                if (nameOff < 26 || nameOff + nameLen * 2 > len) { malformed = true; p += len; continue; }
                name = NtfsText.DecodeUtf16(v.Slice(p + nameOff, nameLen * 2), out _);
            }
            list.Add(new AttributeListEntry(Le.U32(v, p), (ushort)len, Le.U64(v, p + 8), new MftReference(Le.U64(v, p + 16)), Le.U16(v, p + 24), name));
            p += len;
        }
        return list;
    }

    /// <summary>
    /// Record numbers (other than the root itself) reachable from <paramref name="root"/> through attribute lists. <paramref name="listOf"/> gives the list of a
    /// record or null. Each record is visited once and at most <see cref="MaxRecordsWalked"/> are visited; a loop (a list reaching a record already visited,
    /// including the root) is reported in <paramref name="cycle"/>, an entry pointing at its own record is just that record's own attributes.
    /// </summary>
    public static IReadOnlyList<ulong> Walk(ulong root, Func<ulong, IReadOnlyList<AttributeListEntry>?> listOf, out bool cycle, out bool truncated)
    {
        cycle = false; truncated = false;
        var parent = new Dictionary<ulong, ulong>();
        var found = new List<ulong>();
        var queue = new Queue<ulong>();
        queue.Enqueue(root);
        parent[root] = root;
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var entries = listOf(cur);
            if (entries is null) continue;
            foreach (var e in entries)
            {
                var n = e.Record.Number;
                if (n == cur) continue;               // own attributes
                // A target that is the current record's ancestor (or the root) closes a loop.
                var anc = cur;
                for (int i = 0; i <= MaxRecordsWalked; i++)
                {
                    if (anc == n) { cycle = true; break; }
                    if (parent[anc] == anc) break;
                    anc = parent[anc];
                }
                if (parent.ContainsKey(n)) continue;
                if (parent.Count > MaxRecordsWalked) { truncated = true; return found; }
                parent[n] = cur; found.Add(n); queue.Enqueue(n);
            }
        }
        return found;
    }
}

public sealed record DataRun(long Vcn, long Length, long? Lcn)
{
    public bool IsSparse => Lcn is null;
}

/// <summary>Mapping pairs (format card §4).</summary>
public static class DataRuns
{
    public const int MaxRuns = 4096;

    public sealed record Result(IReadOnlyList<DataRun> Runs, string? Error);

    /// <param name="totalClusters">Clusters in the volume, 0 when unknown (then LCNs are only checked to be non-negative).</param>
    public static Result Decode(ReadOnlySpan<byte> m, long lowestVcn, long highestVcn, long totalClusters = 0)
    {
        var runs = new List<DataRun>();
        int p = 0;
        long lcn = 0, vcn = lowestVcn, sum = 0;
        while (p < m.Length)
        {
            byte h = m[p];
            if (h == 0) break;
            int v = h & 0x0F, l = h >> 4;
            if (v < 1 || v > 8) return new(runs, $"run: lungimea are {v} octeți (1..8)");
            if (l > 8) return new(runs, $"run: offsetul are {l} octeți (0..8)");
            if (runs.Count >= MaxRuns) return new(runs, $"run: mai mult de {MaxRuns} run-uri");
            if (p + 1 + v + l > m.Length) return new(runs, "run: trunchiat");
            ulong ulen = 0;
            for (int i = v - 1; i >= 0; i--) ulen = (ulen << 8) | m[p + 1 + i];
            if (ulen == 0 || ulen > long.MaxValue) return new(runs, "run: lungime ≤ 0");
            long len = (long)ulen;
            long? runLcn = null;
            if (l > 0)
            {
                long delta = (sbyte)m[p + 1 + v + l - 1] < 0 ? -1 : 0;   // sign extension from the top byte
                for (int i = l - 1; i >= 0; i--) delta = (delta << 8) | m[p + 1 + v + i];
                try { lcn = checked(lcn + delta); } catch (OverflowException) { return new(runs, "run: LCN depășește intervalul"); }
                if (lcn < 0) return new(runs, "run: LCN negativ");
                if (totalClusters > 0 && (lcn >= totalClusters || len > totalClusters - lcn)) return new(runs, "run: în afara volumului");
                runLcn = lcn;
            }
            // l = 0: sparse run, the current LCN is NOT updated.
            if (sum > long.MaxValue - len) return new(runs, "run: suma lungimilor depășește intervalul");
            runs.Add(new DataRun(vcn, len, runLcn));
            sum += len; vcn += len;
            p += 1 + v + l;
        }
        long expected = highestVcn - lowestVcn + 1;
        if (highestVcn < lowestVcn - 1 || sum != expected) return new(runs, $"run: suma {sum} ≠ HighestVCN−LowestVCN+1 ({expected})");
        return new(runs, null);
    }
}
