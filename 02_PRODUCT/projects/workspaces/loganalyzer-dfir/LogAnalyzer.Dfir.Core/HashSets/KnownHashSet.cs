using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.HashSets;

/// <summary>The two categories of a fingerprint set. A hash in both categories is reported under both (visible conflict).</summary>
public static class HashSetCategory
{
    public const string KnownGood = "cunoscut-bun";
    public const string KnownBad = "cunoscut-rău";
    public static bool IsValid(string? c) => c is KnownGood or KnownBad;
    public static string Normalize(string? c) => (c ?? "").Trim().ToLowerInvariant() switch
    {
        "cunoscut-bun" or "bun" or "good" => KnownGood,
        "cunoscut-rău" or "cunoscut-rau" or "rău" or "rau" or "bad" => KnownBad,
        _ => "",
    };
}

/// <summary>Metadata of an imported set (what the list shows and what a match cites as the set's source). Hashes are kept beside it, not in this record.</summary>
public sealed class KnownHashSetInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>Subset of SHA-256, SHA-1, MD5 present in the set.</summary>
    public List<string> Algorithms { get; set; } = [];
    public int EntryCount { get; set; }
    public string SourceFileName { get; set; } = "";
    /// <summary>SHA-256 of the imported source file (the bytes that were parsed).</summary>
    public string SourceSha256 { get; set; } = "";
    /// <summary>SHA-256 of the stored &lt;id&gt;.hashes.txt; also in the audit entry of the import, which is what a load is checked against.</summary>
    public string StoredSha256 { get; set; } = "";
    public DateTimeOffset ImportedUtc { get; set; }
    /// <summary>The signed-in account that imported the set; empty when nobody is signed in.</summary>
    public string ImportedBy { get; set; } = "";
    [JsonIgnore] public string Version => SourceSha256.Length >= 12 ? SourceSha256[..12] : SourceSha256;
    /// <summary>What the set can match: the case's evidence items are compared by SHA-256 only; MD5 / SHA-1 reach only event fields that carry them (Sysmon, Amcache).</summary>
    [JsonIgnore] public bool CoversEvidenceSamples => Algorithms?.Contains("SHA-256") == true;
}

internal readonly struct Fp160(ulong a, ulong b, uint c) : IComparable<Fp160>
{
    public readonly ulong A = a, B = b; public readonly uint C = c;
    public int CompareTo(Fp160 o) => A != o.A ? A.CompareTo(o.A) : B != o.B ? B.CompareTo(o.B) : C.CompareTo(o.C);
}

internal readonly struct Fp256(ulong a, ulong b, ulong c, ulong d) : IComparable<Fp256>
{
    public readonly ulong A = a, B = b, C = c, D = d;
    public int CompareTo(Fp256 o) => A != o.A ? A.CompareTo(o.A) : B != o.B ? B.CompareTo(o.B) : C != o.C ? C.CompareTo(o.C) : D.CompareTo(o.D);
}

/// <summary>
/// Compact storage of the hashes: three sorted arrays of fixed-size binary values (MD5 = UInt128 = 16 bytes, SHA-1 = 24 bytes, SHA-256 = 32 bytes), binary search.
/// Chosen over a HashSet of strings (about 170 bytes per SHA-256) and over a HashSet of structs (about 45): no per-entry object, no load-factor slack, one allocation per
/// algorithm, so 1 million SHA-256 take about 32 MiB and 10 million about 320 MiB.
/// </summary>
public sealed class CompactHashes
{
    private UInt128[] _md5 = [];
    private Fp160[] _sha1 = [];
    private Fp256[] _sha256 = [];

    public int Count => _md5.Length + _sha1.Length + _sha256.Length;

