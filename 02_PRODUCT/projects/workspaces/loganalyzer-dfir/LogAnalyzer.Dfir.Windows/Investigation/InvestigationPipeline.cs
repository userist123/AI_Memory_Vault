using System.Security.Principal;
using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Graph;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Parsers;
using LogAnalyzer.Verification;

namespace LogAnalyzer.Dfir.Windows.Investigation;

public sealed record CollectionRow(string Collector, EvidenceStatus Status, int EvidenceCount, string Errors, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public sealed class InvestigationResult
{
    public required CaseWorkspace Case { get; init; }
    public List<CollectionRow> Collection { get; } = [];
    public List<ParseResult> Parsing { get; } = [];
    public List<TimelineEvent> Timeline { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<EvidenceGap> Gaps { get; } = [];
    /// <summary>Findings refused because they did not point to evidence in the case (kept for review, never reported as findings).</summary>
    public List<RejectedFinding> RejectedFindings { get; } = [];
    /// <summary>Entities and relationships built from the timeline and findings (Analysis/graph.json).</summary>
    public EvidenceGraph? Graph { get; set; }
    /// <summary>Rule matches (IOC, Sigma, YARA) with rule identity and evidence (Analysis/detections.json).</summary>
    public List<DetectionResult> Detections { get; } = [];
    /// <summary>Rules that were loaded for this run (Analysis/rules.json).</summary>
    public List<DetectionRule> RulesUsed { get; } = [];
    /// <summary>Anti-forensics checks: DETECTED / NOT_DETECTED / UNDETERMINED per technique (Analysis/anti_forensics.json).</summary>
    public List<AntiForensicCheck> AntiForensics { get; } = [];
    /// <summary>The procedure profile used by this run (null = none: log clears stay NotAssessed). Its snapshot is Analysis/procedure_profile.json.</summary>
    public ProcedureProfile? Procedure { get; set; }
    /// <summary>WP15b: policy change timeline, configured/applied/enforced/observed levels, control gaps and unreliable-time windows (Analysis/policy_timeline.json).</summary>
    public PolicyTimelineResult? PolicyTimeline { get; set; }
    public string PolicyTimelineLine => PolicyTimeline is null ? "Cronologie politici: nedefinit (nu a fost calculată)."
        : $"Cronologie politici: {PolicyTimeline.Changes.Count} modificări de politică, {PolicyTimeline.Applications.Count} înregistrări de aplicare GroupPolicy, {PolicyTimeline.Gaps.Count} decalaje de control, {PolicyTimeline.TimeWindows.Count} ferestre de oră nesigură."
          + (PolicyTimeline.ExpectedNote.Length > 0 ? " Politica așteptată: nedefinit." : "");
    /// <summary>WP14a: SHA-256 of the copies of the media / users registers in the case ("" = register not defined, nothing copied).</summary>
    public string MediaRegisterSha256 { get; set; } = "";
    public string UsersRegisterSha256 { get; set; } = "";
    /// <summary>What the report says about the registers: each defined (with the SHA-256 of its copy) or "nedefinit" (never "conform").</summary>
    public string RegisterLine { get; set; } = "Registre: nedefinit (nu au fost folosite).";
    public string ProcedureProfileSha256 { get; set; } = "";
    /// <summary>What the report says about the profile: each section defined or "nedefinit" (never "conform"), and the SHA-256 of the copy in the case.</summary>
    public string ProcedureProfileLine => "Profil de proceduri: " + (Procedure is null ? "niciun profil (toate secțiunile nedefinite)" : LogAnalyzer.Dfir.Profile.ProfileOps.Describe(Procedure)
        + (ProcedureProfileSha256.Length > 0 ? $"; copie în caz Analysis/procedure_profile.json, SHA-256 {ProcedureProfileSha256}" : ""));
    /// <summary>
    /// WP4: the verdict on every finding and on the AI statements (Analysis/verification.json), from the separate verification module. Null for a run that
    /// did not get that far. The verdicts are also set on <see cref="Finding.Verification"/> in memory; findings.json on disk is never rewritten.
    /// </summary>
    public VerificationReport? Verification { get; set; }
    public string TimelineCsv { get; set; } = "";
    public string FindingsJson { get; set; } = "";
    /// <summary>How far the analysis got (UX contract §20). Not a verdict on any finding: see <see cref="Finding.Verification"/>.</summary>
    public OperationState State { get; set; } = OperationState.NotStarted;
    public string StateReason { get; set; } = "";
}

/// <summary>
/// The full investigation, as done by hand on MARIUS-PC: acquire (or import) evidence into a case, parse it with the
/// real parsers, build one timeline, correlate, record findings and evidence gaps. Every step leaves an audit trail.
/// </summary>
public sealed class InvestigationPipeline
{
    private const string Producer = "InvestigationPipeline";
    private readonly ParserRegistry _registry;

    public InvestigationPipeline(ParserRegistry? registry = null) => _registry = registry ?? WindowsParsers.Registry;

    public static IReadOnlyList<ICollector> AllCollectors { get; } =
        [new LiveStateCollector(), new EventLogCollector(), new PrefetchCollector(), new ExecutionArtifactsCollector(), new SrumCollector()];

    /// <summary>Creates a case. <paramref name="scope"/> is mandatory (owner decision 23): an incomplete scope is refused with <see cref="CaseScopeIncompleteException"/>.</summary>
    public static CaseWorkspace NewCase(string casesRoot, string name, CaseScope scope)
    {
        var id = $"CASE-{Environment.MachineName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        return CaseWorkspace.Create(Path.Combine(casesRoot, id), new CaseInfo
        {
            CaseId = id, Name = name, Host = Environment.MachineName, User = Environment.UserName, CreatedAtUtc = DateTimeOffset.UtcNow,
            Investigator = $"{Environment.UserDomainName}\\{Environment.UserName}", Os = Environment.OSVersion.VersionString,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(), Timezone = TimeZoneInfo.Local.Id,
            CollectionMode = "investigation", Scope = scope,
            StationRole = LogAnalyzer.Core.Services.Edition.StationRoles.Name(LogAnalyzer.Core.Services.Edition.StationRoleContext.Role),
        });
    }

    /// <summary>Imports existing evidence files — copies, never moves. Known artifacts get their type; anything else is type "file".</summary>
    public static List<EvidenceItem> Import(CaseWorkspace ws, IEnumerable<string> files)
    {
        var list = new List<EvidenceItem>();
        foreach (var f in files)
        {
            var type = ImportType(f);
            var source = type == "evtx" ? Path.GetFileNameWithoutExtension(f).Replace('%', '/') : type;
            list.Add(ws.ImportFile(f, "Import:" + source, type == "evtx" ? "EventLog:" + source : type, TemporalType.Historical, "Import", DfirInfo.ApplicationVersion,
                notes: "Importat de operator din: " + f));
        }
        return list;
    }

    /// <summary>The evidence type <see cref="Import"/> gives a file. Every file is evidence; one with no known type is "file" (hash IOCs, YARA). WP18: the intake screen uses the same rule.</summary>
    public static string ImportType(string f)
    {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext switch
            {
                ".evtx" => "evtx", ".pf" => "prefetch", ".pcapng" => "pcapng", ".lnk" => "lnk",
                _ when f.EndsWith(".automaticDestinations-ms", StringComparison.OrdinalIgnoreCase) => "jumplist_auto",
                _ when Path.GetFileName(f).Equals("SRUDB.dat", StringComparison.OrdinalIgnoreCase) => "srum",
                _ when Path.GetFileName(f).Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
                       || Path.GetFileName(f).EndsWith("SYSTEM.hiv", StringComparison.OrdinalIgnoreCase) => "system_hive",
                _ when Path.GetFileName(f).Equals("Amcache.hve", StringComparison.OrdinalIgnoreCase) => "amcache",
                _ when EvidenceFingerprint.Detect(f) == "task_xml" => "task_xml",
                _ when EvidenceFingerprint.Detect(f) == "usn_fsutil" => "usn_journal",
                _ when Path.GetFileName(f).Equals("History", StringComparison.OrdinalIgnoreCase) && EvidenceFingerprint.Detect(f) == "sqlite" => "chromium_history",
                _ when Path.GetFileName(f).Equals("places.sqlite", StringComparison.OrdinalIgnoreCase) && EvidenceFingerprint.Detect(f) == "sqlite" => "firefox_places",
                _ when RegistryHiveType(Path.GetFileName(f)) is { } hiveType && EvidenceFingerprint.Detect(f) == "regf" => hiveType,
                // Any other file is still evidence: copied, hashed, listed (no parser → SKIPPED_BY_DESIGN) and available to
                // hash IOCs and YARA rules.
                _ => "file",
            };
    }

    /// <summary>Hive kind from the usual file names of saved or copied hives.</summary>
    public static string? RegistryHiveType(string name)
    {
        var n = name.ToUpperInvariant();
        if (n is "NTUSER.DAT" or "HKCU.HIV" || n.StartsWith("NTUSER", StringComparison.Ordinal) && n.EndsWith(".HIV", StringComparison.Ordinal)) return "ntuser_hive";
        if (n is "SOFTWARE" or "SOFTWARE.HIV" || n.EndsWith("_SOFTWARE.HIV", StringComparison.Ordinal)) return "software_hive";
        return null;
    }

    /// <param name="mediaRegister">WP14a: the media register to compare observed media with. When null the register installed on this machine
    /// (%PROGRAMDATA%\LogAnalyzer\registers) is loaded if there is one; without rows it is "registru nedefinit".</param>
    /// <param name="usersRegister">WP14a: users and clearances, loaded like the media register.</param>
    /// <param name="systemZone">WP14a: the zone of the analysed system, as the operator declares it ("" = not declared).</param>
    /// <param name="procedureProfile">The procedure profile to use. When null the profile installed on this machine (%PROGRAMDATA%\LogAnalyzer\profile) is loaded
    /// if there is one; with none, behaviour is exactly as before the profile existed (log clears are NotAssessed).</param>
    public InvestigationResult Run(CaseWorkspace ws, CollectionProfile profile, bool collect, IProgress<string>? progress = null, CancellationToken ct = default, ProcedureProfile? procedureProfile = null,
        MediaRegister? mediaRegister = null, UsersRegister? usersRegister = null, string systemZone = "")
    {
        var r = new InvestigationResult { Case = ws, State = OperationState.Running, StateReason = "Analiza rulează." };
        try { return RunCore(r, ws, profile, collect, progress, ct, procedureProfile, mediaRegister, usersRegister, systemZone); }
        catch (OperationCanceledException) { MarkEnded(r, ws, OperationState.Cancelled, "Oprit de operator; rezultatele parțiale nu sunt complete."); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { MarkEnded(r, ws, OperationState.Failed, ex.Message); throw; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void MarkEnded(InvestigationResult r, CaseWorkspace ws, OperationState state, string reason)
    {
        r.State = state;
        r.StateReason = reason;
        try
        {
            var dir = Path.Combine(ws.Root, "Analysis");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "run_state.json"), JsonSerializer.Serialize(new RunState(ws.Info.CaseId, state, reason, DateTimeOffset.UtcNow), new JsonSerializerOptions { WriteIndented = true }));
            ws.RecordOutput("Analysis/run_state.json", Producer, DfirInfo.ApplicationVersion);
            ws.Audit("investigation." + state.ToSpec().ToLowerInvariant(), reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the original failure is what the caller must see */ }
    }

    private InvestigationResult RunCore(InvestigationResult r, CaseWorkspace ws, CollectionProfile profile, bool collect, IProgress<string>? progress, CancellationToken ct, ProcedureProfile? procedureProfile = null,
        MediaRegister? mediaRegister = null, UsersRegister? usersRegister = null, string systemZone = "")
    {
        bool elevated = OperatingSystem.IsWindows() && IsAdministrator();
        ws.Audit("investigation.start", $"profile={profile} collect={collect} elevated={elevated}");

        // 1. Acquisition.
        if (collect)
            foreach (var c in AllCollectors.Where(c => (c.Profiles & profile) != 0))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Colectare: {c.Name}");
                var start = DateTimeOffset.UtcNow;
                CollectorOutcome o;
                try { o = c.Collect(new CollectorContext { Case = ws, IsElevated = elevated }, ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                { o = new CollectorOutcome { Status = EvidenceStatus.Failed, Errors = { ex.Message } }; }
                var end = DateTimeOffset.UtcNow;
                r.Collection.Add(new CollectionRow(c.Name, o.Status, o.Evidence.Count, string.Join("; ", o.Errors), start, end));
                ws.RecordCollection(new CollectionAuditRecord(c.Name, start, end, o.Status, o.ExitCode, o.Source, ws.Root, o.Evidence.Sum(e => e.Size),
                    "", o.Tool, o.ToolVersion, string.Join(" && ", o.Commands), string.Join("; ", o.Errors)));
                if (o.Status is EvidenceStatus.NotAvailable or EvidenceStatus.Failed)
                    r.Gaps.Add(new EvidenceGap(c.Name, o.Status, string.Join("; ", o.Errors), "Sursa lipsește din analiză",
                        c.RequiresElevation ? "Rulare ca administrator" : "Import manual al probei", elevated ? "Posibil" : "Cu drepturi de administrator"));
                else if (o.Errors.Count > 0)
                    r.Gaps.Add(new EvidenceGap(c.Name, EvidenceStatus.Partial, string.Join("; ", o.Errors.Take(8)), "Unele surse lipsesc", "Rulare ca administrator / import", "Posibil"));
            }

        // 2. Parsing into one timeline.
        var analysisDir = Path.Combine(ws.Root, "Analysis");
        Directory.CreateDirectory(analysisDir);
        var sink = new ListSink();
        File.WriteAllText(Path.Combine(analysisDir, "parsers.json"), JsonSerializer.Serialize(_registry.Descriptors, new JsonSerializerOptions { WriteIndented = true }));
        ws.RecordOutput("Analysis/parsers.json", Producer, DfirInfo.ApplicationVersion);
        foreach (var ev in ws.LoadEvidence())
        {
            ct.ThrowIfCancellationRequested();
            var full = ws.FullPath(ev.StoredPath);
            var sel = _registry.Select(ev, full);
            if (!sel.HasCandidate)
            {
                // Live snapshots are read by LiveStateAnalyzer below; anything else is listed, never silently ignored.
                if (ev.SourceType != "live_snapshot")
                    r.Parsing.Add(new ParseResult
                    {
                        EvidenceId = ev.EvidenceId, Parser = "-", ParserVersion = "-", Status = EvidenceStatus.SkippedByDesign,
                        Error = sel.Problem, ExpectedSha256 = ev.Sha256,
                    });
                continue;
            }
            if (sel.Parsers.Count == 0)
            {
                var pre0 = sel.Preflight!;
                var refused = new ParseResult
                {
                    EvidenceId = ev.EvidenceId, Parser = string.Join("|", _registry.Candidates(ev).Select(p => p.Descriptor.ParserId)), ParserVersion = "-",
                    Status = EvidenceStatus.Failed, Error = sel.Problem, ExpectedSha256 = ev.Sha256, SourceSha256Before = pre0.Sha256, SourceFingerprint = pre0.Fingerprint,
                };
                r.Parsing.Add(refused);
                r.Gaps.Add(new EvidenceGap(Path.GetFileName(ev.StoredPath), EvidenceStatus.Failed, refused.Error,
                    "Proba nu a fost parsată: rezultatele ar putea să nu provină din sursa achiziționată", "Reachiziție din sursa originală", "Doar prin reachiziție"));
                ws.Audit(pre0.Code == "EVIDENCE_MUTATED" ? "evidence.mutated" : "evidence.preflight_failed", $"{ev.EvidenceId} {sel.Problem}");
                continue;
            }
            var pre = sel.Preflight!;
            foreach (var parser in sel.Parsers)
            {
                progress?.Report($"Parsare: {Path.GetFileName(ev.StoredPath)} ({parser.Descriptor.ParserId})");
                var mark = sink.Events.Count;
                var pr = parser.Parse(ev, full, sink, ct);
                pr.ExpectedSha256 = ev.Sha256;
                pr.SourceSha256Before = pre.Sha256;
                pr.SourceFingerprint = pre.Fingerprint;
                var post = parser.Preflight(ev, full);
                pr.SourceSha256After = post.Sha256;
                if (!post.CanParse)
                {
                    // The source changed while it was being read: nothing extracted from it can be attributed to the acquired original.
                    sink.Events.RemoveRange(mark, sink.Events.Count - mark);
                    pr.Status = EvidenceStatus.Failed;
                    pr.Error = $"{post.Code} în timpul parsării: {post.Detail}" + (pr.Error.Length > 0 ? $" | {pr.Error}" : "");
                    ws.Audit("evidence.mutated", $"{ev.EvidenceId} during parse {post.Detail}");
                }
                else
                    ProvenanceBinder.BindEvents(sink.Events.Skip(mark), ev, parser.Descriptor.ParserId, parser.Descriptor.Version);
                r.Parsing.Add(pr);
                ws.RecordTransformation(ev.EvidenceId, parser.Descriptor.ParserId, parser.Descriptor.Version, "Analysis/timeline.csv", $"{pr.Records} înregistrări, status {pr.Status.ToSpec()}");
                r.Gaps.AddRange(pr.Gaps);
                if (pr.Status is EvidenceStatus.Failed or EvidenceStatus.Partial)
                    r.Gaps.Add(new EvidenceGap(Path.GetFileName(ev.StoredPath), pr.Status, pr.Error.Length > 0 ? pr.Error : $"{pr.MalformedRecords} înregistrări corupte",
                        "Evenimente lipsă din cronologie", "Alt parser / copie a probei", "Posibil"));
            }
        }
        r.Timeline.AddRange(sink.Events.OrderBy(e => e.Time.Utc ?? DateTimeOffset.MaxValue));
        r.Gaps.AddRange(AuditCoverage.Gaps(r.Timeline));

        // 3. Correlation (timeline) + live state.
        progress?.Report("Corelare");
        // WP15a: the procedure profile (explicit, or the one installed on this machine) turns log clears into Routine / Unexpected; without one they stay NotAssessed.
        var profileIssues = new List<ProfileIssue>();
        if (procedureProfile is null)
        {
            var loaded = ProfileStore.Load();
            profileIssues.AddRange(loaded.Issues);
            procedureProfile = loaded.Profile;
            foreach (var i in loaded.Issues.Where(i => i.IsError)) ws.Audit("profile.issue", i.ToString());
        }
        else profileIssues.AddRange(ProfileOps.Validate(procedureProfile));
        LogMaintenancePolicy? maintenance = null;
        if (procedureProfile is not null)
        {
            r.Procedure = procedureProfile;
            r.ProcedureProfileSha256 = ProfileSnapshot.WriteTo(ws, procedureProfile, profileIssues);
            maintenance = ProfileSnapshot.MaintenancePolicyFor(procedureProfile, r.Timeline.Where(e => e.Time.Utc is not null).Select(e => e.Time.Utc!.Value), ws.Info.Timezone);
        }
        var found = new List<Finding>(Correlation.Run(r.Timeline, maintenance, procedureProfile));
        // WP14a: removable media against the media register, CD/DVD, and NIC / Wi-Fi / Bluetooth / DHCP on an air-gapped or standalone scope.
        progress?.Report("Medii amovibile și integritate air-gap");
        int wp14No = found.Select(x => x.FindingId.StartsWith("F-", StringComparison.Ordinal) && int.TryParse(x.FindingId.AsSpan(2), out var n0) ? n0 : 0).DefaultIfEmpty(0).Max();
        var wp14 = Wp14Analysis.Run(ws, r.Timeline, () => $"F-{++wp14No:D4}", mediaRegister, usersRegister, procedureProfile, systemZone);
        found.AddRange(wp14.Findings);
        r.MediaRegisterSha256 = wp14.MediaSha256; r.UsersRegisterSha256 = wp14.UsersSha256; r.RegisterLine = wp14.Line;
        foreach (var live in ws.LoadEvidence().Where(e => e.SourceType == "live_snapshot"))
        {
            var pre = EvidencePreflight.Check(live, ws.FullPath(live.StoredPath));
            if (!pre.CanParse)
            {
                r.Gaps.Add(new EvidenceGap(Path.GetFileName(live.StoredPath), EvidenceStatus.Failed, $"{pre.Code}: {pre.Detail}",
                    "Constatările din starea live lipsesc", "O nouă fotografie a stării live", "Doar prin reachiziție"));
                ws.Audit(pre.Code == "EVIDENCE_MUTATED" ? "evidence.mutated" : "evidence.preflight_failed", $"{live.EvidenceId} {pre.Code} {pre.Detail}");
                continue;
            }
            found.AddRange(LiveStateAnalyzer.Analyze(live, ws.FullPath(live.StoredPath), found.Count));
        }
        // WP15b: policy change timeline, configured/applied/enforced/observed and control gaps, from the case timeline alone (EVTX is enough; the expected policy comes from the profile link).
        progress?.Report("Cronologie politici");
        var expectedIssues = new List<string>();
        var expectedSettings = PolicyTimeline.ExpectedFromProfile(procedureProfile, expectedIssues);
        foreach (var issue in expectedIssues) ws.Audit("policy_timeline.expected_issue", issue);
        var policyTimeline = PolicyTimeline.Build(r.Timeline, new PolicyTimelineOptions
        {
            Expected = expectedSettings, ExpectedIssues = expectedIssues, Maintenance = maintenance,
            HighFindingTimesUtc = found.Where(x => x.Severity >= Severity.High && (x.FirstSeenUtc ?? x.LastSeenUtc) is not null).Select(x => (x.FirstSeenUtc ?? x.LastSeenUtc)!.Value).ToList(),
        });
        int lastFindingNo = found.Select(x => x.FindingId.StartsWith("F-", StringComparison.Ordinal) && int.TryParse(x.FindingId.AsSpan(2), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        found.AddRange(policyTimeline.ToFindings(() => $"F-{++lastFindingNo:D4}"));
        r.PolicyTimeline = policyTimeline;
        // WP14b: combined sequences (control gap + media, SMB + staging + USB, portable software + archive + USB) over the findings and rows above; they add no second detection.
        progress?.Report("Secvențe combinate");
        int sequenceNo = found.Select(x => x.FindingId.StartsWith("F-", StringComparison.Ordinal) && int.TryParse(x.FindingId.AsSpan(2), out var n1) ? n1 : 0).DefaultIfEmpty(0).Max();
        var sequences = SequenceRules.Run(r.Timeline, found, () => $"F-{++sequenceNo:D4}", new SequenceInput { Scope = ws.Info.Scope });
        found.AddRange(sequences);
        var (kept, rejected) = ProvenanceBinder.BindFindings(found, ws.LoadEvidence().ToDictionary(e => e.EvidenceId, StringComparer.Ordinal));
        TimeReliability.Apply(kept, r.Timeline);
        // Finding contract: semantic type, standard state, limitations, provenance, audit trail (added beside Classification).
        var parsersByEvidence = r.Parsing.Where(p => p.Parser != "-" && p.Status is EvidenceStatus.Success or EvidenceStatus.Partial or EvidenceStatus.Empty)
                                         .GroupBy(p => p.EvidenceId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(p => p.Parser).Distinct().ToList(), StringComparer.Ordinal);
        kept = FindingContract.Enrich(kept, new FindingContractContext { ParsersOf = e => parsersByEvidence.GetValueOrDefault(e) ?? [], NowUtc = DateTimeOffset.UtcNow }).ToList();
        r.Findings.AddRange(kept);
        r.RejectedFindings.AddRange(rejected);
        foreach (var x in rejected) ws.Audit("finding.rejected", $"{x.Finding.FindingId} {x.Finding.RuleId}: {x.Reason}");

        // 4. Outputs, kept in the case and hashed into custody.
        progress?.Report("Scriere rezultate");
        // WP3b: every file written below is registered in the custody chain with its SHA-256 and the evidence it rests on.
        var evidenceIds = ws.LoadEvidence().Select(e => e.EvidenceId).ToList();
        r.TimelineCsv = Path.Combine(analysisDir, "timeline.csv");
        TimelineCsv.Write(r.TimelineCsv, r.Timeline);
        ws.RecordOutput("Analysis/timeline.csv", Producer, DfirInfo.ApplicationVersion, evidenceIds);
        r.FindingsJson = Path.Combine(analysisDir, "findings.json");
        File.WriteAllText(r.FindingsJson, SchemaVersions.WithVersion(new { r.Findings, r.Gaps, r.Collection, RejectedFindings = r.RejectedFindings.Select(x => new { x.Finding.FindingId, x.Finding.RuleId, x.Finding.Title, x.Reason }) }, SchemaVersions.Findings));
        ws.RecordOutput("Analysis/findings.json", Producer, DfirInfo.ApplicationVersion, evidenceIds);
        File.WriteAllText(Path.Combine(analysisDir, "parsing.json"), JsonSerializer.Serialize(r.Parsing, new JsonSerializerOptions { WriteIndented = true }));
        ws.RecordOutput("Analysis/parsing.json", Producer, DfirInfo.ApplicationVersion, evidenceIds);
        progress?.Report("Graf de probe");
        r.Graph = EvidenceGraph.Build(r.Timeline, r.Findings, ws.Info.Host);
        var (graphJson, graphSha) = r.Graph.Snapshot(ws.Info.CaseId);
        File.WriteAllText(Path.Combine(analysisDir, "graph.json"), graphJson);
        ws.RecordOutput("Analysis/graph.json", "EvidenceGraph", "1.0", evidenceIds);
        ws.RecordTransformation("CASE", "EvidenceGraph", "1.0", "Analysis/graph.json", $"{r.Graph.Entities.Count} entități, {r.Graph.Relationships.Count} relații, SHA-256 {graphSha}");

        // Detection: rules shipped with the application plus the case's own Rules folder (ioc / yara / sigma).
        progress?.Report("Detecție (IOC, Sigma, YARA)");
        var engine = DetectionEngine.Load(Path.Combine(AppContext.BaseDirectory, "Rules"), Path.Combine(ws.Root, "Rules"));
        var evidenceItems = ws.LoadEvidence();
        var scannable = new List<EvidenceItem>();
        foreach (var e in evidenceItems)
        {
            if (e.SourceType != "file") { scannable.Add(e); continue; }
            var pre = EvidencePreflight.Check(e, ws.FullPath(e.StoredPath));
            if (pre.CanParse) scannable.Add(e);
            else r.Gaps.Add(new EvidenceGap(e.OriginalName, EvidenceStatus.Failed, $"{pre.Code}: {pre.Detail}", "Fișierul nu a fost scanat cu YARA", "Reachiziție", "Doar prin reachiziție"));
        }
        r.Detections.AddRange(engine.Run(r.Timeline, scannable, e => File.ReadAllBytes(ws.FullPath(e.StoredPath))));
        r.RulesUsed.AddRange(engine.Rules);
        r.Gaps.AddRange(engine.LoadErrors);
        File.WriteAllText(Path.Combine(analysisDir, "detections.json"), JsonSerializer.Serialize(r.Detections, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(analysisDir, "rules.json"), JsonSerializer.Serialize(r.RulesUsed, new JsonSerializerOptions { WriteIndented = true }));
        ws.RecordOutput("Analysis/detections.json", "DetectionEngine", "1.0", evidenceIds);
        ws.RecordOutput("Analysis/rules.json", "DetectionEngine", "1.0");
        ws.RecordTransformation("CASE", "DetectionEngine", "1.0", "Analysis/detections.json", $"{r.Detections.Count} potriviri, {r.RulesUsed.Count} reguli");
        r.AntiForensics.AddRange(LogAnalyzer.Dfir.Analysis.AntiForensics.Evaluate(r.Timeline, r.Gaps, maintenance,
            found.Where(x => x.Severity >= Severity.High && x.RuleId != "LOG-TAMPER" && (x.FirstSeenUtc ?? x.LastSeenUtc) is not null).Select(x => (x.FirstSeenUtc ?? x.LastSeenUtc)!.Value).ToList()));
        File.WriteAllText(Path.Combine(analysisDir, "anti_forensics.json"), JsonSerializer.Serialize(r.AntiForensics, new JsonSerializerOptions { WriteIndented = true }));
        ws.RecordOutput("Analysis/anti_forensics.json", "AntiForensics", "1.0", evidenceIds);
        ws.RecordTransformation("CASE", "AntiForensics", "1.0", "Analysis/anti_forensics.json",
            $"{r.AntiForensics.Count(c => c.Result == AntiForensicResult.Detected)} DETECTED, {r.AntiForensics.Count(c => c.Result == AntiForensicResult.Undetermined)} UNDETERMINED");
        File.WriteAllText(Path.Combine(analysisDir, "policy_timeline.json"), policyTimeline.ToJson());
        ws.RecordOutput("Analysis/policy_timeline.json", "PolicyTimeline", "1.0", evidenceIds);
        ws.RecordTransformation("CASE", "PolicyTimeline", "1.0", "Analysis/policy_timeline.json",
            $"{policyTimeline.Changes.Count} modificări, {policyTimeline.Applications.Count} aplicări, {policyTimeline.Gaps.Count} decalaje, {policyTimeline.TimeWindows.Count} ferestre de oră nesigură");
        // WP4 verification layer (R9.7): after graph, detections and anti-forensics, before the Vault export. The verifier reads the case back from disk
        // (it is a separate module); dependencies.json is written first so it can confirm the finding -> evidence dependencies (rewritten with the proposals below).
        progress?.Report("Verificare");
        ws.WriteDependencies(r.Findings, []);
        r.Verification = CaseVerifier.Verify(ws, ct: ct);
        var verdicts = r.Verification.ToContract();
        foreach (var f in r.Findings)
            if (verdicts.TryGetValue(f.FindingId, out var verdict)) f.Verification = verdict;   // in memory only: findings.json stays as it was written
        // Memory Vault proposals (spec §24): written into the case only; submitting them is the operator's step, through the vault's gate.
        var (proposals, refusedForVault) = LogAnalyzer.Dfir.Memory.VaultExport.FromCase(ws.Info.CaseId, ws.LoadEvidence(), r.Findings, r.AntiForensics, r.Gaps,
            $"LogAnalyzer {DfirInfo.ApplicationVersion} ({Environment.UserDomainName}\\{Environment.UserName})", verdicts);
        var vaultFile = Path.Combine(ws.Root, "Exports", "vault_proposals.jsonl");
        // WP3b gate: a proposal that rests on evidence found MODIFIED or MISSING by the last re-check is refused, with the reason, not written for submission.
        var (releasedProposals, invalidatedProposals) = LogAnalyzer.Dfir.Memory.VaultExport.Release(ws, proposals);
        refusedForVault.AddRange(invalidatedProposals);
        LogAnalyzer.Dfir.Memory.VaultExport.Write(vaultFile, releasedProposals);
        File.WriteAllText(Path.Combine(ws.Root, "Exports", "vault_refused.json"), JsonSerializer.Serialize(refusedForVault, new JsonSerializerOptions { WriteIndented = true }));
        ws.RecordTransformation("CASE", "VaultExport", "1.0", "Exports/vault_proposals.jsonl", $"{releasedProposals.Count} propuneri, {refusedForVault.Count} refuzate");
        ws.WriteDependencies(r.Findings, proposals);   // Analysis/dependencies.json: finding and proposal id -> evidence ids
        ws.RecordOutput("Exports/vault_proposals.jsonl", "VaultExport", "1.0", evidenceIds);
        ws.RecordOutput("Exports/vault_refused.json", "VaultExport", "1.0", evidenceIds);
        ws.WriteManifest("Exports/export_manifest.json", ["Exports/vault_proposals.jsonl", "Exports/vault_refused.json", "Analysis/dependencies.json"], Producer, DfirInfo.ApplicationVersion);
        var (state, stateReason) = OperationStates.Summarize(r.Collection.Select(c => c.Status).ToList(), r.Parsing);
        MarkEnded(r, ws, state, stateReason);
        var schemaManifest = SchemaManifest.ForRun();
        schemaManifest.Anchor = ws.Anchor();   // chain heads: a copy held outside the case makes truncation at the end detectable
        schemaManifest.Write(Path.Combine(analysisDir, SchemaVersions.ManifestFile));
        ws.RecordOutput("Analysis/" + SchemaVersions.ManifestFile, Producer, DfirInfo.ApplicationVersion);
        ws.Audit("investigation.end", $"{r.Timeline.Count} events, {r.Findings.Count} findings, {r.Gaps.Count} gaps, state {state.ToSpec()}");
        return r;
    }
}

/// <summary>Findings from the live-state snapshot: what is running or set to start from user-writable locations, and unsigned.</summary>
public static class LiveStateAnalyzer
{
    public static IEnumerable<Finding> Analyze(EvidenceItem item, string path, int offset)
    {
        var snap = JsonSerializer.Deserialize<LiveStateCollector.Snapshot>(File.ReadAllText(path));
        if (snap is null) yield break;
        int n = offset;
        string Id() => $"F-{++n:D4}";
        var internet = snap.Connections.Where(c => c.State != "LISTEN" && IpClassifier.IsExternal(c.Remote.Address.ToString())).ToLookup(c => c.Pid);
        foreach (var p in snap.Processes.Where(p => p.UserWritable && p.Signature.StartsWith("NESEMNAT", StringComparison.Ordinal)))
        {
            var conns = internet[p.Pid].Select(c => $"{c.Remote.Address}:{c.Remote.Port}").Distinct().ToList();
            yield return new Finding
            {
                FindingId = Id(), RuleId = conns.Count > 0 ? "LIVE-UNSIGNED-USERPATH-NET" : "LIVE-UNSIGNED-USERPATH",
                Title = $"Proces nesemnat din locație scriabilă{(conns.Count > 0 ? ", cu conexiuni în Internet" : "")}: {p.Name}",
                Severity = conns.Count > 0 ? Severity.High : Severity.Medium, Category = "Execution", Classification = Classification.Direct,
                Confidence = conns.Count > 0 ? Confidence.High : Confidence.Medium, Process = p.Name, Pid = p.Pid, File = p.Path,
                Ip = string.Join(", ", conns.Take(10)), FirstSeenUtc = snap.TakenUtc,
                Description = $"PID {p.Pid}, părinte {p.Ppid}; linie de comandă: {p.CommandLine}" + (conns.Count > 0 ? $"; conexiuni: {string.Join(", ", conns.Take(10))}" : ""),
                ClassificationReason = "Observat în fotografia stării live (semnătură verificată, tabela TCP).",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Processes[pid={p.Pid}]", "fotografie live")],
                AlternativeExplanations = ["Aplicație legitimă instalată per utilizator, fără semnătură."],
                RecommendedNextSteps = ["Izolați programul din fila „Izolare procese suspecte” pentru scanare completă."],
            };
        }
        foreach (var s in snap.Services.Where(s => s.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-SERVICE-USERPATH", Title = $"Serviciu configurat dintr-o locație scriabilă: {s.Name}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1543.003",
                File = s.Path, User = s.Account, FirstSeenUtc = snap.TakenUtc,
                Description = $"{s.DisplayName}: {s.Path} ({s.StartMode}, {s.State}, cont {s.Account}).",
                ClassificationReason = "Configurația curentă a serviciilor.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Services[{s.Name}]", "fotografie live")],
                ContradictingEvidence = s.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? ["Serviciul este dezactivat (StartMode=Disabled): nu pornește singur."] : [],
            };
        foreach (var t in snap.Tasks.Where(t => t.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-TASK-USERPATH", Title = $"Task programat care rulează din locație scriabilă: {Path.GetFileName(t.Path)}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1053.005",
                File = t.Command, FirstSeenUtc = snap.TakenUtc, Description = $"{t.Path}: {t.Command}",
                ClassificationReason = "Definiția curentă a taskului.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Tasks[{t.Path}]", "fotografie live")],
            };
        foreach (var a in snap.Autoruns.Where(a => a.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-AUTORUN-USERPATH", Title = $"Pornire automată din locație scriabilă: {a.Name}",
                Severity = Severity.Medium, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1547.001",
                File = a.Value, FirstSeenUtc = snap.TakenUtc, Description = $"{a.Location}\\{a.Name} = {a.Value}",
                ClassificationReason = "Cheile Run curente.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Autoruns[{a.Name}]", "fotografie live")],
                AlternativeExplanations = ["Multe aplicații per utilizator (actualizatoare, sincronizare) pornesc legitim din AppData."],
            };
    }
}
