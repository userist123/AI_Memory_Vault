using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    /// <summary>
    /// Exports a Timesketch-compatible CSV. Timesketch requires the columns message, datetime (ISO 8601, UTC)
    /// and timestamp_desc; the remaining columns are extra fields kept for Plaso / Timeline Explorer readers.
    /// Rows without a recorded time cannot be placed on a timeline and are skipped (never stamped with "now").
    /// </summary>
    public class SuperTimelineExportService
    {
        public const string Header =
            "message,datetime,timestamp_desc,source,source_type,event_type,macb,user,host,short,filename,notes";

        public void ExportPlasoCsv(
            string outputPath,
            IEnumerable<ParsedEvent> events,
            IEnumerable<ForensicArtifact> artifacts,
            IEnumerable<RegistryArtifact> registryEntries)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");

            if (events != null)
            {
                foreach (var ev in events)
                {
                    if (ev.TimeCreated == default) continue;
                    AppendRow(sb, ev.Message ?? string.Empty, ev.TimeCreated, "Event Recorded",
                        "EVTX", ev.ProviderName ?? "Windows Event Log", $"EID {ev.EventId}", "M...", "-",
                        ev.MachineName ?? "-", ev.Level ?? "Info", "-", "LogAnalyzer EVTX Engine");
                }
            }

            if (artifacts != null)
            {
                foreach (var art in artifacts)
                {
                    if (art.Timestamp == default) continue;
                    string macb = art.TimestampSemantics switch
                    {
                        TimeSemantics.Created => "..B.",
                        TimeSemantics.LastExecution => ".A..",
                        TimeSemantics.Modified => "M...",
                        _ => "M..."
                    };
                    string desc = art.TimestampSemantics switch
                    {
                        TimeSemantics.Created => "Creation Time",
                        TimeSemantics.LastExecution => "Last Execution Time",
                        TimeSemantics.Modified => "Last Modification Time",
                        _ => "Artifact Time"
                    };
                    AppendRow(sb, art.Summary ?? string.Empty, art.Timestamp, desc,
                        art.ArtifactType ?? "-", art.Strength.ToString(), "Forensic Artifact", macb, "-",
                        art.HostId ?? "-", art.Name ?? string.Empty, Path.GetFileName(art.SourceFilePath ?? string.Empty),
                        "LogAnalyzer Artifact Engine");
                }
            }

            if (registryEntries != null)
            {
                foreach (var reg in registryEntries)
                {
                    if (reg.LastWriteTime is not DateTime regTime || regTime == default) continue;
                    AppendRow(sb, $"{reg.KeyPath} -> {reg.ValueData}", regTime, "Registry Key Last Write",
                        "Registry", reg.Category ?? "Registry Key", "REG_KEY", "M...", "-", "-",
                        reg.ValueName ?? string.Empty, reg.HiveType ?? "Hive", "LogAnalyzer Registry Engine");
                }
            }

            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>ISO 8601 in UTC. Unspecified kind is treated as UTC (the pipeline stores UTC).</summary>
        public static string ToIsoUtc(DateTime t)
        {
            var utc = t.Kind switch
            {
                DateTimeKind.Local => t.ToUniversalTime(),
                DateTimeKind.Utc => t,
                _ => DateTime.SpecifyKind(t, DateTimeKind.Utc)
            };
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'+00:00'", CultureInfo.InvariantCulture);
        }

        private static void AppendRow(StringBuilder sb, string message, DateTime time, string timestampDesc,
            string source, string sourceType, string eventType, string macb, string user, string host,
            string shortDesc, string filename, string notes)
        {
            sb.Append(EscapeCsv(message)).Append(',')
              .Append(ToIsoUtc(time)).Append(',')
              .Append(EscapeCsv(timestampDesc)).Append(',')
              .Append(EscapeCsv(source)).Append(',')
              .Append(EscapeCsv(sourceType)).Append(',')
              .Append(EscapeCsv(eventType)).Append(',')
              .Append(EscapeCsv(macb)).Append(',')
              .Append(EscapeCsv(user)).Append(',')
              .Append(EscapeCsv(host)).Append(',')
              .Append(EscapeCsv(shortDesc)).Append(',')
              .Append(EscapeCsv(filename)).Append(',')
              .Append(EscapeCsv(notes)).Append("\r\n");
        }

        /// <summary>RFC 4180: quote when needed, double the quotes, keep embedded line breaks inside the quotes.</summary>
        public static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            if (field.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
    }
}
