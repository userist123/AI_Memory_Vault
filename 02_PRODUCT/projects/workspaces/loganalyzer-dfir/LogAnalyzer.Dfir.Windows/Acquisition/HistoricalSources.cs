using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>One historical source found under a folder: an archived event log, a log or hive of Windows.old, or a file of a shadow copy.</summary>
public sealed record HistoricalSource(string Path, string Kind, string Origin);

/// <summary>
/// E1: the sources of an older connection history that a plain export of the live channels misses: archived event logs (Archive-&lt;canal&gt;-&lt;dată&gt;.evtx,
/// written when a log is set to "archive when full"), and the event logs, configuration hives and user hives of a previous installation kept in
/// Windows.old. <see cref="Discover"/> finds them under any folder the operator brings (an image mount, a copied disk, a shadow copy root);
/// <see cref="ArchivedLogsCollector"/> collects them from the live station. Reading only; the originals are never changed.
/// </summary>
public static class HistoricalSources
{
    private static readonly Regex ArchiveRx = new(@"^Archive-(?<ch>.+?)-\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2}(?:-\d{1,3})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>"Archive-Security-2026-10-01-08-00-00-123.evtx" → "Security"; "Microsoft-Windows-Partition%4Diagnostic.evtx" → "Microsoft-Windows-Partition/Diagnostic".</summary>
    public static string ChannelFromFileName(string fileName)
    {
        var n = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var m = ArchiveRx.Match(n);
        if (m.Success) n = m.Groups["ch"].Value;
        return n.Replace("%4", "/").Replace('%', '/');
    }

    public static bool IsArchivedLog(string path) => ArchiveRx.IsMatch(System.IO.Path.GetFileNameWithoutExtension(path)) && path.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] ConfigHives = ["SYSTEM", "SOFTWARE"];

    /// <summary>Historical sources under <paramref name="root"/> (recursive, bounded): archived logs anywhere, and Windows.old logs, hives and setupapi.dev.log.</summary>
    public static List<HistoricalSource> Discover(string root, int maxFiles = 20_000)
    {
        var found = new List<HistoricalSource>();
        if (!Directory.Exists(root)) return found;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        int seen = 0;
        foreach (var f in Directory.EnumerateFiles(root, "*", opts))
        {
            if (++seen > maxFiles) break;
            var origin = EvidenceOrigin.Label(f);
            var name = System.IO.Path.GetFileName(f);
            bool winOld = f.Contains(@"\Windows.old\", StringComparison.OrdinalIgnoreCase);
            if (IsArchivedLog(f)) found.Add(new(f, "evtx", origin));
            else if (winOld && f.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase) && f.Contains(@"\winevt\Logs\", StringComparison.OrdinalIgnoreCase)) found.Add(new(f, "evtx", origin));
            else if (winOld && f.Contains(@"\System32\config\", StringComparison.OrdinalIgnoreCase) && ConfigHives.Contains(name, StringComparer.OrdinalIgnoreCase)) found.Add(new(f, name.ToUpperInvariant() == "SYSTEM" ? "system_hive" : "software_hive", origin));
            else if (winOld && name.Equals("NTUSER.DAT", StringComparison.OrdinalIgnoreCase)) found.Add(new(f, "ntuser_hive", origin));
            else if (winOld && name.Equals("setupapi.dev.log", StringComparison.OrdinalIgnoreCase)) found.Add(new(f, "file", origin));
        }
        return found;
    }

    /// <summary>The historical sources of the live station: archived logs in %SystemRoot%\System32\winevt\Logs and the Windows.old folder of the system drive.</summary>
    public static List<HistoricalSource> OnThisStation()
    {
        var list = new List<HistoricalSource>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var logs = System.IO.Path.Combine(windows, "System32", "winevt", "Logs");
        try
        {
            if (Directory.Exists(logs))
                list.AddRange(Directory.EnumerateFiles(logs, "Archive-*.evtx").Select(f => new HistoricalSource(f, "evtx", "jurnal arhivat")));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        var old = System.IO.Path.Combine(System.IO.Path.GetPathRoot(windows) ?? @"C:\", "Windows.old");
        if (Directory.Exists(old))
        {
            foreach (var sub in new[] { @"Windows\System32\winevt\Logs", @"Windows\System32\config", @"Windows\INF", "Users" })
            {
                var d = System.IO.Path.Combine(old, sub);
                if (Directory.Exists(d)) list.AddRange(Discover(d).Where(s => !list.Any(x => x.Path.Equals(s.Path, StringComparison.OrdinalIgnoreCase))));
            }
        }
        return list;
    }
}

/// <summary>E1: collects archived event logs and the Windows.old logs / hives of the live station into the case, each with its origin in the evidence notes.</summary>
public sealed class ArchivedLogsCollector : ICollector
{
    public string Name => "ArchivedLogsCollector";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.QuickAndUp;

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var sources = HistoricalSources.OnThisStation();
        if (sources.Count == 0)
            return new CollectorOutcome
            {
                Status = EvidenceStatus.Empty, Source = "winevt\\Logs (Archive-*.evtx), Windows.old",
                Errors = { ctx.IsElevated ? "Nu există jurnale arhivate și nici Windows.old pe această stație." : "Jurnalele arhivate nu au putut fi căutate: necesită drepturi de administrator (nedeterminat)." },
            };
        var o = new CollectorOutcome { Source = "jurnale arhivate și Windows.old", Tool = "copy" };
        foreach (var s in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var source = s.Kind == "evtx" ? HistoricalSources.ChannelFromFileName(s.Path) : s.Kind;
                o.Evidence.Add(ctx.Case.ImportFile(s.Path, "Historical:" + source, s.Kind == "evtx" ? "EventLog:" + source : s.Kind, TemporalType.Historical, Name, Version,
                    notes: $"Sursă istorică ({s.Origin}): {s.Path}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { o.Errors.Add($"{System.IO.Path.GetFileName(s.Path)}: {ex.Message}"); }
        }
        o.Status = o.Evidence.Count == 0 ? EvidenceStatus.Failed : o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }
}
