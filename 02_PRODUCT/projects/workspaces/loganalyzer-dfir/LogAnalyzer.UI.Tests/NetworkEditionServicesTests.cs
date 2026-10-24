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

        /// <summary>
        /// Security audit #19: the online threat-intel and Microsoft 365 connectors had no production consumer, interpolated unescaped values into
        /// request URLs and held secrets in plain config objects. They were deleted; this keeps them from coming back unreviewed.
        /// </summary>
        [Theory]
        [InlineData("LogAnalyzer.Core.Services.Network.LiveThreatIntelService")]
        [InlineData("LogAnalyzer.Core.Services.Network.M365LiveConnectorService")]
        [InlineData("LogAnalyzer.Core.Services.Network.M365AuthConfig")]
        public void Dead_online_connectors_are_not_shipped(string typeName)
        {
            Assert.Null(typeof(SiemForwarderService).Assembly.GetType(typeName));
        }
    }
}
