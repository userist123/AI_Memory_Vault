using System.Collections.Generic;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class IocExtractionService : IIocExtractionService
    {
        private static readonly Regex Ipv4 = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
        private static readonly Regex Sha256 = new Regex(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);
        private static readonly Regex Sha1 = new Regex(@"\b[a-fA-F0-9]{40}\b", RegexOptions.Compiled);
        private static readonly Regex Md5 = new Regex(@"\b[a-fA-F0-9]{32}\b", RegexOptions.Compiled);
        private static readonly Regex CaleFisier = new Regex(@"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]+", RegexOptions.Compiled);
        private static readonly Regex CheieRegistry = new Regex(@"(HKEY_[A-Z_]+|HKLM|HKCU)\\[^\r\n]+", RegexOptions.Compiled);
        private static readonly Regex Fqdn = new Regex(@"\b(?=.{4,253}\b)(?:[a-zA-Z0-9-]{1,63}\.){1,}[a-zA-Z]{2,63}\b", RegexOptions.Compiled);

        public Dictionary<string, IocItem> Extrage(IEnumerable<(string SursaId, string TextBrut)> intrari)
        {
            var rezultate = new Dictionary<string, IocItem>(System.StringComparer.OrdinalIgnoreCase);

            void Adauga(string valoare, IocType tip, string sursaId)
            {
                if (!rezultate.TryGetValue(valoare, out var item))
                {
                    item = new IocItem { Value = valoare, Type = tip };
                    rezultate[valoare] = item;
                }
                else
                {
                    item.Occurrences++;
                }
                if (!item.RelatedEventIds.Contains(sursaId))
                    item.RelatedEventIds.Add(sursaId);
            }

            foreach (var (sursaId, text) in intrari)
            {
                if (string.IsNullOrEmpty(text)) continue;

                foreach (Match m in Sha256.Matches(text)) Adauga(m.Value, IocType.HashSha256, sursaId);
                foreach (Match m in Sha1.Matches(text)) Adauga(m.Value, IocType.HashSha1, sursaId);
                foreach (Match m in Md5.Matches(text)) Adauga(m.Value, IocType.HashMd5, sursaId);
                foreach (Match m in Ipv4.Matches(text)) Adauga(m.Value, IocType.IPv4, sursaId);
                foreach (Match m in CheieRegistry.Matches(text)) Adauga(m.Value, IocType.CheieRegistry, sursaId);
                foreach (Match m in CaleFisier.Matches(text)) Adauga(m.Value, IocType.CaleFisier, sursaId);
                foreach (Match m in Fqdn.Matches(text))
                    if (!Ipv4.IsMatch(m.Value)) Adauga(m.Value, IocType.Domeniu, sursaId);
            }

            return rezultate;
        }
    }
}
