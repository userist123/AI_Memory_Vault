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

    /// <summary>The live file and its rotated copies under <paramref name="infDir"/>, newest name order. Pure apart from the listing.</summary>
    public static IReadOnlyList<string> Files(string infDir) =>
        Directory.Exists(infDir)
            ? Directory.EnumerateFiles(infDir, "setupapi.dev*.log").Where(f => Path.GetFileName(f).StartsWith("setupapi.dev", StringComparison.OrdinalIgnoreCase))
                       .Order(StringComparer.OrdinalIgnoreCase).Take(20).ToList()
            : [];

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
