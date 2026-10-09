using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// A case folder (spec §7) with its evidence index, chain of custody (§9), collection audit (§11)
/// and application audit log (§81). Raw evidence is stored once, hashed, and marked read-only.
/// </summary>
public sealed class CaseWorkspace
{
    public static readonly string[] Folders = ["Evidence", "Raw", "Parsed", "Derived", "Reports", "Exports", "Logs", "Hashes", "Tools"];

    private readonly object _gate = new();
    private int _evidenceCounter;
    private readonly HashChain _custodyChain;
    private readonly HashChain _auditChain;
    private CollectionContext? _context;
    private bool _adminWarned;

    public string Root { get; }
    public CaseInfo Info { get; private set; }
    public TimeZoneInfo Zone { get; }

    public string EvidenceIndexPath => Path.Combine(Root, "Evidence", "evidence_index.jsonl");
    public string CustodyCsvPath => Path.Combine(Root, "Logs", "chain_of_custody.csv");
    public string CustodyJsonlPath => Path.Combine(Root, "Logs", "chain_of_custody.jsonl");
    public string CollectionAuditPath => Path.Combine(Root, "Logs", "collection_audit.csv");
    public string AuditChainPath => Path.Combine(Root, "Logs", "audit_chain.jsonl");
    public string AppAuditLogPath => Path.Combine(Root, "Logs", "LogAnalyzer_Audit.log");

    private CaseWorkspace(string root, CaseInfo info)
    {
        Root = Path.GetFullPath(root);
        Info = info;
        Zone = TryZone(info.Timezone);
        _custodyChain = new HashChain(Path.Combine(Root, "Logs", "chain_of_custody.jsonl"));
        _auditChain = new HashChain(Path.Combine(Root, "Logs", "audit_chain.jsonl"));
        ScopeNote = info.Scope.MissingFields().Count > 0 ? "scop incomplet (caz vechi)"
                  : info.Scope.Provisional ? "scop provizoriu, neconfirmat de operator: aprobatorul, perioada și categoria sistemului trebuie confirmate"
                  : null;
        _evidenceCounter = Json.ReadLines<EvidenceItem>(EvidenceIndexPath).Count();
    }

    /// <summary>Set when the case has no complete scope (a case created before WP3: "scop incomplet (caz vechi)") or only a provisional one.</summary>
    public string? ScopeNote { get; }

    /// <summary>Who collects, from which machine, which source system and removable medium. Unknown parts are written as "necunoscut".</summary>
    public void SetCollectionContext(CollectionContext context) { lock (_gate) _context = context; }

    /// <summary>Whether the collecting account is a local administrator. Replaceable for tests.</summary>
    public Func<bool> IsLocalAdministrator { get; set; } = CollectionContext.CurrentUserIsLocalAdministrator;

