[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("PC", "Server", "NAS", "DataCenter")]
    [string]$TargetType,

    [Parameter(Mandatory=$true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory=$true)]
    [string]$Hostname
)

$ErrorActionPreference = "SilentlyContinue"

Write-Output "[Collector] Inițializare culegere date audit tip [$TargetType] pentru [$Hostname]..."

# 1. Structura ierarhică: [OutputDir]\[Month-Year]\[Prefix]_[Hostname]
$monthFolder = (Get-Date).ToString("MM-yyyy")
$prefix = switch ($TargetType) {
    "PC" { "PC" }
    "Server" { "SRV" }
    "NAS" { "NAS" }
    "DataCenter" { "DC" }
}

$targetFolderName = "${prefix}_${Hostname}"
$targetPath = Join-Path $OutputDirectory (Join-Path $monthFolder $targetFolderName)

if (!(Test-Path $targetPath)) {
    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
    Write-Output "[Collector] Directorul destinație a fost creat: $targetPath"
} else {
    Write-Output "[Collector] Directorul destinație există: $targetPath"
}

$dateSuffix = (Get-Date).ToString("dd-MM-yyyy")

switch ($TargetType) {
    "PC" {
        Write-Output "[Collector] Se colectează jurnalele EVTX de securitate..."
        wevtutil epl Security "$targetPath\PC_${Hostname}_Security_${dateSuffix}.evtx"
        wevtutil epl System "$targetPath\PC_${Hostname}_System_${dateSuffix}.evtx"
        wevtutil epl Application "$targetPath\PC_${Hostname}_Application_${dateSuffix}.evtx"

        Write-Output "[Collector] Se exportă cheile de rulare automată din Registry..."
        reg export HKCU\Software\Microsoft\Windows\CurrentVersion\Run "$targetPath\PC_${Hostname}_HKCU_RunKeys_${dateSuffix}.reg" /y | Out-Null
        reg export HKLM\Software\Microsoft\Windows\CurrentVersion\Run "$targetPath\PC_${Hostname}_HKLM_RunKeys_${dateSuffix}.reg" /y | Out-Null

        Write-Output "[Collector] Se analizează conexiunile TCP/UDP active..."
        Get-NetTCPConnection | Select-Object LocalAddress, LocalPort, RemoteAddress, RemotePort, State, OwningProcess | 
            Export-Csv -Path "$targetPath\PC_${Hostname}_Netstat_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează procesele active cu semnături hash (SHA256)..."
        Get-Process | Where-Object { $_.Path } | ForEach-Object {
            try {
                $hash = (Get-FileHash $_.Path -Algorithm SHA256).Hash
            } catch {
                $hash = "N/A"
            }
            [PSCustomObject]@{
                PID = $_.Id
                Name = $_.Name
                Path = $_.Path
                Company = $_.Company
                SHA256 = $hash
            }
        } | Export-Csv -Path "$targetPath\PC_${Hostname}_Processes_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează DNS Client Cache..."
        Get-DnsClientCache | Select-Object Entry, RecordName, RecordType, Data, TimeToLive, Status | 
            Export-Csv -Path "$targetPath\PC_${Hostname}_DnsCache_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează Scheduled Tasks active..."
        Get-ScheduledTask | Where-Object { $_.State -ne 'Disabled' } | ForEach-Object {
            $info = Get-ScheduledTaskInfo -TaskName $_.TaskName -TaskPath $_.TaskPath -ErrorAction SilentlyContinue
            [PSCustomObject]@{
                TaskName    = $_.TaskName
                TaskPath    = $_.TaskPath
                State       = $_.State
                Actions     = ($_.Actions | ForEach-Object { "$($_.Execute) $($_.Arguments)" }) -join "; "
                RunAs       = $_.Principal.UserId
                LastRun     = $info.LastRunTime
                NextRun     = $info.NextRunTime
                LastResult  = $info.LastTaskResult
            }
        } | Export-Csv -Path "$targetPath\PC_${Hostname}_ScheduledTasks_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează Utilizatorii și Administratorii Locali..."
        Get-LocalUser | Select-Object Name, Enabled, LastLogon, PasswordLastSet, PasswordExpires | 
            Export-Csv -Path "$targetPath\PC_${Hostname}_LocalUsers_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        Get-LocalGroupMember -Group "Administrators" | Select-Object Name, SID, PrincipalSource | 
            Export-Csv -Path "$targetPath\PC_${Hostname}_LocalAdmins_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează Regulile de Firewall Active..."
        Get-NetFirewallRule -Enabled True -Direction Inbound | ForEach-Object {
            $portFilter = $_ | Get-NetFirewallPortFilter
            $addressFilter = $_ | Get-NetFirewallAddressFilter
            $applicationFilter = $_ | Get-NetFirewallApplicationFilter
            [PSCustomObject]@{
                Name           = $_.Name
                DisplayName    = $_.DisplayName
                Action         = $_.Action
                Direction      = $_.Direction
                LocalPort      = $portFilter.LocalPort
                RemotePort     = $portFilter.RemotePort
                Protocol       = $portFilter.Protocol
                LocalAddress   = $addressFilter.LocalAddress
                RemoteAddress  = $addressFilter.RemoteAddress
                Program        = $applicationFilter.Program
            }
        } | Export-Csv -Path "$targetPath\PC_${Hostname}_FirewallRules_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează și verifică driverele active în kernel (Authenticode)..."
        Get-CimInstance Win32_SystemDriver | ForEach-Object {
            $path = $_.PathName
            if ($path -match '(?i)system32\\drivers\\([a-z0-9_-]+\.sys)') {
                $sysPath = "C:\Windows\System32\Drivers\$($Matches[1])"
            } else {
                $sysPath = $path.Replace('"', '').Replace('\\SystemRoot\\', 'C:\Windows\')
            }
            $sig = $null
            if (Test-Path $sysPath) {
                $sig = Get-AuthenticodeSignature -FilePath $sysPath -ErrorAction SilentlyContinue
            }
            [PSCustomObject]@{
                Name        = $_.Name
                DisplayName = $_.DisplayName
                State       = $_.State
                Path        = $sysPath
                IsSigned    = if ($sig) { $sig.Status -eq 'Valid' } else { $false }
                Signer      = if ($sig) { $sig.SignerCertificate.Subject } else { $null }
            }
        } | Export-Csv -Path "$targetPath\PC_${Hostname}_KernelDrivers_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se culeg detalii despre starea Windows Defender..."
        Get-MpComputerStatus | Select-Object AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, SignatureLastUpdated |
            Export-Csv -Path "$targetPath\PC_${Hostname}_DefenderStatus_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        Get-MpPreference | Select-Object DisableRealtimeMonitoring, ExclusionPath, ExclusionProcess |
            Export-Csv -Path "$targetPath\PC_${Hostname}_DefenderExclusions_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        # ---------------- DEEP FORENSIC ARTIFACTS ----------------
        Write-Output "[Collector] Se colectează istoricul comenzilor PowerShell (ConsoleHost_history)..."
        $psHistory = @()
        Get-ChildItem -Path "C:\Users" -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $userPath = $_.FullName
            $historyFile = Join-Path $userPath "AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt"
            if (Test-Path $historyFile) {
                Get-Content $historyFile -ErrorAction SilentlyContinue | ForEach-Object {
                    if (![string]::IsNullOrWhiteSpace($_)) {
                        $psHistory += [PSCustomObject]@{
                            User = (Split-Path $userPath -Leaf)
                            Command = $_
                            File = $historyFile
                        }
                    }
                }
            }
        }
        if ($psHistory.Count -gt 0) {
            $psHistory | Export-Csv -Path "$targetPath\PC_${Hostname}_PowerShellHistory_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        }

        Write-Output "[Collector] Se colectează istoricul dispozitivelor USB (USBSTOR)..."
        $usbItems = @()
        if (Test-Path "HKLM:\SYSTEM\CurrentControlSet\Enum\USBSTOR") {
            Get-ChildItem -Path "HKLM:\SYSTEM\CurrentControlSet\Enum\USBSTOR" -ErrorAction SilentlyContinue | ForEach-Object {
                $devKey = $_
                Get-ChildItem -Path $devKey.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
                    $prop = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue
                    $usbItems += [PSCustomObject]@{
                        Device = $devKey.PSChildName
                        SerialNumber = $_.PSChildName
                        FriendlyName = $prop.FriendlyName
                        Service = $prop.Service
                    }
                }
            }
        }
        if ($usbItems.Count -gt 0) {
            $usbItems | Export-Csv -Path "$targetPath\PC_${Hostname}_UsbHistory_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        }

        Write-Output "[Collector] Se culeg sesiunile active de utilizator și RDP..."
        $sessions = @()
        try {
            $rawSessions = qwinsta
            foreach ($line in ($rawSessions | Select-Object -Skip 1)) {
                if ($line -match '^\s*>?\s*([a-zA-Z0-9#-]+)\s+([a-zA-Z0-9._-]+)?\s+(\d+)\s+([a-zA-Z]+)') {
                    $sessions += [PSCustomObject]@{
                        SessionName = $Matches[1]
                        UserName    = if ($Matches[2]) { $Matches[2] } else { "(None)" }
                        SessionId   = $Matches[3]
                        State       = $Matches[4]
                    }
                }
            }
        } catch { }
        if ($sessions.Count -gt 0) {
            $sessions | Export-Csv -Path "$targetPath\PC_${Hostname}_ActiveSessions_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        }

        Write-Output "[Collector] Se colectează tabelele de rutare și Portproxy..."
        $routes = @()
        try {
            Get-NetRoute | Select-Object DestinationPrefix, NextHop, RouteMetric, InterfaceAlias | ForEach-Object {
                $routes += $_
            }
        } catch { }
        if ($routes.Count -gt 0) {
            $routes | Export-Csv -Path "$targetPath\PC_${Hostname}_NetRoutes_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        }
    }

    "Server" {
        Write-Output "[Collector] Se colectează jurnalele de securitate ale serverului..."
        wevtutil epl Security "$targetPath\SRV_${Hostname}_Security_${dateSuffix}.evtx"
        wevtutil epl System "$targetPath\SRV_${Hostname}_System_${dateSuffix}.evtx"

        Write-Output "[Collector] Se colectează partajările SMB active..."
        Get-SmbShare | Select-Object Name, Path, Description, Special | 
            Export-Csv -Path "$targetPath\SRV_${Hostname}_Shares_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se salvează conexiunile SMB active pe server..."
        Get-SmbSession | Select-Object Dialect, ClientComputerName, UserName, NumOpens | 
            Export-Csv -Path "$targetPath\SRV_${Hostname}_Sessions_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează DNS Client Cache..."
        Get-DnsClientCache | Select-Object Entry, RecordName, RecordType, Data, TimeToLive, Status | 
            Export-Csv -Path "$targetPath\SRV_${Hostname}_DnsCache_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează Scheduled Tasks active..."
        Get-ScheduledTask | Where-Object { $_.State -ne 'Disabled' } | ForEach-Object {
            $info = Get-ScheduledTaskInfo -TaskName $_.TaskName -TaskPath $_.TaskPath -ErrorAction SilentlyContinue
            [PSCustomObject]@{
                TaskName    = $_.TaskName
                TaskPath    = $_.TaskPath
                State       = $_.State
                Actions     = ($_.Actions | ForEach-Object { "$($_.Execute) $($_.Arguments)" }) -join "; "
                RunAs       = $_.Principal.UserId
                LastRun     = $info.LastRunTime
                NextRun     = $info.NextRunTime
                LastResult  = $info.LastTaskResult
            }
        } | Export-Csv -Path "$targetPath\SRV_${Hostname}_ScheduledTasks_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează Utilizatorii și Administratorii Locali..."
        Get-LocalUser | Select-Object Name, Enabled, LastLogon, PasswordLastSet, PasswordExpires | 
            Export-Csv -Path "$targetPath\SRV_${Hostname}_LocalUsers_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        Get-LocalGroupMember -Group "Administrators" | Select-Object Name, SID, PrincipalSource | 
            Export-Csv -Path "$targetPath\SRV_${Hostname}_LocalAdmins_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează Regulile de Firewall Active..."
        Get-NetFirewallRule -Enabled True -Direction Inbound | ForEach-Object {
            $portFilter = $_ | Get-NetFirewallPortFilter
            $addressFilter = $_ | Get-NetFirewallAddressFilter
            $applicationFilter = $_ | Get-NetFirewallApplicationFilter
            [PSCustomObject]@{
                Name           = $_.Name
                DisplayName    = $_.DisplayName
                Action         = $_.Action
                Direction      = $_.Direction
                LocalPort      = $portFilter.LocalPort
                RemotePort     = $portFilter.RemotePort
                Protocol       = $portFilter.Protocol
                LocalAddress   = $addressFilter.LocalAddress
                RemoteAddress  = $addressFilter.RemoteAddress
                Program        = $applicationFilter.Program
            }
        } | Export-Csv -Path "$targetPath\SRV_${Hostname}_FirewallRules_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se colectează și verifică driverele active în kernel (Authenticode)..."
        Get-CimInstance Win32_SystemDriver | ForEach-Object {
            $path = $_.PathName
            if ($path -match '(?i)system32\\drivers\\([a-z0-9_-]+\.sys)') {
                $sysPath = "C:\Windows\System32\Drivers\$($Matches[1])"
            } else {
                $sysPath = $path.Replace('"', '').Replace('\\SystemRoot\\', 'C:\Windows\')
            }
            $sig = $null
            if (Test-Path $sysPath) {
                $sig = Get-AuthenticodeSignature -FilePath $sysPath -ErrorAction SilentlyContinue
            }
            [PSCustomObject]@{
                Name        = $_.Name
                DisplayName = $_.DisplayName
                State       = $_.State
                Path        = $sysPath
                IsSigned    = if ($sig) { $sig.Status -eq 'Valid' } else { $false }
                Signer      = if ($sig) { $sig.SignerCertificate.Subject } else { $null }
            }
        } | Export-Csv -Path "$targetPath\SRV_${Hostname}_KernelDrivers_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se culeg detalii despre starea Windows Defender..."
        Get-MpComputerStatus | Select-Object AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, SignatureLastUpdated |
            Export-Csv -Path "$targetPath\SRV_${Hostname}_DefenderStatus_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        Get-MpPreference | Select-Object DisableRealtimeMonitoring, ExclusionPath, ExclusionProcess |
            Export-Csv -Path "$targetPath\SRV_${Hostname}_DefenderExclusions_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        # Deep artifacts for server
        Write-Output "[Collector] Se colectează istoricul comenzilor PowerShell (ConsoleHost_history)..."
        $psHistory = @()
        Get-ChildItem -Path "C:\Users" -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $userPath = $_.FullName
            $historyFile = Join-Path $userPath "AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt"
            if (Test-Path $historyFile) {
                Get-Content $historyFile -ErrorAction SilentlyContinue | ForEach-Object {
                    if (![string]::IsNullOrWhiteSpace($_)) {
                        $psHistory += [PSCustomObject]@{
                            User = (Split-Path $userPath -Leaf)
                            Command = $_
                            File = $historyFile
                        }
                    }
                }
            }
        }
        if ($psHistory.Count -gt 0) {
            $psHistory | Export-Csv -Path "$targetPath\SRV_${Hostname}_PowerShellHistory_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        }
    }

    "NAS" {
        Write-Output "[Collector] Auditare conexiuni rețea către NAS..."
        Get-CimInstance Win32_NetworkConnection | Select-Object LocalName, RemoteName, Status, ConnectionType | 
            Export-Csv -Path "$targetPath\NAS_${Hostname}_NetworkConnections_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8

        Write-Output "[Collector] Se rulează scanarea baseline de permisiuni pe shares..."
        $uncPath = "\\$Hostname\Public"
        if (Test-Path $uncPath) {
            Get-ChildItem -Path $uncPath -Depth 1 | ForEach-Object {
                $acl = Get-Acl $_.FullName
                [PSCustomObject]@{
                    Path = $_.FullName
                    Owner = $acl.Owner
                    AccessRules = $acl.AccessToString
                }
            } | Export-Csv -Path "$targetPath\NAS_${Hostname}_AclAudit_${dateSuffix}.csv" -NoTypeInformation -Encoding utf8
        } else {
            "Calea UNC \\$Hostname\Public nu este accesibilă pentru audit permisiuni." | 
                Out-File "$targetPath\NAS_${Hostname}_AclError_${dateSuffix}.log"
        }
    }

    "DataCenter" {
        Write-Output "[Collector] Pentru DataCenter conectat unilateral se recomandă pornirea receptorului Syslog din aplicație."
        "Syslog Receiver required on Port 514 for unidirectional streaming." | 
            Out-File "$targetPath\DC_${Hostname}_CollectorSetup_${dateSuffix}.log"
    }
}

Write-Output "[Collector] Colectare finalizată cu succes. Datele au fost stocate în $targetPath"