    private static int Nibble(char c) => c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
    private static ulong U(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadUInt64BigEndian(s[o..]);

    internal static bool TryParse(ReadOnlySpan<char> hex, out UInt128 md5, out Fp160 sha1, out Fp256 sha256)
    {
        md5 = default; sha1 = default; sha256 = default;
        if (hex.Length is not (32 or 40 or 64)) return false;
        Span<byte> b = stackalloc byte[32];
        var n = hex.Length / 2;
        for (int i = 0; i < n; i++)
        {
            int hi = Nibble(hex[2 * i]), lo = Nibble(hex[2 * i + 1]);
            if ((hi | lo) < 0) return false;
            b[i] = (byte)((hi << 4) | lo);
        }
        switch (hex.Length)
        {
            case 32: md5 = new UInt128(U(b, 0), U(b, 8)); break;
            case 40: sha1 = new Fp160(U(b, 0), U(b, 8), BinaryPrimitives.ReadUInt32BigEndian(b[16..])); break;
            default: sha256 = new Fp256(U(b, 0), U(b, 8), U(b, 16), U(b, 24)); break;
        }
        return true;
    }

    /// <summary>Case-insensitive; false for anything that is not a 32 / 40 / 64 character hex string.</summary>
    public bool Contains(ReadOnlySpan<char> hex) =>
        TryParse(hex, out var m, out var s1, out var s2) && hex.Length switch
        {
            32 => Array.BinarySearch(_md5, m) >= 0,
            40 => Array.BinarySearch(_sha1, s1) >= 0,
            _ => Array.BinarySearch(_sha256, s2) >= 0,
        };

    /// <summary>Upper-case hex of every entry: the SHA-256, SHA-1 and MD5 groups in turn, each sorted by value.</summary>
    public IEnumerable<string> EnumerateHex()
    {
        foreach (var h in _sha256) yield return $"{h.A:X16}{h.B:X16}{h.C:X16}{h.D:X16}";
        foreach (var h in _sha1) yield return $"{h.A:X16}{h.B:X16}{h.C:X8}";
        foreach (var h in _md5) yield return $"{(ulong)(h >> 64):X16}{(ulong)h:X16}";
    }

    public IReadOnlyList<string> Algorithms()
    {
        var l = new List<string>();
        if (_sha256.Length > 0) l.Add("SHA-256");
        if (_sha1.Length > 0) l.Add("SHA-1");
        if (_md5.Length > 0) l.Add("MD5");
        return l;
    }

    /// <summary>Collects hashes (duplicates allowed while adding; removed by <see cref="Compact"/> and <see cref="Build"/>).</summary>
    public sealed class Builder
    {
        private readonly List<UInt128> _md5 = [];
        private readonly List<Fp160> _sha1 = [];
        private readonly List<Fp256> _sha256 = [];
        public int Count => _md5.Count + _sha1.Count + _sha256.Count;

        /// <summary>False when <paramref name="hex"/> is not 32 / 40 / 64 hexadecimal characters.</summary>
        public bool Add(ReadOnlySpan<char> hex)
        {
            if (!TryParse(hex, out var m, out var s1, out var s2)) return false;
            switch (hex.Length) { case 32: _md5.Add(m); break; case 40: _sha1.Add(s1); break; default: _sha256.Add(s2); break; }
            return true;
        }

        private static T[] SortedUnique<T>(List<T> l) where T : struct, IComparable<T>
        {
            var a = l.ToArray(); Array.Sort(a);
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (n == 0 || a[i].CompareTo(a[n - 1]) != 0) a[n++] = a[i];
            if (n != a.Length) Array.Resize(ref a, n);
            return a;
        }

        /// <summary>Removes duplicates in place (keeps memory bounded while a big list is read).</summary>
        public void Compact()
        {
            var a = SortedUnique(_md5); _md5.Clear(); _md5.AddRange(a);
            var b = SortedUnique(_sha1); _sha1.Clear(); _sha1.AddRange(b);
            var c = SortedUnique(_sha256); _sha256.Clear(); _sha256.AddRange(c);
        }

        public CompactHashes Build() => new() { _md5 = SortedUnique(_md5), _sha1 = SortedUnique(_sha1), _sha256 = SortedUnique(_sha256) };
    }
}

/// <summary>A loaded set: metadata plus the compact hashes (32 = MD5, 40 = SHA-1, 64 = SHA-256 hex; case-insensitive on lookup).</summary>
public sealed class KnownHashSet
{
    public KnownHashSet(KnownHashSetInfo info, CompactHashes hashes) { Info = info; Hashes = hashes; }

    /// <summary>Builds the set from hex strings of any case; invalid ones are ignored here (the importer and the loader check them and report).</summary>
    public KnownHashSet(KnownHashSetInfo info, IEnumerable<string> hashes)
    {
        Info = info;
        var b = new CompactHashes.Builder();
        foreach (var h in hashes) b.Add(h);
        Hashes = b.Build();
    }

    public KnownHashSetInfo Info { get; }
    public CompactHashes Hashes { get; }
    public bool Contains(string hexHash) => Hashes.Contains(hexHash);

    public static string? AlgorithmOf(string hex) => hex.Length switch { 64 => "SHA-256", 40 => "SHA-1", 32 => "MD5", _ => null };
}
