using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Persistence;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>The parsers used by the investigation pipeline. A parser not listed here is not used.</summary>
public static class WindowsParsers
{
    public static ParserRegistry Registry { get; } = new(
        [new EvtxParser(), new PrefetchParser(), new SrumNetworkParser(), new PcapngParser(), new SystemHiveExecutionParser(), new AmcacheParser(),
         new ScheduledTaskParser(), new UserHiveParser(), new SoftwareHiveParser()]);
}
