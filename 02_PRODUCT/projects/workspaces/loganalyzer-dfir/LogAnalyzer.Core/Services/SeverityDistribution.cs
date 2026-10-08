using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    /// <summary>
    /// Real severity counts of the loaded alerts, shown on the dashboard instead of fixed sample percentages.
    /// Synthetic test alerts (<see cref="DetectedIssue.IsTestAlert"/>) are never counted. A severity this class does not
    /// know is counted in <see cref="Other"/> and in <see cref="Total"/>, never silently dropped or rounded into Low.
    /// </summary>
    public sealed record SeverityDistribution(int Critical, int High, int Medium, int LowOrInfo, int Other, int TestAlertsExcluded)
    {
        public int Total => Critical + High + Medium + LowOrInfo + Other;

        public static SeverityDistribution Count(IEnumerable<DetectedIssue>? issues)
        {
            int critical = 0, high = 0, medium = 0, low = 0, other = 0, tests = 0;
            foreach (var issue in issues ?? Enumerable.Empty<DetectedIssue>())
            {
                if (issue.IsTestAlert) { tests++; continue; }
                var s = (issue.Severity ?? string.Empty).Trim();
                if (s.Equals("Critical", StringComparison.OrdinalIgnoreCase)) critical++;
                else if (s.Equals("High", StringComparison.OrdinalIgnoreCase)) high++;
                else if (s.Equals("Medium", StringComparison.OrdinalIgnoreCase)) medium++;
                else if (s.Equals("Low", StringComparison.OrdinalIgnoreCase) || s.StartsWith("Info", StringComparison.OrdinalIgnoreCase)) low++;
                else other++;
            }
            return new SeverityDistribution(critical, high, medium, low, other, tests);
        }
    }
}
