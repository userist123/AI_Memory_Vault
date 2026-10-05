using System.Diagnostics;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>An independent implementation available on this machine, used as a reference for differential validation.</summary>
public static class Differential
{
    private static readonly Lazy<string?> PythonLazy = new(() =>
    {
        foreach (var exe in new[] { "python", "py" })
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, "-c \"import sqlite3,sys;print(sqlite3.sqlite_version)\"")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                if (p is null) continue;
                p.WaitForExit(15_000);
                if (p.ExitCode == 0) return exe;
            }
            catch (System.ComponentModel.Win32Exception) { /* not installed under this name */ }
        return null;
    });

    public static string? Python => PythonLazy.Value;

    /// <summary>Runs a Python script (passed through a temp file) and returns its stdout lines.</summary>
    public static string[] RunPython(string script, params string[] args)
    {
        var file = Path.Combine(Path.GetTempPath(), $"ladfir_ref_{Guid.NewGuid():N}.py");
        File.WriteAllText(file, script);
        try
        {
            var psi = new ProcessStartInfo(Python!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8 };
            psi.ArgumentList.Add(file);
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException($"Referința Python a eșuat: {stderr}");
            return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        }
        finally { File.Delete(file); }
    }
}

/// <summary>
/// A corpus test that also needs an independent reference implementation (Python's sqlite3). Skipped — with the reason
/// "DIFFERENTIAL REFERENCE UNAVAILABLE" — when either the corpus file or the reference is missing; never passes without both.
/// </summary>
public sealed class DifferentialFactAttribute : FactAttribute
{
    public DifferentialFactAttribute(string section)
    {
        try
        {
            var f = Corpus.File(section);
            if (!File.Exists(f)) { Skip = $"Regression corpus not present: {f} — FORENSIC VALIDATION = UNAVAILABLE"; return; }
        }
        catch (Exception ex) { Skip = $"Regression corpus unavailable: {ex.Message}"; return; }
        if (Differential.Python is null) Skip = "Python sqlite3 not found — DIFFERENTIAL REFERENCE UNAVAILABLE";
    }
}
