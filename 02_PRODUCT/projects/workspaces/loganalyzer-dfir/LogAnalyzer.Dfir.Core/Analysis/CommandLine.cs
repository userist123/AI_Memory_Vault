namespace LogAnalyzer.Dfir.Analysis;

/// <summary>Executable part of a command stored in a Run key, service, task or Winlogon value (no environment expansion).</summary>
public static class CommandLine
{
    private static readonly string[] Extensions = [".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr", ".msc", ".cpl", ".dll", ".hta", ".sys"];

    public static string Executable(string command)
    {
        var c = command.Trim();
        if (c.Length == 0) return "";
        if (c[0] == '"')
        {
            int end = c.IndexOf('"', 1);
            return end > 0 ? c[1..end] : c[1..];
        }
        // Unquoted paths may contain spaces ("C:\Program Files\A B\x.exe -arg"): cut after the first executable extension
        // that ends a token.
        int best = -1;
        foreach (var ext in Extensions)
            for (int i = c.IndexOf(ext, StringComparison.OrdinalIgnoreCase); i >= 0; i = c.IndexOf(ext, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                int after = i + ext.Length;
                if (after == c.Length || c[after] is ' ' or ',' or '\t')
                {
                    if (best < 0 || after < best) best = after;
                    break;
                }
            }
        if (best > 0) return c[..best];
        int sp = c.IndexOf(' ');
        return sp > 0 ? c[..sp] : c;
    }
}
