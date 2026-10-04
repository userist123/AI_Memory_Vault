using System.Text.Json;

namespace LogAnalyzer.Dfir.Windows.Containment;

public sealed record TrustedProgram(string Sha256, string Path, string Note, DateTimeOffset AddedUtc, string AddedBy);

/// <summary>
/// Programs the operator has vouched for (for example an unsigned developer tool). Matching is by SHA-256, not by
/// path: a different file dropped at a trusted path is not trusted, and an update of a trusted program must be
/// confirmed again. Trusted programs are never contained automatically; manual containment remains possible.
/// </summary>
public sealed class TrustedProgramStore
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Dictionary<string, TrustedProgram> _bySha = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Mtime, long Size, string Sha)> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "trusted_programs.json");

    public TrustedProgramStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        if (File.Exists(_path))
            foreach (var t in JsonSerializer.Deserialize<List<TrustedProgram>>(File.ReadAllText(_path)) ?? [])
                _bySha[t.Sha256] = t;
    }

    public IReadOnlyCollection<TrustedProgram> All => _bySha.Values;

    public TrustedProgram Trust(string programPath, string note, string addedBy)
    {
        var sha = Sha256Of(programPath) ?? throw new IOException($"Nu se poate calcula SHA-256 pentru {programPath}.");
        var t = new TrustedProgram(sha, programPath, note, DateTimeOffset.UtcNow, addedBy);
        _bySha[sha] = t;
        Save();
        return t;
    }

    public bool Remove(string sha256)
    {
        bool removed = _bySha.Remove(sha256);
        if (removed) Save();
        return removed;
    }

    public bool IsTrusted(string programPath) => Sha256Of(programPath) is { } sha && _bySha.ContainsKey(sha);

    private string? Sha256Of(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            if (_hashCache.TryGetValue(path, out var c) && c.Mtime == fi.LastWriteTimeUtc && c.Size == fi.Length) return c.Sha;
            var sha = LogAnalyzer.Dfir.IO.Hashing.Sha256File(path);
            _hashCache[path] = (fi.LastWriteTimeUtc, fi.Length, sha);
            return sha;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_bySha.Values.OrderBy(t => t.Path).ToList(), Opts));
    }
}
