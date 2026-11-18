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
    [property: JsonPropertyName("files")] IReadOnlyList<RemoteFile> Files,
    // the target's own Windows time-zone id: the session logs of AnyDesk / TeamViewer are in the station's local time. A package without it (older format)
    // leaves those times unconverted, never in the analyst's zone.
    [property: JsonPropertyName("timezone")] string? Timezone = null);

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

    /// <summary>The artifact name of the raw NTFS metadata step ($MFT + $Boot of the system volume) and its package sub-folder (files/NTFS/$MFT, files/NTFS/$Boot).</summary>
    public const string NtfsMetadataArtifact = "ntfs-metadata", NtfsFolder = "NTFS";

    public static readonly IReadOnlyList<string> KnownArtifacts =
    [
        "evtx:Security", "evtx:System", "evtx:Application", "evtx:Microsoft-Windows-PowerShell/Operational",
        "evtx:Microsoft-Windows-Windows Defender/Operational", "evtx:Microsoft-Windows-TaskScheduler/Operational",
        "evtx:Microsoft-Windows-Sysmon/Operational", "evtx:Microsoft-Windows-TerminalServices-LocalSessionManager/Operational",
        // RDP-SESSIONS reads the RemoteConnectionManager channel, REMOTE-WINRM the WinRM one
        "evtx:Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational", "evtx:Microsoft-Windows-WinRM/Operational",
        // SMB-SERVER-AUTH-FAIL reads SMBServer/Security 551, SMB-CLIENT-SHARE-DENIED reads SmbClient/Security 31010
        "evtx:Microsoft-Windows-SMBServer/Security", "evtx:Microsoft-Windows-SmbClient/Security",
        "hive:SYSTEM", "hive:SOFTWARE", "prefetch", "tasks", "defender-history", "notepad-tabs", "remote-tool-logs", "search-index", NtfsMetadataArtifact,
        AmcacheArtifact, SetupApiArtifact, FirewallLogArtifact,
    ];

    /// <summary>
    /// Artifacts that <see cref="Authorize"/> accepts only when the operator asks for them; never part of <see cref="KnownArtifacts"/> (the default list).
    /// "user-hives": NTUSER.DAT and UsrClass.dat of every profile; they hold personal history (typed paths, recent documents, shell folders).
    /// "srum" (per-application usage by user SID) and "recent-items" (shortcuts and Jump Lists of every profile) are personal history too: see <see cref="PersonalHistoryArtifacts"/>.
    /// </summary>
    public const string UserHivesArtifact = "user-hives";
    public const string SrumArtifact = "srum", RecentItemsArtifact = "recent-items";
    public static readonly IReadOnlyList<string> OptionalArtifacts = [UserHivesArtifact, SrumArtifact, RecentItemsArtifact];

    /// <summary>The optional artifacts the UI offers under one box, "Include istoricul personal (SRUM, fișiere recente)".</summary>
    public static readonly IReadOnlyList<string> PersonalHistoryArtifacts = [SrumArtifact, RecentItemsArtifact];

    /// <summary>Package sub-folder of the user hives: <c>files/UserHives/&lt;profile&gt;/{NTUSER.DAT|UsrClass.dat}</c>.</summary>
    public const string UserHivesFolder = "UserHives";

    /// <summary>Package sub-folders of the artifacts added to bring the package close to the live collection (one folder per artifact, so Verify can bind a file to its artifact).</summary>
    public const string AmcacheFolder = "Amcache", SrumFolder = "Srum", SetupApiFolder = "SetupApi", FirewallFolder = "Firewall", RecentItemsFolder = "RecentItems";

    /// <summary>The artifact names of Amcache.hve (+ .LOG1/.LOG2), setupapi.dev*.log and pfirewall.log (+ .old).</summary>
    public const string AmcacheArtifact = "amcache", SetupApiArtifact = "setupapi", FirewallLogArtifact = "firewall-log";

    /// <summary>The list a request carries: the default artifacts, plus the optional user hives and the personal history (SRUM, recent items) when the operator ticked them.</summary>
    public static IReadOnlyList<string> ArtifactsFor(bool includeUserHives, bool includePersonalHistory = false) =>
        [.. KnownArtifacts, .. (includeUserHives ? new[] { UserHivesArtifact } : []), .. (includePersonalHistory ? PersonalHistoryArtifacts : [])];

    private static bool IsAllowed(string artifact) => KnownArtifacts.Contains(artifact) || OptionalArtifacts.Contains(artifact);

    /// <summary>Package sub-folders for the Notepad tab files (one folder per profile) and for the Windows Search index (Windows.db with its -wal).</summary>
    public const string NotepadTabsFolder = "NotepadTabState", SearchIndexFolder = "WindowsSearch";

    /// <summary>Package sub-folder for the Defender DetectionHistory files (ProgramData\Microsoft\Windows Defender\Scans\History\Service\DetectionHistory).</summary>
    public const string DefenderHistoryFolder = "DefenderDetectionHistory";

    /// <summary>Package sub-folder for the session logs of remote-access tools: <c>files/RemoteToolLogs/{Users/&lt;profile&gt;|ProgramData|ProgramFiles|ProgramFilesX86}/{AnyDesk|TeamViewer}/&lt;file&gt;</c>.</summary>
    public const string RemoteToolLogsFolder = "RemoteToolLogs";

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
        foreach (var a in list.Where(a => !IsAllowed(a))) throw new ArgumentException($"Artefact necunoscut: {a}.");

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

    /// <summary>
    /// PowerShell helpers shared by the steps that read a file another process may hold open (user-hives, amcache, srum, recent-items, setupapi, firewall-log). Emitted before the
    /// step loop only when the request carries one of those artifacts, so a package without them contains no code to read hives and no call to esentutl.
    /// </summary>
    private const string SharedHelpers = """
            # BEGIN user-hives helpers (the tests extract this block and run it)
            function Get-ShadowCount {
                try { return @(Get-CimInstance -ClassName Win32_ShadowCopy -ErrorAction Stop).Count } catch { return $null }
            }
            # New-Object wraps the IOException of a failed open in a MethodInvocationException: the Win32 code (32 sharing violation, 33 lock violation) is in the
            # inner exception, so the whole chain is walked
            function Test-SharingViolation($ex) {
                $depth = 0
                while ($ex -and $depth -lt 8) {
                    if (($ex -is [IO.IOException]) -and ((($ex.HResult -band 0xFFFF) -eq 32) -or (($ex.HResult -band 0xFFFF) -eq 33))) { return $true }
                    $ex = $ex.InnerException; $depth++
                }
                return $false
            }
            # number of names (hard links) of a file, -1 when it cannot be read; fsutil reads the metadata, so it also works on a file another process holds open
            function Get-HardLinkCount([string]$path) {
                try {
                    $rows = @(& fsutil.exe hardlink list $path 2>$null | Where-Object { $_ -and $_.Trim() })
                    $code = $LASTEXITCODE; $global:LASTEXITCODE = 0
                    if ($code -ne 0) { return -1 }
                    return $rows.Count
                } catch { return -1 }
            }
            # END user-hives helpers
            # Guarded copy of ONE file, shared by amcache, srum, setupapi, firewall-log and recent-items. The caller has already checked that no folder on the way is a link
            # (Get-SafeItem). Here: the file is not a link, not larger than the cap, there is room for it (+256 MiB), it has a single name (a hard link is refused). It is read with sharing;
            # only a failure to OPEN it for a sharing or lock violation goes to the volume-shadow-copy copy (esentutl /vss), and only when the caller allows it ($vss). The shadow copies are
            # counted before the first such read and again at the end of the step (Get-VssLeft): one that stays is reported.
            $script:VssUsed = $false; $script:ShadowBefore = $null
            function Reset-VssState { $script:VssUsed = $false; $script:ShadowBefore = $null }
            function Get-VssLeft {
                if (-not $script:VssUsed) { return '' }
                $after = Get-ShadowCount
                if ($null -eq $script:ShadowBefore -or $null -eq $after) { return '; lista copiilor de umbră nu a putut fi citită: nu s-a putut verifica dacă a rămas una' }
                if ($after -gt $script:ShadowBefore) { return "; a rămas o copie de umbră nouă după colectare ($($after - $script:ShadowBefore)): verificați și ștergeți-o dacă nu aparține altui program" }
                return ''
            }
            # State of the item at $base\<parts>: 'absent', 'link' (a component or the item is a reparse point), 'dir' or 'file'
            function Get-SafeItem([string]$base, [string[]]$parts) {
                $src = $base; $gi = $null
                foreach ($part in $parts) {
                    $src = Join-Path $src $part
                    $gi = Get-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue
                    if (-not $gi) { return @{ State = 'absent'; Obj = $null } }
                    if ($gi.Attributes -band [IO.FileAttributes]::ReparsePoint) { return @{ State = 'link'; Obj = $null } }
                }
                return @{ State = $(if ($gi.PSIsContainer) { 'dir' } else { 'file' }); Obj = $gi }
            }
            # 'ok', 'vss' (copied from the shadow copy), 'skipped' (over the cap), 'link' (reparse point or hard link), 'busy' (held open by another process; only when $vss is $false) or 'failed'
            function Copy-GuardedFile([IO.FileSystemInfo]$gi, [string]$dest, [long]$cap, [bool]$vss) {
                if ($gi.Attributes -band [IO.FileAttributes]::ReparsePoint) { return 'link' }
                if ($gi.Length -gt $cap) { return 'skipped' }
                $free = (New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($dest))).AvailableFreeSpace
                if ($gi.Length + 256MB -gt $free) { return 'failed' }
                $hl = Get-HardLinkCount $gi.FullName
                if ($hl -lt 0) { return 'failed' }
                if ($hl -gt 1) { return 'link' }
                New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($dest)) -Force | Out-Null
                $in = $null; $outFs = $null; $busy = $false; $done = $false; $over = $false; $bad = $false
                try {
                    $in = New-Object IO.FileStream($gi.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite,Delete')
                    $outFs = New-Object IO.FileStream($dest, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
                    # capped copy: a file that grew past the cap after the size check stops at the cap + 1 byte
                    $buf = New-Object byte[] (1MB); $total = [long]0
                    while (($n = $in.Read($buf, 0, $buf.Length)) -gt 0) {
                        $total += $n
                        if ($total -gt $cap) { $over = $true; break }
                        $outFs.Write($buf, 0, $n)
                    }
                    $outFs.Dispose(); $outFs = $null
                    if ($over) { Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue } else { $done = $true }
                } catch {
                    if ($outFs) { $outFs.Dispose(); $outFs = $null }
                    Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue
                    $busy = ((-not $in) -and (Test-SharingViolation $_.Exception))
                    if (-not $busy) { $bad = $true }
                } finally { if ($in) { $in.Dispose() }; if ($outFs) { $outFs.Dispose() } }
                if ($over) { return 'skipped' }
                if ($bad) { return 'failed' }
                $viaVss = $false
                if ($busy) {
                    if (-not $vss) { return 'busy' }
                    if (-not $script:VssUsed) { $script:VssUsed = $true; $script:ShadowBefore = Get-ShadowCount }
                    & esentutl.exe /y $gi.FullName /vss /d $dest 2>&1 | Out-Null
                    $ecode = $LASTEXITCODE; $global:LASTEXITCODE = 0
                    if ($ecode -ne 0 -or -not (Test-Path -LiteralPath $dest -PathType Leaf)) { Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue; return 'failed' }
                    $viaVss = $true
                } elseif (-not $done) { return 'failed' }
                $now = Get-Item -LiteralPath $gi.FullName -Force -ErrorAction SilentlyContinue
                if ($now -and ($now.Attributes -band [IO.FileAttributes]::ReparsePoint)) { Remove-Item -LiteralPath $dest -Force; return 'link' }
                if ((New-Object IO.FileInfo $dest).Length -gt $cap) { Remove-Item -LiteralPath $dest -Force; return 'skipped' }
                if ($viaVss) { return 'vss' }
                return 'ok'
            }
            """;

    /// <summary>The PowerShell branch of the optional user-hives step; appended to the script only when the request carries <see cref="UserHivesArtifact"/>.</summary>
    private const string UserHivesBranch = """
                } elseif ($a -eq 'user-hives') {
                    # OPTIONAL (personal history): NTUSER.DAT and UsrClass.dat of every profile under Users, into files\UserHives\<profile>. No component of the path may be
                    # a link (the file too), at most 512 MiB per file, at most 200 profiles. A file another process holds open (a logged-on user) is read only on a sharing
                    # violation, through esentutl /vss; the shadow copies are counted before the first such read and after the loop, and one that stays is reported.
                    # The transaction logs (.LOG1/.LOG2) are not copied, as in the live collector. The helpers (Get-ShadowCount, Test-SharingViolation, Get-HardLinkCount) are defined before the loop.
                    Step $a {
                        $copied = 0; $failed = 0; $skipped = 0; $links = 0; $viaVss = 0; $taken = 0; $profilesLeft = 0
                        $cap = 512MB
                        $shadowBefore = $null; $vssUsed = $false
                        $free = (New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($files))).AvailableFreeSpace
                        $profiles = @(Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue | Sort-Object Name)
                        $wanted = @(@{ Name = 'NTUSER.DAT'; Parts = @('NTUSER.DAT') }, @{ Name = 'UsrClass.dat'; Parts = @('AppData', 'Local', 'Microsoft', 'Windows', 'UsrClass.dat') })
                        for ($pi = 0; $pi -lt $profiles.Count; $pi++) {
                            $p = $profiles[$pi]
                            if ($p.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                            $counted = $false
                            foreach ($w in $wanted) {
                                $src = $p.FullName; $present = $true; $ok = $true; $gi = $null
                                foreach ($part in $w.Parts) {
                                    $src = Join-Path $src $part
                                    $gi = Get-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue
                                    if (-not $gi) { $present = $false; break }
                                    if ($gi.Attributes -band [IO.FileAttributes]::ReparsePoint) { $ok = $false; $links++; break }
                                }
                                if (-not $present -or -not $ok) { continue }
                                if ($gi.PSIsContainer) { $failed++; continue }
                                if (-not $counted) {
                                    if ($taken -ge 200) { $profilesLeft = $profiles.Count - $pi; break }
                                    $counted = $true; $taken++
                                }
                                if ($gi.Length -gt $cap) { $skipped++; continue }
                                if ($gi.Length + 256MB -gt $free) { $failed++; continue }
                                # a file with more than one name (a hard link to another profile's hive or to any other file) is not a reparse point, so it is refused here
                                $hl = Get-HardLinkCount $gi.FullName
                                if ($hl -lt 0) { $failed++; continue }
                                if ($hl -gt 1) { $links++; continue }
                                $d = Join-Path (Join-Path $files 'UserHives') $p.Name
                                New-Item -ItemType Directory -Path $d -Force | Out-Null
                                $dest = Join-Path $d $w.Name
                                $in = $null; $outFs = $null; $busy = $false; $done = $false; $over = $false
                                try {
                                    $in = New-Object IO.FileStream($gi.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite,Delete')
                                    $outFs = New-Object IO.FileStream($dest, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
                                    # capped copy: a file that grew past the cap after the size check stops at the cap + 1 byte
                                    $buf = New-Object byte[] (1MB); $total = [long]0
                                    while (($n = $in.Read($buf, 0, $buf.Length)) -gt 0) {
                                        $total += $n
                                        if ($total -gt $cap) { $over = $true; break }
                                        $outFs.Write($buf, 0, $n)
                                    }
                                    $outFs.Dispose(); $outFs = $null
                                    if ($over) { Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue; $skipped++ } else { $done = $true }
                                } catch {
                                    if ($outFs) { $outFs.Dispose(); $outFs = $null }
                                    Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue
                                    # only a failure to OPEN the source for a sharing or lock violation goes to esentutl; any other error is a plain failure
                                    $busy = ((-not $in) -and (Test-SharingViolation $_.Exception))
                                    if (-not $busy) { $failed++ }
                                } finally { if ($in) { $in.Dispose() }; if ($outFs) { $outFs.Dispose() } }
                                if ($over) { continue }
                                if ($busy) {
                                    if (-not $vssUsed) { $vssUsed = $true; $shadowBefore = Get-ShadowCount }
                                    & esentutl.exe /y $gi.FullName /vss /d $dest 2>&1 | Out-Null
                                    $ecode = $LASTEXITCODE; $global:LASTEXITCODE = 0
                                    if ($ecode -ne 0 -or -not (Test-Path -LiteralPath $dest -PathType Leaf)) { Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue; $failed++; continue }
                                    $done = $true; $viaVss++
                                }
                                if (-not $done) { continue }
                                $now = Get-Item -LiteralPath $gi.FullName -Force -ErrorAction SilentlyContinue
                                if ($now -and ($now.Attributes -band [IO.FileAttributes]::ReparsePoint)) { Remove-Item -LiteralPath $dest -Force; $links++; if ($busy) { $viaVss-- }; continue }
                                if ((New-Object IO.FileInfo $dest).Length -gt $cap) { Remove-Item -LiteralPath $dest -Force; $skipped++; if ($busy) { $viaVss-- }; continue }
                                $copied++
                                $free -= (New-Object IO.FileInfo $dest).Length
                            }
                            if ($profilesLeft -gt 0) { break }
                        }
                        $left = ''
                        if ($vssUsed) {
                            $shadowAfter = Get-ShadowCount
                            if ($null -eq $shadowBefore -or $null -eq $shadowAfter) { $left = '; lista copiilor de umbra nu a putut fi citita: nu s-a putut verifica daca a ramas una' }
                            elseif ($shadowAfter -gt $shadowBefore) { $left = "; a ramas o copie de umbra noua dupa colectare ($($shadowAfter - $shadowBefore)): verificati si stergeti-o daca nu apartine altui program" }
                        }
                        if ($failed -gt 0 -or $skipped -gt 0 -or $links -gt 0 -or $profilesLeft -gt 0 -or $left) { throw "hive-uri copiate: $copied (din copia de umbra: $viaVss); necopiate (acces refuzat, loc insuficient sau esec esentutl): $failed; sarite (peste 512 MiB): $skipped; legaturi neurmate: $links; profiluri peste limita de 200 necolectate: $profilesLeft$left" }
                        if ($copied -eq 0) { return '__absent__' }
                    }
            """;

    /// <summary>Amcache.hve with its transaction logs, as the live ExecutionArtifactsCollector takes them (the live files are locked: shadow copy only on a sharing violation).</summary>
    private const string AmcacheBranch = """
                } elseif ($a -eq 'amcache') {
                    # %SystemRoot%\appcompat\Programs\Amcache.hve and Amcache.hve.LOG1/.LOG2 (as the live collector) into files\Amcache. No component of the path may be a link, at most
                    # 512 MiB per file, a file with several names is refused. The file is held open by the system: it is read from a shadow copy (through the shared guarded copy) only on a
                    # sharing violation. A station without Amcache.hve is absent, never a failure.
                    Step $a {
                        Reset-VssState
                        $main = Get-SafeItem $WinDir @('appcompat', 'Programs', 'Amcache.hve')
                        if ($main.State -eq 'absent') { return '__absent__' }
                        $copied = 0; $failed = 0; $skipped = 0; $links = 0; $viaVss = 0
                        foreach ($suffix in @('', '.LOG1', '.LOG2')) {
                            $name = 'Amcache.hve' + $suffix
                            $s = Get-SafeItem $WinDir @('appcompat', 'Programs', $name)
                            if ($s.State -eq 'absent') { continue }
                            if ($s.State -eq 'link') { $links++; if ($suffix -eq '') { break }; continue }
                            if ($s.State -eq 'dir') { $failed++; continue }
                            $r = @(Copy-GuardedFile $s.Obj (Join-Path (Join-Path $files 'Amcache') $name) 512MB $true)[-1]
                            switch ($r) { 'ok' { $copied++ } 'vss' { $copied++; $viaVss++ } 'skipped' { $skipped++ } 'link' { $links++ } default { $failed++ } }
                        }
                        $left = Get-VssLeft
                        if ($failed -gt 0 -or $skipped -gt 0 -or $links -gt 0 -or $left) { throw "Amcache: fișiere copiate: $copied (din copia de umbră: $viaVss); necopiate (acces refuzat, loc insuficient sau eșec esentutl): $failed; sărite (peste 512 MiB): $skipped; legături neurmate: $links$left" }
                        if ($main.State -ne 'file' -or $copied -eq 0) { return '__absent__' }
                    }
    """;

    /// <summary>SRUDB.dat and the ESE files beside it that a recovery needs, as the live SrumCollector takes them.</summary>
    private const string SrumBranch = """
                } elseif ($a -eq 'srum') {
                    # OPTIONAL (usage history per application and user SID): %SystemRoot%\System32\sru\SRUDB.dat and the ESE files beside it (SRU.chk, SRUDB.jfm, SRU*.log, SRU*.jrs; at most 20, 64 MiB each)
                    # into files\Srum. The parser needs the logs of the SAME moment as the database to recover a "dirty" copy (JET -550), so the set is never mixed:
                    #  1. every file is read directly, with sharing (no link on the way, at most 2 GiB the database, a file with several names refused);
                    #  2. only if one of them is held open by the system (lab SRV01 2026-11-16: SRU.log and SRUtmp.log), the direct copies are removed and ALL the files are copied from ONE shadow copy
                    #     created by this step (WMI, as in search-index) and deleted at the end; the shadow copies are counted before and after, and one that stays is reported;
                    #  3. without a shadow copy the old rule applies: the database through esentutl /vss, the logs read directly (a log in use is named in the note) - the database may be dirty.
                    # SRUM names the applications itself (SruDbIdMapTable): the SOFTWARE hive is not needed. The note of the step says which moment the set comes from.
                    Step $a {
                        Reset-VssState
                        $main = Get-SafeItem $WinDir @('System32', 'sru', 'SRUDB.dat')
                        if ($main.State -eq 'absent') { return '__absent__' }
                        $dest = Join-Path $files 'Srum'
                        $auxRegex = '^(SRU\.chk|SRUDB\.jfm|SRU[^\\/]*\.(log|jrs))$'
                        $copied = 0; $failed = 0; $skipped = 0; $links = 0; $viaVss = 0; $mainOk = $false
                        $auxFailed = New-Object System.Collections.Generic.List[string]; $auxBusy = New-Object System.Collections.Generic.List[string]
                        $results = [ordered]@{}
                        $note = ''
                        if ($main.State -eq 'link') { $links++ } elseif ($main.State -eq 'dir') { $failed++ } else {
                            $sruDir = Get-SafeItem $WinDir @('System32', 'sru')
                            $set = New-Object System.Collections.Generic.List[object]
                            $set.Add(@{ Name = 'SRUDB.dat'; Obj = $main.Obj; Cap = 2GB })
                            if ($sruDir.State -eq 'dir') {
                                foreach ($f in @(Get-ChildItem -LiteralPath $sruDir.Obj.FullName -File -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -match $auxRegex } | Sort-Object Name | Select-Object -First 20)) {
                                    $set.Add(@{ Name = $f.Name; Obj = $f; Cap = 64MB })
                                }
                            }
                            $busyAny = $false
                            foreach ($e in $set) {
                                $r = @(Copy-GuardedFile $e.Obj (Join-Path $dest $e.Name) $e.Cap $false)[-1]
                                $results[$e.Name] = $r
                                if ($r -eq 'busy') { $busyAny = $true }
                            }
                            $momentUtc = (Get-Date).ToUniversalTime().ToString('o')
                            if (-not $busyAny) { $note = "SRUM: baza și jurnalele ESE citite direct, fără copie de umbră, în jurul momentului $momentUtc" }
                            elseif ($results.Values -contains 'link') { $note = '' }
                            else {
                                # step 2: ONE shadow copy for the whole set; the direct copies are removed first so that nothing of another moment stays
                                Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction SilentlyContinue
                                if (Test-Path -LiteralPath $dest) { throw 'SRUM: copiile directe ale setului nu au putut fi șterse înainte de copia de umbră; setul ar fi amestecat din momente diferite, deci nu a fost colectat' }
                                $results = [ordered]@{}
                                $made = $null; $sc = $null; $shadowErr = ''
                                if (-not $AllowShadowCopy) { $shadowErr = 'copia de umbră nu este permisă pe această rulare' }
                                else {
                                    $script:VssUsed = $true; $script:ShadowBefore = Get-ShadowCount
                                    try {
                                        $made = Invoke-CimMethod -ClassName Win32_ShadowCopy -MethodName Create -Arguments @{ Volume = $WinDir.Substring(0, 3); Context = 'ClientAccessible' } -ErrorAction Stop
                                        if ($made.ReturnValue -ne 0) { throw "copia de umbră nu a putut fi creată (cod $($made.ReturnValue))" }
                                        $momentUtc = (Get-Date).ToUniversalTime().ToString('o')
                                        $sc = Get-CimInstance -ClassName Win32_ShadowCopy | Where-Object { $_.ID -eq $made.ShadowID }
                                        if (-not $sc) { throw "copia de umbră $($made.ShadowID) nu a fost găsită" }
                                        $dev = [string]$sc.DeviceObject
                                        if ($dev -notmatch $ShadowDeviceRegex -or $dev.IndexOf("`n") -ge 0) { throw "DeviceObject neașteptat: $dev" }
                                        $shRoot = $dev + $sruDir.Obj.FullName.Substring(2) + [IO.Path]::DirectorySeparatorChar
                                        # the set is listed IN the shadow copy (the moment it describes); sizes and links are checked there before anything is written
                                        $shSet = New-Object System.Collections.Generic.List[object]
                                        $shSet.Add(@{ Name = 'SRUDB.dat'; Cap = 2GB })
                                        foreach ($fi in @((New-Object IO.DirectoryInfo $shRoot).GetFiles() | Where-Object { $_.Name -match $auxRegex } | Sort-Object Name | Select-Object -First 20)) { $shSet.Add(@{ Name = $fi.Name; Cap = 64MB }) }
                                        $need = [long]0
                                        foreach ($e in $shSet) { $fi = New-Object IO.FileInfo ($shRoot + $e.Name); if ($fi.Exists) { $need += $fi.Length } }
                                        $free = (New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($files))).AvailableFreeSpace
                                        if ($need + 256MB -gt $free) { throw "spațiu liber insuficient pentru setul SRUM ($need octeți); nu a fost copiat" }
                                        New-Item -ItemType Directory -Path $dest -Force | Out-Null
                                        foreach ($e in $shSet) {
                                            $fi = New-Object IO.FileInfo ($shRoot + $e.Name)
                                            if (-not $fi.Exists) { $results[$e.Name] = 'failed'; continue }
                                            if ($fi.Attributes -band [IO.FileAttributes]::ReparsePoint) { $results[$e.Name] = 'link'; continue }
                                            if ($fi.Length -gt $e.Cap) { $results[$e.Name] = 'skipped'; continue }
                                            try { [IO.File]::Copy($fi.FullName, (Join-Path $dest $e.Name), $false); $results[$e.Name] = 'vss' } catch { $results[$e.Name] = 'failed' }
                                        }
                                        if ($results.Values -contains 'failed' -or $results.Values -contains 'link' -or $results.Values -contains 'skipped') { Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction SilentlyContinue; throw 'setul SRUM nu a putut fi copiat complet din copia de umbră' }
                                        $note = "SRUM: baza și toate jurnalele ESE copiate din ACEEAȘI copie de umbră, creată la $momentUtc și ștearsă la final"
                                    } catch { $shadowErr = $_.Exception.Message; $results = [ordered]@{} }
                                    finally {
                                        # deleted through CIM; if that is impossible or fails, vssadmin deletes it by ShadowID; what stays is reported by the count below
                                        $gone = $false
                                        if ($sc) { try { $sc | Remove-CimInstance -ErrorAction Stop; $gone = $true } catch { $gone = $false } }
                                        if (-not $gone -and $made -and $made.ShadowID) {
                                            & vssadmin.exe delete shadows "/shadow=$($made.ShadowID)" /quiet 2>&1 | Out-Null
                                            $global:LASTEXITCODE = 0
                                        }
                                    }
                                }
                                if ($results.Count -eq 0) {
                                    # step 3: no shadow copy of the set: the old rule (database through esentutl, logs read directly); the sets are of different moments
                                    Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction SilentlyContinue
                                    if (Test-Path -LiteralPath $dest) { throw 'SRUM: copiile anterioare ale setului nu au putut fi șterse; setul ar fi amestecat din momente diferite, deci nu a fost colectat' }
                                    foreach ($e in $set) {
                                        $r = @(Copy-GuardedFile $e.Obj (Join-Path $dest $e.Name) $e.Cap ($e.Name -eq 'SRUDB.dat'))[-1]
                                        $results[$e.Name] = $r
                                    }
                                    # review 2026-11-16: the database goes through esentutl only when it is locked; otherwise it is read directly
                                    $dbHow = if ($results['SRUDB.dat'] -eq 'vss') { 'din copia esentutl' } else { 'citită direct' }
                                    $note = "SRUM: copia de umbră WMI a setului nu a putut fi folosită ($shadowErr); baza SRUDB.dat $dbHow, jurnalele ESE citite direct: sunt din alt moment decât baza și baza poate fi murdară"
                                }
                            }
                            foreach ($k in @($results.Keys)) {
                                $r = $results[$k]
                                switch ($r) { 'ok' { $copied++ } 'vss' { $copied++; $viaVss++ } 'skipped' { $skipped++ } 'link' { $links++ } 'busy' { if ($k -eq 'SRUDB.dat') { $failed++ } else { $auxBusy.Add($k) } } default { if ($k -eq 'SRUDB.dat') { $failed++ } else { $auxFailed.Add($k) } } }
                                if ($k -eq 'SRUDB.dat' -and ($r -eq 'ok' -or $r -eq 'vss')) { $mainOk = $true }
                            }
                        }
                        $left = Get-VssLeft
                        # lab SRV01 2026-11-16: the database was copied but two ESE logs in use were not, and the whole step said "failed". The step fails when the
                        # database is missing, a link was met or a shadow copy was left; logs in use are named in the note (the database is read without them)
                        if (-not $mainOk -or $failed -gt 0 -or $skipped -gt 0 -or $links -gt 0 -or $left) { throw "SRUM: fișiere copiate: $copied (din copia de umbră: $viaVss); necopiate (acces refuzat, loc insuficient sau eșec esentutl): $($failed + $auxFailed.Count + $auxBusy.Count); sărite (peste limită): $skipped; legături neurmate: $links$left" }
                        if ($auxBusy.Count -gt 0) { $note += "; jurnale ESE folosite de sistem, necopiate: $($auxBusy -join ', ')" }
                        if ($auxFailed.Count -gt 0) { $note += "; jurnale ESE necopiate din alt motiv (acces refuzat, loc insuficient sau eroare de citire): $($auxFailed -join ', ')" }
                        $global:StepNote = $note
                        if ($copied -eq 0) { return '__absent__' }
                    }
    """;

    /// <summary>setupapi.dev.log and its rotated copies, as the live SetupApiLogCollector takes them.</summary>
    private const string SetupApiBranch = """
                } elseif ($a -eq 'setupapi') {
                    # %SystemRoot%\INF\setupapi.dev.log first, then the newest rotated copies setupapi.dev.AAAAMMZZ_HHMMSS.log (a descending name is a descending date), at most 20 in all, as the live
                    # collector, into files\SetupApi. INF may not be a link; at most 128 MiB per file; a file with several names is refused; read with sharing, no shadow copy.
                    Step $a {
                        $inf = Get-SafeItem $WinDir @('INF')
                        if ($inf.State -eq 'absent') { return '__absent__' }
                        $copied = 0; $failed = 0; $skipped = 0; $links = 0
                        if ($inf.State -eq 'link') { $links++ } elseif ($inf.State -eq 'dir') {
                            $names = @(Get-ChildItem -LiteralPath $inf.Obj.FullName -File -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -cmatch '^setupapi\.dev(\.\d{8}_\d{6})?\.log$' } | ForEach-Object { $_.Name })
                            $rotated = @($names | Where-Object { $_ -cne 'setupapi.dev.log' } | Sort-Object -Descending)
                            $take = @(@($names | Where-Object { $_ -ceq 'setupapi.dev.log' }) + $rotated | Select-Object -First 20)
                            foreach ($name in $take) {
                                $s = Get-SafeItem $WinDir @('INF', $name)
                                if ($s.State -eq 'absent') { continue }
                                if ($s.State -eq 'link') { $links++; continue }
                                if ($s.State -eq 'dir') { $failed++; continue }
                                $r = @(Copy-GuardedFile $s.Obj (Join-Path (Join-Path $files 'SetupApi') $name) 128MB $false)[-1]
                                switch ($r) { 'ok' { $copied++ } 'skipped' { $skipped++ } 'link' { $links++ } default { $failed++ } }
                            }
                        } else { $failed++ }
                        if ($failed -gt 0 -or $skipped -gt 0 -or $links -gt 0) { throw "setupapi: jurnale copiate: $copied; necopiate (acces refuzat sau loc insuficient): $failed; sărite (peste 128 MiB): $skipped; legături neurmate: $links" }
                        if ($copied -eq 0) { return '__absent__' }
                    }
    """;

    /// <summary>pfirewall.log and pfirewall.log.old at their default path.</summary>
    private const string FirewallLogBranch = """
                } elseif ($a -eq 'firewall-log') {
                    # %SystemRoot%\System32\LogFiles\Firewall\pfirewall.log and pfirewall.log.old (the default path) into files\Firewall; absent when logging is off or the file is elsewhere.
                    # No component of the path may be a link; at most 64 MiB per file (the live collector's limit); a file with several names is refused; read with sharing, no shadow copy.
                    # A log that the policy or the local setting moved to another path is NOT collected here (the live collector reads that setting): the step note says so.
                    Step $a {
                        $dir = Get-SafeItem $WinDir @('System32', 'LogFiles', 'Firewall')
                        if ($dir.State -eq 'absent') { return '__absent__' }
                        $copied = 0; $failed = 0; $skipped = 0; $links = 0
                        if ($dir.State -eq 'link') { $links++ } elseif ($dir.State -eq 'dir') {
                            foreach ($name in @('pfirewall.log', 'pfirewall.log.old')) {
                                $s = Get-SafeItem $WinDir @('System32', 'LogFiles', 'Firewall', $name)
                                if ($s.State -eq 'absent') { continue }
                                if ($s.State -eq 'link') { $links++; continue }
                                if ($s.State -eq 'dir') { $failed++; continue }
                                $r = @(Copy-GuardedFile $s.Obj (Join-Path (Join-Path $files 'Firewall') $name) 64MB $false)[-1]
                                switch ($r) { 'ok' { $copied++ } 'skipped' { $skipped++ } 'link' { $links++ } default { $failed++ } }
                            }
                        } else { $failed++ }
                        if ($failed -gt 0 -or $skipped -gt 0 -or $links -gt 0) { throw "firewall: jurnale copiate: $copied; necopiate (acces refuzat sau loc insuficient): $failed; sărite (peste 64 MiB): $skipped; legături neurmate: $links" }
                        if ($copied -eq 0) { return '__absent__' }
                        $global:StepNote = 'doar calea implicită (System32\LogFiles\Firewall); un jurnal mutat prin politică sau setare locală nu este inclus'
                    }
    """;

    /// <summary>The shortcuts and Jump Lists of every profile, as the live RecentItemsCollector takes them (profiles from Users, not from ProfileList).</summary>
    private const string RecentItemsBranch = """
                } elseif ($a -eq 'recent-items') {
                    # OPTIONAL (personal history): per profile under Users, AppData\Roaming\Microsoft\Windows\Recent\*.lnk (at most 2000, 1 MiB each) and its AutomaticDestinations\*.automaticDestinations-ms
                    # (at most 500, 256 MiB each) and CustomDestinations\*.customDestinations-ms (at most 500, 16 MiB each), the newest first, into files\RecentItems\<profile>\{Recent|AutomaticDestinations|
                    # CustomDestinations}. No component of the path may be a link (the profile folder and every folder on the way, the file too); a file with several names is refused; at most 200
                    # profiles; empty files are skipped. Read with sharing, no shadow copy. Files over the count cap are counted as skipped.
                    Step $a {
                        $copied = 0; $failed = 0; $skipped = 0; $links = 0; $taken = 0; $profilesLeft = 0; $failNames = New-Object System.Collections.Generic.List[string]
                        $sets = @(
                            @{ Folder = ''; Out = 'Recent'; Ext = '.lnk'; MaxBytes = 1MB; MaxCount = 2000 },
                            @{ Folder = 'AutomaticDestinations'; Out = 'AutomaticDestinations'; Ext = '.automaticDestinations-ms'; MaxBytes = 256MB; MaxCount = 500 },
                            @{ Folder = 'CustomDestinations'; Out = 'CustomDestinations'; Ext = '.customDestinations-ms'; MaxBytes = 16MB; MaxCount = 500 })
                        $profiles = @(Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue | Sort-Object Name)
                        for ($pi = 0; $pi -lt $profiles.Count; $pi++) {
                            $p = $profiles[$pi]
                            if ($p.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                            $rec = Get-SafeItem $p.FullName @('AppData', 'Roaming', 'Microsoft', 'Windows', 'Recent')
                            if ($rec.State -eq 'absent') { continue }
                            if ($rec.State -eq 'link') { $links++; continue }
                            if ($rec.State -ne 'dir') { $failed++; continue }
                            if ($taken -ge 200) { $profilesLeft = $profiles.Count - $pi; break }
                            $taken++
                            foreach ($set in $sets) {
                                $dirItem = $rec.Obj
                                if ($set.Folder.Length -gt 0) {
                                    $sub = Get-SafeItem $rec.Obj.FullName @($set.Folder)
                                    if ($sub.State -eq 'absent') { continue }
                                    if ($sub.State -eq 'link') { $links++; continue }
                                    if ($sub.State -ne 'dir') { $failed++; continue }
                                    $dirItem = $sub.Obj
                                }
                                try {
                                    $found = @(Get-ChildItem -LiteralPath $dirItem.FullName -File -Force -ErrorAction Stop | Where-Object { $_.Name.EndsWith($set.Ext, [StringComparison]::OrdinalIgnoreCase) } | Sort-Object LastWriteTimeUtc -Descending)
                                } catch { $failed++; if ($failNames.Count -lt 5) { $failNames.Add($p.Name + '/' + $set.Out) }; continue }
                                if ($found.Count -gt $set.MaxCount) { $skipped += ($found.Count - $set.MaxCount) }
                                foreach ($f in @($found | Select-Object -First $set.MaxCount)) {
                                    if ($f.Attributes -band [IO.FileAttributes]::ReparsePoint) { $links++; continue }
                                    if ($f.Length -eq 0) { continue }
                                    $dest = Join-Path (Join-Path (Join-Path (Join-Path $files 'RecentItems') $p.Name) $set.Out) $f.Name
                                    $r = @(Copy-GuardedFile $f $dest $set.MaxBytes $false)[-1]
                                    switch ($r) { 'ok' { $copied++ } 'skipped' { $skipped++ } 'link' { $links++ } default { $failed++; if ($failNames.Count -lt 5) { $failNames.Add($p.Name + '/' + $set.Out + '/' + $f.Name) } } }
                                }
                            }
                        }
                        if ($failed -gt 0 -or $skipped -gt 0 -or $links -gt 0 -or $profilesLeft -gt 0) { throw "fișiere recente copiate: $copied; necopiate (acces refuzat, loc insuficient sau folder care nu poate fi citit): $failed; sărite (peste limita de mărime sau de număr): $skipped; legături neurmate: $links; profiluri peste limita de 200 necolectate: $profilesLeft$(if ($failNames.Count -gt 0) { '; primele necopiate: ' + ($failNames -join ', ') })" }
                        if ($copied -eq 0) { return '__absent__:nu există fișiere recente (LNK sau JumpLists) în profilurile stației' }
                    }
    """;

    /// <summary>The package script. It only reads the target and writes next to itself; it contains no network command.</summary>
    public static string PackageScript(RemoteCollectionRequest r, string requestSha256) => PackageScript(r, requestSha256, allowShadowCopy: true);

    /// <summary>
    /// <paramref name="allowShadowCopy"/> false makes the script behave as if no WMI shadow copy could be created (the SRUM step then uses its fallback rule). The tests use it so that
    /// they never create a shadow copy on the machine that runs them; a package for an operator always allows it.
    /// </summary>
    internal static string PackageScript(RemoteCollectionRequest r, string requestSha256, bool allowShadowCopy)
    {
        if (!HostName.IsMatch(r.TargetHost) || !Guid.TryParse(r.RequestId, out _) || !Regex.IsMatch(requestSha256, "^[0-9A-Fa-f]{64}$")
            || r.Artifacts.Any(a => !IsAllowed(a)))
            throw new ArgumentException("Cererea nu este validă pentru generarea pachetului.");
        var sb = new StringBuilder();
        sb.AppendLine("# LogAnalyzer - pachet de colectare DFIR. Ruleaza pe statia tinta, local; nu foloseste reteaua.");
        sb.AppendLine($"# Cerere {r.RequestId} | caz {OneLine(r.CaseId)} | tinta {r.TargetHost} | operator {OneLine(r.Operator)}");
        sb.AppendLine("#Requires -Version 5.1");
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$RequestId = {Q(r.RequestId)}");
        sb.AppendLine($"$RequestSha256 = {Q(requestSha256.ToUpperInvariant())}");
        sb.AppendLine($"$ExpectedHost = {Q(r.TargetHost)}");
        sb.AppendLine($"$AllowShadowCopy = {(allowShadowCopy ? "$true" : "$false")}");
        sb.AppendLine($"$Artifacts = @({string.Join(", ", r.Artifacts.Select(Q))})");
        sb.Append("""
            if ($env:COMPUTERNAME -ne $ExpectedHost) {
                Write-Error "Pachetul este autorizat pentru $ExpectedHost, nu pentru $env:COMPUTERNAME. Nu se colecteaza nimic."
                exit 2
            }
            $out = Join-Path $PSScriptRoot ('LA_' + $env:COMPUTERNAME + '_' + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
            $files = Join-Path $out 'files'
            New-Item -ItemType Directory -Path $files -Force | Out-Null
            # the Windows folder as a name under SystemDrive (SystemRoot is always on SystemDrive): the amcache, srum, setupapi and firewall-log steps build their paths from it
            $WinDir = Join-Path $env:SystemDrive ([IO.Path]::GetFileName($env:SystemRoot.TrimEnd('\')))
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
                try { $res = @(& $action); $abs = @($res | Where-Object { $_ -is [string] -and $_.StartsWith('__absent__') }); if ($abs.Count -gt 0) { $absent = $true; $err = $(if ($abs[0].Length -gt 11) { $abs[0].Substring(11) } else { 'folderul nu exista pe statie' }) } elseif ($LASTEXITCODE -eq 15007) { $absent = $true; $err = 'jurnalul nu exista pe statie (componenta nu este instalata)' } elseif ($LASTEXITCODE -ne 0) { $err = "cod de iesire $LASTEXITCODE" } } catch { $err = $_.Exception.Message }
                $steps.Add([ordered]@{ artifact = $name; startUtc = $start; endUtc = (Get-Date).ToUniversalTime().ToString('o'); status = $(if ($absent) { 'absent' } elseif ($err) { 'failed' } else { 'ok' }); error = $(if ($err) { $err } else { $global:StepNote }) })
            }
            # ntfs-metadata (T1-T6): $MFT and $Boot read RAW from the read-only device of ONE shadow copy created by the step (File.Copy of the shadow
            # copy's $MFT is refused). Every number read from the volume is hostile until checked; any refusal throws and removes this step's files.
            # \\{2} = the two leading backslashes of the device path; a literal run of four backslashes would trip the no-UNC check of the package-script test
            $ShadowDeviceRegex = '^\\{2}\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy\d+$'
            function Read-NtfsBytes([IO.Stream]$h, [long]$pos, [int]$n) {
                $b = New-Object byte[] $n
                $h.Position = $pos
                $got = 0
                while ($got -lt $n) {
                    $r = $h.Read($b, $got, $n - $got)
                    if ($r -le 0) { throw "citire scurta la pozitia $pos (cerut $n, primit $got)" }
                    $got += $r
                }
                return ,$b
            }
            function Copy-NtfsMetadataRaw([string]$device, [string]$dir) {
                $mftOut = [IO.Path]::Combine($dir, '$MFT'); $bootOut = [IO.Path]::Combine($dir, '$Boot')
                $h = $null; $fs = $null; $bfs = $null; $sha = $null; $madeMft = $false; $madeBoot = $false
                try {
                    $h = [IO.File]::Open($device, 'Open', 'Read', 'ReadWrite')
                    $boot = Read-NtfsBytes $h 0 512
                    if ($boot[510] -ne 0x55 -or $boot[511] -ne 0xAA) { throw 'sectorul de boot nu are semnatura 55AA' }
                    if ([Text.Encoding]::ASCII.GetString($boot, 3, 8) -ne 'NTFS    ') { throw 'volumul nu este NTFS' }
                    $bps = [int][BitConverter]::ToUInt16($boot, 11)
                    if (@(512, 1024, 2048, 4096) -notcontains $bps) { throw "bytes/sector $bps neacceptat" }
                    $spc = [int]$boot[13]
                    if ($spc -lt 1 -or $spc -gt 128 -or ($spc -band ($spc - 1)) -ne 0) { throw "sectoare/cluster $spc neacceptat" }
                    $cs = $bps * $spc
                    $total = [BitConverter]::ToInt64($boot, 40)
                    if ($total -le 0 -or $total -gt 2251799813685248) { throw "numar de sectoare $total neacceptat" }
                    $volBytes = [long]$total * $bps
                    $clusters = ([long]$total - ([long]$total % $spc)) / $spc
                    $mftLcn = [BitConverter]::ToInt64($boot, 48)
                    if ($mftLcn -lt 0 -or $mftLcn -ge $clusters) { throw "LCN-ul `$MFT $mftLcn este in afara volumului" }
                    $cpr = [int]$boot[64]; if ($cpr -gt 127) { $cpr -= 256 }
                    if ($cpr -lt 0) { if (-$cpr -gt 16) { throw "marimea inregistrarii (cod $cpr) neacceptata" }; $rs = [int](1 -shl (-$cpr)) } else { $rs = $cpr * $cs }
                    if ($rs -ne 1024 -and $rs -ne 4096) { throw "marimea inregistrarii $rs neacceptata (doar 1024 sau 4096)" }
                    if ($mftLcn * $cs + $rs -gt $volBytes) { throw 'inregistrarea 0 este in afara volumului' }
                    $rec = Read-NtfsBytes $h ($mftLcn * $cs) $rs
                    if ($rec[0] -ne 0x46 -or $rec[1] -ne 0x49 -or $rec[2] -ne 0x4C -or $rec[3] -ne 0x45) { throw 'inregistrarea 0 nu incepe cu FILE' }
                    $uo = [int][BitConverter]::ToUInt16($rec, 4); $uc = [int][BitConverter]::ToUInt16($rec, 6)
                    if ($uc -ne $rs / 512 + 1 -or $uo -lt 42 -or $uo + 2 * $uc -gt 510) { throw 'matricea de fixup a inregistrarii 0 este invalida' }
                    $usn = [BitConverter]::ToUInt16($rec, $uo)
                    # every sector end is checked BEFORE anything is restored
                    for ($i = 1; $i -lt $uc; $i++) { if ([BitConverter]::ToUInt16($rec, $i * 512 - 2) -ne $usn) { throw "fixup nepotrivit in sectorul $i al inregistrarii 0" } }
                    for ($i = 1; $i -lt $uc; $i++) { $rec[$i * 512 - 2] = $rec[$uo + 2 * $i]; $rec[$i * 512 - 1] = $rec[$uo + 2 * $i + 1] }
                    if (([BitConverter]::ToUInt16($rec, 22) -band 3) -ne 1) { throw 'inregistrarea 0 nu este o inregistrare de fisier in uz' }
                    if ([BitConverter]::ToUInt32($rec, 28) -ne $rs) { throw 'marimea alocata a inregistrarii 0 difera de cea din boot' }
                    if ([BitConverter]::ToInt64($rec, 32) -ne 0) { throw 'inregistrarea 0 nu este inregistrare de baza' }
                    $p = [int][BitConverter]::ToUInt16($rec, 20)
                    if ($p -lt 48 -or $p % 8 -ne 0) { throw 'primul atribut al inregistrarii 0 este invalid' }
                    $lcns = New-Object System.Collections.Generic.List[long]; $lens = New-Object System.Collections.Generic.List[long]
                    $found = 0; $realSize = [long]0; $sumClusters = [long]0; $guard = 0; $ended = $false
                    while ($p + 16 -le $rs) {
                        if (++$guard -gt 64) { throw 'prea multe atribute in inregistrarea 0' }
                        $t = [BitConverter]::ToUInt32($rec, $p)
                        if ($t -eq 4294967295) { $ended = $true; break }
                        $len = [int][BitConverter]::ToUInt32($rec, $p + 4)
                        if ($len -lt 16 -or $len % 8 -ne 0 -or $p + $len -gt $rs) { throw 'lungime de atribut invalida in inregistrarea 0' }
                        if ($t -eq 0x20) { throw 'inregistrarea 0 are $ATTRIBUTE_LIST: colectati $MFT cu aplicatia LogAnalyzer pe statie' }
                        if ($t -eq 0x80 -and $rec[$p + 9] -eq 0) {
                            $found++
                            if ($found -gt 1) { throw 'mai multe atribute $DATA fara nume' }
                            if ($rec[$p + 8] -ne 1) { throw '$DATA este rezident' }
                            if ($len -lt 64) { throw '$DATA nerezident prea scurt' }
                            if (([BitConverter]::ToUInt16($rec, $p + 12) -band 0xC001) -ne 0) { throw '$DATA este comprimat, criptat sau sparse' }
                            if ([BitConverter]::ToUInt16($rec, $p + 34) -ne 0) { throw '$DATA are unitate de compresie' }
                            if ([BitConverter]::ToInt64($rec, $p + 16) -ne 0) { throw '$DATA nu incepe la VCN 0' }
                            $realSize = [BitConverter]::ToInt64($rec, $p + 48)
                            if ([BitConverter]::ToInt64($rec, $p + 56) -ne $realSize) { throw 'valid data length difera de marimea reala' }
                            if ($realSize -le 0 -or $realSize -gt 16GB -or $realSize -gt $volBytes) { throw "marimea `$MFT $realSize neacceptata (peste 16 GiB sau peste volum)" }
                            # a real $MFT is a whole number of records and holds at least the 16 reserved system records
                            if ($realSize % $rs -ne 0 -or $realSize -lt 16 * $rs) { throw "marimea reala $realSize nu este multiplu de $rs sau este sub 16 inregistrari" }
                            $ro = [int][BitConverter]::ToUInt16($rec, $p + 32)
                            if ($ro -lt 64 -or $ro -ge $len) { throw 'offsetul listei de run-uri este invalid' }
                            $q = $p + $ro; $qEnd = $p + $len; $cur = [long]0; $lim = [long]4503599627370496
                            while ($true) {
                                if ($q -ge $qEnd) { throw 'lista de run-uri nu se termina in atribut' }
                                $hb = [int]$rec[$q]
                                if ($hb -eq 0) { break }
                                $lv = $hb -band 15; $lo = $hb -shr 4
                                if ($lv -lt 1 -or $lv -gt 8) { throw "run cu lungimea de $lv octeti neacceptata" }
                                if ($lo -eq 0) { throw 'run sparse in $MFT' }
                                if ($lo -gt 8) { throw "run cu offset de $lo octeti neacceptat" }
                                if ($q + 1 + $lv + $lo -gt $qEnd) { throw 'run in afara atributului' }
                                if ($lens.Count -ge 4096) { throw 'mai mult de 4096 run-uri' }
                                $q++
                                $n = [long]0; for ($k = 0; $k -lt $lv; $k++) { $n = $n -bor ([long]$rec[$q + $k] -shl (8 * $k)) }; $q += $lv
                                $off = [long]0; for ($k = 0; $k -lt $lo; $k++) { $off = $off -bor ([long]$rec[$q + $k] -shl (8 * $k)) }
                                if ($lo -lt 8 -and ($rec[$q + $lo - 1] -band 0x80)) { $off = $off - ([long]1 -shl (8 * $lo)) }
                                $q += $lo
                                if ($n -lt 1 -or $n -gt $lim -or $off -gt $lim -or $off -lt -$lim) { throw 'run cu valori neacceptate' }
                                $cur += $off
                                if ($cur -lt 0 -or $cur -ge $clusters -or $cur + $n -gt $clusters) { throw "run in afara volumului (LCN $cur, $n clustere)" }
                                if ($lens.Count -eq 0 -and $cur -ne $mftLcn) { throw 'primul run nu coincide cu LCN-ul $MFT din boot' }
                                $lcns.Add($cur); $lens.Add($n); $sumClusters += $n
                            }
                            if ($lens.Count -eq 0) { throw 'lista de run-uri este goala' }
                            # as in NtfsVolumeReader: sorted by LCN the runs never overlap, and none covers cluster 0 (the boot sector)
                            $ord = @(0..($lens.Count - 1) | Sort-Object { $lcns[$_] })
                            for ($oi = 0; $oi -lt $ord.Count; $oi++) {
                                if ($lcns[$ord[$oi]] -lt 1) { throw 'run care acopera clusterul 0 (sectorul de boot)' }
                                if ($oi -gt 0 -and $lcns[$ord[$oi]] -lt $lcns[$ord[$oi - 1]] + $lens[$ord[$oi - 1]]) { throw "run-uri suprapuse la LCN $($lcns[$ord[$oi]])" }
                            }
                            if ($sumClusters * $cs -lt $realSize) { throw 'run-urile acopera mai putin decat marimea reala' }
                        }
                        $p += $len
                    }
                    if (-not $ended) { throw 'lista de atribute a inregistrarii 0 nu are marcaj de sfarsit' }
                    if ($found -ne 1) { throw 'inregistrarea 0 nu are un $DATA nerezident fara nume' }
                    # space is checked BEFORE the first byte is written
                    $free = (New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($dir))).AvailableFreeSpace
                    if ($realSize + 512 + 256MB -gt $free) { throw "spatiu liber insuficient pentru `$MFT ($realSize octeti); nu a fost copiat" }
                    $bfs = [IO.File]::Open($bootOut, 'CreateNew', 'Write', 'None'); $madeBoot = $true
                    $bfs.Write($boot, 0, 512); $bfs.Dispose(); $bfs = $null
                    $bootHex = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($boot)) -replace '-', ''
                    $sha = [Security.Cryptography.SHA256]::Create()
                    $fs = [IO.File]::Open($mftOut, 'CreateNew', 'Write', 'None'); $madeMft = $true
                    $buf = New-Object byte[] (4MB); $left = $realSize
                    for ($ri = 0; $ri -lt $lens.Count -and $left -gt 0; $ri++) {
                        $h.Position = $lcns[$ri] * $cs
                        $bytes = [Math]::Min([long]$left, [long]($lens[$ri] * $cs))
                        while ($bytes -gt 0) {
                            $want = [int][Math]::Min([long]$buf.Length, [long]$bytes); $got = 0
                            while ($got -lt $want) {
                                $r = $h.Read($buf, $got, $want - $got)
                                if ($r -le 0) { throw "citire scurta in run-ul $ri (lipsesc $($want - $got) octeti)" }
                                $got += $r
                            }
                            $fs.Write($buf, 0, $got); [void]$sha.TransformBlock($buf, 0, $got, $null, 0)
                            $bytes -= $got; $left -= $got
                        }
                    }
                    if ($left -ne 0) { throw "run-urile au acoperit mai putin decat marimea ($left octeti lipsa)" }
                    [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0); $fs.Dispose(); $fs = $null
                    $mftHex = [BitConverter]::ToString($sha.Hash) -replace '-', ''
                    # the hash computed during the copy must equal the hash of the file as written
                    if ((New-Object IO.FileInfo $mftOut).Length -ne $realSize -or (Get-Sha256Hex $mftOut) -ne $mftHex) { throw '$MFT scris difera de cel citit (SHA-256 sau marime)' }
                    if ((Get-Sha256Hex $bootOut) -ne $bootHex) { throw '$Boot scris difera de cel citit (SHA-256)' }
                    return "`$MFT: $realSize octeti, $($lens.Count) run-uri, inregistrare de $rs octeti"
                } catch {
                    if ($fs) { $fs.Dispose(); $fs = $null }
                    if ($bfs) { $bfs.Dispose(); $bfs = $null }
                    if ($madeMft) { Remove-Item -LiteralPath $mftOut -Force -ErrorAction SilentlyContinue }
                    if ($madeBoot) { Remove-Item -LiteralPath $bootOut -Force -ErrorAction SilentlyContinue }
                    throw
                } finally {
                    if ($fs) { $fs.Dispose() }
                    if ($bfs) { $bfs.Dispose() }
                    if ($h) { $h.Dispose() }
                    if ($sha) { $sha.Dispose() }
                }
            }
            """);
        // the helpers of the steps that read files another process may hold open exist in the script only when a request asks for one of those steps
        if (r.Artifacts.Any(a => a is UserHivesArtifact or AmcacheArtifact or SrumArtifact or RecentItemsArtifact or SetupApiArtifact or FirewallLogArtifact))
        {
            sb.AppendLine();
            sb.Append(SharedHelpers);
        }
        sb.AppendLine();
        sb.Append("""
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
            """);
        sb.AppendLine();
        // the user-hives branch exists in the script only when the operator asked for it: a default package contains no code to read user hives
        if (r.Artifacts.Contains(UserHivesArtifact)) sb.Append(UserHivesBranch);
        // every other branch below is emitted the same way: only for the artifacts the request carries
        foreach (var (artifact, branch) in new[] { (AmcacheArtifact, AmcacheBranch), (SrumArtifact, SrumBranch), (SetupApiArtifact, SetupApiBranch), (FirewallLogArtifact, FirewallLogBranch), (RecentItemsArtifact, RecentItemsBranch) })
            if (r.Artifacts.Contains(artifact)) { sb.AppendLine(); sb.Append(branch); }
        sb.AppendLine();
        sb.Append("""
                } elseif ($a -eq 'remote-tool-logs') {
                    # the session logs of AnyDesk (connection_trace.txt, ad.trace, ad_svc.trace) and TeamViewer (Connections.txt, Connections_incoming.txt), per profile and in
                    # ProgramData / Program Files: no component of a path may be a link, at most 16 MiB per file (ad.trace / ad_svc.trace 64 MiB), empty files skipped
                    Step $a {
                        $copied = 0; $failed = 0; $skipped = 0; $links = 0
                        $targets = New-Object System.Collections.Generic.List[object]
                        foreach ($p in @(Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -Force -ErrorAction SilentlyContinue)) {
                            if ($p.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                            $targets.Add(@{ Base = $p.FullName; Parts = @('AppData', 'Roaming', 'AnyDesk'); Out = ('Users\' + $p.Name + '\AnyDesk'); Files = @('connection_trace.txt', 'ad.trace') })
                            $targets.Add(@{ Base = $p.FullName; Parts = @('AppData', 'Roaming', 'TeamViewer'); Out = ('Users\' + $p.Name + '\TeamViewer'); Files = @('Connections.txt') })
                            # RustDesk: the client writes in log\, the server side in log\server\, the connection manager in log\cm\ (the files are chosen by name below)
                            foreach ($sub in @('log', 'log\server', 'log\cm')) {
                                $targets.Add(@{ Base = $p.FullName; Parts = @(@('AppData', 'Roaming', 'RustDesk') + $sub.Split('\')); Out = ('Users\' + $p.Name + '\RustDesk\' + $sub); Files = @(); Kind = 'rustdesk' })
                            }
                        }
                        if ($env:ProgramData) { $targets.Add(@{ Base = $env:ProgramData; Parts = @('AnyDesk'); Out = 'ProgramData\AnyDesk'; Files = @('connection_trace.txt', 'ad_svc.trace') }) }
                        if ($env:ProgramFiles) { $targets.Add(@{ Base = $env:ProgramFiles; Parts = @('TeamViewer'); Out = 'ProgramFiles\TeamViewer'; Files = @('Connections_incoming.txt') }) }
                        if (${env:ProgramFiles(x86)}) { $targets.Add(@{ Base = ${env:ProgramFiles(x86)}; Parts = @('TeamViewer'); Out = 'ProgramFilesX86\TeamViewer'; Files = @('Connections_incoming.txt') }) }
                        # the RustDesk service runs as LocalService: its logs are under that service profile
                        if ($env:SystemRoot) {
                            foreach ($sub in @('log', 'log\server', 'log\cm')) {
                                $targets.Add(@{ Base = (Join-Path $env:SystemRoot 'ServiceProfiles\LocalService'); Parts = @(@('AppData', 'Roaming', 'RustDesk') + $sub.Split('\')); Out = ('ServiceProfiles\LocalService\RustDesk\' + $sub); Files = @(); Kind = 'rustdesk' })
                            }
                        }
                        foreach ($t in $targets) {
                            # every folder on the way is checked: a user can turn his own AppData\Roaming\AnyDesk into a junction to another folder
                            $src = $t.Base; $ok = $true
                            if (-not (Test-Path -LiteralPath $src)) { continue }
                            foreach ($part in $t.Parts) {
                                $src = Join-Path $src $part
                                if (-not (Test-Path -LiteralPath $src)) { $ok = $false; break }
                                if ((Get-Item -LiteralPath $src -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $ok = $false; $links++; break }
                            }
                            if (-not $ok) { continue }
                            # TeamViewer<NN>_Logfile.log / _OLD.log: the version number is part of the name, so the folder is listed (at most 16 files)
                            $names = @($t.Files)
                            if ($t.Out -like '*TeamViewer') { $names += @(Get-ChildItem -LiteralPath $src -File -Force -Filter 'TeamViewer*_Logfile*.log' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^TeamViewer\d{1,3}_Logfile(_OLD)?(_\d{1,6})?\.log$' } | Select-Object -First 16 | ForEach-Object { $_.Name }) }
                            # RustDesk rustdesk_rCURRENT.log / rustdesk_rYYYY-MM-DD_HH-MM-SS.log (optional copy suffix _N): ALL the entries are read and sorted BEFORE the first 30 are taken (no early cut, so the order of the enumeration cannot hide rCURRENT); rCURRENT is the newest, then by the time in the name
                            if ($t.Kind -eq 'rustdesk') { $names += @(Get-ChildItem -LiteralPath $src -File -Force -Filter 'rustdesk_r*.log' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^rustdesk_r(CURRENT|\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})(_\d{1,6})?\.log$' } | Sort-Object -Descending -Property @{ Expression = { if ($_.Name -match '^rustdesk_rCURRENT') { '9999-99-99_99-99-99' } else { $_.Name.Substring(10, 19) } } }, Name | Select-Object -First 30 | ForEach-Object { $_.Name }) }
                            foreach ($name in $names) {
                                $fp = Join-Path $src $name
                                if (-not (Test-Path -LiteralPath $fp -PathType Leaf)) { continue }
                                $f = Get-Item -LiteralPath $fp -Force
                                if ($f.Attributes -band [IO.FileAttributes]::ReparsePoint) { $links++; continue }
                                $cap = $(if ($name -like '*.trace' -or $name -like 'TeamViewer*_Logfile*.log' -or $name -like 'rustdesk_r*.log') { 64MB } else { 16MB })
                                if ($f.Length -eq 0) { continue }
                                if ($f.Length -gt $cap) { $skipped++; continue }
                                $d = Join-Path (Join-Path $files 'RemoteToolLogs') $t.Out
                                New-Item -ItemType Directory -Path $d -Force | Out-Null
                                $dest = Join-Path $d $name
                                # shared read (the tool may still hold the log open); a file that grew past the cap during the copy is removed
                                $in = $null; $outFs = $null
                                try {
                                    $in = New-Object IO.FileStream($f.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite,Delete')
                                    $outFs = New-Object IO.FileStream($dest, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
                                    $in.CopyTo($outFs)
                                    $outFs.Dispose(); $outFs = $null
                                    if ((New-Object IO.FileInfo $dest).Length -gt $cap) { Remove-Item -LiteralPath $dest -Force; $skipped++ } else { $copied++ }
                                } catch { $failed++; if ($outFs) { $outFs.Dispose(); $outFs = $null }; Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue }
                                finally { if ($in) { $in.Dispose() }; if ($outFs) { $outFs.Dispose() } }
                            }
                        }
                        if ($failed -gt 0 -or $skipped -gt 0 -or $links -gt 0) { throw "jurnale copiate: $copied; necopiate (fisier blocat sau acces refuzat): $failed; sarite (peste 16 MiB; ad.trace, TeamViewer*_Logfile.log și rustdesk_r*.log peste 64 MiB): $skipped; legaturi neurmate: $links" }
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
                } elseif ($a -eq 'ntfs-metadata') {
                    # $MFT + $Boot of the system volume from ONE shadow copy created here and deleted in finally (vssadmin fallback). The copy's device is
                    # opened read-only by Copy-NtfsMetadataRaw; only the DeviceObject of the copy this step created (found by its ShadowID) is ever opened.
                    Step $a {
                        $vol = $env:SystemDrive
                        if ($vol -notmatch '^[A-Za-z]:$') { throw "SystemDrive neasteptat: $vol" }
                        $made = Invoke-CimMethod -ClassName Win32_ShadowCopy -MethodName Create -Arguments @{ Volume = ($vol + '\'); Context = 'ClientAccessible' } -ErrorAction Stop
                        if ($made.ReturnValue -ne 0) { throw "copia de umbra nu a putut fi creata (cod $($made.ReturnValue))" }
                        $sc = $null
                        $d = Join-Path $files 'NTFS'
                        try {
                            $sc = Get-CimInstance -ClassName Win32_ShadowCopy | Where-Object { $_.ID -eq $made.ShadowID }
                            if (-not $sc) { throw "copia de umbra $($made.ShadowID) nu a fost gasita" }
                            $dev = [string]$sc.DeviceObject
                            if ($dev -notmatch $ShadowDeviceRegex -or $dev.IndexOf("`n") -ge 0) { throw "DeviceObject neasteptat: $dev" }
                            New-Item -ItemType Directory -Path $d -Force | Out-Null
                            try { $global:StepNote = Copy-NtfsMetadataRaw $dev $d }
                            catch { Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue; throw }
                        } finally {
                            # deleted through CIM; if that is impossible or fails, vssadmin deletes it by ShadowID; if that fails too the operator is told
                            $gone = $false
                            if ($sc) { try { $sc | Remove-CimInstance -ErrorAction Stop; $gone = $true } catch { $gone = $false } }
                            if (-not $gone) {
                                & vssadmin.exe delete shadows "/shadow=$($made.ShadowID)" /quiet 2>&1 | Out-Null
                                $code = $LASTEXITCODE; $global:LASTEXITCODE = 0
                                if ($code -ne 0) { throw "copia de umbra $($made.ShadowID) nu a putut fi stearsa; stergeti-o manual (vssadmin cod $code)" }
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
                collectedUtc = (Get-Date).ToUniversalTime().ToString('o'); steps = $steps.ToArray(); files = $list; timezone = [TimeZoneInfo]::Local.Id
            }
            $mp = Join-Path $out 'manifest.json'
            [IO.File]::WriteAllText($mp, ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
            [IO.File]::WriteAllText((Join-Path $out 'manifest.sha256'), (Get-Sha256Hex $mp) + "`n")
            Write-Output $out

            """);
        return sb.ToString();
    }

    /// <summary>Package folder (below files/) → the artifact that writes it. The NTFS folder is written only by the ntfs-metadata step of the script.</summary>
    private static readonly (string Folder, string Artifact)[] FolderArtifacts =
    [
        (UserHivesFolder, UserHivesArtifact), (RemoteToolLogsFolder, "remote-tool-logs"), (NotepadTabsFolder, "notepad-tabs"), (SearchIndexFolder, "search-index"),
        (DefenderHistoryFolder, "defender-history"), ("Prefetch", "prefetch"), ("Tasks", "tasks"), (NtfsFolder, NtfsMetadataArtifact),
        (AmcacheFolder, AmcacheArtifact), (SrumFolder, SrumArtifact), (SetupApiFolder, SetupApiArtifact), (FirewallFolder, FirewallLogArtifact), (RecentItemsFolder, RecentItemsArtifact),
    ];

    /// <summary>Null when the package file belongs to an artifact of the request; otherwise the reason it does not.</summary>
    internal static string? UnauthorizedFile(string relativePath, IReadOnlyList<string> artifacts)
    {
        // review r2 2026-11-16: "files/Prefetch/../UserHives/x/NTUSER.DAT" passed as Prefetch while the file read was the UserHives one; a path of the manifest
        // must be canonical: forward slashes only, no "." / ".." / empty segment
        if (relativePath.Contains('\\')) return $"{relativePath}: calea din manifest conține „\\”";
        var seg = relativePath.Split('/');
        if (seg.Any(s => s is "" or "." or "..")) return $"{relativePath}: calea din manifest nu este canonică (segment gol, „.” sau „..”)";
        if (seg.Length < 2 || !seg[0].Equals("files", StringComparison.OrdinalIgnoreCase)) return $"{relativePath}: nu este sub files/";
        if (seg.Length == 2)
        {
            // files/<channel>.evtx and files/<hive>.hive are written one per requested artifact, with the name the script derives from it
            var name = seg[1];
            foreach (var a in artifacts)
            {
                if (a.StartsWith("evtx:", StringComparison.Ordinal) && name.Equals(Regex.Replace(a[5..], "[\\\\/: ]", "_") + ".evtx", StringComparison.OrdinalIgnoreCase)) return null;
                if (a.StartsWith("hive:", StringComparison.Ordinal) && name.Equals(a[5..] + ".hive", StringComparison.OrdinalIgnoreCase)) return null;
            }
            return $"{relativePath}: fișier care nu corespunde niciunui artefact din cerere";
        }
        foreach (var (folder, artifact) in FolderArtifacts)
            if (seg[1].Equals(folder, StringComparison.OrdinalIgnoreCase))
                return artifacts.Contains(artifact) ? null : $"{relativePath}: artefactul „{artifact}” nu este în cererea autorizată";
        return $"{relativePath}: folder care nu corespunde niciunui artefact";
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
            // the same file twice (compared case-insensitively on the full path) would be imported twice under one hash
            if (!listed.Add(full)) { problems.Add($"cale dublată în manifest: {f.Path}"); continue; }
            // every file must belong to an artifact the request authorized (a file injected into a folder of an artifact that was not asked for, with recomputed sums, is refused)
            if (r is not null && UnauthorizedFile(f.Path, r.Artifacts) is { } why) problems.Add(why);
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
    /// <summary>files/SYSTEM.hive and files/SOFTWARE.hive (written by "reg save" for hive:SYSTEM / hive:SOFTWARE) with regf content; any other .hive stays "file".</summary>
    private static string PackageHiveKind(string relativePath, string fullPath)
    {
        var name = Path.GetFileName(relativePath);
        var kind = name.Equals("SYSTEM.hive", StringComparison.OrdinalIgnoreCase) ? "system_hive" : name.Equals("SOFTWARE.hive", StringComparison.OrdinalIgnoreCase) ? "software_hive" : "";
        return kind.Length > 0 && relativePath.Count(ch => ch == '/') == 1 && LogAnalyzer.Dfir.Integrity.EvidenceFingerprint.Detect(fullPath) == "regf" ? kind : "file";
    }

    /// <summary>Matches the names the setupapi step collects (the live collector's pattern, tightened to the two real forms); the script carries the same pattern.</summary>
    private static readonly Regex SetupApiName = new(@"^setupapi\.dev(\.\d{8}_\d{6})?\.log$", RegexOptions.CultureInvariant);
    /// <summary>The ESE files beside SRUDB.dat that the srum step collects (the live collector's rule: SRU.chk, SRUDB.jfm, SRU*.log, SRU*.jrs).</summary>
    private static readonly Regex SrumAuxName = new(@"^(SRU\.chk|SRUDB\.jfm|SRU[^\\/]*\.(log|jrs))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The type of a file in one of the folders added for Amcache, SRUM, setupapi, the firewall log and the recent items; null when the path is not in one of them. Only the exact package
    /// layout, an exact name and the content (the fingerprint) give a type: anything else stays "file". The types are EXACTLY the ones the live collectors give.
    /// </summary>
    private static string? ExtraFolderKind(string relativePath, string fullPath)
    {
        var seg = relativePath.Split('/');
        if (seg.Length < 2 || !seg[0].Equals("files", StringComparison.OrdinalIgnoreCase)) return null;
        string Fp() => LogAnalyzer.Dfir.Integrity.EvidenceFingerprint.Detect(fullPath);
        if (seg[1].Equals(AmcacheFolder, StringComparison.OrdinalIgnoreCase))
            return seg.Length != 3 ? "file" : seg[2] switch
            {
                "Amcache.hve" => Fp() == "regf" && Investigation.InvestigationPipeline.ImportType(fullPath) == "amcache" ? "amcache" : "file",
                "Amcache.hve.LOG1" or "Amcache.hve.LOG2" => "amcache_log",
                _ => "file",
            };
        if (seg[1].Equals(SrumFolder, StringComparison.OrdinalIgnoreCase))
            return seg.Length != 3 ? "file" : seg[2] == "SRUDB.dat" ? (Fp() == "ese" ? "srum" : "file") : SrumAuxName.IsMatch(seg[2]) ? SrumCollector.SrumLogSourceType : "file";
        if (seg[1].Equals(SetupApiFolder, StringComparison.OrdinalIgnoreCase))
            return seg.Length == 3 && SetupApiName.IsMatch(seg[2]) && Fp() == "setupapi_dev" ? LogAnalyzer.Dfir.Devices.SetupApiLogParser.SourceTypeName : "file";
        if (seg[1].Equals(FirewallFolder, StringComparison.OrdinalIgnoreCase))
            return seg.Length == 3 && seg[2] is "pfirewall.log" or "pfirewall.log.old" && Fp() == "pfirewall" ? LogAnalyzer.Dfir.Network.FirewallLogParser.SourceTypeName : "file";
        if (seg[1].Equals(RecentItemsFolder, StringComparison.OrdinalIgnoreCase))
        {
            // files/RecentItems/<profile>/<Recent|AutomaticDestinations|CustomDestinations>/<name>
            if (seg.Length != 5 || seg[2].Length == 0) return "file";
            var (ext, type, finger) = seg[3] switch
            {
                "Recent" => (".lnk", "lnk", "lnk"),
                "AutomaticDestinations" => (".automaticDestinations-ms", "jumplist_auto", "cfb"),
                "CustomDestinations" => (".customDestinations-ms", LogAnalyzer.Dfir.FileSystem.CustomJumpListParser.SourceTypeName, "custom_destinations"),
                _ => ("", "", ""),
            };
            return type.Length > 0 && seg[4].EndsWith(ext, StringComparison.OrdinalIgnoreCase) && Fp() == finger ? type : "file";
        }
        return null;
    }

    /// <summary>Marker written in the notes of every file of one SRUM set; the key is the folder id the set got at import. Used again by the re-import into the case of the run.</summary>
    public const string SrumSetKeyPrefix = "set SRUM: ";

    /// <summary>The start of the srum step note when the whole set was read directly (step 1) or copied from ONE shadow copy (step 2): one moment.</summary>
    internal const string SrumDirectNotePrefix = "SRUM: baza și jurnalele ESE citite direct, fără copie de umbră", SrumShadowNotePrefix = "SRUM: baza și toate jurnalele ESE copiate din ACEEAȘI copie de umbră";

    /// <summary>The start of the srum step note of step 3 (no shadow copy of the set; the database and the logs are copied at different moments).</summary>
    internal const string SrumMixedNotePrefix = "SRUM: copia de umbră WMI a setului nu a putut fi folosită";

    /// <summary>
    /// Review 2026-11-17 LOW: a failed or unknown srum step (e.g. step 1 failed on a log over the limit) is NOT proven to be of different moments, only not proven
    /// to be of one; the notes say exactly that, never the stronger fact of step 3.
    /// </summary>
    public const string SrumUnprovenMomentNote = "set SRUM fără dovada unui singur moment: pasul srum al pachetului nu confirmă că baza și jurnalele ESE provin din aceeași copie";

    /// <summary>
    /// Written by the import in the notes of every file of a SRUM set that is not known to be of one moment (residual of planner #4 item 3: the fact was only
    /// in the manifest step note). SrumNetworkParser repeats it in its recovery gap. The application's own text, never the target's.
    /// </summary>
    public const string SrumMixedMomentNote = "set SRUM din momente diferite: baza și jurnalele ESE nu provin din aceeași copie (pachetul nu a putut folosi o copie de umbră pentru tot setul)";

    /// <summary>
    /// Null when the package says the set is of one moment: exactly one srum step, "ok", whose note starts with the step-1 or step-2 sentence.
    /// <see cref="SrumMixedMomentNote"/> for one "ok" step with the step-3 sentence. Anything else (a failed or missing step, two steps, an unknown note)
    /// gets <see cref="SrumUnprovenMomentNote"/>: an unproven moment is not presented as one, nor as a proven mix.
    /// </summary>
    internal static string? SrumSetMomentNote(IReadOnlyList<RemoteStep> steps)
    {
        var s = steps.Where(x => x.Artifact == SrumArtifact).ToList();
        if (s.Count == 1 && s[0].Status == "ok" && (s[0].Error ?? "") is var e)
        {
            if (e.StartsWith(SrumDirectNotePrefix, StringComparison.Ordinal) || e.StartsWith(SrumShadowNotePrefix, StringComparison.Ordinal)) return null;
            if (e.StartsWith(SrumMixedNotePrefix, StringComparison.Ordinal)) return SrumMixedMomentNote;
        }
        return SrumUnprovenMomentNote;
    }

    internal static string ImportKind(string relativePath, string fullPath) =>
        // the folders of Amcache / SRUM / setupapi / firewall log / recent items first, for the same reason as the user hives (a profile named "Tasks", a stray .evtx or .hive)
        ExtraFolderKind(relativePath, fullPath) is { } extraKind ? extraKind :
        // the user hives: only the exact package path files/UserHives/<profile>/{NTUSER.DAT|UsrClass.dat} and only regf content; anything else under the folder stays "file"
        // (tested first: a profile named "Tasks" must not make them task XML, and a stray .evtx or .hive there must not reach a parser)
        relativePath.StartsWith("files/" + UserHivesFolder + "/", StringComparison.OrdinalIgnoreCase) ? UserHiveKind(relativePath, fullPath)
                // audit 2026-11-16: ".hive" was typed "registry-hive" and the tasks "task-xml", types no parser reads: the SYSTEM / SOFTWARE hives of every remote package
        // were never parsed (services, USB, ShimCache missing). The types are now the parsers' own, decided by name AND content.
        : relativePath.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase) ? "evtx"
        : relativePath.EndsWith(".hive", StringComparison.OrdinalIgnoreCase) ? PackageHiveKind(relativePath, fullPath)
        : relativePath.EndsWith(".pf", StringComparison.OrdinalIgnoreCase) ? "prefetch"
        // the session logs of AnyDesk / TeamViewer: only inside the package folder, only an exact file name with matching content (a file of another kind
        // with the same name, or the same content under another name, stays "file"). Tested before "/Tasks/": a user profile named "Tasks" must not make them task XML.
        : relativePath.StartsWith("files/" + RemoteToolLogsFolder + "/", StringComparison.OrdinalIgnoreCase)
            && LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.ByName(Path.GetFileName(relativePath)) is not null
            && Investigation.InvestigationPipeline.ImportType(fullPath) is var rt && LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.IsKnown(rt) ? rt
        : relativePath.Contains("/Tasks/", StringComparison.OrdinalIgnoreCase) ? (Investigation.InvestigationPipeline.ImportType(fullPath) == "task_xml" ? "task_xml" : "file")
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
        : relativePath.Equals("files/" + NtfsFolder + "/" + NtfsMetadataCollector.MftFileName, StringComparison.Ordinal) ? LogAnalyzer.Dfir.FileSystem.MftParser.SourceTypeName
        : relativePath.Equals("files/" + NtfsFolder + "/" + NtfsMetadataCollector.BootFileName, StringComparison.Ordinal) ? NtfsMetadataCollector.BootSourceType
        : "file";

    private static string UserHiveKind(string relativePath, string fullPath)
    {
        var seg = relativePath.Split('/');
        if (seg.Length != 4 || seg[2].Length == 0) return "file";
        var expected = seg[3] switch { "NTUSER.DAT" => "ntuser_hive", "UsrClass.dat" => "usrclass_hive", _ => "" };
        return expected.Length > 0 && Investigation.InvestigationPipeline.ImportType(fullPath) == expected ? expected : "file";
    }

    /// <summary>Imports a verified package: the manifest, then every file with the manifest as parent and custody from the target.</summary>
    public static IReadOnlyList<EvidenceItem> Import(CaseWorkspace ws, RemotePackageCheck check)
    {
        if (!check.Ok || check.Manifest is null || check.Request is null)
            throw new InvalidOperationException("Pachetul nu a trecut verificarea: " + string.Join("; ", check.Problems));
        var m = check.Manifest;
        var source = "remote:" + m.Host;
        var notes = $"cererea {m.RequestId}; colectat de {m.User} ({(m.Elevated ? "administrator" : "fără drepturi de administrator")}) la {m.CollectedUtc}";
        // $MFT and $Boot are a pair only when the ntfs-metadata step ended "ok" (the script then wrote both from one shadow copy, hash checked);
        // from a failed step the files (the script removes them) would stay plain "file" evidence, never typed for MftParser.
        // Everything about the pair is checked HERE, before the first record is written: exactly one $MFT and one $Boot (exact, ordinal names), and the
        // moment of the shadow copy (the step's startUtc) must be readable: MftParser's 30-day window and system_mismatch_recent count from it.
        var ntfsStep = m.Steps.FirstOrDefault(s => s.Artifact == NtfsMetadataArtifact && s.Status == "ok");
        bool ntfsStepOk = ntfsStep is not null;
        DateTimeOffset ntfsMoment = default;
        if (ntfsStepOk)
        {
            foreach (var (rel, label) in new[] { ("files/" + NtfsFolder + "/" + NtfsMetadataCollector.MftFileName, NtfsMetadataCollector.MftFileName), ("files/" + NtfsFolder + "/" + NtfsMetadataCollector.BootFileName, NtfsMetadataCollector.BootFileName) })
                if (m.Files.Count(x => string.Equals(x.Path, rel, StringComparison.Ordinal)) != 1)
                    throw new InvalidOperationException($"pasul ntfs-metadata este „ok”, dar pachetul nu are exact un {label} ({rel}); importul este refuzat.");
            if (!DateTimeOffset.TryParse(ntfsStep!.StartUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out ntfsMoment))
                throw new InvalidOperationException($"startUtc „{ntfsStep.StartUtc}” al pasului ntfs-metadata nu poate fi citit; $MFT nu se importă cu o oră greșită.");
        }
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
        string? ntfsDir = null;
        // the SRUM set (database + ESE logs): one folder of its own with the ORIGINAL names, so SrumNetworkParser finds the logs beside the database (in the common folder of the
        // source a second file of the same name would become "_1"); one key in the notes of every file of the set
        string? srumDir = null, srumKey = null;
        // the moment fact goes into the notes of every file of the set (before the key, which stays the last segment: InvestigationPipeline.SrumSetKey)
        var srumMoment = SrumSetMomentNote(m.Steps) is { } mixed ? "; " + mixed : "";
        foreach (var f in m.Files)
        {
            var full = Path.Combine(check.PackageDir, f.Path);
            var kind = ImportKind(f.Path, full);
            if (!ntfsStepOk && kind is LogAnalyzer.Dfir.FileSystem.MftParser.SourceTypeName or NtfsMetadataCollector.BootSourceType) kind = "file";
            EvidenceItem item;
            if (kind is LogAnalyzer.Dfir.FileSystem.MftParser.SourceTypeName or NtfsMetadataCollector.BootSourceType)
            {
                ntfsDir ??= Directory.CreateDirectory(Path.Combine(ws.RawDir(source), "NTFS_" + Guid.NewGuid().ToString("N"))).FullName;
                var ndest = Path.Combine(ntfsDir, kind == NtfsMetadataCollector.BootSourceType ? NtfsMetadataCollector.BootFileName : NtfsMetadataCollector.MftFileName);
                File.Copy(full, ndest);
                var ncopied = Hashing.Sha256File(ndest);
                if (!ncopied.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { Directory.Delete(ntfsDir, true); } catch (IOException) { }
                    throw new InvalidOperationException($"{f.Path}: SHA-256 la import ({ncopied}) diferă de cel de pe țintă ({f.Sha256}).");
                }
                File.SetLastWriteTimeUtc(ndest, File.GetLastWriteTimeUtc(full));
                item = ws.RegisterStored(ndest, full, source, kind, TemporalType.Historical, CollectorName, DfirInfo.ApplicationVersion, action: "imported", parentEvidenceId: manifest.EvidenceId,
                    notes: $"{m.Host}:{f.Path}; {notes}; cererea {m.RequestId}; citit brut din copia de umbră a pachetului", timezone: m.Timezone is { Length: > 0 } ntz ? ntz : null, acquiredAtUtc: ntfsMoment);
            }
            else if (kind is "srum" or SrumCollector.SrumLogSourceType)
            {
                if (srumDir is null) { srumKey = Guid.NewGuid().ToString("N"); srumDir = Path.Combine(ws.RawDir(source), "SRUM_" + srumKey); Directory.CreateDirectory(srumDir); }
                var sdest = Path.Combine(srumDir, Path.GetFileName(f.Path));
                File.Copy(full, sdest);
                var scopied = Hashing.Sha256File(sdest);
                if (!scopied.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { Directory.Delete(srumDir, true); } catch (IOException) { }
                    throw new InvalidOperationException($"{f.Path}: SHA-256 la import ({scopied}) diferă de cel de pe țintă ({f.Sha256}).");
                }
                File.SetLastWriteTimeUtc(sdest, File.GetLastWriteTimeUtc(full));
                item = ws.RegisterStored(sdest, full, source, kind, TemporalType.Historical, CollectorName, DfirInfo.ApplicationVersion, action: "imported", parentEvidenceId: manifest.EvidenceId,
                    notes: $"{m.Host}:{f.Path}; {notes}{srumMoment}; {SrumSetKeyPrefix}{srumKey}", timezone: m.Timezone is { Length: > 0 } stz ? stz : null);
            }
            else if (kind is Parsers.WindowsSearchIndexParser.SourceTypeName or WindowsSearchIndexCollector.WalSourceType
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
                    kind is "system_hive" or "software_hive" or "task_xml" ? TemporalType.CurrentSnapshot : TemporalType.Historical,
                    CollectorName, DfirInfo.ApplicationVersion, parentEvidenceId: manifest.EvidenceId,
                    notes: f.Path.StartsWith("files/" + UserHivesFolder + "/", StringComparison.OrdinalIgnoreCase) && f.Path.Split('/') is { Length: >= 3 } uh
                        ? $"{m.Host}:{f.Path}; profil de origine: {uh[2]}; {notes}" : $"{m.Host}:{f.Path}; {notes}",
                    // the target's zone from the manifest; without one, a session log's local times stay unconverted (never the analyst's zone)
                    timezone: m.Timezone is { Length: > 0 } tz ? tz : LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.IsKnown(kind) ? "" : null);
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
