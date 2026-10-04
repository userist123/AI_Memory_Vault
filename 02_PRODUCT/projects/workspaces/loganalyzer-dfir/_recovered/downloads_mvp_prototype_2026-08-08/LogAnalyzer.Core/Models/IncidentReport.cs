using System;
using System.Collections.Generic;

namespace LogAnalyzer.Core.Models
{
    public class IncidentSummary
    {
        public string TitluIncident { get; set; } = "Incident Securitate";
        public string Rezumat { get; set; } = string.Empty;
        public DateTime GeneratLa { get; set; } = DateTime.Now;
        public string Analist { get; set; } = Environment.UserName;
    }

    public class IncidentReport
    {
        public IncidentSummary Rezumat { get; set; } = new IncidentSummary();
        public List<TimelineItem> EvenimenteCheie { get; set; } = new List<TimelineItem>();
        public List<DetectedIssue> AlerteCheie { get; set; } = new List<DetectedIssue>();
        public List<IocItem> IocuriRelevante { get; set; } = new List<IocItem>();
        public List<LogIntegrityResult> IntegritateFisiere { get; set; } = new List<LogIntegrityResult>();
        public string NotiteAnalist { get; set; } = string.Empty;
    }
}
