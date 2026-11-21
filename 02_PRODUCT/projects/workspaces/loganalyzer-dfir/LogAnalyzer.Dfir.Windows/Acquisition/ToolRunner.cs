using System.Diagnostics;
using System.Text;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <param name="OutputLimitHit">The tool wrote more than the caller's limit and was stopped; its output files are cut at the limit.</param>
public sealed record ToolRun(string Executable, IReadOnlyList<string> Arguments, int ExitCode, string StdoutPath, string StderrPath,
                             DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool TimedOut, bool OutputLimitHit = false)
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
    /// <param name="configure">Last word on the start info before the start (environment, working folder); the shell and redirection stay as set here.</param>
    /// <param name="maxOutputBytes">When above zero: stdout is copied byte for byte (not re-encoded) and the tool is stopped as soon as stdout or stderr passes this size.</param>
    public static ToolRun Run(string exe, IEnumerable<string> args, string logDir, string label, TimeSpan timeout, CancellationToken ct, Encoding? outputEncoding = null,
                              Action<ProcessStartInfo>? configure = null, long maxOutputBytes = 0)
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
        configure?.Invoke(psi);
        psi.UseShellExecute = false;

        var start = DateTimeOffset.UtcNow;
        // through the one recording path: "collector.exec" in the sealed audit before the start, "collector.exec_done" after the end
        using var p = LogAnalyzer.Dfir.Auth.CollectorExec.Start(psi);
        bool limitHit = false;
        // never throws: it runs on the copy threads, where an exception would end the application
        void OverLimit() { limitHit = true; try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (Exception) { } }
        StreamWriter? outW = null, errW = null;
        Task? rawCopy = null, rawErr = null;
        if (maxOutputBytes > 0)
        {
            // byte for byte, counted as it arrives (stdout and stderr): a tool that floods is stopped at the limit, not after it has filled the disk or memory
            Task Copy(Stream src, string path) => Task.Run(() =>
            {
                using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                var buf = new byte[81920];
                long total = 0;
                int n;
                try
                {
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (total + n > maxOutputBytes) { dst.Write(buf, 0, (int)(maxOutputBytes - total)); OverLimit(); break; }
                        dst.Write(buf, 0, n);
                        total += n;
                    }
                }
                catch (IOException) { }   // the pipe breaks when the process is stopped
            });
            rawCopy = Copy(p.StandardOutput.BaseStream, outPath);
            rawErr = Copy(p.StandardError.BaseStream, errPath);
        }
        else
        {
            outW = new StreamWriter(outPath, false, new UTF8Encoding(false));
            errW = new StreamWriter(errPath, false, new UTF8Encoding(false));
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (outW) outW.WriteLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (errW) errW.WriteLine(e.Data); };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
        }

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
        // the pipes close with the process; a child that keeps them open is stopped, and the run is then a timeout, never a half-copied success
        foreach (var copy in new[] { rawCopy, rawErr })
            if (copy is not null && !copy.Wait(TimeSpan.FromMinutes(1)))
            {
                timedOut = true;
                try { p.Kill(entireProcessTree: true); } catch (Exception) { }
                copy.Wait(TimeSpan.FromSeconds(30));
            }
        if (outW is not null) lock (outW) outW.Dispose();
        if (errW is not null) lock (errW) errW.Dispose();
        LogAnalyzer.Dfir.Auth.CollectorExec.Done(p);
        ct.ThrowIfCancellationRequested();
        return new ToolRun(exe, argList, timedOut ? -1 : p.ExitCode, outPath, errPath, start, DateTimeOffset.UtcNow, timedOut, limitHit);
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
