using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// Microsoft Defender detection history: one binary file per detection in
/// <c>%ProgramData%\Microsoft\Windows Defender\Scans\History\Service\DetectionHistory\NN\GUID</c> (no extension). The files outlive the
/// <c>Microsoft-Windows-Windows Defender/Operational</c> log, so a detection that is here and not in the log shows that the log was cleared or rotated.
/// The file is a chain of TLV records (u32 length, u32 type, value, padding to 8 bytes); a key/value block (type 0x28) carries the hashes and the
/// threat-tracking data. The file is untrusted data: every length is checked as a 64-bit number against the file and the block, the record count,
/// the string length and the file size are capped, a damaged file gives what could be read and never an exception, and the paths it names are only
/// text to display (the file they name is never opened).
/// </summary>
public sealed class DefenderDetectionHistoryParser : EvidenceParserBase
{
    public const string SourceTypeName = "defender_detection_history";
    public const string FingerprintName = "defender_dh";
    public const string EventSource = "Artifact:DefenderDetectionHistory";
    public const string CollectedBy = "DefenderDetectionHistoryCollector";
    /// <summary>Largest file read; a real record is about 2 KiB. A larger one is described in a gap and not read.</summary>
    public const long MaxFileBytes = 1L * 1024 * 1024;
    public const int MaxRecords = 4_096;
    public const int MaxStringChars = 4_096;
    public const int MaxResources = 64;
    public const int MaxKeys = 256;

    private const uint TypeThreatId = 0x08, TypeFileTime = 0x0A, TypeString = 0x15, TypeGuid = 0x1E, TypeBlock = 0x28, TypeU32 = 0x06;
    private const string MagicPrefix = "Magic.Version";

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "DefenderDetectionHistoryParser", Version = "1.0", Artifact = "Istoricul detecțiilor Microsoft Defender (DetectionHistory)",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = [FingerprintName], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows 10 / 11 și Windows Server cu Microsoft Defender (format binar citit în cod gestionat)",
        FormatVersions = ["înregistrări TLV (lungime, tip, valoare, umplutură la 8 octeți) și bloc cheie/valoare 0x28; „Magic.Version:1.2”"],
        Limitations =
        [
            "Formatul nu este documentat de Microsoft: câmpurile sunt cele verificate pe înregistrări reale (detecție EICAR); un tip de înregistrare necunoscut este sărit, un bloc de chei cu tip necunoscut este citit doar până la el.",
            "Câmpurile care depind de blocul de chei (ora detecției, utilizatorul, procesul, hash-urile) rămân goale când fișierul nu are acest bloc; nu se inventează valori.",
            "Calea este textul scris de Defender; fișierul indicat nu este deschis și poate să nu mai existe.",
            "Fișierul arată că Defender a înregistrat o detecție, nu că programul a rulat și nu ce a făcut Defender după aceea.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "DefenderDetectionHistoryParserTests: înregistrare reală EICAR din laborator (toate câmpurile comparate cu evenimentul 1116), fișiere trunchiate la fiecare decalaj, lungimi uriașe, bloc corupt, ore invalide, șiruri fără NUL, caractere de control",
    };

    /// <summary>What one detection file says. Empty text / null where the file does not say it.</summary>
    public sealed record Detection(string DetectionId, long? ThreatId, string ThreatName, int? SeverityId, int? CategoryId, int? SourceId,
        IReadOnlyList<(string Type, string Path)> Resources, DateTime? DetectionTimeUtc, string TimeSource, string User, string Process,
        string Sha256, string Sha1, string Md5, string Size, bool HasKeyBlock, bool Complete, int Records)
    {
        public string PathText => string.Join("; ", Resources.Select(r => $"{r.Type}:_{r.Path}"));
    }

    private readonly record struct Rec(uint Type, int Start, int Len);

    /// <summary>Quick content test for the fingerprint: first record is an 8-byte ThreatId (0x08), the second a 16-byte GUID (0x1E).</summary>
    public static bool LooksLikeHistory(ReadOnlySpan<byte> head, long length) =>
        length >= 0x28 && head.Length >= 0x18
        && BinaryPrimitives.ReadUInt32LittleEndian(head) == 8 && BinaryPrimitives.ReadUInt32LittleEndian(head[4..]) == TypeThreatId
        && BinaryPrimitives.ReadUInt32LittleEndian(head[0x10..]) == 16 && BinaryPrimitives.ReadUInt32LittleEndian(head[0x14..]) == TypeGuid;

