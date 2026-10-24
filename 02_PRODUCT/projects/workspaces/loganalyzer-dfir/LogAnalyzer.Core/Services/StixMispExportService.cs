using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class StixMispExportService
    {
        /// <summary>STIX 2.1 pattern per IOC type (audit 2026-10-11: every IOC used to be exported as a SHA-256 file hash).</summary>
        public static string StixPattern(IocItem ioc)
        {
            var v = (ioc.Value ?? "").Trim().Replace("\\", "\\\\").Replace("'", "\\'");
            return ioc.Type switch
            {
                IocType.IPv4 => $"[ipv4-addr:value = '{v}']",
                IocType.IPv6 => $"[ipv6-addr:value = '{v}']",
                IocType.Domain => $"[domain-name:value = '{v}']",
                IocType.URL => $"[url:value = '{v}']",
                _ => v.Length switch { 32 => $"[file:hashes.MD5 = '{v}']", 40 => $"[file:hashes.'SHA-1' = '{v}']", 128 => $"[file:hashes.'SHA-512' = '{v}']", _ => $"[file:hashes.'SHA-256' = '{v}']" },
            };
        }

        /// <summary>
        /// MISP attribute type per IOC (audit 2026-10-11: every non-hash IOC used to be exported as "ip-dst", so domains and URLs reached partners
        /// labelled as IP addresses). Hash type follows the hex length; IPv4/IPv6 are destination IPs.
        /// </summary>
        public static string MispType(IocItem ioc) => ioc.Type switch
        {
            IocType.Hash => (ioc.Value ?? "").Trim().Length switch { 32 => "md5", 40 => "sha1", 128 => "sha512", _ => "sha256" },
            IocType.Domain => "domain",
            IocType.URL => "url",
            _ => "ip-dst",
        };

        public static void ExportToStix21(string filePath, List<DetectedIssue> issues, List<IocItem> iocs, object? sessionHashes = null) =>
            File.WriteAllText(filePath, BuildStix21(issues, iocs, sessionHashes), Encoding.UTF8);

        /// <summary>The STIX 2.1 bundle as text (security audit #14: the string-returning exports no longer go through a temporary file).</summary>
        public static string BuildStix21(List<DetectedIssue> issues, List<IocItem> iocs, object? sessionHashes = null)
        {
            var bundleId = $"bundle--{Guid.NewGuid()}";
            var identityId = $"identity--{Guid.NewGuid()}";
            var incidentId = $"incident--{Guid.NewGuid()}";

            var objects = new List<object>
            {
                new
                {
                    type = "identity",
                    spec_version = "2.1",
                    id = identityId,
                    created = DateTime.UtcNow.ToString("o"),
                    modified = DateTime.UtcNow.ToString("o"),
                    name = "LogAnalyzer DFIR Enterprise Security Operations Center",
                    identity_class = "organization"
                },
                new
                {
                    type = "incident",
                    spec_version = "2.1",
                    id = incidentId,
                    created = DateTime.UtcNow.ToString("o"),
                    modified = DateTime.UtcNow.ToString("o"),
                    name = "Security Incident Investigation Bundle",
                    description = "Corelație automată a anomaliilor și indicatorilor de compromitere (IoC)."
                }
            };

            var indicatorIds = new List<string>();

            if (issues != null)
            {
                foreach (var issue in issues)
                {
                    // An alert is not an indicator: it is exported as a note on the incident, with its ATT&CK technique as a reference
                    // (audit 2026-10-11: it used to be an indicator with the pattern "process:name = <technique id>", which matches nothing).
                    objects.Add(new
                    {
                        type = "note",
                        spec_version = "2.1",
                        id = $"note--{Guid.NewGuid()}",
                        created = DateTime.UtcNow.ToString("o"),
                        modified = DateTime.UtcNow.ToString("o"),
                        @abstract = issue.Title,
                        content = issue.Explanation + (string.IsNullOrWhiteSpace(issue.MitreTechniqueId) ? "" : $" (MITRE ATT&CK {issue.MitreTechniqueId})"),
                        object_refs = new[] { incidentId }
                    });
                }
            }

            if (iocs != null)
            {
                foreach (var ioc in iocs)
                {
                    var indId = $"indicator--{Guid.NewGuid()}";
                    indicatorIds.Add(indId);
                    objects.Add(new
                    {
                        type = "indicator",
                        spec_version = "2.1",
                        id = indId,
                        created = DateTime.UtcNow.ToString("o"),
                        modified = DateTime.UtcNow.ToString("o"),
                        name = $"{ioc.Type}: {ioc.Value}",
                        description = $"Indicator IoC tip {ioc.Type}",
                        indicator_types = new[] { "malicious-activity" },
                        pattern = StixPattern(ioc),
                        pattern_type = "stix"
                    });
                }
            }

            // Relationship SRO
            foreach (var indId in indicatorIds)
            {
                objects.Add(new
                {
                    type = "relationship",
                    spec_version = "2.1",
                    id = $"relationship--{Guid.NewGuid()}",
                    created = DateTime.UtcNow.ToString("o"),
                    modified = DateTime.UtcNow.ToString("o"),
                    relationship_type = "indicates",
                    source_ref = indId,
                    target_ref = incidentId
                });
            }

            var bundle = new
            {
                type = "bundle",
                id = bundleId,
                objects = objects
            };

            return JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
        }

        public static void ExportToMispJson(string filePath, List<DetectedIssue> issues, List<IocItem> iocs, string operatorName) =>
            File.WriteAllText(filePath, BuildMispJson(issues, iocs, operatorName), Encoding.UTF8);

        /// <summary>The MISP event as text (no temporary file).</summary>
        public static string BuildMispJson(List<DetectedIssue> issues, List<IocItem> iocs, string operatorName)
        {
            var attributes = new List<object>();

            if (issues != null)
            {
                foreach (var issue in issues)
                {
                    attributes.Add(new
                    {
                        type = "text",
                        category = "Targeting data",
                        value = $"{issue.Title} ({issue.Severity})",
                        comment = issue.Explanation,
                        to_ids = false
                    });
                }
            }

            if (iocs != null)
            {
                foreach (var ioc in iocs)
                {
                    attributes.Add(new
                    {
                        type = MispType(ioc),
                        category = ioc.Type is IocType.Hash ? "Payload delivery" : "Network activity",
                        value = ioc.Value,
                        comment = $"IoC {ioc.Type} - Detectat de {operatorName}",
                        to_ids = true
                    });
                }
            }

            var mispEvent = new
            {
                Event = new
                {
                    info = $"LogAnalyzer Threat Report - Operat de {operatorName}",
                    date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    threat_level_id = "2",
                    analysis = "2",
                    distribution = "0",
                    Attribute = attributes
                }
            };

            return JsonSerializer.Serialize(mispEvent, new JsonSerializerOptions { WriteIndented = true });
        }

        public string ExportToStixJson(
            IEnumerable<KerberosAdFinding> kerbFindings,
            IEnumerable<StandaloneSamFinding> samFindings,
            IEnumerable<UbaAnomalyItem> ubaAnomalies)
        {
            var dummyIssues = (kerbFindings ?? Enumerable.Empty<KerberosAdFinding>())
                .Select(k => new DetectedIssue { Title = k.AttackType, Severity = k.Severity, Explanation = k.Description, MitreTechniqueId = k.MitreTechniqueId })
                .Concat((samFindings ?? Enumerable.Empty<StandaloneSamFinding>()).Select(s => new DetectedIssue { Title = s.FindingType, Severity = s.Severity, Explanation = s.Description, MitreTechniqueId = s.MitreTechniqueId }))
                .ToList();

            return BuildStix21(dummyIssues, new List<IocItem>());
        }

        public string ExportToStix21(
            IEnumerable<KerberosAdFinding> kerbFindings,
            IEnumerable<StandaloneSamFinding> samFindings,
            IEnumerable<UbaAnomalyItem> ubaAnomalies) => ExportToStixJson(kerbFindings, samFindings, ubaAnomalies);

        public string ExportToMispJson(
            IEnumerable<KerberosAdFinding> kerbFindings,
            IEnumerable<StandaloneSamFinding> samFindings,
            IEnumerable<UbaAnomalyItem>? ubaAnomalies = null,
            IEnumerable<FileServerAuditFinding>? fileFindings = null)
        {
            var dummyIssues = (kerbFindings ?? Enumerable.Empty<KerberosAdFinding>())
                .Select(k => new DetectedIssue { Title = k.AttackType, Severity = k.Severity, Explanation = k.Description, MitreTechniqueId = k.MitreTechniqueId })
                .Concat((samFindings ?? Enumerable.Empty<StandaloneSamFinding>()).Select(s => new DetectedIssue { Title = s.FindingType, Severity = s.Severity, Explanation = s.Description, MitreTechniqueId = s.MitreTechniqueId }))
                .ToList();

            return BuildMispJson(dummyIssues, new List<IocItem>(), "Forensic Analyst");
        }
    }
}
