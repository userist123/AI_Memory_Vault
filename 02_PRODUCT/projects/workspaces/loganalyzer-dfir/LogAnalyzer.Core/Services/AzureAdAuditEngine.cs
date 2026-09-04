using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class AzureAdAuditEngine
    {
        public List<AzureAdFinding> Analyze(IEnumerable<ParsedEvent> events)
        {
            var findings = new List<AzureAdFinding>();
            if (events == null) return findings;

            var list = events.ToList();

            var pimEvents = list.Where(e => e.Message != null && 
                (e.Message.Contains("Privileged Identity Management", StringComparison.OrdinalIgnoreCase) || 
                 e.Message.Contains("Add member to role completed (PIM activation)", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("Global Administrator", StringComparison.OrdinalIgnoreCase) ||
                 e.EventId == 50074 || e.EventId == 50125)).ToList();

            foreach (var e in pimEvents)
            {
                findings.Add(new AzureAdFinding
                {
                    ActivityType = "Activare Rol Global Administrator (PIM)",
                    Severity = "High",
                    UserPrincipalName = ExtractUpn(e.Message),
                    SourceLocationOrIp = ExtractIp(e.Message) ?? "Entra ID Cloud Session",
                    MitreTechniqueId = "T1078.004",
                    Description = $"Utilizatorul '{ExtractUpn(e.Message)}' a activat rolul Global Administrator prin PIM.",
                    RemediationActionRo = "Verificați justificarea de activare PIM și aprobați exclusiv pe durata justificată a tichetului.",
                    Timestamp = e.TimeCreated
                });
            }

            var riskySignIns = list.Where(e => e.Message != null && 
                (e.Message.Contains("Impossible Travel", StringComparison.OrdinalIgnoreCase) || 
                 e.Message.Contains("Atypical Travel", StringComparison.OrdinalIgnoreCase) || 
                 e.Message.Contains("riskLevel: high", StringComparison.OrdinalIgnoreCase) ||
                 e.EventId == 50053 || e.EventId == 53003)).ToList();

            foreach (var e in riskySignIns)
            {
                findings.Add(new AzureAdFinding
                {
                    ActivityType = "Autentificare cu Risc Ridicat / Impossible Travel",
                    Severity = "Critical",
                    UserPrincipalName = ExtractUpn(e.Message),
                    SourceLocationOrIp = ExtractIp(e.Message) ?? "Multi-Geo IP Source",
                    MitreTechniqueId = "T1078.004",
                    Description = $"Autentificare cloud suspectă pentru contul '{ExtractUpn(e.Message)}' semnalată cu risc de deplasare imposibilă sau locație atipică.",
                    RemediationActionRo = "Revocați toate sesiunile active din Azure Portal și forțați re-înregistrarea MFA.",
                    Timestamp = e.TimeCreated
                });
            }

            return findings;
        }

        private static string ExtractUpn(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return "user@domain.com";
            var match = Regex.Match(msg, @"(?:UserPrincipalName|UPN|Identity|Account Name):\s*([^\r\n\t,;]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var u = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(u) && !u.Equals("-")) return u;
            }
            return "cloud.user@domain.com";
        }

        private static string? ExtractIp(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return null;
            var match = Regex.Match(msg, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
            return match.Success ? match.Value : null;
        }
    }
}
