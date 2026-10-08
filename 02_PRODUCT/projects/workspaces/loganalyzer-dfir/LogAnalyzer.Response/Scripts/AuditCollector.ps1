#Requires -Version 5.1
<#
.SYNOPSIS
  LogAnalyzer audit collector (PC / Server / NAS / DataCenter). Runs on Windows PowerShell 5.1, which ships with every supported
  Windows version; it needs no module outside Windows, no wmic, no RSAT and no network (except the explicit -AllowNetwork NAS step).

.DESCRIPTION
  Read-only: it never clears, edits or deletes anything on the station. Every output file gets a SHA-256 in CollectionManifest.csv.
  Nothing is skipped silently: a source that does not exist on this Windows version or edition (a module, a command, an event
  channel) is reported as UNAVAILABLE ("indisponibil pe acest sistem") with the reason; a source that failed is reported as ERROR.
  Exit code: 0 = no ERROR (UNAVAILABLE is not an error), 2 = at least one section failed, 1 = invalid parameters.
  Reviewed and ported from _recovered/desktop_mvp_scripts/AuditCollector.ps1 (wmic-free; wevtutil and reg.exe replaced by .NET APIs).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('PC', 'Server', 'NAS', 'DataCenter')][string]$TargetType,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Hostname,
    # The NAS permission audit reads a UNC share over SMB. LogAnalyzer passes this switch only when the edition and the signed policy allow network use.
    [switch]$AllowNetwork
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ($Hostname -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$') {
    Write-Output "[Collector] ERROR Hostname invalid: only letters, digits, '.', '_' and '-' are accepted."
    exit 1
}

$prefix = switch ($TargetType) { 'PC' { 'PC' } 'Server' { 'SRV' } 'NAS' { 'NAS' } 'DataCenter' { 'DC' } }
$targetPath = Join-Path $OutputDirectory (Join-Path (Get-Date).ToString('MM-yyyy') "${prefix}_${Hostname}")
New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
$dateSuffix = (Get-Date).ToString('dd-MM-yyyy')
$sysRoot = if ($env:SystemRoot) { $env:SystemRoot } else { [Environment]::GetFolderPath('Windows') }
$manifest = New-Object System.Collections.Generic.List[object]
Write-Output "[Collector] Colectare tip [$TargetType] pentru [$Hostname] in $targetPath (Windows PowerShell $($PSVersionTable.PSVersion))"

function Get-Sha256Hex([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::OpenRead($Path)
        try { return ([BitConverter]::ToString($sha.ComputeHash($fs)) -replace '-', '') } finally { $fs.Dispose() }
    } finally { $sha.Dispose() }
}

function Add-Manifest([string]$Section, [string]$Status, [string]$Detail, [string]$File = '') {
    $sha = ''
    if ($File -and (Test-Path -LiteralPath $File)) { try { $sha = Get-Sha256Hex $File } catch { $sha = 'N/A' } }
    $manifest.Add([pscustomobject]@{ Section = $Section; Status = $Status; Detail = $Detail; File = $(if ($File) { Split-Path -Leaf $File } else { '' }); SHA256 = $sha; TimeUtc = [DateTime]::UtcNow.ToString('o') })
    switch ($Status) {
        'UNAVAILABLE' { Write-Output "[Collector] INDISPONIBIL pe acest sistem: $Section - $Detail" }
        'ERROR' { Write-Output "[Collector] EROARE: $Section - $Detail" }
        default { Write-Output "[Collector] OK: $Section" }
    }
}

# Runs one section. RequiresCommand: commands/cmdlets that may be missing on some Windows versions or editions.
function Invoke-Section {
    param([string]$Name, [scriptblock]$Body, [string]$OutFile = '', [string[]]$RequiresCommand = @())
    foreach ($c in $RequiresCommand) {
        if (-not (Get-Command $c -ErrorAction SilentlyContinue)) {
            Add-Manifest $Name 'UNAVAILABLE' "comanda sau modulul '$c' nu exista in aceasta versiune sau editie de Windows"
            return
        }
    }
    try {
        $rows = @(& $Body)
        if ($OutFile) {
            $path = Join-Path $targetPath "${prefix}_${Hostname}_${OutFile}_${dateSuffix}.csv"
            if ($rows.Count -gt 0) { $rows | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding UTF8 } else { '' | Set-Content -LiteralPath $path -Encoding UTF8 }
            Add-Manifest $Name 'OK' "$($rows.Count) randuri" $path
        }
        else { Add-Manifest $Name 'OK' '' }
    }
    catch [System.UnauthorizedAccessException] { Add-Manifest $Name 'ERROR' "acces refuzat (rulati ca administrator): $($_.Exception.Message)" }
    catch { Add-Manifest $Name 'ERROR' $_.Exception.Message }
}

