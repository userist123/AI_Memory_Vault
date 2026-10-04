using System.Diagnostics;
using System.Text;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

public sealed record ToolRun(string Executable, IReadOnlyList<string> Arguments, int ExitCode, string StdoutPath, string StderrPath,
                             DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool TimedOut)
{
    public string CommandLine => Quote(Executable) + " " + string.Join(' ', Arguments.Select(Quote));
    private static string Quote(string a) => a.Contains(' ') || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;
}

/// <summary>
/// Runs external tools with an explicit executable path and ArgumentList (no shell, no string concatenation:
/// spec §116, §120, §121). stdout/stderr always go to files inside the case so nothing is lost.
/// </summary>
public static class ToolRunner
{
    public static ToolRun Run(string exe, IEnumerable<string> args, string logDir, string label, TimeSpan timeout, CancellationToken ct)
    {
        Directory.CreateDirectory(logDir);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string outPath = Path.Combine(logDir, $"{label}_{stamp}.stdout.txt"), errPath = Path.Combine(logDir, $"{label}_{stamp}.stderr.txt");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        var argList = args.ToList();
        foreach (var a in argList) psi.ArgumentList.Add(a);

        var start = DateTimeOffset.UtcNow;
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        using var outW = new StreamWriter(outPath, false, new UTF8Encoding(false));
        using var errW = new StreamWriter(errPath, false, new UTF8Encoding(false));
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (outW) outW.WriteLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (errW) errW.WriteLine(e.Data); };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();

        bool timedOut = false;
        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }))
        {
            if (!p.WaitForExit(timeout))
            {
                timedOut = true;
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
            p.WaitForExit();
        }
        ct.ThrowIfCancellationRequested();
        return new ToolRun(exe, argList, timedOut ? -1 : p.ExitCode, outPath, errPath, start, DateTimeOffset.UtcNow, timedOut);
    }

    public static string FileVersion(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).FileVersion ?? ""; }
        catch (FileNotFoundException) { return ""; }
    }

    public static string System32(string exe) => Path.Combine(Environment.SystemDirectory, exe);
}