    public static CaseWorkspace Create(string root, CaseInfo info)
    {
        var missing = info.Scope.MissingFields();
        if (missing.Count > 0) throw new CaseScopeIncompleteException(missing);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"Case folder is not empty: {root}");
        foreach (var f in Folders) Directory.CreateDirectory(Path.Combine(root, f));
        Json.Write(Path.Combine(root, "case.json"), info);
        var ws = new CaseWorkspace(root, info);
        ws.Audit("case.created", $"name={info.Name} host={info.Host}");
        if (info.Scope.Provisional) ws.Audit("case.scope_provisional", ws.ScopeNote!);
        return ws;
    }

    public static CaseWorkspace Open(string root)
    {
        var info = Json.Read<CaseInfo>(Path.Combine(root, "case.json"));
        var ws = new CaseWorkspace(root, info);
        ws.Audit("case.opened", "");
        if (ws.ScopeNote is { } note) ws.Audit(info.Scope.Provisional ? "case.scope_provisional" : "case.scope_incomplete", note);
        if (ws.RetentionWarning(DateTimeOffset.UtcNow) is { } warn) ws.Audit("case.retention_exceeded", warn);
        return ws;
    }

    public IReadOnlyList<EvidenceItem> LoadEvidence() => Json.ReadLines<EvidenceItem>(EvidenceIndexPath).ToList();

    public string FullPath(string storedPath) => Path.GetFullPath(Path.Combine(Root, storedPath));

    /// <summary>Folder for raw evidence of one logical source; collectors write here, then call <see cref="RegisterStored"/>.</summary>
    public string RawDir(string source)
    {
        var d = Path.Combine(Root, "Raw", SafeName(source));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Copies an external file into Raw/ (never moves the original) and registers it.</summary>
    public EvidenceItem ImportFile(string originalPath, string source, string sourceType, TemporalType temporal,
                                   string collector, string collectorVersion, Sensitivity sensitivity = Sensitivity.Confidential,
                                   string? parentEvidenceId = null, string notes = "")
    {
        var dir = RawDir(source);
        var dest = UniquePath(Path.Combine(dir, SafeName(Path.GetFileName(originalPath))));
        using (var src = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var dst = new FileStream(dest, FileMode.CreateNew, FileAccess.Write))
            src.CopyTo(dst);
        File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(originalPath));
        return RegisterStored(dest, originalPath, source, sourceType, temporal, collector, collectorVersion, "imported", sensitivity, parentEvidenceId, notes);
    }

    /// <summary>Registers a file already written inside the case (e.g. by wevtutil). Hashes it and makes it read-only.</summary>
    public EvidenceItem RegisterStored(string storedFullPath, string originalPath, string source, string sourceType, TemporalType temporal,
                                       string collector, string collectorVersion, string action = "acquired",
                                       Sensitivity sensitivity = Sensitivity.Confidential, string? parentEvidenceId = null, string notes = "",
                                       EvidenceStatus status = EvidenceStatus.Success)
    {
        var full = Path.GetFullPath(storedFullPath);
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Stored evidence must live inside the case: {full}");
        var fi = new FileInfo(full);
        var sha = Hashing.Sha256File(full);
        fi.Attributes |= FileAttributes.ReadOnly;

        EvidenceItem item;
        lock (_gate)
        {
            item = new EvidenceItem
            {
                EvidenceId = $"EV-{++_evidenceCounter:D6}",
                CaseId = Info.CaseId,
                Source = source,
                SourceType = sourceType,
                OriginalPath = originalPath,
                StoredPath = Path.GetRelativePath(Root, full),
                OriginalName = Path.GetFileName(originalPath),
                Size = fi.Length,
                Sha256 = sha,
                AcquiredAtUtc = DateTimeOffset.UtcNow,
                Host = Info.Host,
                User = Info.User,
                Timezone = Info.Timezone,
                Collector = collector,
                CollectorVersion = collectorVersion,
                AcquisitionMethod = action,
                ReadOnly = File.GetAttributes(full).HasFlag(FileAttributes.ReadOnly),
                Status = status,
                TemporalType = temporal,
                Sensitivity = sensitivity,
                ParentEvidenceId = parentEvidenceId,
                Notes = notes,
            };
            Json.AppendLine(EvidenceIndexPath, item);
        }
        WarnIfCollectorIsAdministrator();
        Custody(new CustodyEntry(DateTimeOffset.UtcNow, Environment.UserName, action, item.EvidenceId, originalPath, item.StoredPath, sha, collector, collectorVersion, "", ""));
        Audit("evidence.registered", $"{item.EvidenceId} {item.Source} {item.StoredPath} sha256={sha}");
        return item;
    }

    /// <summary>Records a parser or transformation applied to existing evidence (custody of derived data).</summary>
    public void RecordTransformation(string evidenceId, string parser, string parserVersion, string outputRelPath, string description)
    {
        var full = FullPath(outputRelPath);
        var sha = File.Exists(full) ? Hashing.Sha256File(full) : "";
        Custody(new CustodyEntry(DateTimeOffset.UtcNow, Environment.UserName, "parsed", evidenceId, "", outputRelPath, sha, parser, parserVersion, parser, description));
    }

    public void Custody(CustodyEntry e)
    {
        lock (_gate)
        {
            // Custody context (who/from where/which medium) goes to the chained JSONL only; the CSV keeps its original columns.
            var ctx = _context ?? CollectionContext.Default(Info.Host);
            if (string.IsNullOrEmpty(e.CollectorAccount) && e.Action is not ("state.transition" or "state.disposed" or "warning.collector_admin"))
                e = e with { CollectorAccount = ctx.Account, CollectorMachine = ctx.Machine, SourceSystem = ctx.SourceSystem, MediaId = ctx.MediaId };
            e = e with
            {
                CollectorAccount = Unknown(e.CollectorAccount), CollectorMachine = Unknown(e.CollectorMachine),
                SourceSystem = Unknown(e.SourceSystem), MediaId = Unknown(e.MediaId),
            };
            using (var w = new CsvWriter(CustodyCsvPath, CustodyEntry.Header, append: true)) w.WriteRow(e.Row());
            _custodyChain.Append(e);
        }
    }

    private static string Unknown(string s) => string.IsNullOrWhiteSpace(s) ? "necunoscut" : s;

    /// <summary>Warning, never a block: the collecting account is a local administrator of the audited system itself.</summary>
    private void WarnIfCollectorIsAdministrator()
    {
        var ctx = _context ?? CollectionContext.Default(Info.Host);
        lock (_gate)
        {
            if (_adminWarned) return;
            if (!string.Equals(ctx.Machine, Info.Host, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Info.Host)) return;
            if (!IsLocalAdministrator()) return;
            _adminWarned = true;
        }
        Custody(new CustodyEntry(DateTimeOffset.UtcNow, Environment.UserName, "warning.collector_admin", "", "", "", "", "", "", "",
            $"colectorul este administrator al sistemului auditat ({ctx.Account} pe {ctx.Machine})"));
        Audit("warning.collector_admin", "colectorul este administrator al sistemului auditat");
    }

    /// <summary>Verifies the hash chains of the custody log and of the audit chain (WP3a). Legacy logs are reported as such, never as valid.</summary>
    public ChainReport VerifyChains()
    {
        lock (_gate) return new ChainReport(_custodyChain.Verify(), _auditChain.Verify());
    }

    // ---- evidence lifecycle (owner decision 11): states only, no deletion anywhere ----

    private static readonly Dictionary<EvidenceState, EvidenceState> NextState = new()
    {
        [EvidenceState.Acquired] = EvidenceState.Verified,
        [EvidenceState.Verified] = EvidenceState.InAnalysis,
        [EvidenceState.InAnalysis] = EvidenceState.Archived,
    };

    private static string Label(EvidenceState s) => s switch
    {
        EvidenceState.Acquired => "ACQUIRED", EvidenceState.Verified => "VERIFIED", EvidenceState.InAnalysis => "IN_ANALYSIS",
        EvidenceState.Archived => "ARCHIVED", _ => "DISPOSED",
    };

    /// <summary>ACQUIRED → VERIFIED → IN_ANALYSIS → ARCHIVED, one step at a time. DISPOSED is reached only through <see cref="MarkDisposed"/>.</summary>
    public StateResult TransitionState(string evidenceId, EvidenceState to, string actor)
    {
        lock (_gate)
        {
            var all = LoadEvidence().ToList();
            var item = all.FirstOrDefault(e => e.EvidenceId == evidenceId);
            if (item is null) return StateResult.Refuse($"evidență necunoscută: {evidenceId}");
            if (to == EvidenceState.Disposed) return StateResult.Refuse("DISPOSED se setează doar manual, cu doi aprobatori (MarkDisposed)");
            if (item.State == EvidenceState.Disposed) return StateResult.Refuse("evidența este DISPOSED (stare finală)");
            if (!NextState.TryGetValue(item.State, out var allowed) || allowed != to)
                return StateResult.Refuse($"tranziție nepermisă {Label(item.State)} -> {Label(to)}" + (NextState.TryGetValue(item.State, out var a) ? $" (permis doar -> {Label(a)})" : ""));
            var from = item.State;
            item.State = to;
            RewriteEvidenceIndex(all);
            Custody(new CustodyEntry(DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(actor) ? Environment.UserName : actor, "state.transition", evidenceId, "", item.StoredPath,
                item.Sha256, "", "", "", $"{Label(from)}->{Label(to)}"));
            Audit("evidence.state", $"{evidenceId} {Label(from)}->{Label(to)}");
            return StateResult.Success;
        }
    }

    /// <summary>
    /// Marks evidence DISPOSED. This is only a mark: nothing is deleted. Requires two distinct approvers, state ARCHIVED, and no legal hold.
    /// </summary>
    public StateResult MarkDisposed(string evidenceId, string approver1, string approver2, string reason)
    {
        lock (_gate)
        {
            if (Info.LegalHold) return StateResult.Refuse("cazul are LegalHold: marcarea DISPOSED este refuzată");
            var a1 = (approver1 ?? "").Trim(); var a2 = (approver2 ?? "").Trim();
            if (a1.Length == 0 || a2.Length == 0) return StateResult.Refuse("sunt necesari doi aprobatori");
            if (string.Equals(a1, a2, StringComparison.OrdinalIgnoreCase)) return StateResult.Refuse("cei doi aprobatori trebuie să fie persoane distincte");
            var all = LoadEvidence().ToList();
            var item = all.FirstOrDefault(e => e.EvidenceId == evidenceId);
            if (item is null) return StateResult.Refuse($"evidență necunoscută: {evidenceId}");
            if (item.State != EvidenceState.Archived) return StateResult.Refuse($"DISPOSED se poate marca doar din ARCHIVED (acum {Label(item.State)})");
            item.State = EvidenceState.Disposed;
            RewriteEvidenceIndex(all);
            Custody(new CustodyEntry(DateTimeOffset.UtcNow, Environment.UserName, "state.disposed", evidenceId, "", item.StoredPath, item.Sha256, "", "", "",
                $"ARCHIVED->DISPOSED approvers={a1};{a2} reason={reason}".ReplaceLineEndings(" ")));
            Audit("evidence.disposed_mark", $"{evidenceId} approvers={a1};{a2}");
            return StateResult.Success;
        }
    }

    private void RewriteEvidenceIndex(List<EvidenceItem> items)
    {
        var tmp = EvidenceIndexPath + ".tmp";
        File.WriteAllLines(tmp, items.Select(i => System.Text.Json.JsonSerializer.Serialize(i, Json.Line)));
        File.Move(tmp, EvidenceIndexPath, overwrite: true);
    }

    public void SetLegalHold(bool on, string reason)
    {
        lock (_gate)
        {
            Info.LegalHold = on;
            Json.Write(Path.Combine(Root, "case.json"), Info);
        }
        Audit(on ? "case.legal_hold_set" : "case.legal_hold_released", reason);
    }

    public void SetRetention(DateTimeOffset? until)
    {
        lock (_gate)
        {
            Info.RetentionUntilUtc = until;
            Json.Write(Path.Combine(Root, "case.json"), Info);
        }
        Audit("case.retention_set", until?.ToString("O") ?? "(none)");
    }

    /// <summary>Warning text when the retention date has passed and evidence is not yet DISPOSED; otherwise null. The application never deletes anything.</summary>
    public string? RetentionWarning(DateTimeOffset now)
    {
        if (Info.RetentionUntilUtc is not { } until || now <= until) return null;
        var pending = LoadEvidence().Count(e => e.State != EvidenceState.Disposed);
        return $"perioada de retenție a expirat la {until:yyyy-MM-dd}; {pending} evidențe nu sunt marcate DISPOSED (nimic nu se șterge automat)" +
               (Info.LegalHold ? "; cazul are LegalHold" : "");
    }

    public void RecordCollection(CollectionAuditRecord r)
    {
        lock (_gate)
        {
            using var w = new CsvWriter(CollectionAuditPath, CollectionAuditRecord.Header, append: true);
            w.WriteRow(r.Row());
        }
        Audit("collector.finished", $"{r.Collector} status={r.Status.ToSpec()} exit={r.ExitCode} {r.Error}");
    }

    /// <summary>Application audit log (spec §81). Never pass secrets here.</summary>
    public void Audit(string action, string detail)
    {
        var now = DateTimeOffset.UtcNow;
        var d = detail.ReplaceLineEndings(" ");
        lock (_gate)
        {
            File.AppendAllText(AppAuditLogPath, $"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\t{Environment.UserName}\t{action}\t{d}\n");
            _auditChain.Append(new AuditEntry(now, Environment.UserName, action, d));
        }
    }

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray()).Trim(' ', '.');
        return string.IsNullOrEmpty(s) ? "_" : s;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!; var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        for (int i = 1; ; i++) { var p = Path.Combine(dir, $"{stem}_{i}{ext}"); if (!File.Exists(p)) return p; }
    }

    private static TimeZoneInfo TryZone(string id)
    {
        if (string.IsNullOrEmpty(id)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}

public sealed record AuditEntry(DateTimeOffset TimestampUtc, string Who, string Action, string Detail);

public sealed record StateResult(bool Ok, string Reason)
{
    public static readonly StateResult Success = new(true, "");
    public static StateResult Refuse(string reason) => new(false, reason);
}

/// <summary>Who collects, from which machine, which source system, which removable medium ("" = unknown, written as "necunoscut").</summary>
public sealed record CollectionContext(string Account, string Machine, string SourceSystem, string MediaId)
{
    public static CollectionContext Default(string auditedHost) => new(
        $"{Environment.UserDomainName}\\{Environment.UserName}", Environment.MachineName, auditedHost, "");

    /// <summary>True when the current process runs as a local administrator (Windows token check; false elsewhere).</summary>
    public static bool CurrentUserIsLocalAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or InvalidOperationException) { return false; }
    }
}

