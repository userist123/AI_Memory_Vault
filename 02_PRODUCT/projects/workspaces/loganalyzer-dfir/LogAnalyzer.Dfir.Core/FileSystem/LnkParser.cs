using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// Shell link file (.lnk, [MS-SHLLINK]): the target path (local or network, from LinkInfo), arguments, working directory,
/// the target's own times and size as recorded when the link was written, the volume (type, serial, label) and the
/// machine name from the TrackerDataBlock. A Recent link shows that the user opened the target.
/// </summary>
public sealed class LnkParser : EvidenceParserBase
{
    private static readonly Guid LinkClsid = new("00021401-0000-0000-C000-000000000046");
    private static readonly string[] DriveTypes = ["UNKNOWN", "NO_ROOT_DIR", "REMOVABLE", "FIXED", "REMOTE", "CDROM", "RAMDISK"];

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "LnkParser", Version = "1.0", Artifact = "Fișier shortcut Windows (.lnk)",
        SourceTypes = ["lnk"], FileNames = [".lnk"], Fingerprints = ["lnk"],
        SupportedOs = "Oricare (format binar citit în cod gestionat)",
        FormatVersions = ["MS-SHLLINK: antet, LinkInfo (cale locală / rețea, volum), StringData, TrackerDataBlock, EnvironmentVariableDataBlock"],
        Limitations =
        [
            "Ținta este luată din LinkInfo (sau din RelativePath); lista de ID-uri shell (LinkTargetIDList) nu este decodată.",
            "Căile ANSI sunt decodate cu pagina de cod a stației de analiză; căile Unicode din LinkInfo au prioritate.",
            "Orele din antet sunt ale fișierului țintă la momentul scrierii linkului, nu ale linkului.",
            "Jump Lists (AutomaticDestinations / CustomDestinations) nu sunt încă citite.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "LnkParserTests: linkurile reale din Recent (corpus) comparate cu shell-ul Windows (WScript.Shell: țintă, argumente, folder de lucru) și link sintetic cu valori exacte",
    };

