using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>An authorized remote collection: who asked, why, for which station and which artifacts. Its SHA-256 binds the package.</summary>
public sealed record RemoteCollectionRequest(
    string RequestId, string CaseId, string TargetHost, string Operator, string Justification, IReadOnlyList<string> Artifacts, DateTimeOffset CreatedUtc);

public sealed record RemoteStep(
    [property: JsonPropertyName("artifact")] string Artifact, [property: JsonPropertyName("startUtc")] string StartUtc,
    [property: JsonPropertyName("endUtc")] string EndUtc, [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("error")] string Error);

public sealed record RemoteFile(
    [property: JsonPropertyName("path")] string Path, [property: JsonPropertyName("size")] long Size, [property: JsonPropertyName("sha256")] string Sha256);

/// <summary>manifest.json written by the package on the target, after hashing every file it collected.</summary>
public sealed record RemoteManifest(
    [property: JsonPropertyName("format")] string Format, [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("requestSha256")] string RequestSha256, [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("user")] string User, [property: JsonPropertyName("elevated")] bool Elevated,
    [property: JsonPropertyName("collectedUtc")] string CollectedUtc, [property: JsonPropertyName("steps")] IReadOnlyList<RemoteStep> Steps,
    [property: JsonPropertyName("files")] IReadOnlyList<RemoteFile> Files);

public sealed record RemotePackageCheck(bool Ok, IReadOnlyList<string> Problems, RemoteManifest? Manifest, RemoteCollectionRequest? Request, string PackageDir);

/// <summary>
/// Remote DFIR without the application touching the network (master spec §16): AUTHORIZE (request stored in the case with its
/// SHA-256) → PREFLIGHT (target name, artifact list) → COLLECT + HASH (a PowerShell package that runs on the target, locally,
/// refuses another host, hashes every file and the manifest) → TRANSFER (by the operator: removable media or their own
/// remoting) → VERIFY (manifest hash, request binding, host, every file's size and SHA-256, no missing or extra file) →
/// IMPORT (into the case with custody from the target path). Works the same in AirGapped mode. Password stores (SAM,
/// SECURITY hives), browser cookies and saved logins are never on the artifact list.
/// </summary>
public static class RemoteCollection
{
    public const string Format = "loganalyzer-remote-collection/1";
    public const string CollectorName = "LogAnalyzer remote package";

    public static readonly IReadOnlyList<string> KnownArtifacts =
    [
        "evtx:Security", "evtx:System", "evtx:Application", "evtx:Microsoft-Windows-PowerShell/Operational",
        "evtx:Microsoft-Windows-Windows Defender/Operational", "evtx:Microsoft-Windows-TaskScheduler/Operational",
        "evtx:Microsoft-Windows-Sysmon/Operational", "evtx:Microsoft-Windows-TerminalServices-LocalSessionManager/Operational",
        "hive:SYSTEM", "hive:SOFTWARE", "prefetch", "tasks", "defender-history", "notepad-tabs", "search-index",
    ];

    /// <summary>Package sub-folders for the Notepad tab files (one folder per profile) and for the Windows Search index (Windows.db with its -wal).</summary>
    public const string NotepadTabsFolder = "NotepadTabState", SearchIndexFolder = "WindowsSearch";

    /// <summary>Package sub-folder for the Defender DetectionHistory files (ProgramData\Microsoft\Windows Defender\Scans\History\Service\DetectionHistory).</summary>
    public const string DefenderHistoryFolder = "DefenderDetectionHistory";

    private static readonly Regex HostName = new("^[A-Za-z0-9][A-Za-z0-9-]{0,14}$");
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string RequestsDir(CaseWorkspace ws) => Path.Combine(ws.Root, "Remote", "requests");

