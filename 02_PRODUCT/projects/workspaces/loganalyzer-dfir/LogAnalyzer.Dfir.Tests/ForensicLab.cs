using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The forensic laboratory run (master spec §21, §22), separate from core CI: it runs only with LADFIR_LAB=1. Asked for and
/// without the corpus it fails (every case UNVERIFIED) instead of passing.
/// </summary>
public sealed class LabFactAttribute : FactAttribute
{
    public LabFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LADFIR_LAB") != "1") Skip = "FORENSIC LAB NOT RUN (set LADFIR_LAB=1) — FORENSIC VALIDATION = UNAVAILABLE";
    }
}

public enum LabStatus { Pass, Fail, Partial, Unverified }

/// <summary>One compared value. Explained = the difference is accounted for by something the parser itself reported.</summary>
public sealed record LabComparison(string Field, string Expected, string Actual, bool Equal, string Explanation = "");

/// <summary>REAL CORPUS · KNOWN TRUTH · EXPECTED · ACTUAL · DIFF for one parser case.</summary>
public sealed record LabCase(string Parser, string Case, string Corpus, string KnownTruth, LabStatus Status, IReadOnlyList<LabComparison> Comparisons, string Note)
{
    public static LabCase From(string parser, string name, string corpus, string truth, IReadOnlyList<LabComparison> c, string note = "")
    {
        var status = c.All(x => x.Equal) ? LabStatus.Pass
                   : c.All(x => x.Equal || x.Explanation.Length > 0) ? LabStatus.Partial : LabStatus.Fail;
        return new(parser, name, corpus, truth, status, c, note);
    }
}

public sealed class ForensicLab(ITestOutputHelper output)
{
    private static LabComparison Cmp(string field, object expected, object actual, string explanation = "")
    {
        string e = Convert.ToString(expected, CultureInfo.InvariantCulture) ?? "", a = Convert.ToString(actual, CultureInfo.InvariantCulture) ?? "";
        bool eq = e == a;
        return new(field, e, a, eq, eq ? "" : explanation);
    }

    private static LabComparison AtLeast(string field, long min, long actual) => new(field, $"≥ {min}", actual.ToString(CultureInfo.InvariantCulture), actual >= min);

    private static EvidenceItem Item(string path) => new() { EvidenceId = "EV-LAB", CaseId = "LAB", Source = Path.GetFileName(path), SourceType = "lab", StoredPath = path };

    /// <summary>Counts records and keeps the first and last RecordID and time, without holding the log in memory.</summary>
    private sealed class RangeSink : IEventSink
    {
        public long Count, MinId = long.MaxValue, MaxId = long.MinValue;
        public DateTimeOffset? First, Last;
        public void Add(TimelineEvent e)
        {
            Count++;
            if (long.TryParse(e.Locator.Replace("EventRecordID=", "").Split(';')[0], out var id)) { MinId = Math.Min(MinId, id); MaxId = Math.Max(MaxId, id); }
            if (e.Time.Utc is { } t) { if (First is null || t < First) First = t; if (Last is null || t > Last) Last = t; }
        }
    }

