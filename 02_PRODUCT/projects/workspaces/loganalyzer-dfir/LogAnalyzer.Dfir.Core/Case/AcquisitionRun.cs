using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>One stored file of a collector, as it was registered in the evidence store at the end of the collector.</summary>
public sealed record AcquisitionItem(string EvidenceId, string OriginalPath, string StoredPath, long Size, string Sha256);

public sealed class AcquisitionCollectorRecord
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Tool { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public EvidenceStatus Status { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Commands { get; set; } = [];
    /// <summary>SHA-256 over the item list (<see cref="AcquisitionRunRecord.ComputeManifestSha256"/>); empty only when the collector produced no items.</summary>
    public string ManifestSha256 { get; set; } = "";
    public List<AcquisitionItem> Items { get; set; } = [];
}

/// <summary>
/// "Fișa colectării": one record per acquisition run, written to Logs/acquisition_runs/&lt;RunId&gt;.json when the run ends (completed, cancelled or
/// failed) and registered as an output, so a later edit of the record or of any listed file is detected by the re-check.
/// </summary>
public sealed class AcquisitionRunRecord
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = SchemaVersions.AcquisitionRun;
    public string RunId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Station { get; set; } = "";
    public string ApplicationVersion { get; set; } = "";
    public string Profile { get; set; } = "";
    public bool Elevated { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public OperationState State { get; set; }
    public string Reason { get; set; } = "";
    public List<AcquisitionCollectorRecord> Collectors { get; set; } = [];

    public const string Folder = "Logs/acquisition_runs";
    private static readonly Regex RunIdPattern = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool IsValidRunId(string? id) => id is not null && RunIdPattern.IsMatch(id);

    /// <summary>
    /// SHA-256 (lowercase hex) of the canonical JSON array <c>[[sha256 lowercase, size, storedPath], ...]</c> sorted by stored path (ordinal, then hash,
    /// then size so the result never depends on input order). JSON quoting keeps one item from ever being re-read as two; "" when there are no items.
    /// </summary>
    public static string ComputeManifestSha256(IEnumerable<AcquisitionItem> items)
    {
        var rows = items.Select(i => (Sha: (i.Sha256 ?? "").ToLowerInvariant(), i.Size, Path: i.StoredPath ?? ""))
                        .OrderBy(i => i.Path, StringComparer.Ordinal).ThenBy(i => i.Sha, StringComparer.Ordinal).ThenBy(i => i.Size)
                        .Select(i => new object[] { i.Sha, i.Size, i.Path }).ToList();
        return rows.Count == 0 ? "" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));
    }

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public static bool IsValidSha256(string? s) => s is not null && Sha256Pattern.IsMatch(s);
}

/// <summary>Collects what each collector stored during one run and writes the run record once, at the end.</summary>
public sealed class AcquisitionRunRecorder
{
    private readonly CaseWorkspace _ws;
    private readonly AcquisitionRunRecord _record;
    private bool _completed;

    private AcquisitionRunRecorder(CaseWorkspace ws, AcquisitionRunRecord record) { _ws = ws; _record = record; }

    public string RunId => _record.RunId;

