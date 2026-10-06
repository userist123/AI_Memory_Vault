using System.Collections.Generic;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Interfaces
{
    public interface ILogIntegrityService
    {
        LogIntegrityResult Analizeaza(string numeFisier, IReadOnlyList<ParsedEvent> evenimenteDinFisier);
    }
}
