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
        public static void ExportToStix21(string filePath, List<DetectedIssue> issues, List<IocItem> iocs, object? sessionHashes = null)
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
                    var indId = $"indicator--{Guid.NewGuid()}";
                    indicatorIds.Add(indId);
                    objects.Add(new
                    {
                        type = "indicator",
                        spec_version = "2.1",
                        id = indId,
                        created = DateTime.UtcNow.ToString("o"),
                        modified = DateTime.UtcNow.ToString("o"),
                        name = issue.Title,
                        description = issue.Explanation,
                        indicator_types = new[] { "malicious-activity" },
                        pattern = $"[process:name = '{issue.MitreTechniqueId}']",
                        pattern_type = "stix"
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
                        pattern = $"[file:hashes.'SHA-256' = '{ioc.Value}']",
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

            var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        public static void ExportToMispJson(string filePath, List<DetectedIssue> issues, List<IocItem> iocs, string operatorName)
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
                        type = ioc.Type == IocType.Hash ? "sha256" : "ip-dst",
                        category = "Payload delivery",
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

            var json = JsonSerializer.Serialize(mispEvent, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json, Encoding.UTF8);
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

            var tempPath = Path.GetTempFileName();
            ExportToStix21(tempPath, dummyIssues, new List<IocItem>());
            var content = File.ReadAllText(tempPath, Encoding.UTF8);
            try { File.Delete(tempPath); } catch { }
            return content;
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

            var tempPath = Path.GetTempFileName();
            ExportToMispJson(tempPath, dummyIssues, new List<IocItem>(), "Forensic Analyst");
            var content = File.ReadAllText(tempPath, Encoding.UTF8);
            try { File.Delete(tempPath); } catch { }
            return content;
        }
    }
}
