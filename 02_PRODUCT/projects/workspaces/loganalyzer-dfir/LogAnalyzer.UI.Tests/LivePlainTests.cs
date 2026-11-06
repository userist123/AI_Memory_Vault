using System;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services.Network;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>Live alerts told in plain words (owner 2026-11-06): when, on which computer, who, what; no event codes, technique ids or tool names.</summary>
    public class LivePlainTests
    {
        private const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        private static readonly DateTime T0 = new(2026, 11, 6, 13, 20, 0, DateTimeKind.Utc);
        private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");

        private static ParsedEvent Ev(int id, string data, string message = "") => new()
        {
            EventId = id, MachineName = "LAB-WS01", TimeCreated = T0, Message = message,
            XmlData = $"<Event xmlns=\"{Ns}\"><EventData>{data}</EventData></Event>",
        };

        private static string D(string name, string value) => $"<Data Name=\"{name}\">{value}</Data>";

        private static void NoCodes(string s) => Assert.DoesNotMatch(new Regex(@"\b(?:\d{3,5}|T\d{4}(?:\.\d{3})?)\b|S-1-5-|ALERTĂ|🚨|⚠"), s.Replace("2026", "").Replace("15:20", ""));

        [Fact]
        public void A_cleared_log_says_who_cleared_which_log_where_and_when()
        {
            var a = new LiveSecurityMonitoringEngine().EvaluateLiveEvent(Ev(104, D("SubjectUserName", "labadmin")));
            Assert.NotNull(a);
            var s = LivePlain.Tell(a!, Ev(104, D("SubjectUserName", "labadmin")), Bucharest);
            Assert.StartsWith("Pe 6 noiembrie 2026, la ora 15:20, pe calculatorul LAB-WS01, utilizatorul cu contul „labadmin” a șters jurnalul", s);
            NoCodes(s);
            Assert.NotEmpty(a.Plain);   // the engine fills it on every alert
        }

        [Fact]
        public void A_new_account_and_a_new_service_name_the_account_and_the_program()
        {
            var e = new LiveSecurityMonitoringEngine();
            var acc = Ev(4720, D("SubjectUserName", "ana.pop") + D("TargetUserName", "labbackup$"));
            var s1 = LivePlain.Tell(e.EvaluateLiveEvent(acc)!, acc, Bucharest);
            Assert.Contains("utilizatorul cu contul „ana.pop” a creat contul nou „labbackup$”", s1);
            NoCodes(s1);
            var svc = Ev(7045, D("ServiceName", "UpdSvc") + D("ImagePath", @"C:\ProgramData\upd\updsvc.exe") + D("AccountName", "LocalSystem"));
            var s2 = LivePlain.Tell(e.EvaluateLiveEvent(svc)!, svc, Bucharest);
            Assert.Contains(@"a instalat serviciul nou „UpdSvc”, care pornește singur programul C:\ProgramData\upd\updsvc.exe", s2);
        }

        [Fact]
        public void Repeated_wrong_passwords_say_whose_account_and_from_where_without_blaming_the_owner()
        {
            var e = new LiveSecurityMonitoringEngine();
            DetectedIssue? a = null; ParsedEvent? last = null;
            for (int i = 0; i < 5; i++) a = e.EvaluateLiveEvent(last = Ev(4625, D("TargetUserName", "ion") + D("IpAddress", "10.0.0.5") + D("WorkstationName", "LAB-WS02")));
            var s = LivePlain.Tell(a!, last!, Bucharest);
            Assert.Contains("cineva a greșit de mai multe ori parola contului „ion”, de la calculatorul LAB-WS02 (adresa 10.0.0.5)", s);
            NoCodes(s.Replace("10.0.0.5", ""));
        }
    }
}
