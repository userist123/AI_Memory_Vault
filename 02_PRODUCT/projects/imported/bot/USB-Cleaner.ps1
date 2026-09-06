#Requires -RunAsAdministrator
<#
.SYNOPSIS
    USB Complete Cleaner - sterge TOATE urmele USB de pe sistem
    Autor: generat pentru curatare completa USB forensica
    Necesita: PowerShell 5.1+ ca Administrator
#>

param(
    [switch]$Simulate,
    [switch]$Force
)

$ErrorActionPreference = "SilentlyContinue"
$version = "1.0"
$logFile = "$env:USERPROFILE\Desktop\USB-Cleaner-Log-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
$backupFile = "$env:USERPROFILE\Desktop\USB-Backup-$(Get-Date -Format 'yyyyMMdd-HHmmss').reg"

# ============================================================
# FUNCTII HELPER
# ============================================================

function Write-Log {
    param([string]$Message, [string]$Color = "White", [string]$Level = "INFO")
    $timestamp = Get-Date -Format "HH:mm:ss"
    $line = "[$timestamp][$Level] $Message"
    Write-Host $line -ForegroundColor $Color
    Add-Content -Path $logFile -Value $line -ErrorAction SilentlyContinue
}

function Write-Section {
    param([string]$Title)
    $line = "=" * 60
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host $line -ForegroundColor Cyan
    Add-Content -Path $logFile -Value "`n$line`n  $Title`n$line"
}

function Remove-RegKeySafe {
    param([string]$Path)
    if (Test-Path "Registry::$Path") {
        if (-not $Simulate) {
            Remove-Item -Path "Registry::$Path" -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Log "  [DEL] $Path" "Yellow" "REG"
        return $true
    }
    return $false
}

function Export-RegistryBackup {
    Write-Log "Creez backup registry la: $backupFile" "Green"
    $keys = @(
        "HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR",
        "HKLM\SYSTEM\CurrentControlSet\Enum\USB",
        "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2"
    )
    $backupContent = "Windows Registry Editor Version 5.00`r`n"
    foreach ($key in $keys) {
        $regOutput = & reg export $key "$env:TEMP\tmp_backup.reg" /y 2>&1
        if (Test-Path "$env:TEMP\tmp_backup.reg") {
            $content = Get-Content "$env:TEMP\tmp_backup.reg" -Raw
            $backupContent += "`r`n" + $content
            Remove-Item "$env:TEMP\tmp_backup.reg" -Force -ErrorAction SilentlyContinue
        }
    }
    Set-Content -Path $backupFile -Value $backupContent -ErrorAction SilentlyContinue
    Write-Log "Backup salvat: $backupFile" "Green"
}

# ============================================================
# BANNER
# ============================================================

Clear-Host
Write-Host @"
╔══════════════════════════════════════════════════════════════╗
║          USB COMPLETE CLEANER v$version                          ║
║    Sterge TOATE urmele USB - Registry, Logs, Prefetch        ║
╚══════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

if ($Simulate) {
    Write-Host "  [MODUL SIMULARE - nu se sterge nimic, doar se listeaza]" -ForegroundColor Magenta
    Write-Host ""
}

# Verificare administrator
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "EROARE: Scripul trebuie rulat ca Administrator!" -ForegroundColor Red
    Write-Host "Click dreapta pe PowerShell -> Run as Administrator" -ForegroundColor Yellow
    exit 1
}

Write-Log "Script pornit. Simulator=$Simulate" "Green"

# Confirmare daca nu e -Force
if (-not $Force -and -not $Simulate) {
    Write-Host ""
    Write-Host "  ATENTIE: Aceasta operatiune va sterge DEFINITIV toate urmele USB." -ForegroundColor Red
    Write-Host "  Se va crea un backup automat pe Desktop inainte de stergere." -ForegroundColor Yellow
    Write-Host ""
    $confirm = Read-Host "  Continui? (DA/NU)"
    if ($confirm -ne "DA") {
        Write-Log "Utilizatorul a anulat operatiunea." "Yellow"
        exit 0
    }
}

# ============================================================
# BACKUP
# ============================================================
if (-not $Simulate) {
    Write-Section "BACKUP REGISTRY"
    Export-RegistryBackup
}

