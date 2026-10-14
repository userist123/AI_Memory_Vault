using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Infrastructure.Engines
{
    public class YaraRule
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Severity { get; set; } = "High";
        public string MitreId { get; set; } = "T1059";
        public List<string> StringPatterns { get; set; } = new();
        public List<string> RegexPatterns { get; set; } = new();
        public int MatchCount { get; set; }
    }

    public class YaraRuleEngine
    {
        public List<YaraRule> Rules { get; } = new();

        public YaraRuleEngine()
        {
            InitializeDefaultRules();
        }

        private void InitializeDefaultRules()
        {
            Rules.Add(new YaraRule
            {
                Name = "Webshell_PHP_JSP_Generic",
                Description = "Detectează tipare specifice de Web Shell (eval, passthru, base64_decode, Runtime.getRuntime)",
                Severity = "Critical",
                MitreId = "T1505.003",
                StringPatterns = new List<string> { "eval(base64_decode", "passthru($_POST", "shell_exec($_GET", "Runtime.getRuntime().exec(" }
            });

            Rules.Add(new YaraRule
            {
                Name = "Mimikatz_Credential_Dumper_Strings",
                Description = "Detectează comenzi și string-uri asociate utilitarului Mimikatz (sekurlsa, logonpasswords, lsadump)",
                Severity = "Critical",
                MitreId = "T1003.001",
                StringPatterns = new List<string> { "sekurlsa::logonpasswords", "lsadump::sam", "sekurlsa::wdigest", "privilege::debug", "token::elevate" }
            });

            Rules.Add(new YaraRule
            {
                Name = "CobaltStrike_NamedPipe_Default",
                Description = "Detectează tipare de Named Pipes folosite de Cobalt Strike Beacons",
                Severity = "Critical",
                MitreId = "T1071",
                RegexPatterns = new List<string> { @"\\pipe\\msse-[0-9a-f]{4}-server", @"\\pipe\\status_[0-9a-f]{4}", @"\\pipe\\postex_[0-9a-f]{4}" }
            });

            Rules.Add(new YaraRule
            {
                Name = "PowerShell_DownloadCradle_Suspicious",
                Description = "Detectează metode avansate de descărcare și execuție în memorie fără salvare pe disc (IEX WebClient)",
                Severity = "High",
                MitreId = "T1059.001",
                StringPatterns = new List<string> { "DownloadString(", "DownloadData(", "Net.WebClient).DownloadFile", "BitConverter.ToString([System.Security.Cryptography" }
            });

            Rules.Add(new YaraRule
            {
                Name = "Ransomware_ShadowCopy_InhibitRecovery",
                Description = "Detectează comenzi agresive de ștergere a punctelor de restaurare și jurnalelor USN (vssadmin, wbadmin, bcdedit)",
                Severity = "Critical",
                MitreId = "T1490",
                StringPatterns = new List<string> { "bcdedit /set {default} bootstatuspolicy ignoreallfailures", "bcdedit /set {default} recoveryenabled no", "wbadmin delete catalog -quiet" }
            });
        }

        /// <summary>Explicit timeout of every rule pattern (security audit #11): patterns come from the rule list, the text from evidence.</summary>
        public static TimeSpan MatchTimeout => LogAnalyzer.Dfir.RegexDefaults.RuleMatchTimeout;

        public List<DetectedIssue> Evaluate(IEnumerable<ParsedEvent> events)
        {
            var issues = new List<DetectedIssue>();

            foreach (var rule in Rules)
            {
                rule.MatchCount = 0;
            }

            // Compile each rule's patterns once, with the timeout. An invalid pattern or a timeout makes the rule "not evaluated": it is reported,
            // never counted as a match and never silently treated as clean, and it is not run on the remaining events.
            var compiled = new Dictionary<YaraRule, List<Regex>>();
            var disabled = new HashSet<YaraRule>();
            foreach (var rule in Rules)
            {
                var list = new List<Regex>();
                foreach (var rp in rule.RegexPatterns)
                {
                    try { list.Add(new Regex(rp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout)); }
                    catch (ArgumentException ex)
                    {
                        issues.Add(NotEvaluated(rule, $"expresie regulată invalidă ({ex.Message})"));
                        disabled.Add(rule);
                        break;
                    }
                }
                compiled[rule] = list;
            }

            foreach (var ev in events)
            {
                string text = $"{ev.Message} {ev.XmlData}";
                if (string.IsNullOrWhiteSpace(text)) continue;

                foreach (var rule in Rules)
                {
                    if (disabled.Contains(rule)) continue;
                    bool matched = false;

                    foreach (var sp in rule.StringPatterns)
                    {
                        if (text.Contains(sp, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = true;
                            break;
                        }
                    }

                    if (!matched)
                    {
                        try
                        {
                            foreach (var rx in compiled[rule])
                            {
                                if (rx.IsMatch(text))
                                {
                                    matched = true;
                                    break;
                                }
                            }
                        }
                        catch (RegexMatchTimeoutException ex)
                        {
                            issues.Add(NotEvaluated(rule, $"expresia regulată a depășit {ex.MatchTimeout.TotalMilliseconds:0} ms pe evenimentul {ev.EventId}; regula nu a mai fost rulată pe evenimentele rămase", ev));
                            disabled.Add(rule);
                            continue;
                        }
                    }

                    if (matched)
                    {
                        rule.MatchCount++;
                        issues.Add(new DetectedIssue
                        {
                            Title = $"Potrivire Regulă YARA: {rule.Name}",
                            Severity = rule.Severity,
                            Explanation = $"{rule.Description}\n\nSursă: EID {ev.EventId} pe {ev.MachineName} la {ev.TimeCreated:yyyy-MM-dd HH:mm:ss}.\nText extras: {Truncate(text, 250)}",
                            ComplianceTag = $"YARA Signature - {rule.Name}",
                            MitreTechniqueId = rule.MitreId,
                            MitreTacticName = "Threat Detection",
                            Status = AlertStatus.Nouă,
                            RelatedEvents = new List<ParsedEvent> { ev }
                        });
                    }
                }
            }

            return issues;
        }

        private static DetectedIssue NotEvaluated(YaraRule rule, string why, ParsedEvent? ev = null) => new()
        {
            Title = $"Regulă YARA neevaluată: {rule.Name}",
            Severity = "Medium",
            Explanation = $"Regula „{rule.Name}” nu a fost evaluată: {why}. Absența unei potriviri pentru această regulă NU înseamnă că evenimentele sunt curate.",
            ComplianceTag = $"YARA Signature - {rule.Name} (neevaluată)",
            MitreTechniqueId = rule.MitreId,
            MitreTacticName = "Threat Detection",
            Status = AlertStatus.Nouă,
            RelatedEvents = ev is null ? new List<ParsedEvent>() : new List<ParsedEvent> { ev },
        };

        private static string Truncate(string str, int maxLen)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return str.Length <= maxLen ? str : str.Substring(0, maxLen) + "...";
        }
    }
}
