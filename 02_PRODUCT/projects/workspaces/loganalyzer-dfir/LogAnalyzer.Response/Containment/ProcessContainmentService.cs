using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Containment;

/// <summary>
/// Contains one program without disconnecting the station: per-program firewall block rules (outbound and inbound),
/// optional suspension, then a read-only scan. Everything is recorded in the case (audit log, custody, evidence).
/// Every action is reversible through <see cref="Release"/>.
/// </summary>
public sealed class ProcessContainmentService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    private readonly IFirewallController _firewall;
    private readonly ProcessScanner _scanner;
    private readonly CaseWorkspace _case;
    private readonly Action<int>? _suspend;
    private readonly Action<int>? _resume;
    private readonly string _operator;

    public ProcessContainmentService(IFirewallController firewall, ProcessScanner scanner, CaseWorkspace caseWorkspace,
                                     Action<int>? suspend = null, Action<int>? resume = null, string? operatorName = null)
    {
        _firewall = firewall;
        _scanner = scanner;
        _case = caseWorkspace;
        _suspend = suspend;
        _resume = resume;
        _operator = operatorName ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
    }

    public string IncidentsDir => Path.Combine(_case.Root, "Incidents");

    public ProcessIncident Contain(string programPath, int? pid, string trigger, ContainmentOptions options, CancellationToken ct = default)
    {
        programPath = Path.GetFullPath(programPath);
        var incident = new ProcessIncident
        {
            IncidentId = NextIncidentId(),
            CaseId = _case.Info.CaseId,
            ProgramPath = programPath,
            Pid = pid,
            Trigger = trigger,
            CreatedUtc = DateTimeOffset.UtcNow,
            State = ContainmentState.Contained,
        };
        _case.Audit("containment.start", $"{incident.IncidentId} {programPath} pid={pid?.ToString() ?? "-"} trigger={trigger}");

        // 1. Cut the program's network access (outbound always, inbound by default).
        string tag = $"{incident.IncidentId}-{ShortHash(programPath)}";
        var rules = new List<(string Name, FirewallDirection Dir)> { ($"{WindowsFirewallController.RulePrefix}{tag}-out", FirewallDirection.Outbound) };
        if (options.BlockInbound) rules.Add(($"{WindowsFirewallController.RulePrefix}{tag}-in", FirewallDirection.Inbound));
        foreach (var (name, dir) in rules)
        {
            try
            {
                _firewall.AddProgramBlockRule(name, programPath, dir,
                    $"LogAnalyzer {incident.IncidentId}: acces la rețea blocat doar pentru {Path.GetFileName(programPath)} ({trigger}).");
                incident.FirewallRules.Add(name);
                Record(incident, $"firewall.block.{dir.ToString().ToLowerInvariant()}", name, true, programPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                Record(incident, $"firewall.block.{dir.ToString().ToLowerInvariant()}", name, false, ex.Message);
            }
        }
        if (incident.FirewallRules.Count == 0) incident.State = ContainmentState.Failed;

        // 2. Optional suspension (operator's choice).
        if (options.SuspendProcess && pid is int p && _suspend is not null)
        {
            try { _suspend(p); incident.ProcessSuspended = true; Record(incident, "process.suspend", p.ToString(), true, ""); }
            catch (InvalidOperationException ex) { Record(incident, "process.suspend", p.ToString(), false, ex.Message); }
        }

        // 3. Read-only scan, then evidence.
        ct.ThrowIfCancellationRequested();
        incident.Scan = _scanner.Scan(programPath, pid, ct);
        Record(incident, "scan", programPath, true, $"{incident.Scan.SuspicionReasons.Count} motive de suspiciune, {incident.Scan.Gaps.Count} goluri");

        if (options.CopySampleIntoCase && File.Exists(programPath))
        {
            try
            {
                var ev = _case.ImportFile(programPath, $"incident_{incident.IncidentId}", "sample", TemporalType.CurrentSnapshot,
                    "ProcessContainmentService", DfirInfo.ApplicationVersion, Sensitivity.HighlySensitive,
                    notes: "Copie a programului izolat. Nu se execută.");
                incident.EvidenceIds.Add(ev.EvidenceId);
                Record(incident, "evidence.sample", ev.EvidenceId, true, ev.Sha256);
            }
            catch (IOException ex) { Record(incident, "evidence.sample", programPath, false, ex.Message); }
        }

        incident.Findings.AddRange(BuildContractFindings(incident));
        Save(incident);
        return incident;
    }

    /// <summary>Collects the attempts the firewall blocked since containment (needs failure auditing, see BlockedConnectionLog).</summary>
    public ProcessIncident RefreshBlockedAttempts(ProcessIncident incident)
    {
        try
        {
            var seen = incident.BlockedAttempts.Select(b => b.RecordId).ToHashSet();
            foreach (var b in BlockedConnectionLog.ForProgram(incident.ProgramPath, incident.CreatedUtc))
                if (seen.Add(b.RecordId)) incident.BlockedAttempts.Add(b);
            Record(incident, "blocked.refresh", incident.ProgramPath, true, $"{incident.BlockedAttempts.Count} încercări blocate");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or EventLogException)
        {
            Record(incident, "blocked.refresh", incident.ProgramPath, false, ex.Message);
        }
        incident.Findings.RemoveAll(f => f.RuleId == "CONTAIN-BLOCKED-ATTEMPTS");
        incident.Findings.AddRange(BuildContractFindings(incident).Where(f => f.RuleId == "CONTAIN-BLOCKED-ATTEMPTS"));
        Save(incident);
        return incident;
    }

    public ProcessIncident Release(ProcessIncident incident, string reason)
    {
        if (incident.ProcessSuspended && incident.Pid is int p && _resume is not null)
        {
            try { _resume(p); incident.ProcessSuspended = false; Record(incident, "process.resume", p.ToString(), true, reason); }
            catch (InvalidOperationException ex) { Record(incident, "process.resume", p.ToString(), false, ex.Message); }
        }
        foreach (var rule in incident.FirewallRules.ToList())
        {
            try
            {
                bool removed = _firewall.RemoveRule(rule);
                Record(incident, "firewall.remove", rule, true, removed ? reason : "regula nu mai exista");
                incident.FirewallRules.Remove(rule);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                Record(incident, "firewall.remove", rule, false, ex.Message);
            }
        }
        if (incident.FirewallRules.Count == 0) incident.State = ContainmentState.Released;
        Save(incident);
        return incident;
    }

    public IReadOnlyList<ProcessIncident> LoadAll()
    {
        if (!Directory.Exists(IncidentsDir)) return [];
        return Directory.EnumerateFiles(IncidentsDir, "incident.json", SearchOption.AllDirectories)
                        .Select(f => JsonSerializer.Deserialize<ProcessIncident>(File.ReadAllText(f), JsonOpts)!)
                        .OrderByDescending(i => i.CreatedUtc).ToList();
    }

    public string IncidentDir(ProcessIncident incident) => Path.Combine(IncidentsDir, incident.IncidentId);

    private void Save(ProcessIncident incident)
    {
        var dir = IncidentDir(incident);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "incident.json"), JsonSerializer.Serialize(incident, JsonOpts));
    }

    private void Record(ProcessIncident incident, string action, string target, bool ok, string detail)
    {
        incident.Actions.Add(new ContainmentAction(DateTimeOffset.UtcNow, action, target, ok, detail, _operator));
        _case.Audit($"containment.{action}", $"{incident.IncidentId} {(ok ? "OK" : "FAILED")} {target} {detail}");
    }

    private string NextIncidentId()
    {
        Directory.CreateDirectory(IncidentsDir);
        int n = Directory.GetDirectories(IncidentsDir, "INC-*").Length + 1;
        string id;
        do { id = $"INC-{n++:D4}"; } while (Directory.Exists(Path.Combine(IncidentsDir, id)));
        return id;
    }

    private static string ShortHash(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s.ToUpperInvariant())))[..8];

    /// <summary>The incident's findings with the finding contract applied (semantic type, standard state, limitations, provenance).</summary>
    internal static IEnumerable<Finding> BuildContractFindings(ProcessIncident i) =>
        LogAnalyzer.Dfir.Analysis.FindingContract.Enrich(BuildFindings(i).ToList(), new LogAnalyzer.Dfir.Analysis.FindingContractContext { NowUtc = DateTimeOffset.UtcNow });

    /// <summary>Findings carry the observation they rest on; classification follows the spec (DIRECT = observed now).</summary>
    internal static IEnumerable<Finding> BuildFindings(ProcessIncident i)
    {
        var s = i.Scan;
        var name = Path.GetFileName(i.ProgramPath);
        if (s is not null && s.SuspicionReasons.Count > 0)
            yield return new Finding
            {
                FindingId = $"{i.IncidentId}-F1", RuleId = "CONTAIN-SUSPICIOUS-PROGRAM",
                Title = $"Program suspect izolat: {name}",
                Severity = s.Defender?.ThreatFound == true ? Severity.Critical : s.SuspicionReasons.Count >= 3 ? Severity.High : Severity.Medium,
                Category = "Execution / Command and Control", Classification = Classification.Direct,
                Confidence = s.SuspicionReasons.Count >= 3 ? Confidence.High : Confidence.Medium,
                Process = name, Pid = i.Pid, File = i.ProgramPath,
                Description = string.Join(" ", s.SuspicionReasons),
                ClassificationReason = "Observații directe pe stație în momentul scanării (semnătură, locație, conexiuni, persistență).",
                SupportingEvidence = i.EvidenceIds.Select(e => new EvidenceRef(e, i.ProgramPath, "copia programului")).ToList(),
                AlternativeExplanations = ["Software legitim nesemnat, instalat per utilizator (de verificat cu proprietarul stației)."],
                RecommendedNextSteps = ["Verificați destinațiile din raport.", "Căutați același SHA-256 pe alte stații.", "Păstrați izolarea până la clarificare."],
            };
        var external = i.AllNetworkIntents.Where(n => !SuspiciousProcessDetector.IsPrivateOrLocal(n.Destination)).ToList();
        if (i.BlockedAttempts.Count > 0)
            yield return new Finding
            {
                FindingId = $"{i.IncidentId}-F2", RuleId = "CONTAIN-BLOCKED-ATTEMPTS",
                Title = $"{name} a încercat să comunice după izolare",
                Severity = Severity.High, Category = "Command and Control", Classification = Classification.Direct, Confidence = Confidence.High,
                Process = name, File = i.ProgramPath,
                FirstSeenUtc = i.BlockedAttempts.Min(b => b.TimeUtc), LastSeenUtc = i.BlockedAttempts.Max(b => b.TimeUtc),
                Ip = string.Join(", ", i.BlockedAttempts.Select(b => b.DestAddress).Distinct().Take(10)),
                Description = $"{i.BlockedAttempts.Count} încercări blocate către {i.BlockedAttempts.Select(b => $"{b.DestAddress}:{b.DestPort}").Distinct().Count()} destinații.",
                ClassificationReason = "Evenimente Security 5157 înregistrate de Windows pentru calea programului izolat.",
                SupportingEvidence = i.BlockedAttempts.Take(20).Select(b => new EvidenceRef("Security.evtx (live)", $"RecordID {b.RecordId}", $"{b.DestAddress}:{b.DestPort}/{b.Protocol}")).ToList(),
            };
        else if (external.Count > 0)
            yield return new Finding
            {
                FindingId = $"{i.IncidentId}-F2", RuleId = "CONTAIN-EXTERNAL-CONNECTIONS",
                Title = $"{name} avea conexiuni către Internet la momentul izolării",
                Severity = Severity.Medium, Category = "Command and Control", Classification = Classification.Direct, Confidence = Confidence.Medium,
                Process = name, Pid = i.Pid, File = i.ProgramPath,
                Ip = string.Join(", ", external.Select(e => e.Destination).Distinct().Take(10)),
                Description = string.Join("; ", external.Select(e => $"{e.Destination}:{e.Port} ({e.Kind})").Distinct().Take(20)),
                ClassificationReason = "Tabela TCP a stației, citită la scanare.",
            };
        if (s is not null && s.Persistence.Count > 0)
            yield return new Finding
            {
                FindingId = $"{i.IncidentId}-F3", RuleId = "CONTAIN-PERSISTENCE",
                Title = $"{name} pornește automat",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High,
                File = i.ProgramPath,
                Description = string.Join("; ", s.Persistence.Select(p => $"{p.Mechanism}: {p.Location}")),
                ClassificationReason = "Intrări de autostart care conțin calea sau numele programului.",
                RecommendedNextSteps = ["Dezactivați intrările de autostart după preluarea probelor, nu înainte."],
            };
    }
}
