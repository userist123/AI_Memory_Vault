using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Dfir.Detection;

public sealed record PeSection(string Name, uint VirtualSize, uint RawSize, double Entropy, bool Executable, bool Writable);

public sealed record PeImport(string Dll, IReadOnlyList<string> Functions);

/// <summary>An observation about a PE file with its reason. Never a verdict: a packed or unsigned program is not malicious by itself.</summary>
public sealed record PeIndicator(string Code, string Text, string WhyItMatters);

/// <summary>
/// What a static, read-only look at a Windows executable shows (master plan 6.7): header, compile time as written by the linker (forgeable),
/// sections with entropy and rights, imports, overlay, embedded Authenticode presence, hashes, and indicators that explain themselves.
/// </summary>
public sealed class PeReport
{
    public string Path { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Md5 { get; init; } = "";
    public long Size { get; init; }
    public bool IsPe { get; init; }
    public string Problem { get; init; } = "";
    public string Machine { get; init; } = "";
    public bool Is64 { get; init; }
    public bool IsDll { get; init; }
    public string Subsystem { get; init; } = "";
    public DateTimeOffset? LinkerTimestampUtc { get; init; }
    public List<PeSection> Sections { get; init; } = [];
    public List<PeImport> Imports { get; init; } = [];
    public string ImpHash { get; set; } = "";
    public long OverlaySize { get; init; }
    /// <summary>The import table was longer than the limits (DLLs, functions, names) and was cut; the counts are then lower bounds.</summary>
    public bool ImportsTruncated { get; set; }
    public bool HasEmbeddedSignature { get; init; }
    public bool HasTls { get; init; }
    public List<PeIndicator> Indicators { get; init; } = [];
}

/// <summary>One evidence file read as a PE, with the Authenticode verdict of the station that analysed it (embedded, or its own catalog; no network).</summary>
public sealed record PeTriageRow(string EvidenceId, string OriginalName, PeReport Report, string SignatureKind, string SignatureStatus, string Signer, bool SignatureValid);

/// <summary><c>Analysis/pe_triage.json</c>: written by the pipeline, read back when a case is opened.</summary>
public static class PeTriageFile
{
    public const string RelPath = "Analysis/pe_triage.json";
    public static void Write(string path, IReadOnlyList<PeTriageRow> rows) => LogAnalyzer.Dfir.IO.Json.Write(path, rows);
    public static List<PeTriageRow> Read(string path) => File.Exists(path) ? LogAnalyzer.Dfir.IO.Json.Read<List<PeTriageRow>>(path) : [];
}

/// <summary>
/// A bounded PE reader: every offset and count is checked against the file, sizes are capped, and a malformed file gives a report with a Problem,
/// never an exception. It reads; it never loads or runs the file.
/// </summary>
public static class PeTriage
{
    private const int MaxSections = 96, MaxImportDlls = 512, MaxFunctionsPerDll = 2048, MaxFunctionsTotal = 10_000, MaxNameLength = 128;
    /// <summary>Bytes read for the entropy of one section; a larger section is measured on its first part (the result says so in its name).</summary>
    private const int MaxEntropyBytes = 16 * 1024 * 1024;
    public const long MaxBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Imports often seen in injection, credential theft or download-and-run; their presence is context, not proof. Read from the shipped data
    /// (Analysis/Data/pe_notable_imports.json, embedded compressed) so the DLL does not carry these names as plain text.
    /// </summary>
    private static readonly Lazy<Dictionary<string, string>> NotableImportsData = new(() =>
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = System.Text.Json.JsonDocument.Parse(LogAnalyzer.Dfir.Analysis.ShippedData.Read("pe_notable_imports.json"));
        foreach (var e in doc.RootElement.GetProperty("imports").EnumerateArray())
            d[e.GetProperty("name").GetString() ?? ""] = e.GetProperty("meaning").GetString() ?? "";
        return d;
    });
    private static Dictionary<string, string> NotableImports => NotableImportsData.Value;
    public static PeReport Analyze(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) return new PeReport { Path = path, Problem = "fișierul nu există" };
        if (fi.Length > MaxBytes) return new PeReport { Path = path, Size = fi.Length, Problem = $"fișier peste {MaxBytes / (1024 * 1024)} MB: nu se analizează" };
        return Analyze(File.ReadAllBytes(path), path);
    }

    public static PeReport Analyze(byte[] b, string path = "")
    {
        string sha = Convert.ToHexStringLower(SHA256.HashData(b)), md5 = Convert.ToHexStringLower(MD5.HashData(b));
        PeReport Bad(string why) => new() { Path = path, Sha256 = sha, Md5 = md5, Size = b.Length, Problem = why };
        try
        {
            if (b.Length < 0x40 || b[0] != (byte)'M' || b[1] != (byte)'Z') return Bad("nu este un executabil Windows (lipsește semnătura MZ)");
            int pe = I32(b, 0x3C);
            if (pe < 0x40 || pe > b.Length - 24 || b[pe] != (byte)'P' || b[pe + 1] != (byte)'E' || b[pe + 2] != 0 || b[pe + 3] != 0) return Bad("antetul PE lipsește sau este în afara fișierului");
            int coff = pe + 4;
            ushort machine = U16(b, coff), nSect = U16(b, coff + 2), optSize = U16(b, coff + 16), chars = U16(b, coff + 18);
            uint stamp = U32(b, coff + 4);
            int opt = coff + 20;
            if (opt + optSize > b.Length || optSize < 2) return Bad("antetul opțional este trunchiat");
            ushort magic = U16(b, opt);
            bool is64 = magic == 0x20B;
            if (magic is not (0x10B or 0x20B)) return Bad($"antet opțional necunoscut (0x{magic:X})");
            ushort subsystem = optSize >= 70 ? U16(b, opt + 68) : (ushort)0;
            int ddBase = opt + (is64 ? 112 : 96);
            int ddCount = (int)Math.Min(U32(b, opt + (is64 ? 108 : 92)), 16);
            (uint Rva, uint Size) Dir(int i) => i < ddCount && ddBase + i * 8 + 8 <= opt + optSize ? (U32(b, ddBase + i * 8), U32(b, ddBase + i * 8 + 4)) : (0u, 0u);

            if (nSect > MaxSections) return Bad($"{nSect} secțiuni: depășește limita de {MaxSections}");
            int st = opt + optSize;
            if (st + nSect * 40 > b.Length) return Bad("tabela de secțiuni este în afara fișierului");
            var raw = new List<(string Name, uint Va, uint Vs, uint Rp, uint Rs, uint Ch)>();
            for (int i = 0; i < nSect; i++)
            {
                int s = st + i * 40;
                var name = Encoding.ASCII.GetString(b, s, 8).TrimEnd('\0');
                raw.Add((name, U32(b, s + 12), U32(b, s + 8), U32(b, s + 20), U32(b, s + 16), U32(b, s + 36)));
            }
            // never below the end of the headers (review 2026-11-08: sections with no raw data counted the headers as overlay)
            long endOfImage = Math.Max(st + nSect * 40L, raw.Count == 0 ? 0 : raw.Max(r => (long)r.Rp + r.Rs));
            var security = Dir(4);
            long overlay = Math.Max(0, b.Length - endOfImage);
            if (security.Rva > 0 && security.Rva >= endOfImage) overlay = Math.Max(0, overlay - security.Size);   // the signature sits after the image; it is not an overlay

            var r = new PeReport
            {
                Path = path, Sha256 = sha, Md5 = md5, Size = b.Length, IsPe = true, Is64 = is64, IsDll = (chars & 0x2000) != 0,
                Machine = machine switch { 0x14C => "x86", 0x8664 => "x64", 0xAA64 => "ARM64", 0x1C4 => "ARM", _ => $"0x{machine:X}" },
                Subsystem = subsystem switch { 2 => "interfață grafică", 3 => "consolă", 1 => "nativ (driver)", 0 => "", _ => subsystem.ToString() },
                LinkerTimestampUtc = stamp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(stamp),
                OverlaySize = overlay, HasEmbeddedSignature = security.Rva > 0 && security.Size > 8 && security.Rva + (long)security.Size <= b.Length,
                HasTls = Dir(9).Rva > 0,
            };
            var measured = new Dictionary<(uint, uint), double>();   // sections sharing the same raw range are measured once (review: 96 x 64 MB took 10 s)
            foreach (var x in raw)
            {
                int len = (int)Math.Min(Math.Min(x.Rs, (long)b.Length - x.Rp), MaxEntropyBytes);
                if (!measured.TryGetValue((x.Rp, (uint)len), out var ent))
                    measured[(x.Rp, (uint)len)] = ent = x.Rs > 0 && x.Rp < b.Length && len > 0 ? Entropy(b, (int)x.Rp, len) : 0;
                r.Sections.Add(new PeSection(x.Name, x.Vs, x.Rs, Math.Round(ent, 2), (x.Ch & 0x20000000) != 0, (x.Ch & 0x80000000) != 0));
            }

            long Off(uint rva)
            {
                foreach (var x in raw) if (rva >= x.Va && rva < x.Va + Math.Max(x.Vs, x.Rs)) return x.Rp + (rva - x.Va);
                return -1;
            }
            string Str(long off) { if (off < 0 || off >= b.Length) return ""; int e = (int)off; while (e < b.Length && e - off < MaxNameLength && b[e] != 0) e++; return Encoding.ASCII.GetString(b, (int)off, e - (int)off); }

            var imp = Dir(1);
            if (imp.Rva > 0 && Off(imp.Rva) is var io and >= 0)
            {
                // caps on the whole table, not only per DLL, and a lookup list shared by several descriptors is read once
                // (review 2026-11-08: 512 descriptors sharing one 2048-entry list gave a million functions and a 273 MB report)
                int total = 0;
                var seenLists = new HashSet<long>();
                for (int d = 0; d < MaxImportDlls; d++)
                {
                    long e = io + d * 20;
                    if (e + 20 > b.Length) break;
                    uint ilt = U32(b, (int)e), nameRva = U32(b, (int)e + 12), iat = U32(b, (int)e + 16);
                    if (ilt == 0 && nameRva == 0 && iat == 0) break;
                    var dll = Str(Off(nameRva));
                    var fns = new List<string>();
                    long t = Off(ilt != 0 ? ilt : iat);
                    int step = is64 ? 8 : 4;
                    if (t >= 0 && !seenLists.Add(t)) { r.ImportsTruncated = true; t = -1; }
                    for (int k = 0; t >= 0 && k < MaxFunctionsPerDll && t + (k + 1) * step <= b.Length; k++)
                    {
                        if (total >= MaxFunctionsTotal) { r.ImportsTruncated = true; break; }
                        total++;
                        ulong v = is64 ? BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan((int)(t + k * step))) : U32(b, (int)(t + k * step));
                        if (v == 0) break;
                        bool ordinal = is64 ? (v & 0x8000000000000000UL) != 0 : (v & 0x80000000UL) != 0;
                        var hint = ordinal ? -1 : Off((uint)(v & 0x7FFFFFFF));   // IMAGE_IMPORT_BY_NAME: a 2-byte hint, then the name
                        fns.Add(ordinal ? $"#{v & 0xFFFF}" : hint < 0 ? "?" : Str(hint + 2));
                    }
                    if (dll.Length > 0) r.Imports.Add(new PeImport(dll, fns));
                    if (d == MaxImportDlls - 1 || total >= MaxFunctionsTotal) r.ImportsTruncated |= d == MaxImportDlls - 1 || total >= MaxFunctionsTotal;
                }
            }
            r.ImpHash = ImpHash(r.Imports);
            Indicators(r);
            return r;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return Bad("structură PE invalidă: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// md5 of "dll.function" in import order, lower case, extension dropped, ordinals as "ord&lt;n&gt;". It differs from pefile/VirusTotal for
    /// programs importing oleaut32, ws2_32 or wsock32 by ordinal, which pefile translates to names (review 2026-11-08: 340 of 1,098 System32
    /// binaries); the screen calls it „imphash (ordinale netraduse)”.
    /// </summary>
    public static string ImpHash(IReadOnlyList<PeImport> imports)
    {
        var parts = new List<string>();
        foreach (var i in imports)
        {
            var dll = i.Dll.ToLowerInvariant();
            foreach (var ext in new[] { ".dll", ".ocx", ".sys" }) if (dll.EndsWith(ext, StringComparison.Ordinal)) { dll = dll[..^ext.Length]; break; }
            foreach (var f in i.Functions) parts.Add(dll + "." + (f.StartsWith('#') ? "ord" + f[1..] : f.ToLowerInvariant()));
        }
        return parts.Count == 0 ? "" : Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(string.Join(",", parts))));
    }

    public static double Entropy(byte[] b, int offset, int count)
    {
        if (count <= 0) return 0;
        Span<int> freq = stackalloc int[256];
        for (int i = offset; i < offset + count; i++) freq[b[i]]++;
        double e = 0;
        foreach (var f in freq) if (f > 0) { double p = (double)f / count; e -= p * Math.Log2(p); }
        return e;
    }

    private static void Indicators(PeReport r)
    {
        void Add(string c, string t, string w) => r.Indicators.Add(new PeIndicator(c, t, w));
        var high = r.Sections.Where(s => s.Entropy >= 7.2 && s.RawSize >= 1024).ToList();
        if (high.Count > 0) Add("PE-HIGH-ENTROPY", $"secțiuni cu entropie mare: {string.Join(", ", high.Select(s => $"{s.Name} {s.Entropy}"))}",
            "conținut comprimat sau criptat: se întâlnește la programe împachetate (packers), dar și la instalatoare și resurse comprimate");
        var wx = r.Sections.Where(s => s.Executable && s.Writable).ToList();
        if (wx.Count > 0) Add("PE-WX-SECTION", $"secțiuni care pot fi și scrise și executate: {string.Join(", ", wx.Select(s => s.Name))}",
            "codul care se modifică singur la rulare apare la despachetare sau injecție; compilatoarele obișnuite produc rar așa ceva");
        int fnCount = r.Imports.Sum(i => i.Functions.Count);
        if (!r.IsDll && fnCount is > 0 and < 8) Add("PE-FEW-IMPORTS", $"doar {fnCount} funcții importate",
            "un program care importă foarte puțin își rezolvă de obicei funcțiile singur, la rulare (frecvent la programe împachetate)");
        var notable = r.Imports.SelectMany(i => i.Functions).Where(NotableImports.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (notable.Count > 0) Add("PE-NOTABLE-IMPORTS", "importă: " + string.Join("; ", notable.Select(n => $"{n} ({NotableImports[n]})")),
            "aceste funcții sunt folosite și de programe legitime (depanatoare, utilitare de administrare); împreună cu alte urme pot indica injecție sau furt de date");
        if (r.LinkerTimestampUtc is { } t && t > DateTimeOffset.UtcNow.AddDays(1)) Add("PE-FUTURE-TIMESTAMP", $"momentul compilării este în viitor ({t:yyyy-MM-dd})",
            "momentul compilării este scris de linker și poate fi falsificat sau înlocuit (build-uri reproductibile)");
        if (r.OverlaySize > 4096) Add("PE-OVERLAY", $"{r.OverlaySize:N0} octeți după sfârșitul imaginii (overlay)",
            "instalatoarele și arhivele auto-extractibile pun date acolo; la fel și programele care ascund un al doilea fișier");
        if (r.HasTls) Add("PE-TLS", "are funcții TLS (rulează înainte de punctul de intrare)",
            "folosite legitim de unele biblioteci, dar și pentru a rula cod înainte de depanator");
        if (r.ImportsTruncated) Add("PE-IMPORTS-TRUNCATED", "tabela de importuri depășește limitele de citire și a fost citită parțial",
            "o tabelă de importuri neobișnuit de mare sau repetată apare în fișiere construite să încurce analiza");
        if (!r.HasEmbeddedSignature) Add("PE-UNSIGNED", "nu are semnătură Authenticode încorporată",
            "multe programe Windows sunt semnate prin cataloage, nu în fișier; lipsa semnăturii încorporate nu înseamnă că fișierul este rău intenționat");
    }

    private static ushort U16(byte[] b, int o) => o >= 0 && o + 2 <= b.Length ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)) : throw new ArgumentOutOfRangeException(nameof(o));
    private static uint U32(byte[] b, int o) => o >= 0 && o + 4 <= b.Length ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : throw new ArgumentOutOfRangeException(nameof(o));
    private static int I32(byte[] b, int o) => o >= 0 && o + 4 <= b.Length ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o)) : throw new ArgumentOutOfRangeException(nameof(o));
}
