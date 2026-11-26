<#
.SYNOPSIS
  One-time setup of AI App Studio on this PC (repo mode): installs the dependencies, checks every prerequisite and
  creates the desktop shortcut "AI App Studio". Safe to run again at any time (each step is idempotent).

.DESCRIPTION
  Steps (messages in Romanian, for the owner):
    1. tools on PATH: uv, node, pnpm, cargo (optional: without it the Chromium window is used), docker, claude (the Android emulator
       host is set up separately by the owner, see docs/GHID_UTILIZARE.md; PM3a: studio code never touches it);
    2. uv sync --frozen (Python backend);
    3. pnpm install --frozen-lockfile (desktop UI);
    4. cargo build in apps/desktop/src-tauri (dev build of the studio window: it loads the vite dev server);
    5. Docker Desktop running (started when it is installed but stopped) and the build images present
       (aas-web-build:dev, aas-android-build:dev; built from images/ when missing);
    6. Claude CLI installed and logged in (`claude auth status`);
    7. desktop shortcut "AI App Studio.lnk" -> scripts/start_studio.ps1.
  Nothing is downloaded outside the package managers and docker build; no setting of the studio is changed.

.PARAMETER NoShortcut
  Skip step 7.
.PARAMETER SkipImages
  Do not build missing Docker images (the android image takes several minutes and ~2 GB).
#>
param([switch]$NoShortcut, [switch]$SkipImages)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$Product = Split-Path -Parent $PSScriptRoot
$Desktop = Join-Path $Product 'apps\desktop'
$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Step, [string]$State, [string]$Hint = '') {
  $results.Add([pscustomobject]@{ Pas = $Step; Stare = $State; Ce_faci = $Hint })
  $color = switch ($State) { 'OK' { 'Green' } 'ATENȚIE' { 'Yellow' } default { 'Red' } }
  Write-Host ("[{0}] {1} {2}" -f $State, $Step, $(if ($Hint) { "- $Hint" } else { '' })) -ForegroundColor $color
}
function Find-Tool([string]$Name) { $c = Get-Command $Name -ErrorAction SilentlyContinue; if ($c) { $c.Source } else { $null } }
function Invoke-Step([string]$Exe, [string[]]$ArgList, [string]$Cwd) {
  $p = Start-Process -FilePath $Exe -ArgumentList $ArgList -WorkingDirectory $Cwd -NoNewWindow -Wait -PassThru
  return $p.ExitCode
}

Write-Host 'AI App Studio - pregătire (o singură dată)' -ForegroundColor Cyan

# 1. tools
$uv = Find-Tool 'uv'; $node = Find-Tool 'node'; $pnpm = Find-Tool 'pnpm.cmd'; $cargo = Find-Tool 'cargo'
$docker = Find-Tool 'docker'; $claude = Find-Tool 'claude'
if (-not $pnpm -and $node) {
  $corepack = Find-Tool 'corepack.cmd'
  if ($corepack -and (Invoke-Step $corepack @('enable', 'pnpm') $Desktop) -eq 0) { $pnpm = Find-Tool 'pnpm.cmd' }
}
if (-not $uv) { Add-Result 'uv (Python)' 'LIPSĂ' 'instalează: winget install --id astral-sh.uv -e, apoi redeschide PowerShell' }
if (-not $node) { Add-Result 'Node.js' 'LIPSĂ' 'instalează: winget install --id OpenJS.NodeJS.LTS -e' }
if (-not $pnpm) { Add-Result 'pnpm' 'LIPSĂ' 'instalează: npm install -g pnpm@10' }
if (-not $cargo) { Add-Result 'cargo (Rust)' 'ATENȚIE' 'fără Rust studioul se deschide într-o fereastră Chromium; pentru fereastra proprie: winget install --id Rustlang.Rustup -e' }
if (-not $docker) { Add-Result 'Docker Desktop' 'LIPSĂ' 'instalează Docker Desktop (winget install --id Docker.DockerDesktop -e); fără el nu se pot construi aplicații' }
if (-not $claude) { Add-Result 'Claude CLI' 'LIPSĂ' 'instalează Claude Code (claude.ai/install), apoi rulează claude o dată ca să te autentifici' }

