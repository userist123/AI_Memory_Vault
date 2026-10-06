using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    public class SecurityEventIngestionServiceTests
    {
        [Fact]
        public async Task ReadsValidMetadataOnlyEvent()
        {
            var path = Path.Combine(Path.GetTempPath(), $"mv-security-{Guid.NewGuid():N}.jsonl");
            try
            {
                await File.WriteAllTextAsync(path,
                    """{"schema_version":1,"timestamp":"2026-10-02T12:00:00Z","event_type":"TOOL_BLOCKED","source":"runtime_adapter","actor":"agent","correlation_id":"corr-1","trust_state":"BLOCKED","tool":{"name":"demo","allowed":false,"decision_reason":"policy_denied"},"metadata":{"repository":"owner/repo"}}"""
                    + Environment.NewLine,
                    Encoding.UTF8);

                var service = new SecurityEventIngestionService();
                var records = await service.ReadAsync(path);

                Assert.Single(records);
                Assert.Equal("TOOL_BLOCKED", records[0].EventType);
                Assert.Equal("BLOCKED", records[0].TrustState);
                Assert.Equal("AI Security", service.ToTimelineItem(records[0]).Category);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task RejectsUnknownMetadataAndInvalidHash()
        {
            var path = Path.Combine(Path.GetTempPath(), $"mv-security-{Guid.NewGuid():N}.jsonl");
            try
            {
                await File.WriteAllTextAsync(path,
                    """{"schema_version":1,"timestamp":"2026-10-02T12:00:00Z","event_type":"IOC_OBSERVED","source":"runtime","actor":"agent","correlation_id":"corr-2","artifact":{"sha256":"not-a-hash"},"metadata":{"raw_prompt":"secret"}}"""
                    + Environment.NewLine,
                    Encoding.UTF8);

                var service = new SecurityEventIngestionService();
                var records = await service.ReadAsync(path);

                Assert.Empty(records);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
