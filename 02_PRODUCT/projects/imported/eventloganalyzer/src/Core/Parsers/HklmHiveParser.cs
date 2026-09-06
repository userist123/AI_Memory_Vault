using EventLogAnalyzer.Core.Models;
using RegistryLib = Registry;

namespace EventLogAnalyzer.Core.Parsers;

/// <summary>
/// Parses an exported HKLM hive (SYSTEM or SOFTWARE, backed up as
/// HKLM-*.hiv) for stability/security-relevant misconfigurations. MVP scope
/// intentionally covers a small, high-signal set of keys rather than a full
/// registry dump.
/// </summary>
public sealed class HklmHiveParser : IArtifactParser<ConfigFinding>
{
    public bool CanParse(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return name.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
               && (filePath.EndsWith(".hiv", StringComparison.OrdinalIgnoreCase)
                   || filePath.EndsWith(".reg", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ConfigFinding> Parse(string filePath)
    {
        // .reg text exports are handled separately (simple line parsing);
        // .hiv binary exports go through the raw hive parser.
        return filePath.EndsWith(".reg", StringComparison.OrdinalIgnoreCase)
            ? ParseRegText(filePath)
            : ParseBinaryHive(filePath);
    }

    private static List<ConfigFinding> ParseBinaryHive(string filePath)
    {
        var findings = new List<ConfigFinding>();
        var hive = new RegistryLib.RegistryHive(filePath) { RecoverDeleted = false };
        hive.ParseHive();

        CheckCriticalServicesStartType(hive, filePath, findings);
        CheckLastKnownGoodMismatch(hive, filePath, findings);

        return findings;
    }

    /// <summary>
    /// Flags critical services (as configured under SYSTEM\CurrentControlSet\Services)
    /// that are set to Disabled (Start=4) when they are commonly expected to be
    /// running - a frequent cause of "service X failed to start" chains in the
    /// event log and a common indicator of tampering.
    /// </summary>
    private static void CheckCriticalServicesStartType(
        RegistryLib.RegistryHive hive, string filePath, List<ConfigFinding> findings)
    {
        string[] criticalServices = { "Dnscache", "LanmanWorkstation", "RpcSs", "EventLog", "Winmgmt" };
        const int serviceDisabled = 4;

        var servicesKey = hive.GetKey(@"ControlSet001\Services") ?? hive.GetKey(@"CurrentControlSet\Services");
        if (servicesKey is null) return;

        foreach (var serviceName in criticalServices)
        {
            var svcKey = servicesKey.SubKeys.FirstOrDefault(
                k => k.KeyName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
            var startValue = svcKey?.Values.FirstOrDefault(v => v.ValueName == "Start");
            if (startValue is null) continue;

            if (int.TryParse(startValue.ValueData, out var start) && start == serviceDisabled)
            {
                findings.Add(new ConfigFinding
                {
                    HiveFile = filePath,
                    KeyPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}",
                    Finding = $"Critical service '{serviceName}' is set to Disabled (Start=4)",
                    Severity = Severity.Suspicious,
                    Explanation = $"'{serviceName}' is normally Automatic or Manual. A Disabled " +
                                   "start type here will produce repeated dependency-failure events " +
                                   "from any service that depends on it, and is a common persistence-" +
                                   "evasion or sabotage indicator worth confirming with the owner."
                });
            }
        }
    }

    private static void CheckLastKnownGoodMismatch(
        RegistryLib.RegistryHive hive, string filePath, List<ConfigFinding> findings)
    {
        var selectKey = hive.GetKey("Select");
        var current = selectKey?.Values.FirstOrDefault(v => v.ValueName == "Current")?.ValueData;
        var lastKnownGood = selectKey?.Values.FirstOrDefault(v => v.ValueName == "LastKnownGood")?.ValueData;

        if (current != null && lastKnownGood != null && current != lastKnownGood)
        {
            findings.Add(new ConfigFinding
            {
                HiveFile = filePath,
                KeyPath = @"SYSTEM\Select",
                Finding = $"Current ControlSet ({current}) differs from LastKnownGood ({lastKnownGood})",
                Severity = Severity.Warning,
                Explanation = "The system did not boot cleanly on its most recent successful boot " +
                               "using this ControlSet, or a driver/service failure triggered a " +
                               "fallback. Worth cross-referencing with boot-time events (EventID 6008, " +
                               "41) in the System log."
            });
        }
    }

    private static List<ConfigFinding> ParseRegText(string filePath)
    {
        // Minimal, dependency-free .reg exporter format reader for the MVP:
        // looks only for lines under [HKEY_LOCAL_MACHINE\...] blocks, enough
        // to catch obviously-disabled critical keys without a full parser.
        var findings = new List<ConfigFinding>();
        var lines = File.ReadAllLines(filePath);
        string currentKey = string.Empty;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentKey = trimmed[1..^1];
                continue;
            }

            if (trimmed.StartsWith("\"Start\"", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains("dword:00000004"))
            {
                findings.Add(new ConfigFinding
                {
                    HiveFile = filePath,
                    KeyPath = currentKey,
                    Finding = "Service key has Start=4 (Disabled)",
                    Severity = Severity.Suspicious,
                    Explanation = "Flagged from .reg text export - cross-check against the binary " +
                                   "hive parse if available for full context."
                });
            }
        }

        return findings;
    }
}
