namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// Windows path semantics for paths that are EVIDENCE (a target recorded in a .lnk, a Prefetch executable, an event field),
/// independent of the platform the analysis runs on. <c>System.IO.Path</c> follows the host: on Linux and macOS
/// <c>Path.GetFileName(@"C:\Users\u\a.exe")</c> returns the whole string, so evidence paths must never go through it
/// (PR212 research item 4). Use this class for strings that came from evidence and <c>System.IO.Path</c> only for
/// paths of files on the machine running the analysis.
/// Both '\' and '/' separate components, ':' ends a drive designator, exactly as on Windows.
/// </summary>
public static class WinPath
{
    private static bool IsSep(char c) => c == '\\' || c == '/';

    /// <summary>File name after the last separator or drive colon; "" when the path ends in one. Same results as Windows Path.GetFileName.</summary>
    public static string GetFileName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        int i = path.AsSpan().LastIndexOfAny('\\', '/', ':');
        return i < 0 ? path : path[(i + 1)..];
    }

    public static string GetFileNameWithoutExtension(string? path)
    {
        var name = GetFileName(path);
        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    /// <summary>Extension including the dot; "" when there is none or the name ends in a dot.</summary>
    public static string GetExtension(string? path)
    {
        var name = GetFileName(path);
        int dot = name.LastIndexOf('.');
        return dot < 0 || dot == name.Length - 1 ? "" : name[dot..];
    }

    /// <summary>Length of the root: "C:\" = 3, "C:" = 2, "\\server\share" = through the share, "\" = 1, otherwise 0.</summary>
    public static int GetRootLength(string path)
    {
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0])) return path.Length >= 3 && IsSep(path[2]) ? 3 : 2;
        if (path.Length >= 2 && IsSep(path[0]) && IsSep(path[1]))
        {
            // UNC (or \\?\ / \\.\ device) path: \\server\share[\]
            int i = 2, parts = 0;
            while (i < path.Length && parts < 2)
            {
                while (i < path.Length && !IsSep(path[i])) i++;
                parts++;
                if (parts < 2 && i < path.Length) i++;
            }
            return i;                                  // like .NET: the root of a UNC path has no trailing separator
        }
        return path.Length >= 1 && IsSep(path[0]) ? 1 : 0;
    }

    /// <summary>Everything before the last separator. "" for a bare name, null for a root or an empty input (as Windows Path.GetDirectoryName).</summary>
    public static string? GetDirectoryName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        int root = GetRootLength(path);
        if (root > 0 && path.AsSpan(root).IndexOfAnyExcept('\\', '/') < 0) return null;   // the root itself (or the root plus separators)
        int i = path.Length - 1;
        while (i >= root && !IsSep(path[i])) i--;
        if (i < root) return root > 0 ? path[..root] : "";
        // Collapse a run of separators before the cut, like Windows does for "a\\b".
        int end = i;
        while (end > root && IsSep(path[end - 1])) end--;
        return path[..Math.Max(end, root)];
    }
}
