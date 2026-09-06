#Requires -RunAsAdministrator
<#
.SYNOPSIS
    USB Forensics Reader - citeste din log-urile EXISTENTE ale Windows
    toate datele despre medii de stocare si fisierele accesate/transferate.
    
    IMPORTANT: Fisierele transferate sunt vizibile DOAR daca a fost activat
    in prealabil "Audit Removable Storage" (Event ID 4663).
    Fara audit activ, se pot vedea doar dispozitivele conectate, nu fisierele.
#>

$ErrorActionPreference = "SilentlyContinue"
$reportFile = "$env:USERPROFILE\Desktop\USB-Forensics-Report-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"

# ============================================================
# HELPERS
# ============================================================

function Write-Header {
    param([string]$Title)
    $line = "=" * 70
    $output = "`n$line`n  $Title`n$line"
    Write-Host $output -ForegroundColor Cyan
    Add-Content -Path $reportFile -Value $output
}

function Write-Out {
    param([string]$Text, [string]$Color = "White")
    Write-Host $Text -ForegroundColor $Color
    Add-Content -Path $reportFile -Value $Text
}

function Write-Note {
    param([string]$Text)
    Write-Host "  [!] $Text" -ForegroundColor Magenta
    Add-Content -Path $reportFile -Value "  [!] $Text"
}

# ============================================================
# BANNER
# ============================================================

Clear-Host
$banner = @"
╔══════════════════════════════════════════════════════════════════════╗
║            USB FORENSICS READER - Date din log-urile Windows         ║
║    Citeste DOAR ce exista - nu modifica nimic pe sistem              ║
╚══════════════════════════════════════════════════════════════════════╝
"@
Write-Host $banner -ForegroundColor Cyan
Set-Content -Path $reportFile -Value "USB FORENSICS READER - Raport generat: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`nCalculator: $env:COMPUTERNAME | User: $env:USERNAME`n"

# Verificare admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "EROARE: Trebuie rulat ca Administrator!" -ForegroundColor Red
    exit 1
}

# ============================================================
# SECTIUNEA 1: REGISTRY - USBSTOR (dispozitive de stocare)
# ============================================================

Write-Header "1. DISPOZITIVE USB DE STOCARE (Registry USBSTOR)"

$usbStorPath = "HKLM:\SYSTEM\CurrentControlSet\Enum\USBSTOR"
if (Test-Path $usbStorPath) {
    $deviceTypes = Get-ChildItem -Path $usbStorPath -ErrorAction SilentlyContinue
    $totalDevices = 0

    foreach ($devType in $deviceTypes) {
        # Parsare nume: Disk&Ven_SanDisk&Prod_Ultra&Rev_1.00
        $parts = $devType.PSChildName -split "&"
        $vendor = ($parts | Where-Object {$_ -match "^Ven_"}) -replace "Ven_", ""
        $product = ($parts | Where-Object {$_ -match "^Prod_"}) -replace "Prod_", ""
        $revision = ($parts | Where-Object {$_ -match "^Rev_"}) -replace "Rev_", ""

        $instances = Get-ChildItem -Path $devType.PSPath -ErrorAction SilentlyContinue
        foreach ($instance in $instances) {
            $totalDevices++
            $props = Get-ItemProperty -Path $instance.PSPath -ErrorAction SilentlyContinue
            
            # Serial number este numele instantei (ultimul segment, fara &0 sau &1)
            $serial = $instance.PSChildName -replace "&\d+$", ""
            
            # Timestamp ultima conectare din subcheia Properties
            $lastConnected = "N/A"
            $firstInstall = "N/A"
            
            $propsPath83 = Join-Path $instance.PSPath "Properties\{83da6326-97a6-4088-9453-a1923f573b29}"
            # 0064 = prima instalare, 0066 = ultima conectare, 0067 = ultima deconectare
            @("0064","0065","0066","0067") | ForEach-Object {
                $tPath = Join-Path $propsPath83 "00$_"
                # Incercam direct
            }

            $line = "`n  Dispozitiv #$totalDevices"
            $line += "`n    Producator : $vendor"
            $line += "`n    Model      : $product"
            $line += "`n    Revisie    : $revision"
            $line += "`n    Serial Nr  : $serial"
            $line += "`n    DeviceDesc : $($props.DeviceDesc)"
            $line += "`n    FriendlyName: $($props.FriendlyName)"
            $line += "`n    ClassGUID  : $($props.ClassGUID)"
            $line += "`n    Status     : $($props.ConfigFlags)"

            Write-Out $line "Yellow"
        }
    }
    Write-Out "`n  Total dispozitive gasite: $totalDevices" "Green"
} else {
    Write-Out "  USBSTOR gol sau inexistent." "Gray"
}

