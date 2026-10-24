using System.Net;
using System.Text.Json;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Regression: the live-state snapshot crashed the investigation ("SocketException 10045") because IPEndPoint was serialized as an object.</summary>
public class LiveStateSerializationTests
{
    [Fact]
    public void Connections_round_trip_as_text_for_ipv4_and_ipv6()
    {
        var conns = new List<TcpConnection>
        {
            new(4, new IPEndPoint(IPAddress.Parse("10.0.0.5"), 49152), new IPEndPoint(IPAddress.Parse("93.184.216.34"), 443), "ESTABLISHED"),
            new(8, new IPEndPoint(IPAddress.IPv6Loopback, 135), new IPEndPoint(IPAddress.Parse("2001:db8::1"), 0), "LISTEN"),
        };
        var json = JsonSerializer.Serialize(conns);
        Assert.Contains("\"10.0.0.5:49152\"", json);
        Assert.Contains("[2001:db8::1]:0", json);
        var back = JsonSerializer.Deserialize<List<TcpConnection>>(json)!;
        Assert.Equal(conns, back);
        Assert.Equal("93.184.216.34", back[0].Remote.Address.ToString());
    }

    [Fact]
    public void A_live_snapshot_with_connections_serializes_and_reads_back()
    {
        var snap = new LiveStateCollector.Snapshot(DateTimeOffset.UtcNow, "PC1", [], [new(4, new IPEndPoint(IPAddress.Loopback, 1), new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53), "ESTABLISHED")], [], [], [], []);
        var text = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
        var back = JsonSerializer.Deserialize<LiveStateCollector.Snapshot>(text)!;
        Assert.Equal("8.8.8.8:53", back.Connections[0].Remote.ToString());
    }
}
