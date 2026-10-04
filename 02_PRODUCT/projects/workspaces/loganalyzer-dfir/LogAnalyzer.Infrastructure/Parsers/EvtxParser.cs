using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;

namespace LogAnalyzer.Infrastructure
{
    public class EvtxParser : IEventParser
    {
        /// <summary>Records that could not be read in the last parsed file.</summary>
        public int LastMalformedRecords { get; private set; }

        /// <summary>Set when the last file had corrupt chunks and a repaired copy was parsed.</summary>
        public string LastRepairNote { get; private set; } = "";

        public IEnumerable<ParsedEvent> ParseEvtxFile(string filePath)
        {
            if (!File.Exists(filePath)) yield break;

            // Suprimăm avertismentul CA1416 care indică faptul că librăria e doar pentru Windows
            #pragma warning disable CA1416 
            LastMalformedRecords = 0;
            LastRepairNote = "";
            var damaged = LogAnalyzer.Dfir.IO.EvtxRepair.Inspect(filePath).Where(c => !c.Valid && !c.Problem.StartsWith("chunk gol", StringComparison.Ordinal)).ToList();
            if (damaged.Count > 0)
            {
                // One corrupt chunk makes the Windows API reject the file: parse a repaired copy (original untouched).
                var repaired = Path.Combine(Path.GetTempPath(), "LogAnalyzer", "evtx_repair", Guid.NewGuid().ToString("N")[..8] + "_" + Path.GetFileName(filePath));
                var r = LogAnalyzer.Dfir.IO.EvtxRepair.Repair(filePath, repaired);
                LastRepairNote = $"{damaged.Count} chunk-uri corupte eliminate ({r.KeptChunks}/{r.TotalChunks} păstrate); lipsesc RecordID " +
                                 string.Join(", ", damaged.Take(10).Select(c => $"{c.FirstRecordId}–{c.LastRecordId}"));
                filePath = repaired;
            }
            using var reader = new EventLogReader(filePath, PathType.FilePath);
            int consecutiveErrors = 0;

            while (true)
            {
                // A corrupt record must not end the file: count it and continue with the next one.
                EventRecord? record;
                try { record = reader.ReadEvent(); consecutiveErrors = 0; }
                catch (EventLogException)
                {
                    LastMalformedRecords++;
                    if (++consecutiveErrors > 1000) throw new InvalidDataException($"Prea multe înregistrări ilizibile consecutive în {Path.GetFileName(filePath)}.");
                    continue;
                }
                if (record is null) break;

                using (record)
                {
                    string msg = $"Event ID {record.Id}";
                    try 
                    { 
                        msg = record.FormatDescription() ?? msg; 
                    } 
                    catch { /* Fallback dacă descrierea nu poate fi rezolvată nativ */ }

                    string level = "Info";
                    try { level = record.LevelDisplayName ?? "Info"; } catch { }

                    string xml = string.Empty;
                    try { xml = record.ToXml() ?? string.Empty; } catch { }

                    var ev = new ParsedEvent
                    {
                        EventId = (int)record.Id,
                        TimeCreated = record.TimeCreated ?? default, // missing time stays missing (never "now")
                        ProviderName = record.ProviderName ?? "Windows",
                        Level = level,
                        MachineName = record.MachineName ?? "Local",
                        Message = msg,
                        XmlData = xml
                    };

                    var assessment = ForensicEventKnowledgeService.GetAssessment(ev);
                    ev.OfficialDescription = assessment.ThreatScenarioRo;
                    ev.TacticalExample = assessment.ContainmentPlaybookRo;
                    ev.PotentialCriticality = assessment.MitreTtpRo;

                    yield return ev;
                }
            }
            #pragma warning restore CA1416
        }
    }
}