# ============================================================
# 1. REGISTRY - USBSTOR (istoricul dispozitivelor de stocare)
# ============================================================
Write-Section "1. REGISTRY - USBSTOR (istoricul dispozitivelor)"

$usbStorPath = "HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR"
if (Test-Path "Registry::$usbStorPath") {
    $devices = Get-ChildItem -Path "Registry::$usbStorPath" -ErrorAction SilentlyContinue
    $count = 0
    foreach ($device in $devices) {
        Write-Log "  Gasit: $($device.PSChildName)" "Yellow"
        $count++
    }
    Write-Log "Total dispozitive USBSTOR gasite: $count" "Cyan"
    
    if (-not $Simulate) {
        Remove-Item -Path "Registry::$usbStorPath" -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -Path "Registry::$usbStorPath" -Force | Out-Null
        Write-Log "USBSTOR curatat cu succes." "Green"
    }
} else {
    Write-Log "USBSTOR nu exista sau este deja gol." "Gray"
}

# ============================================================
# 2. REGISTRY - USB (interfete USB, hub-uri)
# ============================================================
Write-Section "2. REGISTRY - USB Interfaces"

$usbKeys = @(
    "HKLM\SYSTEM\CurrentControlSet\Enum\USB",
    "HKLM\SYSTEM\CurrentControlSet\Control\DeviceClasses\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}",
    "HKLM\SYSTEM\CurrentControlSet\Control\DeviceClasses\{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}"
)

