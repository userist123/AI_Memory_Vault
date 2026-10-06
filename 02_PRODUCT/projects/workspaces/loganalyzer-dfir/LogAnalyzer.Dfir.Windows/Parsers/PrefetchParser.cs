using System.Text;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Windows 10/11 Prefetch (SCCA v30/v31, MAM-compressed) parser. Emits one event per recorded run time
/// (up to 8) and keeps the referenced-file list. Prefetch is strong execution evidence but not infallible
/// (spec §29): classification stays DIRECT for "a run was recorded", never for intent or parentage.
/// </summary>
public sealed class PrefetchParser : EvidenceParserBase
{
    public override string Name => "PrefetchParser";
    public override string Version => "1.0";
    public override bool CanParse(EvidenceItem item) => item.SourceType == "prefetch" || item.StoredPath.EndsWith(".pf", StringComparison.OrdinalIgnoreCase);

    public sealed record PrefetchInfo(int Version, string ExeName, uint Hash, int RunCount, IReadOnlyList<DateTimeOffset> RunTimesUtc,
                                      IReadOnlyList<string> ReferencedFiles, IReadOnlyList<string> Volumes)
    {
        public string? ExePathGuess => ReferencedFiles.FirstOrDefault(f => f.EndsWith("\\" + ExeName, StringComparison.OrdinalIgnoreCase));
    }

    public static PrefetchInfo Read(string path)
    {
        var raw = File.ReadAllBytes(path);
        var d = raw.Length >= 3 && raw[0] == 'M' && raw[1] == 'A' && raw[2] == 'M' ? NtCompression.DecompressMam(raw) : raw;
        if (d.Length < 0x100 || Encoding.ASCII.GetString(d, 4, 4) != "SCCA") throw new InvalidDataException("Not an SCCA prefetch file.");
        int version = BitConverter.ToInt32(d, 0);
        if (version < 30) throw new NotSupportedException($"Prefetch version {version} is not supported (Win10+ v30/v31 only).");

        string exe = Encoding.Unicode.GetString(d, 16, 60).Split('\0')[0];
        uint hash = BitConverter.ToUInt32(d, 76);
        int mOff = BitConverter.ToInt32(d, 84), fnOff = BitConverter.ToInt32(d, 100), fnSize = BitConverter.ToInt32(d, 104);
        int volOff = BitConverter.ToInt32(d, 108), volCnt = BitConverter.ToInt32(d, 112);

        var runs = new List<DateTimeOffset>();
        for (int i = 0; i < 8; i++)
        {
            long ft = BitConverter.ToInt64(d, 128 + 8 * i);
            if (ft > 0) runs.Add(new DateTimeOffset(DateTime.FromFileTimeUtc(ft), TimeSpan.Zero));
        }
        // Two v30 layouts exist; the metrics-array offset tells them apart (0x130 → run count at 0xD0, else 0xC8).
        int runCount = BitConverter.ToInt32(d, mOff == 0x130 ? 0xD0 : 0xC8);

        var files = fnOff > 0 && fnOff + fnSize <= d.Length
            ? Encoding.Unicode.GetString(d, fnOff, fnSize).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            : [];

        var vols = new List<string>();
        for (int i = 0; i < volCnt && volOff > 0; i++)
        {
            int o = volOff + i * 96;
            if (o + 20 > d.Length) break;
            int dpOff = BitConverter.ToInt32(d, o), dpLen = BitConverter.ToInt32(d, o + 4);
            long ct = BitConverter.ToInt64(d, o + 8); uint serial = BitConverter.ToUInt32(d, o + 16);
            int s = volOff + dpOff;
            string dev = s > 0 && s + dpLen * 2 <= d.Length ? Encoding.Unicode.GetString(d, s, dpLen * 2) : "";
            vols.Add($"{dev} serial={serial:X8} created={(ct > 0 ? DateTime.FromFileTimeUtc(ct).ToString("o") : "")}");
        }
        return new PrefetchInfo(version, exe, hash, runCount, runs, files, vols);
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var pf = Read(fullPath);
        string exePath = pf.ExePathGuess ?? pf.ExeName;
        for (int i = 0; i < pf.RunTimesUtc.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = new TimelineEvent
            {
                Time = Timestamp.FromUtc(pf.RunTimesUtc[i].UtcDateTime, pf.RunTimesUtc[i].ToString("o"), "prefetch FILETIME"),
                Source = "Prefetch",
                EvidenceId = item.EvidenceId,
                Provider = "Prefetch v" + pf.Version,
                Process = pf.ExeName,
                Path = exePath,
                Summary = $"{pf.ExeName} execution recorded by Prefetch (run {(i == 0 ? "latest" : "previous #" + i)}; total run count {pf.RunCount})",
                TimeSemantics = i == 0 ? "last run (prefetch)" : "previous run (prefetch)",
                TemporalType = TemporalType.Historical,
                Classification = Classification.Direct,
                Confidence = Confidence.Medium,
                Locator = $"{System.IO.Path.GetFileName(fullPath)}#run{i}",
                Fields =
                {
                    ["PrefetchHash"] = pf.Hash.ToString("X8"),
                    ["RunCount"] = pf.RunCount.ToString(),
                    ["ReferencedFileCount"] = pf.ReferencedFiles.Count.ToString(),
                    ["ReferencedFiles"] = string.Join("|", pf.ReferencedFiles),
                },
            };
            sink.Add(e);
            result.Records++;
        }
    }
}
