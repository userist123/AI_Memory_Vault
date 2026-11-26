# profile_example.ps1 - a PER-APP test profile. Copy to profile_<app>.ps1 and adapt.
# It holds everything app-specific (how to launch, how to locate each function, the ordered test
# steps with an EXPECTED RESULT per step). The generic engine stays in lib_ui.ps1.
# Usage:  . .\lib_ui.ps1 ; . .\profile_myapp.ps1 ; Invoke-AppTests

$App = @{
  Name      = 'MyApp'
  Exe       = 'C:\Path\To\MyApp.exe'
  MainTitle = 'Main Window'          # exact title or wildcard
  # Data/hardware a test might touch, so the engine can back up / refuse destructive actions:
  DataFiles = @('C:\Path\To\app.mdb')
  # Prefer UIA AutomationId, then Name. Coordinates ONLY if a control exposes neither (owner-drawn).
  Locators  = @{
    Open     = @{ AutomationId=''; Name='Open';     Coord=$null }
    Settings = @{ AutomationId=''; Name='Settings'; Coord=$null }
    # Coord is a LAST resort for owner-drawn MFC controls; store as @{X=;Y=} local to the window.
  }
}

# One test case = objective + precondition + ACTION + EXPECTED (the oracle, written BEFORE acting) + evidence.
# Status is decided by the oracle check, not by "a window opened". Returns PASS/FAIL/BLOCKED/SKIPPED.
$Tests = @(
  @{ Id='T01'; Func='Open';     Pre='Main window focused';         Expected='A file-open dialog titled "Open" appears'; Oracle={ param($pid) (Wait-WindowTitle $pid 'Open*' 5000) -ne $null } }
  @{ Id='T02'; Func='Settings'; Pre='Main window focused';         Expected='Settings dialog appears with a Save button'; Oracle={ param($pid) $w=Wait-WindowTitle $pid 'Settings*' 5000; $w -and (Find-Uia $w.H 'Save') } }
)

function Invoke-AppTests([string]$OutDir="$PSScriptRoot\out"){
  New-Item -ItemType Directory -Force $OutDir | Out-Null
  # data safety: back up every data file before the run
  foreach($f in $App.DataFiles){ $b=Backup-File $f; if($b){ "backup: $($b.Backup)" } }
  $proc = Start-Process $App.Exe -PassThru
  Wait-WindowTitle $proc.Id $App.MainTitle 15000 | Out-Null
  $results=@()
  foreach($t in $Tests){
    $main = Find-Window $proc.Id $App.MainTitle
    if(-not $main){ $results += [pscustomobject]@{Id=$t.Id;Status='BLOCKED';Note='main window gone'}; continue }
    Set-Front $main.H
    $loc=$App.Locators[$t.Func]
    $act = if($loc.AutomationId -or $loc.Name){ Invoke-Uia $main.H $proc.Id $loc.Name $loc.AutomationId }
           elseif($loc.Coord){ $r=New-Object Win32Ui+RECT;[void][Win32Ui]::GetWindowRect($main.H,[ref]$r); Click-PointSafe ($r.Left+$loc.Coord.X) ($r.Top+$loc.Coord.Y) $main.H $proc.Id }
           else { 'NO_LOCATOR' }
    if(-not (Test-ProcessAlive $proc.Id)){ $results += [pscustomobject]@{Id=$t.Id;Status='FAIL';Note='process crashed after action'}; break }
    $pass = & $t.Oracle $proc.Id
    $shot = "$OutDir\$($t.Id)_$($t.Func).png"
    $top = Get-AppWindows $proc.Id | Where-Object { $_.Title -ne $App.MainTitle -and $_.Title -ne '' } | Select-Object -First 1
    if($top){ Save-WindowShot $top.H $shot | Out-Null } else { Save-WindowShot $main.H $shot | Out-Null }
    $status = if($act -like 'DISABLED*'){ 'BLOCKED' } elseif($pass){ 'PASS' } else { 'FAIL' }
    $results += [pscustomobject]@{Id=$t.Id;Func=$t.Func;Status=$status;Act=$act;Expected=$t.Expected;Shot=$shot}
    # close any opened child dialog before next step (keyboard first)
    if($top){ Set-Front $top.H; [System.Windows.Forms.SendKeys]::SendWait('{ESC}') ; Start-Sleep -Milliseconds 400 }
  }
  Restore-Desktop
  $results | Format-Table Id,Func,Status,Act -Auto | Out-String
  $results
}
