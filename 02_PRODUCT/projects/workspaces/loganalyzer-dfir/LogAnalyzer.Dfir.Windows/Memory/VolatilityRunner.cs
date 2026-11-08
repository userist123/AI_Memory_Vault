using System.Diagnostics;
using System.Text.Json;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;

namespace LogAnalyzer.Dfir.Windows.Memory;

/// <summary>One plugin run: what was asked, what came back, and the evidence it became (empty when it failed).</summary>
public sealed record VolatilityPluginRun(string Plugin, int ExitCode, bool TimedOut, string OutputEvidenceId, string Problem, string StderrPath, TimeSpan Duration);

public sealed class VolatilityRunResult
{
    /// <summary>Why nothing was started (the checks before the first plugin). Empty when the plugins ran.</summary>
    public List<string> Refused { get; } = [];
    public List<VolatilityPluginRun> Plugins { get; } = [];
    public string ImageEvidenceId { get; init; } = "";
    public bool Started => Refused.Count == 0;
}

/// <summary>
/// Master plan 6.6, controlled run of Volatility 3 on a memory image that is already evidence in the case. Trust model: the station administrator approves
/// one tool folder and one symbol folder by pinning their manifests in a profile folder only administrators can change; the analyst may only run listed
/// plugins on an image of the case. Threats and their checks:
/// <list type="number">
/// <item>A profile written by a non-administrator: the profile folder, the tool folder and the symbol folder (every file and subfolder) must be owned by
/// Administrators / SYSTEM / TrustedInstaller and writable by no one else (<see cref="AuthStoreProtection.CheckDirectory"/>).</item>
/// <item>A swapped executable or a changed file next to it (DLL, Python package, plugin): the whole tool folder must match its pinned manifest; the
/// executable's own hash is pinned too, and it stays open with writes denied until the last plugin ends.</item>
/// <item>A changed symbol table: the symbol folder must match its pinned manifest exactly.</item>
/// <item>Network and inherited configuration: --offline; PYTHON* and VOLATILITY* variables removed; TEMP, TMP, the working folder and the cache in a
/// private folder deleted afterwards.</item>
/// <item>Argument injection: plugins only from <see cref="AllowedPlugins"/>; the image path is the case's own copy; no shell, one argument per item.</item>
/// <item>The image altered: it is checked against the evidence index before, held open with writes denied during, and checked again after the runs.</item>
/// <item>Hangs and floods: a timeout per plugin; the tool is stopped as soon as an output passes <see cref="MaxOutputBytes"/>.</item>
/// <item>Output posing as results: an output must be a JSON array; it is then read by the same parser as a hand-imported one (rows are candidates).</item>
/// </list>
/// Every start is in the sealed audit (collector.exec through <see cref="ToolRunner"/>); every output is registered as evidence derived from the image,
/// with the tool, manifest hashes and arguments in its custody notes; a refusal, a failure and an interruption are in the case audit.
/// </summary>
public static class VolatilityRunner
{
    public const string Collector = "Volatility3";
    public const long MaxOutputBytes = 512L * 1024 * 1024;

    /// <summary>The plugins whose output the case can read (<see cref="VolatilityJsonParser"/>). Nothing else is ever passed to the tool.</summary>
    public static IReadOnlyList<string> AllowedPlugins { get; } = ["windows.pslist", "windows.psscan", "windows.pstree", "windows.netscan", "windows.cmdline", "windows.malfind"];

    /// <summary>File types that are taken for memory images when offering a choice (any evidence id can still be passed).</summary>
    public static bool LooksLikeMemoryImage(EvidenceItem e) =>
        Path.GetExtension(e.OriginalName).ToLowerInvariant() is ".raw" or ".mem" or ".vmem" or ".dmp" or ".lime" or ".img" or ".bin" && e.Size > 0;

