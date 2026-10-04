using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    /// <summary>
    /// Read-only ingestion boundary for Memory Vault security events.
    /// It validates the metadata-only contract before exposing events to the UI.
    /// It does not authorize tools, execute actions, or mutate evidence.
    /// </summary>
    public sealed class SecurityEventIngestionService
    {
        private static readonly HashSet<string> TrustStates =
            new(StringComparer.OrdinalIgnoreCase) { "UNTRUSTED", "REVIEW", "TRUSTED", "BLOCKED" };

        private static readonly Regex Sha256 =
            new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> MetadataKeys =
            new(StringComparer.Ordinal)
            {
                "source_url", "repository", "path", "commit",
                "verification_type", "confidence", "evidence_ref"
            };

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<IReadOnlyList<SecurityEventRecord>> ReadAsync(
            string jsonlPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jsonlPath))
                throw new ArgumentException("Event stream path is required.", nameof(jsonlPath));

            var fullPath = Path.GetFullPath(jsonlPath);
            if (!File.Exists(fullPath))
                return Array.Empty<SecurityEventRecord>();

            var records = new List<SecurityEventRecord>();

            await using var stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 64 * 1024, useAsync: true);
            using var reader = new StreamReader(stream);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var record = JsonSerializer.Deserialize<SecurityEventRecord>(line, _jsonOptions);
                    if (record is not null && Validate(record))
                        records.Add(record);
                }
                catch (JsonException)
                {
                    // Malformed/untrusted lines are ignored; the UI remains read-only.
                }
            }

            return records;
        }

        public TimelineItem ToTimelineItem(SecurityEventRecord record)
        {
            if (!Validate(record))
                throw new ArgumentException("Invalid security event record.", nameof(record));

            var severity = record.TrustState?.ToUpperInvariant() switch
            {
                "BLOCKED" => "Critic",
                "REVIEW" => "Avertizare",
                "UNTRUSTED" => "Avertizare",
                _ => "Informativ"
            };

            var decision = record.Tool?.DecisionReason;
            var tool = record.Tool?.Name;
            var detail = string.Join(" | ",
                new[] { record.EventType, tool, decision }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            return new TimelineItem
            {
                Timestamp = record.Timestamp.UtcDateTime,
                Source = "MemoryVault.SecurityBoundary",
                Category = "AI Security",
                Severity = severity,
                UserOrHost = record.Actor,
                Title = detail.Length == 0 ? record.EventType : detail,
                Description = $"CorrelationId={record.CorrelationId}; TrustState={record.TrustState ?? "N/A"}"
            };
        }

        private static bool Validate(SecurityEventRecord record)
        {
            if (record.SchemaVersion != 1 ||
                record.Timestamp == default ||
                string.IsNullOrWhiteSpace(record.EventType) ||
                string.IsNullOrWhiteSpace(record.Source) ||
                string.IsNullOrWhiteSpace(record.Actor) ||
                string.IsNullOrWhiteSpace(record.CorrelationId))
                return false;

            if (record.TrustState is not null && !TrustStates.Contains(record.TrustState))
                return false;

            if (record.Artifact?.Sha256 is not null && !Sha256.IsMatch(record.Artifact.Sha256))
                return false;

            if (record.Metadata.Keys.Any(key => !MetadataKeys.Contains(key)))
                return false;

            return true;
        }
    }
}
