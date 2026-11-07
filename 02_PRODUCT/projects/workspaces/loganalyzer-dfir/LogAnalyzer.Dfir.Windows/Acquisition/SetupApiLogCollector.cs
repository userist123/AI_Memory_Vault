using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// The device installation log of the live station (<c>%SystemRoot%\INF\setupapi.dev.log</c> and its rotated copies setupapi.dev.AAAAMMZZ_HHMMSS.log):
/// the first installation of every stick, phone or other device, read by <see cref="LogAnalyzer.Dfir.Devices.SetupApiLogParser"/> (research 2026-11-07,
/// plan P2.3). Copied read only, bounded in size.
/// </summary>
public sealed class SetupApiLogCollector : ICollector
{
    public string Name => "SetupApiLogCollector";
    public string Version => "1.0";
    public bool RequiresElevation => false;
    public CollectionProfile Profiles => CollectionProfile.QuickAndUp;

    /// <summary>
    /// The live file first, then the newest rotated copies (setupapi.dev.AAAAMMZZ_HHMMSS.log, so a descending name is a descending date), at most 20 in all.
    /// Review 2026-11-08: an ascending order put the live file after 20 rotated copies and dropped it silently.
    /// </summary>
    public static IReadOnlyList<string> Files(string infDir) => Directory.Exists(infDir) ? Order(Directory.EnumerateFiles(infDir, "setupapi.dev*.log")) : [];

    /// <summary>The same order on a list of paths. Pure, for the tests.</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> paths)
    {
        var all = paths.Where(f => Path.GetFileName(f).StartsWith("setupapi.dev", StringComparison.OrdinalIgnoreCase)).ToList();
        var live = all.Where(f => Path.GetFileName(f).Equals("setupapi.dev.log", StringComparison.OrdinalIgnoreCase));
        return live.Concat(all.Except(live).OrderDescending(StringComparer.OrdinalIgnoreCase)).Take(20).ToList();
    }

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var inf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
        var files = Files(inf);
        if (files.Count == 0) return CollectorOutcome.NotAvailable("sursă lipsă: setupapi.dev.log nu există în %SystemRoot%\\INF", inf);
        var o = new CollectorOutcome { Source = inf, Tool = "copy" };
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(f).Length > LogAnalyzer.Dfir.Devices.SetupApiLogParser.MaxLogBytes) { o.Errors.Add($"{Path.GetFileName(f)}: mai mare decât limita de citire, necopiat"); continue; }
                o.Evidence.Add(ctx.Case.ImportFile(f, "SetupAPI", LogAnalyzer.Dfir.Devices.SetupApiLogParser.SourceTypeName, TemporalType.Historical, Name, Version));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { o.Errors.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        o.Status = o.Evidence.Count == 0 ? EvidenceStatus.Failed : o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }
}
