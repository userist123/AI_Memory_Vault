using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Network;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// AirGapped: every networked service refuses before any request leaves the process. A counting handler stands in for
    /// the network, so "0 requests" is measured, not assumed.
    /// </summary>
    [Collection(AppModeCollection.Name)]
    public class AirGappedNoNetworkTests
    {
        private sealed class CountingHandler : HttpMessageHandler
        {
            public int Requests;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Interlocked.Increment(ref Requests);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            }
        }

        public static IEnumerable<object[]> Calls()
        {
            yield return new object[] { "threat-intel ip", (Func<HttpClient, Task>)(c => new LiveThreatIntelService(c).CheckIpReputationAsync("198.51.100.24", "k")) };
            yield return new object[] { "threat-intel hash", (Func<HttpClient, Task>)(c => new LiveThreatIntelService(c).CheckFileHashReputationAsync(new string('a', 64), "k")) };
            yield return new object[] { "siem", (Func<HttpClient, Task>)(c => new SiemForwarderService(c).ForwardAlertsToSplunkHecAsync(
                new List<DetectedIssue>(), new SiemForwarderConfig { SplunkHecUrl = "https://siem.invalid/x", SplunkHecToken = "t" })) };
            yield return new object[] { "m365 token", (Func<HttpClient, Task>)(c => new M365LiveConnectorService(c).GetAccessTokenAsync(
                new M365AuthConfig { TenantId = "t", ClientId = "c", ClientSecret = "s" })) };
            yield return new object[] { "m365 sign-ins", (Func<HttpClient, Task>)(c => new M365LiveConnectorService(c).FetchRecentSignInsAsync("token")) };
        }

        [Theory]
        [MemberData(nameof(Calls))]
        public async Task AirGapped_sends_no_request(string name, Func<HttpClient, Task> call)
        {
            AppModeTestScope.Use(AppMode.AirGapped);
            var handler = new CountingHandler();
            using var client = new HttpClient(handler);

            await Assert.ThrowsAsync<NetworkBlockedException>(() => call(client));
            Assert.True(handler.Requests == 0, $"{name}: {handler.Requests} cereri trimise în AirGapped");
        }

        [Fact]
        public async Task Network_mode_does_reach_the_handler()
        {
            // Control: the same harness sees traffic when the mode allows it, so the AirGapped zero above is meaningful.
            AppModeTestScope.Use(AppMode.Network);
            var handler = new CountingHandler();
            using var client = new HttpClient(handler);
            await new SiemForwarderService(client).ForwardAlertsToSplunkHecAsync(
                new List<DetectedIssue> { new DetectedIssue() }, new SiemForwarderConfig { SplunkHecUrl = "https://siem.invalid/x", SplunkHecToken = "t" });
            Assert.True(handler.Requests > 0);
        }
    }
}
