<#
.SYNOPSIS
  Casa3D doctor for the designer's PC (Windows PowerShell 5.1). Prints one OK / WARN / FAIL line per check.

.DESCRIPTION
  Checks:
    1. the server answers on 127.0.0.1:<Port> (/api/health);
    2. the server process started AFTER the last build (.next\BUILD_ID): otherwise an old server is still
       running after an update and every new static chunk returns 400/404;
    3. a static chunk taken from the /login HTML returns 200 locally and through the tunnel
       (URL read from <RenderDir>\public-url.txt);
    4. the tunnel process (cloudflared) is alive;
    5. the phone-access auth file exists (CASA3D_AUTH_FILE or <AppDir>\.data\auth.json);
    6. free disk space on the app drive (WARN < 5 GB, FAIL < 1 GB).

  -Fix restarts the server the robust way: the owner of the port is found, and its PARENT process tree
  (npm / cmd that started node) is killed with "taskkill /T /F" (Stop-Process on node.exe can fail with
  "Access denied"). Then the server is started again from <AppDir> and the tunnel is restarted with
  <RenderDir>\public-link.ps1 when that file exists. The app is never rebuilt automatically.

  Exit code: 0 when every check is OK or WARN, 1 when at least one check FAILs.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\pc\doctor.ps1
  powershell -ExecutionPolicy Bypass -File tools\pc\doctor.ps1 -Fix
#>
[CmdletBinding()]
param(
  [int]$Port = 3123,
  [string]$AppDir = '',
  [string]$RenderDir = 'D:\casa3d-render',
  [switch]$Fix,
  [int]$TimeoutSec = 10
)

