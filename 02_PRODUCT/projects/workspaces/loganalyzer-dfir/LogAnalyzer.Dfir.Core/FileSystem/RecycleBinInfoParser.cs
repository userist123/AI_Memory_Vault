using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// Recycle Bin metadata file ($I&lt;id&gt;&lt;ext&gt;, Windows Vista and later): when the file was deleted, where it was, how large it was.
/// Version 1 (Vista - 8.1): int64 version = 1, int64 size, FILETIME, wchar[260], 544 bytes. Version 2 (Windows 10 and later): int64 version = 2,
/// int64 size, FILETIME, int32 character count (with the terminating NUL), wchar[count].
/// The deletion time is a FILETIME (UTC). A missing $R file next to the $I means the content was emptied or removed, not that the file never existed.
/// </summary>
public sealed class RecycleBinInfoParser : EvidenceParserBase
{
    public const string SourceTypeName = "recycle_info";
    /// <summary>Largest $I read: a v2 path is at most 32 767 characters (64 KiB); anything near the limit is already abnormal.</summary>
    public const long MaxInfoFileBytes = 128 * 1024;
    public const int V1Length = 544;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "RecycleBinInfoParser", Version = "1.1", Artifact = "Metadate Coș de reciclare ($I)",
        MaxInputBytes = MaxInfoFileBytes,
        SourceTypes = [SourceTypeName], Fingerprints = [SourceTypeName],
        SupportedOs = "Windows Vista și ulterioare (format binar citit în cod gestionat)",
        FormatVersions = ["Versiunea 1 (544 de octeți, cale de 260 de caractere)", "Versiunea 2 (Windows 10+, lungimea căii în fișier)"],
        Limitations =
        [
            "Ora este momentul trimiterii în Coș (FILETIME UTC), nu al ultimei utilizări a fișierului și nu al golirii Coșului.",
            "Fișierul $R (conținutul) nu este citit; dacă lipsește lângă $I, conținutul a fost golit sau eliminat, nu înseamnă că fișierul nu a existat. La colectarea live $R nu se copiază: se consemnează doar dacă exista și ce dimensiune avea în momentul colectării.",
            "Un $I rămâne doar cât timp elementul este în Coș; după golire metadatele pot dispărea.",
            "Versiuni necunoscute sau fișiere trunchiate sunt raportate ca lacune, nu interpretate.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "RecycleBinInfoParserTests: fișiere $I sintetice v1 și v2 cu valori exacte și intrări ostile (lungimi negative sau peste fișier, versiune necunoscută, fișier trunchiat); UserArtifactsCollectorTests: starea $R consemnată la colectare",
    };

