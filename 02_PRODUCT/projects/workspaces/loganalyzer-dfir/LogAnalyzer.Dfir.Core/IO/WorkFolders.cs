namespace LogAnalyzer.Dfir.IO;

/// <summary>
/// Security audit #14: where parsers put working copies of evidence (SQLite / ESE copies, repaired EVTX). Never %TEMP%.
/// <list type="bullet">
/// <item>Evidence inside a case (the normal path: the pipeline parses <c>&lt;case&gt;\Raw\...</c>): transient copies go to
/// <c>&lt;case&gt;\Work\&lt;tag&gt;_&lt;id&gt;</c> and are deleted in a <c>finally</c>; files that are kept as derived evidence (a repaired EVTX)
/// go to <c>&lt;case&gt;\Derived\...</c> and are hashed into the custody chain by the pipeline.</item>
/// <item>A file outside any case (ad-hoc legacy parsing, tools): <c>%LOCALAPPDATA%\LogAnalyzer\work\&lt;tag&gt;_&lt;id&gt;</c>, per user, deleted after use.</item>
/// </list>
/// </summary>
public static class WorkFolders
{
    public const string CaseWorkFolder = "Work", CaseDerivedFolder = "Derived", CaseMarkerFile = "case.json";

    /// <summary>Process-wide replacement of the per-user work root (test assemblies set it so tests never write the station's real folders).</summary>
    internal static string? UserWorkRootOverride { get; set; }
    private static readonly AsyncLocal<string?> ScopedUserWorkRoot = new();

    /// <summary>Per-test replacement of the per-user work root, restored on dispose.</summary>
    internal static IDisposable UseUserWorkRoot(string root)
    {
        var previous = ScopedUserWorkRoot.Value;
        ScopedUserWorkRoot.Value = root;
        return new Restore(() => ScopedUserWorkRoot.Value = previous);
    }

    public static string UserWorkRoot => ScopedUserWorkRoot.Value ?? UserWorkRootOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "work");

    /// <summary>The case folder that contains <paramref name="path"/> (the nearest ancestor holding case.json, at most 8 levels up), or null.</summary>
    public static string? CaseRootOf(string path)
    {
        string? dir;
        try { dir = Path.GetDirectoryName(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        for (int i = 0; dir is not null && i < 8; i++, dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, CaseMarkerFile))) return dir;
        return null;
    }

    /// <summary>A new, empty working folder for one use with <paramref name="sourcePath"/>; delete it with <see cref="TryDelete"/> in a finally.</summary>
    public static string NewWorkDirectory(string sourcePath, string tag)
    {
        var root = CaseRootOf(sourcePath) is { } caseRoot ? Path.Combine(caseRoot, CaseWorkFolder) : UserWorkRoot;
        var dir = Path.Combine(root, $"{tag}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Removes a working folder (clearing read-only attributes first). A failure is returned, never thrown, so it cannot hide the original error.</summary>
    public static bool TryDelete(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return true;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        private Action? _undo = undo;
        public void Dispose() { _undo?.Invoke(); _undo = null; }
    }
}
