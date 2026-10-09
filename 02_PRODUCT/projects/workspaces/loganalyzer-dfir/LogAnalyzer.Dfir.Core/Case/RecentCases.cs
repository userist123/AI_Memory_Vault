using System.Text.Json;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Case;

/// <summary>How an existing case was found when it was opened (WP5). Anything other than <see cref="Active"/> is shown read-only.</summary>
public enum CaseLifecycle { Active, Sealed, Archived, Invalidated, IntegrityFailed }

public static class CaseLifecycleNames
{
    public static string Spec(CaseLifecycle l) => l switch
    {
        CaseLifecycle.Active => "ACTIVE", CaseLifecycle.Sealed => "SEALED", CaseLifecycle.Archived => "ARCHIVED",
        CaseLifecycle.Invalidated => "INVALIDATED", _ => "INTEGRITY_FAILED",
    };

    public static string Label(CaseLifecycle l) => l switch
    {
        CaseLifecycle.Active => "activ", CaseLifecycle.Sealed => "sigilat (închis)", CaseLifecycle.Archived => "arhivat",
        CaseLifecycle.Invalidated => "invalidat", _ => "integritate eșuată",
    };
}

/// <summary>One remembered case. <see cref="State"/> is the last known lifecycle (spec name), not a live reading.</summary>
public sealed record RecentCase(string Path, string Title, DateTimeOffset LastOpenedUtc, string State);

/// <summary>A remembered case with what the folder looks like now. A folder that no longer exists is shown as missing, never dropped silently.</summary>
public sealed record RecentCaseView(RecentCase Entry, bool Exists)
{
    public string StateText => Exists ? Entry.State : "LIPSĂ (folderul nu mai există)";
}

/// <summary>
/// The list of recently opened cases, stored per user (default: %LOCALAPPDATA%\LogAnalyzer\recent_cases.json). It only remembers paths; it
/// grants nothing, and a case is always opened through <see cref="CaseWorkspace.Open"/> with its integrity re-check. Entries leave the list only
/// through <see cref="Forget"/>, which the operator calls explicitly.
/// </summary>
public sealed class RecentCases(string file)
{
    public const int MaxEntries = 20;
    private readonly object _gate = new();

    public string File { get; } = file;

    public static string DefaultFile() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "recent_cases.json");

    public IReadOnlyList<RecentCaseView> Load()
    {
        lock (_gate) return ReadEntries().Select(e => new RecentCaseView(e, Directory.Exists(e.Path))).ToList();
    }

    /// <summary>Remembers (or moves to the top) a case that was just opened. A list that cannot be written is not an error for the operator: the case is open anyway.</summary>
    public void Record(string path, string title, CaseLifecycle state, DateTimeOffset? at = null)
    {
        var full = System.IO.Path.GetFullPath(path);
        lock (_gate)
        {
            var list = ReadEntries().Where(e => !SamePath(e.Path, full)).ToList();
            list.Insert(0, new RecentCase(full, title, at ?? DateTimeOffset.UtcNow, CaseLifecycleNames.Spec(state)));
            Write(list.Take(MaxEntries).ToList());
        }
    }

    public void Forget(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        lock (_gate) Write(ReadEntries().Where(e => !SamePath(e.Path, full)).ToList());
    }

    private static bool SamePath(string a, string b) => string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private List<RecentCase> ReadEntries()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return [];
            return JsonSerializer.Deserialize<List<RecentCase>>(System.IO.File.ReadAllText(File), Json.Options) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    private void Write(List<RecentCase> list)
    {
        try { Json.Write(File, list); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the list is a convenience */ }
    }
}
