using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Windows.Policy;

/// <summary>A policy opened in the workbench: the native policy, its library copy and what the import could not take.</summary>
public sealed record OpenedPolicy(PolicyDocument Policy, string LibraryPath, IReadOnlyList<string> Unsupported, IReadOnlyList<string> Metadata);

/// <summary>
/// What the application uses to work with owner policies: open (native or imported), register, validate, dry-run, approve,
/// plan, apply, roll back. Every step is done as the Windows account running the process — there is no typed operator or
/// approver name — so approving needs a different account with access to the same store (spec: no self-approval).
/// Imported policies are written to the library as native YAML; that exact text is what the SHA-256 binds.
/// </summary>
public sealed class PolicyWorkbench
{
    private readonly Func<string> _identity;
    public string Root { get; }
    public PolicyStore Store { get; }
    public PolicyExecutor Executor { get; }
    public string LibraryDir => Path.Combine(Root, "library");
    public string EvidenceDir => Path.Combine(Root, "evidence");

    public PolicyWorkbench(string? root = null, IEnumerable<ISettingProvider>? providers = null, Func<string>? identity = null)
    {
        Root = root ?? DefaultRoot;
        _identity = identity ?? (() => { using var id = WindowsIdentity.GetCurrent(); return id.Name; });
        Store = new PolicyStore(Path.Combine(Root, "store.json"));
        Executor = new PolicyExecutor(providers ?? WindowsSettingProviders.All(), EvidenceDir);
    }

    public static string DefaultRoot => Path.GetDirectoryName(PolicyStore.DefaultPath)!;

    public string Operator => _identity();

    /// <summary>Opens a native policy (.lapolicy/.yaml/.yml/.json) or imports Registry.pol, .inf, audit.csv, LGPO text, a GPO backup zip or folder.</summary>
    public OpenedPolicy Open(string path)
    {
        if (Directory.Exists(path)) return Keep(PolicyImport.FromGpoBackup(GpoBackup.FromFolder(path), "1.0.0", path));
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".lapolicy" or ".yaml" or ".yml" or ".json")
        {
            var p = PolicyLoader.LoadFile(path);
            return new OpenedPolicy(p, path, [], []);
        }
        var bytes = File.ReadAllBytes(path);
        var id = "IMPORT-" + Convert.ToHexString(SHA256.HashData(bytes))[..12];
        ImportReport r = ext switch
        {
            ".pol" => PolicyImport.FromRegistryPol(bytes, path.Contains(@"\User\", StringComparison.OrdinalIgnoreCase) ? "HKCU" : "HKLM", id, "1.0.0", Operator, path),
            ".inf" => PolicyImport.FromSecurityTemplate(SecurityTemplate.Decode(bytes), id, "1.0.0", Operator, path),
            ".csv" => PolicyImport.FromAuditCsv(SecurityTemplate.Decode(bytes), id, "1.0.0", Operator, path),
            ".txt" => PolicyImport.FromLgpoText(SecurityTemplate.Decode(bytes), id, "1.0.0", Operator, path),
            ".zip" => PolicyImport.FromGpoBackup(GpoBackup.FromZip(new MemoryStream(bytes)), "1.0.0", path),
            _ => throw new NotSupportedException($"Formatul „{ext}” nu este cunoscut (lapolicy, yaml, json, pol, inf, csv, txt LGPO, zip/folder backup GPO)."),
        };
        return Keep(r);
    }

    /// <summary>Writes an imported policy to the library. An existing file with other text is never overwritten.</summary>
    private OpenedPolicy Keep(ImportReport r)
    {
        Directory.CreateDirectory(LibraryDir);
        var file = Path.Combine(LibraryDir, $"{Safe(r.Policy.Id)}@{Safe(r.Policy.Version)}.lapolicy");
        if (File.Exists(file))
        {
            var existing = PolicyLoader.LoadFile(file);
            if (existing.Sha256 != r.Policy.Sha256)
                throw new PolicyLifecycleException($"Biblioteca are deja {Path.GetFileName(file)} cu alt conținut; nu se suprascrie.");
        }
        else File.WriteAllText(file, r.Yaml, new UTF8Encoding(false));
        return new OpenedPolicy(PolicyLoader.LoadFile(file), file, r.Unsupported, r.Metadata);
    }

    private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public PolicyRecord Register(PolicyDocument p) => Store.Register(p, Operator);
    public PolicyRecord Validate(PolicyDocument p) => Store.Validate(p, Operator);

    /// <summary>Dry run: reads every control on this station without changing anything; TESTED only if all were read.</summary>
    public (PolicyPlan Plan, PolicyRecord Record) DryRun(PolicyDocument p)
    {
        var plan = Executor.Plan(p);
        return (plan, Store.MarkTested(p, Operator, plan.Controls.Count - plan.Unreadable, plan.Unreadable));
    }

    public PolicyRecord Approve(PolicyDocument p, string reason) => Store.Approve(p, Operator, reason);
    public PolicyPlan Plan(PolicyDocument p) => Executor.Plan(p);
    public ExecutionRecord Apply(PolicyDocument p, PolicyPlan plan, string confirmedPlanSha256) => Executor.Apply(p, plan, Store, Operator, confirmedPlanSha256);
    public ExecutionRecord Rollback(PolicyDocument p, string executionId) => Executor.Rollback(p, executionId, Operator);
    public PolicyRecord Retire(PolicyDocument p, string reason) => Store.Retire(p, Operator, reason);

    /// <summary>Executions recorded for a policy, newest first.</summary>
    public IReadOnlyList<ExecutionRecord> Executions(PolicyDocument p)
    {
        var dir = Path.Combine(EvidenceDir, "executions");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.json").Select(f => Executor.Load(Path.GetFileNameWithoutExtension(f)))
            .Where(e => e.PolicySha256 == p.Sha256).OrderByDescending(e => e.StartedUtc).ToList();
    }
}