# Event log export through the Windows Event Log API (no wevtutil). A channel that does not exist is UNAVAILABLE, not an error.
function Export-Channel([string]$Channel) {
    $safe = $Channel -replace '[\\/ ]', '_'
    $name = "EVTX $Channel"
    try {
        $session = New-Object System.Diagnostics.Eventing.Reader.EventLogSession
        if (-not (@($session.GetLogNames()) -contains $Channel)) { Add-Manifest $name 'UNAVAILABLE' "canalul de evenimente nu exista pe acest sistem"; return }
        $dest = Join-Path $targetPath "${prefix}_${Hostname}_${safe}_${dateSuffix}.evtx"
        $session.ExportLog($Channel, [System.Diagnostics.Eventing.Reader.PathType]::LogName, '*', $dest)
        Add-Manifest $name 'OK' '' $dest
    }
    catch { Add-Manifest $name 'ERROR' $_.Exception.Message }
}

function Get-UsersRoot {
    try {
        $v = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList' -Name ProfilesDirectory).ProfilesDirectory
        return [Environment]::ExpandEnvironmentVariables($v)
    } catch { return (Join-Path $env:SystemDrive 'Users') }
}

function Get-TaskRows {
    Get-ScheduledTask | Where-Object { $_.State -ne 'Disabled' } | ForEach-Object {
        $info = Get-ScheduledTaskInfo -TaskName $_.TaskName -TaskPath $_.TaskPath -ErrorAction SilentlyContinue
        [pscustomobject]@{
            TaskName = $_.TaskName; TaskPath = $_.TaskPath; State = $_.State
            Actions = (($_.Actions | ForEach-Object { "$($_.Execute) $($_.Arguments)" }) -join '; ')
            RunAs = $_.Principal.UserId
            LastRun = $(if ($info) { $info.LastRunTime } else { $null }); NextRun = $(if ($info) { $info.NextRunTime } else { $null }); LastResult = $(if ($info) { $info.LastTaskResult } else { $null })
        }
    }
}

function Get-FirewallRows {
    Get-NetFirewallRule -Enabled True -Direction Inbound | ForEach-Object {
        $p = $_ | Get-NetFirewallPortFilter; $a = $_ | Get-NetFirewallAddressFilter; $app = $_ | Get-NetFirewallApplicationFilter
        [pscustomobject]@{ Name = $_.Name; DisplayName = $_.DisplayName; Action = $_.Action; Direction = $_.Direction; LocalPort = $p.LocalPort; RemotePort = $p.RemotePort
            Protocol = $p.Protocol; LocalAddress = $a.LocalAddress; RemoteAddress = $a.RemoteAddress; Program = $app.Program }
    }
}

