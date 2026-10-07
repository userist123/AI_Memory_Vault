using System.Collections.Generic;

namespace LogAnalyzer.Core.Interfaces
{
    public interface IIocExtractionService
    {
        Dictionary<string, Models.IocItem> Extrage(IEnumerable<(string SursaId, string TextBrut)> intrari);
    }
}
