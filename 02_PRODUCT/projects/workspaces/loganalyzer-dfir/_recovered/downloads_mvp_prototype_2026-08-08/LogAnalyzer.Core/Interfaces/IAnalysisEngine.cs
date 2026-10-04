using System.Collections.Generic;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Interfaces
{
    public interface IAnalysisEngine
    {
        IEnumerable<DetectedIssue> AnalyzeEvents(IReadOnlyList<ParsedEvent> events, IReadOnlyList<DetectionRule> reguli);
    }
}
