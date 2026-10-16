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

/// <summary>One 4104 event of a script block that Windows split over several events (same ScriptBlockId, MessageNumber of MessageTotal).</summary>
public sealed record ScriptBlockPart(DateTimeOffset TimeUtc, int ProcessId, string ScriptBlockId, int Number, int Total, string Text);

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

    /// <summary>
    /// A script block split over several 4104 events (finding B5), all with the same ScriptBlockId and process. The parts are reassembled in MessageNumber order
    /// and the text is matched like a single block. A set that is not exactly 1..MessageTotal, each number once and one agreed total, is never confirmed: when it
    /// falls inside the window of a program record that carries a script hash it is "Claimed" (DE VERIFICAT) with the reason; otherwise it is not the application's.
    /// Parts of different ScriptBlockIds are never merged: the caller passes one set per (ScriptBlockId, process).
    /// </summary>
    public static SelfExecMatch ClassifySplit(IReadOnlyList<ScriptBlockPart> parts, OperationAudit.CollectorExecs execs)
    {
        if (parts.Count == 0) return SelfExecMatch.NoMatch;
        var first = parts.MinBy(p => p.TimeUtc)!; var last = parts.MaxBy(p => p.TimeUtc)!;
        var totals = parts.Select(p => p.Total).Distinct().ToList();
        var numbers = parts.Select(p => p.Number).ToList();
        string? problem = null;
        if (parts.Select(p => p.ScriptBlockId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 || parts.Select(p => p.ProcessId).Distinct().Count() != 1) problem = "părțile nu aparțin aceluiași bloc și proces";
        else if (totals.Count != 1 || totals[0] < 2) problem = "părțile nu au același număr total de mesaje";
        else if (numbers.Distinct().Count() != numbers.Count) problem = "o parte apare de mai multe ori (duplicat)";
        else if (numbers.Any(n => n < 1 || n > totals[0]) || numbers.Count != totals[0]) problem = $"lipsesc părți: {numbers.Count} din {totals[0]}";
        if (problem is null)
        {
            var ordered = parts.OrderBy(p => p.Number).ToList();
            var text = string.Concat(ordered.Select(p => p.Text));
            var a = Classify(new ProcessObservation(first.TimeUtc, "4104", first.ProcessId, 0, "", "", "", text), execs);
            var b = Classify(new ProcessObservation(last.TimeUtc, "4104", last.ProcessId, 0, "", "", "", text), execs);
            if (a.Kind == SelfExecKind.Own && b.Kind == SelfExecKind.Own && a.Record!.ExecId == b.Record!.ExecId) return a;
            if (a.Kind == SelfExecKind.Claimed) return a;
            if (b.Kind == SelfExecKind.Claimed) return b;
            return SelfExecMatch.NoMatch;
        }
        foreach (var r in execs.Valid.Concat(execs.Refused.Select(x => x.Record)))
            if (r.ScriptSha256.Length == 64 && InWindow(first.TimeUtc, r) && (first.ProcessId <= 0 || r.ChildPid <= 0 || first.ProcessId == r.ChildPid))
                return new(SelfExecKind.Claimed, r, $"bloc de script împărțit în mai multe evenimente 4104 (ScriptBlockId {first.ScriptBlockId}), set incomplet sau inconsecvent: {problem}; amprenta scriptului nu poate fi verificată");
        return SelfExecMatch.NoMatch;
    }

    private static bool InWindow(DateTimeOffset t, CollectorExecRecord r)
    {
        var end = r.EndUtc is { } e && e - r.StartUtc <= OperationAudit.MaxExecWindow ? e : r.StartUtc + OperationAudit.MaxExecWindow;
        return t >= r.StartUtc - Slack && t <= end + Slack;
    }

    internal static bool Matches(ProcessObservation o, CollectorExecRecord r)
    {
        if (!InWindow(o.TimeUtc, r)) return false;
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
