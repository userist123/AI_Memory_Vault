using System.Text.RegularExpressions;
using Xunit;

namespace LogAnalyzer.Edition.Tests;

/// <summary>
/// STAGE2_PLAN WP3 read-only rule: the collector never clears or deletes the audited system's logs. Scans the metadata of every
/// LogAnalyzer assembly in both edition outputs (members, P/Invokes, string literals) for calls that clear source logs.
/// A positive control proves the scanner flags such patterns, so a scan that finds nothing cannot pass silently.
/// </summary>
public sealed class ReadOnlyCollectionTests
{
    private static readonly string[] BannedMembers =
    [
        "System.Diagnostics.EventLog::Clear", "System.Diagnostics.EventLog::Delete", "System.Diagnostics.EventLog::DeleteEventSource",
        "System.Diagnostics.Eventing.Reader.EventLogSession::ClearLog",
    ];

    private static readonly string[] BannedPInvokeFunctions = ["EvtClearLog", "ClearEventLog", "ClearEventLogW", "ClearEventLogA"];

    // wevtutil cl / clear-log, Clear-EventLog, Remove-EventLog, EvtClearLog as literals (command lines are often one string).
    internal static readonly Regex BannedLiteral = new(
        @"(\bwevtutil(\.exe)?\s+(/\S+\s+)*(cl|clear-log)\b)|(\bClear-EventLog\b)|(\bRemove-EventLog\b)|(\bEvtClearLog\b)|(\bEventLog\.Clear\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Attacker-technique INDICATORS that the detection engines look for in monitored events (RansomwareDetectionEngine command-line
    // patterns; the explanation text of LiveSecurityMonitoringEngine). They are matched against event data, never executed.
    // Exact strings, in LogAnalyzer.Core.dll only: anything else, or the same text in another assembly, is a violation.
    // The last entry is a fragment of LiveSecurityMonitoringEngine's interpolated explanation ("... (EID {id} / wevtutil cl) pe [{machine}] ...").
    private static readonly string[] DetectorIndicatorLiterals =
    [
        "wevtutil cl security", "wevtutil cl system", " / wevtutil cl) pe [",
    ];

    private static bool IsDetectorIndicator(AssemblyFacts f, string literal) =>
        f.File.Equals("LogAnalyzer.Core.dll", StringComparison.OrdinalIgnoreCase) && DetectorIndicatorLiterals.Contains(literal);

    internal static List<string> Violations(AssemblyFacts f)
    {
        var v = new List<string>();
        foreach (var m in f.MemberRefs.Where(m => BannedMembers.Contains(m))) v.Add($"{f.File}: member {m}");
        foreach (var p in f.PInvokes.Where(p => BannedPInvokeFunctions.Contains(p.Function, StringComparer.OrdinalIgnoreCase))) v.Add($"{f.File}: P/Invoke {p.Dll}!{p.Function}");
        foreach (var s in f.UserStrings.Where(s => BannedLiteral.IsMatch(s) && !IsDetectorIndicator(f, s))) v.Add($"{f.File}: literal \"{s}\"");
        return v;
    }

    private static IEnumerable<AssemblyFacts> Facts(string dir)
    {
        foreach (var path in BuildOutputInspector.OwnAssemblies(dir).Concat(Directory.EnumerateFiles(dir, "LogAnalyzer*.exe")))
        {
            AssemblyFacts f;
            try { f = BuildOutputInspector.Read(path); } catch (BadImageFormatException) { continue; }
            yield return f;
        }
    }

    [Theory]
    [InlineData("ClassifiedOutput")]
    [InlineData("UnclassifiedOutput")]
    public void No_production_assembly_clears_or_deletes_source_logs(string outputKey)
    {
        var dir = BuildOutputInspector.Output(outputKey);
        var all = Facts(dir).ToList();
        Assert.NotEmpty(all); // an empty scan is a failed scan
        var found = all.SelectMany(Violations).ToList();
        Assert.True(found.Count == 0, "Source-log clearing found:\n" + string.Join("\n", found));
    }

    [Fact]
    public void Detector_indicator_allowlist_applies_only_to_core_assembly()
    {
        AssemblyFacts In(string file) => new(file, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), [], new HashSet<string> { "wevtutil cl security" });
        Assert.Empty(Violations(In("LogAnalyzer.Core.dll")));
        Assert.NotEmpty(Violations(In("LogAnalyzer.Dfir.Windows.dll")));
    }

    [Theory]
    [InlineData("wevtutil cl Security")]
    [InlineData("wevtutil.exe /q:true cl System")]
    [InlineData("wevtutil clear-log Application")]
    [InlineData("Clear-EventLog -LogName Security")]
    [InlineData("EvtClearLog")]
    public void Scanner_flags_clearing_patterns_positive_control(string literal)
    {
        var f = new AssemblyFacts("synthetic.dll", new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), [], new HashSet<string> { literal });
        Assert.NotEmpty(Violations(f));
    }

    [Fact]
    public void Scanner_flags_banned_members_and_pinvokes_positive_control_and_allows_export()
    {
        var bad = new AssemblyFacts("synthetic.dll", new HashSet<string>(), new HashSet<string>(),
            new HashSet<string> { "System.Diagnostics.EventLog::Clear" }, new HashSet<string>(), [("advapi32.dll", "ClearEventLogW")], new HashSet<string>());
        Assert.Equal(2, Violations(bad).Count);
        var ok = new AssemblyFacts("synthetic.dll", new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), [],
            new HashSet<string> { "wevtutil.exe", "epl", "wevtutil epl Security out.evtx" });
        Assert.Empty(Violations(ok));
    }
}