# ============================================================
# SECTIUNEA 2: LITERE DRIVE ASIGNATE (MountPoints2 + MountedDevices)
# ============================================================

Write-Header "2. LITERE DRIVE ASIGNATE DISPOZITIVELOR USB"

$mountedPath = "HKLM:\SYSTEM\MountedDevices"
if (Test-Path $mountedPath) {
    $mounted = Get-ItemProperty -Path $mountedPath -ErrorAction SilentlyContinue
    $mounted.PSObject.Properties | Where-Object {$_.Name -match "^\\DosDevices\\"} | ForEach-Object {
        $driveLetter = $_.Name -replace "\\DosDevices\\", ""
        Write-Out "  Drive $driveLetter -> [date binare mapate in registry]" "Yellow"
    }
}

$mountPoints2 = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2"
if (Test-Path $mountPoints2) {
    Write-Out "`n  MountPoints2 (drive-uri deschise de userul curent):" "Cyan"
    Get-ChildItem -Path $mountPoints2 -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.PSChildName -match "^#") {
            $displayPath = $_.PSChildName -replace "#", "\" -replace "##", "\\"
            Write-Out "    $displayPath" "Yellow"
        }
    }
}

# ============================================================
# SECTIUNEA 3: EVENT LOG - CONECTARE / DECONECTARE USB
# ============================================================

Write-Header "3. EVENTURI CONECTARE / DECONECTARE USB (Event Viewer)"

Write-Note "Sursa: System Log | Provider: Microsoft-Windows-Kernel-PnP"
Write-Note "Event 2003=instalat, 2006=configurat, 2102=pornit, 2100=oprit, 3100=pornit, 3102=scos"

$pnpEvents = $null
try {
    $filterXml = @"
<QueryList>
  <Query Id="0" Path="System">
    <Select Path="System">
      *[System[Provider[@Name='Microsoft-Windows-Kernel-PnP'] 
        and (EventID=2003 or EventID=2006 or EventID=2100 or EventID=2101 or EventID=2102 
             or EventID=3003 or EventID=3100 or EventID=3102)]]
    </Select>
  </Query>
</QueryList>
"@
    $pnpEvents = Get-WinEvent -FilterXml $filterXml -ErrorAction Stop | 
        Where-Object {$_.Message -match "USBSTOR|Disk|USB\\VID|removable|storage" -or $_.Properties[0].Value -match "USBSTOR|USB\\VID"}
} catch {}