    /// <summary>Records the authorization in the case. The target is a computer name (what the package compares with COMPUTERNAME).</summary>
    public static (RemoteCollectionRequest Request, string Sha256) Authorize(CaseWorkspace ws, string targetHost, string operatorName, string justification, IEnumerable<string> artifacts)
    {
        var host = targetHost.Trim();
        if (!HostName.IsMatch(host)) throw new ArgumentException($"„{targetHost}” nu este un nume de calculator (NetBIOS, cel mult 15 caractere: litere, cifre, cratimă).");
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("Lipsește operatorul.");
        if (string.IsNullOrWhiteSpace(justification)) throw new ArgumentException("Colectarea la distanță cere o justificare.");
        var list = artifacts.Distinct().ToList();
        if (list.Count == 0) throw new ArgumentException("Nu a fost ales niciun artefact.");
        foreach (var a in list.Where(a => !KnownArtifacts.Contains(a))) throw new ArgumentException($"Artefact necunoscut: {a}.");

        var r = new RemoteCollectionRequest(Guid.NewGuid().ToString(), ws.Info.CaseId, host.ToUpperInvariant(), operatorName.Trim(), justification.Trim(), list, DateTimeOffset.UtcNow);
        var dir = RequestsDir(ws);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, r.RequestId + ".json");
        var text = JsonSerializer.Serialize(r, Indented);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        var item = ws.RegisterStored(path, "authorization", "remote-request", "remote collection request", TemporalType.CurrentSnapshot, CollectorName, DfirInfo.ApplicationVersion,
            action: "authorized", notes: $"{r.TargetHost}: {string.Join(", ", r.Artifacts)}; justificare: {r.Justification}");
        ws.Audit("remote.authorized", $"{r.RequestId} host={r.TargetHost} operator={r.Operator} sha256={item.Sha256}");
        return (r, item.Sha256);
    }

    /// <summary>
    /// Text placed in a '#' comment of the generated script must stay on one line, or the rest of it would be executed.
    /// PowerShell ends a comment at CR, LF, NEL (U+0085), LS (U+2028) and PS (U+2029); all become spaces.
    /// </summary>
    public static string OneLine(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029') chars[i] = ' ';
        return new string(chars);
    }

    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>The package script. It only reads the target and writes next to itself; it contains no network command.</summary>
    public static string PackageScript(RemoteCollectionRequest r, string requestSha256)
    {
        if (!HostName.IsMatch(r.TargetHost) || !Guid.TryParse(r.RequestId, out _) || !Regex.IsMatch(requestSha256, "^[0-9A-Fa-f]{64}$")
            || r.Artifacts.Any(a => !KnownArtifacts.Contains(a)))
            throw new ArgumentException("Cererea nu este validă pentru generarea pachetului.");
        var sb = new StringBuilder();
        sb.AppendLine("# LogAnalyzer - pachet de colectare DFIR. Ruleaza pe statia tinta, local; nu foloseste reteaua.");
        sb.AppendLine($"# Cerere {r.RequestId} | caz {OneLine(r.CaseId)} | tinta {r.TargetHost} | operator {OneLine(r.Operator)}");
        sb.AppendLine("#Requires -Version 5.1");
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$RequestId = {Q(r.RequestId)}");
        sb.AppendLine($"$RequestSha256 = {Q(requestSha256.ToUpperInvariant())}");
        sb.AppendLine($"$ExpectedHost = {Q(r.TargetHost)}");
        sb.AppendLine($"$Artifacts = @({string.Join(", ", r.Artifacts.Select(Q))})");
        sb.Append("""
            if ($env:COMPUTERNAME -ne $ExpectedHost) {
                Write-Error "Pachetul este autorizat pentru $ExpectedHost, nu pentru $env:COMPUTERNAME. Nu se colecteaza nimic."
                exit 2
            }
            $out = Join-Path $PSScriptRoot ('LA_' + $env:COMPUTERNAME + '_' + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
            $files = Join-Path $out 'files'
            New-Item -ItemType Directory -Path $files -Force | Out-Null
            $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            # SHA-256 through .NET, not the FileHash cmdlet: that cmdlet lives in a module that fails to load when
            # Windows PowerShell inherits a PowerShell 7 PSModulePath (a launcher, a CI runner, an admin console),
            # and a package that cannot hash must stop, not write a manifest without hashes.
            function Get-Sha256Hex([string]$path) {
                $stream = [IO.File]::OpenRead($path)
                try {
                    $sha = [Security.Cryptography.SHA256]::Create()
                    try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '') }
                    finally { $sha.Dispose() }
                } finally { $stream.Dispose() }
            }
            $steps = New-Object System.Collections.Generic.List[object]
            function Step([string]$name, [scriptblock]$action) {
                $start = (Get-Date).ToUniversalTime().ToString('o')
                $err = ''
                $global:LASTEXITCODE = 0
                $absent = $false
                $global:StepNote = ''
                # wevtutil 15007: the channel does not exist on this station (Sysmon not installed): absent, not a failure (lab SRV01 2026-11-07)
                # an action that returns '__absent__' found nothing to collect (a folder that does not exist on this station)
                try { $res = @(& $action); if ($res -contains '__absent__') { $absent = $true; $err = 'folderul nu exista pe statie' } elseif ($LASTEXITCODE -eq 15007) { $absent = $true; $err = 'jurnalul nu exista pe statie (componenta nu este instalata)' } elseif ($LASTEXITCODE -ne 0) { $err = "cod de iesire $LASTEXITCODE" } } catch { $err = $_.Exception.Message }
                $steps.Add([ordered]@{ artifact = $name; startUtc = $start; endUtc = (Get-Date).ToUniversalTime().ToString('o'); status = $(if ($absent) { 'absent' } elseif ($err) { 'failed' } else { 'ok' }); error = $(if ($err) { $err } else { $global:StepNote }) })
            }
            foreach ($a in $Artifacts) {
                if ($a.StartsWith('evtx:')) {
                    $ch = $a.Substring(5)
                    $f = Join-Path $files (($ch -replace '[\\/: ]', '_') + '.evtx')
                    Step $a { & wevtutil.exe epl $ch $f 2>&1 | Out-Null }
                } elseif ($a.StartsWith('hive:')) {
                    $h = $a.Substring(5)
                    $f = Join-Path $files ($h + '.hive')
                    Step $a { & reg.exe save ('HKLM\' + $h) $f /y 2>&1 | Out-Null }
                } elseif ($a -eq 'prefetch') {
                    $d = Join-Path $files 'Prefetch'
                    New-Item -ItemType Directory -Path $d -Force | Out-Null
                    Step $a { Copy-Item -Path (Join-Path $env:SystemRoot 'Prefetch\*.pf') -Destination $d -ErrorAction Stop }
                } elseif ($a -eq 'tasks') {
                    Step $a { Copy-Item -Path (Join-Path $env:SystemRoot 'System32\Tasks') -Destination (Join-Path $files 'Tasks') -Recurse -ErrorAction Stop }
                } elseif ($a -eq 'defender-history') {
                    # one file per detection, kept after the Defender log is cleared; readable by administrators only
                    $src = Join-Path $env:ProgramData 'Microsoft\Windows Defender\Scans\History\Service\DetectionHistory'
                    Step $a { if (-not (Test-Path -LiteralPath $src)) { return '__absent__' }; Copy-Item -LiteralPath $src -Destination (Join-Path $files 'DefenderDetectionHistory') -Recurse -ErrorAction Stop }
                } elseif ($a -eq 'notepad-tabs') {
                    # the tab files of the Windows 11 Notepad, per profile: at most 500 files of at most 4 MiB each, links not followed
                    Step $a {
                        $copied = 0; $failed = 0; $skipped = 0; $links = 0
                        foreach ($p in @(Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue)) {
                            if ($p.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                            # every folder on the way is checked: a user can turn his own LocalState into a junction to another user's,
                            # and that user's tabs would be collected under the wrong name
                            $src = $p.FullName; $ok = $true
                            foreach ($part in 'AppData', 'Local', 'Packages', 'Microsoft.WindowsNotepad_8wekyb3d8bbwe', 'LocalState', 'TabState') {
                                $src = Join-Path $src $part
                                if (-not (Test-Path -LiteralPath $src)) { $ok = $false; break }
                                if ((Get-Item -LiteralPath $src -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $ok = $false; $links++; break }
                            }
                            if (-not $ok) { continue }
                            $d = Join-Path (Join-Path $files 'NotepadTabState') $p.Name
                            New-Item -ItemType Directory -Path $d -Force | Out-Null
                            $taken = 0
                            foreach ($f in @(Get-ChildItem -LiteralPath $src -Filter '*.bin' -File -Force -ErrorAction SilentlyContinue)) {
                                if ($f.Extension -ne '.bin') { continue }
                                if ($f.Attributes -band [IO.FileAttributes]::ReparsePoint) { $links++; continue }
                                if ($f.Length -gt 4MB -or $taken -ge 500) { $skipped++; continue }
                                try { Copy-Item -LiteralPath $f.FullName -Destination $d -ErrorAction Stop; $copied++; $taken++ } catch { $failed++ }
                            }
                        }
                        if ($failed -gt 0 -or $skipped -gt 0 -or $links -gt 0) { throw "file copiate: $copied; necopiate (Notepad deschis?): $failed; sarite (peste 4 MiB sau peste 500 pe profil): $skipped; legaturi neurmate: $links" }
                        if ($copied -eq 0) { return '__absent__' }
                    }
                } elseif ($a -eq 'search-index') {
                    # Windows.db and its -wal from ONE shadow copy, deleted afterwards: a journal from another moment would rebuild a state
                    # that never existed. The search service is not stopped. No shadow copy -> the step fails, nothing is copied.
                    # Windows-gather.db and its -wal come from the SAME shadow copy (both or neither, independent of the Windows.db pair); a missing or
                    # failed gather copy does not fail the step, it is reported as text in the step's error field ($global:StepNote).
                    Step $a {
                        $dir = Join-Path $env:ProgramData 'Microsoft\Search\Data\Applications\Windows'
                        if (-not (Test-Path -LiteralPath (Join-Path $dir 'Windows.db'))) { return '__absent__' }
                        $made = Invoke-CimMethod -ClassName Win32_ShadowCopy -MethodName Create -Arguments @{ Volume = $dir.Substring(0, 3); Context = 'ClientAccessible' } -ErrorAction Stop
                        if ($made.ReturnValue -ne 0) { throw "copia de umbra nu a putut fi creata (cod $($made.ReturnValue))" }
                        $sc = $null
                        $d = Join-Path $files 'WindowsSearch'
                        try {
                            $sc = Get-CimInstance -ClassName Win32_ShadowCopy | Where-Object { $_.ID -eq $made.ShadowID }
                            if (-not $sc) { throw "copia de umbra $($made.ShadowID) nu a fost gasita" }
                            $root = $sc.DeviceObject + $dir.Substring(2) + [IO.Path]::DirectorySeparatorChar
                            # sizes are read in the shadow copy before anything is written: over 2 GiB, or more than the free space, nothing is copied
                            $need = [long]0
                            foreach ($n in 'Windows.db', 'Windows.db-wal') {
                                $fi = New-Object IO.FileInfo ($root + $n)
                                if ($fi.Exists) { if ($fi.Length -gt 2GB) { throw "$n depaseste 2 GiB; indexul nu a fost copiat" }; $need += $fi.Length }
                            }
                            $free = (New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($files))).AvailableFreeSpace
                            if ($need + 256MB -gt $free) { throw "spatiu liber insuficient pentru index ($need octeti); nu a fost copiat" }
                            # Windows-gather.db and its -wal: size, link and free space (with the Windows.db pair) are checked here too; a gather refused
                            # for any of these is left out and reported, it does not fail the step
                            $gNote = 'gather: absent'; $gTake = $false; $gNeed = [long]0
                            if ((New-Object IO.FileInfo ($root + 'Windows-gather.db')).Exists) {
                                $gNote = ''; $gTake = $true
                                foreach ($n in 'Windows-gather.db', 'Windows-gather.db-wal') {
                                    $fi = New-Object IO.FileInfo ($root + $n)
                                    if (-not $fi.Exists) { continue }
                                    if ($fi.Attributes -band [IO.FileAttributes]::ReparsePoint) { $gTake = $false; $gNote = "gather: $n este o legatura, nu este urmata; necopiat" }
                                    elseif ($fi.Length -gt 2GB) { $gTake = $false; $gNote = "gather: $n depaseste 2 GiB; necopiat" }
                                    else { $gNeed += $fi.Length }
                                }
                                if ($gTake -and $need + $gNeed + 256MB -gt $free) { $gTake = $false; $gNote = "gather: spatiu liber insuficient ($gNeed octeti); necopiat" }
                            }
                            New-Item -ItemType Directory -Path $d -Force | Out-Null
                            try {
                                [IO.File]::Copy($root + 'Windows.db', (Join-Path $d 'Windows.db'), $false)
                                try { [IO.File]::Copy($root + 'Windows.db-wal', (Join-Path $d 'Windows.db-wal'), $false) } catch [IO.FileNotFoundException] { }
                            } catch {
                                # both files or neither: a database left without the journal of its own snapshot would be read as a complete pair
                                Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
                                throw
                            }
                            # Windows-gather.db: both files or neither, never a database left without the journal of its own snapshot; no link is followed
                            # (the files are read in the shadow copy by full path). The gather copy never fails the step.
                            $global:StepNote = $gNote
                            if ($gTake) {
                                $gdb = Join-Path $d 'Windows-gather.db'
                                try {
                                    [IO.File]::Copy($root + 'Windows-gather.db', $gdb, $false)
                                    try { [IO.File]::Copy($root + 'Windows-gather.db-wal', ($gdb + '-wal'), $false) } catch [IO.FileNotFoundException] { }
                                    $global:StepNote = ''
                                } catch {
                                    Remove-Item -LiteralPath $gdb, ($gdb + '-wal') -Force -ErrorAction SilentlyContinue
                                    $global:StepNote = "gather: necopiat ($($_.Exception.Message))"
                                }
                            }
                        } finally {
                            if ($sc) { $sc | Remove-CimInstance -ErrorAction Stop }
                            else {
                                & vssadmin.exe delete shadows "/shadow=$($made.ShadowID)" /quiet 2>&1 | Out-Null
                                $code = $LASTEXITCODE; $global:LASTEXITCODE = 0
                                if ($code -ne 0) { throw "copia de umbra $($made.ShadowID) nu a putut fi stearsa (vssadmin cod $code); stergeti-o manual" }
                            }
                        }
                    }
                }
            }
            try {
                $list = @(Get-ChildItem -LiteralPath $files -Recurse -File -ErrorAction Stop | ForEach-Object {
                    [ordered]@{ path = $_.FullName.Substring($out.Length + 1).Replace('\', '/'); size = $_.Length; sha256 = (Get-Sha256Hex $_.FullName) }
                })
            } catch {
                Write-Error "Hashing failed: $($_.Exception.Message). The package is incomplete and must not be used."
                exit 3
            }
            $manifest = [ordered]@{
                format = 'loganalyzer-remote-collection/1'; requestId = $RequestId; requestSha256 = $RequestSha256
                host = $env:COMPUTERNAME; user = [Security.Principal.WindowsIdentity]::GetCurrent().Name; elevated = $isAdmin
                collectedUtc = (Get-Date).ToUniversalTime().ToString('o'); steps = $steps.ToArray(); files = $list
            }
            $mp = Join-Path $out 'manifest.json'
            [IO.File]::WriteAllText($mp, ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
            [IO.File]::WriteAllText((Join-Path $out 'manifest.sha256'), (Get-Sha256Hex $mp) + "`n")
            Write-Output $out

            """);
        return sb.ToString();
    }

    /// <summary>Checks a transferred package against the case's authorization, without changing it.</summary>
    public static RemotePackageCheck Verify(CaseWorkspace ws, string packageDir)
    {
        var problems = new List<string>();
        var mp = Path.Combine(packageDir, "manifest.json");
        if (!File.Exists(mp)) return new(false, ["manifest.json lipsește"], null, null, packageDir);
        var manifestSha = Hashing.Sha256File(mp);
        var sp = Path.Combine(packageDir, "manifest.sha256");
        if (!File.Exists(sp)) problems.Add("manifest.sha256 lipsește");
        else if (!File.ReadAllText(sp).Trim().Equals(manifestSha, StringComparison.OrdinalIgnoreCase)) problems.Add("manifest.json a fost modificat după colectare (SHA-256 diferit)");

        RemoteManifest? m;
        try { m = JsonSerializer.Deserialize<RemoteManifest>(File.ReadAllText(mp)); }
        catch (JsonException ex) { return new(false, [.. problems, "manifest.json nu poate fi citit: " + ex.Message], null, null, packageDir); }
        if (m is null || m.Format != Format) return new(false, [.. problems, "format de manifest necunoscut"], m, null, packageDir);

        RemoteCollectionRequest? r = null;
        var rp = Guid.TryParse(m.RequestId, out var gid) ? Path.Combine(RequestsDir(ws), gid + ".json") : "";
        if (rp.Length == 0 || !File.Exists(rp)) problems.Add($"cererea {m.RequestId} nu există în acest caz: colectarea nu a fost autorizată aici");
        else
        {
            var stored = ws.LoadEvidence().LastOrDefault(e => ws.FullPath(e.StoredPath).Equals(Path.GetFullPath(rp), StringComparison.OrdinalIgnoreCase));
            var sha = Hashing.Sha256File(rp);
            if (stored is null || !stored.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase)) problems.Add("cererea stocată în caz a fost modificată după autorizare");
            if (!sha.Equals(m.RequestSha256, StringComparison.OrdinalIgnoreCase)) problems.Add("pachetul a fost generat pentru altă versiune a cererii (SHA-256 diferit)");
            r = JsonSerializer.Deserialize<RemoteCollectionRequest>(File.ReadAllText(rp));
            if (r is not null && !r.TargetHost.Equals(m.Host, StringComparison.OrdinalIgnoreCase)) problems.Add($"colectat pe {m.Host}, autorizat pentru {r.TargetHost}");
        }

        var root = Path.GetFullPath(packageDir) + Path.DirectorySeparatorChar;
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Files)
        {
            var full = Path.GetFullPath(Path.Combine(packageDir, f.Path));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(f.Path)) { problems.Add($"cale în afara pachetului: {f.Path}"); continue; }
            listed.Add(full);
            if (!File.Exists(full)) { problems.Add($"lipsește {f.Path}"); continue; }
            if (new FileInfo(full).Length != f.Size) problems.Add($"{f.Path}: dimensiune {new FileInfo(full).Length}, manifest {f.Size}");
            else if (!Hashing.Sha256File(full).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) problems.Add($"{f.Path}: SHA-256 diferit de cel calculat pe țintă");
        }
        foreach (var extra in Directory.GetFiles(packageDir, "*", SearchOption.AllDirectories).Select(Path.GetFullPath)
                     .Where(p => !listed.Contains(p) && !p.Equals(Path.GetFullPath(mp), StringComparison.OrdinalIgnoreCase) && !p.Equals(Path.GetFullPath(sp), StringComparison.OrdinalIgnoreCase)))
            problems.Add($"fișier care nu este în manifest: {Path.GetRelativePath(packageDir, extra)}");
        return new(problems.Count == 0, problems, m, r, packageDir);
    }

    /// <summary>
    /// The evidence type of a package file. A file of the Defender history folder is typed like a direct import (GUID name and matching content),
    /// so anything else placed there stays "file" and never reaches the DetectionHistory parser.
    /// </summary>
    internal static string ImportKind(string relativePath, string fullPath) =>
        relativePath.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase) ? "evtx" : relativePath.EndsWith(".hive", StringComparison.OrdinalIgnoreCase) ? "registry-hive"
        : relativePath.EndsWith(".pf", StringComparison.OrdinalIgnoreCase) ? "prefetch" : relativePath.Contains("/Tasks/", StringComparison.OrdinalIgnoreCase) ? "task-xml"
        : relativePath.Contains("/" + DefenderHistoryFolder + "/", StringComparison.OrdinalIgnoreCase)
            && Investigation.InvestigationPipeline.ImportType(fullPath) is var t && t == LogAnalyzer.Dfir.FileSystem.DefenderDetectionHistoryParser.SourceTypeName ? t
        : relativePath.Contains("/" + NotepadTabsFolder + "/", StringComparison.OrdinalIgnoreCase)
            && Investigation.InvestigationPipeline.ImportType(fullPath) is var n && n == LogAnalyzer.Dfir.FileSystem.NotepadTabStateParser.SourceTypeName ? n
        : relativePath.Equals("files/" + SearchIndexFolder + "/Windows.db", StringComparison.OrdinalIgnoreCase)
            && Investigation.InvestigationPipeline.ImportType(fullPath) is var s && s == Parsers.WindowsSearchIndexParser.SourceTypeName ? s
        : relativePath.Equals("files/" + SearchIndexFolder + "/Windows.db-wal", StringComparison.OrdinalIgnoreCase) ? WindowsSearchIndexCollector.WalSourceType
        : relativePath.Equals("files/" + SearchIndexFolder + "/Windows-gather.db", StringComparison.OrdinalIgnoreCase)
            && Investigation.InvestigationPipeline.ImportType(fullPath) is var g && g == Parsers.WindowsSearchGatherParser.SourceTypeName ? g
        : relativePath.Equals("files/" + SearchIndexFolder + "/Windows-gather.db-wal", StringComparison.OrdinalIgnoreCase) ? Parsers.WindowsSearchGatherParser.WalSourceType
        : "file";

    /// <summary>Imports a verified package: the manifest, then every file with the manifest as parent and custody from the target.</summary>
    public static IReadOnlyList<EvidenceItem> Import(CaseWorkspace ws, RemotePackageCheck check)
    {
        if (!check.Ok || check.Manifest is null || check.Request is null)
            throw new InvalidOperationException("Pachetul nu a trecut verificarea: " + string.Join("; ", check.Problems));
        var m = check.Manifest;
        var source = "remote:" + m.Host;
        var notes = $"cererea {m.RequestId}; colectat de {m.User} ({(m.Elevated ? "administrator" : "fără drepturi de administrator")}) la {m.CollectedUtc}";
        var manifest = ws.ImportFile(Path.Combine(check.PackageDir, "manifest.json"), source, "remote-manifest", TemporalType.CurrentSnapshot,
            CollectorName, DfirInfo.ApplicationVersion, parentEvidenceId: null, notes: notes);
        var items = new List<EvidenceItem> { manifest };
        // The search index: Windows.db and its -wal are stored together in a folder of their own for this import, so the journal the parser finds
        // beside the database is the one of this package (never one left by an earlier import). The pair is vouched for as one shadow copy only
        // when the package's search-index step ended "ok": the script then copied both files, or the database alone when there was no journal.
        bool indexStepOk = m.Steps.Any(s => s.Artifact == "search-index" && s.Status == "ok");
        // Windows-gather.db and its -wal go into the same folder; the folder id is the moment key written in the notes of both databases (the package script
        // takes them from one shadow copy, and the step must be "ok" for the note and the key to be written)
        string? indexDir = null, indexKey = null;
        foreach (var f in m.Files)
        {
            var full = Path.Combine(check.PackageDir, f.Path);
            var kind = ImportKind(f.Path, full);
            EvidenceItem item;
            if (kind is Parsers.WindowsSearchIndexParser.SourceTypeName or WindowsSearchIndexCollector.WalSourceType
                     or Parsers.WindowsSearchGatherParser.SourceTypeName or Parsers.WindowsSearchGatherParser.WalSourceType)
            {
                if (indexDir is null) { indexKey = Guid.NewGuid().ToString("N"); indexDir = Path.Combine(ws.RawDir(source), "WindowsSearch_" + indexKey); Directory.CreateDirectory(indexDir); }
                var dest = Path.Combine(indexDir, kind switch
                {
                    WindowsSearchIndexCollector.WalSourceType => "Windows.db-wal",
                    Parsers.WindowsSearchGatherParser.SourceTypeName => Parsers.WindowsSearchGatherParser.DatabaseFileName,
                    Parsers.WindowsSearchGatherParser.WalSourceType => Parsers.WindowsSearchGatherParser.DatabaseFileName + "-wal",
                    _ => "Windows.db",
                });
                File.Copy(full, dest);
                // checked before the file is registered beside the database: a journal swapped after Verify must not stay next to a vouched-for database
                var copied = Hashing.Sha256File(dest);
                if (!copied.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { Directory.Delete(indexDir, true); } catch (IOException) { }
                    throw new InvalidOperationException($"{f.Path}: SHA-256 la import ({copied}) diferă de cel de pe țintă ({f.Sha256}).");
                }
                File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(full));
                item = ws.RegisterStored(dest, full, source, kind, TemporalType.Historical, CollectorName, DfirInfo.ApplicationVersion, action: "imported", parentEvidenceId: manifest.EvidenceId,
                    notes: $"{m.Host}:{f.Path}; {notes}" + (indexStepOk && kind == Parsers.WindowsSearchIndexParser.SourceTypeName ? $"; copia de umbră a pachetului, {Parsers.WindowsSearchIndexParser.SameSnapshotNote}; {Parsers.WindowsSearchGatherParser.SnapshotKeyPrefix}{indexKey}"
                        : indexStepOk && kind == Parsers.WindowsSearchGatherParser.SourceTypeName ? $"; copia de umbră a pachetului, {Parsers.WindowsSearchGatherParser.GatherSameSnapshotNote}; {Parsers.WindowsSearchGatherParser.SnapshotKeyPrefix}{indexKey}" : ""));
            }
            else
                item = ws.ImportFile(full, source, kind,
                    kind is "registry-hive" or "task-xml" ? TemporalType.CurrentSnapshot : TemporalType.Historical,
                    CollectorName, DfirInfo.ApplicationVersion, parentEvidenceId: manifest.EvidenceId, notes: $"{m.Host}:{f.Path}; {notes}");
            if (!item.Sha256.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{f.Path}: SHA-256 la import ({item.Sha256}) diferă de cel de pe țintă ({f.Sha256}).");
            // Custody starts on the target: who collected it, when, from where, with the hash computed there.
            ws.Custody(new CustodyEntry(DateTimeOffset.TryParse(m.CollectedUtc, out var t) ? t : DateTimeOffset.MinValue, m.User, "remote-collected", item.EvidenceId,
                $"{m.Host}:{f.Path}", item.StoredPath, f.Sha256.ToUpperInvariant(), CollectorName, DfirInfo.ApplicationVersion, "", $"cererea {m.RequestId}"));
            items.Add(item);
        }
        var failed = m.Steps.Where(s => s.Status is not ("ok" or "absent")).Select(s => $"{s.Artifact}: {s.Error}").ToList();
        ws.Audit("remote.imported", $"{m.RequestId} host={m.Host} files={m.Files.Count} failed_steps={failed.Count} {string.Join(" | ", failed)}");
        return items;
    }
}
