using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace LogAnalyzer.Dfir.Policy;

public enum PolicyStatus { Draft, Validated, Tested, Approved, Deployed, Verified, Retired }

/// <summary>What a control reads (and, if remediable, writes): a registry value, an audit subcategory or a service start mode.</summary>
public sealed record SettingRef(string Type, string Hive, string Key, string Name, string ValueType)
{
    public string Display => Type switch
    {
        "registry" => $@"{Hive}\{Key}\{Name}",
        "audit" => $"audit:{Name}",
        "service" => $"service:{Name}",
        _ => $"{Type}:{Name}",
    };
}

/// <summary>Expected state: equals / in / min..max / absent. Values are kept as text exactly as the policy states them.</summary>
public sealed record Expectation(string Operator, IReadOnlyList<string> Values)
{
    public bool IsSatisfiedBy(string? current) => Operator switch
    {
        "absent" => current is null,
        "equals" => current is not null && Values.Count == 1 && Same(current, Values[0]),
        "in" => current is not null && Values.Any(v => Same(current, v)),
        "range" => current is not null && long.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                   && (Values[0].Length == 0 || n >= long.Parse(Values[0], CultureInfo.InvariantCulture))
                   && (Values[1].Length == 0 || n <= long.Parse(Values[1], CultureInfo.InvariantCulture)),
        _ => throw new InvalidOperationException($"Operator necunoscut {Operator}"),
    };

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    public string Display => Operator switch
    {
        "absent" => "valoarea nu există",
        "equals" => Values[0],
        "in" => "una din: " + string.Join(", ", Values),
        "range" => $"între {(Values[0].Length > 0 ? Values[0] : "-∞")} și {(Values[1].Length > 0 ? Values[1] : "+∞")}",
        _ => Operator,
    };

    /// <summary>The single value remediation writes; null when the expectation does not name one (in/range/absent).</summary>
    public string? RemediationValue => Operator == "equals" ? Values[0] : null;
}

public sealed record PolicyControl(
    string Id, string Title, string Description, string Scope, SettingRef Setting, Expectation Desired,
    string Remediation, string Verification, string Severity, IReadOnlyList<string> EvidenceRequired,
    IReadOnlyList<string> Dependencies, IReadOnlyList<string> Exceptions, IReadOnlyList<string> References, string Impact);

/// <summary>Owner-supplied policy (master spec §11). Its SHA-256 over the exact source text binds every lifecycle step.</summary>
public sealed record PolicyDocument(
    string Id, string Version, string Title, string Author, string Created, string Description, string Target,
    IReadOnlyList<string> Compatibility, IReadOnlyList<PolicyControl> Controls, string Sha256, string SourceFormat, string SourcePath)
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>Reads the native format (.lapolicy, YAML or JSON with "policy:" and "controls:"). Unknown keys are rejected.</summary>
public static class PolicyLoader
{
    private static readonly string[] PolicyKeys = ["id", "version", "title", "author", "created", "description", "target", "compatibility"];
    private static readonly string[] ControlKeys = ["id", "title", "description", "scope", "detection", "desired_state", "remediation", "verification",
                                                    "severity", "evidence_required", "dependencies", "exceptions", "references", "impact"];

    public static PolicyDocument LoadFile(string path) => Parse(File.ReadAllText(path), Path.GetExtension(path).TrimStart('.').ToLowerInvariant(), path);

