using System;
using System.Collections.Generic;

namespace LogAnalyzer.Core.Models
{
    /// <summary>
    /// Metadata-only event produced by the Memory Vault security boundary.
    /// LogAnalyzer consumes this as forensic telemetry; it is never an
    /// authorization source.
    /// </summary>
    public sealed class SecurityEventRecord
    {
        public int SchemaVersion { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string Actor { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public string? TrustState { get; init; }
        public string? ScannerVerdict { get; init; }
        public SecurityEventTool? Tool { get; init; }
        public SecurityEventArtifact? Artifact { get; init; }
        public Dictionary<string, string> Metadata { get; init; } = new();
    }

    public sealed class SecurityEventTool
    {
        public string? Name { get; init; }
        public bool? Allowed { get; init; }
        public string? DecisionReason { get; init; }
    }

    public sealed class SecurityEventArtifact
    {
        public string? Sha256 { get; init; }
    }
}
