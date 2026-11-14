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
            "critic" or "critical" or "high" => "#A3201C" /* Registru theme token Critical */,
            "avertizare" or "warning" or "medium" => "#7A5D00" /* Registru theme token Medium */,
            _ => "#2E6638" /* Registru theme token Success */
        };
        public string DotColor => SeverityColor;
    }
}