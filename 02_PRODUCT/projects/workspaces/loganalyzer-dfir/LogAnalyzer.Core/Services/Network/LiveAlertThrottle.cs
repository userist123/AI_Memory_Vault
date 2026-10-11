using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services.Network
{
    /// <summary>
    /// De-duplicates live alerts (senior review L10): the same alert on the same station for the same account within <see cref="Window"/> does not open a
    /// new alert, sound or toast; its event is added to the alert already shown (up to <see cref="MaxRelated"/>), so nothing is lost and the operator
    /// sees one line per problem instead of one per event. Test alerts are always admitted.
    /// </summary>
    public sealed class LiveAlertThrottle
    {
        public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(10);
        public int MaxRelated { get; init; } = 50;

        /// <summary>Repeats folded into an alert already shown, since start.</summary>
        public int Suppressed { get; private set; }

        private readonly Dictionary<string, (DetectedIssue First, DateTime LastUtc)> _open = new(StringComparer.OrdinalIgnoreCase);

        public bool Admit(DetectedIssue alert, ParsedEvent ev)
        {
            if (alert.IsTestAlert) return true;
            // Password spraying touches a different account on every event: key it by source address, not by account.
            var who = alert.MitreTechniqueId == "T1110.003"
                ? "src:" + (LiveSecurityMonitoringEngine.Field(ev.XmlData, "IpAddress") ?? LiveSecurityMonitoringEngine.Field(ev.XmlData, "WorkstationName") ?? "")
                : LiveSecurityMonitoringEngine.Field(ev.XmlData, "TargetUserName") ?? LiveSecurityMonitoringEngine.Field(ev.XmlData, "SubjectUserName") ?? "";
            var key = $"{alert.Title}|{ev.MachineName}|{who}";
            var at = ev.TimeCreated == default ? DateTime.UtcNow : ev.TimeCreated.ToUniversalTime();
            if (_open.TryGetValue(key, out var open) && at - open.LastUtc < Window && at >= open.LastUtc - Window)
            {
                if (open.First.RelatedEvents.Count < MaxRelated) open.First.RelatedEvents.Add(ev);
                _open[key] = (open.First, at > open.LastUtc ? at : open.LastUtc);
                Suppressed++;
                return false;
            }
            _open[key] = (alert, at);
            if (_open.Count > 1000)
                foreach (var old in _open.Where(p => at - p.Value.LastUtc > Window).Select(p => p.Key).ToList()) _open.Remove(old);
            return true;
        }
    }
}
