using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>
/// Planner #11 A5: reads the configuration values of <see cref="ConfigurationAudit"/> from a STORED SOFTWARE or SYSTEM hive (read-only, through <see cref="RawRegistry"/>),
/// so every configuration finding points at a hashed copy in the case. The live registry is not read here: on a live run the hives are the "reg save" copies made by the
/// collectors in the Standard and Full profiles. The Quick profile saves SYSTEM and three SMALL SOFTWARE subkey hives instead of the whole SOFTWARE hive
/// (<see cref="LogAnalyzer.Dfir.Windows.Acquisition.SoftwareSubkeyHivesCollector"/>): <see cref="ReadSoftwareSubkeys"/> reads the same values from them. A subkey that is missing is a gap, never a guess.
/// </summary>
public static class ConfigurationHiveReader
{
    public static string? HiveOf(EvidenceItem item) => item.SourceType switch { "software_hive" => "SOFTWARE", "system_hive" => "SYSTEM", _ => null };

    /// <summary>Evidence type of a SOFTWARE subkey saved with <c>reg save HKLM\SOFTWARE\&lt;subkey&gt;</c>; the subkey becomes the ROOT of the file and <c>OriginalPath</c> is the subkey path.</summary>
    public const string SubkeyHiveType = "software_subkey_hive";
    /// <summary>Evidence type of the JSON marker that proves a SOFTWARE subkey does not exist on the station ({"key":..., "exists":false, "checkedUtc":...}); OriginalPath = the key.</summary>
    public const string AbsentType = "software_subkey_absent";
    /// <summary>Evidence type of the small JSON with the Winlogon autologon facts (Quick profile): AutoAdminLogon, DefaultUserName and whether DefaultPassword is present. The password itself is never in it.</summary>
    public const string WinlogonType = "winlogon_autologon_json";
    public const string SnapshotTimeSource = "Instantaneu la colectare";

