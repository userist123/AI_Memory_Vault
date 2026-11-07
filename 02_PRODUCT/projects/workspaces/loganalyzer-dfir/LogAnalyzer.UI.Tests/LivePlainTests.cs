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
            Assert.Contains(@"a fost instalat serviciul nou „UpdSvc”, care pornește singur programul C:\ProgramData\upd\updsvc.exe", s2);
            Assert.DoesNotContain("LocalSystem", s2);   // the run-as account is not the installer
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

        [Fact]
        public void An_xml_escaped_value_is_shown_decoded()
        {
            // lab VM 2026-11-07: „?&lt;PSDirectVMLegacy>” in the status bar
            Assert.Equal("?<PSDirectVMLegacy>", LiveSecurityMonitoringEngine.Field($"<Event xmlns=\"{Ns}\"><EventData><Data Name=\"TargetUserName\">?&lt;PSDirectVMLegacy&gt;</Data></EventData></Event>", "TargetUserName"));
        }

        private static ParsedEvent UserDataEv(int id, string inner) => new()
        {
            EventId = id, MachineName = "LAB-WS01", TimeCreated = T0, Message = "",
            XmlData = $"<Event xmlns=\"{Ns}\"><UserData>{inner}</UserData></Event>",
        };

        private static string Tell(ParsedEvent ev) => LivePlain.Tell(new LiveSecurityMonitoringEngine().EvaluateLiveEvent(ev)!, ev, Bucharest);

        // finding 1: 1102 / 104 keep the account in <UserData><LogFileCleared>, not in <Data Name=...>
        [Fact]
        public void Finding1_a_cleared_log_1102_and_104_name_the_account_from_UserData()
        {
            const string Ns2 = "http://schemas.microsoft.com/win/2004/08/events/eventlog";
            var s1102 = Tell(UserDataEv(1102, $"<LogFileCleared xmlns=\"{Ns2}\"><SubjectUserSid>S-1-5-21-1-2-3-1000</SubjectUserSid><SubjectUserName>labadmin</SubjectUserName><SubjectDomainName>LAB</SubjectDomainName><SubjectLogonId>0x1</SubjectLogonId></LogFileCleared>"));
            Assert.Contains("utilizatorul cu contul „labadmin” a șters jurnalul „Security”", s1102);
            Assert.DoesNotContain("cineva", s1102);
            var s104 = Tell(UserDataEv(104, $"<LogFileCleared xmlns=\"{Ns2}\"><SubjectUserName>labadmin</SubjectUserName><SubjectDomainName>LAB</SubjectDomainName><Channel>Windows PowerShell</Channel><BackupPath></BackupPath></LogFileCleared>"));
            Assert.Contains("utilizatorul cu contul „labadmin” a șters jurnalul „Windows PowerShell”", s104);
        }

        // finding 2: AccountName of a 7045 is the run-as account; missing parts are left out, never „?”
        [Fact]
        public void Finding2_a_new_service_credits_nobody_and_leaves_out_missing_parts()
        {
            var full = Tell(Ev(7045, D("ServiceName", "UpdSvc") + D("ImagePath", @"C:\x\u.exe") + D("AccountName", "LocalSystem") + D("SubjectUserName", "labadmin")));
            Assert.Contains(@"a fost instalat serviciul nou „UpdSvc”, care pornește singur programul C:\x\u.exe", full);
            Assert.DoesNotContain("labadmin", full);
            var onlyName = Tell(Ev(7045, D("ServiceName", "UpdSvc")));
            Assert.EndsWith("a fost instalat serviciul nou „UpdSvc”.", onlyName);
            var onlyImage = Tell(Ev(7045, D("ImagePath", @"C:\x\u.exe")));
            Assert.EndsWith(@"a fost instalat un serviciu nou, care pornește singur programul C:\x\u.exe.", onlyImage);
            var none = Tell(Ev(7045, ""));
            Assert.EndsWith("a fost instalat un serviciu nou.", none);
            Assert.DoesNotContain("?", full + onlyName + onlyImage + none);
        }

        // finding 3: PsExec started here names who started it and the direction (out); the service side credits no one; no tool name (round-2 review)
        [Fact]
        public void Finding3_PsExec_started_here_names_its_account_and_the_service_side_names_no_one()
        {
            var cmd = Tell(Ev(4688, D("SubjectUserName", "labadmin"), @"C:\Tools\PsExec.exe \\LAB-WS02 cmd"));
            Assert.Contains("utilizatorul cu contul „labadmin” a pornit aici un program care rulează comenzi pe alt calculator, prin rețea", cmd);
            Assert.DoesNotContain("PsExec", cmd);
            var install = Tell(Ev(7045, D("ServiceName", "PSEXESVC") + D("ImagePath", @"%SystemRoot%\PSEXESVC.exe") + D("AccountName", "LocalSystem"), "PSEXESVC"));
            Assert.Contains("a fost instalat serviciul nou „PSEXESVC”, care pornește singur programul", install);
            Assert.DoesNotContain("LocalSystem", install);
        }

        // finding 4: a typed command is told as a typed command
        [Fact]
        public void Finding4_a_typed_command_says_what_was_typed_and_that_the_result_is_not_in_the_event()
        {
            var add = Tell(Ev(4688, D("SubjectUserName", "labadmin"), "net user labbackup$ P@ssw0rd1 /add"));
            Assert.Contains("utilizatorul cu contul „labadmin” a dat comanda de creare a unui cont nou „labbackup$” („net user … /add”); rezultatul nu se vede în acest eveniment", add);
            Assert.DoesNotContain("P@ssw0rd1", add);
            var noName = Tell(Ev(4688, D("SubjectUserName", "labadmin"), "net user /add"));
            Assert.Contains("a dat comanda de creare a unui cont nou („net user … /add”)", noName);
            Assert.DoesNotContain("?", noName);
            var wipe = Tell(Ev(4688, D("SubjectUserName", "labadmin"), "wevtutil cl \"Windows PowerShell\""));
            Assert.Contains("utilizatorul cu contul „labadmin” a dat comanda de golire a jurnalului „Windows PowerShell”", wipe);
            var wipe2 = Tell(Ev(4688, D("SubjectUserName", "labadmin"), "wevtutil.exe cl Security"));
            Assert.Contains("a dat comanda de golire a jurnalului „Security”", wipe2);
        }

        // finding 12: no raw SID, no „?”
        [Fact]
        public void Finding12_a_group_member_without_a_name_is_a_local_account_never_a_sid()
        {
            var noName = Tell(Ev(4732, D("MemberName", "-") + D("MemberSid", "S-1-5-21-1-2-3-1105") + D("TargetUserName", "Administrators") + D("SubjectUserName", "labadmin")));
            // the SID alone does not say local or domain: neither is guessed (round-2 review); a well-known domain group is named
            Assert.Contains("a pus un cont al cărui nume nu apare în eveniment în grupul „Administrators”", noName);
            var domainAdmins = Tell(Ev(4732, D("MemberName", "-") + D("MemberSid", "S-1-5-21-1-2-3-512") + D("TargetUserName", "Administrators") + D("SubjectUserName", "labadmin")));
            Assert.Contains("a pus grupul de domeniu „Domain Admins” în grupul „Administrators”", domainAdmins);
            Assert.DoesNotContain("S-1-5", noName);
            var sidOnly = Tell(Ev(4732, D("MemberSid", "S-1-5-21-1-2-3-1105") + D("TargetUserName", "Administrators") + D("SubjectUserName", "labadmin")));
            Assert.DoesNotContain("S-1-5", sidOnly);
            var named = Tell(Ev(4732, D("MemberName", "CN=ana,CN=Users,DC=lab") + D("TargetUserName", "Administrators") + D("SubjectUserName", "labadmin")));
            Assert.Contains("a pus contul „ana” în grupul „Administrators”", named);
            var noGroup = Tell(Ev(4732, D("MemberName", "ana") + D("SubjectUserName", "labadmin")));
            Assert.Contains("a pus contul „ana” într-un grup", noGroup);
            Assert.DoesNotContain("?", noName + sidOnly + named + noGroup);
        }
    }
}