    static LnkParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var d = File.ReadAllBytes(fullPath);
        var link = Decode(d);
        var target = link.Target;
        sink.Add(new TimelineEvent
        {
            Time = link.Modified > 0 ? Timestamp.FromFileTime(link.Modified, "LNK header target WriteTime") : Timestamp.Unknown(),
            TimeSemantics = "target file last modified, as recorded in the link header",
            Source = "LNK", EvidenceId = item.EvidenceId, Path = target, Process = target.Length > 0 ? WinPath.GetFileName(target) : "",
            Summary = $"Shortcut către {(target.Length > 0 ? target : "(țintă nedecodată)")}" + (link.Arguments.Length > 0 ? $" {link.Arguments}" : "") +
                      (link.Machine.Length > 0 ? $"; creat pe {link.Machine}" : ""),
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High,
            Locator = "MS-SHLLINK",
            Fields =
            {
                ["TargetSource"] = link.TargetSource, ["Arguments"] = link.Arguments, ["WorkingDirectory"] = link.WorkingDir,
                ["RelativePath"] = link.RelativePath, ["Name"] = link.Name, ["IconLocation"] = link.Icon, ["EnvTarget"] = link.EnvTarget,
                ["TargetCreatedUtc"] = Iso(link.Created), ["TargetAccessedUtc"] = Iso(link.Accessed), ["TargetModifiedUtc"] = Iso(link.Modified),
                ["TargetSize"] = link.Size.ToString(CultureInfo.InvariantCulture), ["FileAttributes"] = $"0x{link.Attributes:X}",
                ["DriveType"] = link.DriveType, ["VolumeSerial"] = link.Serial, ["VolumeLabel"] = link.Label, ["NetName"] = link.NetName,
                ["MachineId"] = link.Machine, ["LinkFlags"] = $"0x{link.Flags:X}",
                ["LnkFileModifiedUtc"] = File.GetLastWriteTimeUtc(fullPath).ToString("o", CultureInfo.InvariantCulture),
            },
        });
        result.Records++;
    }

    private static string Iso(long ft) => ft is > 0 and <= Timestamp.MaxFileTime ? DateTime.FromFileTimeUtc(ft).ToString("o", CultureInfo.InvariantCulture) : "";

    public sealed record Link(uint Flags, uint Attributes, long Created, long Accessed, long Modified, uint Size, string Target, string TargetSource,
                              string Name, string RelativePath, string WorkingDir, string Arguments, string Icon, string DriveType, string Serial,
                              string Label, string NetName, string Machine, string EnvTarget);

    public static Link Decode(byte[] d)
    {
        Need(d, 0, 0x4C);
        if (BinaryPrimitives.ReadInt32LittleEndian(d) != 0x4C || new Guid(d.AsSpan(4, 16)) != LinkClsid)
            throw new InvalidDataException("Antet LNK invalid (dimensiune sau CLSID).");
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x14));
        bool unicode = (flags & 0x80) != 0;
        int pos = 0x4C;
        if ((flags & 0x1) != 0) { Need(d, pos, 2); pos += 2 + BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pos)); }

        string target = "", source = "None", drive = "", serial = "", label = "", net = "";
        if ((flags & 0x2) != 0)
        {
            Need(d, pos, 0x1C);
            int size = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(pos));
            Need(d, pos, size);
            int hdr = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(pos + 4));
            uint liFlags = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos + 8));
            int volOff = I(d, pos + 12), baseOff = I(d, pos + 16), netOff = I(d, pos + 20), suffixOff = I(d, pos + 24);
            int baseU = hdr >= 0x24 ? I(d, pos + 28) : 0, suffixU = hdr >= 0x24 ? I(d, pos + 32) : 0;
            string suffix = suffixU > 0 ? Utf16Z(d, pos + suffixU, pos + size) : AnsiZ(d, pos + suffixOff, pos + size);
            if ((liFlags & 1) != 0)
            {
                int v = pos + volOff;
                Need(d, v, 16);
                int type = I(d, v + 4);
                drive = type >= 0 && type < DriveTypes.Length ? DriveTypes[type] : type.ToString(CultureInfo.InvariantCulture);
                serial = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(v + 8)).ToString("X8", CultureInfo.InvariantCulture);
                int labelOff = I(d, v + 12);
                label = labelOff == 0x14 ? Utf16Z(d, v + I(d, v + 16), pos + size) : AnsiZ(d, v + labelOff, pos + size);
                var basePath = baseU > 0 ? Utf16Z(d, pos + baseU, pos + size) : AnsiZ(d, pos + baseOff, pos + size);
                target = Join(basePath, suffix);
                source = "LinkInfo";
            }
            else if ((liFlags & 2) != 0)
            {
                int c = pos + netOff;
                Need(d, c, 0x14);
                net = AnsiZ(d, c + I(d, c + 8), pos + size);
                target = Join(net, suffix);
                source = "LinkInfo";
            }
            pos += size;
        }

        string name = "", rel = "", wd = "", args = "", icon = "";
        foreach (var (bit, set) in new (uint, Action<string>)[] { (0x4, s => name = s), (0x8, s => rel = s), (0x10, s => wd = s), (0x20, s => args = s), (0x40, s => icon = s) })
        {
            if ((flags & bit) == 0) continue;
            Need(d, pos, 2);
            int count = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pos));
            int bytes = unicode ? count * 2 : count;
            Need(d, pos + 2, bytes);
            set(unicode ? Encoding.Unicode.GetString(d, pos + 2, bytes) : Ansi.GetString(d, pos + 2, bytes));
            pos += 2 + bytes;
        }
        if (target.Length == 0 && rel.Length > 0) { target = rel; source = "RelativePath"; }

        string machine = "", env = "";
        while (pos + 4 <= d.Length)
        {
            int size = I(d, pos);
            if (size < 4) break;                                       // TerminalBlock
            if (pos + size > d.Length) throw new InvalidDataException("Bloc ExtraData trunchiat.");
            uint sig = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos + 4));
            if (sig == 0xA0000003 && size >= 0x60) machine = AnsiZ(d, pos + 16, pos + 32);
            if (sig == 0xA0000001 && size >= 0x314) env = Utf16Z(d, pos + 8 + 260, pos + size);
            pos += size;
        }

        return new Link(flags, BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x18)), BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(0x1C)),
            BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(0x24)), BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(0x2C)),
            BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x34)), target, source, name, rel, wd, args, icon, drive, serial, label, net, machine, env);
    }

    private static Encoding Ansi => Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);

    private static string Join(string a, string b) => b.Length == 0 ? a : a.EndsWith('\\') ? a + b : a + "\\" + b;

    private static int I(byte[] d, int at) { Need(d, at, 4); return BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(at)); }

    private static void Need(byte[] d, int at, int len)
    {
        if (at < 0 || len < 0 || at + len > d.Length) throw new InvalidDataException($"LNK trunchiat: necesari {len} octeți la 0x{at:X}, fișierul are {d.Length}.");
    }

    private static string AnsiZ(byte[] d, int at, int limit)
    {
        Need(d, at, 0);
        int end = at;
        while (end < Math.Min(limit, d.Length) && d[end] != 0) end++;
        return Ansi.GetString(d, at, end - at);
    }

    private static string Utf16Z(byte[] d, int at, int limit)
    {
        Need(d, at, 0);
        int end = at;
        while (end + 1 < Math.Min(limit, d.Length) && (d[end] != 0 || d[end + 1] != 0)) end += 2;
        return Encoding.Unicode.GetString(d, at, end - at);
    }
}
