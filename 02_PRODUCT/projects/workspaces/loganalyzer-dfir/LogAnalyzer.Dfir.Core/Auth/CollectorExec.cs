using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>
/// One program the application itself started (a Windows tool that reads logs, the registry, shadow copies, the audit policy, or the bundled collection
/// script), as written in the sealed audit: "collector.exec" before the start, "collector.exec_done" after it ended.
/// </summary>
public sealed record CollectorExecRecord(string ExecId, string RunId, int AppPid, string AppSha256, string Exe, string ExeSha256, string CommandLine,
                                         string ScriptSha256, DateTimeOffset StartUtc)
{
    public int ChildPid { get; init; }
    public int? ExitCode { get; init; }
    public DateTimeOffset? EndUtc { get; init; }
    /// <summary>Position of the "collector.exec" record in the audit (1-based, non-empty lines).</summary>
    public int Index { get; init; }
}

/// <summary>
/// The single path through which the application starts another program. Before the start it writes "collector.exec" to the sealed audit (control run id,
/// full path of the executable, the exact command line Windows will record, SHA-256 of the executable and of the script it runs, the application's process
/// id and executable hash); after the end it writes "collector.exec_done" (child process id, exit code). A later control lists the events these programs
/// left in the Windows logs as the application's own activity only when they match such a record exactly (<see cref="Analysis.SelfExecMatcher"/>).
/// Nothing is recognised by folder or file name.
/// </summary>
public static class CollectorExec
{
    public const string Exec = "collector.exec", ExecDone = "collector.exec_done";

    /// <summary>Where the records go: (event, detail) → recorded. Set by the application at start (the sealed audit); null = nothing is recorded.</summary>
    public static Func<string, string, bool>? Sink { get; set; }

    private static string _runId = "";
    /// <summary>The id of the station control in progress ("control.started"); empty outside a control (such records are never confirmed).</summary>
    public static string CurrentRunId { get => Volatile.Read(ref _runId); set => Volatile.Write(ref _runId, value ?? ""); }

    private static readonly ConditionalWeakTable<Process, string> Started = new();
    private static readonly Lazy<string> OwnSha = new(() => FileSha256(Environment.ProcessPath ?? ""));

    /// <summary>
    /// Records "collector.exec", then starts the program. <paramref name="scriptPath"/> is the script the program runs (PowerShell -File), whose
    /// SHA-256 is recorded too. The record is written before the start: a program the audit refused to record still runs (its events are then not
    /// recognised and stay ordinary findings).
    /// </summary>
    public static Process Start(ProcessStartInfo psi, string? scriptPath = null)
    {
        var execId = Guid.NewGuid().ToString("N");
        var exe = psi.FileName;
        var detail = ExecDetail(execId, CurrentRunId, Environment.ProcessId, OwnSha.Value, FileSha256(exe),
                                scriptPath is null ? "" : ScriptFileSha256(scriptPath), exe, CommandLineOf(psi));
        Sink?.Invoke(Exec, detail);
        var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        Started.AddOrUpdate(p, execId);
        return p;
    }

    /// <summary>Records "collector.exec_done" for a program started by <see cref="Start"/> (child process id and exit code; -1 when it did not end).</summary>
    public static void Done(Process p)
    {
        if (!Started.TryGetValue(p, out var execId)) return;
        int pid, exit;
        try { pid = p.Id; } catch (InvalidOperationException) { pid = 0; }
        try { exit = p.HasExited ? p.ExitCode : -1; } catch (InvalidOperationException) { exit = -1; }
        Sink?.Invoke(ExecDone, DoneDetail(execId, CurrentRunId, pid, exit, DateTimeOffset.UtcNow));
    }

    // ---- detail lines ----

    /// <summary>The "collector.exec" detail. The executable and the command line come last (in that order): a path or a command may contain "; ".</summary>
    public static string ExecDetail(string execId, string runId, int appPid, string appSha256, string exeSha256, string scriptSha256, string exe, string commandLine) =>
        $"exec={execId}; run={runId}; app_pid={appPid.ToString(CultureInfo.InvariantCulture)}; app_sha256={appSha256}; exe_sha256={exeSha256}; script_sha256={scriptSha256}; exe={exe.ReplaceLineEndings(" ")}; cmd={commandLine.ReplaceLineEndings(" ")}";

    public static string DoneDetail(string execId, string runId, int childPid, int exitCode, DateTimeOffset endUtc) =>
        $"exec={execId}; run={runId}; child_pid={childPid.ToString(CultureInfo.InvariantCulture)}; exit={exitCode.ToString(CultureInfo.InvariantCulture)}; end={endUtc.UtcDateTime:o}";