    private static readonly Regex OwnerSid = new(@"[\\/]\$Recycle\.Bin[\\/](S-1-[0-9-]+)[\\/]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Content check used by <c>EvidenceFingerprint</c>: version 1 or 2, enough bytes for the fixed part, and either the name starts with "$I"
    /// or the text field starts like a path (X:\ or \\).
    /// </summary>
    public static bool LooksLikeInfo(ReadOnlySpan<byte> head, long length, string fileName)
    {
        if (length < 24 || head.Length < 24) return false;
        long version = BinaryPrimitives.ReadInt64LittleEndian(head);
        if (version is not (1 or 2)) return false;
        if (fileName.StartsWith("$I", StringComparison.OrdinalIgnoreCase)) return true;
        int at = version == 1 ? 24 : 28;
        if (head.Length < at + 6) return false;
        char c0 = (char)BinaryPrimitives.ReadUInt16LittleEndian(head[at..]), c1 = (char)BinaryPrimitives.ReadUInt16LittleEndian(head[(at + 2)..]), c2 = (char)BinaryPrimitives.ReadUInt16LittleEndian(head[(at + 4)..]);
        return char.IsAsciiLetter(c0) && c1 == ':' && c2 == '\\' || c0 == '\\' && c1 == '\\';
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var d = File.ReadAllBytes(fullPath);
        var name = Path.GetFileName(fullPath);
        if (!Decode(d, out var info, out var problem))
        {
            result.Status = EvidenceStatus.Failed;
            result.Error = problem;
            result.Gaps.Add(new EvidenceGap(name, EvidenceStatus.Failed, problem, "Ștergerea acestui fișier din Coș nu a putut fi datată sau localizată",
                "Jurnalul USN, ShellBags, LNK / Jump Lists pentru aceeași cale", "Posibil, din alte surse"));
            return;
        }

        // $R is the content; it is looked at only when the stored file keeps its $I name, so a lone imported $I is not read as "content removed".
        // E7 slice 2: the live collector never copies $R; it records at collection whether $R was there and how large it was, and that record wins.
        string content = "not_checked", contentText = "prezența fișierului $R nu a putut fi verificată";
        string rSize = "";
        if (CollectedRState(item) is { } collected)
        {
            (content, rSize) = collected;
            contentText = content switch
            {
                "present" => "conținutul ($R) exista lângă $I la colectare" + (rSize.Length > 0 ? $" ({rSize} octeți)" : "") + "; nu a fost copiat",
                "present_folder" => "conținutul ($R) exista lângă $I la colectare, ca folder (un folder șters); nu a fost copiat",
                "absent" => "conținutul ($R) lipsea lângă $I la colectare: conținut golit sau eliminat",
                _ => "prezența fișierului $R nu a putut fi verificată la colectare",
            };
        }
        else if (name.Length > 2 && name.StartsWith("$I", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(fullPath) ?? "";
            var r = Path.Combine(dir, "$R" + name[2..]);
            if (File.Exists(r) || Directory.Exists(r)) { content = "present"; contentText = "conținutul ($R) există lângă $I"; }
            else { content = "absent"; contentText = "conținutul ($R) lipsește lângă $I: conținut golit sau eliminat (sau neadus împreună cu $I)"; }
        }

        var sid = OwnerSid.Match(item.OriginalPath ?? "") is { Success: true } m ? m.Groups[1].Value : "";
        var when = Timestamp.FromFileTime(info.DeletedFileTime, "Recycle Bin $I FILETIME");
        var sizeText = info.Size >= 0 ? $"{info.Size:N0} octeți" : "dimensiune necunoscută";
        sink.Add(new TimelineEvent
        {
            Time = when,
            TimeSemantics = "deletion time written when the item was sent to the Recycle Bin ($I FILETIME, UTC)",
            Source = "RecycleBin", EvidenceId = item.EvidenceId, Path = info.OriginalPath, Process = "",
            Summary = $"Șters în Coșul de reciclare: {info.OriginalPath} ({sizeText}); {contentText}" + (when.IsKnown ? "" : "; ora ștergerii este necunoscută în metadate"),
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High,
            Locator = $"{name} (v{info.Version})",
            Fields =
            {
                ["OriginalPath"] = info.OriginalPath, ["OriginalSize"] = info.Size.ToString(CultureInfo.InvariantCulture), ["FormatVersion"] = info.Version.ToString(CultureInfo.InvariantCulture),
                ["DeletedFileTime"] = info.DeletedFileTime.ToString(CultureInfo.InvariantCulture), ["RContent"] = content, ["RSize"] = rSize, ["OwnerSid"] = sid, ["InfoFile"] = name,
            },
        });
        result.Records++;
    }

    public sealed record Info(int Version, long Size, long DeletedFileTime, string OriginalPath);

    /// <summary>The live collector (E7 slice 2) that copies $I files; only its notes are trusted for the $R record.</summary>
    public const string CollectedBy = "UserArtifactsCollector";
    /// <summary>Prefix of the $R record the collector writes into the evidence notes.</summary>
    public const string RNotePrefix = "$R la colectare: ";

    private static readonly Regex RNoteRx = new(@"\$R la colectare: (prezent ca folder|prezent|lipsă|neverificat)(?:, (\d{1,19}) octeți)?", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>The $R record as the collector writes it: existence and size only, the content is never copied.</summary>
    public static string RNote(string state, long? size = null) => RNotePrefix + state switch
    {
        "present" => "prezent" + (size is { } n ? ", " + n.ToString(CultureInfo.InvariantCulture) + " octeți" : ""),
        "present_folder" => "prezent ca folder",
        "absent" => "lipsă",
        _ => "neverificat",
    } + " (conținutul $R nu este copiat)";

    /// <summary>The $R state recorded at collection, or null when the item was not collected by <see cref="CollectedBy"/> (an import keeps the old rule).</summary>
    public static (string State, string Size)? CollectedRState(EvidenceItem item)
    {
        if (item.Collector != CollectedBy || item.AcquisitionMethod != "acquired" || string.IsNullOrEmpty(item.Notes)) return null;
        var m = RNoteRx.Match(item.Notes);
        if (!m.Success) return null;
        var state = m.Groups[1].Value switch { "prezent" => "present", "prezent ca folder" => "present_folder", "lipsă" => "absent", _ => "not_checked" };
        return (state, m.Groups[2].Success ? m.Groups[2].Value : "");
    }

    /// <summary>Decodes a $I file. False with a reason (Romanian) when the bytes are not a readable v1 or v2 record; never throws for bad input.</summary>
    public static bool Decode(ReadOnlySpan<byte> d, out Info info, out string problem)
    {
        info = new(0, 0, 0, "");
        problem = "";
        if (d.Length < 24) { problem = $"fișier $I prea scurt ({d.Length} octeți; antetul fix are 24)"; return false; }
        long version = BinaryPrimitives.ReadInt64LittleEndian(d);
        long size = BinaryPrimitives.ReadInt64LittleEndian(d[8..]);
        long ft = BinaryPrimitives.ReadInt64LittleEndian(d[16..]);
        if (version == 1)
        {
            if (d.Length < V1Length) { problem = $"$I versiunea 1 are {d.Length} octeți, dar formatul cere {V1Length}; fișier trunchiat"; return false; }
            info = new(1, size, ft, Utf16Z(d.Slice(24, V1Length - 24)));
            return true;
        }
        if (version == 2)
        {
            if (d.Length < 28) { problem = "$I versiunea 2 fără câmpul de lungime a căii; fișier trunchiat"; return false; }
            int count = BinaryPrimitives.ReadInt32LittleEndian(d[24..]);
            if (count <= 0) { problem = $"$I versiunea 2: lungimea căii nu este validă ({count})"; return false; }
            if (28L + 2L * count > d.Length) { problem = $"$I versiunea 2: calea declară {count} caractere, dar fișierul are doar {(d.Length - 28) / 2}"; return false; }
            info = new(2, size, ft, Utf16Z(d.Slice(28, 2 * count)));
            return true;
        }
        problem = $"versiune $I necunoscută ({version}); se cunosc 1 și 2";
        return false;
    }

    private static string Utf16Z(ReadOnlySpan<byte> b)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 2 <= b.Length; i += 2)
        {
            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(b[i..]);
            if (c == 0) break;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
