using System.Globalization;
using System.Text;

namespace LogAnalyzer.Dfir.Policy;

/// <summary>Result of an import: the policy (in native form, with its own hash), its YAML text and everything that was not imported.</summary>
public sealed record ImportReport(PolicyDocument Policy, string Yaml, IReadOnlyList<string> Unsupported, IReadOnlyList<string> Metadata);

/// <summary>
/// Converts external policy formats into the native policy. Nothing is invented: each control carries the exact key, value and
/// data of its source line; what the engine cannot read or apply (user rights, binary values, legacy audit categories,
/// file/registry ACLs…) is listed in <see cref="ImportReport.Unsupported"/> with its source, never dropped silently.
/// </summary>
public static class PolicyImport
{
    /// <summary>[System Access] names the engine reads (NetUserModalsGet levels 0 and 3).</summary>
    public static readonly string[] SecpolNames =
        ["MinimumPasswordLength", "MaximumPasswordAge", "MinimumPasswordAge", "PasswordHistorySize", "LockoutBadCount", "LockoutDuration", "ResetLockoutCount"];

    private static readonly string[] MetadataSections = ["Unicode", "Version"];

    private sealed class Builder(string id, string version, string title, string author, string description)
    {
        private readonly StringBuilder _controls = new();
        public readonly List<string> Unsupported = [];
        public readonly List<string> Metadata = [];
        private int _n;

        public void Registry(string scope, string hive, string key, string name, string valueType, string desired, string source, bool absent = false)
        {
            _n++;
            _controls.Append($$"""
                  - id: {{Q($"{scope}-{_n:D4}")}}
                    title: {{Q($@"{hive}\{key}\{name}")}}
                    scope: {{Q(scope)}}
                    detection: { type: registry, hive: {{hive}}, key: {{Q(key)}}, value: {{Q(name)}}, value_type: {{valueType}} }
                    desired_state: {{(absent ? "{ absent: 'true' }" : "{ equals: " + Q(desired) + " }")}}
                    remediation: {{(absent ? "manual" : "set")}}
                    verification: reread
                    references: [{{Q(source)}}]

                """);
        }

        public void Audit(string name, string setting, string source)
        {
            _n++;
            _controls.Append($$"""
                  - id: {{Q($"AUD-{_n:D4}")}}
                    title: {{Q("Audit: " + name)}}
                    scope: machine
                    detection: { type: audit, name: {{Q(name)}} }
                    desired_state: {{Q(setting)}}
                    remediation: set
                    verification: reread
                    references: [{{Q(source)}}]

                """);
        }

        public void Secpol(string name, string value, string source)
        {
            _n++;
            _controls.Append($$"""
                  - id: {{Q($"SEC-{_n:D4}")}}
                    title: {{Q("Politică de cont: " + name)}}
                    scope: machine
                    detection: { type: secpol, name: {{Q(name)}} }
                    desired_state: { equals: {{Q(value)}} }
                    remediation: manual
                    verification: reread
                    references: [{{Q(source)}}]

                """);
        }

        public ImportReport Build(string format, string sourcePath)
        {
            var yaml = $$"""
                policy:
                  id: {{Q(id)}}
                  version: {{Q(version)}}
                  title: {{Q(title)}}
                  author: {{Q(author)}}
                  description: {{Q(description)}}
                  target: windows
                controls:

                """ + _controls;
            var parsed = PolicyLoader.Parse(yaml, format, sourcePath);
            return new ImportReport(parsed, yaml, Unsupported, Metadata);
        }
    }

    /// <summary>YAML single-quoted scalar: backslashes stay literal, a quote is doubled.</summary>
    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    public static ImportReport FromGpoBackup(GpoBackup g, string version, string sourcePath = "")
    {
        var name = g.DisplayName.Length > 0 ? g.DisplayName : "GPO";
        var b = new Builder($"GPO-{(g.Guid.Length > 0 ? g.Guid.Trim('{', '}') : name)}", version, name, $"GPO:{name}",
            $"Importat din backup-ul GPO „{name}” {g.Guid}.");
        if (g.MachinePol is not null) AddPol(b, RegistryPol.Parse(g.MachinePol), "HKLM", "machine", "Machine/registry.pol");
        if (g.UserPol is not null) AddPol(b, RegistryPol.Parse(g.UserPol), "HKCU", "user", "User/registry.pol");
        if (g.SecurityTemplate is not null) AddInf(b, SecurityTemplate.Parse(g.SecurityTemplate), "GptTmpl.inf");
        if (g.AuditCsv is not null) AddAudit(b, AuditCsv.Parse(g.AuditCsv), "audit.csv");
        return b.Build("gpo-backup", sourcePath);
    }

    public static ImportReport FromRegistryPol(byte[] pol, string hive, string id, string version, string author, string sourcePath = "")
    {
        var b = new Builder(id, version, $"Registry.pol {Path.GetFileName(sourcePath)}", author, "Importat din Registry.pol.");
        AddPol(b, RegistryPol.Parse(pol), hive, hive == "HKCU" ? "user" : "machine", "Registry.pol");
        return b.Build("registry.pol", sourcePath);
    }

    public static ImportReport FromLgpoText(string text, string id, string version, string author, string sourcePath = "")
    {
        var b = new Builder(id, version, $"LGPO {Path.GetFileName(sourcePath)}", author, "Importat din text LGPO.");
        foreach (var (scope, e) in LgpoText.Parse(text))
            AddPol(b, [e], scope == "User" ? "HKCU" : "HKLM", scope == "User" ? "user" : "machine", "LGPO text");
        return b.Build("lgpo-text", sourcePath);
    }

