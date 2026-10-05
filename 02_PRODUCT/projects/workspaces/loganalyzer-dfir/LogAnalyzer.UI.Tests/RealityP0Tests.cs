using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// P0 (docs/dfir/REALITY_AUDIT.md): no success reported for actions that were not executed, no invented certificate
    /// fields, no current time standing in for a missing evidence timestamp.
    /// </summary>
    public class RealityP0Tests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ContainmentScript_IsNeverReportedAsExecuted(bool airGapped)
        {
            var r = new AlertActionTriggerService().ExecuteContainmentScript("Izolare Cont / Stație", "user1", airGapped);

            Assert.False(r.Success);
            Assert.Equal(ContainmentExecutionResult.NotExecuted, r.Status);
            Assert.DoesNotContain("SIMULATION", r.OutputLog);
            Assert.DoesNotContain("PENDING AGENT ACK", r.OutputLog);
            Assert.DoesNotContain("validată", r.OutputLog);
            Assert.Contains("NU a fost executată", r.OutputLog);
        }

        [Fact]
        public async Task Sanitization_ZeroPass_IsVerifiedByReadBack()
        {
            var f = TempFile(new byte[200_000]);
            try
            {
                File.WriteAllBytes(f, RandomBytes(200_000));
                var r = await new MediaSanitizationEngine().SanitizeMediaAsync(f, SanitizationMethod.NistClearZero);
                Assert.True(r.Success);
                Assert.True(r.ReadBackVerified);
                Assert.Null(r.ReadBackMismatchOffset);
            }
            finally { File.Delete(f); }
        }

        [Fact]
        public async Task Sanitization_RandomLastPass_CannotBeVerifiedAsZeroized()
        {
            var f = TempFile(RandomBytes(100_000));
            try
            {
                var r = await new MediaSanitizationEngine().SanitizeMediaAsync(f, SanitizationMethod.NistClearRandom);
                Assert.True(r.Success);
                Assert.False(r.ReadBackVerified);
                Assert.False(string.IsNullOrEmpty(r.ReadBackNote));
            }
            finally { File.Delete(f); }
        }

        [Fact]
        public void Certificate_FromResult_HasNoInventedFields()
        {
            var result = new SanitizationResult
            {
                Success = true, Method = SanitizationMethod.NistClearZero, TotalPassesExecuted = 1, TotalBytesSanitized = 4096,
                PreSanitizationSha256 = "aa", PostSanitizationSha256 = "bb", ReadBackVerified = true,
                StartedAtUtc = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc), CompletedAtUtc = new DateTime(2026, 10, 5, 8, 0, 1, DateTimeKind.Utc),
            };
            var c = SanitizationCertificateData.FromResult(result, @"C:\x\disk.img", "operator1");

            Assert.Equal("", c.HardwareSerialNumber);
            Assert.Equal("", c.VerifierOperator);
            Assert.True(c.IsVerifiedZeroized);
            Assert.Equal(result.CompletedAtUtc, c.TimestampUtc);
            Assert.Equal(64, c.TamperEvidentAuditHash.Length);
            Assert.Equal(c.ComputeAuditHash(), c.TamperEvidentAuditHash);

            var unverified = SanitizationCertificateData.FromResult(new SanitizationResult { Success = true, ReadBackVerified = false }, "f", "op");
            Assert.False(unverified.IsVerifiedZeroized);
            Assert.False(new SanitizationCertificateData().IsVerifiedZeroized);
        }

        [Fact]
        public void TextCertificate_ShowsUndeclaredFieldsAsSuch()
        {
            var text = new SanitizationCertificateGenerator().GenerateTextCertificate(new SanitizationCertificateData());
            Assert.Contains("NECONFIRMATĂ", text);
            Assert.Contains("NEDECLARAT", text);
        }

        [Fact]
        public void SuperTimeline_RegistryWithoutLastWrite_IsNotStampedWithNow()
        {
            var csv = Path.Combine(Path.GetTempPath(), $"st_{Guid.NewGuid():N}.csv");
            try
            {
                new SuperTimelineExportService().ExportPlasoCsv(csv, new List<ParsedEvent>(), new List<ForensicArtifact>(),
                    new List<RegistryArtifact> { new RegistryArtifact { KeyPath = @"HKLM\X", ValueName = "v", LastWriteTime = null } });
                var line = File.ReadAllLines(csv)[1];
                Assert.StartsWith("-,-,", line);
                Assert.DoesNotContain(DateTime.UtcNow.ToString("MM/dd/yyyy"), line);
            }
            finally { File.Delete(csv); }
        }

        private static string TempFile(byte[] content)
        {
            var f = Path.Combine(Path.GetTempPath(), $"p0_{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(f, content);
            return f;
        }

        private static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            new Random(7).NextBytes(b);
            for (int i = 0; i < n; i += 997) b[i] |= 1;
            return b;
        }
    }
}
