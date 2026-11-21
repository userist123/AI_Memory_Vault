using System;

namespace LogAnalyzer.Core.Models
{
    public class ParsedEvent
    {
        public int EventId { get; set; }
        public DateTime TimeCreated { get; set; }
        public string? ProviderName { get; set; }
        public string? Level { get; set; }
        public string? MachineName { get; set; }
        public string? Message { get; set; }
        public string? XmlData { get; set; }
        
        public string? OfficialDescription { get; set; }
        public string? TacticalExample { get; set; }
        public string? ReferenceUrl { get; set; }
        public string? PotentialCriticality { get; set; }

        /// <summary>A grid row's name for UI Automation: time, event id, source and host, never the type name.</summary>
        public override string ToString() => $"{TimeCreated:yyyy-MM-dd HH:mm:ss} · {EventId} · {ProviderName} · {MachineName}";
    }
}