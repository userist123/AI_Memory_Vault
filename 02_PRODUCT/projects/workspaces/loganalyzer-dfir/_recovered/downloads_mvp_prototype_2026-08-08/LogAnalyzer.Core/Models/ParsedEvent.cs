using System;

namespace LogAnalyzer.Core.Models
{
    public class ParsedEvent
    {
        public DateTime TimeCreated { get; set; }
        public int EventId { get; set; }
        public string Level { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string? MachineName { get; set; }
        public string? Message { get; set; }
        public long EventRecordId { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
    }
}
