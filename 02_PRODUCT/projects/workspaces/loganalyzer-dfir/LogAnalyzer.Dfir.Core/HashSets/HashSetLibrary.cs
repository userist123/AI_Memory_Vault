using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.HashSets;

public sealed record HashSetAuditEntry(string Action, string SetId, string Name, string Category, int Entries, string SourceSha256, string Who, string WhoSource, DateTimeOffset WhenUtc);

/// <summary>
/// The library of fingerprint sets kept in the application data (%PROGRAMDATA%\LogAnalyzer\hashsets, next to the profile and the registers):
/// one &lt;id&gt;.json (metadata) and one &lt;id&gt;.hashes.txt (one hash per line) per set, plus a hash-chained audit of imports and removals.
/// Only the administrator changes it (same gate as the registers). Data only: nothing here is applied to the system.
/// </summary>
public sealed class HashSetLibrary(string? dir = null)
{
    public const string AuditFileName = "hashsets_audit.jsonl";
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "hashsets");

    public string Dir { get; } = dir ?? DefaultDir;
    private readonly object _gate = new();

    /// <summary>Sets that load; a set whose files cannot be read is reported in <paramref name="problems"/>, never treated as an empty set.</summary>
    public List<KnownHashSet> Load(out List<string> problems)
    {
        var sets = new List<KnownHashSet>(); problems = [];
        lock (_gate)
        {
            if (!Directory.Exists(Dir)) return sets;
            foreach (var meta in Directory.GetFiles(Dir, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    var info = Json.Read<KnownHashSetInfo>(meta);
                    var hashFile = HashesPath(info.Id);
                    if (info.Id.Length == 0 || !File.Exists(hashFile)) { problems.Add($"{Path.GetFileName(meta)}: lipsește fișierul de amprente"); continue; }
                    var hashes = new HashSet<string>(File.ReadLines(hashFile).Where(l => l.Length > 0), StringComparer.Ordinal);
                    if (hashes.Count != info.EntryCount) { problems.Add($"{info.Name}: {hashes.Count} amprente în fișier față de {info.EntryCount} înregistrate (set nefolosit)"); continue; }
                    sets.Add(new KnownHashSet(info, hashes));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
                { problems.Add($"{Path.GetFileName(meta)}: {ex.Message}"); }
            }
        }
        return sets.OrderBy(s => s.Info.ImportedUtc).ToList();
    }

    public List<KnownHashSet> Load() => Load(out _);

    private string HashesPath(string id) => Path.Combine(Dir, id + ".hashes.txt");
    private string MetaPath(string id) => Path.Combine(Dir, id + ".json");

    /// <summary>Saves a set. Returns an error message, or "" on success.</summary>
    public string Add(KnownHashSet set)
    {
        if (!OperatorIdentity.MayEditAdministration) return OperatorIdentity.AdministratorOnlyMessage;
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Dir);
                var tmp = HashesPath(set.Info.Id) + ".tmp";
                File.WriteAllLines(tmp, set.Hashes.Order(StringComparer.Ordinal));
                File.Move(tmp, HashesPath(set.Info.Id), overwrite: true);
                Json.Write(MetaPath(set.Info.Id), set.Info);
                Audit("import", set.Info);
            }
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"Setul nu a putut fi salvat: {ex.Message}"; }
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

    /// <summary>Removes a set. Returns an error message, or "" on success.</summary>
    public string Remove(string id)
    {
        if (!OperatorIdentity.MayEditAdministration) return OperatorIdentity.AdministratorOnlyMessage;
        try
        {
            lock (_gate)
            {
                if (!File.Exists(MetaPath(id))) return "Setul nu există.";
                var info = Json.Read<KnownHashSetInfo>(MetaPath(id));
                File.Delete(MetaPath(id));
                if (File.Exists(HashesPath(id))) File.Delete(HashesPath(id));
                Audit("remove", info);
            }
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return $"Setul nu a putut fi eliminat: {ex.Message}"; }
    }

    private void Audit(string action, KnownHashSetInfo i) =>
        new HashChain(Path.Combine(Dir, AuditFileName)).Append(new HashSetAuditEntry(action, i.Id, i.Name, i.Category, i.EntryCount, i.SourceSha256,
            OperatorIdentity.WhoDomainQualified, OperatorIdentity.WhoSource, DateTimeOffset.UtcNow));
}