    public static PolicyDocument Parse(string text, string format = "lapolicy", string sourcePath = "")
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root) throw new FormatException("Politica nu este un document YAML/JSON.");
        var p = Map(root, "policy") ?? throw new FormatException("Lipsește secțiunea „policy”.");
        Reject(p, PolicyKeys, "policy");
        var controlsNode = Child(root, "controls") as YamlSequenceNode ?? throw new FormatException("Lipsește lista „controls”.");
        var controls = controlsNode.Children.Select((n, i) => Control(n as YamlMappingNode ?? throw new FormatException($"controls[{i}] nu este o hartă."), i)).ToList();
        return new PolicyDocument(Text(p, "id"), Text(p, "version"), Text(p, "title"), Text(p, "author"), Text(p, "created"), Text(p, "description"),
            Text(p, "target"), List(p, "compatibility"), controls, PolicyDocument.Hash(text), format, sourcePath);
    }

    private static PolicyControl Control(YamlMappingNode c, int index)
    {
        Reject(c, ControlKeys, $"controls[{index}]");
        var det = Map(c, "detection") ?? throw new FormatException($"controls[{index}]: lipsește detection.");
        var setting = new SettingRef(Text(det, "type").ToLowerInvariant(), Text(det, "hive").ToUpperInvariant(), Text(det, "key"),
            Text(det, "value") is { Length: > 0 } v ? v : Text(det, "name"), Text(det, "value_type").ToLowerInvariant());
        var desired = Child(c, "desired_state") switch
        {
            YamlScalarNode s => new Expectation("equals", [s.Value ?? ""]),
            YamlMappingNode m when Child(m, "equals") is YamlScalarNode e => new Expectation("equals", [e.Value ?? ""]),
            YamlMappingNode m when Child(m, "in") is YamlSequenceNode seq => new Expectation("in", seq.Children.Select(x => ((YamlScalarNode)x).Value ?? "").ToList()),
            YamlMappingNode m when Child(m, "absent") is YamlScalarNode { Value: "true" } => new Expectation("absent", []),
            YamlMappingNode m when Child(m, "min") is not null || Child(m, "max") is not null => new Expectation("range", [Text(m, "min"), Text(m, "max")]),
            _ => throw new FormatException($"controls[{index}]: desired_state trebuie să fie o valoare sau equals / in / absent / min-max."),
        };
        return new PolicyControl(Text(c, "id"), Text(c, "title"), Text(c, "description"), Text(c, "scope"), setting, desired,
            Text(c, "remediation"), Text(c, "verification"), Text(c, "severity"), List(c, "evidence_required"), List(c, "dependencies"),
            List(c, "exceptions"), List(c, "references"), Text(c, "impact"));
    }

    private static YamlNode? Child(YamlMappingNode m, string key) => m.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v : null;
    private static YamlMappingNode? Map(YamlMappingNode m, string key) => Child(m, key) as YamlMappingNode;
    private static string Text(YamlMappingNode m, string key) => Child(m, key) is YamlScalarNode s ? s.Value ?? "" : "";
    private static IReadOnlyList<string> List(YamlMappingNode m, string key) => Child(m, key) switch
    {
        YamlSequenceNode seq => seq.Children.Select(x => x is YamlScalarNode s ? s.Value ?? "" : x.ToString()).ToList(),
        YamlScalarNode s when (s.Value ?? "").Length > 0 => [s.Value!],
        _ => [],
    };

    private static void Reject(YamlMappingNode m, string[] allowed, string where)
    {
        var unknown = m.Children.Keys.Select(k => ((YamlScalarNode)k).Value ?? "").Where(k => !allowed.Contains(k)).ToList();
        if (unknown.Count > 0) throw new FormatException($"{where}: chei necunoscute {string.Join(", ", unknown)} (nu se ignoră nimic în tăcere).");
    }
}

/// <summary>Structural checks a policy must pass to leave DRAFT. Every message names the control.</summary>
public static class PolicyValidator
{
    private static readonly string[] ValueTypes = ["dword", "qword", "string", "expand_string", "multi_string"];

    public static List<string> Validate(PolicyDocument p)
    {
        var e = new List<string>();
        foreach (var (field, value) in new[] { ("id", p.Id), ("version", p.Version), ("title", p.Title), ("author", p.Author) })
            if (value.Length == 0) e.Add($"policy.{field} lipsește");
        if (p.Controls.Count == 0) e.Add("politica nu are niciun control");
        foreach (var dup in p.Controls.GroupBy(c => c.Id).Where(g => g.Count() > 1)) e.Add($"control duplicat {dup.Key}");
        var ids = p.Controls.Select(c => c.Id).ToHashSet();
        foreach (var c in p.Controls)
        {
            string W(string m) => $"{(c.Id.Length > 0 ? c.Id : "(fără id)")}: {m}";
            if (c.Id.Length == 0) e.Add(W("lipsește id"));
            if (c.Title.Length == 0) e.Add(W("lipsește title"));
            switch (c.Setting.Type)
            {
                case "registry":
                    if (c.Setting.Hive is not ("HKLM" or "HKCU")) e.Add(W($"hive „{c.Setting.Hive}” (acceptat: HKLM, HKCU)"));
                    if (c.Setting.Key.Length == 0) e.Add(W("lipsește key"));
                    if (!ValueTypes.Contains(c.Setting.ValueType)) e.Add(W($"value_type „{c.Setting.ValueType}” (acceptat: {string.Join(", ", ValueTypes)})"));
                    break;
                case "audit":
                    if (!AuditSubcategories.TryResolve(c.Setting.Name, out _)) e.Add(W($"subcategoria de audit „{c.Setting.Name}” nu este cunoscută"));
                    if (c.Desired.Operator != "equals" || !AuditSubcategories.IsSetting(c.Desired.Values[0])) e.Add(W("desired_state pentru audit: No Auditing / Success / Failure / Success and Failure"));
                    break;
                case "service":
                    if (c.Setting.Name.Length == 0) e.Add(W("lipsește numele serviciului"));
                    break;
                default:
                    e.Add(W($"detection.type „{c.Setting.Type}” nu este acceptat (registry, audit, service)"));
                    break;
            }
            if (c.Remediation is not ("set" or "manual" or "none")) e.Add(W("remediation trebuie să fie set, manual sau none"));
            if (c.Remediation == "set" && c.Desired.RemediationValue is null) e.Add(W("remediation set cere o valoare unică în desired_state (equals)"));
            if (c.Verification is not ("reread" or "manual")) e.Add(W("verification trebuie să fie reread sau manual"));
            foreach (var d in c.Dependencies.Where(d => !ids.Contains(d))) e.Add(W($"dependența {d} nu există în politică"));
        }
        return e;
    }
}