    public static AcquisitionRunRecorder Begin(CaseWorkspace ws, string profile, bool elevated) =>
        new(ws, new AcquisitionRunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            CaseId = ws.Info.CaseId,
            Operator = OperatorIdentity.Who,
            Station = Environment.MachineName,
            ApplicationVersion = DfirInfo.ApplicationVersion,
            Profile = profile,
            Elevated = elevated,
            StartUtc = DateTimeOffset.UtcNow,
        });

    /// <summary>Adds one finished collector (also a failed one, with no items) and returns its ManifestSha256 ("" when it stored nothing).</summary>
    public string Add(string collectorName, string collectorVersion, string tool, string toolVersion, EvidenceStatus status,
                      IEnumerable<string> errors, IEnumerable<string> commands, IEnumerable<EvidenceItem> evidence, DateTimeOffset start, DateTimeOffset end)
    {
        var items = evidence.Select(e => new AcquisitionItem(e.EvidenceId, e.OriginalPath, e.StoredPath, e.Size, e.Sha256)).ToList();
        var manifest = AcquisitionRunRecord.ComputeManifestSha256(items);
        _record.Collectors.Add(new AcquisitionCollectorRecord
        {
            Name = collectorName, Version = collectorVersion, Tool = tool, ToolVersion = toolVersion, Status = status,
            StartUtc = start, EndUtc = end, Errors = errors.ToList(), Commands = commands.ToList(), ManifestSha256 = manifest, Items = items,
        });
        return manifest;
    }

    /// <summary>Evidence a collector registered since <paramref name="start"/> (used when the collector was interrupted and returned no outcome).</summary>
    public static List<EvidenceItem> EvidenceSince(CaseWorkspace ws, string collectorName, DateTimeOffset start)
    {
        try { return ws.LoadEvidence().Where(e => e.Collector == collectorName && e.AcquiredAtUtc >= start).ToList(); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Adds a collector that was cancelled or failed without an outcome, with the evidence it had already registered.</summary>
    public string AddInterrupted(string collectorName, string collectorVersion, EvidenceStatus status, string error, DateTimeOffset start, DateTimeOffset end) =>
        Add(collectorName, collectorVersion, "", "", status, [error], [], EvidenceSince(_ws, collectorName, start), start, end);

    /// <summary>Writes the record atomically (temp file in the same folder, no overwrite), registers it as an output and audits it. A second call does nothing.</summary>
    public void Complete(OperationState state, string reason)
    {
        if (_completed) return;
        _completed = true;
        _record.EndUtc = DateTimeOffset.UtcNow;
        _record.State = state;
        _record.Reason = reason;

        // Review round 2: a read-only case gets nothing written (RecordOutput would refuse after the move and leave an unregistered record behind).
        if (_ws.IsReadOnly) throw new InvalidOperationException("cazul este deschis numai pentru citire; fișa colectării nu a fost scrisă");
        var rel = $"{AcquisitionRunRecord.Folder}/{_record.RunId}.json";
        var full = _ws.FullPath(rel);
        var dir = Path.GetDirectoryName(full)!;
        if (AcquisitionRunVerifier.IsReparsePoint(Path.Combine(_ws.Root, "Logs")) || AcquisitionRunVerifier.IsReparsePoint(dir))
            throw new IOException("folderul fișelor de colectare este o legătură (junction/symlink); fișa nu a fost scrisă");
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{_record.RunId}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(_record, Json.Options), new UTF8Encoding(false));
            File.Move(tmp, full, overwrite: false);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        var items = _record.Collectors.SelectMany(c => c.Items).ToList();
        OutputRecord o;
        try { o = _ws.RecordOutput(rel, "AcquisitionRun", DfirInfo.ApplicationVersion, items.Select(i => i.EvidenceId)); }
        catch
        {
            // the custody chain does not know the file: remove it rather than leave a record every later Recheck reports as unknown
            try { File.Delete(full); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        _ws.Audit("acquisition.run_recorded", $"run={_record.RunId} sha256={o.Sha256} collectors={_record.Collectors.Count} items={items.Count} state={state}");
    }
}

public enum AcquisitionFindingKind
{
    ItemIntact, ItemMissing, ItemModified, ItemUnreadable, ItemOutsideCase,
    EvidenceNotInStore, EvidenceStoreMismatch, ManifestMismatch, RecordInvalid, Unregistered,
    /// <summary>The record file differs from the SHA-256 the custody chain registered for it (edited after the run ended).</summary>
    RecordModified,
}

/// <summary>One record file with what the checks found. <see cref="Record"/> is null for a record that is not to be trusted (unregistered, modified, invalid, unreadable).</summary>
public sealed record AcquisitionRunInspection(string RunFile, string RunId, AcquisitionRunRecord? Record, IReadOnlyList<AcquisitionRunFinding> Findings);

/// <summary><see cref="FolderProblem"/> is set when nothing in the folder could be verified (junction, unreadable folder or custody registry).</summary>
public sealed record AcquisitionRunInspectionResult(string? FolderProblem, IReadOnlyList<AcquisitionRunInspection> Runs);

public sealed record AcquisitionRunFinding(string RunFile, string RunId, string EvidenceId, AcquisitionFindingKind Kind, string Detail)
{
    public bool IsProblem => Kind != AcquisitionFindingKind.ItemIntact;
}

/// <summary>
/// Re-checks every acquisition run record of a case against the files on disk, the evidence store and the output registry. Reports; never throws for
/// bad data (only <see cref="OperationCanceledException"/>) and never touches the files.
/// </summary>
public static class AcquisitionRunVerifier
{
    private const long MaxRecordBytes = 64L * 1024 * 1024;

    public static bool IsReparsePoint(string dir)
    {
        try { return Directory.Exists(dir) && new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <param name="knownActualHashes">Full path to SHA-256 already computed by the caller (the re-check); those files are not hashed again.</param>
    /// <param name="registeredOutputs">Output path to the SHA-256 registered in the custody chain; when null they are read from the case.</param>
    public static IReadOnlyList<AcquisitionRunFinding> Verify(CaseWorkspace ws, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? knownActualHashes = null, IReadOnlyDictionary<string, string>? registeredOutputs = null)
    {
        var r = Inspect(ws, ct, knownActualHashes, registeredOutputs);
        if (r.FolderProblem is not null) return [Bad("", "", r.FolderProblem)];
        return r.Runs.SelectMany(x => x.Findings).ToList();
    }

    /// <summary>Known hashes (full path to SHA-256 of the evidence files the re-check computed) and the registered outputs (path to registered SHA-256) of a finished re-check.</summary>
    public static (IReadOnlyDictionary<string, string> Known, IReadOnlyDictionary<string, string> Registered) MapsFrom(CaseWorkspace ws, RecheckResult r)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in r.Evidence)
            if (c.ActualSha256 is not null && ws.TryFullPath(c.StoredPath, out var cf)) known[cf] = c.ActualSha256;
        var registered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in r.Outputs) registered[o.Path] = o.ExpectedSha256;
        return (known, registered);
    }

    /// <summary>
    /// Same checks as <see cref="Verify"/>, but keeps the record the checks were made on: <see cref="AcquisitionRunInspection.Record"/> is the record that was
    /// read once, hashed (same bytes) against the registered SHA-256 and found well-formed; null when it is not to be trusted.
    /// </summary>
    public static AcquisitionRunInspectionResult Inspect(CaseWorkspace ws, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? knownActualHashes = null, IReadOnlyDictionary<string, string>? registeredOutputs = null)
    {
        var runs = new List<AcquisitionRunInspection>();
        var dir = Path.Combine(ws.Root, "Logs", "acquisition_runs");
        if (!Directory.Exists(dir)) return new(null, runs);
        if (IsReparsePoint(Path.Combine(ws.Root, "Logs")) || IsReparsePoint(dir))
            return new("folderul fișelor de colectare este o legătură (junction/symlink) și nu a fost urmat", runs);

        string[] files;
        try { files = Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new("folderul fișelor de colectare nu a putut fi citit: " + ex.Message, runs); }

        IReadOnlyList<EvidenceItem>? store = null;
        string? storeProblem = null;
        try { store = ws.LoadEvidence(); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        { storeProblem = "indexul probelor nu a putut fi citit: " + ex.Message; }
        var byId = new Dictionary<string, EvidenceItem>(StringComparer.Ordinal);
        foreach (var e in store ?? []) if (e.EvidenceId is not null) byId.TryAdd(e.EvidenceId, e);

        Dictionary<string, string> registered;
        try
        {
            registered = registeredOutputs is not null ? new(registeredOutputs, StringComparer.OrdinalIgnoreCase)
                : ws.RegisteredOutputs().ToDictionary(o => o.Path, o => o.Sha256, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var f in files) runs.Add(new(Path.GetFileName(f), Path.GetFileNameWithoutExtension(f), null, []));
            return new("registrul rezultatelor nu a putut fi citit: " + ex.Message, runs);
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var findings = new List<AcquisitionRunFinding>();
            AcquisitionRunRecord? rec = null;
            try { rec = VerifyOne(ws, file, store is null ? null : byId, storeProblem, registered, knownActualHashes, findings, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { rec = null; findings.Add(Bad(Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), "verificarea fișei a eșuat neașteptat: " + ex.GetType().Name)); }
            runs.Add(new(Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), rec, findings));
        }
        return new(null, runs);
    }

    private static AcquisitionRunFinding Bad(string file, string runId, string detail) =>
        new(file, runId, "", AcquisitionFindingKind.RecordInvalid, detail);

    /// <summary>First structural problem of a deserialized record (nulls, bad hashes, empty paths), or null when it can be checked.</summary>
    private static string? Structure(AcquisitionRunRecord rec)
    {
        if (rec.Collectors is null) return "lista colectorilor lipsește";
        foreach (var c in rec.Collectors)
        {
            if (c is null) return "un colector este nul";
            if (c.Name is null || c.ManifestSha256 is null) return "un colector nu are nume sau sumă de control";
            if (c.ManifestSha256.Length != 0 && !AcquisitionRunRecord.IsValidSha256(c.ManifestSha256)) return $"colectorul {c.Name}: suma de control a listei nu este SHA-256 valid";
            if (c.Items is null) return $"colectorul {c.Name}: lista de fișiere lipsește";
            foreach (var i in c.Items)
            {
                if (i is null) return $"colectorul {c.Name}: un element al listei este nul";
                if (string.IsNullOrEmpty(i.EvidenceId)) return $"colectorul {c.Name}: un fișier nu are identificator de probă";
                if (string.IsNullOrEmpty(i.StoredPath)) return $"colectorul {c.Name}: {i.EvidenceId} nu are cale";
                if (!AcquisitionRunRecord.IsValidSha256(i.Sha256)) return $"colectorul {c.Name}: {i.EvidenceId} nu are SHA-256 valid";
            }
        }
        return null;
    }

    /// <summary>
    /// The one safe way to read a record file (no symlink, size cap, schema check). The bytes are read once; the SHA-256 returned and the record parsed
    /// are both of those bytes, so the hash that is compared with the registered one is the hash of what is displayed.
    /// </summary>
    private static AcquisitionRunRecord? ReadRecord(string file, string name, out string sha256, out string problem)
    {
        problem = ""; sha256 = "";
        try
        {
            var fi = new FileInfo(file);
            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint)) { problem = "fișa este o legătură simbolică și nu a fost urmată"; return null; }
            if (fi.Length > MaxRecordBytes) { problem = "fișa depășește dimensiunea maximă acceptată"; return null; }
            var bytes = File.ReadAllBytes(file);
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var text = Encoding.UTF8.GetString(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsSpan(3) : bytes);
            var rec = JsonSerializer.Deserialize<AcquisitionRunRecord>(text, Json.Options) ?? throw new InvalidDataException("fișă goală");
            SchemaVersions.Accept(rec.SchemaVersion, name);
            return rec;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            problem = "fișa nu a putut fi citită: " + ex.Message;
            return null;
        }
    }

    private static AcquisitionRunRecord? VerifyOne(CaseWorkspace ws, string file, IReadOnlyDictionary<string, EvidenceItem>? byId, string? storeProblem, Dictionary<string, string> registered,
                                  IReadOnlyDictionary<string, string>? knownHashes, List<AcquisitionRunFinding> findings, CancellationToken ct)
    {
        var name = Path.GetFileName(file);
        var stem = Path.GetFileNameWithoutExtension(file);
        if (!AcquisitionRunRecord.IsValidRunId(stem))
        {
            findings.Add(Bad(name, "", "numele fișei nu este un identificator de rulare valid (32 de caractere hexazecimale mici); fișa nu a fost citită"));
            return null;
        }
        if (!registered.TryGetValue($"{AcquisitionRunRecord.Folder}/{name}", out var registeredSha))
        {
            findings.Add(new(name, stem, "", AcquisitionFindingKind.Unregistered, "fișă necunoscută lanțului de custodie; conținutul ei nu este de încredere"));
            return null;
        }

        var rec = ReadRecord(file, name, out var actualSha, out var readProblem);
        if (rec is null) { findings.Add(Bad(name, stem, readProblem)); return null; }
        if (!string.Equals(actualSha, registeredSha, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new(name, stem, "", AcquisitionFindingKind.RecordModified, "fișă modificată după înregistrare; conținutul ei nu este de încredere"));
            return null;
        }

        if (!string.Equals(rec.RunId, stem, StringComparison.Ordinal))
        {
            findings.Add(Bad(name, stem, "identificatorul din fișă nu corespunde numelui fișierului"));
            return null;
        }
        if (!string.Equals(rec.CaseId, ws.Info.CaseId, StringComparison.Ordinal))
        {
            findings.Add(Bad(name, stem, "fișa aparține altui caz"));
            return null;
        }
        if (Structure(rec) is { } bad) { findings.Add(Bad(name, stem, "fișă invalidă: " + bad)); return null; }
        if (storeProblem is not null) findings.Add(Bad(name, stem, storeProblem));

        foreach (var c in rec.Collectors)
        {
            if (!string.Equals(AcquisitionRunRecord.ComputeManifestSha256(c.Items), c.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                findings.Add(new(name, stem, "", AcquisitionFindingKind.ManifestMismatch, $"colectorul {c.Name}: lista de fișiere nu corespunde sumei de control a colectării"));
            foreach (var i in c.Items)
            {
                ct.ThrowIfCancellationRequested();
                findings.Add(CheckItem(ws, name, stem, c.Name, i, knownHashes, ct));
                if (byId is null) continue;
                if (!byId.TryGetValue(i.EvidenceId, out var s))
                    findings.Add(new(name, stem, i.EvidenceId, AcquisitionFindingKind.EvidenceNotInStore, $"{i.EvidenceId} nu există în indexul probelor"));
                else if (!string.Equals(s.Sha256, i.Sha256, StringComparison.OrdinalIgnoreCase) || s.Size != i.Size
                         || !string.Equals(s.StoredPath, i.StoredPath, StringComparison.Ordinal))
                    findings.Add(new(name, stem, i.EvidenceId, AcquisitionFindingKind.EvidenceStoreMismatch, $"{i.EvidenceId}: indexul probelor diferă de fișa colectării (sumă, dimensiune sau cale)"));
            }
        }
        return rec;
    }

    private static AcquisitionRunFinding CheckItem(CaseWorkspace ws, string file, string runId, string collector, AcquisitionItem i,
                                                   IReadOnlyDictionary<string, string>? knownHashes, CancellationToken ct)
    {
        AcquisitionRunFinding R(AcquisitionFindingKind k, string d) => new(file, runId, i.EvidenceId, k, $"{collector}: {i.EvidenceId} {d}");
        if (!ws.TryFullPath(i.StoredPath, out var full)) return R(AcquisitionFindingKind.ItemOutsideCase, CaseWorkspace.OutsideCaseReason);
        if (!File.Exists(full)) return R(AcquisitionFindingKind.ItemMissing, $"lipsește ({i.StoredPath})");
        try
        {
            var actual = knownHashes is not null && knownHashes.TryGetValue(full, out var h) ? h : Hashing.Sha256File(full, ct);
            return string.Equals(actual, i.Sha256, StringComparison.OrdinalIgnoreCase)
                ? R(AcquisitionFindingKind.ItemIntact, "intact")
                : R(AcquisitionFindingKind.ItemModified, $"modificat după colectare ({i.StoredPath})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return R(AcquisitionFindingKind.ItemUnreadable, $"nu a putut fi citit: {ex.Message}");
        }
    }
}