    /// <summary>Text for display: line breaks and tabs become spaces, other hidden and bidi control characters are removed and named (<see cref="ReportText.Clean"/>).</summary>
    public static string Safe(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var flat = string.Create(s.Length, s, (span, src) => { for (int i = 0; i < span.Length; i++) span[i] = src[i] is '\r' or '\n' or '\t' ? ' ' : src[i]; });
        return ReportText.Clean(flat);
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        byte[] data;
        using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var buf = new byte[(int)Math.Min(fs.Length, MaxFileBytes + 1)];
            int n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            if (n > MaxFileBytes)
            {
                result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Failed, $"fișierul depășește {MaxFileBytes} octeți", "Detecția nu a fost citită", "Fișierul brut din caz", "Posibil"));
                return;
            }
            data = buf.AsSpan(0, n).ToArray();
        }
        var d = Decode(data);
        if (d.DetectionId.Length == 0 && d.ThreatName.Length == 0)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial, "fișierul nu conține nici identificatorul detecției, nici numele amenințării", "Detecția nu poate fi folosită", "Fișierul brut din caz", "Posibil"));
            return;
        }
        if (!d.Complete)
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial, "citirea s-a oprit înaintea sfârșitului fișierului (înregistrare trunchiată, lungime nevalidă sau prea multe înregistrări)", "Pot lipsi câmpuri ale detecției", "Fișierul brut din caz", "Posibil"));

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Detection ID"] = d.DetectionId,
            ["Threat Name"] = d.ThreatName,
            ["Threat ID"] = d.ThreatId?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["Severity ID"] = d.SeverityId?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["Category ID"] = d.CategoryId?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["Source ID"] = d.SourceId?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["Path"] = d.PathText,
            ["Detection User"] = d.User,
            ["Process Name"] = d.Process,
            ["SHA256"] = d.Sha256, ["SHA1"] = d.Sha1, ["MD5"] = d.Md5, ["Size"] = d.Size,
            ["Detection Time"] = d.DetectionTimeUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            ["Resource Types"] = string.Join(", ", d.Resources.Select(r => r.Type).Distinct()),
        };
        var first = d.Resources.Count > 0 ? d.Resources[0].Path : "";
        sink.Add(new TimelineEvent
        {
            Time = d.DetectionTimeUtc is { } t ? Timestamp.FromUtc(t, t.ToString("o", CultureInfo.InvariantCulture), "Defender DetectionHistory FILETIME") : Timestamp.Unknown(),
            TimeSemantics = d.TimeSource.Length > 0 ? $"Defender detection time ({d.TimeSource})" : "Defender detection time (not in the file)",
            TimeZoneBasis = "UTC (native)",
            Source = EventSource, EvidenceId = item.EvidenceId, Provider = "Microsoft Defender DetectionHistory",
            User = Safe(d.User), Process = d.Process.Length > 0 ? Safe(WinPath.GetFileName(d.Process)) : "", Path = Safe(first), Hash = d.Sha256,
            Summary = $"Istoricul Defender: detecția {(d.ThreatName.Length > 0 ? Safe(d.ThreatName) : "(fără nume)")}{(first.Length > 0 ? $" pentru {Safe(first)}" : "")}",
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High,
            Locator = "DetectionHistory/" + Path.GetFileName(fullPath),
            Fields = fields,
        });
        result.Records++;
    }

    /// <summary>Reads one detection file. Never throws, whatever the bytes are.</summary>
    public static Detection Decode(ReadOnlySpan<byte> bytes) => DecodeCore(bytes.ToArray());

    private static Detection DecodeCore(byte[] d)
    {
        // 1. the chain of records; progress is at least 8 bytes per record
        var recs = new List<Rec>();
        long off = 0;
        bool complete = true;
        while (off + 8 <= d.Length)
        {
            if (recs.Count >= MaxRecords) { complete = false; break; }
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)off)), type = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)(off + 4)));
            long start = off + 8;
            if (len > d.Length - start) { complete = false; break; }
            recs.Add(new Rec(type, (int)start, (int)len));
            off = start + ((len + 7L) & ~7L);
        }
        if (complete && off < d.Length) complete = false;   // bytes left that are not even a record header

        string Str(Rec r) => DecodeString(d.AsSpan(r.Start, r.Len));
        uint U32(Rec r) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(r.Start));
        int blockIdx = recs.FindIndex(r => r.Type == TypeBlock);
        int preEnd = blockIdx >= 0 ? blockIdx : recs.Count;

        // 2. before the block: threat id, detection GUID, threat name, severity / category, resources
        long? threatId = null;
        string id = "";
        for (int i = 0; i < preEnd; i++)
            if (threatId is null && recs[i].Type == TypeThreatId && recs[i].Len >= 8) threatId = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(recs[i].Start)));
        for (int i = 0; i < preEnd; i++)
            if (recs[i].Type == TypeGuid && recs[i].Len == 16)
            {
                var guid = new Guid(d.AsSpan(recs[i].Start, 16));
                if (guid != Guid.Empty) id = "{" + guid.ToString().ToUpperInvariant() + "}";
                break;
            }

        string name = ""; int nameIdx = -1;
        for (int i = 0; i < preEnd && nameIdx < 0; i++)
            if (recs[i].Type == TypeString && Str(recs[i]) is { Length: > 0 } s && !s.StartsWith(MagicPrefix, StringComparison.Ordinal)) { name = s; nameIdx = i; }
        int? sev = null, cat = null;
        if (nameIdx >= 0)
        {
            var nums = new List<uint>();
            for (int i = nameIdx + 1; i < preEnd && nums.Count < 3 && recs[i].Type == TypeU32 && recs[i].Len >= 4; i++) nums.Add(U32(recs[i]));
            if (nums.Count > 1 && nums[1] <= int.MaxValue) sev = (int)nums[1];
            if (nums.Count > 2 && nums[2] <= int.MaxValue) cat = (int)nums[2];
        }
        var resources = new List<(string, string)>();
        for (int i = 0; i + 2 < preEnd && resources.Count < MaxResources; i++)
        {
            if (recs[i].Type != TypeString || !Str(recs[i]).StartsWith(MagicPrefix, StringComparison.Ordinal)) continue;
            if (recs[i + 1].Type != TypeString || recs[i + 2].Type != TypeString) continue;
            string type = Str(recs[i + 1]), path = Str(recs[i + 2]);
            if (type.StartsWith(MagicPrefix, StringComparison.Ordinal) || type.Length == 0 && path.Length == 0) continue;
            resources.Add((type, path));
            i += 2;
        }

        // 3. the key/value block
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (blockIdx >= 0) ReadBlock(d.AsSpan(recs[blockIdx].Start, recs[blockIdx].Len), keys);
        if (keys.TryGetValue("ThreatTrackingThreatName", out var kn) && kn.Length > 0) name = kn;
        int? source = keys.TryGetValue("ThreatTrackingScanSource", out var ss) && int.TryParse(ss, NumberStyles.None, CultureInfo.InvariantCulture, out var si) ? si : null;
        if (threatId is null && keys.TryGetValue("ThreatTrackingThreatId", out var kt) && long.TryParse(kt, NumberStyles.None, CultureInfo.InvariantCulture, out var kti)) threatId = kti;

        // 4. after the block: detection time, user, process (without a block they stay empty)
        DateTime? when = null; string timeSource = "", user = "", process = "";
        if (blockIdx >= 0)
        {
            var post = recs.Skip(blockIdx + 1).ToList();
            int ftIdx = post.FindIndex(r => r.Type == TypeFileTime && r.Len >= 8);
            if (ftIdx >= 0 && FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(post[ftIdx].Start))) is { } w) { when = w; timeSource = "FILETIME după blocul de chei"; }
            // Lab (EICAR, then the Defender log cleared): Defender rewrote the file and the FILETIME after the block moved 13 s later, while
            // ThreatTrackingStartTime stayed within 1 ms of the 1116 Detection Time. In a fresh file the FILETIME is the exact 1116 time, so it is
            // kept unless it lies more than a second after ThreatTrackingStartTime (a rewritten file).
            if (keys.TryGetValue("ThreatTrackingStartTime", out var st) && long.TryParse(st, NumberStyles.None, CultureInfo.InvariantCulture, out var sti) && FromFileTime(sti) is { } w2
                && (when is null || when.Value - w2 > TimeSpan.FromSeconds(1)))
            { when = w2; timeSource = "ThreatTrackingStartTime"; }
            var strings = post.Where(r => r.Type == TypeString).Select(Str).ToList();
            int u = strings.FindIndex(s => s.Contains('\\') &&!s.Contains(':'));
            if (u >= 0) { user = strings[u]; if (u + 1 < strings.Count) process = strings[u + 1]; }
        }

        string K(params string[] names) { foreach (var n in names) if (keys.TryGetValue(n, out var v)) return v; return ""; }
        return new Detection(id, threatId, name, sev, cat, source, resources, when, timeSource, user, process,
            K("ThreatTrackingSha256", "ThreatTrackingSha256Hash"), K("ThreatTrackingSha1"), K("ThreatTrackingMD5"), K("ThreatTrackingSize"),
            blockIdx >= 0, complete, recs.Count);
    }

    /// <summary>A FILETIME as a UTC time; null for zero, negative or beyond-calendar values.</summary>
    private static DateTime? FromFileTime(long ft) => ft > 0 && ft <= Timestamp.MaxFileTime ? DateTime.FromFileTimeUtc(ft) : null;

    /// <summary>UTF-16 LE text up to the first NUL, at most <see cref="MaxStringChars"/> characters; an odd trailing byte is dropped, broken surrogates become U+FFFD.</summary>
    private static string DecodeString(ReadOnlySpan<byte> v)
    {
        int n = Math.Min(v.Length & ~1, MaxStringChars * 2);
        int chars = 0;
        while (chars * 2 < n && (v[chars * 2] | v[chars * 2 + 1]) != 0) chars++;
        return Encoding.Unicode.GetString(v[..(chars * 2)]);
    }

    /// <summary>
    /// The pairs of the 0x28 block: header of 0x18 bytes, then key length, key (UTF-16 with NUL), value type and value (6 = length + text, 4 = 8 bytes,
    /// 3 = 4 bytes, 5 = 1 byte). A length that is odd or goes past the block, or an unknown value type, ends the block with what was read.
    /// </summary>
    private static void ReadBlock(ReadOnlySpan<byte> b, Dictionary<string, string> keys)
    {
        long p = 0x18;
        while (keys.Count < MaxKeys && p + 4 <= b.Length)
        {
            uint kl = BinaryPrimitives.ReadUInt32LittleEndian(b[(int)p..]); p += 4;
            if (kl == 0 || (kl & 1) != 0 || kl > b.Length - p) return;
            var key = DecodeString(b.Slice((int)p, (int)kl)); p += kl;
            if (p + 4 > b.Length) return;
            uint vt = BinaryPrimitives.ReadUInt32LittleEndian(b[(int)p..]); p += 4;
            string value;
            switch (vt)
            {
                case 6:
                    if (p + 4 > b.Length) return;
                    uint n = BinaryPrimitives.ReadUInt32LittleEndian(b[(int)p..]); p += 4;
                    if (n > b.Length - p) return;
                    value = DecodeString(b.Slice((int)p, (int)n)); p += n; break;
                case 4:
                    if (p + 8 > b.Length) return;
                    value = BinaryPrimitives.ReadUInt64LittleEndian(b[(int)p..]).ToString(CultureInfo.InvariantCulture); p += 8; break;
                case 3:
                    if (p + 4 > b.Length) return;
                    value = BinaryPrimitives.ReadUInt32LittleEndian(b[(int)p..]).ToString(CultureInfo.InvariantCulture); p += 4; break;
                case 5:
                    if (p + 1 > b.Length) return;
                    value = b[(int)p].ToString(CultureInfo.InvariantCulture); p += 1; break;
                default: return;
            }
            keys[key] = value;
        }
    }
}