/// <summary>Advanced audit policy subcategories by name and GUID (the GUIDs Windows uses in AuditQuerySystemPolicy).</summary>
public static class AuditSubcategories
{
    public static readonly IReadOnlyDictionary<string, Guid> ByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
    {
        ["Process Creation"] = new("0cce922b-69ae-11d9-bed3-505054503030"),
        ["Process Termination"] = new("0cce922c-69ae-11d9-bed3-505054503030"),
        ["Logon"] = new("0cce9215-69ae-11d9-bed3-505054503030"),
        ["Logoff"] = new("0cce9216-69ae-11d9-bed3-505054503030"),
        ["Account Lockout"] = new("0cce9217-69ae-11d9-bed3-505054503030"),
        ["Special Logon"] = new("0cce921b-69ae-11d9-bed3-505054503030"),
        ["Security State Change"] = new("0cce9210-69ae-11d9-bed3-505054503030"),
        ["Security System Extension"] = new("0cce9211-69ae-11d9-bed3-505054503030"),
        ["System Integrity"] = new("0cce9212-69ae-11d9-bed3-505054503030"),
        ["Audit Policy Change"] = new("0cce922f-69ae-11d9-bed3-505054503030"),
        ["Authentication Policy Change"] = new("0cce9230-69ae-11d9-bed3-505054503030"),
        ["User Account Management"] = new("0cce9235-69ae-11d9-bed3-505054503030"),
        ["Security Group Management"] = new("0cce9237-69ae-11d9-bed3-505054503030"),
        ["Filtering Platform Connection"] = new("0cce9226-69ae-11d9-bed3-505054503030"),
        ["Filtering Platform Packet Drop"] = new("0cce9225-69ae-11d9-bed3-505054503030"),
        ["Removable Storage"] = new("0cce9245-69ae-11d9-bed3-505054503030"),
        ["Plug and Play Events"] = new("0cce9248-69ae-11d9-bed3-505054503030"),
        ["Other Object Access Events"] = new("0cce9227-69ae-11d9-bed3-505054503030"),
        ["Credential Validation"] = new("0cce923f-69ae-11d9-bed3-505054503030"),
        ["Kerberos Authentication Service"] = new("0cce9242-69ae-11d9-bed3-505054503030"),
    };

    public static bool TryResolve(string nameOrGuid, out Guid guid)
    {
        if (Guid.TryParse(nameOrGuid.Trim('{', '}'), out guid)) return true;
        return ByName.TryGetValue(nameOrGuid, out guid);
    }

    public static readonly string[] Settings = ["No Auditing", "Success", "Failure", "Success and Failure"];
    public static bool IsSetting(string s) => Settings.Contains(s, StringComparer.OrdinalIgnoreCase);
    public static string FromFlags(uint flags) => (flags & 3) switch { 1 => "Success", 2 => "Failure", 3 => "Success and Failure", _ => "No Auditing" };
    public static uint ToFlags(string s) => s.ToLowerInvariant() switch { "success" => 1, "failure" => 2, "success and failure" => 3, _ => 4 }; // 4 = POLICY_AUDIT_EVENT_NONE
}
