using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class EventParserService : IEventParser
    {
        public IEnumerable<ParsedEvent> ParseEvtxFile(string filePath)
        {
            EventLogReader? reader = null;
            try
            {
                var query = new EventLogQuery(filePath, PathType.FilePath);
                reader = new EventLogReader(query);
            }
            catch (Exception ex)
            {
                LogEroare($"Nu s-a putut deschide fisierul EVTX '{filePath}': {ex.Message}");
                yield break;
            }

            using (reader)
            {
                EventRecord? record;
                while (true)
                {
                    try
                    {
                        record = reader.ReadEvent();
                    }
                    catch (Exception ex)
                    {
                        LogEroare($"Eroare la citirea unei inregistrari din '{filePath}': {ex.Message}");
                        break;
                    }

                    if (record == null) break;

                    ParsedEvent? parsed = null;
                    try
                    {
                        using (record)
                        {
                            parsed = new ParsedEvent
                            {
                                TimeCreated = record.TimeCreated ?? DateTime.MinValue,
                                EventId = record.Id,
                                Level = record.LevelDisplayName ?? "Necunoscut",
                                ProviderName = record.ProviderName ?? "Necunoscut",
                                MachineName = record.MachineName,
                                Message = SigurMesaj(record),
                                EventRecordId = record.RecordId ?? 0,
                                ChannelName = record.LogName ?? string.Empty,
                                SourceFile = filePath
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        LogEroare($"Eveniment ignorat (parsare esuata) in '{filePath}': {ex.Message}");
                        continue;
                    }

                    if (parsed != null)
                        yield return parsed;
                }
            }
        }

        private static string? SigurMesaj(EventRecord record)
        {
            try { return record.FormatDescription(); }
            catch { return "[Mesaj indisponibil - metadate provider lipsa]"; }
        }

        private static void LogEroare(string mesaj)
        {
            try
            {
                var caleLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "erori_ingestie.log");
                File.AppendAllText(caleLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {mesaj}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
