using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class DnsAuditEngine
    {
        public List<DnsAuditFinding> Analyze(IEnumerable<ParsedEvent> events)
        {
            var findings = new List<DnsAuditFinding>();
            if (events == null) return findings;

            var list = events.ToList();

            var dnsEvents = list.Where(e => (e.EventId >= 257 && e.EventId <= 260) || 
                (e.Message != null && e.Message.Contains("DNS Server", StringComparison.OrdinalIgnoreCase) && 
                (e.Message.Contains("zone", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("Record", StringComparison.OrdinalIgnoreCase)))).ToList();

            foreach (var e in dnsEvents)
            {
                string findingType = e.EventId switch
                {
                    257 => "Creare Înregistrare DNS",
                    258 => "Modificare Înregistrare DNS",
                    259 => "Ștergere Înregistrare DNS",
                    _ => (e.Message != null && e.Message.Contains("deleted", StringComparison.OrdinalIgnoreCase)) ? "Ștergere Înregistrare DNS" : "Modificare Înregistrare DNS"
                };

                findings.Add(new DnsAuditFinding
                {
                    FindingType = findingType,
                    Severity = "Medium",
                    RecordName = ExtractDnsQuery(e.Message),
                    ZoneName = ExtractZone(e.Message) ?? "domain.local",
                    MitreTechniqueId = "T1071.004",
                    Description = $"Înregistrată operațiune DNS '{findingType}' pentru resursa '{ExtractDnsQuery(e.Message)}'.",
                    RemediationActionRo = "Verificați conformitatea intrării DNS cu politicile de zonă și autorizarea administratorului.",
                    Timestamp = e.TimeCreated
                });
            }

            return findings;
        }

        private static string ExtractDnsQuery(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return "dns.record.local";
            var match = Regex.Match(msg, @"(?:QueryName|RecordName|Record Name|Node):\s*([^\r\n\t,;]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var val = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(val) && !val.Equals("-")) return val;
            }
            return "dns.record.local";
        }

        private static string? ExtractZone(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return null;
            var match = Regex.Match(msg, @"(?:ZoneName|Zone Name|Zone):\s*([^\r\n\t,;]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }
}
