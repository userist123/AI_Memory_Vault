using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.HashSets;

/// <param name="StoredSha256">SHA-256 of the stored .hashes.txt (null in entries written before the integrity check: such a set is not used).</param>
public sealed record HashSetAuditEntry(string Action, string SetId, string Name, string Category, int Entries, string SourceSha256, string? StoredSha256, string Who, string WhoSource, DateTimeOffset WhenUtc);

/// <summary>
/// The library of fingerprint sets kept in the application data (%PROGRAMDATA%\LogAnalyzer\hashsets, next to the profile and the registers):
/// one &lt;id&gt;.json (metadata) and one &lt;id&gt;.hashes.txt (one hash per line) per set, plus a hash-chained audit of imports and removals.
/// Only the administrator changes it (same gate as the registers). Data only: nothing here is applied to the system.
/// <para>
/// Trust model: ordinary users may be able to create files in the folder (inherited ACL), so a file is never trusted by itself. A set is used only when the
/// audit chain is valid, holds an import entry for the id with the same values (name, category, count, SHA-256 of the source and of the stored file) with no removal
/// after it, and the SHA-256 recomputed over the bytes actually read equals the one in the audit. Anything else is refused with a reason (never silently). The audit
/// is a plain chain: someone who can rewrite the whole folder as administrator can also rewrite it; the folder ACL set at creation is the barrier against ordinary users.
/// </para>
/// </summary>
public sealed class HashSetLibrary(string? dir = null, int maxTotalEntries = HashSetLibrary.DefaultMaxTotalEntries, bool? verifyFolderTrust = null)
{
    public const string AuditFileName = "hashsets_audit.jsonl";
    /// <summary>Over all sets together; a set that would pass it is refused whole, with a message.</summary>
    public const int DefaultMaxTotalEntries = 10_000_000;
    private const long MaxMetaBytes = 1 << 20;
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "hashsets");

    private static readonly Regex IdRx = new(@"^[a-z0-9][a-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public static bool IsValidId(string? id) => id is not null && IdRx.IsMatch(id);

    public string Dir { get; } = dir ?? DefaultDir;
    /// <summary>
    /// Review of R7a, round 2: the sets are trusted only in a folder no standard user can write into or own (otherwise the unkeyed audit and the set files can
    /// be forged or deleted together). On by default for the station folder (<see cref="DefaultDir"/>); a test folder opts in explicitly.
    /// </summary>
    public bool VerifyFolderTrust { get; } = verifyFolderTrust ?? dir is null;
    public int MaxTotalEntries { get; } = maxTotalEntries;
    /// <summary>Set when this process, running as administrator, created the folder and could not restrict its ACL; also listed by <see cref="Load(out List{string})"/>.</summary>
    public string AclProblem { get; private set; } = "";
    private readonly object _gate = new();

    private sealed record Cached(string Signature, int Limit, List<KnownHashSet> Sets, List<string> Problems);
    private static readonly ConcurrentDictionary<string, Cached> Memo = new(StringComparer.OrdinalIgnoreCase);
    private string MemoKey => Path.GetFullPath(Dir) + (VerifyFolderTrust ? "|trust" : "|plain");   // the folder-trust check changes the result, so it is part of the key

    /// <summary>
    /// Sets that load and pass the checks; every set that does not is in <paramref name="problems"/> with the reason, never treated as an empty set and never used.
    /// The result is kept in the process and reused until a file of the folder changes (name, length, date) or a set is imported or removed here.
    /// Never throws for the content of a file.
    /// </summary>
    public List<KnownHashSet> Load(out List<string> problems)
    {
        lock (_gate)
        {
            List<KnownHashSet> sets; List<string> probs;
            try
            {
                var sig = Signature();
                if (sig.Length > 0 && Memo.TryGetValue(MemoKey, out var c) && c.Signature == sig && c.Limit == MaxTotalEntries) { sets = c.Sets; probs = c.Problems; }
                else
                {
                    (sets, probs) = LoadUncached();
                    if (sig.Length > 0 && Signature() == sig) Memo[MemoKey] = new Cached(sig, MaxTotalEntries, sets, probs);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { sets = []; probs = [$"Folderul seturilor de amprente nu poate fi citit: {ex.Message}"]; }
            problems = [.. probs];
            if (AclProblem.Length > 0) problems.Add(AclProblem);
            return [.. sets];
        }
    }

    public List<KnownHashSet> Load() => Load(out _);

    private string Signature()
    {
        if (!Directory.Exists(Dir)) return "";
        var sb = new StringBuilder("d");
        foreach (var f in Directory.EnumerateFiles(Dir).Order(StringComparer.OrdinalIgnoreCase))
        {
            var fi = new FileInfo(f);
            sb.Append('|').Append(fi.Name).Append(':').Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks);
        }
        return sb.ToString();
    }

    private void Invalidate() => Memo.TryRemove(MemoKey, out _);

    private static string Reason(string label, string file, string why) => $"Setul „{label}” ({file}) nu a fost folosit: {why}";

    private (List<KnownHashSet>, List<string>) LoadUncached()
    {
        var sets = new List<KnownHashSet>(); var problems = new List<string>();
        if (!Directory.Exists(Dir)) return (sets, problems);
        if (VerifyFolderTrust)
        {
            var trust = FolderTrustProblem(Dir, repair: IsAdministrator());
            if (trust.Length > 0) { problems.Add(trust); return (sets, problems); }
        }
        var stems = new HashSet<string>(Directory.GetFiles(Dir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.Ordinal);
        var candidates = new List<KnownHashSetInfo>();
        foreach (var meta in Directory.GetFiles(Dir, "*.json").Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(meta);
            KnownHashSetInfo? info = null;
            try
            {
                if (new FileInfo(meta).Length > MaxMetaBytes) { problems.Add(Reason(file, file, "fișierul de metadate este prea mare")); continue; }
                info = JsonSerializer.Deserialize<KnownHashSetInfo>(File.ReadAllText(meta), Json.Options);
                if (info is null) { problems.Add(Reason(file, file, "fișier de metadate gol")); continue; }
                var why = Validate(info, Path.GetFileNameWithoutExtension(file));
                if (why.Length > 0) { problems.Add(Reason(info.Name ?? file, file, why)); continue; }
                candidates.Add(info);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { problems.Add(Reason(info?.Name ?? file, file, $"metadate ilizibile sau în format greșit ({ex.GetType().Name}: {ex.Message})")); }
        }
        var audit = ReadAudit(out var auditError);
        // review of R7a, round 2: a set the audit says was imported (and not removed) whose files are gone is reported, never dropped silently
        if (auditError.Length == 0)
            foreach (var (id, e) in audit)
                if (e.Action == "import" && !stems.Contains(id))
                    problems.Add(Reason(e.Name ?? id, id + ".json", "setul apare importat în jurnalul de audit, dar fișierele lui lipsesc din folder; nu este folosit"));
        if (candidates.Count == 0) return (sets, problems);
        long total = 0;
        foreach (var info in candidates.OrderBy(c => c.ImportedUtc).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            var file = info.Id + ".json";
            try
            {
                var why = auditError.Length > 0 ? auditError : AgainstAudit(info, audit);
                if (why.Length == 0 && total + info.EntryCount > MaxTotalEntries)
                    why = $"limita totală de {MaxTotalEntries:N0} intrări pe toate seturile ar fi depășită ({total:N0} deja încărcate + {info.EntryCount:N0}); eliminați un set sau măriți limita";
                if (why.Length > 0) { problems.Add(Reason(info.Name, file, why)); continue; }
                var set = ReadSet(info, out why);
                if (set is null) { problems.Add(Reason(info.Name, file, why)); continue; }
                total += set.Hashes.Count;
                sets.Add(set);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { problems.Add(Reason(info.Name, file, $"{ex.GetType().Name}: {ex.Message}")); }
        }
        return (sets.OrderBy(s => s.Info.ImportedUtc).ToList(), problems);
    }

    private static bool IsSha256Hex(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);

    private static string Validate(KnownHashSetInfo info, string fileStem)
    {
        if (!IsValidId(info.Id)) return "identificator invalid (se acceptă a-z, 0-9, _ și -, 1-64 de caractere, începând cu literă sau cifră; fără „..” și fără separatori)";
        if (!string.Equals(info.Id, fileStem, StringComparison.Ordinal)) return "identificatorul din metadate nu corespunde numelui fișierului";
        if (!HashSetCategory.IsValid(info.Category)) return $"categorie invalidă „{info.Category}” (se acceptă exact „cunoscut-bun” sau „cunoscut-rău”)";
        if (string.IsNullOrWhiteSpace(info.Name)) return "numele setului lipsește";
        if (info.EntryCount <= 0) return "număr de intrări invalid";
        if (!IsSha256Hex(info.StoredSha256) || !IsSha256Hex(info.SourceSha256)) return "lipsesc SHA-256 al sursei sau al fișierului stocat (set creat în afara importului sau înainte de verificarea de integritate; reimportați)";
        return "";
    }

    /// <summary>The import/remove entries of the audit by set id (last wins), or the reason the audit cannot be trusted.</summary>
    private Dictionary<string, HashSetAuditEntry> ReadAudit(out string error)
    {
        var last = new Dictionary<string, HashSetAuditEntry>(StringComparer.Ordinal);
        error = "";
        var path = Path.Combine(Dir, AuditFileName);
        if (!File.Exists(path)) { error = "jurnalul de audit al seturilor lipsește (nu există intrare de import)"; return last; }
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = $"jurnalul de audit nu poate fi citit: {ex.Message}"; return last; }
        var v = new HashChain(path).Verify(bytes);
        if (v.Status != ChainStatus.Valid) { error = $"jurnalul de audit al seturilor nu este valid ({v.Message})"; return last; }
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<HashSetAuditEntry>(line, Json.Line);
                if (e?.SetId is not null && e.Action is "import" or "remove") last[e.SetId] = e;
            }
            catch (JsonException) { }
        }
        return last;
    }

    private static string AgainstAudit(KnownHashSetInfo i, Dictionary<string, HashSetAuditEntry> audit)
    {
        if (!audit.TryGetValue(i.Id, out var e)) return "nu există o intrare de audit de import pentru acest set (fișiere puse în folder în afara importului)";
        if (e.Action != "import") return "setul a fost eliminat (intrare de eliminare în audit)";
        if (e.Name != i.Name || e.Category != i.Category || e.Entries != i.EntryCount
            || !string.Equals(e.SourceSha256, i.SourceSha256, StringComparison.OrdinalIgnoreCase) || !string.Equals(e.StoredSha256, i.StoredSha256, StringComparison.OrdinalIgnoreCase))
            return "metadatele nu coincid cu intrarea de audit a importului (nume, categorie, număr de intrări sau SHA-256)";
        return "";
    }

    private KnownHashSet? ReadSet(KnownHashSetInfo info, out string why)
    {
        why = "";
        var path = SafePath(info.Id, ".hashes.txt");
        if (path is null) { why = "calea fișierului de amprente iese din folderul seturilor"; return null; }
        if (!File.Exists(path)) { why = "lipsește fișierul de amprente"; return null; }
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        using var hs = new HashingStream(fs);
        using var reader = new StreamReader(hs, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        var b = new CompactHashes.Builder();
        int lines = 0; string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines++;
            if (lines > info.EntryCount) { why = $"fișierul are mai multe linii decât cele {info.EntryCount:N0} intrări înregistrate"; return null; }
            if (!b.Add(line)) { why = $"linia {lines}: amprentă invalidă (se așteaptă exact 32, 40 sau 64 de caractere hexazecimale)"; return null; }
        }
        var actual = hs.FinishHex();
        if (!string.Equals(actual, info.StoredSha256, StringComparison.OrdinalIgnoreCase)) { why = "SHA-256 al fișierului de amprente nu coincide cu cel din audit (fișier modificat după import)"; return null; }
        var hashes = b.Build();
        if (lines != info.EntryCount || hashes.Count != info.EntryCount) { why = $"{hashes.Count} amprente distincte în {lines} linii față de {info.EntryCount} înregistrate"; return null; }
        return new KnownHashSet(info, hashes);
    }

    /// <summary>The path of <c>&lt;id&gt;&lt;suffix&gt;</c> inside the folder, or null when the id is not valid or the full path would leave the folder.</summary>
    private string? SafePath(string id, string suffix)
    {
        if (!IsValidId(id)) return null;
        var root = Path.GetFullPath(Dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, id + suffix));
        return string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static readonly HashSet<string> TrustedSids = new(StringComparer.OrdinalIgnoreCase)
    {
        "S-1-5-32-544",                                                   // BUILTIN\Administrators
        "S-1-5-18",                                                       // SYSTEM
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", // TrustedInstaller
    };

    /// <summary>
    /// Review of R7a, round 2: "" when the folder and every file in it are owned by Administrators / SYSTEM / TrustedInstaller and no other account may write,
    /// delete or change permissions on the folder; otherwise the reason, and every set is refused (the audit is an unkeyed chain, so whoever can rewrite the
    /// folder can forge or delete sets together with their audit entries). With <paramref name="repair"/> (an administrator process) the owner and the restricted
    /// ACL are applied first; a repair that fails leaves the check to decide. Never throws.
    /// </summary>
    public static string FolderTrustProblem(string dir, bool repair)
    {
        if (!OperatingSystem.IsWindows()) return "";
        const FileSystemRights WriteLike = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        try
        {
            if (repair)
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    var s = di.GetAccessControl(); s.SetOwner(admins); di.SetAccessControl(s);
                    ApplyRestrictedAcl(dir);
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        var fi = new FileInfo(f); var fs = fi.GetAccessControl(); fs.SetOwner(admins); fi.SetAccessControl(fs);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException or PrivilegeNotHeldException) { }
            }
            var dsec = new DirectoryInfo(dir).GetAccessControl();
            if (dsec.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !TrustedSids.Contains(owner.Value))
                return $"Folderul seturilor de amprente ({dir}) aparține unui cont care nu este Administrators sau SYSTEM; niciun set nu este folosit. Un administrator trebuie să refacă drepturile folderului și să reimporte seturile.";
            foreach (FileSystemAccessRule r in dsec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (r.AccessControlType != AccessControlType.Allow || (r.FileSystemRights & WriteLike) == 0) continue;
                if (r.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue;     // e.g. CREATOR OWNER: applies to children only, and their owners are checked below
                var sid = r.IdentityReference.Value;
                if (!TrustedSids.Contains(sid))
                    return $"Folderul seturilor de amprente ({dir}) poate fi modificat de un cont care nu este administrator ({sid}); niciun set nu este folosit. Un administrator trebuie să deschidă aplicația o dată ca administrator (drepturile se refac) sau să le refacă manual.";
            }
            foreach (var f in Directory.GetFiles(dir))
                if (new FileInfo(f).GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier fo || !TrustedSids.Contains(fo.Value))
                    return $"Fișierul „{Path.GetFileName(f)}” din folderul seturilor de amprente aparține unui cont care nu este Administrators sau SYSTEM; niciun set nu este folosit. Eliminați fișierul ca administrator și reimportați seturile.";
            return "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return $"Drepturile folderului seturilor de amprente ({dir}) nu pot fi verificate ({ex.GetType().Name}: {ex.Message}); niciun set nu este folosit."; }
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Restricts <paramref name="dir"/>: Administrators and SYSTEM full control, Users read only, no inheritance from ProgramData.
    /// Returns "" or the reason it could not be done (never throws).
    /// </summary>
    public static string ApplyRestrictedAcl(string dir)
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inh, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(dir).SetAccessControl(sec);
            return "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return $"Folderul seturilor de amprente nu a putut fi restricționat (drepturi: Administrators și SYSTEM control total, Users doar citire): {ex.Message}. Verificați manual drepturile folderului {dir}."; }
    }

    private void EnsureDirectory()
    {
        if (Directory.Exists(Dir)) return;
        Directory.CreateDirectory(Dir);
        if (IsAdministrator()) AclProblem = ApplyRestrictedAcl(Dir);
    }

    /// <summary>Saves a set. Returns an error message, or "" on success.</summary>
    public string Add(KnownHashSet set)
    {
        if (!OperatorIdentity.MayEditAdministration) return OperatorIdentity.AdministratorOnlyMessage;
        if (!IsValidId(set.Info.Id)) return "Setul nu a putut fi salvat: identificator invalid.";
        if (!HashSetCategory.IsValid(set.Info.Category)) return "Setul nu a putut fi salvat: categorie invalidă.";
        string? hashesPath = null, metaPath = null;
        try
        {
            lock (_gate)
            {
                EnsureDirectory();
                hashesPath = SafePath(set.Info.Id, ".hashes.txt"); metaPath = SafePath(set.Info.Id, ".json");
                if (hashesPath is null || metaPath is null) return "Setul nu a putut fi salvat: calea iese din folderul seturilor.";
                var tmp = hashesPath + ".tmp";
                using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)) { NewLine = "\n" })
                    foreach (var h in set.Hashes.EnumerateHex()) w.WriteLine(h);
                set.Info.StoredSha256 = Hashing.Sha256File(tmp);
                set.Info.EntryCount = set.Hashes.Count;
                set.Info.Algorithms = [.. set.Hashes.Algorithms()];
                File.Move(tmp, hashesPath, overwrite: true);
                Json.Write(metaPath, set.Info);
                Audit("import", set.Info);
            }
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // no audit entry means the set would be refused anyway; do not leave the files behind
            try { if (hashesPath is not null) File.Delete(hashesPath); if (metaPath is not null) File.Delete(metaPath); } catch (Exception) { }
            return $"Setul nu a putut fi salvat: {ex.Message}";
        }
        finally { Invalidate(); }
    }

    /// <summary>Imports a file and saves the set; the result carries the counts and the invalid lines.</summary>
    public HashSetImportResult Import(string path, string name, string category, HashSetImportOptions? options = null)
    {
        if (!OperatorIdentity.MayEditAdministration) return new HashSetImportResult { Error = OperatorIdentity.AdministratorOnlyMessage };
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
        var r = HashSetImporter.ImportFile(path, name, category, options, OperatorIdentity.SignedInAccount ?? "");
        if (!r.Ok) return r;
        var err = Add(r.Set!);
        return err.Length == 0 ? r : new HashSetImportResult { Error = err, InvalidLineCount = r.InvalidLineCount, InvalidLines = r.InvalidLines, LinesRead = r.LinesRead };
    }

    /// <summary>Removes a set. Returns an error message, or "" on success. Paths are built from a validated id only.</summary>
    public string Remove(string id)
    {
        if (!OperatorIdentity.MayEditAdministration) return OperatorIdentity.AdministratorOnlyMessage;
        var metaPath = SafePath(id, ".json"); var hashesPath = SafePath(id, ".hashes.txt");
        if (metaPath is null || hashesPath is null) return "Identificator de set invalid; nu s-a șters nimic.";
        try
        {
            lock (_gate)
            {
                if (!File.Exists(metaPath)) return "Setul nu există.";
                KnownHashSetInfo info;
                try { info = JsonSerializer.Deserialize<KnownHashSetInfo>(File.ReadAllText(metaPath), Json.Options) ?? new KnownHashSetInfo(); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException) { info = new KnownHashSetInfo(); }
                info.Id = id; info.Name ??= "(ilizibil)"; info.Category ??= ""; info.SourceSha256 ??= "";
                Audit("remove", info);
                File.Delete(metaPath);
                if (File.Exists(hashesPath)) File.Delete(hashesPath);
            }
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"Setul nu a putut fi eliminat: {ex.Message}"; }
        finally { Invalidate(); }
    }

    private void Audit(string action, KnownHashSetInfo i) =>
        new HashChain(Path.Combine(Dir, AuditFileName)).Append(new HashSetAuditEntry(action, i.Id, i.Name, i.Category, i.EntryCount, i.SourceSha256, i.StoredSha256,
            OperatorIdentity.WhoDomainQualified, OperatorIdentity.WhoSource, DateTimeOffset.UtcNow));
}
