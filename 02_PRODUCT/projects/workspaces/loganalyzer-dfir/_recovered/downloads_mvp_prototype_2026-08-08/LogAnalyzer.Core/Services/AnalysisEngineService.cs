using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class AnalysisEngineService : IAnalysisEngine
    {
        public IEnumerable<DetectedIssue> AnalyzeEvents(IReadOnlyList<ParsedEvent> events, IReadOnlyList<DetectionRule> reguli)
        {
            var issues = new List<DetectedIssue>();

            foreach (var regula in reguli)
            {
                if (regula.Status != StatusRegula.Activa) continue;
                if (regula.EventIduri.Count == 0) continue;

                try
                {
                    var evenimenteRegula = events.Where(e => regula.EventIduri.Contains(e.EventId)).ToList();
                    if (evenimenteRegula.Count == 0) continue;

                    if (regula.PragEvenimente <= 1)
                    {
                        foreach (var ev in evenimenteRegula)
                        {
                            issues.Add(ConstruiesteAlerta(regula, new List<ParsedEvent> { ev },
                                $"{regula.Descriere} (host: {ev.MachineName ?? "necunoscut"}, {ev.TimeCreated:dd.MM.yyyy HH:mm}).", ev.MachineName));
                        }
                        continue;
                    }

                    var grupuri = regula.FereastraMinute > 0
                        ? GrupeazaInFerestre(evenimenteRegula, regula.FereastraMinute)
                        : evenimenteRegula.GroupBy(e => e.MachineName ?? "necunoscut")
                                           .Select(g => (Host: g.Key, Evenimente: g.ToList()));

                    foreach (var (host, evenimenteGrup) in grupuri)
                    {
                        if (evenimenteGrup.Count < regula.PragEvenimente) continue;

                        issues.Add(ConstruiesteAlerta(regula, evenimenteGrup.Take(10).ToList(),
                            $"{regula.Descriere} Detectate {evenimenteGrup.Count} evenimente pe {host} " +
                            $"(prag configurat: {regula.PragEvenimente}).", host));
                    }
                }
                catch
                {
                    // O regula defecta nu opreste evaluarea celorlalte reguli.
                }
            }

            return issues;
        }

        private static IEnumerable<(string Host, List<ParsedEvent> Evenimente)> GrupeazaInFerestre(
            List<ParsedEvent> evenimente, int fereastraMinute)
        {
            foreach (var grupHost in evenimente.GroupBy(e => e.MachineName ?? "necunoscut"))
            {
                var sortate = grupHost.OrderBy(e => e.TimeCreated).ToList();
                var fereastraCurenta = new List<ParsedEvent>();

                foreach (var ev in sortate)
                {
                    fereastraCurenta.RemoveAll(x => (ev.TimeCreated - x.TimeCreated).TotalMinutes > fereastraMinute);
                    fereastraCurenta.Add(ev);

                    if (fereastraCurenta.Count >= 2)
                        yield return (grupHost.Key, new List<ParsedEvent>(fereastraCurenta));
                }
            }
        }

        private static DetectedIssue ConstruiesteAlerta(DetectionRule regula, List<ParsedEvent> evenimente, string explicatie, string? host)
        {
            return new DetectedIssue
            {
                Title = regula.Titlu,
                Severity = regula.Severitate.ToString(),
                Explanation = explicatie,
                MitreTacticId = regula.Mitre.TacticaId,
                MitreTacticName = regula.Mitre.TacticaNume,
                MitreTechniqueId = regula.Mitre.TehnicaId,
                MitreTechniqueName = regula.Mitre.TehnicaNume,
                RelatedEvents = evenimente
            };
        }
    }
}