    /// <summary>
    /// The checks made before anything is started. Empty = the profile, the tool folder and the symbols are the approved ones and only administrators can change them.
    /// <paramref name="aclCheck"/> is the folder rights check (tests replace it; production uses <see cref="AuthStoreProtection.CheckDirectory"/>).
    /// </summary>
    public static List<string> CheckTool(VolatilityToolProfile? profile, string? profileProblem, string? profileDir = null, Func<string, StoreAclResult>? aclCheck = null)
    {
        var refused = new List<string>();
        var dir = profileDir ?? VolatilityToolProfile.DefaultDir;
        var acl = aclCheck ?? AuthStoreProtection.CheckDirectory;
        if (profileProblem is not null) { refused.Add("Profilul uneltelor nu poate fi citit: " + profileProblem); return refused; }
        if (profile is null || profile.Executable.Length == 0)
        { refused.Add($"Volatility nu este configurat: administratorul îl aprobă cu „LogAnalyzer.KeyGen volatility-profile” (profilul ajunge în {dir})."); return refused; }
        void Rights(string folder, string what)
        {
            var r = acl(folder);
            if (!r.Protected) refused.Add($"{what} poate fi modificat(ă) și de alte conturi decât administratorii: {string.Join("; ", r.Problems.Take(4))}.");
        }
        Rights(dir, "Folderul profilului uneltelor");
        var exe = profile.Executable;
        if (!Path.IsPathFullyQualified(exe) || exe.StartsWith(@"\\", StringComparison.Ordinal)) { refused.Add("Calea executabilului trebuie să fie completă și locală (nu o cale de rețea)."); return refused; }
        if (!File.Exists(exe)) { refused.Add($"Executabilul Volatility nu există: {exe}."); return refused; }
        if (new FileInfo(exe).LinkTarget is not null) { refused.Add("Executabilul este o legătură; este refuzat."); return refused; }
        if (profile.Sha256.Trim().Length != 64) refused.Add("Profilul nu are amprenta SHA-256 a executabilului.");
        else if (!Hashing.Sha256File(exe).Equals(profile.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            refused.Add("Executabilul Volatility nu este cel aprobat: amprenta lui diferă de cea din profil.");
        var toolDir = Path.GetDirectoryName(exe)!;
        Rights(toolDir, "Folderul uneltei");
        refused.AddRange(FolderManifest.Verify(toolDir, Path.Combine(dir, VolatilityToolProfile.ToolManifestName), profile.ToolManifestSha256, "unealtă"));
        if (profile.SymbolsDir.Length > 0 && Directory.Exists(profile.SymbolsDir)) Rights(profile.SymbolsDir, "Folderul simbolurilor");
        refused.AddRange(FolderManifest.Verify(profile.SymbolsDir, Path.Combine(dir, VolatilityToolProfile.SymbolsManifestName), profile.SymbolsManifestSha256, "simboluri"));
        if (profile.TimeoutMinutes is < 1 or > 24 * 60) refused.Add("Limita de timp din profil trebuie să fie între 1 minut și 24 de ore.");
        return refused;
    }

    /// <summary>The fixed argument list for one plugin. Paths are full paths, so none can be read as an option.</summary>
    public static List<string> Arguments(string symbolsDir, string cachePath, string imagePath, string plugin) =>
        ["--offline", "-q", "-r", "json", "--cache-path", cachePath, "-s", symbolsDir, "-f", imagePath, plugin];

    /// <summary>Removes what a Python program would read from the environment instead of from its own files, and keeps its temporary files private.</summary>
    public static void Sanitize(ProcessStartInfo psi, string workDir, string tempDir)
    {
        foreach (var k in psi.Environment.Keys.Where(k => k.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase) || k.StartsWith("VOLATILITY", StringComparison.OrdinalIgnoreCase)).ToList())
            psi.Environment.Remove(k);
        psi.Environment["TEMP"] = tempDir;
        psi.Environment["TMP"] = tempDir;
        psi.WorkingDirectory = workDir;
    }

    public delegate ToolRun Launcher(string exe, IReadOnlyList<string> args, string logDir, string label, TimeSpan timeout, CancellationToken ct, Action<ProcessStartInfo> configure);

    private static ToolRun DefaultLauncher(string exe, IReadOnlyList<string> args, string logDir, string label, TimeSpan timeout, CancellationToken ct, Action<ProcessStartInfo> configure) =>
        ToolRunner.Run(exe, args, logDir, label, timeout, ct, configure: configure, maxOutputBytes: MaxOutputBytes);

    /// <param name="launcher">Tests only: replaces the process start; every check before and after it stays.</param>
    public static VolatilityRunResult Run(CaseWorkspace ws, string imageEvidenceId, IReadOnlyList<string> plugins, VolatilityToolProfile? profile, string? profileProblem,
                                          CancellationToken ct, Launcher? launcher = null, string? profileDir = null, Func<string, StoreAclResult>? aclCheck = null)
    {
        var result = new VolatilityRunResult { ImageEvidenceId = imageEvidenceId };
        if (ws.IsReadOnly) { result.Refused.Add("Cazul este doar pentru citire (" + ws.ReadOnlyReason + "): ieșirile nu pot fi adăugate."); return result; }
        var bad = plugins.Where(p => !AllowedPlugins.Contains(p, StringComparer.Ordinal)).ToList();
        if (bad.Count > 0) result.Refused.Add("Plugin nepermis: " + string.Join(", ", bad) + ". Permise: " + string.Join(", ", AllowedPlugins) + ".");
        if (plugins.Count == 0) result.Refused.Add("Niciun plugin ales.");
        var image = ws.LoadEvidence().FirstOrDefault(e => e.EvidenceId == imageEvidenceId);
        string imagePath = "";
        if (image is null) result.Refused.Add($"Proba {imageEvidenceId} nu este în caz.");
        else
            try { imagePath = ws.FullPath(image.StoredPath); }
            catch (ArgumentException) { result.Refused.Add($"Proba {image.EvidenceId} nu are calea în interiorul cazului; este refuzată."); }
        result.Refused.AddRange(CheckTool(profile, profileProblem, profileDir, aclCheck));
        if (result.Refused.Count > 0) { ws.Audit("volatility.refused", $"{imageEvidenceId}: {string.Join(" | ", result.Refused)}"); return result; }

        if (!File.Exists(imagePath) || !Hashing.Sha256File(imagePath, ct).Equals(image!.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Refused.Add($"Imaginea {image!.EvidenceId} nu mai are amprenta din indexul probelor; nu se rulează nimic pe ea.");
            ws.Audit("volatility.refused", $"{image.EvidenceId}: amprenta imaginii diferă");
            return result;
        }

        var exe = profile!.Executable;
        var exeSha = profile.Sha256.Trim().ToLowerInvariant();
        var work = Path.Combine(Path.GetTempPath(), "la_vol_" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(work, "cache"), empty = Path.Combine(work, "cwd"), temp = Path.Combine(work, "tmp");
        Directory.CreateDirectory(cache); Directory.CreateDirectory(empty); Directory.CreateDirectory(temp);
        var logDir = Path.Combine(ws.Root, "Logs", "tools");
        var outDir = ws.RawDir("Volatility");
        var pending = new List<(string Plugin, string Stdout, ToolRun Run, List<string> Args)>();
        bool finished = false;
        try
        {
            // the approved executable and the image stay open with writes denied while the tool runs: neither can be swapped after the checks
            using (var pinExe = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var pinImage = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!Hashing.Sha256File(exe).Equals(exeSha, StringComparison.OrdinalIgnoreCase))
                {
                    result.Refused.Add("Executabilul s-a schimbat între verificare și pornire.");
                    ws.Audit("volatility.refused", "executabilul s-a schimbat între verificare și pornire");
                    finished = true;
                    return result;
                }
                CollectorExec.AddApprovedTool(exe);
                ws.Audit("volatility.start", $"{image!.EvidenceId} sha256={image.Sha256}; executabil sha256={exeSha}; manifest unealtă sha256={profile.ToolManifestSha256.Trim().ToLowerInvariant()}; " +
                                             $"manifest simboluri sha256={profile.SymbolsManifestSha256.Trim().ToLowerInvariant()}; pluginuri {string.Join(",", plugins)}");
                foreach (var plugin in plugins)
                {
                    ct.ThrowIfCancellationRequested();
                    var args = Arguments(Path.GetFullPath(profile.SymbolsDir), cache, imagePath, plugin);
                    var run = (launcher ?? DefaultLauncher)(exe, args, logDir, "volatility_" + plugin.Replace('.', '_'), TimeSpan.FromMinutes(profile.TimeoutMinutes), ct, psi => Sanitize(psi, empty, temp));
                    pending.Add((plugin, run.StdoutPath, run, args));
                }
            }

            // the image must be unchanged after the runs, or none of the outputs is kept
            if (!Hashing.Sha256File(imagePath, ct).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (plugin, _, run, _) in pending)
                    result.Plugins.Add(new VolatilityPluginRun(plugin, run.ExitCode, run.TimedOut, "", "Imaginea și-a schimbat amprenta în timpul rulării; ieșirea nu a fost păstrată.", run.StderrPath, run.EndUtc - run.StartUtc));
                ws.Audit("volatility.image_changed", $"{image.EvidenceId}: amprenta s-a schimbat în timpul rulării; ieșirile nu au fost păstrate");
                finished = true;
                return result;
            }

            foreach (var (plugin, stdout, run, args) in pending)
            {
                var problem = OutputProblem(run, stdout);
                if (problem is not null)
                {
                    result.Plugins.Add(new VolatilityPluginRun(plugin, run.ExitCode, run.TimedOut, "", problem, run.StderrPath, run.EndUtc - run.StartUtc));
                    ws.Audit("volatility.plugin_failed", $"{image.EvidenceId} {plugin}: {problem}");
                    continue;
                }
                var dest = Path.Combine(outDir, $"{image.EvidenceId}_{plugin}_{run.StartUtc:yyyyMMdd_HHmmss}.json");
                File.Copy(stdout, dest, overwrite: false);
                var ev = ws.RegisterStored(dest, $"{exe} {plugin} pe {image.EvidenceId}", "Memory:Volatility", VolatilityJsonParser.SourceTypeName, TemporalType.Historical,
                    Collector, ToolRunner.FileVersion(exe), action: "derived", parentEvidenceId: image.EvidenceId,
                    notes: $"Derivat din {image.EvidenceId} (SHA-256 {image.Sha256}) de Volatility 3, executabil SHA-256 {exeSha}, " +
                           $"manifest unealtă SHA-256 {profile.ToolManifestSha256.Trim().ToLowerInvariant()}, manifest simboluri SHA-256 {profile.SymbolsManifestSha256.Trim().ToLowerInvariant()}, " +
                           $"argumente: {string.Join(' ', args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a))}; cod de ieșire {run.ExitCode}; {(run.EndUtc - run.StartUtc).TotalSeconds:0} s. " +
                           "Ieșirea este copiată octet cu octet. Rândurile sunt ce a scris unealta, nu o verificare independentă a memoriei.");
                result.Plugins.Add(new VolatilityPluginRun(plugin, run.ExitCode, false, ev.EvidenceId, "", run.StderrPath, run.EndUtc - run.StartUtc));
            }
            ws.Audit("volatility.done", $"{image.EvidenceId}: {result.Plugins.Count(p => p.OutputEvidenceId.Length > 0)} ieșiri păstrate din {result.Plugins.Count}");
            finished = true;
            return result;
        }
        finally
        {
            if (!finished) ws.Audit("volatility.aborted", $"{imageEvidenceId}: rularea a fost întreruptă după {pending.Count} pluginuri; nicio ieșire nu a fost păstrată");
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Why an output is not kept: timeout, too large, error exit, empty, or not a JSON array. Null = keep it.</summary>
    public static string? OutputProblem(ToolRun run, string stdout)
    {
        if (run.TimedOut) return "Pluginul a depășit limita de timp și a fost oprit.";
        if (run.OutputLimitHit) return $"Ieșirea a depășit {MaxOutputBytes / (1024 * 1024)} MB; unealta a fost oprită și ieșirea nu a fost păstrată.";
        if (run.ExitCode != 0) return $"Pluginul s-a încheiat cu codul {run.ExitCode} (vezi jurnalul de erori al uneltei).";
        var fi = new FileInfo(stdout);
        if (!fi.Exists || fi.Length == 0) return "Pluginul nu a scris nimic (simboluri lipsă sau plugin eșuat).";
        if (fi.Length > MaxOutputBytes) return $"Ieșirea depășește {MaxOutputBytes / (1024 * 1024)} MB și nu a fost păstrată.";
        try
        {
            using var s = File.OpenRead(stdout);
            using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { MaxDepth = 256 });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "Ieșirea nu este o listă JSON (formatul Volatility 3 cu -r json).";
        }
        catch (JsonException ex) { return "Ieșirea nu este JSON valid: " + ex.Message; }
        catch (IOException ex) { return "Ieșirea nu poate fi citită: " + ex.Message; }
        return null;
    }
}
