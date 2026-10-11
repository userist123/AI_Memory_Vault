using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Regression against the real MARIUS-PC corpus (spec §110). Expected values come from targets.json;
/// the parsers know nothing about this incident.
/// </summary>
public class CorpusRegressionTests
{
    private static EvidenceItem Item(string type, TemporalType t = TemporalType.Historical) =>
        new() { EvidenceId = "EV-TEST", CaseId = "REG", Source = "corpus", SourceType = type, TemporalType = t };

    [CorpusFact("prefetch")]
    public void Prefetch_mam_is_really_decompressed_with_run_count_and_times()
    {
        var pf = PrefetchParser.Read(Corpus.File("prefetch"));
        Assert.Equal(Corpus.S("prefetch", "exeName"), pf.ExeName);
        Assert.Equal((int)Corpus.L("prefetch", "runCount"), pf.RunCount);
        Assert.Equal(DateTime.Parse(Corpus.S("prefetch", "lastRunUtc")), pf.RunTimesUtc[0].UtcDateTime, TimeSpan.FromSeconds(1));
        Assert.Contains(Corpus.S("prefetch", "pathContains"), pf.ExePathGuess, StringComparison.OrdinalIgnoreCase);

        var sink = new ListSink();
        var r = new PrefetchParser().Parse(Item("prefetch"), Corpus.File("prefetch"), sink, default);
        Assert.Equal(EvidenceStatus.Success, r.Status);
        Assert.Equal(pf.RunTimesUtc.Count, sink.Events.Count);
        Assert.All(sink.Events, e => Assert.True(e.Time.IsKnown));
    }

    [CorpusFact("prefetchMsiexec")]
    public void Prefetch_referenced_files_expose_the_loader_folder()
    {
        var pf = PrefetchParser.Read(Corpus.File("prefetchMsiexec"));
        Assert.Contains(pf.ReferencedFiles, f => f.Contains(Corpus.S("prefetchMsiexec", "referencedFileContains"), StringComparison.OrdinalIgnoreCase));
    }

    [CorpusFact("srum")]
    public void Srum_network_usage_reproduces_the_exfiltration_volume()
    {
        var rows = SrumNetworkParser.Read(Corpus.File("srum")).ToList();
        Assert.True(rows.Count > 1000);
        var app = Corpus.S("srum", "appContains");
        var ts = DateTime.Parse(Corpus.S("srum", "timestampUtc"));
        var hit = Assert.Single(rows, r => r.App.Contains(app, StringComparison.OrdinalIgnoreCase) && Math.Abs((r.TimestampUtc - ts).TotalMinutes) < 1);
        Assert.Equal(Corpus.L("srum", "bytesSent"), hit.BytesSent);
        Assert.Equal(Corpus.L("srum", "bytesRecvd"), hit.BytesRecvd);
    }

    [CorpusFact("defenderEvtx")]
    public void Defender_evtx_detection_is_parsed_with_path_and_threat()
    {
        var sink = new ListSink();
        var r = new EvtxParser().Parse(Item("evtx"), Corpus.File("defenderEvtx"), sink, default);
        Assert.True(r.Status is EvidenceStatus.Success or EvidenceStatus.Partial, r.Error);
        Assert.Contains(sink.Events, e => e.EventId == Corpus.S("defenderEvtx", "eventId")
                                          && e.Path.Contains(Corpus.S("defenderEvtx", "pathContains"), StringComparison.OrdinalIgnoreCase)
                                          && e.Fields.GetValueOrDefault("Threat Name") == Corpus.S("defenderEvtx", "threatName"));
        Assert.All(sink.Events, e => Assert.StartsWith("EventRecordID=", e.Locator));
    }

    [CorpusFact("msiEvtx")]
    public void Application_evtx_msiinstaller_install_is_found()
    {
        var sink = new ListSink();
        new EvtxParser().Parse(Item("evtx"), Corpus.File("msiEvtx"), sink, default);
        Assert.Contains(sink.Events, e => e.Provider == Corpus.S("msiEvtx", "provider") && e.EventId == Corpus.S("msiEvtx", "eventId")
                                          && e.Summary.Contains(Corpus.S("msiEvtx", "messageContains"), StringComparison.Ordinal));
    }

    [CorpusFact("pcapng")]
    public void Pcapng_wifi_capture_yields_flows_dns_and_sni()
    {
        var sink = new ListSink();
        var r = new PcapngParser().Parse(Item("pcapng", TemporalType.Live), Corpus.File("pcapng"), sink, default);
        Assert.True(r.Status is EvidenceStatus.Success or EvidenceStatus.Partial, r.Error);
        int flows = sink.Events.Count(e => e.Source == "PCAP:flow");
        Assert.True(flows >= Corpus.L("pcapng", "minFlows"), $"flows={flows} records={r.Records} status={r.Status} error={r.Error} gaps={string.Join(" | ", r.Gaps.Select(g => g.Reason))}");
        Assert.Contains(sink.Events, e => e.Source == "PCAP:DNS" && e.Dns.Contains(Corpus.S("pcapng", "dnsNameContains")));
        Assert.Contains(sink.Events, e => e.Source == "PCAP:TLS" && e.Dns.Contains(Corpus.S("pcapng", "sniContains")));
        Assert.All(sink.Events, e => Assert.Equal(TemporalType.Live, e.TemporalType));
    }
}
