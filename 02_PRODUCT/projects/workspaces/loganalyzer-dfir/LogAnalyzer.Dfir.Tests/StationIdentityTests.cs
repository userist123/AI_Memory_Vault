using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Senior review I1: a report on imported evidence must name the evidence's station, not the examiner's machine.</summary>
public class StationIdentityTests
{
    private static TimelineEvent E(string host) => new() { Time = new Timestamp(DateTimeOffset.UtcNow, "test", "utc"), Source = "EVTX", EvidenceId = "E1", Summary = "s", Host = host };

    [Fact]
    public void Imported_evidence_names_its_own_station_and_the_analysis_machine_separately()
    {
        var r = StationIdentity.FromEvidence([E("WS-07.corp.local"), E("WS-07"), E("ws-07")], "EXAMINER-PC");
        Assert.Equal("WS-07", r.Station);
        Assert.Equal("stația WS-07 (din probe; analiza a rulat pe EXAMINER-PC)", r.Line);
    }

    [Fact]
    public void Several_stations_are_all_listed_and_no_host_falls_back_with_a_note()
    {
        Assert.Contains("stațiile din probe: A (2 evenimente), B (1 evenimente)", StationIdentity.FromEvidence([E("A"), E("A"), E("B")], "X").Line);
        Assert.Contains("probele nu numesc calculatorul", StationIdentity.FromEvidence([E(""), E("-")], "X").Line);
        Assert.Equal("stația X", StationIdentity.FromEvidence([E("x")], "X").Line);
    }
}
