using System;
using System.Collections.Generic;
using System.Linq;

namespace LogAnalyzer.Core.Services
{
    public class ThreatFeedMatch
    {
        public string IocValue { get; set; } = string.Empty;
        public string ThreatActorOrCampaign { get; set; } = string.Empty;
        public string MalwareFamily { get; set; } = string.Empty;
        public string Confidence { get; set; } = "High";
        public string Description { get; set; } = string.Empty;
    }

    public class OfflineThreatFeedMatcher
    {
        private readonly Dictionary<string, ThreatFeedMatch> _knownHashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ThreatFeedMatch> _knownIps = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ThreatFeedMatch> _knownDomains = new(StringComparer.OrdinalIgnoreCase);

        public OfflineThreatFeedMatcher()
        {
            // Empty by default: no built-in sample indicators (a sample entry would raise false matches).
            // Indicators come from a feed file supplied by the operator (LoadCsv).
        }

        /// <summary>Loads indicators from a CSV file: type(hash|ip|domain),value,actor_or_campaign,family. Lines starting with # are ignored.</summary>
        public int LoadCsv(string path)
        {
            int n = 0;
            foreach (var line in System.IO.File.ReadLines(path))
            {
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var f = line.Split(',');
                if (f.Length < 4) continue;
                switch (f[0].Trim().ToLowerInvariant())
                {
                    case "hash": RegisterHash(f[1].Trim(), f[2].Trim(), f[3].Trim()); n++; break;
                    case "ip": RegisterIp(f[1].Trim(), f[2].Trim(), f[3].Trim()); n++; break;
                    case "domain": RegisterDomain(f[1].Trim(), f[2].Trim(), f[3].Trim()); n++; break;
                }
            }
            return n;
        }

        public void RegisterHash(string hash, string threatActor, string family)
        {
            _knownHashes[hash] = new ThreatFeedMatch { IocValue = hash, ThreatActorOrCampaign = threatActor, MalwareFamily = family, Description = $"Hash identificat în baza de semnături offline ca aparținând familiei [{family}]." };
        }

        public void RegisterIp(string ip, string threatActor, string family)
        {
            _knownIps[ip] = new ThreatFeedMatch { IocValue = ip, ThreatActorOrCampaign = threatActor, MalwareFamily = family, Description = $"Adresă IP identificată în feed-ul offline ca [{threatActor}]." };
        }

        public void RegisterDomain(string domain, string threatActor, string family)
        {
            _knownDomains[domain] = new ThreatFeedMatch { IocValue = domain, ThreatActorOrCampaign = threatActor, MalwareFamily = family, Description = $"Domeniu FQDN identificat în feed-ul offline ca C2 pentru [{threatActor}]." };
        }

        public ThreatFeedMatch? MatchHash(string hash) => _knownHashes.TryGetValue(hash, out var m) ? m : null;
        public ThreatFeedMatch? MatchIp(string ip) => _knownIps.TryGetValue(ip, out var m) ? m : null;
        public ThreatFeedMatch? MatchDomain(string domain) => _knownDomains.TryGetValue(domain, out var m) ? m : null;

        public List<ThreatFeedMatch> MatchAllIocs(IEnumerable<string> hashes, IEnumerable<string> ips, IEnumerable<string> domains)
        {
            var matches = new List<ThreatFeedMatch>();
            if (hashes != null)
            {
                foreach (var h in hashes)
                {
                    var m = MatchHash(h);
                    if (m != null) matches.Add(m);
                }
            }
            if (ips != null)
            {
                foreach (var ip in ips)
                {
                    var m = MatchIp(ip);
                    if (m != null) matches.Add(m);
                }
            }
            if (domains != null)
            {
                foreach (var d in domains)
                {
                    var m = MatchDomain(d);
                    if (m != null) matches.Add(m);
                }
            }
            return matches;
        }
    }
}
