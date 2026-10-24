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

    /// <summary>The run a started program belongs to (a station control, or the action window of a command started outside one) with its execution id.</summary>
    private sealed record StartedExec(string ExecId, string RunId, bool OwnsActionWindow);

    private static readonly ConditionalWeakTable<Process, StartedExec> Started = new();

    /// <summary>Prefix of the run id of an action window: "act-" + the execution id of the one program it covers (never a control run id).</summary>
    public const string ActionRunPrefix = "act-";
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
        // A command started outside a station control (investigation, containment) gets an action window of its own (finding B5): "action.started" with the
        // run id and the application hash BEFORE "collector.exec", "action.finished" after "collector.exec_done". The run id is the one the application
        // wrote to its sealed audit; nothing in a process name or command line can create one.
        var runId = CurrentRunId;
        bool action = runId.Length == 0;
        if (action)
        {
            runId = ActionRunPrefix + execId;
            Sink?.Invoke(OperationAudit.ActionStarted, OperationAudit.ActionStartedDetail(runId, OwnSha.Value, Path.GetFileName(exe)));
        }
        var detail = ExecDetail(execId, runId, Environment.ProcessId, OwnSha.Value, FileSha256(exe),
                                scriptPath is null ? "" : ScriptFileSha256(scriptPath), exe, CommandLineOf(psi));
        Sink?.Invoke(Exec, detail);
        Process p;
        try { p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}"); }
        catch
        {
            if (action) Sink?.Invoke(OperationAudit.ActionFinished, OperationAudit.ActionFinishedDetail(runId));   // the window never stays open
            throw;
        }
        Started.AddOrUpdate(p, new StartedExec(execId, runId, action));
        return p;
    }

    /// <summary>Records "collector.exec_done" for a program started by <see cref="Start"/> (child process id and exit code; -1 when it did not end).</summary>
    public static void Done(Process p)
    {
        if (!Started.TryGetValue(p, out var started)) return;
        Started.Remove(p);                                   // the first end counts; a second call writes nothing
        int pid, exit;
        try { pid = p.Id; } catch (InvalidOperationException) { pid = 0; }
        try { exit = p.HasExited ? p.ExitCode : -1; } catch (InvalidOperationException) { exit = -1; }
        Sink?.Invoke(ExecDone, DoneDetail(started.ExecId, started.RunId, pid, exit, DateTimeOffset.UtcNow));
        if (started.OwnsActionWindow) Sink?.Invoke(OperationAudit.ActionFinished, OperationAudit.ActionFinishedDetail(started.RunId));
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

    // ---- the fixed inventory ----

    /// <summary>
    /// The only programs a "collector.exec" record may name to be accepted as the application's own (review of PR #296): the Windows tools the application
    /// starts, by their fixed location (System32 from <see cref="Environment.SystemDirectory"/>, Defender under Program Files). Any other program is refused.
    /// </summary>
    public static IReadOnlyList<string> Inventory()
    {
        var sys = Environment.SystemDirectory;
        string[] extra;
        lock (Extra) extra = [.. Extra];
        return
        [
            .. new[] { "reg.exe", "esentutl.exe", "bcdedit.exe", "auditpol.exe" }.Select(n => Path.Combine(sys, n)),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe"),
            .. extra,
        ];
    }

    private static readonly List<string> Extra = [];

    /// <summary>
    /// Adds a program to the inventory: called only by the edition code that starts it (LogAnalyzer.Response, registered by the Unclassified and Network
    /// compositions: the firewall tool). The Standalone edition has no such code and adds nothing.
    /// </summary>
    internal static void AddToInventory(string fullPath)
    {
        if (!Path.IsPathFullyQualified(fullPath)) throw new ArgumentException("The inventory takes full paths only.", nameof(fullPath));
        lock (Extra) if (!Extra.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) Extra.Add(fullPath);
    }

    /// <summary>A script interpreter the application starts for exactly one script: its path, the script-block hash of the build and the command-line test.</summary>
    internal sealed record ScriptHost(string Exe, string ScriptSha256, Func<string, bool> RunsTheScript);

    private static readonly List<ScriptHost> Hosts = [];

    /// <summary>
    /// Registers the interpreter that runs the application's collection script (only the editions that ship it). A record for that interpreter is accepted only
    /// when its recorded script hash is <paramref name="scriptSha256"/> (the script embedded at build time) and <paramref name="runsTheScript"/> accepts its
    /// command line (exactly the argument form the application emits).
    /// </summary>
    internal static void AddScriptHost(string exeFullPath, string scriptSha256, Func<string, bool> runsTheScript)
    {
        if (scriptSha256.Length != 64) throw new ArgumentException("The build-time script hash is missing.", nameof(scriptSha256));
        AddToInventory(exeFullPath);
        lock (Hosts)
        {
            Hosts.RemoveAll(h => h.Exe.Equals(exeFullPath, StringComparison.OrdinalIgnoreCase));
            Hosts.Add(new ScriptHost(exeFullPath, scriptSha256, runsTheScript));
        }
    }

    private static readonly string[] Interpreters = ["powershell.exe", "pwsh.exe", "powershell_ise.exe"];

    /// <summary>
    /// Why a record for a script interpreter is not the application's own (null = it is, or the program is not an interpreter): a PowerShell record must be
    /// for a registered host, carry the build-time script hash and run exactly the collection script.
    /// </summary>
    internal static string? ScriptHostRefusal(CollectorExecRecord r)
    {
        ScriptHost? host;
        lock (Hosts) host = Hosts.FirstOrDefault(h => Normalize(h.Exe).Equals(Normalize(r.Exe), StringComparison.OrdinalIgnoreCase));
        if (host is null)
            return Interpreters.Contains(Path.GetFileName(r.Exe.Trim().Trim('"')), StringComparer.OrdinalIgnoreCase)
                ? "PowerShell este acceptat doar pentru scriptul de colectare al aplicației, iar această ediție nu îl are" : null;
        if (!r.ScriptSha256.Equals(host.ScriptSha256, StringComparison.OrdinalIgnoreCase))
            return "amprenta scriptului consemnată nu este cea a scriptului de colectare fixat la construirea aplicației";
        return host.RunsTheScript(r.CommandLine) ? null : "linia de comandă PowerShell nu rulează exact scriptul de colectare al aplicației, în forma în care îl pornește aplicația";
    }

    /// <summary>True when <paramref name="exe"/> is, after normalisation and ignoring case, one of the inventory paths.</summary>
    public static bool InInventory(string exe, IEnumerable<string>? inventory = null)
    {
        var n = Normalize(exe);
        return n.Length > 0 && ((inventory ?? Inventory()).Any(i => Normalize(i).Equals(n, StringComparison.OrdinalIgnoreCase)) || IsDefenderPlatformMpCmdRun(exe));
    }

    private static readonly System.Text.RegularExpressions.Regex DefenderPlatformVersion =
        new(@"^[0-9]+(\.[0-9]+){3}(-[0-9]+)?\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// True only for exactly "%ProgramData%\Microsoft\Windows Defender\Platform\&lt;version&gt;\MpCmdRun.exe" (finding B9: the Defender command-line tool of the
    /// platform folder, which the stub under Program Files starts). The path must already be in its canonical form (the same as Path.GetFullPath returns, so no
    /// "..", no doubled or alternate separators, no 8.3 names, no trailing dot or space), a drive path (no UNC or device path), without an alternate data stream,
    /// with exactly two segments after "Platform" (a version such as 4.18.26080.3-0, then the file); the comparison ignores case only.
    /// </summary>
    public static bool IsDefenderPlatformMpCmdRun(string exe)
    {
        var raw = exe.Trim('"');            // no whitespace trimming: a trailing space or dot is a different spelling and is refused
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (raw.Length < 4 || programData.Length < 3 || !Path.IsPathFullyQualified(raw) || raw.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (raw.IndexOf(':', 2) >= 0 || raw.Contains("..", StringComparison.Ordinal) || raw.Contains('/') || raw.Contains('?') || raw.Contains('*')) return false;
        string full;
        try { full = Path.GetFullPath(raw); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        if (!full.Equals(raw, StringComparison.OrdinalIgnoreCase)) return false;
        var prefix = Path.Combine(Path.GetFullPath(programData), "Microsoft", "Windows Defender", "Platform") + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = full[prefix.Length..].Split(Path.DirectorySeparatorChar);
        return rest.Length == 2 && DefenderPlatformVersion.IsMatch(rest[0]) && rest[1].Equals("MpCmdRun.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        path = path.Trim().Trim('"');
        try { return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : ""; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return ""; }
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
