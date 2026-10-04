using System.Collections.Generic;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Interfaces
{
    public interface IRegistryParser
    {
        IEnumerable<RegistryArtifact> ParseNtUserDat(string filePath);
        IEnumerable<RegistryArtifact> ParseRegFile(string filePath);
    }
}
