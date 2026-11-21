using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>
/// The Microsoft Defender detection history of the live station, read by <see cref="DefenderDetectionHistoryParser"/>:
/// <c>%ProgramData%\Microsoft\Windows Defender\Scans\History\Service\DetectionHistory\NN\GUID</c>. The files stay after the Defender event log is cleared or rotated,
/// so they show detections the log no longer has. Copied read only. The folder is readable by administrators only; a missing folder (Defender not installed or
/// never detected anything) or a refused one is reported, never fatal. Junctions and other reparse points are not followed, the file count and the file size are capped.
/// </summary>
public sealed class DefenderDetectionHistoryCollector : ICollector
{
    public string Name => DefenderDetectionHistoryParser.CollectedBy;
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.QuickAndUp;

    public const int MaxFiles = 2_000;

    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Microsoft", "Windows Defender", "Scans", "History", "Service", "DetectionHistory");

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var root = DefaultRoot;
        if (!Directory.Exists(root)) return CollectorOutcome.NotAvailable("sursă lipsă: folderul DetectionHistory al Defender nu există (Defender neinstalat sau nicio detecție înregistrată)", root);
        var o = new CollectorOutcome { Source = root, Tool = "copy" };
        CollectFrom(ctx.Case, root, o, ct);
        o.Status = o.Evidence.Count == 0 ? (o.Errors.Count > 0 ? EvidenceStatus.Failed : EvidenceStatus.Empty) : o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }

    /// <summary>Copies the detection files under <paramref name="root"/> (one level of NN folders); refusals go to <paramref name="o"/>.Errors. Internal for the tests.</summary>
    internal static void CollectFrom(LogAnalyzer.Dfir.Case.CaseWorkspace ws, string root, CollectorOutcome o, CancellationToken ct)
    {
        if (UserArtifactsCollector.ReparseOnPath(root) is { Length: > 0 } link) { o.Errors.Add($"legătura „{link}” pe drumul spre DetectionHistory nu este urmată"); return; }
        List<DirectoryInfo> folders;
        try
        {
            folders = new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint })
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            o.Errors.Add($"DetectionHistory nu a putut fi citit ({(ex is UnauthorizedAccessException ? "necesită administrator" : ex.Message)})");
            return;
        }
        int copied = 0;
        foreach (var dir in folders)
        {
            ct.ThrowIfCancellationRequested();
            List<FileInfo> files;
            try
            {
                files = dir.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint }).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                o.Errors.Add($"{dir.Name}: nu a putut fi citit ({(ex is UnauthorizedAccessException ? "necesită administrator" : ex.Message)})");
                continue;
            }
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                if (copied >= MaxFiles) { o.Errors.Add($"s-au copiat primele {MaxFiles} de fișiere; restul nu au fost copiate"); return; }
                if (!Guid.TryParse(f.Name, out _) || f.Length == 0) continue;   // the history files are named by a GUID
                if (f.Length > DefenderDetectionHistoryParser.MaxFileBytes) { o.Errors.Add($"{dir.Name}\\{f.Name}: depășește limita de citire, necopiat"); continue; }
                try
                {
                    o.Evidence.Add(ws.ImportFile(f.FullName, "DefenderDetectionHistory", DefenderDetectionHistoryParser.SourceTypeName, TemporalType.Historical, DefenderDetectionHistoryParser.CollectedBy, "1.0"));
                    copied++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { o.Errors.Add($"{dir.Name}\\{f.Name}: {ex.Message}"); }
            }
        }
    }
}
