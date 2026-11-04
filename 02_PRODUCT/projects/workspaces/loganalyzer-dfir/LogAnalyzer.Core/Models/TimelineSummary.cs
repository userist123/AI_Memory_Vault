using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LogAnalyzer.Core.Models
{
    /// <summary>
    /// The side panel of the timeline page, computed only from the events shown (the current page): first and last event time and the highest severity
    /// with its count. With no events every value says so; nothing is ever a fixed sample (the panel once printed "CRITICAL (3 ALERTS)" and an
    /// "EDR Shield" sentence whatever the data).
    /// </summary>
    public sealed record TimelineSummary(string First, string Last, string Peak, bool PeakIsCritical, int Count)
    {
        public const string NoEvents = "niciun eveniment încărcat";

        private static int Rank(string? severity) => (severity ?? "").Trim().ToLowerInvariant() switch
        {
            "critic" or "critical" => 4,
            "high" or "ridicat" or "ridicată" or "mare" => 3,
            "avertizare" or "warning" or "medium" or "mediu" or "medie" => 2,
            "low" or "scăzut" or "scazut" or "redus" => 1,
            _ => 0,
        };

        private static string Label(int rank) => rank switch
        {
            4 => "CRITIC", 3 => "RIDICAT", 2 => "MEDIU", 1 => "SCĂZUT", _ => "INFORMATIV",
        };

        public static TimelineSummary From(IEnumerable<TimelineItem> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return new TimelineSummary(NoEvents, NoEvents, NoEvents, false, 0);
            string T(DateTime t) => t.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) + (t.Kind == DateTimeKind.Utc ? " UTC" : "");
            int peak = list.Max(i => Rank(i.Severity));
            int n = list.Count(i => Rank(i.Severity) == peak);
            return new TimelineSummary(T(list.Min(i => i.Timestamp)), T(list.Max(i => i.Timestamp)),
                $"{Label(peak)} ({n} {(n == 1 ? "eveniment" : "evenimente")})", peak == 4, list.Count);
        }
    }
}