    /// <summary>The subkey path below SOFTWARE of a subkey hive item (from OriginalPath, "HKLM\SOFTWARE\" prefix removed), or null when it does not name one.</summary>
    public static string? SubkeyOf(EvidenceItem item)
    {
        if (item.SourceType != SubkeyHiveType && item.SourceType != AbsentType) return null;
        var p = item.OriginalPath.Trim().TrimEnd('\\');
        foreach (var prefix in new[] { @"HKLM\SOFTWARE\", @"HKEY_LOCAL_MACHINE\SOFTWARE\", @"SOFTWARE\" })
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { p = p[prefix.Length..]; return p.Length > 0 ? p : null; }
        return null;
    }

    /// <summary>
    /// The SOFTWARE configuration values from the saved subkey hives (Quick profile), merged into ONE <see cref="ConfigHive"/> labelled SOFTWARE. Every probe and list key keeps its
    /// full-path locator (SOFTWARE\Microsoft\...\EnableLUA, so the "reg query" of the manual verification stays right) and carries the evidence id and SHA-256 of the subkey file it
    /// was read from. A probe whose subkey was not saved (or not readable) is not recorded, so the audit reports it as a gap. An unreadable subkey file is skipped (its probes become gaps).
    /// </summary>
    public static ConfigHive ReadSoftwareSubkeys(IReadOnlyList<(EvidenceItem Item, string FullPath)> subkeyHives, IReadOnlyList<(EvidenceItem Item, string FullPath)>? absentMarkers = null, (EvidenceItem Item, string FullPath)? winlogon = null)
    {
        var first = subkeyHives.Count > 0 ? subkeyHives[0].Item : absentMarkers is { Count: > 0 } ? absentMarkers[0].Item : winlogon!.Value.Item;
        string origin = subkeyHives.All(x => x.Item.TemporalType == TemporalType.CurrentSnapshot) ? "" : (first.OriginalPath.Length > 0 ? first.OriginalPath : first.Source);
        var h = new ConfigHive { Hive = "SOFTWARE", EvidenceId = first.EvidenceId, Sha256 = first.Sha256, Origin = origin };
        // a subkey PROVEN absent (hashed marker, exists=false) has absent values: the rules'' default semantics, not a gap
        foreach (var (item, path) in absentMarkers ?? [])
        {
            if (SubkeyOf(item) is not { } asub || !MarkerSaysAbsent(path)) continue;
            foreach (var p in ConfigurationAudit.Probes("SOFTWARE", "ControlSet001"))
                if (RelOf(p.Key, asub) is not null) h.Values[p.Locator] = new ConfigValue(null, null, item.EvidenceId, item.Sha256);
            foreach (var lk in ConfigurationAudit.ListKeys("SOFTWARE"))
                if (RelOf(lk, asub) is not null) h.Lists["SOFTWARE\\" + lk] = new ConfigNames(null, null, item.EvidenceId, item.Sha256);
        }
        if (winlogon is { } wl) ReadWinlogonJson(h, wl.Item, wl.FullPath);
        foreach (var (item, path) in subkeyHives)
        {
            if (SubkeyOf(item) is not { } sub) continue;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var reg = new RawRegistry(fs);
                // a key of the full SOFTWARE path maps onto the file root: the subkey itself is the root, a deeper key is its remainder
                static string? Rel(string key, string sub) => RelOf(key, sub);
                foreach (var p in ConfigurationAudit.Probes("SOFTWARE", "ControlSet001"))
                {
                    if (Rel(p.Key, sub) is not { } rel) continue;
                    var key = reg.OpenKey(rel);
                    var v = key?.Value(p.Name);
                    h.Values[p.Locator] = new ConfigValue(ConfigurationAudit.ProbeText(p, v?.AsText), key is null ? null : new DateTimeOffset(DateTime.SpecifyKind(key.LastWriteUtc, DateTimeKind.Utc)), item.EvidenceId, item.Sha256);
                }
                foreach (var lk in ConfigurationAudit.ListKeys("SOFTWARE"))
                {
                    if (Rel(lk, sub) is not { } rel) continue;
                    var key = reg.OpenKey(rel);
                    h.Lists["SOFTWARE\\" + lk] = new ConfigNames(key?.Values.Select(x => x.Name).Where(x => x.Length > 0).ToList(),
                        key is null ? null : new DateTimeOffset(DateTime.SpecifyKind(key.LastWriteUtc, DateTimeKind.Utc)), item.EvidenceId, item.Sha256);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException or OverflowException or IndexOutOfRangeException)
            {
                // unreadable subkey file: its values stay unrecorded and the audit reports them as gaps
            }
        }
        return h;
    }

    /// <summary>The Winlogon JSON of the Quick profile gives the three autologon probes; the password is only "(prezentă)" or "". An unreadable or malformed file records nothing (the audit reports the gap).</summary>
    private static void ReadWinlogonJson(ConfigHive h, EvidenceItem item, string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (r.ValueKind != System.Text.Json.JsonValueKind.Object || !r.TryGetProperty("DefaultPasswordPresent", out var pp) || pp.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)) return;
            string Str(string n) => r.TryGetProperty(n, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() ?? "" : "";
            ConfigValue V(string? data) => new(data, null, item.EvidenceId, item.Sha256, SnapshotTimeSource);
            h.Values[ConfigurationAudit.AutoAdminLogon.Locator] = V(Str("AutoAdminLogon") is { Length: > 0 } a ? a : null);
            h.Values[ConfigurationAudit.DefaultUserName.Locator] = V(Str("DefaultUserName") is { Length: > 0 } u ? u : null);
            h.Values[ConfigurationAudit.DefaultPassword.Locator] = V(pp.GetBoolean() ? ConfigurationAudit.SecretPresent : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
    }

    private static string? RelOf(string key, string sub) =>
        key.Equals(sub, StringComparison.OrdinalIgnoreCase) ? "" : key.StartsWith(sub + "\\", StringComparison.OrdinalIgnoreCase) ? key[(sub.Length + 1)..] : null;

    private static bool MarkerSaysAbsent(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("exists", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.False;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return false; }
    }

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
            h.Values[p.Locator] = new ConfigValue(ConfigurationAudit.ProbeText(p, v is null ? null : v.AsText), key is null ? null : new DateTimeOffset(DateTime.SpecifyKind(key.LastWriteUtc, DateTimeKind.Utc)));
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
