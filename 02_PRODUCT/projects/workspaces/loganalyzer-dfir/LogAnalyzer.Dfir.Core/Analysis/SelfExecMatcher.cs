using LogAnalyzer.Dfir.Auth;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// A process start (Security 4688, Sysmon 1) or a PowerShell script block (4104) as the matcher sees it. Empty / 0 = not in the event.
/// </summary>
public sealed record ProcessObservation(DateTimeOffset TimeUtc, string Kind, int ProcessId, int ParentProcessId, string Image, string CommandLine,
                                        string ImageSha256 = "", string ScriptBlockText = "")
{
    public bool IsScriptBlock => Kind == "4104";
}

public enum SelfExecKind
{
    /// <summary>Not a program of the application: the rules treat it as any other event.</summary>
    None,
    /// <summary>Matches exactly a valid sealed "collector.exec" record: the application's own activity, listed apart, never a finding.</summary>
    Own,
    /// <summary>Matches a "collector.exec" record that fails validation: claimed to be the application's, not confirmed (DE VERIFICAT); stays a finding.</summary>
    Claimed,
}

public sealed record SelfExecMatch(SelfExecKind Kind, CollectorExecRecord? Record, string Why)
{
    public static readonly SelfExecMatch NoMatch = new(SelfExecKind.None, null, "");
}

/// <summary>
/// Decides whether an event in the Windows logs was left by a program the application itself started (owner rule 2026-10-16: the tools the application uses
/// to read logs, the registry, shadow copies or the audit policy are not abuse). Recognition never uses folder or file names: an event is the application's
/// own only when it matches a sealed "collector.exec" record that passed <see cref="OperationAudit.ReadCollectorExecs"/> exactly:
/// <list type="bullet">
/// <item>time inside the record's window (from the start record to its end record, at most <see cref="OperationAudit.MaxExecWindow"/>);</item>
/// <item>process start: the same child process id, or the application's process id as parent; the same executable path; the same command line;
/// and, when the event carries the executable's SHA-256 (Sysmon), the hash recorded at start;</item>
/// <item>script block: the SHA-256 of the block text equals the recorded script hash, and the process id (when present) is the recorded child.</item>
/// </list>
/// A look-alike (same command at another time, another process, another file) is not the application's and stays an ordinary event. An event that matches a
/// refused record (outside a valid control run, broken chain, other application build, no end record) is "activitate pretinsă a aplicației, neconfirmată".
/// </summary>
public static class SelfExecMatcher
{
    public const string OwnLabel = "activitate proprie a aplicației";
    public const string ClaimedLabel = "activitate pretinsă a aplicației, neconfirmată";

    /// <summary>Rounding between the audit clock and the event clock (same machine, same clock): a few seconds, never minutes.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    public static SelfExecMatch Classify(ProcessObservation o, OperationAudit.CollectorExecs execs)
    {
        foreach (var r in execs.Valid)
            if (Matches(o, r)) return new(SelfExecKind.Own, r, "");
        foreach (var (r, why) in execs.Refused)
            if (Matches(o, r)) return new(SelfExecKind.Claimed, r, why);
        return SelfExecMatch.NoMatch;
    }

    internal static bool Matches(ProcessObservation o, CollectorExecRecord r)
    {
        var end = r.EndUtc is { } e && e - r.StartUtc <= OperationAudit.MaxExecWindow ? e : r.StartUtc + OperationAudit.MaxExecWindow;
        if (o.TimeUtc < r.StartUtc - Slack || o.TimeUtc > end + Slack) return false;
        if (o.IsScriptBlock)
            return r.ScriptSha256.Length == 64 && o.ScriptBlockText.Length > 0
                && CollectorExec.ScriptSha256(o.ScriptBlockText).Equals(r.ScriptSha256, StringComparison.OrdinalIgnoreCase)
                && (o.ProcessId <= 0 || (r.ChildPid > 0 && o.ProcessId == r.ChildPid));
        bool process = (r.ChildPid > 0 && o.ProcessId == r.ChildPid) || (r.AppPid > 0 && o.ParentProcessId == r.AppPid);
        return process
            && SamePath(o.Image, r.Exe)
            && o.CommandLine.Trim().Equals(r.CommandLine.Trim(), StringComparison.Ordinal)
            && (o.ImageSha256.Length == 0 || o.ImageSha256.Equals(r.ExeSha256, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SamePath(string a, string b) =>
        a.Length > 0 && b.Length > 0 && Full(a).Equals(Full(b), StringComparison.OrdinalIgnoreCase);

    private static string Full(string p)
    {
        p = p.Trim().Trim('"');
        try { return Path.IsPathFullyQualified(p) ? Path.GetFullPath(p) : p; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return p; }
    }
}