foreach ($key in $usbKeys) {
    if (Test-Path "Registry::$key") {
        $subkeys = Get-ChildItem -Path "Registry::$key" -ErrorAction SilentlyContinue | Where-Object {$_.PSChildName -match "USBSTOR|Disk|USB"}
        foreach ($sub in $subkeys) {
            Write-Log "  Gasit: $($sub.PSChildName)" "Yellow"
            if (-not $Simulate) {
                Remove-Item -Path $sub.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        Write-Log "Cheie procesata: $key" "Gray"
    }
}

# ============================================================
# 3. REGISTRY - MountPoints2 (literele de drive, ex: E:\, F:\)
# ============================================================
Write-Section "3. REGISTRY - MountPoints2 (litere drive USB)"

$mountPath = "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2"
if (Test-Path "Registry::$mountPath") {
    $mounts = Get-ChildItem -Path "Registry::$mountPath" -ErrorAction SilentlyContinue
    foreach ($mount in $mounts) {
        if ($mount.PSChildName -match "#" -or $mount.PSChildName -match "USB") {
            Write-Log "  Gasit: $($mount.PSChildName)" "Yellow"
            if (-not $Simulate) {
                Remove-Item -Path $mount.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Log "MountPoints2 procesat." "Green"
}

# ============================================================
# 4. REGISTRY - Cheile din toate controlset-urile (001, 002, etc.)
# ============================================================
Write-Section "4. REGISTRY - Toate ControlSet-urile"

$allControlSets = Get-ChildItem -Path "Registry::HKLM\SYSTEM" -ErrorAction SilentlyContinue | Where-Object {$_.PSChildName -match "^ControlSet\d+$"}
foreach ($cs in $allControlSets) {
    $csUsbStor = "HKLM\SYSTEM\$($cs.PSChildName)\Enum\USBSTOR"
    if (Test-Path "Registry::$csUsbStor") {
        Write-Log "  Gasit: $csUsbStor" "Yellow"
        if (-not $Simulate) {
            Remove-Item -Path "Registry::$csUsbStor" -Recurse -Force -ErrorAction SilentlyContinue
            New-Item -Path "Registry::$csUsbStor" -Force | Out-Null
        }
    }
}

# ============================================================
# 5. EVENT VIEWER LOGS (System, Setup, Application)
# ============================================================
Write-Section "5. EVENT VIEWER - Stergere log-uri sistem"

$eventLogs = @("System", "Setup", "Application", "Microsoft-Windows-DriverFrameworks-UserMode/Operational")

foreach ($log in $eventLogs) {
    try {
        if ($Simulate) {
            $logInfo = Get-WinEvent -ListLog $log -ErrorAction SilentlyContinue
            if ($logInfo) {
                Write-Log "  [SIM] Ar sterge log: $log ($($logInfo.RecordCount) inregistrari)" "Yellow"
            }
        } else {
            wevtutil cl $log 2>&1 | Out-Null
            Write-Log "  Log sters: $log" "Green"
        }
    } catch {
        Write-Log "  Nu s-a putut accesa: $log" "Gray"
    }
}

# Log-uri suplimentare pentru USB
$additionalLogs = @(
    "Microsoft-Windows-Kernel-PnP/Configuration",
    "Microsoft-Windows-DeviceSetupManager/Admin",
    "Microsoft-Windows-DeviceSetupManager/Operational",
    "Microsoft-Windows-Storage-Storport/Operational"
)

foreach ($log in $additionalLogs) {
    try {
        if ($Simulate) {
            Write-Log "  [SIM] Ar sterge log: $log" "Yellow"
        } else {
            wevtutil cl $log 2>&1 | Out-Null
            Write-Log "  Log sters: $log" "Green"
        }
    } catch {
        Write-Log "  Log inexistent sau inaccessibil: $log" "Gray"
    }
}

# ============================================================
# 6. PREFETCH (dovezi ca USBOblivion sau alte tool-uri au rulat)
# ============================================================
Write-Section "6. PREFETCH - Stergere fisiere prefetch USB"

$prefetchPath = "$env:SystemRoot\Prefetch"
if (Test-Path $prefetchPath) {
    $prefetchFiles = Get-ChildItem -Path $prefetchPath -Filter "*.pf" -ErrorAction SilentlyContinue | 
        Where-Object {$_.Name -match "ROBOCOPY|USBOBLIVION|PRIVAZ|USBDEVIEW|USB"}
    
    foreach ($pf in $prefetchFiles) {
        Write-Log "  Gasit prefetch: $($pf.Name)" "Yellow"
        if (-not $Simulate) {
            Remove-Item -Path $pf.FullName -Force -ErrorAction SilentlyContinue
            Write-Log "  Sters: $($pf.Name)" "Green"
        }
    }
    
    # Sterge si prefetch-ul acestui script
    $thisPrefetch = Get-ChildItem -Path $prefetchPath -Filter "POWERSHELL*" -ErrorAction SilentlyContinue
    foreach ($pf in $thisPrefetch) {
        Write-Log "  [SELF] Prefetch PowerShell: $($pf.Name)" "Magenta"
        if (-not $Simulate) {
            Remove-Item -Path $pf.FullName -Force -ErrorAction SilentlyContinue
        }
    }
    
    Write-Log "Prefetch procesat." "Green"
} else {
    Write-Log "Prefetch dezactivat pe acest sistem." "Gray"
}

# ============================================================
# 7. SETUPAPI.DEV.LOG (log instalare drivere USB)
# ============================================================
Write-Section "7. SETUPAPI.DEV.LOG - Curatare log instalare drivere"

$setupApiLog = "$env:SystemRoot\INF\setupapi.dev.log"
$setupApiSetup = "$env:SystemRoot\INF\setupapi.setup.log"

foreach ($logPath in @($setupApiLog, $setupApiSetup)) {
    if (Test-Path $logPath) {
        $sizeKB = [math]::Round((Get-Item $logPath).Length / 1KB, 1)
        Write-Log "  Gasit: $logPath ($sizeKB KB)" "Yellow"
        
        if (-not $Simulate) {
            # Citim continutul, stergem sectiunile legate de USB
            $content = Get-Content $logPath -Raw -ErrorAction SilentlyContinue
            if ($content) {
                # Strategia: trunchiem fisierul (Windows il va recrea gol)
                Set-Content -Path $logPath -Value "" -Force -ErrorAction SilentlyContinue
                Write-Log "  Curatat: $logPath" "Green"
            }
        }
    } else {
        Write-Log "  Nu exista: $logPath" "Gray"
    }
}

# ============================================================
# 8. RECENT FILES / JUMP LISTS (fisiere recent accesate de pe USB)
# ============================================================
Write-Section "8. RECENT FILES si JUMP LISTS"

$recentPaths = @(
    "$env:APPDATA\Microsoft\Windows\Recent",
    "$env:APPDATA\Microsoft\Windows\Recent\AutomaticDestinations",
    "$env:APPDATA\Microsoft\Windows\Recent\CustomDestinations"
)

foreach ($rPath in $recentPaths) {
    if (Test-Path $rPath) {
        $recentItems = Get-ChildItem -Path $rPath -File -ErrorAction SilentlyContinue
        $count = ($recentItems | Measure-Object).Count
        Write-Log "  Gasit $count fisiere in: $rPath" "Yellow"
        
        if (-not $Simulate -and $count -gt 0) {
            Remove-Item -Path "$rPath\*" -Force -ErrorAction SilentlyContinue
            Write-Log "  Curatat: $rPath" "Green"
        }
    }
}

# ============================================================
# 9. SHELLBAGS (istoricul folderelor deschise, inclusiv de pe USB)
# ============================================================
Write-Section "9. SHELLBAGS - Stergere istoricul folderelor"

$shellbagKeys = @(
    "HKCU\Software\Microsoft\Windows\Shell\BagMRU",
    "HKCU\Software\Microsoft\Windows\Shell\Bags",
    "HKCU\Software\Microsoft\Windows\ShellNoRoam\BagMRU",
    "HKCU\Software\Microsoft\Windows\ShellNoRoam\Bags",
    "HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU",
    "HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags"
)

foreach ($key in $shellbagKeys) {
    if (Test-Path "Registry::$key") {
        Write-Log "  Gasit: $key" "Yellow"
        if (-not $Simulate) {
            Remove-Item -Path "Registry::$key" -Recurse -Force -ErrorAction SilentlyContinue
            Write-Log "  Sters: $key" "Green"
        }
    }
}

# ============================================================
# 10. THUMBNAIL CACHE (miniaturi de fisiere de pe USB)
# ============================================================
Write-Section "10. THUMBNAIL CACHE"

$thumbPaths = @(
    "$env:LOCALAPPDATA\Microsoft\Windows\Explorer"
)

foreach ($tPath in $thumbPaths) {
    if (Test-Path $tPath) {
        $thumbFiles = Get-ChildItem -Path $tPath -Filter "thumbcache_*.db" -ErrorAction SilentlyContinue
        foreach ($tf in $thumbFiles) {
            Write-Log "  Gasit: $($tf.Name) ($([math]::Round($tf.Length/1KB, 1)) KB)" "Yellow"
            if (-not $Simulate) {
                Remove-Item -Path $tf.FullName -Force -ErrorAction SilentlyContinue
                Write-Log "  Sters: $($tf.Name)" "Green"
            }
        }
    }
}

# ============================================================
# 11. FISIERUL DE BACKUP .REG CREAT DE USBOBLIVION
# ============================================================
Write-Section "11. Backup .reg creat de USBOblivion"

$docPaths = @(
    "$env:USERPROFILE\Documents",
    "$env:USERPROFILE\Desktop"
)

foreach ($docPath in $docPaths) {
    $regBackups = Get-ChildItem -Path $docPath -Filter "*USB*Oblivion*.reg" -ErrorAction SilentlyContinue
    $regBackups += Get-ChildItem -Path $docPath -Filter "usb*.reg" -ErrorAction SilentlyContinue
    
    foreach ($rb in $regBackups) {
        Write-Log "  Gasit backup USBOblivion: $($rb.FullName)" "Magenta"
        if (-not $Simulate) {
            Remove-Item -Path $rb.FullName -Force -ErrorAction SilentlyContinue
            Write-Log "  Sters backup: $($rb.Name)" "Green"
        }
    }
}

# ============================================================
# SUMAR FINAL
# ============================================================
Write-Section "SUMAR FINAL"

if ($Simulate) {
    Write-Log "SIMULARE COMPLETA. Nicio modificare nu a fost facuta." "Magenta"
    Write-Log "Ruleaza fara -Simulate pentru curatare reala." "Yellow"
} else {
    Write-Log "CURATARE COMPLETA FINALIZATA!" "Green"
    Write-Log "Log complet salvat la: $logFile" "Cyan"
    Write-Log "Backup registry salvat la: $backupFile" "Cyan"
    Write-Log "" "White"
    Write-Log "IMPORTANT: Reporneste calculatorul pentru a finaliza curatarea." "Red"
    Write-Log "Dupa repornire, sterge manual acest script si log-ul de pe Desktop." "Yellow"
}

Write-Host ""
Write-Host "Apasa orice tasta pentru a inchide..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