# 2. Python backend
if ($uv) {
  if ((Invoke-Step $uv @('sync', '--frozen') $Product) -eq 0) { Add-Result 'dependențe Python (uv sync)' 'OK' }
  else { Add-Result 'dependențe Python (uv sync)' 'EROARE' 'vezi mesajele de mai sus; rulează din nou setup.ps1' }
}
# 3. desktop UI
if ($pnpm) {
  # non-interactive: never stop on pnpm's "remove node_modules?" prompt; keep the store node_modules was made with
  $pnpmArgs = @('install', '--frozen-lockfile', '--config.confirmModulesPurge=false')
  $modYaml = Join-Path $Desktop 'node_modules\.modules.yaml'
  if (Test-Path $modYaml) {
    $m = Select-String -Path $modYaml -Pattern '^storeDir:\s*(.+)$' | Select-Object -First 1
    if ($m) {
      $store = $m.Matches[0].Groups[1].Value.Trim().Trim('"', "'")
      if ((Split-Path -Leaf $store) -match '^v\d+$') { $store = Split-Path -Parent $store }
      if (Test-Path $store) { $pnpmArgs += @('--store-dir', "`"$store`"") }
    }
  }
  if ((Invoke-Step $pnpm $pnpmArgs $Desktop) -eq 0) { Add-Result 'dependențe interfață (pnpm install)' 'OK' }
  else { Add-Result 'dependențe interfață (pnpm install)' 'EROARE' 'vezi mesajele de mai sus' }
}
# 4. studio window (dev build: loads the vite dev server, never an embedded old UI)
if ($cargo) {
  Write-Host 'construiesc fereastra studioului (cargo build; prima dată durează câteva minute)...'
  if ((Invoke-Step $cargo @('build') (Join-Path $Desktop 'src-tauri')) -eq 0) { Add-Result 'fereastra studioului (cargo build)' 'OK' }
  else { Add-Result 'fereastra studioului (cargo build)' 'ATENȚIE' 'se va folosi fereastra Chromium' }
}

# 5. Docker running + images
if ($docker) {
  $running = { $ErrorActionPreference = 'Continue'; & $docker info --format '{{.ServerVersion}}' *> $null; return ($LASTEXITCODE -eq 0) }
  if (-not (& $running)) {
    $dd = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (Test-Path $dd) {
      Write-Host 'pornesc Docker Desktop (până la 3 minute)...'
      Start-Process -FilePath $dd | Out-Null
      $end = (Get-Date).AddMinutes(3)
      while ((Get-Date) -lt $end -and -not (& $running)) { Start-Sleep -Seconds 5 }
    }
  }
  if (& $running) {
    Add-Result 'Docker pornit' 'OK'
    $images = @(
      @{ Tag = 'aas-web-build:dev'; Args = @('build', '-f', 'images/web-build/Dockerfile', '--build-context', 'scripts=images/web-build', '-t', 'aas-web-build:dev', 'kit') },
      @{ Tag = 'aas-android-build:dev'; Args = @('build', '-f', 'images/android-build/Dockerfile', '--build-context', 'aas=images/android-build', '-t', 'aas-android-build:dev', 'kit') }
    )
    foreach ($img in $images) {
      $ErrorActionPreference = 'Continue'; & $docker image inspect $img.Tag *> $null; $have = ($LASTEXITCODE -eq 0)
      $ErrorActionPreference = 'Stop'
      if ($have) { Add-Result "imagine $($img.Tag)" 'OK'; continue }
      if ($SkipImages) { Add-Result "imagine $($img.Tag)" 'LIPSĂ' 'rulează setup.ps1 fără -SkipImages'; continue }
      Write-Host "construiesc imaginea $($img.Tag) (câteva minute)..."
      if ((Invoke-Step $docker $img.Args $Product) -eq 0) { Add-Result "imagine $($img.Tag)" 'OK' }
      else { Add-Result "imagine $($img.Tag)" 'EROARE' 'construirea imaginii a eșuat; vezi mesajele de mai sus' }
    }
  } else {
    Add-Result 'Docker pornit' 'LIPSĂ' 'pornește Docker Desktop și așteaptă „Engine running”, apoi rulează din nou setup.ps1'
  }
}

# 6. Claude CLI login
if ($claude) {
  $ErrorActionPreference = 'Continue'; $status = & $claude auth status 2>$null | Out-String; $rc = $LASTEXITCODE
  $ErrorActionPreference = 'Stop'
  if ($rc -eq 0 -and $status -match '"loggedIn"\s*:\s*true') { Add-Result 'Claude CLI autentificat' 'OK' }
  else { Add-Result 'Claude CLI autentificat' 'LIPSĂ' 'deschide PowerShell, scrie claude și urmează pașii de login (/login)' }
}

# 7. desktop shortcut
if (-not $NoShortcut) {
  $start = Join-Path $PSScriptRoot 'start_studio.ps1'
  $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'AI App Studio.lnk'
  $ws = New-Object -ComObject WScript.Shell
  $sc = $ws.CreateShortcut($lnk)
  $sc.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
  $sc.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$start`""
  $sc.WorkingDirectory = $Product
  $sc.WindowStyle = 7  # minimized console; the studio window opens on its own
  $ico = Join-Path $Desktop 'src-tauri\icons\icon.ico'
  if (Test-Path $ico) { $sc.IconLocation = $ico }
  $sc.Description = 'Pornește AI App Studio'
  $sc.Save()
  Add-Result 'scurtătură Desktop „AI App Studio”' 'OK'
}

Write-Host ''
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$bad = @($results | Where-Object { $_.Stare -in @('LIPSĂ', 'EROARE') })
if ($bad.Count) { Write-Host 'Rezolvă rândurile LIPSĂ/EROARE de mai sus, apoi rulează din nou setup.ps1.' -ForegroundColor Yellow; exit 1 }
Write-Host 'Gata. Pornește studioul cu dublu-clic pe „AI App Studio” de pe Desktop.' -ForegroundColor Green
