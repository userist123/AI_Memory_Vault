using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.Response.Collection;

namespace LogAnalyzer.Response;

/// <summary>
/// The programs this assembly starts, added to the application's program inventory (A17) by the compositions of the editions that ship it (Unclassified,
/// Network): the firewall tool, and PowerShell for the collection script only (exact command line, build-time script hash). The Standalone edition has
/// neither and registers nothing.
/// </summary>
public static class ResponseProgramInventory
{
    public static void Register()
    {
        CollectorExec.AddToInventory(NetshRunner.NetshPath);
        var powershell = PowerShellAuditCollector.PowerShellPath;
        var script = PowerShellAuditCollector.ScriptPath;
        CollectorExec.AddScriptHost(powershell, PowerShellAuditCollector.BuildScriptBlockSha256,
            commandLine => PowerShellAuditCollector.RunsTheCollectionScript(commandLine, powershell, script));
    }
}
