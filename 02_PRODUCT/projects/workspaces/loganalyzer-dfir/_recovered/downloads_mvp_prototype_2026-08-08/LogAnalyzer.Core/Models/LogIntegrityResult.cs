using System.Collections.Generic;

namespace LogAnalyzer.Core.Models
{
    public enum StatusIntegritate { Normal, PosibilAlterat, PuternicSuspect }

    public class LogIntegrityResult
    {
        public string NumeFisier { get; set; } = string.Empty;
        public StatusIntegritate Status { get; set; } = StatusIntegritate.Normal;
        public List<string> Motive { get; } = new List<string>();
    }
}