    private static string Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var s = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return s;
    }

    private static string Iso(DateTimeOffset? t) => t?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "";

    /// <summary>§22: every EVTX in the corpus against wevtutil (record count, first/last RecordID, first/last time).</summary>
    private static IEnumerable<LabCase> EvtxDifferential(string root)
    {
        foreach (var f in Directory.GetFiles(Path.Combine(root, "01_RAW_EVENTLOGS"), "*.evtx").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var gli = Run("wevtutil.exe", "gli", f, "/lf:true");
            long count = long.Parse(Regex.Match(gli, @"numberOfLogRecords:\s*(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            var sink = new RangeSink();
            var r = new EvtxParser().Parse(Item(f), f, sink, default);
            var explained = r.Gaps.Count > 0 ? "parserul a raportat: " + string.Join("; ", r.Gaps.Select(g => g.Reason)) : "";
            var c = new List<LabComparison> { Cmp("înregistrări", count, sink.Count, explained) };
            if (count > 0)
            {
                // The oldest and newest records as wevtutil reads them (gli's oldestRecordNumber is 1 for exported files, not a RecordID).
                (string Id, string Time) Edge(bool newest)
                {
                    var x = Run("wevtutil.exe", "qe", f, "/lf:true", "/c:1", "/f:xml", "/rd:" + (newest ? "true" : "false"));
                    return (Regex.Match(x, "<EventRecordID>(\\d+)</EventRecordID>").Groups[1].Value,
                            Iso(DateTimeOffset.Parse(Regex.Match(x, "SystemTime='([^']+)'").Groups[1].Value, CultureInfo.InvariantCulture)));
                }
                var (firstId, firstTime) = Edge(false);
                var (lastId, lastTime) = Edge(true);
                c.Add(Cmp("primul RecordID", firstId, sink.MinId, explained));
                c.Add(Cmp("ultimul RecordID", lastId, sink.MaxId, explained));
                c.Add(Cmp("RecordID continuu (ultimul − primul + 1)", count, sink.MaxId - sink.MinId + 1, explained));
                // The parser's earliest/latest time against the oldest/newest record's time: equal unless the clock went backwards.
                c.Add(Cmp("cea mai veche oră", firstTime, Iso(sink.First), "ora nu crește monoton cu RecordID (schimbare de oră a sistemului)"));
                c.Add(Cmp("cea mai nouă oră", lastTime, Iso(sink.Last), "ora nu crește monoton cu RecordID (schimbare de oră a sistemului)"));
            }
            yield return LabCase.From("EvtxParser", Path.GetFileName(f), f, "wevtutil gli / qe (implementarea Windows)", c,
                r.Status == EvidenceStatus.Success ? "" : $"status {r.Status}: {r.Error}");
        }
    }

    /// <summary>§21: the parsers' known truth from the investigation (targets.json).</summary>
    private static IEnumerable<LabCase> KnownTruth()
    {
        const string truth = "investigația manuală a incidentului NanAgent (targets.json)";
        {
            var pf = PrefetchParser.Read(Corpus.File("prefetch"));
            yield return LabCase.From("PrefetchParser", "SETUP.EXE", Corpus.File("prefetch"), truth,
            [
                Cmp("executabil", Corpus.S("prefetch", "exeName"), pf.ExeName),
                Cmp("număr de rulări", Corpus.L("prefetch", "runCount"), pf.RunCount),
                Cmp("ultima rulare (UTC, secunde)", Corpus.S("prefetch", "lastRunUtc"), pf.RunTimesUtc[0].UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                Cmp("calea conține", true, pf.ExePathGuess.Contains(Corpus.S("prefetch", "pathContains"), StringComparison.OrdinalIgnoreCase)),
            ]);
        }
        {
            var ts = DateTime.Parse(Corpus.S("srum", "timestampUtc"), CultureInfo.InvariantCulture);
            var hits = SrumNetworkParser.Read(Corpus.File("srum")).Where(r => r.App.Contains(Corpus.S("srum", "appContains"), StringComparison.OrdinalIgnoreCase)
                                                                            && Math.Abs((r.TimestampUtc - ts).TotalMinutes) < 1).ToList();
            yield return LabCase.From("SrumNetworkParser", "msbuild.exe 15:15 UTC", Corpus.File("srum"), truth,
            [
                Cmp("rânduri potrivite", 1, hits.Count),
                Cmp("octeți trimiși", Corpus.L("srum", "bytesSent"), hits.FirstOrDefault()?.BytesSent ?? -1),
                Cmp("octeți primiți", Corpus.L("srum", "bytesRecvd"), hits.FirstOrDefault()?.BytesRecvd ?? -1),
            ]);
        }
        {
            var sink = new ListSink();
            new EvtxParser().Parse(Item(Corpus.File("defenderEvtx")), Corpus.File("defenderEvtx"), sink, default);
            yield return LabCase.From("EvtxParser", "Defender 1116 NanAgent32.exe", Corpus.File("defenderEvtx"), truth,
            [
                Cmp("detecție cu calea și amenințarea", true, sink.Events.Any(e => e.EventId == Corpus.S("defenderEvtx", "eventId")
                    && e.Path.Contains(Corpus.S("defenderEvtx", "pathContains"), StringComparison.OrdinalIgnoreCase)
                    && e.Fields.GetValueOrDefault("Threat Name") == Corpus.S("defenderEvtx", "threatName"))),
            ]);
        }
        {
            var sink = new ListSink();
            var r = new PcapngParser().Parse(Item(Corpus.File("pcapng")), Corpus.File("pcapng"), sink, default);
            yield return LabCase.From("PcapngParser", "captură pktmon", Corpus.File("pcapng"), truth,
            [
                AtLeast("fluxuri", Corpus.L("pcapng", "minFlows"), sink.Events.Count(e => e.Source == "PCAP:flow")),
                Cmp("DNS " + Corpus.S("pcapng", "dnsNameContains"), true, sink.Events.Any(e => e.Source == "PCAP:DNS" && e.Dns.Contains(Corpus.S("pcapng", "dnsNameContains")))),
                Cmp("SNI " + Corpus.S("pcapng", "sniContains"), true, sink.Events.Any(e => e.Source == "PCAP:TLS" && e.Dns.Contains(Corpus.S("pcapng", "sniContains")))),
            ], r.Status == EvidenceStatus.Success ? "" : $"status {r.Status}");
        }
        {
            var sink = new ListSink();
            var r = new AmcacheParser().Parse(Item(Corpus.File("amcache")), Corpus.File("amcache"), sink, default);
            yield return LabCase.From("AmcacheParser", "InventoryApplicationFile", Corpus.File("amcache"), truth,
            [
                AtLeast("intrări", Corpus.L("amcache", "minEntries"), r.Records),
                Cmp("înregistrări corupte", 0, r.MalformedRecords),
            ]);
        }
    }

    [LabFact]
    public void Forensic_lab_report()
    {
        var cases = new List<LabCase>();
        bool corpus = Directory.Exists(Path.Combine(Corpus.Root, "01_RAW_EVENTLOGS"));
        if (corpus)
        {
            cases.AddRange(KnownTruth());
            cases.AddRange(EvtxDifferential(Corpus.Root));
        }
        else
            cases.Add(new LabCase("*", "corpus", Corpus.Root, "—", LabStatus.Unverified, [], "Corpusul lipsește: nimic nu este validat."));

        string Word(LabStatus s) => s.ToString().ToUpperInvariant();
        var summary = $"FORENSIC LAB = {string.Join(", ", Enum.GetValues<LabStatus>().Select(s => $"{cases.Count(c => c.Status == s)} {Word(s)}"))} ({cases.Count} cazuri)";
        var md = new StringBuilder($"# Raport de laborator forensic\n\n{summary}\n\n");
        foreach (var c in cases.OrderBy(c => c.Status == LabStatus.Pass).ThenBy(c => c.Parser).ThenBy(c => c.Case))
        {
            md.Append($"## {Word(c.Status)} — {c.Parser} / {c.Case}\n\nCorpus: `{c.Corpus}` · adevăr cunoscut: {c.KnownTruth}{(c.Note.Length > 0 ? $" · {c.Note}" : "")}\n\n");
            md.Append("| câmp | așteptat | obținut | diferență |\n|---|---|---|---|\n");
            foreach (var x in c.Comparisons)
                md.Append($"| {x.Field} | {x.Expected} | {x.Actual} | {(x.Equal ? "—" : x.Explanation.Length > 0 ? "explicată: " + x.Explanation : "**NEEXPLICATĂ**")} |\n");
            md.Append('\n');
        }
        var json = JsonSerializer.Serialize(new { Summary = summary, Cases = cases }, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        // Next to the binaries: JSON and plain text (a .md there would sit inside the vault's tree as an unlisted note).
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "forensic_lab_report.json"), json, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "forensic_lab_report.txt"), md.ToString(), new UTF8Encoding(false));
        if (Environment.GetEnvironmentVariable("LADFIR_LAB_OUT") is { Length: > 0 } outDir)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "forensic_lab_report.md"), md.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "forensic_lab_report.json"), json, new UTF8Encoding(false));
        }
        output.WriteLine(summary);
        foreach (var c in cases.Where(c => c.Status != LabStatus.Pass))
            output.WriteLine($"{Word(c.Status)} {c.Parser} {c.Case}: " + string.Join("; ", c.Comparisons.Where(x => !x.Equal).Select(x => $"{x.Field} {x.Expected} ≠ {x.Actual}")));

        Assert.True(corpus, "LADFIR_LAB=1 dar corpusul lipsește: FORENSIC VALIDATION = UNAVAILABLE");
        Assert.DoesNotContain(cases, c => c.Status == LabStatus.Fail);
    }
}
