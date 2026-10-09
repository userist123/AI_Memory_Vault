using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.Dfir.Windows.Domain;

public sealed class UserInvestigationResult
{
    public required string User { get; init; }
    public DirectoryAccount? Account { get; set; }
    public List<string> PrivilegedGroups { get; } = [];
    public List<ActionEntry> Timeline { get; } = [];
    public Dictionary<string, int> LogonSources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EvidenceGap> Gaps { get; } = [];
    public List<string> Observations { get; } = [];
}
