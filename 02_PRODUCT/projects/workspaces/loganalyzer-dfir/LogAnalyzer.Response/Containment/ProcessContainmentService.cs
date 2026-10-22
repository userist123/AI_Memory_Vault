using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Response;

namespace LogAnalyzer.Dfir.Windows.Containment;

/// <summary>
/// Contains one program without disconnecting the station: per-program firewall block rules (outbound and inbound),
/// optional suspension, then a read-only scan. Everything is recorded in the case (audit log, custody, evidence).
/// Every action is reversible through <see cref="Release"/>. Both are controlled responses: after applying, the firewall rules are
/// re-read and the process is re-enumerated (PID + start time + image SHA-256); the outcome (VERIFIED / NOT_VERIFIED / FAILED) and
/// its evidence go to the case audit, and the incident state claims "contained" or "released" only when the re-check confirmed it.
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
    private readonly IProcessTable? _processes;

    /// <summary>Time limit of each re-read.</summary>
    public TimeSpan VerifyTimeout { get; init; } = ResponseVerifier.DefaultTimeout;

    public ProcessContainmentService(IFirewallController firewall, ProcessScanner scanner, CaseWorkspace caseWorkspace,
                                     Action<int>? suspend = null, Action<int>? resume = null, string? operatorName = null,
                                     IProcessTable? processes = null)
    {
        _processes = processes;
        _firewall = firewall;
        _scanner = scanner;
        _case = caseWorkspace;
        _suspend = suspend;
        _resume = resume;
        _operator = operatorName ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
    }

    public string IncidentsDir => Path.Combine(_case.Root, "Incidents");

    [ControlledResponse("firewall rules re-read (name, program, direction, enabled); process re-enumerated (PID + start + image SHA-256, threads suspended)")]
    public ProcessIncident Contain(string programPath, int? pid, string trigger, ContainmentOptions options, CancellationToken ct = default)
    {
        RequireHostChange("contain", programPath); // before any rule, suspension or incident; only the refusal itself is audited
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
        // 2. Optional suspension (operator's choice). The identity is taken first so the re-check cannot confuse a reused PID, and the
        // process holding the PID must BE the program being contained (image path, and SHA-256 when the suspect's is known): a stale
        // suspect list must never suspend an unrelated process. Mismatch or unreadable -> no suspension (REJECTED, with the cause);
        // the firewall rules for the path are applied and re-checked separately.
        bool suspendRequested = options.SuspendProcess && pid is not null && _suspend is not null;
        string? suspendRefused = null;
        if (suspendRequested && pid is int p)
        {
            (incident.ProcessIdentity, suspendRefused) = IdentifyTarget(p, programPath, options.ExpectedSha256);
            if (suspendRefused is not null) Record(incident, "process.suspend", p.ToString(), false, "suspendare refuzată: " + suspendRefused);
            else
            {
                try { _suspend!(p); incident.ProcessSuspended = true; Record(incident, "process.suspend", p.ToString(), true, ""); }
                catch (InvalidOperationException ex) { Record(incident, "process.suspend", p.ToString(), false, ex.Message); }
            }
        }

        // VERIFY: re-read the firewall and the process list; the action's own return value is not evidence.
        var probes = rules.Select(r => RulePresent(r.Name, programPath, r.Dir)).ToList();
        if (suspendRequested && pid is int sp)
            probes.Add(suspendRefused is { } why
                ? new ResponseProbe($"proces PID {sp}", "suspendat (toate firele)",
                    () => new ResponseObservation(ResponseStatus.Rejected, $"suspendare refuzată, procesul NU a fost atins: {why}"))
                : ProcessVerification.Suspended(incident.ProcessIdentity, sp, _processes));
        incident.Verification = Verified("containment", "apply", probes);
        // A Failed incident may still hold active rules (partial containment): FirewallRules lists them, and ContainmentText.Summary names them.
        incident.State = incident.FirewallRules.Count == 0 ? ContainmentState.Failed : incident.Verification.Status switch
        {
            ResponseStatus.Verified => ContainmentState.Contained,
            ResponseStatus.Failed => ContainmentState.Failed,
            _ => ContainmentState.NotVerified,
        };

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

    [ControlledResponse("firewall rules re-read (absent); process re-enumerated (resumed, or ended / PID reused)")]
    public ProcessIncident Release(ProcessIncident incident, string reason)
    {
        RequireHostChange("release", incident.IncidentId);
        bool resumeRequested = incident.ProcessSuspended && incident.Pid is not null && _resume is not null;
        string? resumeRefused = null;
        if (resumeRequested && incident.Pid is int p)
        {
            // Resume ONLY when the current holder of the PID is the captured process (PID + start time + image SHA-256). A PID that now
            // belongs to another process is never touched (the original has ended); an unknown identity or an unreadable process list
            // is never resumed either: the PID may have been reused, and resuming it could wake an unrelated (or malicious) process.
            var (m, observed) = CurrentHolder(incident.ProcessIdentity, p);
            if (m is ProcessVerification.Match.Gone or ProcessVerification.Match.Reused)
            {
                incident.ProcessSuspended = false;
                Record(incident, "process.resume", p.ToString(), true, "nu s-a reluat nimic: " + observed);
            }
            else if (m != ProcessVerification.Match.Same)
            {
                resumeRefused = observed;
                Record(incident, "process.resume", p.ToString(), false,
                    $"NU s-a reluat: {observed}. Verificați manual procesul PID {p} și reluați-l doar dacă este programul izolat.");
            }
            else
            {
                try { _resume!(p); incident.ProcessSuspended = false; Record(incident, "process.resume", p.ToString(), true, reason); }
                catch (InvalidOperationException ex) { Record(incident, "process.resume", p.ToString(), false, ex.Message); }
            }
        }
        var targets = incident.FirewallRules.ToList();
        foreach (var rule in targets)
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
        // VERIFY the rollback: every rule re-read as absent, the process re-enumerated.
        // Every rule this incident ever tried to create is re-read, so a release with nothing left to remove is still checked.
        var ruleNames = targets.Concat(incident.Actions.Where(x => x.Action.StartsWith("firewall.block.", StringComparison.Ordinal)).Select(x => x.Target))
                               .Distinct(StringComparer.Ordinal).ToList();
        var probes = ruleNames.Select(RuleAbsent).ToList();
        if (resumeRequested && incident.Pid is int rp)
            probes.Add(resumeRefused is { } why
                ? new ResponseProbe($"proces PID {rp}", "nu mai este suspendat (reluat sau terminat)",
                    () => ResponseObservation.Unknown($"procesul NU a fost reluat (identitatea nu poate fi confirmată): {why}"))
                : ProcessVerification.NotSuspended(incident.ProcessIdentity, rp, _processes));
        incident.ReleaseVerification = Verified("containment", "rollback", probes);
        // A rule the re-read did not confirm as gone stays on the incident, so a new release tries it again.
        foreach (var c in incident.ReleaseVerification.Checks.Where(c => c.Status != ResponseStatus.Verified && ruleNames.Contains(c.Target)))
            if (!incident.FirewallRules.Contains(c.Target)) incident.FirewallRules.Add(c.Target);
        if (incident.FirewallRules.Count == 0 && incident.ReleaseVerification.Status == ResponseStatus.Verified) incident.State = ContainmentState.Released;
        Save(incident);
        return incident;
    }

    /// <summary>
    /// The identity of the process holding <paramref name="pid"/>, or why it must not be suspended: no process reader, the PID is free,
    /// its image or start time cannot be read, its image is not <paramref name="programPath"/>, or its SHA-256 differs from the suspect's.
    /// </summary>
    private (ResponseProcessIdentity? Identity, string? Refused) IdentifyTarget(int pid, string programPath, string? expectedSha256)
    {
        if (_processes is null) return (null, "nu există un cititor al listei de procese: nu se poate confirma că PID-ul este programul izolat");
        try
        {
            var now = _processes.Observe(pid);
            if (now is null) return (null, $"niciun proces cu PID {pid}");
            if (string.IsNullOrEmpty(now.ImagePath) || now.StartUtc is null)
                return (null, $"imaginea sau ora de start a PID {pid} nu pot fi citite: nu se poate confirma că este {Path.GetFileName(programPath)}");
            if (!SamePath(now.ImagePath, programPath))
                return (null, $"PID {pid} rulează {now.ImagePath}, nu {programPath} (listă de suspecți învechită sau PID refolosit)");
            var id = ProcessVerification.Capture(_processes, pid);
            if (id is null) return (null, $"identitatea PID {pid} (start, SHA-256 al imaginii) nu poate fi stabilită");
            if (!SamePath(id.ImagePath, programPath)) return (null, $"PID {pid} rulează {id.ImagePath}, nu {programPath}");
            if (expectedSha256 is { Length: > 0 } want && !want.Equals(id.ImageSha256, StringComparison.OrdinalIgnoreCase))
                return (null, $"imaginea PID {pid} are SHA-256 {id.ImageSha256[..Math.Min(12, id.ImageSha256.Length)]}…, nu cel al suspectului ({want[..Math.Min(12, want.Length)]}…)");
            return (id, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, $"lista proceselor nu poate fi citită ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>What holds the PID now compared with the captured identity; Unknown when there is no identity or the list cannot be read.</summary>
    private (ProcessVerification.Match Match, string Observed) CurrentHolder(ResponseProcessIdentity? id, int pid)
    {
        if (_processes is null) return (ProcessVerification.Match.Unknown, "nu există un cititor al listei de procese");
        if (id is null) return (ProcessVerification.Match.Unknown, "identitatea procesului (start, SHA-256) nu a fost stabilită la izolare");
        try { return ProcessVerification.Compare(id, _processes.Observe(pid), _processes); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (ProcessVerification.Match.Unknown, $"lista proceselor nu poate fi citită ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>The host-change gate; a refusal is written to the case audit (who, what) before it propagates. Nothing else is written.</summary>
    private void RequireHostChange(string action, string target)
    {
        try { LogAnalyzer.Dfir.Auth.OperatorIdentity.RequireHostChange(); }
        catch (LogAnalyzer.Dfir.Auth.HostChangeNotAuthorizedException)
        {
            _case.Audit("containment.refused",
                $"{action} {target} NOT_AUTHORIZED: {LogAnalyzer.Dfir.Auth.OperatorIdentity.Who} nu are dreptul de a schimba stația; nu s-a schimbat nimic");
            throw;
        }
    }

    private ResponseProbe RulePresent(string name, string programPath, FirewallDirection dir) =>
        new(name, $"regulă de blocare activă pentru {Path.GetFileName(programPath)}, {dir}", () =>
        {
            var r = _firewall.ListContainmentRules().FirstOrDefault(x => x.Name == name);
            if (r is null) return ResponseObservation.Differs("regula nu există în firewall");
            if (!r.Enabled) return ResponseObservation.Differs("regula există, dar este dezactivată");
            if (r.Direction != dir) return ResponseObservation.Differs($"regula are direcția {r.Direction}");
            if (!string.Equals(r.ProgramPath, programPath, StringComparison.OrdinalIgnoreCase)) return ResponseObservation.Differs($"regula este pentru {r.ProgramPath}");
            return ResponseObservation.Holds("regula există, activă, pentru program");
        });

    private ResponseProbe RuleAbsent(string name) =>
        new(name, "regula nu mai există", () =>
            _firewall.ListContainmentRules().Any(x => x.Name == name)
                ? ResponseObservation.Differs("regula există în continuare")
                : ResponseObservation.Holds("regula nu mai există în firewall"));

    private ResponseOutcome Verified(string action, string phase, List<ResponseProbe> probes)
    {
        var o = ResponseVerifier.Verify(action, phase, probes, VerifyTimeout);
        return ResponseVerifier.Record(o, (a, d) => _case.Audit($"containment.{a}", d));
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
