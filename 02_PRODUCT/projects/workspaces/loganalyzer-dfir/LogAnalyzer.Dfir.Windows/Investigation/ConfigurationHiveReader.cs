using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Planner #11 A5: reads the configuration values of <see cref="ConfigurationAudit"/> from a STORED SOFTWARE or SYSTEM hive (read-only, through <see cref="RawRegistry"/>),
/// so every configuration finding points at a hashed copy in the case. The live registry is not read here: on a live run the hives are the "reg save" copies made by the
/// collectors in the Standard and Full profiles (Quick does not save them, and the audit then reports a gap, never a guess).
/// </summary>
public static class ConfigurationHiveReader
{
    public static string? HiveOf(EvidenceItem item) => item.SourceType switch { "software_hive" => "SOFTWARE", "system_hive" => "SYSTEM", _ => null };

    public static ConfigHive Read(EvidenceItem item, string fullPath)
    {
        var label = HiveOf(item) ?? throw new ArgumentException("not a SOFTWARE or SYSTEM hive", nameof(item));
        string origin = item.TemporalType == TemporalType.CurrentSnapshot ? "" : (item.OriginalPath.Length > 0 ? item.OriginalPath : item.Source);
        try
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Read(new RawRegistry(fs), item.EvidenceId, item.Sha256, label, origin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException or OverflowException or IndexOutOfRangeException)
        {
            return new ConfigHive { Hive = label, EvidenceId = item.EvidenceId, Sha256 = item.Sha256, Origin = origin, Error = $"hive-ul {label} din caz nu a putut fi citit: {ex.Message}" };
        }
    }

    /// <summary>Reads the probes from an open hive. A key or value that does not exist is recorded as absent (not as an error).</summary>
    public static ConfigHive Read(RawRegistry reg, string evidenceId, string sha256, string label, string origin = "")
    {
        string cs = "ControlSet001";
        if (label == "SYSTEM" && reg.OpenKey("Select")?.Value("Current") is { Type: RawValue.RegDword } cur && uint.TryParse(cur.AsText, out var n) && n is >= 1 and <= 999)
            cs = $"ControlSet{n:000}";
        var h = new ConfigHive { Hive = label, EvidenceId = evidenceId, Sha256 = sha256, Origin = origin, ControlSet = cs };
        if (label == "SYSTEM" && reg.OpenKey(cs) is null)
            return new ConfigHive { Hive = label, EvidenceId = evidenceId, Sha256 = sha256, Origin = origin, ControlSet = cs, Error = $"hive-ul SYSTEM nu conține {cs}" };
        foreach (var p in ConfigurationAudit.Probes(label, cs))
        {
            var key = reg.OpenKey(p.Key);
            var v = key?.Value(p.Name);
            // a value that is not text or a number (binary, empty) is passed as its text ("" when it has none): the rule turns it into a gap
            h.Values[p.Locator] = new ConfigValue(v is null ? null : v.AsText, key is null ? null : new DateTimeOffset(DateTime.SpecifyKind(key.LastWriteUtc, DateTimeKind.Utc)));
        }
        foreach (var lk in ConfigurationAudit.ListKeys(label))
        {
            var key = reg.OpenKey(lk);
            h.Lists[label + "\\" + lk] = new ConfigNames(key?.Values.Select(x => x.Name).Where(x => x.Length > 0).ToList(),
                key is null ? null : new DateTimeOffset(DateTime.SpecifyKind(key.LastWriteUtc, DateTimeKind.Utc)));
        }
        return h;
    }
}
