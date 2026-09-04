using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class FileServerAuditEngine
    {
        public List<FileServerAuditFinding> Analyze(IEnumerable<ParsedEvent> events)
        {
            var findings = new List<FileServerAuditFinding>();
            if (events == null) return findings;

            var list = events.ToList();

            var fileEvents = list.Where(e => (e.EventId == 4663 || e.EventId == 5145 || e.EventId == 4656) && e.Message != null && 
                (e.Message.Contains(".locked", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains(".crypto", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("Share Name:", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("Object Name:", StringComparison.OrdinalIgnoreCase))).ToList();

            // 1. Detecție Ransomware
            var lockedEvents = fileEvents.Where(e => e.Message != null && (e.Message.Contains(".locked", StringComparison.OrdinalIgnoreCase) || e.Message.Contains(".crypto", StringComparison.OrdinalIgnoreCase))).ToList();
            if (lockedEvents.Count >= 5)
            {
                var first = lockedEvents.Min(e => e.TimeCreated);
                var last = lockedEvents.Max(e => e.TimeCreated);
                findings.Add(new FileServerAuditFinding
                {
                    ActivityType = "Ransomware Mass Encryption Attack",
                    Severity = "Critical",
                    SharePathOrFileName = ExtractShareOrFile(lockedEvents.FirstOrDefault()?.Message) ?? @"\\FileServer\DataShare\",
                    AccessedBy = ExtractUserOrIp(lockedEvents.FirstOrDefault()?.Message),
                    ServerHost = lockedEvents.FirstOrDefault()?.MachineName ?? "FileServer",
                    MitreTechniqueId = "T1486",
                    Description = $"Identificate {lockedEvents.Count} fișiere redenumite/criptate (.locked). Risc iminent de atac ransomware.",
                    RemediationActionRo = "Deconectați imediat sesiunea SMB a utilizatorului și opriți serviciul Server pe gazdă.",
                    Timestamp = last
                });
            }

            // 2. Detecție Acces Folder Confidențial
            var sensitiveEvents = fileEvents.Where(e => e.Message != null && (e.Message.Contains("Confidential", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("salarii", StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (var se in sensitiveEvents)
            {
                findings.Add(new FileServerAuditFinding
                {
                    ActivityType = "Acces Neautorizat Director Confidențial",
                    Severity = "High",
                    SharePathOrFileName = ExtractShareOrFile(se.Message) ?? @"\\FileServer\Confidential\",
                    AccessedBy = ExtractUserOrIp(se.Message),
                    ServerHost = se.MachineName ?? "FileServer",
                    MitreTechniqueId = "T1039",
                    Description = $"Acces înregistrat pe director sensibil ({ExtractShareOrFile(se.Message)}).",
                    RemediationActionRo = "Verificați permisiunile NTFS și calitatea de membru în grupul de acces.",
                    Timestamp = se.TimeCreated
                });
            }

            return findings;
        }

        private static string ExtractUserOrIp(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return "Utilizator";
            var match = Regex.Match(msg, @"(?:TargetUserName|Account Name|Source IP|Caller):\s*([^\r\n\t,;]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var val = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(val) && !val.Equals("-")) return val;
            }
            return "Sesiune Rețea";
        }

        private static string? ExtractShareOrFile(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return null;
            var match = Regex.Match(msg, @"(?:Share Name|Object Name|File Name):\s*([^\r\n\t,;]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }
}
