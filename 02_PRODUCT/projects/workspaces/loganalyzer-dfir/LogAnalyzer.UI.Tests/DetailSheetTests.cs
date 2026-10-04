using System;
using System.IO;
using System.Linq;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services.Details;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    [Collection("DetailCorpus (process-wide state)")]
    public class DetailSheetTests
    {
        private const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

        private static ParsedEvent Security(int id, DateTime utc, string data) => new()
        {
            EventId = id,
            TimeCreated = utc,
            ProviderName = "Microsoft-Windows-Security-Auditing",
            MachineName = "WS-01",
            Message = $"event {id}",
            XmlData = $"<Event xmlns=\"{Ns}\"><System><Provider Name=\"Microsoft-Windows-Security-Auditing\"/><EventID>{id}</EventID>" +
                      $"<TimeCreated SystemTime=\"{utc:o}\"/><EventRecordID>{id}01</EventRecordID><Channel>Security</Channel><Computer>WS-01</Computer></System>" +
                      $"<EventData>{data}</EventData></Event>",
        };

        private static string D(string name, string value) => $"<Data Name=\"{name}\">{value}</Data>";

        [Fact]
        public void Failed_rdp_logon_is_explained_with_type_reason_account_and_origin()
        {
            var t = new DateTime(2026, 9, 29, 21, 14, 3, DateTimeKind.Utc);
            var ev = Security(4625, t, D("TargetUserName", "admin") + D("TargetDomainName", "WS-01") + D("LogonType", "10") +
                                       D("Status", "0xc000006d") + D("SubStatus", "0xc000006a") + D("IpAddress", "185.220.101.47") + D("WorkstationName", "KALI"));
            DetailSheetBuilder.EventCorpus = null;
            var s = DetailSheetBuilder.Build(ev);

            Assert.Equal(t, s.TimeUtc);
            Assert.Contains(s.Fields, f => f.Section == "System" && f.Name == "EventRecordID" && f.Value == "462501");
            Assert.Contains(s.Fields, f => f.Section == "EventData" && f.Name == "LogonType" && f.Value.Contains("RDP"));
            Assert.Contains(s.Fields, f => f.Name == "SubStatus" && f.Value.Contains("parolă greșită"));
            Assert.Contains(s.Meaning, m => m.Contains("EȘUATĂ"));
            Assert.Contains(s.Meaning, m => m.Contains("Motivul eșecului: parolă greșită"));
            Assert.Contains(s.Meaning, m => m.Contains("185.220.101.47"));
            Assert.Contains("<EventData>", s.Raw);
        }

        [Fact]
        public void Related_events_are_found_through_the_logon_session_and_stay_inside_the_time_window()
        {
            var t = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
            var logon = Security(4624, t, D("TargetUserName", "owner") + D("TargetLogonId", "0x8a3f21") + D("LogonType", "2"));
            var proc = Security(4688, t.AddMinutes(3), D("SubjectLogonId", "0x8a3f21") + D("NewProcessName", @"C:\ProgramData\x\agent.exe"));
            var late = Security(4688, t.AddHours(2), D("SubjectLogonId", "0x8a3f21"));
            var other = Security(4688, t.AddMinutes(1), D("SubjectLogonId", "0x111"));
            DetailSheetBuilder.EventCorpus = () => new[] { logon, proc, late, other };
            try
            {
                var s = DetailSheetBuilder.Build(logon);
                var r = Assert.Single(s.Related);
                Assert.Same(proc, r.Source);
                Assert.Contains("0x8a3f21", r.Link);
            }
            finally { DetailSheetBuilder.EventCorpus = null; }
        }

        [Fact]
        public void An_event_without_a_timestamp_is_not_given_the_current_time()
        {
            var s = DetailSheetBuilder.Build(new ParsedEvent { EventId = 1, ProviderName = "x" });
            Assert.Null(s.TimeUtc);
            Assert.Contains("fără timestamp", s.TimeText);
        }

        [Fact]
        public void Any_other_object_gets_all_its_properties_and_a_pdf()
        {
            var item = new RegistryArtifact { KeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "Updater", ValueData = @"C:\Users\x\AppData\u.exe", LastWriteTime = DateTime.UtcNow };
            var s = DetailSheetBuilder.Build(item);
            Assert.Contains(s.Meaning, m => m.Contains("T1547.001"));
            var anon = DetailSheetBuilder.Build(new { Name = "proc", Pid = 42, Remotes = new[] { "1.2.3.4:443" } });
            Assert.Contains(anon.Fields, f => f.Name == "Pid" && f.Value == "42");
            Assert.Contains(anon.Fields, f => f.Section == "Remotes" && f.Value == "1.2.3.4:443");

            var pdf = Path.Combine(Path.GetTempPath(), $"detail-{Guid.NewGuid():N}.pdf");
            try
            {
                DetailSheetPdf.Write(s, pdf);
                Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 5));
            }
            finally { File.Delete(pdf); }
        }
    }
}