function Get-DriverRows {
    Get-CimInstance Win32_SystemDriver | ForEach-Object {
        $path = [string]$_.PathName
        $sysPath = $path.Replace('"', '')
        if ($sysPath -match '(?i)^\\SystemRoot\\') { $sysPath = $sysRoot + $sysPath.Substring(11) }
        elseif ($sysPath.StartsWith('\??\')) { $sysPath = $sysPath.Substring(4) }
        elseif ($sysPath -match '(?i)^system32\\') { $sysPath = Join-Path $sysRoot $sysPath }
        $sig = $null
        if ($sysPath -and (Test-Path -LiteralPath $sysPath)) { $sig = Get-AuthenticodeSignature -FilePath $sysPath -ErrorAction SilentlyContinue }
        [pscustomobject]@{ Name = $_.Name; DisplayName = $_.DisplayName; State = $_.State; Path = $sysPath
            IsSigned = $(if ($sig) { $sig.Status -eq 'Valid' } else { $false }); Signer = $(if ($sig -and $sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { $null }) }
    }
}

function Get-RunKeyRows {
    foreach ($k in 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce',
                   'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run', 'HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce',
                   'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run') {
        if (-not (Test-Path $k)) { continue }
        $item = Get-ItemProperty -Path $k
        foreach ($p in $item.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' }) { [pscustomobject]@{ Key = $k; Name = $p.Name; Value = [string]$p.Value } }
    }
}

function Get-PsHistoryRows {
    $root = Get-UsersRoot
    foreach ($d in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue) {
        $f = Join-Path $d.FullName 'AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt'
        if (-not (Test-Path -LiteralPath $f)) { continue }
        foreach ($line in Get-Content -LiteralPath $f -ErrorAction SilentlyContinue) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { [pscustomobject]@{ User = $d.Name; Command = $line; File = $f } }
        }
    }
}

function Get-UsbRows {
    $k = 'HKLM:\SYSTEM\CurrentControlSet\Enum\USBSTOR'
    if (-not (Test-Path $k)) { return }
    foreach ($dev in Get-ChildItem -Path $k -ErrorAction SilentlyContinue) {
        foreach ($inst in Get-ChildItem -Path $dev.PSPath -ErrorAction SilentlyContinue) {
            $prop = Get-ItemProperty -Path $inst.PSPath -ErrorAction SilentlyContinue
            [pscustomobject]@{ Device = $dev.PSChildName; SerialNumber = $inst.PSChildName; FriendlyName = $prop.FriendlyName; Service = $prop.Service }
        }
    }
}

function Get-DefenderRows {
    [pscustomobject]@{ Setting = 'ComputerStatus'; Value = ((Get-MpComputerStatus | Select-Object AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, SignatureLastUpdated | ConvertTo-Json -Compress)) }
    [pscustomobject]@{ Setting = 'Preference'; Value = ((Get-MpPreference | Select-Object DisableRealtimeMonitoring, ExclusionPath, ExclusionProcess | ConvertTo-Json -Compress)) }
}

function Get-ProcessRows {
    Get-Process | Where-Object { $_.Path } | ForEach-Object {
        $h = 'N/A'
        try { $h = Get-Sha256Hex $_.Path } catch { }
        [pscustomobject]@{ PID = $_.Id; Name = $_.Name; Path = $_.Path; Company = $_.Company; SHA256 = $h }
    }
}

function Add-CommonSections {
    Invoke-Section 'Conexiuni TCP' { Get-NetTCPConnection | Select-Object LocalAddress, LocalPort, RemoteAddress, RemotePort, State, OwningProcess } 'Netstat' -RequiresCommand Get-NetTCPConnection
    Invoke-Section 'DNS Client Cache' { Get-DnsClientCache | Select-Object Entry, RecordName, RecordType, Data, TimeToLive, Status } 'DnsCache' -RequiresCommand Get-DnsClientCache
    Invoke-Section 'Scheduled Tasks' { Get-TaskRows } 'ScheduledTasks' -RequiresCommand Get-ScheduledTask, Get-ScheduledTaskInfo
    Invoke-Section 'Utilizatori locali' { Get-LocalUser | Select-Object Name, Enabled, LastLogon, PasswordLastSet, PasswordExpires } 'LocalUsers' -RequiresCommand Get-LocalUser
    Invoke-Section 'Administratori locali' { Get-LocalGroupMember -Group 'Administrators' | Select-Object Name, SID, PrincipalSource } 'LocalAdmins' -RequiresCommand Get-LocalGroupMember
    Invoke-Section 'Reguli firewall active' { Get-FirewallRows } 'FirewallRules' -RequiresCommand Get-NetFirewallRule, Get-NetFirewallPortFilter
    Invoke-Section 'Drivere kernel' { Get-DriverRows } 'KernelDrivers' -RequiresCommand Get-CimInstance, Get-AuthenticodeSignature
    Invoke-Section 'Windows Defender' { Get-DefenderRows } 'Defender' -RequiresCommand Get-MpComputerStatus, Get-MpPreference
    Invoke-Section 'Istoric PowerShell' { Get-PsHistoryRows } 'PowerShellHistory'
}

switch ($TargetType) {
    'PC' {
        foreach ($c in 'Security', 'System', 'Application') { Export-Channel $c }
        Invoke-Section 'Chei de rulare automata' { Get-RunKeyRows } 'RunKeys'
        Invoke-Section 'Procese cu SHA256' { Get-ProcessRows } 'Processes'
        Add-CommonSections
        Invoke-Section 'Istoric USB (USBSTOR)' { Get-UsbRows } 'UsbHistory'
        # Raw output: the column layout and headers of qwinsta are localized, so it is stored as text, not parsed.
        if (Get-Command qwinsta -ErrorAction SilentlyContinue) {
            try { $f = Join-Path $targetPath "${prefix}_${Hostname}_ActiveSessions_${dateSuffix}.txt"; (& qwinsta 2>&1 | Out-String) | Set-Content -LiteralPath $f -Encoding UTF8; Add-Manifest 'Sesiuni utilizator / RDP' 'OK' '' $f }
            catch { Add-Manifest 'Sesiuni utilizator / RDP' 'ERROR' $_.Exception.Message }
        } else { Add-Manifest 'Sesiuni utilizator / RDP' 'UNAVAILABLE' "comanda 'qwinsta' nu exista in aceasta editie de Windows" }
        Invoke-Section 'Tabela de rutare' { Get-NetRoute | Select-Object DestinationPrefix, NextHop, RouteMetric, InterfaceAlias } 'NetRoutes' -RequiresCommand Get-NetRoute
    }
    'Server' {
        foreach ($c in 'Security', 'System') { Export-Channel $c }
        Invoke-Section 'Partajari SMB' { Get-SmbShare | Select-Object Name, Path, Description, Special } 'Shares' -RequiresCommand Get-SmbShare
        Invoke-Section 'Sesiuni SMB' { Get-SmbSession | Select-Object Dialect, ClientComputerName, UserName, NumOpens } 'Sessions' -RequiresCommand Get-SmbSession
        Add-CommonSections
    }
    'NAS' {
        Invoke-Section 'Conexiuni de retea mapate' { Get-CimInstance Win32_NetworkConnection | Select-Object LocalName, RemoteName, Status, ConnectionType } 'NetworkConnections' -RequiresCommand Get-CimInstance
        if (-not $AllowNetwork) {
            Add-Manifest 'Audit permisiuni pe partajare (SMB)' 'UNAVAILABLE' 'foloseste reteaua; blocat de modul AirGapped / politica semnata (pornit fara -AllowNetwork)'
        } else {
            Invoke-Section 'Audit permisiuni pe partajare (SMB)' {
                $unc = "\\$Hostname\Public"
                if (-not (Test-Path -LiteralPath $unc)) { throw "calea $unc nu este accesibila" }
                Get-ChildItem -LiteralPath $unc -Depth 1 | ForEach-Object { $acl = Get-Acl -LiteralPath $_.FullName; [pscustomobject]@{ Path = $_.FullName; Owner = $acl.Owner; AccessRules = $acl.AccessToString } }
            } 'AclAudit'
        }
    }
    'DataCenter' {
        $f = Join-Path $targetPath "${prefix}_${Hostname}_CollectorSetup_${dateSuffix}.log"
        'Syslog Receiver required on Port 514 for unidirectional streaming (start it from the application; needs the unclassified edition in connected mode).' | Set-Content -LiteralPath $f -Encoding UTF8
        Add-Manifest 'Instructiuni DataCenter' 'OK' '' $f
    }
}

$manifestPath = Join-Path $targetPath "${prefix}_${Hostname}_CollectionManifest_${dateSuffix}.csv"
$manifest | Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding UTF8
$errors = @($manifest | Where-Object Status -eq 'ERROR').Count
$unavailable = @($manifest | Where-Object Status -eq 'UNAVAILABLE').Count
Write-Output "[Collector] Gata: $(@($manifest | Where-Object Status -eq 'OK').Count) sectiuni OK, $unavailable indisponibile pe acest sistem, $errors cu eroare. Manifest: $manifestPath"
if ($errors -gt 0) { exit 2 }
exit 0