    public static ImportReport FromSecurityTemplate(string inf, string id, string version, string author, string sourcePath = "")
    {
        var b = new Builder(id, version, $"Șablon de securitate {Path.GetFileName(sourcePath)}", author, "Importat din șablon .inf.");
        AddInf(b, SecurityTemplate.Parse(inf), Path.GetFileName(sourcePath));
        return b.Build("inf", sourcePath);
    }

    public static ImportReport FromAuditCsv(string csv, string id, string version, string author, string sourcePath = "")
    {
        var b = new Builder(id, version, $"Audit avansat {Path.GetFileName(sourcePath)}", author, "Importat din audit.csv.");
        AddAudit(b, AuditCsv.Parse(csv), "audit.csv");
        return b.Build("audit-csv", sourcePath);
    }

    private static void AddPol(Builder b, IEnumerable<PolEntry> entries, string hive, string scope, string source)
    {
        foreach (var e in entries)
        {
            var where = $@"{source}: {e.Key}\{e.ValueName}";
            switch (e.Action)
            {
                case "set" when e.Type is PolEntry.RegDword or PolEntry.RegQword or PolEntry.RegSz or PolEntry.RegExpandSz or PolEntry.RegMultiSz:
                    b.Registry(scope, hive, e.Key, e.ValueName, e.TypeName, e.Text, source);
                    break;
                case "delete":
                    b.Registry(scope, hive, e.Key, e.ValueName.StartsWith("**del.", StringComparison.OrdinalIgnoreCase) ? e.ValueName[6..] : e.ValueName, "string", "", source, absent: true);
                    break;
                case "set":
                    b.Unsupported.Add($"{where}: tip {e.TypeName} (valoare {e.Text.Length} caractere) nu este acceptat de motor");
                    break;
                default:
                    b.Unsupported.Add($"{where}: acțiune specială {e.Action} nu este acceptată");
                    break;
            }
        }
    }

    private static void AddInf(Builder b, Dictionary<string, List<(string Key, string Value)>> sections, string source)
    {
        foreach (var (section, items) in sections)
        {
            if (MetadataSections.Contains(section, StringComparer.OrdinalIgnoreCase)) { b.Metadata.Add($"{source} [{section}]: {items.Count} chei de metadate"); continue; }
            switch (section)
            {
                case "System Access":
                    foreach (var (k, v) in items)
                        if (SecpolNames.Contains(k, StringComparer.OrdinalIgnoreCase)) b.Secpol(k, v, $"{source} [System Access]");
                        else b.Unsupported.Add($"{source} [System Access] {k} = {v}: nu este citit de motor (doar NetUserModalsGet nivel 0 și 3)");
                    break;
                case "Registry Values":
                    foreach (var (k, v) in items)
                    {
                        var parts = v.Split(',', 2);
                        var path = k.Replace('/', '\\');
                        int slash = path.LastIndexOf('\\');
                        string? hive = path.StartsWith(@"MACHINE\", StringComparison.OrdinalIgnoreCase) ? "HKLM"
                                     : path.StartsWith(@"USER\", StringComparison.OrdinalIgnoreCase) ? "HKCU" : null;
                        if (hive is null || slash < 0 || parts.Length != 2 || !int.TryParse(parts[0], out var type))
                        { b.Unsupported.Add($"{source} [Registry Values] {k} = {v}: formă necunoscută"); continue; }
                        var key = path[(path.IndexOf('\\') + 1)..slash];
                        var name = path[(slash + 1)..];
                        var data = parts[1].Trim().Trim('"');
                        switch (type)
                        {
                            case PolEntry.RegDword: b.Registry("machine", hive, key, name, "dword", data, $"{source} [Registry Values]"); break;
                            case PolEntry.RegSz: b.Registry("machine", hive, key, name, "string", data, $"{source} [Registry Values]"); break;
                            case PolEntry.RegExpandSz: b.Registry("machine", hive, key, name, "expand_string", data, $"{source} [Registry Values]"); break;
                            case PolEntry.RegMultiSz: b.Registry("machine", hive, key, name, "multi_string", string.Join(" ", data.Split(',').Select(x => x.Trim().Trim('"')).Where(x => x.Length > 0)), $"{source} [Registry Values]"); break;
                            default: b.Unsupported.Add($"{source} [Registry Values] {k}: tipul {type} nu este acceptat"); break;
                        }
                    }
                    break;
                default:
                    b.Unsupported.Add($"{source} [{section}]: {items.Count} setări (secțiunea nu este acceptată de motor: drepturi de utilizator, audit vechi, ACL-uri, servicii)");
                    break;
            }
        }
    }

    private static void AddAudit(Builder b, IEnumerable<AuditCsv.Row> rows, string source)
    {
        foreach (var r in rows)
        {
            var name = AuditSubcategories.ByName.FirstOrDefault(kv => kv.Value == r.Guid).Key;
            if (name is null) { b.Unsupported.Add($"{source}: subcategoria {r.Subcategory} {{{r.Guid}}} nu este în tabelul motorului"); continue; }
            b.Audit(name, AuditSubcategories.FromFlags((uint)r.SettingValue), source);
        }
    }
}
