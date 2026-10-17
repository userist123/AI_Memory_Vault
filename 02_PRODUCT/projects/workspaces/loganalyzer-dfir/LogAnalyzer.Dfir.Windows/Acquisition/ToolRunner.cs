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
    public static ToolRun Run(string exe, IEnumerable<string> args, string logDir, string label, TimeSpan timeout, CancellationToken ct, Encoding? outputEncoding = null)
    {
        Directory.CreateDirectory(logDir);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string outPath = Path.Combine(logDir, $"{label}_{stamp}.stdout.txt"), errPath = Path.Combine(logDir, $"{label}_{stamp}.stderr.txt");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding ?? Encoding.UTF8, StandardErrorEncoding = outputEncoding ?? Encoding.UTF8,
        };
        var argList = args.ToList();
        foreach (var a in argList) psi.ArgumentList.Add(a);

        var start = DateTimeOffset.UtcNow;
        // through the one recording path: "collector.exec" in the sealed audit before the start, "collector.exec_done" after the end
        using var p = LogAnalyzer.Dfir.Auth.CollectorExec.Start(psi);
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
        LogAnalyzer.Dfir.Auth.CollectorExec.Done(p);
        ct.ThrowIfCancellationRequested();
        return new ToolRun(exe, argList, timedOut ? -1 : p.ExitCode, outPath, errPath, start, DateTimeOffset.UtcNow, timedOut);
    }

    /// <summary>The console OEM code page that console tools such as bcdedit write in (cp852 / cp850 ...); falls back to UTF-8 when the code page is not available.</summary>
    public static Encoding ConsoleOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Encoding.UTF8; }
    }

    public static string FileVersion(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).FileVersion ?? ""; }
        catch (FileNotFoundException) { return ""; }
    }

    public static string System32(string exe) => Path.Combine(Environment.SystemDirectory, exe);
}
