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

    public string Root { get; }
    public CaseInfo Info { get; private set; }
    public TimeZoneInfo Zone { get; }

    public string EvidenceIndexPath => Path.Combine(Root, "Evidence", "evidence_index.jsonl");
    public string CustodyCsvPath => Path.Combine(Root, "Logs", "chain_of_custody.csv");
    public string CustodyJsonlPath => Path.Combine(Root, "Logs", "chain_of_custody.jsonl");
    public string CollectionAuditPath => Path.Combine(Root, "Logs", "collection_audit.csv");
    public string AppAuditLogPath => Path.Combine(Root, "Logs", "LogAnalyzer_Audit.log");

    private CaseWorkspace(string root, CaseInfo info)
    {
        Root = Path.GetFullPath(root);
        Info = info;
        Zone = TryZone(info.Timezone);
        _evidenceCounter = Json.ReadLines<EvidenceItem>(EvidenceIndexPath).Count();
    }

    public static CaseWorkspace Create(string root, CaseInfo info)
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"Case folder is not empty: {root}");
        foreach (var f in Folders) Directory.CreateDirectory(Path.Combine(root, f));
        Json.Write(Path.Combine(root, "case.json"), info);
        var ws = new CaseWorkspace(root, info);
        ws.Audit("case.created", $"name={info.Name} host={info.Host}");
        return ws;
    }

    public static CaseWorkspace Open(string root)
    {
        var info = Json.Read<CaseInfo>(Path.Combine(root, "case.json"));
        var ws = new CaseWorkspace(root, info);
        ws.Audit("case.opened", "");
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
            using (var w = new CsvWriter(CustodyCsvPath, CustodyEntry.Header, append: true)) w.WriteRow(e.Row());
            Json.AppendLine(CustodyJsonlPath, e);
        }
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
        lock (_gate)
            File.AppendAllText(AppAuditLogPath, $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}\t{Environment.UserName}\t{action}\t{detail.ReplaceLineEndings(" ")}\n");
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

public sealed record CustodyEntry(DateTimeOffset TimestampUtc, string Who, string Action, string EvidenceId, string From, string To,
                                  string Sha256, string Tool, string ToolVersion, string Parser, string Transformation)
{
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