public sealed record CustodyEntry(DateTimeOffset TimestampUtc, string Who, string Action, string EvidenceId, string From, string To,
                                  string Sha256, string Tool, string ToolVersion, string Parser, string Transformation)
{
    /// <summary>Account that collected, collecting machine, audited source system, removable medium id (WP3a). JSONL only.</summary>
    public string CollectorAccount { get; init; } = "";
    public string CollectorMachine { get; init; } = "";
    public string SourceSystem { get; init; } = "";
    public string MediaId { get; init; } = "";

    public static readonly string[] Header = ["TimestampUtc", "Who", "Action", "EvidenceId", "From", "To", "Sha256", "Tool", "ToolVersion", "Parser", "Transformation"];
    public object?[] Row() => [TimestampUtc, Who, Action, EvidenceId, From, To, Sha256, Tool, ToolVersion, Parser, Transformation];
}

/// <summary>One collector run (spec §11, §120).</summary>
public sealed record CollectionAuditRecord(string Collector, DateTimeOffset StartUtc, DateTimeOffset EndUtc, EvidenceStatus Status, int? ExitCode,
                                           string Source, string Destination, long Bytes, string Sha256, string Tool, string ToolVersion,
                                           string CommandLine, string Error)
{
    public static readonly string[] Header = ["Collector", "StartUTC", "EndUTC", "DurationSeconds", "Status", "ExitCode", "Source", "Destination", "Bytes", "SHA256", "Tool", "ToolVersion", "CommandLine", "Error"];
    public object?[] Row() => [Collector, StartUtc, EndUtc, Math.Round((EndUtc - StartUtc).TotalSeconds, 3), Status.ToSpec(), ExitCode, Source, Destination, Bytes, Sha256, Tool, ToolVersion, CommandLine, Error];
}
