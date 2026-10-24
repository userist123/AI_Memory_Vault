using System;
using System.Collections.Generic;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Audit 2026-10-11 (docs/dfir/RESEARCH_2026-10-11_CSIRT_RAPORTARE.md §1.3): the NIS2 draft declared four mitigation measures as applied whatever the
    /// case said, counted deadlines from detection, addressed an e-mail address and hard-coded the sector; the MISP/STIX exports labelled every
    /// non-hash IOC as an IP address and every STIX IOC as a SHA-256 file hash.
    /// </summary>
    public class Nis2AndIocExportTests
    {
        private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void The_draft_never_declares_measures_the_case_does_not_record()
        {
            var text = new Nis2NotificationService().GenerateDnscDraft(Nis2Stage.Notification_72h, new Nis2Entity(), "", "Ransomware", "", null, null, 10, 2, nowUtc: Now);
            Assert.DoesNotContain("Izolarea logică", text);
            Assert.DoesNotContain("Revocarea conturilor", text);
            Assert.Contains("Nicio măsură nu este înregistrată în caz", text);
            Assert.Contains("PNRISC", text);
            Assert.DoesNotContain("contact@dnsc.ro", text);
            Assert.DoesNotContain("Infrastructură Critică", text);
            Assert.Contains("[DE COMPLETAT: denumirea entității]", text);
            Assert.Contains("Termenele nu se pot calcula", text);
        }

        [Fact]
        public void Deadlines_run_from_awareness_and_the_final_report_from_the_notification()
        {
            var aware = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
            var text = new Nis2NotificationService().GenerateDnscDraft(Nis2Stage.EarlyWarning_24h, new Nis2Entity("Org", "RO1", "esențială", "energie"), "CAZ-1", "T", "WS-01",
                aware, null, 0, 0, crossBorder: true, nowUtc: Now);
            Assert.Contains("Avertizare timpurie: până la 2026-10-11 08:00 UTC", text);
            Assert.Contains("Notificare de incident: până la 2026-10-13 08:00 UTC", text);
            Assert.Contains("2026-10-10 14:00 UTC", text);   // 6 h cross-border
            Assert.Equal(new DateTime(2026, 11, 13, 9, 0, 0), Nis2Deadlines.Final(new DateTime(2026, 10, 13, 9, 0, 0)));
            var final = new Nis2NotificationService().GenerateDnscDraft(Nis2Stage.FinalReport_1Month, new Nis2Entity(), "", "T", "", aware, null, 0, 0, nowUtc: Now);
            Assert.Contains("[DE COMPLETAT: data transmiterii notificării de 72 de ore]", final);
        }

        [Fact]
        public void Recorded_measures_are_split_into_applied_and_in_progress()
        {
            var actions = new List<Nis2ResponseAction>
            {
                new(new DateTime(2026, 10, 10, 9, 0, 0), "Stația WS-01 izolată din rețea", "PC\\ion", true),
                new(new DateTime(2026, 10, 10, 10, 0, 0), "Resetarea parolelor conturilor afectate", "PC\\maria", false),
            };
            var text = new Nis2NotificationService().GenerateDnscDraft(Nis2Stage.Notification_72h, new Nis2Entity(), "", "T", "", null, null, 0, 0, actions, nowUtc: Now);
            Assert.True(text.IndexOf("Aplicate:") < text.IndexOf("Stația WS-01 izolată"));
            Assert.True(text.IndexOf("În curs:") < text.IndexOf("Resetarea parolelor"));
        }

        [Theory]
        [InlineData(IocType.IPv4, "10.0.0.5", "ip-dst", "[ipv4-addr:value = '10.0.0.5']")]
        [InlineData(IocType.IPv6, "fe80::1", "ip-dst", "[ipv6-addr:value = 'fe80::1']")]
        [InlineData(IocType.Domain, "rau.example", "domain", "[domain-name:value = 'rau.example']")]
        [InlineData(IocType.URL, "http://rau.example/x", "url", "[url:value = 'http://rau.example/x']")]
        [InlineData(IocType.Hash, "d41d8cd98f00b204e9800998ecf8427e", "md5", "[file:hashes.MD5 = 'd41d8cd98f00b204e9800998ecf8427e']")]
        [InlineData(IocType.Hash, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "sha256", "[file:hashes.'SHA-256' = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855']")]
        public void Every_ioc_is_exported_as_what_it_is(IocType type, string value, string misp, string stix)
        {
            var ioc = new IocItem { Type = type, Value = value };
            Assert.Equal(misp, StixMispExportService.MispType(ioc));
            Assert.Equal(stix, StixMispExportService.StixPattern(ioc));
        }

        [Fact]
        public void Quotes_in_a_stix_value_are_escaped() =>
            Assert.Equal(@"[url:value = 'http://x/a\'b']", StixMispExportService.StixPattern(new IocItem { Type = IocType.URL, Value = "http://x/a'b" }));
    }
}
