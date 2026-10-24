using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Connectors.Collection;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Security audit #7: the UDP syslog receiver binds to one chosen interface, accepts only allow-listed sources, writes each datagram as one escaped
    /// line (no forged CR/LF lines), caps message size, rate and the UI log, refuses a host name that could leave the output folder and hashes the
    /// evidence log as it is written.
    /// </summary>
    public sealed class SyslogIngestSecurityTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-syslog-" + Guid.NewGuid().ToString("N"));
        private static readonly IPEndPoint Dc = new(IPAddress.Parse("10.0.0.5"), 51514);
        private static readonly DateTimeOffset T0 = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

        public SyslogIngestSecurityTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        private SyslogIngest Ingest(List<string> ui, params string[] allowed) =>
            new(Path.Combine(_dir, "syslog.log"), allowed.Select(IPAddress.Parse).ToHashSet(), ui.Add);

        [Fact]
        public void A_datagram_with_CR_LF_and_a_forged_prefix_becomes_one_escaped_line()
        {
            var ui = new List<string>();
            using (var s = Ingest(ui, "10.0.0.5"))
                s.Accept(T0, Dc, Encoding.UTF8.GetBytes("<13>real\r\n[2026-01-01 00:00:00] [1.2.3.4:514] fake\u0007\u001b[31m"));
            var lines = File.ReadAllLines(Path.Combine(_dir, "syslog.log"));
            Assert.Single(lines);
            Assert.Contains(@"real\r\n[2026-01-01 00:00:00] [1.2.3.4:514] fake\x07\x1B[31m", lines[0]);
            Assert.StartsWith("[2026-10-12 10:00:00Z] [10.0.0.5:51514] ", lines[0]);
        }

        [Fact]
        public void Escaping_is_unambiguous()
        {
            Assert.Equal(@"a\\r", SyslogIngest.Escape("a\\r"));
            Assert.Equal(@"a\r", SyslogIngest.Escape("a\r"));
            Assert.Equal(@"\x00\t", SyslogIngest.Escape("\0\t").Replace("\\t", "\\t"));
        }

        [Fact]
        public void Sources_not_on_the_allow_list_are_refused_and_counted()
        {
            var ui = new List<string>();
            using (var s = Ingest(ui, "10.0.0.5"))
            {
                Assert.False(s.Accept(T0, new IPEndPoint(IPAddress.Parse("10.0.0.9"), 514), Encoding.UTF8.GetBytes("x")));
                Assert.True(s.Accept(T0, Dc, Encoding.UTF8.GetBytes("y")));
                Assert.Equal(1, s.RejectedSources);
            }
            Assert.Single(File.ReadAllLines(Path.Combine(_dir, "syslog.log")));
        }

        [Fact]
        public void Oversized_messages_are_truncated_and_marked()
        {
            var ui = new List<string>();
            using (var s = Ingest(ui, "10.0.0.5"))
                s.Accept(T0, Dc, Encoding.UTF8.GetBytes(new string('A', SyslogIngest.MaxMessageBytes * 3)));
            var line = File.ReadAllLines(Path.Combine(_dir, "syslog.log")).Single();
            Assert.True(line.Length < SyslogIngest.MaxMessageBytes + 200);
            Assert.Contains("[TRUNCHIAT", line);
        }

        [Fact]
        public void A_flood_of_100000_datagrams_keeps_the_rate_and_the_ui_log_bounded()
        {
            var ui = new List<string>();
            var uiText = "";
            int written;
            using (var s = new SyslogIngest(Path.Combine(_dir, "syslog.log"), new HashSet<IPAddress> { Dc.Address }, l => { ui.Add(l); uiText = BoundedText.Append(uiText, l, 50_000); }))
            {
                for (int i = 0; i < 100_000; i++) s.Accept(T0.AddMilliseconds(i / 100), Dc, Encoding.UTF8.GetBytes("msg " + i)); // 100 000 in one second
                written = s.Written;
                Assert.True(s.RateDropped > 0);
            }
            Assert.True(written <= SyslogIngest.MaxDatagramsPerSecond * 2, $"written {written}");
            Assert.True(ui.Count <= SyslogIngest.MaxUiLinesPerSecond * 2 + 10, $"ui lines {ui.Count}");
            Assert.True(uiText.Length <= 50_000);
            Assert.Equal(written, File.ReadAllLines(Path.Combine(_dir, "syslog.log")).Length);
        }

        [Fact]
        public void The_log_is_hashed_as_it_is_written()
        {
            var ui = new List<string>();
            var path = Path.Combine(_dir, "syslog.log");
            string hash;
            using (var s = Ingest(ui, "10.0.0.5"))
            {
                s.Accept(T0, Dc, Encoding.UTF8.GetBytes("one"));
                s.Accept(T0, Dc, Encoding.UTF8.GetBytes("two"));
                hash = s.Sha256Hex;
            }
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), hash);
            Assert.Equal(hash, File.ReadAllText(path + ".sha256").Split(' ')[0].Trim());
        }

        [Theory]
        [InlineData("DC01", null)]
        [InlineData("dc-01.corp", null)]
        [InlineData(@"..\..\x", "nume")]
        [InlineData("a/b", "nume")]
        [InlineData("a..b", "nume")]
        [InlineData("", "nume")]
        public void Hostname_that_could_leave_the_output_folder_is_refused(string hostname, string? error)
        {
            var o = new SyslogListenerOptions(514, _dir, hostname, IPAddress.Loopback, []);
            var e = UdpSyslogReceiver.Validate(o);
            if (error is null) Assert.Null(e); else Assert.Contains(error, e);
        }

        [Fact]
        public void Listening_on_all_interfaces_or_a_foreign_address_is_refused()
        {
            Assert.Contains("interfață", UdpSyslogReceiver.Validate(new SyslogListenerOptions(514, _dir, "DC01", IPAddress.Any, [])));
            Assert.Contains("interfață", UdpSyslogReceiver.Validate(new SyslogListenerOptions(514, _dir, "DC01", IPAddress.Parse("203.0.113.77"), [])));
            Assert.Null(UdpSyslogReceiver.Validate(new SyslogListenerOptions(514, _dir, "DC01", IPAddress.Loopback, [])));
        }

        [Fact]
        public void Bounded_text_keeps_only_the_newest_whole_lines()
        {
            var t = "";
            for (int i = 0; i < 1000; i++) t = BoundedText.Append(t, "line " + i, 200);
            Assert.True(t.Length <= 200);
            Assert.EndsWith("line 999" + Environment.NewLine, t);
            Assert.StartsWith("line ", t);
        }
    }
}
