using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Offline SYSTEM hive: BAM (Background Activity Moderator: last execution per user, an EXECUTION artifact) and
/// ShimCache/AppCompatCache (paths the system examined, with the FILE's last-modified time: PRESENCE, not execution,
/// on Windows 10/11). Works on a hive saved from this station (reg save) or imported from another one.
/// </summary>
public sealed class SystemHiveExecutionParser : EvidenceParserBase
{
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "SystemHiveExecutionParser", Version = "1.0", Artifact = "Hive SYSTEM — BAM și ShimCache (AppCompatCache)",
        SourceTypes = ["system_hive"], FileNames = ["SYSTEM", "SYSTEM.hiv", "HKLM_SYSTEM.hiv"], Fingerprints = ["regf"],
        SupportedOs = "Oricare (citire offline cu RawRegistry)",
        FormatVersions = ["BAM: Windows 10 1709+ (bam\\State\\UserSettings) și bam\\UserSettings", "ShimCache: intrări 10ts (Windows 8.1 / 10 / 11), inclusiv valori big-data peste 16 KB", "BAM: căi de executabile și aplicații împachetate (UWP)"],
        Limitations =
        [
            "Jurnalele de tranzacții ale hive-ului (.LOG1/.LOG2) nu sunt aplicate.",
            "ShimCache pentru Windows 7 / XP nu este decodat (gol PARTIAL).",
            "Ora ShimCache este ultima modificare a fișierului, nu rularea; BAM păstrează doar ultima rulare per utilizator.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "ExecutionArtifactParserTests: hive SYSTEM real din corpus comparat cu reg query (BAM 72 valori, AppCompatCache identic octet cu octet) și hive-uri sintetice",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reg = new RawRegistry(fs);
        int current = reg.ReadValue("Select", "Current") is { Length: >= 4 } c ? BinaryPrimitives.ReadInt32LittleEndian(c) : 1;
        var cs = $"ControlSet{current:D3}";

        // BAM: ControlSet\Services\bam\State\UserSettings\<SID> (older builds: bam\UserSettings).
        var bamPath = reg.OpenKey($@"{cs}\Services\bam\State\UserSettings") is not null ? $@"{cs}\Services\bam\State\UserSettings" : $@"{cs}\Services\bam\UserSettings";
        var bamKeys = reg.SubKeys(bamPath).ToList();
        if (reg.OpenKey(bamPath) is null)
            result.Gaps.Add(new EvidenceGap("BAM", EvidenceStatus.NotAvailable, "cheia bam lipsește din hive (Windows mai vechi de 10 1709 sau dezactivat)",
                "Ultima execuție per utilizator nu este disponibilă din BAM", "Prefetch, Amcache, jurnalul 4688", "Nu"));
        else
            foreach (var k in bamKeys)
            {
                ct.ThrowIfCancellationRequested();
                var sid = k.Path[(k.Path.LastIndexOf('\\') + 1)..];
                foreach (var v in k.Values)
                {
                    // Values are executable device paths or, for packaged (UWP) apps, the package family name.
                    var name = v.Name;
                    var data = v.Data;
                    if (v.Type != RawValue.RegBinary || data.Length < 8) continue;
                    long ft = BinaryPrimitives.ReadInt64LittleEndian(data);
                    if (ft <= 0) continue;
                    bool packaged = !name.Contains('\\');
                    var path = packaged ? name : DevicePathToDisplay(name);
                    sink.Add(new TimelineEvent
                    {
                        Time = Timestamp.FromFileTime(ft, "BAM FILETIME"),
                        Source = "BAM", EvidenceId = item.EvidenceId, User = sid, Path = path,
                        Process = Path.GetFileName(path),
                        Summary = packaged ? $"BAM: aplicația împachetată {name} rulată ultima dată de utilizatorul {sid}"
                                           : $"BAM: {Path.GetFileName(path)} rulat ultima dată de utilizatorul {sid}",
                        TimeSemantics = "last execution (BAM)", TemporalType = TemporalType.Historical,
                        Classification = Classification.Direct, Confidence = Confidence.High,
                        Locator = $@"SYSTEM\{cs}\Services\bam\...\{sid}\{name}",
                        Fields = { ["Sid"] = sid, ["DevicePath"] = name },
                    });
                    result.Records++;
                }
            }

        // ShimCache.
        // AppCompatCache is usually > 16 KB, i.e. a big-data value that DiscUtils truncates to its 12-byte "db" header.
        var acc = reg.ReadValue($@"{cs}\Control\Session Manager\AppCompatCache", "AppCompatCache");
        if (acc is null)
            result.Gaps.Add(new EvidenceGap("ShimCache", EvidenceStatus.NotAvailable, "valoarea AppCompatCache lipsește", "Fără listă ShimCache", "Amcache", "Nu"));
        else
        {
            int n = 0;
            foreach (var (path, modified, order) in ShimCache.Parse(acc))
            {
                sink.Add(new TimelineEvent
                {
                    Time = modified is long ft && ft > 0 ? Timestamp.FromFileTime(ft, "ShimCache last modified") : Timestamp.Unknown(),
                    Source = "ShimCache", EvidenceId = item.EvidenceId, Path = path, Process = Path.GetFileName(path),
                    Summary = $"ShimCache: {path} (prezență; ora = ultima modificare a fișierului, nu rularea)",
                    TimeSemantics = "file last modified (ShimCache) — not execution",
                    TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                    Locator = $@"SYSTEM\{cs}\Control\Session Manager\AppCompatCache #{order}",
                    Fields = { ["CacheOrder"] = order.ToString() },
                });
                n++;
            }
            result.Records += n;
            if (n == 0)
                result.Gaps.Add(new EvidenceGap("ShimCache", EvidenceStatus.Partial, $"format necunoscut (antet 0x{(acc.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(acc) : 0):X})",
                    "Intrările ShimCache nu au fost decodate", "Analiză cu alt instrument", "Da"));
        }
    }

