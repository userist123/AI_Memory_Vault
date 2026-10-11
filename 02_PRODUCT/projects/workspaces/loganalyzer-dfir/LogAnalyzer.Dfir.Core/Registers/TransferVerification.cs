using System.Text;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Registers;

public enum DeclaredFileStatus
{
    /// <summary>The declared SHA-256 was found on the medium (possibly under another name).</summary>
    Identical,
    /// <summary>A file with the declared name exists but is larger than the size cap, so its content was not compared.</summary>
    SameName,
    /// <summary>A file with the declared name exists but its SHA-256 is different.</summary>
    DifferentContent,
    /// <summary>Neither the SHA-256 nor the name is on the medium.</summary>
    NotFound,
    /// <summary>A file with the declared name exists but could not be read (locked, access denied, I/O error).</summary>
    Unreadable,
}

/// <summary>One file declared in a transfer row and what was found for it on the medium.</summary>
public sealed record DeclaredFileResult(string RegistrationNumber, string Name, string Sha256, DeclaredFileStatus Status, IReadOnlyList<string> Where)
{
    public string StatusText => Status switch
    {
        DeclaredFileStatus.Identical => "IDENTIC",
        DeclaredFileStatus.SameName => "ACELAȘI NUME",
        DeclaredFileStatus.DifferentContent => "CONȚINUT DIFERIT",
        DeclaredFileStatus.NotFound => "NEGĂSIT",
        _ => "NECITIBIL",
    };

    public bool IsProblem => Status != DeclaredFileStatus.Identical;

    /// <summary>One plain-Romanian line for a non-IT controller.</summary>
    public string Text
    {
        get
        {
            var n = Name.Length > 0 ? $"„{Name}”" : "fișier fără nume în registru";
            return Status switch
            {
                DeclaredFileStatus.Identical => $"{RegistrationNumber}: {n} — IDENTIC, aceeași amprentă SHA-256 se află pe suport la: {string.Join(", ", Where)}.",
                DeclaredFileStatus.SameName => $"{RegistrationNumber}: {n} — ACELAȘI NUME la {string.Join(", ", Where)}, dar fișierul depășește limita de mărime și conținutul nu a fost comparat.",
                DeclaredFileStatus.DifferentContent => $"{RegistrationNumber}: {n} — CONȚINUT DIFERIT: pe suport există un fișier cu același nume ({string.Join(", ", Where)}), dar amprenta SHA-256 nu este cea înregistrată.",
                DeclaredFileStatus.NotFound => $"{RegistrationNumber}: {n} — NEGĂSIT pe suport (nici amprenta, nici numele).",
                _ => $"{RegistrationNumber}: {n} — NECITIBIL: există la {string.Join(", ", Where)}, dar nu a putut fi citit (blocat sau acces refuzat).",
            };
        }
    }
}

/// <summary>A file on the medium whose SHA-256 matches no declared transfer row.</summary>
public sealed record UnlistedFile(string RelativePath, string Sha256, long Bytes);

/// <summary>Result of comparing the declared transfers with the content of a folder or medium.</summary>
public sealed class TransferVerification
{
    public string Root { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public List<string> TransferNumbers { get; init; } = [];
    public List<DeclaredFileResult> Declared { get; init; } = [];
    public bool UnlistedChecked { get; init; }
    public List<UnlistedFile> Unlisted { get; init; } = [];
    /// <summary>Files on the medium that could not be read (relative path).</summary>
    public List<string> UnreadableOnMedium { get; init; } = [];
    /// <summary>Files on the medium skipped because they exceed the size cap.</summary>
    public List<string> SkippedTooLarge { get; init; } = [];
    public int FilesScanned { get; set; }
    public bool Truncated { get; set; }
    public bool Cancelled { get; set; }

    public int Count(DeclaredFileStatus s) => Declared.Count(d => d.Status == s);

    public string SummaryText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Cancelled ? "Verificare ÎNTRERUPTĂ (rezultat parțial). " : "Verificare încheiată. ");
            sb.Append($"Suport: {Root}. Transferuri comparate: {TransferNumbers.Count}; fișiere declarate: {Declared.Count}; fișiere citite de pe suport: {FilesScanned}. ");
            sb.Append($"IDENTIC {Count(DeclaredFileStatus.Identical)} · ACELAȘI NUME {Count(DeclaredFileStatus.SameName)} · CONȚINUT DIFERIT {Count(DeclaredFileStatus.DifferentContent)} · NEGĂSIT {Count(DeclaredFileStatus.NotFound)} · NECITIBIL {Count(DeclaredFileStatus.Unreadable)}.");
            if (UnlistedChecked) sb.Append($" Fișiere de pe suport care nu apar în niciun transfer declarat: {Unlisted.Count}.");
            if (SkippedTooLarge.Count > 0) sb.Append($" {SkippedTooLarge.Count} fișiere au fost sărite (prea mari).");
            if (UnreadableOnMedium.Count > 0) sb.Append($" {UnreadableOnMedium.Count} fișiere de pe suport nu au putut fi citite.");
            if (Truncated) sb.Append(" Numărul maxim de fișiere a fost atins: suportul nu a fost parcurs complet.");
            return sb.ToString();
        }
    }
}

