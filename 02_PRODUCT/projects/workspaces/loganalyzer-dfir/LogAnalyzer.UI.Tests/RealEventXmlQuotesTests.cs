using System;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// EventRecord.ToXml (live monitoring and imported .evtx) writes &lt;Data Name='…'&gt; with apostrophes; parsers that read only Name="…" never saw a
    /// field of a real event (2026-11-08, checked on a lab Security .evtx). Synthetic values only.
    /// </summary>
    public class RealEventXmlQuotesTests
    {
        private static ParsedEvent Logon(string quote) => new()
        {
            EventId = 4624, MachineName = "LAB-WS01", ProviderName = "Microsoft-Windows-Security-Auditing", TimeCreated = new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc),
            XmlData = $"<Event><EventData><Data Name={quote}TargetUserName{quote}>ana.pop</Data><Data Name={quote}LogonType{quote}>10</Data></EventData></Event>",
        };

        [Theory]
        [InlineData("'")]
        [InlineData("\"")]
        public void The_event_explanation_reads_the_fields_whatever_the_quotes(string quote)
        {
            var a = ForensicEventKnowledgeService.GetAssessment(Logon(quote));
            Assert.Contains("ana.pop", a.ForensicDetailsRo);
            Assert.Contains("Tip Logon: 10", a.ForensicDetailsRo);
            Assert.Equal("Atenție", a.SeverityRo);   // a remote desktop logon; before the fix a real event always read as "Informativ"
        }
    }
}
