using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class KerberosAdAttackEngine
    {
        private static readonly HashSet<string> DcSyncGuids = new(StringComparer.OrdinalIgnoreCase)
        {
            "1131f6aa-9c07-11d1-f79f-00c04fc2dcd2", // DS-Replication-Get-Changes-All
            "1131f6ad-9c07-11d1-f79f-00c04fc2dcd2", // DS-Replication-Get-Changes
            "89e4b325-9444-11d1-ae62-00c04fc2dcd2"  // DS-Replication-Get-Changes-In-Filtered-Set
        };

        public AdAuditSummary GetSummary(IEnumerable<ParsedEvent> events)
        {
            var summary = new AdAuditSummary();
            if (events == null) return summary;

            var list = events.ToList();
            summary.TotalAdEventsAnalyzed = list.Count;
            summary.PrivilegedGroupChanges = list.Count(e => (e.EventId == 4728 || e.EventId == 4732 || e.EventId == 4756) && e.Message != null && (e.Message.Contains("Domain Admins", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("Enterprise Admins", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("Schema Admins", StringComparison.OrdinalIgnoreCase)));
            summary.GpoPolicyChanges = list.Count(e => e.EventId == 5136 || e.EventId == 5137 || e.EventId == 5141 || (e.Message != null && e.Message.Contains("groupPolicyContainer", StringComparison.OrdinalIgnoreCase)));
            summary.KerberosAttacksDetected = Analyze(list).Count;

            return summary;
        }

        public AdAuditSummary GetAuditSummary(IEnumerable<ParsedEvent> events) => GetSummary(events);

        public List<KerberosAdFinding> Analyze(IEnumerable<ParsedEvent> events)
        {
            var findings = new List<KerberosAdFinding>();
            if (events == null) return findings;

            var list = events.ToList();

            // 1. Kerberoasting (T1558.003)
            var kerbEvents = list.Where(e => e.EventId == 4769 && e.Message != null &&
                (e.Message.Contains("0x17", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("Ticket Options: 0x40810010", StringComparison.OrdinalIgnoreCase)) &&
                !IsMachineOrKrbtgtAccount(e.Message)).ToList();

            foreach (var g in kerbEvents.GroupBy(e => ExtractServiceAccount(e.Message)))
            {
                var first = g.Min(e => e.TimeCreated);
                var last = g.Max(e => e.TimeCreated);
                findings.Add(new KerberosAdFinding
                {
                    Category = "Credential Access",
                    AttackType = "Kerberoasting (TGS Request RC4-HMAC)",
                    Severity = "High",
                    TargetAccount = $"SPN / Cont Serviciu: {g.Key}",
                    MitreTechniqueId = "T1558.003",
                    Description = $"Detectate {g.Count()} cereri TGS cu criptare slabă RC4 (0x17) pentru '{g.Key}' (interval {first:HH:mm:ss} - {last:HH:mm:ss}). Risc de offline password cracking.",
                    Timestamp = last
                });
            }

            // 2. AS-REP Roasting (T1558.004)
            var asRepEvents = list.Where(e => e.EventId == 4768 && e.Message != null &&
                (e.Message.Contains("Pre-Authentication Type: 0", StringComparison.OrdinalIgnoreCase) || 
                 e.Message.Contains("Pre-Authentication Type:\t0", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("PreAuthType: 0", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("DONT_REQ_PREAUTH", StringComparison.OrdinalIgnoreCase)) &&
                !IsMachineOrKrbtgtAccount(e.Message)).ToList();

            foreach (var g in asRepEvents.GroupBy(e => ExtractTargetUser(e.Message)))
            {
                var last = g.Max(e => e.TimeCreated);
                findings.Add(new KerberosAdFinding
                {
                    Category = "Credential Access",
                    AttackType = "AS-REP Roasting (Pre-Authentication Disabled)",
                    Severity = "High",
                    TargetAccount = $"Cont Utilizator: {g.Key}",
                    MitreTechniqueId = "T1558.004",
                    Description = $"Cerere TGT fără pre-autentificare Kerberos pentru utilizatorul '{g.Key}'. Permite extragerea hash-ului AS-REP pentru spargere offline.",
                    Timestamp = last
                });
            }

            // 3. DCSync (T1003.006)
            var dcsyncEvents = list.Where(e => e.EventId == 4662 && e.Message != null &&
                DcSyncGuids.Any(guid => e.Message.Contains(guid, StringComparison.OrdinalIgnoreCase)) &&
                !IsAuthorizedDomainController(e.Message)).ToList();

            foreach (var g in dcsyncEvents.GroupBy(e => ExtractTargetUser(e.Message)))
            {
                var last = g.Max(e => e.TimeCreated);
                findings.Add(new KerberosAdFinding
                {
                    Category = "Credential Access",
                    AttackType = "DCSync Attack (DS-Replication Abuse)",
                    Severity = "Critical",
                    TargetAccount = $"Apelant Neautorizat: {g.Key}",
                    MitreTechniqueId = "T1003.006",
                    Description = $"Apel neautorizat al drepturilor de replicare director (Get-Changes-All). Tentativă de extragere a bazei de date ntds.dit și hash-urilor KRBTGT.",
                    Timestamp = last
                });
            }

            // 4. DCShadow (T1207)
            var dcShadowEvents = list.Where(e => (e.EventId == 4742 || e.EventId == 5137) && e.Message != null &&
                (e.Message.Contains("E3514235-4B06-11D1-AB04-00C04FC2DCD2", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("GC/", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("nTDSDSA", StringComparison.OrdinalIgnoreCase)) &&
                !IsAuthorizedDomainController(e.Message)).ToList();

            foreach (var g in dcShadowEvents.GroupBy(e => ExtractTargetUser(e.Message)))
            {
                var last = g.Max(e => e.TimeCreated);
                findings.Add(new KerberosAdFinding
                {
                    Category = "Defense Evasion / Persistence",
                    AttackType = "DCShadow Attack (Rogue Domain Controller Injection)",
                    Severity = "Critical",
                    TargetAccount = $"Cont Suspect: {g.Key}",
                    MitreTechniqueId = "T1207",
                    Description = $"Detectată înregistrare de SPN sau obiect nTDSDSA de Domain Controller de către o entitate neautorizată. Tentativă de injectare modificări AD ocolind SIEM-ul.",
                    Timestamp = last
                });
            }

            return findings;
        }

        public List<KerberosAdFinding> AnalyzeEvents(IEnumerable<ParsedEvent> events) => Analyze(events);

        private static bool IsMachineOrKrbtgtAccount(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            var u = ExtractTargetUser(msg);
            if (string.IsNullOrEmpty(u)) return false;
            if (u.Equals("krbtgt", StringComparison.OrdinalIgnoreCase)) return true;
            if (u.EndsWith("$")) return true;
            return false;
        }

        private static bool IsAuthorizedDomainController(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            var u = ExtractTargetUser(msg);
            if (string.IsNullOrEmpty(u)) return false;
            if (u.EndsWith("$") && (u.StartsWith("DC", StringComparison.OrdinalIgnoreCase) || u.Contains("DC-", StringComparison.OrdinalIgnoreCase) || u.Contains("DOMAIN", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            return false;
        }

        private static string ExtractServiceAccount(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return "UnknownService";
            var match = Regex.Match(msg, @"Service Name:\s*([^\r\n\t]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var val = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(val) && !val.Equals("-")) return val;
            }
            return ExtractTargetUser(msg);
        }

        private static string ExtractTargetUser(string? msg)
        {
            if (string.IsNullOrEmpty(msg)) return "UnknownAccount";
            var match = Regex.Match(msg, @"(?:TargetUserName|Account Name):\s*([^\r\n\t]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var val = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(val) && !val.Equals("-")) return val;
            }
            return "UnknownAccount";
        }
    }
}