    public static bool TryParseExec(string detail, DateTimeOffset whenUtc, out CollectorExecRecord record)
    {
        record = null!;
        var exeAt = detail.IndexOf("; exe=", StringComparison.Ordinal);
        if (exeAt < 0) return false;
        var cmdAt = detail.IndexOf("; cmd=", exeAt, StringComparison.Ordinal);
        if (cmdAt < 0) return false;
        var kv = Pairs(detail[..exeAt]);
        if (!kv.TryGetValue("exec", out var id) || id.Length == 0) return false;
        if (!int.TryParse(kv.GetValueOrDefault("app_pid", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var appPid)) return false;
        record = new CollectorExecRecord(id, kv.GetValueOrDefault("run", ""), appPid, kv.GetValueOrDefault("app_sha256", ""), detail[(exeAt + 6)..cmdAt],
            kv.GetValueOrDefault("exe_sha256", ""), detail[(cmdAt + 6)..], kv.GetValueOrDefault("script_sha256", ""), whenUtc);
        return true;
    }

    public static bool TryParseDone(string detail, out (string ExecId, string RunId, int ChildPid, int ExitCode, DateTimeOffset EndUtc) done)
    {
        done = default;
        var kv = Pairs(detail);
        var ci = CultureInfo.InvariantCulture;
        if (!kv.TryGetValue("exec", out var id) || id.Length == 0) return false;
        if (!int.TryParse(kv.GetValueOrDefault("child_pid", ""), NumberStyles.Integer, ci, out var pid)) return false;
        if (!int.TryParse(kv.GetValueOrDefault("exit", ""), NumberStyles.Integer, ci, out var exit)) return false;
        if (!DateTimeOffset.TryParse(kv.GetValueOrDefault("end", ""), ci, DateTimeStyles.RoundtripKind, out var end)) return false;
        done = (id, kv.GetValueOrDefault("run", ""), pid, exit, end);
        return true;
    }

    private static Dictionary<string, string> Pairs(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in text.Split("; "))
            if (kv.IndexOf('=') is var eq and > 0) d[kv[..eq].Trim()] = kv[(eq + 1)..].Trim();
        return d;
    }

    // ---- what Windows records ----

    /// <summary>
    /// The command line Windows receives (and writes in 4688 / Sysmon 1) for this start: the executable in quotes, then <see cref="ProcessStartInfo.Arguments"/>
    /// as given or every <see cref="ProcessStartInfo.ArgumentList"/> element quoted the way .NET quotes it.
    /// </summary>
    public static string CommandLineOf(ProcessStartInfo psi)
    {
        var sb = new StringBuilder();
        var file = psi.FileName.Trim();
        bool quoted = file.Length >= 2 && file[0] == '"' && file[^1] == '"';
        if (!quoted) sb.Append('"');
        sb.Append(file);
        if (!quoted) sb.Append('"');
        if (!string.IsNullOrEmpty(psi.Arguments)) sb.Append(' ').Append(psi.Arguments);
        else foreach (var a in psi.ArgumentList) { sb.Append(' '); AppendArgument(sb, a); }
        return sb.ToString();
    }

    /// <summary>The quoting rule of .NET for one argument (System.PasteArguments): unchanged without spaces or quotes, otherwise quoted with backslashes escaped before quotes.</summary>
    private static void AppendArgument(StringBuilder sb, string arg)
    {
        if (arg.Length != 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"')) { sb.Append(arg); return; }
        sb.Append('"');
        for (var i = 0; i < arg.Length;)
        {
            var c = arg[i++];
            if (c == '\\')
            {
                var n = 1;
                while (i < arg.Length && arg[i] == '\\') { i++; n++; }
                if (i == arg.Length) sb.Append('\\', n * 2);
                else if (arg[i] == '"') { sb.Append('\\', n * 2 + 1).Append('"'); i++; }
                else sb.Append('\\', n);
                continue;
            }
            if (c == '"') { sb.Append('\\').Append('"'); continue; }
            sb.Append(c);
        }
        sb.Append('"');
    }

    // ---- hashes ----

    public static string FileSha256(string path)
    {
        try
        {
            if (path.Length == 0 || !File.Exists(path)) return "";
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return Convert.ToHexStringLower(SHA256.HashData(s));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>The hash a PowerShell script block (4104 ScriptBlockText) is compared by: the text with line endings as "\n", UTF-8.</summary>
    public static string ScriptSha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));

    /// <summary><see cref="ScriptSha256"/> of a script file (read as text: the BOM is not part of the script block).</summary>
    public static string ScriptFileSha256(string path)
    {
        try { return File.Exists(path) ? ScriptSha256(File.ReadAllText(path)) : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
}
