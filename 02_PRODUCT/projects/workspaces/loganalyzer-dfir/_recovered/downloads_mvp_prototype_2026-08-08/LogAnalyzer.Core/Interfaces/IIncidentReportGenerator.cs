using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Interfaces
{
    public interface IIncidentReportGenerator
    {
        string GenereazaMarkdown(IncidentReport raport);
    }
}