public sealed record TransferVerifyProgress(int FilesDone, int FilesTotal, string Current);

public sealed class TransferVerifyOptions
{
    public const long DefaultMaxFileBytes = 2L * 1024 * 1024 * 1024;
    public const int DefaultMaxFiles = 50_000;
    public long MaxFileBytes { get; init; } = DefaultMaxFileBytes;
    public int MaxFiles { get; init; } = DefaultMaxFiles;
    public bool ReportUnlisted { get; init; } = true;
}

/// <summary>
/// Checks transfers by CONTENT (research 2026-10-12, item 1): compares the SHA-256 recorded in the transfer register with what is on a folder or
/// presented medium. Read-only towards the medium (files are opened for reading only, directories are only listed; junctions/symlinks are not
/// followed; system folders are skipped), bounded by a file-size cap and a file-count cap, cancellable. Pure with respect to the register.
/// </summary>
public static class TransferVerifier
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { "System Volume Information", "$RECYCLE.BIN", "RECYCLER", "RECYCLER.BIN", "$Recycle.Bin" };

    /// <summary>The (name, sha256) pairs a transfer row declares; the names come from <c>Content</c> split on „;”, the hashes from <c>Sha256</c> split on whitespace.</summary>
    public static List<(string Name, string Sha256)> Declared(TransferRow row)
    {
        var hashes = row.Sha256.Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var names = row.Content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return hashes.Select((h, i) => (names.Length == hashes.Length ? names[i] : "", h.ToUpperInvariant())).ToList();
    }

    private sealed record Entry(string Rel, string Full, long Bytes);

    /// <param name="scope">The transfers whose declared files are looked for.</param>
    /// <param name="allTransfers">Every transfer of the register; a file on the medium matching none of them is „not in any declared transfer”.</param>
    public static TransferVerification Verify(IReadOnlyList<TransferRow> scope, IReadOnlyList<TransferRow> allTransfers, string root, TransferVerifyOptions? options = null,
                                              IProgress<TransferVerifyProgress>? progress = null, CancellationToken ct = default)
    {
        options ??= new TransferVerifyOptions();
        var full = Path.GetFullPath(root);
        var declaredAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in allTransfers) foreach (var (_, h) in Declared(t)) declaredAll.Add(h);

        var result = new TransferVerification
        {
            Root = full, StartedUtc = DateTimeOffset.UtcNow, TransferNumbers = scope.Select(t => t.RegistrationNumber).ToList(),
            UnlistedChecked = options.ReportUnlisted,
        };

        var truncated = false;
        var entries = Enumerate(full, options, result, ref truncated);
        result.Truncated = truncated;

        var byHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var hashOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);       // rel path -> sha256
        var unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tooLarge = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        foreach (var e in entries)
        {
            if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
            progress?.Report(new TransferVerifyProgress(done, entries.Count, e.Rel));
            done++;
            if (e.Bytes > options.MaxFileBytes) { tooLarge.Add(e.Rel); result.SkippedTooLarge.Add(e.Rel); continue; }
            try
            {
                var h = Hashing.Sha256File(e.Full, ct);
                hashOf[e.Rel] = h;
                if (!byHash.TryGetValue(h, out var l)) byHash[h] = l = [];
                l.Add(e.Rel);
                result.FilesScanned++;
                if (options.ReportUnlisted && !declaredAll.Contains(h)) result.Unlisted.Add(new UnlistedFile(e.Rel, h, e.Bytes));
            }
            catch (OperationCanceledException) { result.Cancelled = true; break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable.Add(e.Rel); result.UnreadableOnMedium.Add(e.Rel); }
        }
        progress?.Report(new TransferVerifyProgress(done, entries.Count, ""));

        foreach (var t in scope)
            foreach (var (name, hash) in Declared(t))
            {
                if (byHash.TryGetValue(hash, out var where))
                {
                    result.Declared.Add(new DeclaredFileResult(t.RegistrationNumber, name, hash, DeclaredFileStatus.Identical, where.Take(5).ToList()));
                    continue;
                }
                // same name: the declared relative path, or (declared name is only a file name, or the folder was chosen one level higher) the same file name anywhere
                var fileName = name.Length > 0 ? Path.GetFileName(name.Replace('/', '\\')) : "";
                var sameName = name.Length == 0 ? [] : entries.Where(e => e.Rel.Equals(name, StringComparison.OrdinalIgnoreCase) || e.Rel.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase) ||
                                                                          Path.GetFileName(e.Rel).Equals(fileName, StringComparison.OrdinalIgnoreCase)).Select(e => e.Rel).ToList();
                if (sameName.Count == 0) result.Declared.Add(new DeclaredFileResult(t.RegistrationNumber, name, hash, DeclaredFileStatus.NotFound, []));
                else if (sameName.Any(unreadable.Contains)) result.Declared.Add(new DeclaredFileResult(t.RegistrationNumber, name, hash, DeclaredFileStatus.Unreadable, sameName.Take(5).ToList()));
                else if (sameName.Any(tooLarge.Contains)) result.Declared.Add(new DeclaredFileResult(t.RegistrationNumber, name, hash, DeclaredFileStatus.SameName, sameName.Take(5).ToList()));
                else if (sameName.Any(hashOf.ContainsKey)) result.Declared.Add(new DeclaredFileResult(t.RegistrationNumber, name, hash, DeclaredFileStatus.DifferentContent, sameName.Take(5).ToList()));
                else result.Declared.Add(new DeclaredFileResult(t.RegistrationNumber, name, hash, DeclaredFileStatus.NotFound, []));      // cancelled before it was reached
            }
        return result;
    }

    /// <summary>Lists regular files below <paramref name="root"/>, not following reparse points, skipping system folders; stops at the file-count cap.</summary>
    private static List<Entry> Enumerate(string root, TransferVerifyOptions o, TransferVerification result, ref bool truncated)
    {
        var list = new List<Entry>();
        var stack = new Stack<string>();
        if (File.Exists(root)) { var fi = new FileInfo(root); list.Add(new Entry(fi.Name, fi.FullName, fi.Length)); return list; }
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<FileSystemInfo> items;
            try { items = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result.UnreadableOnMedium.Add(Path.GetRelativePath(root, dir) + "\\"); continue; }
            foreach (var item in items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (item.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (item is DirectoryInfo d)
                {
                    if (dir == root && SkippedFolders.Contains(d.Name)) continue;
                    stack.Push(d.FullName);
                }
                else if (item is FileInfo f)
                {
                    if (list.Count >= o.MaxFiles) { truncated = true; return list; }
                    long len; try { len = f.Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { len = 0; }
                    list.Add(new Entry(Path.GetRelativePath(root, f.FullName), f.FullName, len));
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Records the verification in the register audit chain (<c>register_audit.jsonl</c> next to the register): who, when, which medium and transfers were
    /// compared and the result counts. Returns false (and writes nothing) when authentication is required and nobody is signed in.
    /// </summary>
    public static bool WriteAudit(TransferVerification v, string? registerPath = null)
    {
        if (LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired && LogAnalyzer.Dfir.Auth.OperatorIdentity.Current is null) return false;
        registerPath ??= TransferStore.DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(registerPath))!);
        var sha = File.Exists(registerPath) ? Hashing.Sha256File(registerPath) : "";
        var after = new List<string[]>
        {
            new[] { "suport comparat", v.Root },
            new[] { "transferuri comparate", string.Join("; ", v.TransferNumbers) },
            new[] { "stare", v.Cancelled ? "întreruptă" : v.Truncated ? "incompletă (limită de fișiere)" : "completă" },
            new[] { "IDENTIC", v.Count(DeclaredFileStatus.Identical).ToString() },
            new[] { "ACELAȘI NUME", v.Count(DeclaredFileStatus.SameName).ToString() },
            new[] { "CONȚINUT DIFERIT", v.Count(DeclaredFileStatus.DifferentContent).ToString() },
            new[] { "NEGĂSIT", v.Count(DeclaredFileStatus.NotFound).ToString() },
            new[] { "NECITIBIL", v.Count(DeclaredFileStatus.Unreadable).ToString() },
            new[] { "fișiere pe suport în niciun transfer", v.UnlistedChecked ? v.Unlisted.Count.ToString() : "nesolicitat" },
            new[] { "fișiere citite de pe suport", v.FilesScanned.ToString() },
        };
        new LogAnalyzer.Dfir.Case.HashChain(RegisterStore.AuditPathFor(registerPath)).Append(new RegisterAuditEntry("transfers", "transfer.verify",
            LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoDomainQualified, LogAnalyzer.Dfir.Auth.OperatorIdentity.WhoSource, DateTimeOffset.UtcNow, sha, sha, [], after));
        return true;
    }
}
