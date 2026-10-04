using System.Collections.Generic;

namespace LogAnalyzer.Core.Models
{
    public enum IocStatus { Necunoscut, Benign, Suspect, Malitios }
    public enum IocType { IPv4, IPv6, Domeniu, HashMd5, HashSha1, HashSha256, CaleFisier, CheieRegistry }

    public class IocItem
    {
        public IocType Type { get; set; }
        public string Value { get; set; } = string.Empty;
        public IocStatus Status { get; set; } = IocStatus.Necunoscut;
        public int Occurrences { get; set; } = 1;
        public List<string> RelatedEventIds { get; } = new List<string>();
    }
}
