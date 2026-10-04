using System;
using System.Threading.Tasks;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Network;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>Tests that touch the process-wide operating mode run in one collection, never in parallel.</summary>
    [CollectionDefinition(Name)]
    public sealed class AppModeCollection
    {
        public const string Name = "AppMode (process-wide state)";
    }

    internal static class AppModeTestScope
    {
        public static void Use(AppMode mode)
        {
            AppModeContext.ResetForTests();
            AppModeContext.Initialize(new ModeDecision(mode, true, "test", ConnectivitySnapshot.Unknown("test")));
        }
    }

    [Collection(AppModeCollection.Name)]
    public class OperatingModeTests
    {
        private static ConnectivitySnapshot Snap(ConnectivityState s) => new(s, "test", Array.Empty<string>(), DateTime.UtcNow);

        [Theory]
        [InlineData(ConnectivityState.Internet, AppMode.Network)]
        [InlineData(ConnectivityState.LocalNetworkOnly, AppMode.AirGapped)]
        [InlineData(ConnectivityState.NoNetwork, AppMode.AirGapped)]
        [InlineData(ConnectivityState.Unknown, AppMode.AirGapped)]
        public void Detection_maps_only_confirmed_internet_to_network(ConnectivityState state, AppMode expected)
        {
            var d = OperatingModeResolver.Resolve(Array.Empty<string>(), null, Snap(state));
            Assert.Equal(expected, d.Mode);
            Assert.False(d.IsOverride);
        }

        [Fact]
        public void Command_line_beats_station_file_which_beats_detection()
        {
            var online = Snap(ConnectivityState.Internet);
            Assert.Equal(AppMode.AirGapped, OperatingModeResolver.Resolve(new[] { "--mode=airgapped" }, "network", online).Mode);
            Assert.Equal(AppMode.AirGapped, OperatingModeResolver.Resolve(Array.Empty<string>(), " offline\r\n", online).Mode);
            Assert.Equal(AppMode.Network, OperatingModeResolver.Resolve(new[] { "--MODE=Network" }, null, Snap(ConnectivityState.NoNetwork)).Mode);
            // auto defers to detection even when a station file exists
            Assert.Equal(AppMode.Network, OperatingModeResolver.Resolve(new[] { "--mode=auto" }, "airgapped", online).Mode);
        }

        [Fact]
        public void Unknown_override_values_fail_closed()
        {
            var online = Snap(ConnectivityState.Internet);
            Assert.Equal(AppMode.AirGapped, OperatingModeResolver.Resolve(new[] { "--mode=netwrk" }, null, online).Mode);
            Assert.Equal(AppMode.AirGapped, OperatingModeResolver.Resolve(Array.Empty<string>(), "maybe", online).Mode);
        }

        [Theory]
        [InlineData(0x0, ConnectivityState.NoNetwork)]
        [InlineData(0x1, ConnectivityState.NoNetwork)]              // IPV4_NOTRAFFIC
        [InlineData(0x11, ConnectivityState.LocalNetworkOnly)]      // IPV4_SUBNET
        [InlineData(0x20, ConnectivityState.LocalNetworkOnly)]      // IPV4_LOCALNETWORK
        [InlineData(0x42, ConnectivityState.Internet)]              // IPV4_INTERNET | IPV6_NOTRAFFIC
        [InlineData(0x402, ConnectivityState.Internet)]             // IPV6_INTERNET only
        public void Network_list_manager_flags_are_mapped(int flags, ConnectivityState expected)
        {
            Assert.Equal(expected, WindowsConnectivityProbe.MapNlmConnectivity(flags));
        }

        [Fact]
        public void Probe_runs_on_this_station_without_throwing()
        {
            var s = new WindowsConnectivityProbe().Probe();
            Assert.False(string.IsNullOrEmpty(s.Source));
        }

        [Fact]
        public async Task AirGapped_mode_blocks_networked_features()
        {
            AppModeTestScope.Use(AppMode.AirGapped);
            var ex = await Assert.ThrowsAsync<NetworkBlockedException>(
                () => new LiveThreatIntelService().CheckIpReputationAsync("198.51.100.24", "key"));
            Assert.Contains("AirGapped", ex.Message);
            Assert.Throws<NetworkBlockedException>(() =>
                new LogAnalyzer.Infrastructure.Services.AuditCollectionService().StartSyslogListener(0, ".", "h", _ => { }));

            AppModeTestScope.Use(AppMode.Network);
            NetworkPolicy.EnsureAllowed("test");
        }

        [Fact]
        public void Mode_cannot_be_changed_once_set()
        {
            AppModeTestScope.Use(AppMode.AirGapped);
            Assert.Throws<InvalidOperationException>(() =>
                AppModeContext.Initialize(new ModeDecision(AppMode.Network, false, "x", ConnectivitySnapshot.Unknown("x"))));
        }
    }
}