if ($pnpEvents -and $pnpEvents.Count -gt 0) {
    $grouped = $pnpEvents | Sort-Object TimeCreated -Descending | Select-Object -First 100
    Write-Out "  Ultimele $($grouped.Count) evenimente USB (max 100):" "Cyan"
    $grouped | ForEach-Object {
        $evLine = "  [$($_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))] EventID=$($_.Id) | $($_.Message.Split("`n")[0])"
        Write-Out $evLine "Yellow"
    }
} else {
    Write-Out "  Nu s-au gasit evenimente PnP USB in System log." "Gray"
    Write-Note "Posibil: log-ul a fost sters sau limita de dimensiune depasita."
}

# ============================================================
# SECTIUNEA 4: SETUPAPI.DEV.LOG - Prima instalare drivere
# ============================================================

Write-Header "4. SETUPAPI.DEV.LOG - Prima instalare drivere USB"

$setupApiLog = "$env:SystemRoot\INF\setupapi.dev.log"
if (Test-Path $setupApiLog) {
    $sizeKB = [math]::Round((Get-Item $setupApiLog).Length / 1KB, 1)
    Write-Out "  Fisier: $setupApiLog ($sizeKB KB)" "Gray"
    
    $logContent = Get-Content $setupApiLog -ErrorAction SilentlyContinue
    if ($logContent) {
        # Cautam sectiunile legate de USBSTOR
        $usbSections = @()
        $inSection = $false
        $currentSection = @()
        
        for ($i = 0; $i -lt $logContent.Count; $i++) {
            $line = $logContent[$i]
            if ($line -match "USBSTOR|USB\\VID_|USB\\MS_COMP") {
                $inSection = $true
                # Include cateva linii de context inainte
                $start = [Math]::Max(0, $i - 3)
                $currentSection = $logContent[$start..$i]
            } elseif ($inSection) {
                $currentSection += $line
                if ($currentSection.Count -ge 15 -or $line -match "^\s*$") {
                    $usbSections += $currentSection
                    $usbSections += "---"
                    $inSection = $false
                    $currentSection = @()
                }
            }
        }
        
        if ($usbSections.Count -gt 0) {
            Write-Out "  Sectiuni legate de USB din setupapi.dev.log:" "Cyan"
            $usbSections | Select-Object -First 80 | ForEach-Object {
                Write-Out "  $_" "Yellow"
            }
        } else {
            Write-Out "  Nu s-au gasit referinte USB in setupapi.dev.log." "Gray"
        }
    }
} else {
    Write-Out "  setupapi.dev.log nu exista." "Gray"
}

# ============================================================
# SECTIUNEA 5: FISIERE TRANSFERATE (Event ID 4663)
# ============================================================

Write-Header "5. FISIERE ACCESATE / TRANSFERATE PE USB (Event ID 4663)"

Write-Note "Aceasta sectiune contine date DOAR daca a fost activat 'Audit Removable Storage'."
Write-Note "Implicit pe Windows, acest audit NU este activ. Daca nu gasesti date, vezi sectiunea 6."

$fileEvents = $null
try {
    $filterFile = @"
<QueryList>
  <Query Id="0" Path="Security">
    <Select Path="Security">
      *[System[EventID=4663] 
        and EventData[Data[@Name='ObjectType']='File']
        and EventData[Data[@Name='ObjectName'][contains(., 'Harddisk')]]]
    </Select>
  </Query>
</QueryList>
"@
    $fileEvents = Get-WinEvent -FilterXml $filterFile -ErrorAction Stop | Select-Object -First 200
} catch {}

if ($fileEvents -and $fileEvents.Count -gt 0) {
    Write-Out "  Fisiere accesate pe dispozitive removable (4663) - primele 200:" "Green"
    Write-Out "  $($fileEvents.Count) evenimente gasite!" "Green"
    
    $fileEvents | ForEach-Object {
        $xml = [xml]$_.ToXml()
        $data = $xml.Event.EventData.Data
        $user = ($data | Where-Object {$_.Name -eq "SubjectUserName"}).'#text'
        $domain = ($data | Where-Object {$_.Name -eq "SubjectDomainName"}).'#text'
        $object = ($data | Where-Object {$_.Name -eq "ObjectName"}).'#text'
        $access = ($data | Where-Object {$_.Name -eq "AccessMask"}).'#text'
        $process = ($data | Where-Object {$_.Name -eq "ProcessName"}).'#text'
        
        # Decode access mask
        $accessStr = switch ($access) {
            "0x2" { "WRITE" }
            "0x1" { "READ" }
            "0x40000" { "DELETE" }
            "0x10000" { "READ_CONTROL" }
            default { "ACCESS($access)" }
        }
        
        $evLine = "  [$($_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))] $domain\$user | $accessStr | $object | via: $(Split-Path $process -Leaf)"
        Write-Out $evLine "Yellow"
    }
} else {
    Write-Out "  Nu s-au gasit evenimente 4663 in Security log." "Gray"
    Write-Note "Audit Removable Storage probabil nu este activat pe acest PC."
    Write-Note "Vezi Sectiunea 6 pentru cum se activeaza pentru monitorizare viitoare."
}

# Cauta si 4656 (access denied)
$deniedEvents = $null
try {
    $filterDenied = "<QueryList><Query Id='0' Path='Security'><Select Path='Security'>*[System[EventID=4656] and EventData[Data[@Name='ObjectType']='File']]</Select></Query></QueryList>"
    $deniedEvents = Get-WinEvent -FilterXml $filterDenied -ErrorAction Stop | Select-Object -First 20
} catch {}

if ($deniedEvents -and $deniedEvents.Count -gt 0) {
    Write-Out "`n  Accese blocate pe USB (4656) - ultimele 20:" "Cyan"
    $deniedEvents | ForEach-Object {
        Write-Out "  [$($_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))] $($_.Message.Split("`n")[0])" "Yellow"
    }
}

# ============================================================
# SECTIUNEA 6: FISIERE RECENTE DE PE USB (LNK files / Recent)
# ============================================================

Write-Header "6. FISIERE RECENT DESCHISE DE PE USB (LNK - Shortcut Files)"

Write-Note "Windows creeaza automat .lnk in Recent pentru fiecare fisier deschis."
Write-Note "Inclusiv fisiere deschise direct de pe stick USB. NU necesita audit activat."

$recentPath = "$env:APPDATA\Microsoft\Windows\Recent"
if (Test-Path $recentPath) {
    $lnkFiles = Get-ChildItem -Path $recentPath -Filter "*.lnk" -ErrorAction SilentlyContinue
    
    $usbLnkFiles = @()
    
    foreach ($lnk in $lnkFiles) {
        try {
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut($lnk.FullName)
            $targetPath = $shortcut.TargetPath
            
            # Cauta fisiere care au fost pe drive-uri removable (nu C:, nu D: fix)
            # LNK-urile catre USB au de obicei path-uri cu litere de drive non-sistem
            # sau contin referinte la drive-uri care nu mai exista (target absent)
            if ($targetPath -and $targetPath -ne "") {
                $driveLetter = [System.IO.Path]::GetPathRoot($targetPath)
                $targetExists = Test-Path $targetPath
                
                # Daca targetul nu exista si nu e pe C:\ - probabil era USB
                if (-not $targetExists -and $driveLetter -ne "C:\") {
                    $usbLnkFiles += [PSCustomObject]@{
                        LastAccessed = $lnk.LastWriteTime
                        OriginalPath = $targetPath
                        Drive        = $driveLetter
                        LnkName      = $lnk.Name
                        TargetExists = $targetExists
                        Note         = "Drive absent - probabil USB deja scos"
                    }
                } elseif ($targetPath -match "^[E-Z]:\\") {
                    # Drive-uri cu litere mari (E-Z) - potential USB/external
                    $usbLnkFiles += [PSCustomObject]@{
                        LastAccessed = $lnk.LastWriteTime
                        OriginalPath = $targetPath
                        Drive        = $driveLetter
                        LnkName      = $lnk.Name
                        TargetExists = $targetExists
                        Note         = if ($targetExists) {"Drive prezent"} else {"Drive absent"}
                    }
                }
            }
        } catch {}
    }
    
    if ($usbLnkFiles.Count -gt 0) {
        Write-Out "  Fisiere deschise de pe drive-uri externe/USB ($($usbLnkFiles.Count) gasite):" "Green"
        $usbLnkFiles | Sort-Object LastAccessed -Descending | ForEach-Object {
            $fLine = "  [$($_.LastAccessed.ToString('yyyy-MM-dd HH:mm:ss'))] $($_.OriginalPath) [$($_.Note)]"
            Write-Out $fLine "Yellow"
        }
    } else {
        Write-Out "  Nu s-au gasit LNK-uri catre drive-uri externe." "Gray"
        Write-Out "  Total LNK-uri analizate: $($lnkFiles.Count)" "Gray"
    }
}

# ============================================================
# SECTIUNEA 7: SHELLBAGS - Foldere naviagate pe USB
# ============================================================

Write-Header "7. SHELLBAGS - Foldere deschise de pe USB"

Write-Note "ShellBags stocheaza foldere vizualizate in Explorer, inclusiv de pe USB."

$shellbagPaths = @(
    "HKCU:\Software\Microsoft\Windows\Shell\BagMRU",
    "HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU"
)

$shellbagCount = 0
foreach ($sbPath in $shellbagPaths) {
    if (Test-Path $sbPath) {
        # Cautam intrari care contin referinte la drive-uri
        $allKeys = Get-ChildItem -Path $sbPath -Recurse -ErrorAction SilentlyContinue
        $shellbagCount += $allKeys.Count
    }
}

Write-Out "  Total intrari ShellBag gasite: $shellbagCount" $(if ($shellbagCount -gt 0) {"Yellow"} else {"Gray"})
Write-Note "Decodarea completa a ShellBags necesita tool specializat (ShellBagsExplorer/SBECmd)."
Write-Note "Informatia bruta este stocata in format binar in registry."

# ============================================================
# SECTIUNEA 8: USER ASSIST - Aplicatii rulate de pe USB
# ============================================================

Write-Header "8. USERASSIST - Aplicatii rulate de pe USB"

$userAssistPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist"
if (Test-Path $userAssistPath) {
    $guids = Get-ChildItem -Path $userAssistPath -ErrorAction SilentlyContinue
    foreach ($guid in $guids) {
        $countPath = Join-Path $guid.PSPath "Count"
        if (Test-Path $countPath) {
            $entries = Get-ItemProperty -Path $countPath -ErrorAction SilentlyContinue
            $entries.PSObject.Properties | Where-Object {
                $_.Name -notmatch "^PS" -and $_.Value -ne $null
            } | ForEach-Object {
                # Decode ROT13 (UserAssist foloseste ROT13 encoding)
                $decoded = $_.Name -creplace '[A-Za-z]', {
                    $c = [int][char]$_.Value
                    if ($c -ge 65 -and $c -le 90) { [char](($c - 65 + 13) % 26 + 65) }
                    elseif ($c -ge 97 -and $c -le 122) { [char](($c - 97 + 13) % 26 + 97) }
                    else { $_.Value }
                }
                # Filtreaza doar aplicatii de pe drive-uri non-sistem
                if ($decoded -match "^[E-Z]:\\" -or ($decoded -match "\\" -and $decoded -notmatch "^C:\\" -and $decoded -notmatch "^::{")) {
                    Write-Out "  [UserAssist] $decoded" "Yellow"
                }
            }
        }
    }
}

# ============================================================
# SECTIUNEA 9: STAREA AUDITULUI (e activ sau nu?)
# ============================================================

Write-Header "9. STATUS AUDIT REMOVABLE STORAGE"

$auditCheck = & auditpol /get /subcategory:"Removable Storage" 2>&1
if ($auditCheck -match "Success and Failure|Success|Failure") {
    $status = ($auditCheck | Where-Object {$_ -match "Removable Storage"})
    Write-Out "  Audit Removable Storage: ACTIV" "Green"
    Write-Out "  $status" "Green"
    Write-Note "Evenimentele 4663 (fisiere accesate) sunt inregistrate."
} else {
    Write-Out "  Audit Removable Storage: INACTIV" "Red"
    Write-Note "Fisierele transferate prin USB NU sunt inregistrate in Event Log."
    Write-Note "Pentru a activa (necesita restart policy):"
    Write-Out "" "White"
    Write-Out "    auditpol /set /subcategory:`"Removable Storage`" /success:enable /failure:enable" "Cyan"
    Write-Out "" "White"
    Write-Note "Dupa activare, EVENT ID 4663 va loga fiecare fisier accesat pe USB."
}

# ============================================================
# SUMAR
# ============================================================

Write-Header "SUMAR RAPORT"
Write-Out "  Raport complet salvat la: $reportFile" "Green"
Write-Out "  Data generare: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" "Gray"
Write-Out "  Calculator: $env:COMPUTERNAME | User: $env:USERNAME" "Gray"
Write-Out "" "White"
Write-Note "Datele afisate provin EXCLUSIV din log-urile existente pe acest PC."
Write-Note "Daca log-urile au fost sterse anterior, informatia nu mai poate fi recuperata."

Write-Host ""
Write-Host "Apasa orice tasta pentru a inchide..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
