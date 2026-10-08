using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// Automatic Jump List (*.automaticDestinations-ms): an OLE compound file with one shell link per item (streams named by
/// the entry number in hex) and a DestList stream giving, per item, the last use time, use count, pin state and the
/// machine name. One event per DestList entry, joined to its link.
/// </summary>
public sealed class JumpListParser : EvidenceParserBase
{
    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "JumpListParser", Version = "1.0", Artifact = "Jump List automată (*.automaticDestinations-ms)",
        SourceTypes = ["jumplist_auto"], FileNames = [".automaticDestinations-ms"], Fingerprints = ["cfb"],
        SupportedOs = "Oricare (OLE Compound File citit în cod gestionat)",
        FormatVersions = ["DestList versiunea 3 – 6 (Windows 10 / 11; intrare de 130 de octeți + cale UTF-16 + 4 octeți)"],
        Limitations =
        [
            "Jump Lists personalizate (*.customDestinations-ms) nu sunt citite.",
            "Numele aplicației nu este rezolvat din AppID (primele 16 caractere hex ale numelui de fișier).",
            "DestList versiunea 1 (Windows 7 / 8) nu este acceptată: parsarea eșuează explicit.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "JumpListParserTests: compound file sintetic cu valori exacte; pe corpus (54 de fișiere reale) consistență între DestList și linkurile din streamuri (număr, numere de intrare, căi) — nu există un instrument independent pentru comparație",
    };

    public static bool IsEntryStream(string name) => name.Length > 0 && name.All(Uri.IsHexDigit);

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var cf = new CompoundFile(File.ReadAllBytes(fullPath));
        var appId = Path.GetFileName(fullPath).Split('.')[0];
        // Link streams are named by the entry number in hex; DestList and (Windows 11) DestListPropertyStore are metadata.
        var streams = cf.Entries.Where(e => e.Type == 2 && IsEntryStream(e.Name)).ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var destListEntry = cf.Entries.FirstOrDefault(e => e.Type == 2 && e.Name == "DestList");
        if (destListEntry is null || destListEntry.Size < 32)
        {
            if (streams.Count > 0)
                result.Gaps.Add(new EvidenceGap(Path.GetFileName(fullPath), EvidenceStatus.Partial, "DestList lipsește sau e gol, dar există linkuri",
                    "Ora și numărul de utilizări nu sunt disponibile", "Linkurile din Recent", "Nu"));
            return;                                                                   // an empty jump list: EMPTY, not FAILED
        }
        var dl = cf.Read(destListEntry);
        int version = BinaryPrimitives.ReadInt32LittleEndian(dl);
        if (version < 3) throw new InvalidDataException($"DestList versiunea {version} (Windows 7/8) nu este acceptată.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(dl.AsSpan(4));
        int pos = 32;
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (pos + 130 > dl.Length) throw new InvalidDataException($"DestList trunchiat la intrarea {i + 1} din {count}.");
            int entryNo = BinaryPrimitives.ReadInt32LittleEndian(dl.AsSpan(pos + 88));
            long ft = BinaryPrimitives.ReadInt64LittleEndian(dl.AsSpan(pos + 100));
            int pin = BinaryPrimitives.ReadInt32LittleEndian(dl.AsSpan(pos + 108));
            int uses = BinaryPrimitives.ReadInt32LittleEndian(dl.AsSpan(pos + 116));
            int len = BinaryPrimitives.ReadUInt16LittleEndian(dl.AsSpan(pos + 128));
            if (pos + 130 + len * 2 > dl.Length) throw new InvalidDataException($"Calea intrării {entryNo} depășește DestList.");
            var destPath = Encoding.Unicode.GetString(dl, pos + 130, len * 2);
            var host = Encoding.ASCII.GetString(dl, pos + 72, 16).TrimEnd('\0');
            var streamName = entryNo.ToString("x", CultureInfo.InvariantCulture);
            LnkParser.Link? link = null;
            if (streams.TryGetValue(streamName, out var s))
            {
                try { link = LnkParser.Decode(cf.Read(s)); }
                catch (InvalidDataException) { result.MalformedRecords++; }
            }
            else result.MalformedRecords++;
            var target = link?.Target is { Length: > 0 } t ? t : destPath;
            sink.Add(new TimelineEvent
            {
                Time = ft > 0 ? Timestamp.FromFileTime(ft, "DestList last access FILETIME") : Timestamp.Unknown(),
                TimeSemantics = "item last used through this application (Jump List DestList)",
                Source = "JumpList", EvidenceId = item.EvidenceId, Path = target, Process = target.Contains('\\') ? Path.GetFileName(target) : "",
                Host = host, Summary = $"Jump List {appId}: {target} folosit de {uses} ori" + (pin >= 0 ? " (fixat)" : ""),
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = $"DestList entry {entryNo} / stream {streamName}",
                Fields =
                {
                    ["AppId"] = appId, ["EntryNumber"] = entryNo.ToString(CultureInfo.InvariantCulture), ["AccessCount"] = uses.ToString(CultureInfo.InvariantCulture),
                    ["Pinned"] = pin >= 0 ? "true" : "false", ["Hostname"] = host, ["DestListPath"] = destPath, ["LnkTarget"] = link?.Target ?? "",
                    ["Arguments"] = link?.Arguments ?? "", ["DestListVersion"] = version.ToString(CultureInfo.InvariantCulture),
                },
            });
            result.Records++;
            pos += 130 + len * 2 + 4;
        }
    }
}