$ErrorActionPreference = 'Stop'
# PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so the app folder is resolved here
if (-not $AppDir) { $AppDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$script:failed = 0
# TLS 1.2 for the tunnel (PowerShell 5.1 defaults to older protocols)
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

function Write-Result([string]$State, [string]$Name, [string]$Detail) {
  $color = 'Green'; if ($State -eq 'WARN') { $color = 'Yellow' } elseif ($State -eq 'FAIL') { $color = 'Red'; $script:failed++ }
  Write-Host ('{0,-4}  {1,-16} {2}' -f $State, $Name, $Detail) -ForegroundColor $color
}

function Get-Http([string]$Url) {
  # returns @{ Status = <int or 0>; Body = <string>; Error = <string> } and never throws
  try {
    $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSec -MaximumRedirection 0 -ErrorAction Stop
    return @{ Status = [int]$r.StatusCode; Body = [string]$r.Content; Error = '' }
  } catch {
    $code = 0
    if ($_.Exception.Response) { try { $code = [int]$_.Exception.Response.StatusCode } catch { $code = 0 } }
    return @{ Status = $code; Body = ''; Error = $_.Exception.Message }
  }
}

function Get-PortOwner([int]$P) {
  try {
    $c = Get-NetTCPConnection -LocalPort $P -State Listen -ErrorAction Stop | Select-Object -First 1
    if ($c) { return [int]$c.OwningProcess }
  } catch { }
  # fallback without the NetTCPIP module
  $line = netstat -ano -p tcp | Select-String -Pattern (':{0}\s+\S+\s+LISTENING\s+(\d+)' -f $P) | Select-Object -First 1
  if ($line) { return [int]$line.Matches[0].Groups[1].Value }
  return $null
}

function Get-ProcInfo([int]$ProcId) {
  # CIM works even when Get-Process cannot read StartTime of another user's / elevated process
  try { return Get-CimInstance Win32_Process -Filter ("ProcessId = {0}" -f $ProcId) -ErrorAction Stop } catch { return $null }
}

function Read-PublicUrl {
  $f = Join-Path $RenderDir 'public-url.txt'
  if (-not (Test-Path -LiteralPath $f)) { return $null }
  $u = (Get-Content -LiteralPath $f -TotalCount 1 -ErrorAction SilentlyContinue)
  if ($u) { $u = $u.Trim().TrimEnd('/') }
  if ($u -match '^https?://') { return $u }
  return $null
}

function Invoke-Checks {
  $script:failed = 0
  $base = 'http://127.0.0.1:{0}' -f $Port
  Write-Host ('Casa3D doctor  app={0}  port={1}  {2}' -f $AppDir, $Port, (Get-Date -Format 's'))

  # 1. server up
  $h = Get-Http "$base/api/health"
  if ($h.Status -eq 200) {
    $persist = ''
    try { $j = $h.Body | ConvertFrom-Json; if ($j.persistent -eq $false) { $persist = ' (in-memory DB: projects are not kept)' } } catch { }
    Write-Result 'OK' 'server' ("$base answers 200$persist")
  } else { Write-Result 'FAIL' 'server' ("$base/api/health -> {0} {1}" -f $h.Status, $h.Error) }

  # 2. server newer than the build
  $buildId = Join-Path $AppDir '.next\BUILD_ID'
  $owner = Get-PortOwner $Port
  if (-not (Test-Path -LiteralPath $buildId)) { Write-Result 'FAIL' 'build' ".next\BUILD_ID missing: run 'npm run build' in $AppDir" }
  elseif (-not $owner) { Write-Result 'FAIL' 'stale-server' "nothing listens on port $Port" }
  else {
    $built = (Get-Item -LiteralPath $buildId).LastWriteTime
    $p = Get-ProcInfo $owner
    if ($p -and $p.CreationDate) {
      if ($p.CreationDate -ge $built) { Write-Result 'OK' 'stale-server' ("pid {0} started {1:s}, build {2:s}" -f $owner, $p.CreationDate, $built) }
      else { Write-Result 'FAIL' 'stale-server' ("pid {0} started {1:s} BEFORE build {2:s}: restart (-Fix)" -f $owner, $p.CreationDate, $built) }
    } else { Write-Result 'WARN' 'stale-server' "cannot read the start time of pid $owner" }
  }

  # 3. a static chunk from the /login HTML, locally and through the tunnel
  $login = Get-Http "$base/login"
  $chunk = $null
  if ($login.Status -eq 200) {
    $m = [regex]::Match($login.Body, '/_next/static/[^"''\s<>]+\.(js|css)')
    if ($m.Success) { $chunk = $m.Value }
  }
  if (-not $chunk) { Write-Result 'FAIL' 'chunk-local' ("no /_next/static chunk in /login (status {0})" -f $login.Status) }
  else {
    $c = Get-Http ($base + $chunk)
    if ($c.Status -eq 200) { Write-Result 'OK' 'chunk-local' "$chunk -> 200" } else { Write-Result 'FAIL' 'chunk-local' ("$chunk -> {0} (old server after an update? use -Fix)" -f $c.Status) }
  }
  $pub = Read-PublicUrl
  if (-not $pub) { Write-Result 'WARN' 'chunk-tunnel' ("no URL in {0}" -f (Join-Path $RenderDir 'public-url.txt')) }
  elseif (-not $chunk) { Write-Result 'FAIL' 'chunk-tunnel' 'skipped: no chunk found locally' }
  else {
    $t = Get-Http ($pub + $chunk)
    if ($t.Status -eq 200) { Write-Result 'OK' 'chunk-tunnel' "$pub$chunk -> 200" } else { Write-Result 'FAIL' 'chunk-tunnel' ("$pub$chunk -> {0} {1}" -f $t.Status, $t.Error) }
  }

  # 4. tunnel process
  $tun = Get-Process -Name 'cloudflared' -ErrorAction SilentlyContinue
  if ($tun) { Write-Result 'OK' 'tunnel' ("cloudflared running (pid {0})" -f (($tun | ForEach-Object { $_.Id }) -join ', ')) }
  else { Write-Result 'FAIL' 'tunnel' 'cloudflared is not running (-Fix restarts it when public-link.ps1 exists)' }

  # 5. auth file
  $auth = $env:CASA3D_AUTH_FILE
  if (-not $auth) { $auth = Join-Path $AppDir '.data\auth.json' }
  if (Test-Path -LiteralPath $auth) {
    try { $a = Get-Content -LiteralPath $auth -Raw | ConvertFrom-Json; if ($a.v -eq 1) { Write-Result 'OK' 'auth' "$auth present" } else { Write-Result 'FAIL' 'auth' "$auth is not a v1 auth file" } }
    catch { Write-Result 'FAIL' 'auth' "$auth is not valid JSON" }
  } else { Write-Result 'FAIL' 'auth' "$auth missing: set the phone password from the app on this PC" }

  # 6. free disk space
  try {
    $root = [IO.Path]::GetPathRoot((Resolve-Path -LiteralPath $AppDir).Path)
    $drive = New-Object IO.DriveInfo($root)
    $gb = [math]::Round($drive.AvailableFreeSpace / 1GB, 1)
    if ($gb -lt 1) { Write-Result 'FAIL' 'disk' "$gb GB free on $root" } elseif ($gb -lt 5) { Write-Result 'WARN' 'disk' "$gb GB free on $root" } else { Write-Result 'OK' 'disk' "$gb GB free on $root" }
  } catch { Write-Result 'WARN' 'disk' ("cannot read free space: {0}" -f $_.Exception.Message) }

  return $script:failed
}

function Stop-PortOwnerTree([int]$P) {
  $procId = Get-PortOwner $P
  if (-not $procId) { Write-Host "no process listens on port $P"; return }
  $node = Get-ProcInfo $procId
  $target = $procId
  if ($node -and $node.ParentProcessId) {
    $parent = Get-ProcInfo ([int]$node.ParentProcessId)
    # the parent is npm / cmd / powershell that started the server; never kill the desktop or a system process
    $protected = @('explorer.exe', 'services.exe', 'svchost.exe', 'wininit.exe', 'winlogon.exe', 'csrss.exe', 'smss.exe', 'System')
    if ($parent -and ($protected -notcontains $parent.Name)) { $target = [int]$parent.ProcessId }
  }
  Write-Host ("taskkill /PID {0} /T /F  (port owner {1})" -f $target, $procId)
  & taskkill.exe /PID $target /T /F | Out-Host
  for ($i = 0; $i -lt 20 -and (Get-PortOwner $P); $i++) { Start-Sleep -Milliseconds 500 }
  if (Get-PortOwner $P) { & taskkill.exe /PID $procId /T /F | Out-Host }
}

function Start-Server {
  Write-Host "starting the server from $AppDir on port $Port"
  # npm start = "next start -H 127.0.0.1"; the window stays minimized so the owner can see its log
  Start-Process -FilePath 'cmd.exe' -ArgumentList ('/c npm start -- -p {0}' -f $Port) -WorkingDirectory $AppDir -WindowStyle Minimized | Out-Null
  for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Seconds 1; if ((Get-Http ('http://127.0.0.1:{0}/api/health' -f $Port)).Status -eq 200) { Write-Host 'server is up'; return } }
  Write-Host 'server did not answer within 60 s' -ForegroundColor Red
}

function Restart-Tunnel {
  $linkScript = Join-Path $RenderDir 'public-link.ps1'
  if (-not (Test-Path -LiteralPath $linkScript)) { Write-Host "no ${linkScript}: tunnel not restarted"; return }
  Get-Process -Name 'cloudflared' -ErrorAction SilentlyContinue | ForEach-Object { & taskkill.exe /PID $_.Id /T /F | Out-Host }
  Write-Host "restarting the tunnel with $linkScript"
  Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $linkScript) -WorkingDirectory $RenderDir -WindowStyle Minimized | Out-Null
  Start-Sleep -Seconds 8
}

$fails = Invoke-Checks
if ($Fix) {
  Write-Host ''
  Write-Host '-Fix: restarting the server and the tunnel'
  Stop-PortOwnerTree $Port
  Start-Server
  Restart-Tunnel
  Write-Host ''
  $fails = Invoke-Checks
}
if ($fails -gt 0) { exit 1 } else { exit 0 }
