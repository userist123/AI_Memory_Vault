using System.Buffers.Binary;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// Custom Jump List (*.customDestinations-ms): the items an application itself pinned or listed (tasks, frequent / recent categories), stored as shell links
/// one after another, between a small header and the footer 0xBABFFBAB. There is no DestList: no last-use time and no use count. Every link found by its
/// header is decoded with <see cref="LnkParser.Decode"/> (research 2026-11-07, plan P3.1: these files were not read at all).
/// </summary>
public sealed class CustomJumpListParser : EvidenceParserBase
{
    public const string SourceTypeName = "jumplist_custom";
    public const long MaxFileBytes = 16L * 1024 * 1024;
    /// <summary>Links read from one file at most.</summary>
    public const int MaxLinks = 2_000;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "CustomJumpListParser", Version = "1.0", Artifact = "Jump List personalizată (*.customDestinations-ms)",
        SourceTypes = [SourceTypeName], FileNames = [".customDestinations-ms"], Fingerprints = ["custom_destinations"],
        MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows 7 – Windows 11 (linkuri LNK în format MS-SHLLINK)",
        FormatVersions = ["antet versiunea 2, linkuri LNK consecutive, terminator 0xBABFFBAB"],
        Limitations =
        [
            "Nu are listă de utilizare (DestList): fără ora ultimei folosiri și fără număr de folosiri; ora din rând este data modificării țintei, așa cum o păstrează linkul.",
            "Categoriile (sarcini, recente, frecvente) nu sunt deosebite: fiecare link este un rând.",
            "Numele aplicației nu este rezolvat din AppID (primele 16 caractere hex ale numelui de fișier).",
            "Arată că un fișier sau o comandă apare în lista aplicației, nu că fișierul a fost copiat.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "CustomJumpListParserTests: fișier construit din linkuri LNK în formatul documentat; verificat pe fișierele reale ale unui PC (numărul de linkuri găsite = numărul de antete LNK)",
    };

    private static readonly byte[] LinkHeader = [0x4C, 0, 0, 0, 0x01, 0x14, 0x02, 0x00, 0, 0, 0, 0, 0xC0, 0, 0, 0, 0, 0, 0, 0x46];

    /// <summary>The offsets of every shell-link header in the file (header size 0x4C followed by the shell-link CLSID). Pure.</summary>
    public static List<int> LinkOffsets(ReadOnlySpan<byte> data, int max = MaxLinks)
    {
        var list = new List<int>();
        for (int i = 0; i + LinkHeader.Length <= data.Length && list.Count < max; i++)
        {
            i = data[i..].IndexOf(LinkHeader) is var k and >= 0 ? i + k : -1;
            if (i < 0) break;
            list.Add(i);
        }
        return list;
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var data = File.ReadAllBytes(fullPath);
        if (data.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(data) != 2)
            throw new InvalidDataException("Fișierul nu începe ca o Jump List personalizată (versiunea 2).");
        var appId = Path.GetFileName(fullPath).Split('.')[0];
        var offsets = LinkOffsets(data);
        if (offsets.Count >= MaxLinks)
            result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial, $"citirea s-a oprit la {MaxLinks} de linkuri", "Pot lipsi elemente din listă", "Fișierul brut din caz", "Da"));
        for (int n = 0; n < offsets.Count; n++)
        {
            ct.ThrowIfCancellationRequested();
            var end = n + 1 < offsets.Count ? offsets[n + 1] : data.Length;
            LnkParser.Link link;
            try { link = LnkParser.Decode(data[offsets[n]..end]); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException) { result.MalformedRecords++; continue; }
            var target = link.Target.Length > 0 ? link.Target : link.RelativePath;
            sink.Add(new TimelineEvent
            {
                Time = link.Modified > 0 ? Timestamp.FromFileTime(link.Modified, "LNK header target WriteTime (Jump List personalizată)") : Timestamp.Unknown(),
                TimeSemantics = "target file last modified, as recorded in the link header",
                Source = "JumpList", EvidenceId = item.EvidenceId, Path = target, Process = target.Contains('\\') ? WinPath.GetFileName(target) : "",
                Summary = $"Jump List personalizată {appId}: {(target.Length > 0 ? target : "(link fără țintă)")}{(link.Arguments.Length > 0 ? " " + link.Arguments : "")}",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $"offset 0x{offsets[n]:x}",
                Fields =
                {
                    ["AppId"] = appId, ["LnkTarget"] = link.Target, ["Arguments"] = link.Arguments, ["VolumeSerial"] = link.Serial, ["DriveType"] = link.DriveType,
                    ["VolumeLabel"] = link.Label, ["Machine"] = link.Machine, ["JumpListKind"] = "custom",
                },
            });
            result.Records++;
        }
    }
}
