using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class LogIntegrityService : ILogIntegrityService
    {
        private static readonly HashSet<int> EventIduriStergere = new HashSet<int> { 1102, 104 };
        private static readonly HashSet<int> EventIduriOprirePornire = new HashSet<int> { 1100, 1101, 6005, 6006, 6008 };
        private const long PragGapSuspect = 500;

        public LogIntegrityResult Analizeaza(string numeFisier, IReadOnlyList<ParsedEvent> evenimenteDinFisier)
        {
            var rezultat = new LogIntegrityResult { NumeFisier = numeFisier };
            if (evenimenteDinFisier.Count == 0) return rezultat;

            var sortate = evenimenteDinFisier.OrderBy(e => e.EventRecordId).ToList();

            var stergeri = sortate.Count(e => EventIduriStergere.Contains(e.EventId));
            if (stergeri > 0)
                rezultat.Motive.Add($"Detectat(e) {stergeri} eveniment(e) de stergere jurnal (Event ID 1102/104).");

            int gapuriSuspecte = 0;
            for (int i = 1; i < sortate.Count; i++)
            {
                long diferenta = sortate[i].EventRecordId - sortate[i - 1].EventRecordId;
                if (diferenta > PragGapSuspect)
                {
                    gapuriSuspecte++;
                    rezultat.Motive.Add(
                        $"Salt suspect de {diferenta} intre RecordID {sortate[i - 1].EventRecordId} " +
                        $"({sortate[i - 1].TimeCreated:dd.MM HH:mm}) si {sortate[i].EventRecordId} ({sortate[i].TimeCreated:dd.MM HH:mm}).");
                }
            }

            int opriri = sortate.Count(e => EventIduriOprirePornire.Contains(e.EventId));
            if (opriri > 2)
                rezultat.Motive.Add($"Detectate {opriri} evenimente de oprire/pornire logging.");

            int scor = stergeri * 3 + gapuriSuspecte * 2 + (opriri > 2 ? 1 : 0);
            rezultat.Status = scor >= 5 ? StatusIntegritate.PuternicSuspect
                             : scor >= 2 ? StatusIntegritate.PosibilAlterat
                             : StatusIntegritate.Normal;

            return rezultat;
        }
    }
}
