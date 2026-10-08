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
    [Collection(AppModeCollection.Name)]
    public class NetworkEditionServicesTests
    {
        [Fact]
        public void SiemForwarderService_FormatsCefSyslogCorrectly()
        {
            var forwarder = new SiemForwarderService();
            var issues = new List<DetectedIssue>
            {
                new DetectedIssue
                {
                    Title = "Tentativă Mimikatz LSASS Dump",
                    Severity = "Critical",
                    MitreTechniqueId = "T1003.001",
                    Explanation = "Acces neautorizat la procesul LSASS de la un proces extern nesemnat."
                }
            };

            var cefList = forwarder.FormatToCefSyslog(issues);

            Assert.NotNull(cefList);
            Assert.Single(cefList);
            Assert.Contains("CEF:0|LogAnalyzer|DFIR Enterprise", cefList[0]);
            Assert.Contains("ALERT_T1003.001", cefList[0]);
            Assert.Contains("Tentativă Mimikatz LSASS Dump", cefList[0]);
        }

        [Fact]
        public async Task LiveThreatIntelService_HandlesEmptyApiKeyGracefully()
        {
            AppModeTestScope.Use(AppMode.Network);
            var service = new LiveThreatIntelService();
            var rep = await service.CheckIpReputationAsync("198.51.100.24", "");

            Assert.NotNull(rep);
            Assert.Equal("198.51.100.24", rep.IocValue);
            Assert.Equal(0, rep.MaliciousScore);
            Assert.Contains("Cheia API nu este configurată", rep.Details);
        }
    }
}
