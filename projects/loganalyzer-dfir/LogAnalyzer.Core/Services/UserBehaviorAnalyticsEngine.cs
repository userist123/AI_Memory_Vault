using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class UserBehaviorAnalyticsEngine
    {
        private static readonly HashSet<string> IgnoredSystemAccounts = new(StringComparer.OrdinalIgnoreCase)
        {
            "SYSTEM", "LOCAL SERVICE", "NETWORK SERVICE", "ANONYMOUS LOGON",
            "DWM-1", "DWM-2", "DWM-3", "UMFD-0", "UMFD-1", "UMFD-2", "-"
        };

        public List<UbaAnomalyItem> Evaluate(IEnumerable<ParsedEvent> events)
        {
            var anomalies = new List<UbaAnomalyItem>();
            if (events == null) return anomalies;

            var list = events.ToList();

            // 1. Detectare autentificare în afara orelor normale (23:00 - 06:00) - Agregat per Utilizator
            var offHoursLogons = list.Where(e => e.EventId == 4624 && (e.TimeCreated.Hour >= 23 || e.TimeCreated.Hour < 6)).ToList();
            var validOffHours = offHoursLogons
                .Select(e => new { Event = e, User = ExtractTargetUser(e.Message) })
                .Where(x => IsRealUser(x.User))
                .GroupBy(x => x.User);

            foreach (var g in validOffHours)
            {
                var first = g.Min(x => x.Event.TimeCreated);
                var last = g.Max(x => x.Event.TimeCreated);
                anomalies.Add(new UbaAnomalyItem
                {
                    Username = g.Key,
                    Workstation = g.FirstOrDefault()?.Event.MachineName ?? "Workstation",
                    AnomalyType = "Autentificare în Afara Orelor Normale (Off-Hours Logon)",
                    Severity = "High",
                    RiskWeight = 75.0,
                    Description = $"Utilizatorul {g.Key} a înregistrat {g.Count()} autentificări nocturne între {first:HH:mm} și {last:HH:mm}. Abatere comportamentală de la programul autorizat.",
                    Timestamp = last
                });
            }

            // 2. Detectare sesiuni concurente pe mai multe stații în interval scurt
            var logonsByUser = list.Where(e => e.EventId == 4624)
                .Select(e => new { Event = e, User = ExtractTargetUser(e.Message) })
                .Where(x => IsRealUser(x.User))
                .GroupBy(x => x.User);

            foreach (var userGroup in logonsByUser)
            {
                var userEvents = userGroup.Select(x => x.Event).OrderBy(e => e.TimeCreated).ToList();
                for (int i = 0; i < userEvents.Count - 1; i++)
                {
                    var e1 = userEvents[i];
                    var e2 = userEvents[i + 1];
                    if (!string.Equals(e1.MachineName, e2.MachineName, StringComparison.OrdinalIgnoreCase) &&
                        (e2.TimeCreated - e1.TimeCreated).TotalMinutes < 15)
                    {
                        anomalies.Add(new UbaAnomalyItem
                        {
                            Username = userGroup.Key,
                            Workstation = $"{e1.MachineName}, {e2.MachineName}",
                            AnomalyType = "Sesiuni Concurente Multi-Stație (Impossible Concurrent Logon)",
                            Severity = "Critical",
                            RiskWeight = 90.0,
                            Description = $"Utilizatorul {userGroup.Key} s-a autentificat simultan pe {e1.MachineName} și {e2.MachineName} într-un interval de {Math.Round((e2.TimeCreated - e1.TimeCreated).TotalMinutes, 1)} minute.",
                            Timestamp = e2.TimeCreated
                        });
                        break;
                    }
                }
            }

            // 3. Detectare rafală de autentificări eșuate urmate de succes imediat (Brute-Force Compromise)
            var failedLogons = list.Where(e => e.EventId == 4625)
                .Select(e => new { Event = e, User = ExtractTargetUser(e.Message) })
                .Where(x => IsRealUser(x.User))
                .GroupBy(x => x.User);

            var successLogons = list.Where(e => e.EventId == 4624)
                .Select(e => new { Event = e, User = ExtractTargetUser(e.Message) })
                .Where(x => IsRealUser(x.User))
                .ToList();

            foreach (var userGroup in failedLogons)
            {
                if (userGroup.Count() >= 3)
                {
                    var lastFail = userGroup.Max(x => x.Event.TimeCreated);
                    var subsequentSuccess = successLogons.FirstOrDefault(s => s.User == userGroup.Key && s.Event.TimeCreated >= lastFail && (s.Event.TimeCreated - lastFail).TotalMinutes <= 5);

                    if (subsequentSuccess != null)
                    {
                        anomalies.Add(new UbaAnomalyItem
                        {
                            Username = userGroup.Key,
                            Workstation = subsequentSuccess.Event.MachineName ?? "Workstation",
                            AnomalyType = "Succes după Rafală Eșuată (Brute-Force Compromise)",
                            Severity = "Critical",
                            RiskWeight = 95.0,
                            Description = $"Contul {userGroup.Key} a înregistrat {userGroup.Count()} eșecuri consecutive urmate de o autentificare reușită la {subsequentSuccess.Event.TimeCreated:HH:mm:ss}.",
                            Timestamp = subsequentSuccess.Event.TimeCreated
                        });
                    }
                }
            }

            return anomalies;
        }

        private static bool IsRealUser(string? user)
        {
            if (string.IsNullOrEmpty(user)) return false;
            if (user.EndsWith("$") || IgnoredSystemAccounts.Contains(user)) return false;
            return true;
        }

        private static string ExtractTargetUser(string? message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;

            // 1. Căutare prioritară în TargetUserName (specific evenimentelor EID 4624/4625)
            var match = Regex.Match(message, @"TargetUserName:\s*([^\r\n\t]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var u = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(u) && !u.Equals("-")) return u;
            }

            // 2. Căutare în secțiunea 'New Logon:' sau 'Target Account:'
            int newLogonIdx = message.IndexOf("New Logon:", StringComparison.OrdinalIgnoreCase);
            if (newLogonIdx >= 0)
            {
                var sub = message.Substring(newLogonIdx);
                var subMatch = Regex.Match(sub, @"Account Name:\s*([^\r\n\t]+)", RegexOptions.IgnoreCase);
                if (subMatch.Success)
                {
                    var u = subMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(u) && !u.Equals("-")) return u;
                }
            }

            // 3. Fallback pe 'Account Name:'
            var generalMatch = Regex.Match(message, @"Account Name:\s*([^\r\n\t]+)", RegexOptions.IgnoreCase);
            if (generalMatch.Success)
            {
                var u = generalMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(u) && !u.Equals("-")) return u;
            }

            return string.Empty;
        }
    }
}
