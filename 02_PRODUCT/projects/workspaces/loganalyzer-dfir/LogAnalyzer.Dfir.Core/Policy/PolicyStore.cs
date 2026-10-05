using System.Text.Json;

namespace LogAnalyzer.Dfir.Policy;

public sealed record PolicyTransition(PolicyStatus From, PolicyStatus To, string Operator, DateTimeOffset AtUtc, string Reason, string PolicySha256);

public sealed class PolicyRecord
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Sha256 { get; init; }
    public required string Author { get; init; }
    public required string RegisteredBy { get; init; }
    public PolicyStatus Status { get; set; }
    public List<PolicyTransition> History { get; init; } = [];
    public List<string> ValidationErrors { get; set; } = [];
}

public sealed class PolicyLifecycleException(string message) : InvalidOperationException(message);

/// <summary>
/// Lifecycle of owner policies (master spec §12): DRAFT → VALIDATED → TESTED → APPROVED → DEPLOYED → VERIFIED, RETIRED from any
/// state. Every step names the operator and the policy SHA-256; a policy whose text changed since registration is blocked
/// (a changed policy is a new DRAFT). Approval by the author or by whoever registered the policy is refused. Nothing
/// can be applied from DRAFT, VALIDATED or TESTED.
/// </summary>
public sealed class PolicyStore
{
    private readonly string _path;
    private readonly Dictionary<string, PolicyRecord> _records;

    public PolicyStore(string path)
    {
        _path = path;
        _records = File.Exists(path)
            ? JsonSerializer.Deserialize<List<PolicyRecord>>(File.ReadAllText(path))!.ToDictionary(r => Key(r.Id, r.Version))
            : [];
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "Policies", "store.json");

    private static string Key(string id, string version) => $"{id}@{version}";

    public PolicyRecord? Get(PolicyDocument p) => _records.GetValueOrDefault(Key(p.Id, p.Version));

    public PolicyRecord Register(PolicyDocument p, string op)
    {
        if (Get(p) is { } existing)
        {
            if (!existing.Sha256.Equals(p.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new PolicyLifecycleException($"Politica {p.Id} versiunea {p.Version} există deja cu alt conținut (SHA-256 {existing.Sha256[..12]}…). Creșteți versiunea.");
            return existing;
        }
        var r = new PolicyRecord { Id = p.Id, Version = p.Version, Sha256 = p.Sha256, Author = p.Author, RegisteredBy = op, Status = PolicyStatus.Draft };
        r.History.Add(new PolicyTransition(PolicyStatus.Draft, PolicyStatus.Draft, op, DateTimeOffset.UtcNow, "înregistrată", p.Sha256));
        _records[Key(p.Id, p.Version)] = r;
        Save();
        return r;
    }

    public PolicyRecord Validate(PolicyDocument p, string op)
    {
        var r = Require(p, PolicyStatus.Draft);
        var errors = PolicyValidator.Validate(p);
        r.ValidationErrors = errors;
        if (errors.Count > 0) { Save(); throw new PolicyLifecycleException($"Politica nu trece validarea ({errors.Count} erori): {string.Join("; ", errors.Take(5))}"); }
        return Move(r, p, PolicyStatus.Validated, op, "validare structurală trecută");
    }

    /// <summary>TESTED only after a dry run that read every control without error on a station.</summary>
    public PolicyRecord MarkTested(PolicyDocument p, string op, int controlsRead, int readErrors)
    {
        var r = Require(p, PolicyStatus.Validated);
        if (readErrors > 0 || controlsRead != p.Controls.Count)
            throw new PolicyLifecycleException($"Rularea de probă a citit {controlsRead} din {p.Controls.Count} controale, cu {readErrors} erori: politica rămâne VALIDATED.");
        return Move(r, p, PolicyStatus.Tested, op, $"rulare de probă: {controlsRead} controale citite fără erori");
    }

    public PolicyRecord Approve(PolicyDocument p, string approver, string reason)
    {
        var r = Require(p, PolicyStatus.Tested);
        if (Same(approver, r.Author) || Same(approver, r.RegisteredBy))
            throw new PolicyLifecycleException($"Auto-aprobarea nu este permisă: {approver} este autorul sau cel care a înregistrat politica.");
        if (reason.Trim().Length == 0) throw new PolicyLifecycleException("Aprobarea cere o justificare.");
        return Move(r, p, PolicyStatus.Approved, approver, reason);
    }

    public PolicyRecord MarkDeployed(PolicyDocument p, string op, string executionId) =>
        Move(Require(p, PolicyStatus.Approved), p, PolicyStatus.Deployed, op, $"aplicată (execuția {executionId})");

    public PolicyRecord MarkVerified(PolicyDocument p, string op, string executionId) =>
        Move(Require(p, PolicyStatus.Deployed), p, PolicyStatus.Verified, op, $"toate controalele verificate (execuția {executionId})");

    public PolicyRecord Retire(PolicyDocument p, string op, string reason)
    {
        var r = Get(p) ?? throw new PolicyLifecycleException($"Politica {p.Id} {p.Version} nu este înregistrată.");
        return Move(r, p, PolicyStatus.Retired, op, reason);
    }

    /// <summary>Throws unless the policy may change the station now: APPROVED (first deployment) or DEPLOYED/VERIFIED (re-apply), same hash.</summary>
    public PolicyRecord RequireApplicable(PolicyDocument p)
    {
        var r = Get(p) ?? throw new PolicyLifecycleException($"Politica {p.Id} {p.Version} nu este înregistrată: nu se aplică.");
        CheckHash(r, p);
        if (r.Status is not (PolicyStatus.Approved or PolicyStatus.Deployed or PolicyStatus.Verified))
            throw new PolicyLifecycleException($"Politica este {r.Status.ToString().ToUpperInvariant()}: doar o politică APPROVED se poate aplica.");
        return r;
    }

    private PolicyRecord Require(PolicyDocument p, PolicyStatus expected)
    {
        var r = Get(p) ?? throw new PolicyLifecycleException($"Politica {p.Id} {p.Version} nu este înregistrată.");
        CheckHash(r, p);
        if (r.Status != expected) throw new PolicyLifecycleException($"Pasul cere starea {expected.ToString().ToUpperInvariant()}, politica este {r.Status.ToString().ToUpperInvariant()}.");
        return r;
    }

    private static void CheckHash(PolicyRecord r, PolicyDocument p)
    {
        if (!r.Sha256.Equals(p.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PolicyLifecycleException($"SHA-256 al politicii s-a schimbat ({r.Sha256[..12]}… → {p.Sha256[..12]}…): pașii aprobați nu se mai aplică textului modificat.");
    }

    private PolicyRecord Move(PolicyRecord r, PolicyDocument p, PolicyStatus to, string op, string reason)
    {
        r.History.Add(new PolicyTransition(r.Status, to, op, DateTimeOffset.UtcNow, reason, p.Sha256));
        r.Status = to;
        Save();
        return r;
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_records.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
