using System;
using System.IO;
using System.Linq;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>Owner 2026-10-11: the data transfer register (who carried what, on which medium, with SHA-256 and a four-eyes witness).</summary>
    public class TransferRegisterTests
    {
        private const string H1 = "D7A8FBB307D7809469CA9ABCB0082E4F8D5651E46D3CDB762D02D0BF37C9E592";

        private static TransferRow Row(string nr = "1234-2026-0001") => new()
        {
            RegistrationNumber = nr, DateTime = "2026-10-11 10:00", Classification = "secret", Direction = "ieșire",
            Source = "Structura A", SourceStation = "1234DOC5143SV", Destination = "Structura B", Person = "ion", MediumType = "Stick USB",
            MediumSerial = "AA11", Content = "plan.7z", Sha256 = H1, Witness = "maria",
        };

        [Fact]
        public void A_complete_transfer_is_valid_and_four_eyes_is_enforced()
        {
            var reg = new TransferRegister { Rows = [Row()] };
            Assert.DoesNotContain(reg.Validate(), i => i.IsError);
            var same = Row(); same.Witness = "ION";
            Assert.Contains(new TransferRegister { Rows = [same] }.Validate(), i => i.IsError && i.Message.Contains("patru ochi"));
            var noHash = Row(); noHash.Sha256 = "abc";
            Assert.Contains(new TransferRegister { Rows = [noHash] }.Validate(), i => i.Message.Contains("64 de caractere"));
            var noMedium = Row(); noMedium.MediumSerial = "";
            Assert.Contains(new TransferRegister { Rows = [noMedium] }.Validate(), i => i.Message.Contains("mediul de stocare nu este identificat"));
        }

        [Fact]
        public void Numbers_are_automatic_per_year_and_never_reuse_a_gap()
        {
            var reg = new TransferRegister { Rows = [Row("1234-2026-0001"), Row("1234-2026-0007"), Row("1234-2025-0099")] };
            Assert.Equal("1234-2026-0008", reg.NextNumber("1234", 2026));
            Assert.Equal("TR-2027-0001", reg.NextNumber("", 2027));
        }

        [Fact]
        public void Transfers_are_checked_against_the_media_register()
        {
            var media = new MediaRegister { Rows = [new MediaRow { RegistrationNumber = "1234-56S", Serial = "AA11", Classification = "secret de serviciu", Status = "activ", ValidTo = "2026-12-31" }] };
            var f = TransferChecks.Check(new TransferRegister { Rows = [Row()] }, media);
            Assert.Contains(f, x => x.Message.Contains("nivel mai mare decât mediul"));      // SECRET content on an SDS medium
            var unknown = Row(); unknown.MediumSerial = "ZZ99";
            Assert.Contains(TransferChecks.Check(new TransferRegister { Rows = [unknown] }, media), x => x.Message.Contains("NU este în registrul de medii"));
        }

        [Fact]
        public void The_store_only_appends_and_keeps_an_audit_trail()
        {
            var dir = Path.Combine(Path.GetTempPath(), "la-tr-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, "transfers.json");
            try
            {
                Assert.DoesNotContain(TransferStore.Append(Row(), path, out var sha1), i => i.IsError);
                var bad = Row("1234-2026-0002"); bad.Witness = "";
                Assert.Contains(TransferStore.Append(bad, path, out _), i => i.IsError);
                Assert.Single(TransferStore.Load(path, out _).Rows);                         // the invalid one was not written
                Assert.True(File.Exists(RegisterStore.AuditPathFor(path)));
                Assert.Equal(64, sha1.Length);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Fact]
        public void The_register_prints_with_signatures()
        {
            var path = Path.Combine(Path.GetTempPath(), "la-tr-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                LogAnalyzer.Dfir.Windows.Registers.TransferReportPdf.Write(path, new TransferRegister { Rows = [Row()] }, [], new LogAnalyzer.Dfir.Reporting.ReportHeaderProfile { Unit = "Unitatea X" }, "SECRET", true);
                Assert.True(new FileInfo(path).Length > 1000);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
