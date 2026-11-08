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

public static class InvestigationResultAcquisitionExtensions
{
    /// <summary>
    /// Fills <see cref="InvestigationResult.AcquisitionRuns"/> from the case. Cancellation propagates; an IO error is kept in
    /// <see cref="InvestigationResult.AcquisitionRunsError"/> (shown instead of "no records"), never turned into an empty list.
    /// </summary>
    public static void LoadAcquisitionRuns(this InvestigationResult r, CaseWorkspace ws, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? knownActualHashes = null, IReadOnlyDictionary<string, string>? registeredOutputs = null)
    {
        r.AcquisitionRuns.Clear();
        r.AcquisitionRunsError = "";
        try { r.AcquisitionRuns.AddRange(AcquisitionRunOverview.Load(ws, ct, knownActualHashes, registeredOutputs)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { r.AcquisitionRunsError = ex.Message; }
    }

    /// <summary>Fills <see cref="InvestigationResult.Custody"/> (tab 9) from the case custody chain. Cancellation propagates; an IO error is kept in <see cref="InvestigationResult.CustodyError"/>.</summary>
    public static void LoadCustody(this InvestigationResult r, CaseWorkspace ws, CancellationToken ct = default)
    {
        r.Custody = null;
        r.CustodyError = "";
        try { r.Custody = CustodyOverview.Load(ws, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException) { r.CustodyError = ex.Message; }   // review: never stop the case from opening
    }
}

public sealed class InvestigationResult
{
    public required CaseWorkspace Case { get; init; }
    public List<CollectionRow> Collection { get; } = [];
    /// <summary>R7c: the acquisition run records of the case with the verdict of their re-check (Logs/acquisition_runs); filled after the acquisition ended and when a case is opened.</summary>
    public List<AcquisitionRunRow> AcquisitionRuns { get; } = [];
    /// <summary>Why <see cref="AcquisitionRuns"/> could not be read ("" = it was read; an empty list then really means no records).</summary>
    public string AcquisitionRunsError { get; set; } = "";
    /// <summary>The case custody chain with its verdict (tab 9 "Istoricul probelor"); null until read or when <see cref="CustodyError"/> says why it could not be.</summary>
    public CustodyLedger? Custody { get; set; }
    public string CustodyError { get; set; } = "";
    public List<ParseResult> Parsing { get; } = [];
    /// <summary>Master plan 6.7: the evidence files that are Windows executables, read statically (Analysis/pe_triage.json).</summary>
    public List<LogAnalyzer.Dfir.Detection.PeTriageRow> PeTriage { get; } = [];
    /// <summary>Master plan 6.10: identities rebuilt from the Security events of this run (Analysis/identity.json); null on a case analysed before it existed.</summary>
    public LogAnalyzer.Dfir.Identity.IdentityReport? Identity { get; set; }
    /// <summary>Master plan 6.16: the immutable record of this run and its difference from the previous run (null for the first run or an opened case).</summary>
    public AnalysisRunManifest? Run { get; set; }
    public RunDiff? RunDiff { get; set; }
    public List<TimelineEvent> Timeline { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<EvidenceGap> Gaps { get; } = [];
    /// <summary>Findings refused because they did not point to evidence in the case (kept for review, never reported as findings).</summary>
    public List<RejectedFinding> RejectedFindings { get; } = [];
    /// <summary>Entities and relationships built from the timeline and findings (Analysis/graph.json).</summary>
    public EvidenceGraph? Graph { get; set; }
    /// <summary>Rule matches (IOC, Sigma, YARA) with rule identity and evidence (Analysis/detections.json).</summary>
    public List<DetectionResult> Detections { get; } = [];
    /// <summary>R7a: hashes of the case found in an operator-imported fingerprint set, known-good (label only) and known-bad (also a finding), Analysis/known_hashes.json.</summary>
    public List<LogAnalyzer.Dfir.HashSets.HashSetMatch> KnownHashMatches { get; } = [];
    /// <summary>Rules that were loaded for this run (Analysis/rules.json).</summary>
    public List<DetectionRule> RulesUsed { get; } = [];
    /// <summary>Anti-forensics checks: DETECTED / NOT_DETECTED / UNDETERMINED per technique (Analysis/anti_forensics.json).</summary>
    public List<AntiForensicCheck> AntiForensics { get; } = [];
    /// <summary>The procedure profile used by this run (null = none: log clears stay NotAssessed). Its snapshot is Analysis/procedure_profile.json.</summary>
    public ProcedureProfile? Procedure { get; set; }
    /// <summary>E7: drive letters the station facts tie to removable media (a reopened case gets them back from Analysis/user_activity.json).</summary>
    public List<string> FactsRemovableLetters { get; } = [];
    /// <summary>E7: the classified folders of the profile this run used (Analysis/user_activity.json); null = take them from <see cref="Procedure"/>. A reopened case gets them from the file.</summary>
    public List<LogAnalyzer.Dfir.Profile.ClassifiedFolderRow>? ActivityFolders { get; set; }
    /// <summary>E7: set by the case loader for a reopened case that has user-activity rows but no saved context (written before it existed): the view then says it cannot be evaluated.</summary>
    public string ActivityUnevaluable { get; set; } = "";
    /// <summary>E7: ShellBags / RecentDocs / Office MRU / Recycle Bin rows of the timeline read against the profile's classified folders and the removable volumes (same view as the USER-ACTIVITY-* rules).</summary>
    public LogAnalyzer.Dfir.Analysis.UserActivityReport UserActivityView()
    {
        if (ActivityUnevaluable.Length > 0)
        {
            // rows are listed, but none is read against folders or letters that were not saved: no relevance, no "nedefinite în profil"
            var plain = LogAnalyzer.Dfir.Analysis.UserActivityAnalysis.Analyze(Timeline, [], [], evaluate: false);
            return plain.Total == 0 ? plain : new LogAnalyzer.Dfir.Analysis.UserActivityReport { Items = plain.Items, Unevaluable = ActivityUnevaluable };
        }
        return LogAnalyzer.Dfir.Analysis.Wp14Rules.UserActivityForCase(Timeline, ActivityFolders ?? Procedure?.ClassifiedFolders, FactsRemovableLetters);
    }
    /// <summary>WP15b: policy change timeline, configured/applied/enforced/observed levels, control gaps and unreliable-time windows (Analysis/policy_timeline.json).</summary>
    public PolicyTimelineResult? PolicyTimeline { get; set; }
    public string PolicyTimelineLine => PolicyTimeline is null ? "Cronologie politici: nedefinită (nu a fost calculată)."
        : $"Cronologie politici: {LogAnalyzer.Dfir.Language.RoText.Count(PolicyTimeline.Changes.Count, "modificare", "modificări")} de politică, {LogAnalyzer.Dfir.Language.RoText.Count(PolicyTimeline.Applications.Count, "înregistrare", "înregistrări")} de aplicare GroupPolicy, {PolicyTimeline.Gaps.Count} decalaje de control, {PolicyTimeline.TimeWindows.Count} ferestre de oră nesigură."
          + (PolicyTimeline.ExpectedNote.Length > 0 ? " Politica așteptată: nedefinită." : "");
    /// <summary>WP14a: SHA-256 of the copies of the media / users registers in the case ("" = register not defined, nothing copied).</summary>
    public string MediaRegisterSha256 { get; set; } = "";
    public string UsersRegisterSha256 { get; set; } = "";
    /// <summary>What the report says about the registers: each defined (with the SHA-256 of its copy) or "nedefinit" (never "conform").</summary>
    public string RegisterLine { get; set; } = "Registre: nedefinite (nu au fost folosite).";
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
/// The full investigation, as done by hand on a development PC: acquire (or import) evidence into a case, parse it with the
/// real parsers, build one timeline, correlate, record findings and evidence gaps. Every step leaves an audit trail.
/// </summary>
public sealed class InvestigationPipeline
{
    private const string Producer = "InvestigationPipeline";

    /// <summary>
    /// Master plan 6.12: the approved, verified offline threat-intelligence package of the station, loaded as one more rules root (null = none).
    /// The caller verifies it (signature, signer, file hashes) right before the run; a package that did not verify arrives with only a Problem.
    /// </summary>
    public LogAnalyzer.Dfir.Detection.ActiveThreatIntel? ThreatIntel { get; init; }
    private readonly ParserRegistry _registry;

    public InvestigationPipeline(ParserRegistry? registry = null) => _registry = registry ?? WindowsParsers.Registry;

    public static IReadOnlyList<ICollector> AllCollectors { get; } =
        [new LiveStateCollector(), new EventLogCollector(), new ArchivedLogsCollector(), new PrefetchCollector(), new ExecutionArtifactsCollector(), new SrumCollector(), new AirGapFactsCollector(), new FirewallLogCollector(), new UserArtifactsCollector(), new UsnJournalCollector(), new SetupApiLogCollector(), new RecentItemsCollector()];

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
            // E1: an archived log ("Archive-Security-<date>.evtx") is the Security channel; E1/E10: the origin (archived log, Windows.old, shadow copy) goes into the notes and custody.
            var source = type == "evtx" ? LogAnalyzer.Dfir.Windows.Acquisition.HistoricalSources.ChannelFromFileName(f) : type;
            var origin = LogAnalyzer.Dfir.Analysis.EvidenceOrigin.Label(f);
            list.Add(ws.ImportFile(f, "Import:" + source, type == "evtx" ? "EventLog:" + source : type, TemporalType.Historical, "Import", DfirInfo.ApplicationVersion,
                notes: (origin.Length > 0 ? $"Sursă istorică ({origin}). " : "") + "Importat de operator din: " + f));
            if (origin.Contains("VSS", StringComparison.Ordinal)) ws.Audit("vss.import", $"{list[^1].EvidenceId} din {f}");
        }
        return list;
    }

    /// <summary>The evidence type <see cref="Import"/> gives a file. Every file is evidence; one with no known type is "file" (hash IOCs, YARA). WP18: the intake screen uses the same rule.</summary>
    public static string ImportType(string f)
    {
            // A Recycle Bin $I record keeps the original extension ($IAB12CD.lnk, $IAB12CD.evtx): it is checked before the extension, by name and
            // content, or a deleted shortcut or event log would be typed by its extension and its deletion record lost (review round 2).
            if (Path.GetFileName(f).StartsWith("$I", StringComparison.OrdinalIgnoreCase) && EvidenceFingerprint.Detect(f) == "recycle_info") return "recycle_info";
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext switch
            {
                ".evtx" => "evtx", ".pf" => "prefetch", ".pcapng" => "pcapng", ".lnk" => "lnk",
                _ when f.EndsWith(".automaticDestinations-ms", StringComparison.OrdinalIgnoreCase) => "jumplist_auto",
                _ when f.EndsWith(".customDestinations-ms", StringComparison.OrdinalIgnoreCase) => LogAnalyzer.Dfir.FileSystem.CustomJumpListParser.SourceTypeName,
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
        if (n is "USRCLASS.DAT" or "USRCLASS.HIV" || n.StartsWith("USRCLASS", StringComparison.Ordinal) && n.EndsWith(".HIV", StringComparison.Ordinal)) return "usrclass_hive";
        return null;
    }

    /// <param name="mediaRegister">WP14a: the media register to compare observed media with. When null the register installed on this machine
    /// (%PROGRAMDATA%\LogAnalyzer\registers) is loaded if there is one; without rows it is "registru nedefinit".</param>
    /// <param name="usersRegister">WP14a: users and clearances, loaded like the media register.</param>
    /// <param name="systemZone">WP14a: the zone of the analysed system, as the operator declares it ("" = not declared).</param>
    /// <param name="procedureProfile">The procedure profile to use. When null the profile installed on this machine (%PROGRAMDATA%\LogAnalyzer\profile) is loaded
    /// if there is one; with none, behaviour is exactly as before the profile existed (log clears are NotAssessed).</param>
    public InvestigationResult Run(CaseWorkspace ws, CollectionProfile profile, bool collect, IProgress<string>? progress = null, CancellationToken ct = default, ProcedureProfile? procedureProfile = null,
        MediaRegister? mediaRegister = null, UsersRegister? usersRegister = null, string systemZone = "", LogAnalyzer.Dfir.HashSets.HashSetLibrary? hashSets = null)
    {
        var r = new InvestigationResult { Case = ws, State = OperationState.Running, StateReason = "Analiza rulează." };
        try { return RunCore(r, ws, profile, collect, progress, ct, procedureProfile, mediaRegister, usersRegister, systemZone, hashSets); }
        catch (OperationCanceledException) { MarkEnded(r, ws, OperationState.Cancelled, "Oprit de operator; rezultatele parțiale nu sunt complete."); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { MarkEnded(r, ws, OperationState.Failed, ex.Message); throw; }
        // evaluation 2026-11-05: a defect (ArgumentException in the evidence graph) left the case without run_state.json, read later as
        // "starea analizei este necunoscută"; the run is recorded as failed with what happened, then the exception goes on to the caller
        catch (Exception ex) when (ex is not OutOfMemoryException) { MarkEnded(r, ws, OperationState.Failed, $"Eroare internă în analiză ({ex.GetType().Name}): {ex.Message}"); throw; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Security audit #17: an evidence index entry whose stored path resolves outside the case folder is reported and audited, never read.</summary>
    private static EvidenceGap OutsideCaseGap(CaseWorkspace ws, EvidenceItem ev)
    {
        ws.Audit("evidence.path_outside_case", $"{ev.EvidenceId} {ev.StoredPath}");
        return new EvidenceGap(ev.EvidenceId, EvidenceStatus.Failed, CaseWorkspace.OutsideCaseReason,
            "Proba nu a fost citită: indexul probelor indică un fișier din afara cazului (index modificat sau caz importat necorespunzător)",
            "Verificați indexul probelor și reimportați proba în caz", "Doar prin reachiziție");
    }

    /// <summary>The newest facts file of the case (adapters, burn tooling, printers, screenshots, virtual machines, archivers, camera / microphone), checked against its acquisition hash first; null when the case has none or it is unreadable.</summary>
    private static AirGapFacts? LoadAirGapFacts(CaseWorkspace ws, InvestigationResult r)
    {
        var item = ws.LoadEvidence().Where(e => e.SourceType == AirGapFacts.SourceType).OrderByDescending(e => e.AcquiredAtUtc).FirstOrDefault();
        if (item is null) return null;
        if (!ws.TryFullPath(item.StoredPath, out var full))
        {
            r.Gaps.Add(OutsideCaseGap(ws, item));
            return null;
        }
        var pre = EvidencePreflight.Check(item, full);
        if (!pre.CanParse)
        {
            r.Gaps.Add(new EvidenceGap(Path.GetFileName(item.StoredPath), EvidenceStatus.Failed, $"{pre.Code}: {pre.Detail}", "Constatările despre adaptoare, imprimante, mașini virtuale, arhivatoare și echipamente lipsesc", "O nouă colectare a faptelor stației", "Doar prin reachiziție"));
            ws.Audit(pre.Code == "EVIDENCE_MUTATED" ? "evidence.mutated" : "evidence.preflight_failed", $"{item.EvidenceId} {pre.Code} {pre.Detail}");
            return null;
        }
        try
        {
            var facts = AirGapFacts.FromJson(File.ReadAllText(full));
            facts.EvidenceId = item.EvidenceId;
            return facts;
        }
        catch (JsonException ex)
        {
            r.Gaps.Add(new EvidenceGap(Path.GetFileName(item.StoredPath), EvidenceStatus.Failed, ex.Message, "Faptele stației nu au putut fi citite", "O nouă colectare", "Doar prin reachiziție"));
            return null;
        }
    }

    /// <summary>Why a transaction-log companion is not parsed on its own (evaluation 2026-11-05: „niciun parser înregistrat” read like a defect).</summary>
    internal static string? AuxiliaryNote(string sourceType) => sourceType switch
    {
        LogAnalyzer.Dfir.Windows.Acquisition.SrumCollector.SrumLogSourceType =>
            "jurnal de tranzacții ESE al bazei SRUM: nu se citește separat; parserul SRUM îl folosește la recuperarea unei copii „murdare”",
        "amcache_log" => "jurnal de tranzacții al hive-ului Amcache: păstrat în caz ca probă; reaplicarea lui pe hive nu este încă implementată (intrările scrise doar în jurnal pot lipsi)",
        _ => null,
    };

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

    /// <summary>
    /// Writes the acquisition run record. A failure is audited and shown as a gap; it never throws, so it can neither mask the exception that ended
    /// the run nor fail an otherwise good run.
    /// </summary>
    internal static void CompleteRunRecord(AcquisitionRunRecorder recorder, CaseWorkspace ws, InvestigationResult r, OperationState state, string reason)
    {
        try { recorder.Complete(state, reason); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try { ws.Audit("acquisition.run_record_failed", $"run={recorder.RunId} {ex.GetType().Name}: {ex.Message}"); }
            catch (Exception ex2) when (ex2 is not OutOfMemoryException) { /* audit unavailable: the gap below still tells the operator; nothing may escape the finally */ }
            r.Gaps.Add(new EvidenceGap("Fișa colectării", EvidenceStatus.Failed, ex.Message, "Fișa colectării nu a putut fi scrisă; colectarea nu are înregistrare per rulare",
                "Repetarea colectării sau a scrierii fișei", "Posibil"));
        }
    }

    private InvestigationResult RunCore(InvestigationResult r, CaseWorkspace ws, CollectionProfile profile, bool collect, IProgress<string>? progress, CancellationToken ct, ProcedureProfile? procedureProfile = null,
        MediaRegister? mediaRegister = null, UsersRegister? usersRegister = null, string systemZone = "", LogAnalyzer.Dfir.HashSets.HashSetLibrary? hashSets = null)
    {
        bool elevated = OperatingSystem.IsWindows() && IsAdministrator();
        var startedUtc = DateTimeOffset.UtcNow;   // master plan 6.16: the run manifest records when the run began
        var recorder = collect ? AcquisitionRunRecorder.Begin(ws, profile.ToString(), elevated) : null;
        ws.Audit("investigation.start", $"profile={profile} collect={collect} elevated={elevated}" + (recorder is null ? "" : $" run={recorder.RunId}"));

        // 1. Acquisition. The run record is written when the loop ends, whatever way it ends.
        if (recorder is not null)
        {
            var runState = OperationState.Completed;
            var runReason = "";
            try
            {
                foreach (var c in AllCollectors.Where(c => (c.Profiles & profile) != 0))
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Colectare: {c.Name}");
                    var start = DateTimeOffset.UtcNow;
                    CollectorOutcome o;
                    try { o = c.Collect(new CollectorContext { Case = ws, IsElevated = elevated }, ct); }
                    catch (OperationCanceledException)
                    {
                        // Whatever the collector had already stored belongs to the record even though it never returned an outcome.
                        recorder.AddInterrupted(c.Name, c.Version, EvidenceStatus.Partial, "colectare întreruptă (anulată)", start, DateTimeOffset.UtcNow);
                        throw;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        o = new CollectorOutcome { Status = EvidenceStatus.Failed, Errors = { ex.Message } };
                        o.Evidence.AddRange(AcquisitionRunRecorder.EvidenceSince(ws, c.Name, start));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // Review round 2: an unexpected failure still ends the run, but the evidence already stored is not left out of the record.
                        recorder.AddInterrupted(c.Name, c.Version, EvidenceStatus.Failed, $"{ex.GetType().Name}: {ex.Message}", start, DateTimeOffset.UtcNow);
                        throw;
                    }
                    var end = DateTimeOffset.UtcNow;
                    var manifest = recorder.Add(c.Name, c.Version, o.Tool, o.ToolVersion, o.Status, o.Errors, o.Commands, o.Evidence, start, end);
                    r.Collection.Add(new CollectionRow(c.Name, o.Status, o.Evidence.Count, string.Join("; ", o.Errors), start, end));
                    ws.RecordCollection(new CollectionAuditRecord(c.Name, start, end, o.Status, o.ExitCode, o.Source, ws.Root, o.Evidence.Sum(e => e.Size),
                        manifest, o.Tool, o.ToolVersion, string.Join(" && ", o.Commands), string.Join("; ", o.Errors)));
                    if (o.Status is EvidenceStatus.NotAvailable or EvidenceStatus.Failed)
                        r.Gaps.Add(new EvidenceGap(c.Name, o.Status, string.Join("; ", o.Errors), "Sursa lipsește din analiză",
                            c.RequiresElevation ? "Rulare ca administrator" : "Import manual al probei", elevated ? "Posibil" : "Cu drepturi de administrator"));
                    else if (o.Errors.Count > 0)
                        r.Gaps.Add(new EvidenceGap(c.Name, EvidenceStatus.Partial, string.Join("; ", o.Errors.Take(8)), "Unele surse lipsesc", "Rulare ca administrator / import", "Posibil"));
                }
            }
            catch (OperationCanceledException) { runState = OperationState.Cancelled; runReason = "colectare anulată de operator"; throw; }
            catch (Exception ex) { runState = OperationState.Failed; runReason = ex.GetType().Name + ": " + ex.Message; throw; }
            finally { CompleteRunRecord(recorder, ws, r, runState, runReason); }
            r.LoadAcquisitionRuns(ws, ct);   // after the finally: a cancelled or failed run keeps its own exception
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
            if (!ws.TryFullPath(ev.StoredPath, out var full))
            {
                r.Parsing.Add(new ParseResult
                {
                    EvidenceId = ev.EvidenceId, Parser = "-", ParserVersion = "-", Status = EvidenceStatus.Failed,
                    Error = CaseWorkspace.OutsideCaseReason, ExpectedSha256 = ev.Sha256,
                });
                r.Gaps.Add(OutsideCaseGap(ws, ev));
                continue;
            }
            var sel = _registry.Select(ev, full);
            if (!sel.HasCandidate)
            {
                // Live snapshots are read by LiveStateAnalyzer below; anything else is listed, never silently ignored.
                if (ev.SourceType is not ("live_snapshot" or "airgap_facts"))
                    r.Parsing.Add(new ParseResult
                    {
                        EvidenceId = ev.EvidenceId, Parser = "-", ParserVersion = "-", Status = EvidenceStatus.SkippedByDesign,
                        Error = AuxiliaryNote(ev.SourceType) ?? sel.Problem, ExpectedSha256 = ev.Sha256,
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
                // An empty or unexpected-format file that still matches its acquisition hash is the acquired file, only not interpreted.
                r.Gaps.Add(pre0.Status == PreflightStatus.FormatMismatch
                    ? new EvidenceGap(Path.GetFileName(ev.StoredPath), EvidenceStatus.Failed, refused.Error,
                        char.ToUpperInvariant(pre0.FormatNotice[0]) + pre0.FormatNotice[1..], "Verificați sursa originală; dacă acolo fișierul are conținut, reachiziție", "Doar prin reachiziție")
                    : new EvidenceGap(Path.GetFileName(ev.StoredPath), EvidenceStatus.Failed, refused.Error,
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
                // Derived files a parser kept in the case (e.g. a repaired EVTX, security audit #14) are hashed into custody, linked to their evidence.
                foreach (var derived in pr.DerivedOutputs)
                {
                    try { ws.RecordOutput(derived, parser.Descriptor.ParserId, parser.Descriptor.Version, [ev.EvidenceId]); }
                    catch (Exception dex) when (dex is ArgumentException or IOException or UnauthorizedAccessException)
                    { r.Gaps.Add(new EvidenceGap(derived, EvidenceStatus.Partial, $"proba derivată nu a putut fi înregistrată în custodie: {dex.Message}", "Copia derivată nu are SHA-256 în lanțul de custodie", "Rularea din nou a analizei", "Posibil")); }
                }
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
                ws.RecordTransformation(ev.EvidenceId, parser.Descriptor.ParserId, parser.Descriptor.Version, "Analysis/timeline.csv", $"{LogAnalyzer.Dfir.Language.RoText.Count(pr.Records, "înregistrare", "înregistrări")}, stare {LogAnalyzer.Dfir.Language.RoLabels.Source(pr.Status)} ({pr.Status.ToSpec()})");
                r.Gaps.AddRange(pr.Gaps);
                if (pr.Status is EvidenceStatus.Failed or EvidenceStatus.Partial)
                    r.Gaps.Add(new EvidenceGap(Path.GetFileName(ev.StoredPath), pr.Status, pr.Error.Length > 0 ? pr.Error : $"{LogAnalyzer.Dfir.Language.RoText.Count(pr.MalformedRecords, "înregistrare", "înregistrări")} corupte",
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
        progress?.Report("Suporturi de stocare amovibile și integritate air-gap");
        int wp14No = LiveStateAnalyzer.MaxFindingNo(found);
        var airGapFacts = LoadAirGapFacts(ws, r);
        r.FactsRemovableLetters.AddRange(airGapFacts?.RemovableLetters ?? []);
        var wp14 = Wp14Analysis.Run(ws, r.Timeline, () => $"F-{++wp14No:D4}", mediaRegister, usersRegister, procedureProfile, systemZone, facts: airGapFacts);
        found.AddRange(wp14.Findings);
        r.MediaRegisterSha256 = wp14.MediaSha256; r.UsersRegisterSha256 = wp14.UsersSha256; r.RegisterLine = wp14.Line;
        foreach (var live in ws.LoadEvidence().Where(e => e.SourceType == "live_snapshot"))
        {
            if (!ws.TryFullPath(live.StoredPath, out var liveFull)) { r.Gaps.Add(OutsideCaseGap(ws, live)); continue; }
            var pre = EvidencePreflight.Check(live, liveFull);
            if (!pre.CanParse)
            {
                r.Gaps.Add(new EvidenceGap(Path.GetFileName(live.StoredPath), EvidenceStatus.Failed, $"{pre.Code}: {pre.Detail}",
                    "Constatările din starea live lipsesc", "Un nou instantaneu al stării live", "Doar prin reachiziție"));
                ws.Audit(pre.Code == "EVIDENCE_MUTATED" ? "evidence.mutated" : "evidence.preflight_failed", $"{live.EvidenceId} {pre.Code} {pre.Detail}");
                continue;
            }
            found.AddRange(LiveStateAnalyzer.Analyze(live, liveFull, LiveStateAnalyzer.MaxFindingNo(found)).ToList());   // evaluation 2026-11-05: found.Count collided with a reserved id (F-0305 twice -> EvidenceGraph crash)
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
        int lastFindingNo = LiveStateAnalyzer.MaxFindingNo(found);
        found.AddRange(policyTimeline.ToFindings(() => $"F-{++lastFindingNo:D4}"));
        r.PolicyTimeline = policyTimeline;
        // WP14b: combined sequences (control gap + media, SMB + staging + USB, portable software + archive + USB) over the findings and rows above; they add no second detection.
        progress?.Report("Secvențe combinate");
        int sequenceNo = LiveStateAnalyzer.MaxFindingNo(found);
        var sequences = SequenceRules.Run(r.Timeline, found, () => $"F-{++sequenceNo:D4}", new SequenceInput { Scope = ws.Info.Scope });
        found.AddRange(sequences);
        // R7a: fingerprint sets imported by the operator. Known-bad -> high finding; known-good -> label only (Analysis/known_hashes.json), the item stays in the case.
        var hashSetList = (hashSets ?? new LogAnalyzer.Dfir.HashSets.HashSetLibrary()).Load(out var hashSetProblems);
        foreach (var hp in hashSetProblems) { r.Gaps.Add(new EvidenceGap("Set de amprente", EvidenceStatus.Failed, hp, "Setul nu a fost folosit la potrivire", "Reimportați setul", "Da")); ws.Audit("hashset.problem", hp); }
        // Coverage, said plainly: the case's samples are compared by SHA-256 only; a set of MD5 / SHA-1 only reaches the event fields that carry those hashes.
        foreach (var hs in hashSetList.Where(h => !h.Info.CoversEvidenceSamples))
            r.Gaps.Add(new EvidenceGap("Set de amprente", EvidenceStatus.Partial, $"Setul „{hs.Info.Name}” conține doar {string.Join(", ", hs.Info.Algorithms)}. {LogAnalyzer.Dfir.HashSets.HashSetMatcher.CoverageNote}",
                "Probele din caz (fișierele importate) nu sunt comparate cu acest set", "Folosiți un set cu SHA-256 pentru probele din caz", "Da"));
        var hashMatches = LogAnalyzer.Dfir.HashSets.HashSetMatcher.Match(hashSetList, ws.LoadEvidence(), r.Timeline);
        int hashFindingNo = LiveStateAnalyzer.MaxFindingNo(found);
        found.AddRange(LogAnalyzer.Dfir.HashSets.HashSetMatcher.ToFindings(hashMatches, () => $"F-{++hashFindingNo:D4}"));
        r.KnownHashMatches.AddRange(hashMatches);
        // Master plan 6.6: rules over Volatility rows and the memory/disk correlation, from the case timeline alone.
        int memFindingNo = LiveStateAnalyzer.MaxFindingNo(found);
        var memFindings = LogAnalyzer.Dfir.Memory.MemoryFindings.Run(r.Timeline, () => $"F-{++memFindingNo:D4}");
        found.AddRange(memFindings.Findings);
        r.Gaps.AddRange(memFindings.Gaps);
        if (collect)
        {
            // evaluation 2026-11-05: a signed per-user program is checked on this station before it is reported as high (see SignatureCalibration)
            // review 2026-11-05: only findings whose every piece of evidence was collected live on this host (not imported, not remote)
            var liveHere = ws.LoadEvidence().Where(e => e.Collector is { Length: > 0 } c && c != "Import" && !c.Contains("remote package", StringComparison.OrdinalIgnoreCase)
                                                 && !string.Equals(e.AcquisitionMethod, "imported", StringComparison.OrdinalIgnoreCase)   // review round 2 LOW-1: Host is the case host for every item
                                                 && string.Equals(e.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                             .Select(e => e.EvidenceId).ToHashSet(StringComparer.Ordinal);
            var calibrated = SignatureCalibration.Apply(found, File.Exists, Native.Authenticode.Verify, ArtifactPathResolver.ForThisStation().Resolve,
                f => f.SupportingEvidence.Count > 0 && f.SupportingEvidence.All(e => liveHere.Contains(e.EvidenceId))).ToList();
            int lowered = calibrated.Where((x, i) => x.Severity != found[i].Severity).Count();
            if (lowered > 0) ws.Audit("finding.severity_calibrated", $"{lowered} constatări din locații în care utilizatorii pot scrie coborâte cu un nivel: fișier semnat valid, verificat pe stație, probe colectate pe stație");
            found = calibrated;
        }
        var (kept, rejected) = ProvenanceBinder.BindFindings(found, ws.LoadEvidence().ToDictionary(e => e.EvidenceId, StringComparer.Ordinal));
        LogAnalyzer.Dfir.Case.ClockMemory.Refresh();
        TimeReliability.Apply(kept, r.Timeline, LogAnalyzer.Dfir.Case.ClockMemory.StartupWarning.Length > 0);
        // Finding contract: semantic type, standard state, limitations, provenance, audit trail (added beside Classification).
        var parsersByEvidence = r.Parsing.Where(p => p.Parser != "-" && p.Status is EvidenceStatus.Success or EvidenceStatus.Partial or EvidenceStatus.Empty)
                                         .GroupBy(p => p.EvidenceId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(p => p.Parser).Distinct().ToList(), StringComparer.Ordinal);
        // the plain sentence names the person an account belongs to (users register) and tells the time in the station's zone
        kept = FindingContract.Enrich(kept, new FindingContractContext { ParsersOf = e => parsersByEvidence.GetValueOrDefault(e) ?? [], NowUtc = DateTimeOffset.UtcNow,
            // the saved register when the caller passes none, as the media rules do (Wp14Analysis): the app passed none, so no person was ever named
            PersonOf = (usersRegister ?? LogAnalyzer.Dfir.Registers.RegisterStore.Load<LogAnalyzer.Dfir.Registers.UsersRegister>().Register) is { } ur ? ur.PersonOf : null, Zone = TimeZoneInfo.Local,
            AccountOfSid = LogAnalyzer.Dfir.Language.AccountResolver.FromEvents(r.Timeline) }).ToList();
        r.Findings.AddRange(kept);
        foreach (var dup in kept.GroupBy(x => x.FindingId, StringComparer.Ordinal).Where(x => x.Count() > 1))
        {
            // numbering defect, said plainly instead of crashing later (EvidenceGraph, report): the findings stay, the id is ambiguous
            ws.Audit("finding.duplicate_id", $"{dup.Key} x{dup.Count()}: {string.Join(", ", dup.Select(x => x.RuleId))}");
            r.Gaps.Add(new EvidenceGap("Numerotarea constatărilor", EvidenceStatus.Partial, $"Identificatorul {dup.Key} a fost dat la {dup.Count()} constatări ({string.Join(", ", dup.Select(x => x.RuleId))})",
                "Trimiterile între constatări după acest identificator pot fi ambigue", "Raportați defectul; reluați analiza după actualizare", "Da"));
        }
        r.RejectedFindings.AddRange(rejected);
        foreach (var x in rejected) ws.Audit("finding.rejected", $"{x.Finding.FindingId} {x.Finding.RuleId}: {x.Reason}");

        // 4. Outputs, kept in the case and hashed into custody.
        progress?.Report("Scriere rezultate");
        // WP3b: every file written below is registered in the custody chain with its SHA-256 and the evidence it rests on.
        var evidenceIds = ws.LoadEvidence().Select(e => e.EvidenceId).ToList();
        r.TimelineCsv = Path.Combine(analysisDir, "timeline.csv");
        TimelineCsv.Write(r.TimelineCsv, r.Timeline);
        ws.RecordOutput("Analysis/timeline.csv", Producer, DfirInfo.ApplicationVersion, evidenceIds);
        // E7: what the user-activity view needs after the case is reopened (timeline.csv does not keep the parser fields, the profile or the station's removable letters).
        r.ActivityFolders = [.. r.Procedure?.ClassifiedFolders.Select(f => new LogAnalyzer.Dfir.Profile.ClassifiedFolderRow { Path = f.Path, Note = f.Note }) ?? []];
        LogAnalyzer.Dfir.Analysis.UserActivityContext.From(r.Timeline, r.ActivityFolders, r.FactsRemovableLetters).Write(Path.Combine(analysisDir, "user_activity.json"));
        ws.RecordOutput(LogAnalyzer.Dfir.Analysis.UserActivityContext.RelPath, Producer, DfirInfo.ApplicationVersion, evidenceIds);
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
        var engine = DetectionEngine.Load(Path.Combine(AppContext.BaseDirectory, "Rules"), Path.Combine(ws.Root, "Rules"), ThreatIntel?.RulesRoot ?? "");
        if (ThreatIntel is { RulesRoot: null, Problem: { Length: > 0 } tiProblem } && !tiProblem.StartsWith("niciun pachet", StringComparison.Ordinal))
            r.Gaps.Add(new EvidenceGap("Pachetul de informații despre amenințări", EvidenceStatus.Failed, tiProblem,
                "Regulile pachetului nu au fost folosite la această analiză", "Verificați pachetul în pagina Politici și reluați analiza", "Da"));
        else if (ThreatIntel?.RulesRoot is not null)
        {
            ws.Audit("threat_intel.used", $"{ThreatIntel.PackageId} v{ThreatIntel.Version}");
            if (ThreatIntel.Problem.Length > 0)   // used, but said: an expired package
                r.Gaps.Add(new EvidenceGap("Pachetul de informații despre amenințări", EvidenceStatus.Partial, ThreatIntel.Problem,
                    "Regulile pachetului au fost folosite, dar pot fi depășite", "Un pachet nou de la publicator", "Da"));
        }
        engine.KnownHashSets = hashSetList;
        var evidenceItems = ws.LoadEvidence();
        var scannable = new List<EvidenceItem>();
        foreach (var e in evidenceItems)
        {
            if (!ws.TryFullPath(e.StoredPath, out _)) continue; // already reported as a gap by the parsing loop (audit #17)
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
        File.WriteAllText(Path.Combine(analysisDir, "known_hashes.json"), JsonSerializer.Serialize(r.KnownHashMatches.Select(m => new { m.SetName, m.Category, m.Label, m.SetSourceFile, m.SetSourceSha256, m.EvidenceId, m.Path, m.Algorithm, m.Hash, m.Source }), new JsonSerializerOptions { WriteIndented = true }));
        ws.RecordOutput("Analysis/known_hashes.json", "HashSetMatcher", "1.0", evidenceIds);
        ws.RecordOutput("Analysis/detections.json", "DetectionEngine", "1.0", evidenceIds);
        ws.RecordOutput("Analysis/rules.json", "DetectionEngine", "1.0");
        ws.RecordTransformation("CASE", "DetectionEngine", "1.0", "Analysis/detections.json", $"{r.Detections.Count} potriviri, {r.RulesUsed.Count} reguli");
        // Master plan 6.7: every evidence file that is a Windows executable is read statically (never run) and its signature is checked on this
        // station (embedded or catalog, cache only, no network). Observations with their reasons, not verdicts.
        progress?.Report("Triaj executabile");
        foreach (var e in evidenceItems)
        {
            if (!ws.TryFullPath(e.StoredPath, out var full) || !File.Exists(full)) continue;
            try
            {
                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                    if (fs.Length < 64 || fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') continue;
                var pe = LogAnalyzer.Dfir.Detection.PeTriage.Analyze(full);
                if (!pe.IsPe)
                {
                    // starts like an executable but cannot be read as one: malformed programs are the interesting ones, so it is said, not dropped
                    r.Gaps.Add(new EvidenceGap(e.OriginalName, EvidenceStatus.Partial, pe.Problem, "Fișierul începe ca un executabil Windows, dar nu a putut fi citit ca unul",
                        "Analiză manuală într-un mediu izolat", "Posibil"));
                    continue;
                }
                var sig = OperatingSystem.IsWindows() ? LogAnalyzer.Dfir.Windows.Native.Authenticode.Verify(full) : new LogAnalyzer.Dfir.Windows.Native.SignatureInfo(false, false, "none", "neverificat (nu este Windows)", "");
                r.PeTriage.Add(new LogAnalyzer.Dfir.Detection.PeTriageRow(e.EvidenceId, e.OriginalName, pe, sig.Kind, sig.Status, sig.Signer, sig.IsValid));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                r.Gaps.Add(new EvidenceGap(e.OriginalName, EvidenceStatus.Failed, ex.Message, "Executabilul nu a putut fi citit pentru triaj", "Reachiziție", "Posibil"));
            }
        }
        // written also when empty, so a run with no executables never leaves an earlier run's list behind
        {
            LogAnalyzer.Dfir.Detection.PeTriageFile.Write(Path.Combine(analysisDir, "pe_triage.json"), r.PeTriage);
            ws.RecordOutput(LogAnalyzer.Dfir.Detection.PeTriageFile.RelPath, "PeTriage", "1", r.PeTriage.Select(p => p.EvidenceId));
        }
        // Master plan 6.10: identities by SID across DCs, servers and workstations, written always so a reopened case (whose timeline.csv has no
        // event fields) still has them.
        progress?.Report("Identități și sesiuni");
        r.Identity = LogAnalyzer.Dfir.Identity.IdentityReconstruction.Build(r.Timeline);
        LogAnalyzer.Dfir.Identity.IdentityReport.Write(Path.Combine(analysisDir, "identity.json"), r.Identity);
        ws.RecordOutput(LogAnalyzer.Dfir.Identity.IdentityReport.RelPath, "IdentityReconstruction", LogAnalyzer.Dfir.Identity.IdentityReconstruction.Version, evidenceIds);
        ws.RecordTransformation("CASE", "IdentityReconstruction", LogAnalyzer.Dfir.Identity.IdentityReconstruction.Version, LogAnalyzer.Dfir.Identity.IdentityReport.RelPath,
            $"{r.Identity.Identities.Count} identități, {r.Identity.Sessions.Count} sesiuni, {r.Identity.DirectoryChanges.Count} modificări de director, {r.Identity.DuplicatesDropped} duplicate eliminate");
        r.AntiForensics.AddRange(LogAnalyzer.Dfir.Analysis.AntiForensics.Evaluate(r.Timeline, r.Gaps, maintenance,
            found.Where(x => x.Severity >= Severity.High && x.RuleId != "LOG-TAMPER" && (x.FirstSeenUtc ?? x.LastSeenUtc) is not null).Select(x => (x.FirstSeenUtc ?? x.LastSeenUtc)!.Value).ToList()));
        File.WriteAllText(Path.Combine(analysisDir, "anti_forensics.json"), JsonSerializer.Serialize(r.AntiForensics, new JsonSerializerOptions { WriteIndented = true }));
        ws.RecordOutput("Analysis/anti_forensics.json", "AntiForensics", "1.0", evidenceIds);
        ws.RecordTransformation("CASE", "AntiForensics", "1.0", "Analysis/anti_forensics.json",
            $"{r.AntiForensics.Count(c => c.Result == AntiForensicResult.Detected)} detectate, {r.AntiForensics.Count(c => c.Result == AntiForensicResult.Undetermined)} nedeterminate (verificări de ascundere a urmelor)");
        File.WriteAllText(Path.Combine(analysisDir, "policy_timeline.json"), policyTimeline.ToJson());
        ws.RecordOutput("Analysis/policy_timeline.json", "PolicyTimeline", "1.0", evidenceIds);
        ws.RecordTransformation("CASE", "PolicyTimeline", "1.0", "Analysis/policy_timeline.json",
            $"{LogAnalyzer.Dfir.Language.RoText.Count(policyTimeline.Changes.Count, "modificare", "modificări")}, {policyTimeline.Applications.Count} aplicări, {policyTimeline.Gaps.Count} decalaje, {policyTimeline.TimeWindows.Count} ferestre de oră nesigură");
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
        // Master plan 6.16: the immutable run record (manifest, finding snapshot, diff with the previous run), BEFORE the anchor below so the anchor's
        // chain heads cover it (review 2026-11-08, P4). A failure of any kind is a gap, never a failed run (review P2: a duplicate rule id threw).
        try
        {
            static string Fingerprint(object? o) => o is null ? "nu" : "da " + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o))))[..16];
            var settings = new Dictionary<string, string>
            {
                ["profil_colectare"] = profile.ToString(), ["colectare"] = collect ? "da" : "nu", ["fus_sistem"] = systemZone,
                ["profil_proceduri"] = Fingerprint(r.Procedure), ["registru_medii"] = Fingerprint(mediaRegister), ["registru_utilizatori"] = usersRegister is null ? "nu" : "da",
                ["seturi_amprente"] = hashSets is null ? "nu" : "da",
                ["pachet_ti"] = ThreatIntel?.RulesRoot is not null ? $"{ThreatIntel.PackageId} v{ThreatIntel.Version}" : "nu",
            };
            var manifest = AnalysisRuns.Build(ws, startedUtc, r.Findings, r.Parsing, r.RulesUsed, r.Timeline.Count, state.ToSpec(), LogAnalyzer.Dfir.Analysis.FindingContract.Version, settings);
            (r.Run, r.RunDiff) = AnalysisRuns.Record(ws, manifest);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            try { ws.Audit("analysis.run_record_failed", $"{ex.GetType().Name}: {ex.Message}"); }
            catch (Exception ex2) when (ex2 is not OutOfMemoryException) { /* the gap below still tells the operator */ }
            r.Gaps.Add(new EvidenceGap("Fișa rulării analizei", EvidenceStatus.Failed, ex.Message, "Rularea nu are manifest imuabil; nu poate fi comparată cu altele",
                "Reluarea analizei", "Da"));
        }
        var schemaManifest = SchemaManifest.ForRun();
        schemaManifest.Anchor = ws.Anchor();   // chain heads: a copy held outside the case makes truncation at the end detectable
        schemaManifest.Write(Path.Combine(analysisDir, SchemaVersions.ManifestFile));
        ws.RecordOutput("Analysis/" + SchemaVersions.ManifestFile, Producer, DfirInfo.ApplicationVersion);
        ws.Audit("investigation.end", $"{r.Timeline.Count} events, {r.Findings.Count} findings, {r.Gaps.Count} gaps, state {state.ToSpec()}");
        r.LoadCustody(ws, CancellationToken.None);   // review round 2: never cancelled after investigation.end (a late cancel would rewrite a finished run as cancelled). Last: the tab shows every custody entry of this run (outputs, parsing), with or without collection
        return r;
    }
}

/// <summary>Findings from the live-state snapshot: what is running or set to start from user-writable locations, and unsigned.</summary>
public static class LiveStateAnalyzer
{
    /// <summary>Highest number among the "F-nnnn" ids already given; the next stage numbers from here. Correlation reserves ids ahead of
    /// building some findings, so the count of findings is not the next free number.</summary>
    public static int MaxFindingNo(IEnumerable<Finding> found) =>
        found.Select(x => x.FindingId.StartsWith("F-", StringComparison.Ordinal) && int.TryParse(x.FindingId.AsSpan(2), out var n) ? n : 0).DefaultIfEmpty(0).Max();

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
                Title = $"Proces nesemnat dintr-o locație în care utilizatorii pot scrie{(conns.Count > 0 ? ", cu conexiuni în Internet" : "")}: {p.Name}",
                Severity = conns.Count > 0 ? Severity.High : Severity.Medium, Category = "Execution", Classification = Classification.Direct,
                Confidence = conns.Count > 0 ? Confidence.High : Confidence.Medium, Process = p.Name, Pid = p.Pid, File = p.Path,
                Ip = string.Join(", ", conns.Take(10)), FirstSeenUtc = snap.TakenUtc,
                Description = $"PID {p.Pid}, părinte {p.Ppid}; linie de comandă: {p.CommandLine}" + (conns.Count > 0 ? $"; conexiuni: {string.Join(", ", conns.Take(10))}" : ""),
                ClassificationReason = "Observat în fotografia stării live (semnătură verificată, tabela TCP).",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Processes[pid={p.Pid}]", "instantaneu al stării live")],
                AlternativeExplanations = ["Aplicație legitimă instalată per utilizator, fără semnătură."],
                RecommendedNextSteps = ["Izolați programul din fila „Izolare procese suspecte” pentru scanare completă."],
            };
        }
        foreach (var s in snap.Services.Where(s => s.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-SERVICE-USERPATH", Title = $"Serviciu configurat dintr-o locație în care utilizatorii pot scrie: {s.Name}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1543.003",
                File = s.Path, User = s.Account, FirstSeenUtc = snap.TakenUtc,
                Description = $"{s.DisplayName}: {s.Path} ({s.StartMode}, {s.State}, cont {s.Account}).",
                ClassificationReason = "Configurația curentă a serviciilor.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Services[{s.Name}]", "instantaneu al stării live")],
                ContradictingEvidence = s.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? ["Serviciul este dezactivat (StartMode=Disabled): nu pornește singur."] : [],
            };
        foreach (var t in snap.Tasks.Where(t => t.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-TASK-USERPATH", Title = $"Activitate planificată care rulează dintr-o locație în care utilizatorii pot scrie: {Path.GetFileName(t.Path)}",
                Severity = Severity.High, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.High, MitreTechniqueId = "T1053.005",
                File = t.Command, FirstSeenUtc = snap.TakenUtc, Description = $"{t.Path}: {t.Command}",
                ClassificationReason = "Definiția curentă a activității planificate.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Tasks[{t.Path}]", "instantaneu al stării live")],
            };
        foreach (var a in snap.Autoruns.Where(a => a.UserWritable))
            yield return new Finding
            {
                FindingId = Id(), RuleId = "LIVE-AUTORUN-USERPATH", Title = $"Pornire automată dintr-o locație în care utilizatorii pot scrie: {a.Name}",
                Severity = Severity.Medium, Category = "Persistence", Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1547.001",
                File = a.Value, FirstSeenUtc = snap.TakenUtc, Description = $"{a.Location}\\{a.Name} = {a.Value}",
                ClassificationReason = "Cheile Run curente.",
                SupportingEvidence = [new EvidenceRef(item.EvidenceId, $"Autoruns[{a.Name}]", "instantaneu al stării live")],
                AlternativeExplanations = ["Multe aplicații per utilizator (actualizatoare, sincronizare) pornesc legitim din AppData."],
            };
    }
}