    /// <summary>"\Device\HarddiskVolume3\Windows\x.exe" → "\Windows\x.exe" (the volume letter is not recorded).</summary>
    public static string DevicePathToDisplay(string p) =>
        p.StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase) && p.IndexOf('\\', 22) is int i and > 0 ? p[i..] : p;
}

/// <summary>AppCompatCache decoder for Windows 8.1/10/11 ("10ts" entries).</summary>
public static class ShimCache
{
    public static IEnumerable<(string Path, long? Modified, int Order)> Parse(byte[] d)
    {
        if (d.Length < 0x34) yield break;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(d);          // header size: 0x30 (Win10 1507) or 0x34 (later)
        if (offset is not (0x30 or 0x34) || offset >= d.Length) yield break;
        int order = 0;
        while (offset + 12 <= d.Length)
        {
            if (Encoding.ASCII.GetString(d, offset, 4) != "10ts") yield break;
            int entrySize = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(offset + 8));
            int p = offset + 12;
            if (entrySize <= 0 || p + entrySize > d.Length) yield break;
            int pathLen = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p));
            if (p + 2 + pathLen + 8 > d.Length) yield break;
            var path = Encoding.Unicode.GetString(d, p + 2, pathLen);
            long modified = BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(p + 2 + pathLen));
            yield return (path, modified, order++);
            offset = p + entrySize;
        }
    }
}

/// <summary>
/// Amcache.hve: Root\InventoryApplicationFile — programs present on the station with SHA-1, publisher, version and
/// PE link date. Presence (and usually installation/first run), not proof of each execution.
/// </summary>
public sealed class AmcacheParser : EvidenceParserBase
{
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "AmcacheParser", Version = "1.0", Artifact = "Amcache.hve — inventarul fișierelor de aplicații",
        SourceTypes = ["amcache"], FileNames = ["Amcache.hve"], Fingerprints = ["regf"],
        SupportedOs = "Oricare (citire offline cu RawRegistry)",
        FormatVersions = ["Root\\InventoryApplicationFile (Windows 10 / 11)"],
        Limitations =
        [
            "Formatul vechi Root\\File (Windows 8) nu este citit.",
            "Jurnalele de tranzacții .LOG1/.LOG2 nu sunt aplicate; lipsa lor e raportată ca gol.",
            "Amcache arată prezența unui fișier (și de obicei instalarea), nu fiecare rulare.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "ExecutionArtifactParserTests: Amcache.hve real din corpus (peste 6000 de intrări) și hive sintetic",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reg = new RawRegistry(fs);
        if (reg.OpenKey(@"Root\InventoryApplicationFile") is null)
        {
            result.Gaps.Add(new EvidenceGap("Amcache InventoryApplicationFile", EvidenceStatus.NotAvailable, "cheia lipsește (format Amcache vechi)",
                "Inventarul aplicațiilor nu este disponibil", "Root\\File (format Windows 8)", "Parțial"));
            return;
        }
        foreach (var k in reg.SubKeys(@"Root\InventoryApplicationFile"))
        {
            ct.ThrowIfCancellationRequested();
            var keyName = k.Path[(k.Path.LastIndexOf('\\') + 1)..];
            string V(string n) => k.Value(n)?.AsText ?? "";
            var path = V("LowerCaseLongPath");
            var sha1 = V("FileId") is { Length: 44 } fid && fid.StartsWith("0000") ? fid[4..].ToUpperInvariant() : V("FileId");
            sink.Add(new TimelineEvent
            {
                Time = Hive.KeyTime(k),
                Source = "Amcache", EvidenceId = item.EvidenceId, Path = path, Process = V("Name"), Hash = sha1,
                Summary = $"Amcache: {V("Name")} ({V("Publisher")} {V("Version")}) prezent la {path}",
                TimeSemantics = "Amcache entry last written (≈ first seen / install)",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $@"Amcache.hve\Root\InventoryApplicationFile\{keyName}",
                Fields =
                {
                    ["SHA1"] = sha1, ["Publisher"] = V("Publisher"), ["Version"] = V("Version"), ["ProductName"] = V("ProductName"),
                    ["LinkDate"] = V("LinkDate"), ["Size"] = V("Size"), ["IsOsComponent"] = V("IsOsComponent"),
                },
            });
            result.Records++;
        }
        if (File.Exists(fullPath + ".LOG1") is false && File.Exists(Path.Combine(Path.GetDirectoryName(fullPath)!, "Amcache.hve.LOG1")) is false)
            result.Gaps.Add(new EvidenceGap("Amcache transaction logs", EvidenceStatus.Partial, "jurnalele de tranzacții .LOG1/.LOG2 nu au fost aplicate",
                "Cele mai recente intrări pot lipsi dacă hive-ul era „murdar” la copiere", "Copiere împreună cu .LOG1/.LOG2", "Parțial"));
    }
}
