using System.Globalization;
using LogAnalyzer.Dfir.Analysis;
using Ro = LogAnalyzer.Dfir.Language.RoText;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// The application's own programs (owner rule 2026-10-16): the Windows tools and the bundled script the application starts to read logs, the registry, shadow
/// copies or the audit policy are not abuse. An event they left (4688, Sysmon 1, PowerShell 4104) is taken out of every other check only when it matches a
/// valid sealed "collector.exec" record exactly (<see cref="SelfExecMatcher"/>), and is listed apart by A17. Nothing is recognised by folder or file name.
/// An event matching a record that fails validation stays in the checks and A17 reports it as claimed, not confirmed.
/// </summary>
public static partial class ControlEvaluator
{
    private const string SysmonChannel = "Microsoft-Windows-Sysmon/Operational", PowerShellChannel = "Microsoft-Windows-PowerShell/Operational";

    /// <summary>The event as the matcher sees it; null for an event that is neither a process start nor a script block.</summary>
    internal static ProcessObservation? Observation(LogFact l) => (l.Channel, l.Id) switch
    {
        ("Security", 4688) => new(l.TimeUtc, "4688", Pid(V(l, "NewProcessId")), Pid(V(l, "ProcessId")), V(l, "NewProcessName"), V(l, "CommandLine")),
        (SysmonChannel, 1) => new(l.TimeUtc, "Sysmon 1", Pid(V(l, "ProcessId")), Pid(V(l, "ParentProcessId")), V(l, "Image"), V(l, "CommandLine"), Sha256Of(V(l, "Hashes"))),
        (PowerShellChannel, 4104) => new(l.TimeUtc, "4104", Pid(V(l, "ProcessId")), 0, "", "", "", V(l, "ScriptBlockText")),
        _ => null,
    };

    private static int Pid(string v)
    {
        v = v.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : 0;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static string Sha256Of(string hashes)
    {
        foreach (var part in hashes.Split(','))
            if (part.Trim().StartsWith("SHA256=", StringComparison.OrdinalIgnoreCase)) return part.Trim()[7..];
        return "";
    }

    /// <summary>
    /// Moves the events of the application's own programs out of <see cref="StationFacts.Logs"/> (into <see cref="StationFacts.OwnActivity"/>) so no check
    /// counts them, and fills <see cref="StationFacts.ClaimedActivity"/> with the events that only claim to be the application's (they stay in the logs).
    /// </summary>
    private static void SeparateOwnActivity(StationFacts f)
    {
        f.ClaimedActivity.Clear();
        if (f.CollectorExecs is not { } execs) return;
        var own = new List<LogFact>();
        foreach (var l in f.Logs)
        {
            if (Observation(l) is not { } o) continue;
            var m = SelfExecMatcher.Classify(o, execs);
            if (m.Kind == SelfExecKind.Own) own.Add(l);
            else if (m.Kind == SelfExecKind.Claimed) f.ClaimedActivity.Add((l, m.Why));
        }
        if (own.Count == 0) return;
        var set = own.ToHashSet();
        f.Logs.RemoveAll(set.Contains);
        f.OwnActivity.AddRange(own);
    }

    private static void OwnActivityCheck(StationFacts f, ControlReport r)
    {
        // without the sealed audit nothing can be recognised: the check is not emitted and every event stays with the other checks (A11 names the missing log)
        if (f.CollectorExecs is not { } execs) return;
        const string title = "Activitate proprie a aplicației";
        var own = f.OwnActivity.Select(l => $"{Ev(l)}: {OwnText(l)}");
        var ownText = f.OwnActivity.Count == 0
            ? "Activitate proprie a aplicației: nicio comandă a aplicației în jurnalele citite."
            : $"Activitate proprie a aplicației: {Ro.CountFem(f.OwnActivity.Count, "comandă", "comenzi")}, toate consemnate în jurnalul sigilat al aplicației (programul, linia de comandă și amprenta lui); nu sunt constatări.";
        if (f.ClaimedActivity.Count == 0 && execs.Unreadable.Count == 0)
        {
            Add(r, "A17", "Audit", title, ControlStatus.Conform, ownText, own);
            return;
        }
        var claimed = f.ClaimedActivity.Select(c => $"{Ev(c.Log)}: {OwnText(c.Log)} — {SelfExecMatcher.ClaimedLabel}: {c.Why}");
        var detail = $"{Ro.Capitalize(Ro.CountMasc(f.ClaimedActivity.Count, "eveniment", "evenimente"))} {(f.ClaimedActivity.Count == 1 ? "este" : "sunt")} {SelfExecMatcher.ClaimedLabel}: " +
                     "se potrivesc cu o comandă din jurnalul aplicației care nu trece verificarea (nu este în interiorul unui control valid, lanțul nu se verifică, programul nu este din inventarul aplicației, fișierul lui nu mai are amprenta consemnată sau lipsesc datele programului); " +
                     "rămân la celelalte verificări ca orice eveniment. " +
                     (execs.Unreadable.Count > 0 ? $"{Ro.Capitalize(Ro.CountFem(execs.Unreadable.Count, "înregistrare", "înregistrări"))} „collector.exec” nu {(execs.Unreadable.Count == 1 ? "poate" : "pot")} fi citite. " : "") +
                     ownText;
        Add(r, "A17", "Audit", title, ControlStatus.DeVerificat, detail, claimed.Concat(own),
            "Comparați comenzile neconfirmate cu ce a rulat operatorul aplicației la acea oră; o comandă pe care nimeni nu o poate explica este tratată ca activitate străină.");
    }

    private static string OwnText(LogFact l) => l.Id == 4104 && l.Channel == PowerShellChannel
        ? $"bloc de script {Shorten(V(l, "ScriptBlockText"), 80)}"
        : $"{V(l, l.Channel == SysmonChannel ? "Image" : "NewProcessName")} {Shorten(V(l, "CommandLine"), 160)}".Trim();
}
