using System;

namespace LogAnalyzer.Core.Models
{
    public class TimelineItem
    {
        /// <summary>What a screen reader announces for the item (a list item is named after its text; the default was the class name, lab 2026-11-10).</summary>
        public override string ToString() => $"{Timestamp:dd.MM.yyyy HH:mm:ss} · {(string.IsNullOrWhiteSpace(Title) ? Category : Title)}{(string.IsNullOrWhiteSpace(Severity) ? "" : " · " + Severity)}";
        public DateTime Timestamp { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Severity { get; set; } = "Informativ";
        public string? MitreTags { get; set; }
        public string UserOrHost { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SeverityColor => Severity?.ToLowerInvariant() switch
        {
            "critic" or "critical" or "high" => "#ef4444",
            "avertizare" or "warning" or "medium" => "#fbbf24",
            _ => "#22ff88"
        };
        public string DotColor => SeverityColor;
    }
}