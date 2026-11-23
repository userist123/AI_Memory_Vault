<#
.SYNOPSIS
  Runs AI App Studio on this PC: backend + preview server + desktop UI, one command, everything stopped on exit.

.DESCRIPTION
  1. generates the owner token (256 bit, OS CSPRNG) in memory only;
  2. starts the backend (python -m apps.backend --token-stdin) and hands it the token on STDIN;
  3. the backend also runs the preview server (127.0.0.1:<PreviewPort>, one origin per project; S9 B3: the same
     process as the API, as in the packaged app - there is no second preview process any more);
  4. starts the UI and hands the SAME token over on stdin as well - never argv, environment, file or URL:
       -Ui tauri   the Tauri debug build (apps/desktop/src-tauri, attach mode AAS_ATTACH_BACKEND=1; it loads the
                   vite dev server on 127.0.0.1:1421, which this script starts too);
       -Ui window  the vite dev server + a Chromium app window (apps/desktop/scripts/studio_window.mjs) with the
                   session injected into the top window only;
       -Ui auto    tauri when the debug exe exists, otherwise window;
  5. waits until the UI is closed (or Ctrl+C), then stops every process it started.

  Everything runs on 127.0.0.1. Nothing is pushed anywhere. Temp/caches follow the TMP/TEMP of the caller.

.PARAMETER RunnerCli
  DEV/TEST: JSON list used instead of the installed `claude` for the built-in runner (e.g. the fake CLI fixture).
.PARAMETER DataDir
  Data root (default %LOCALAPPDATA%\AIAppStudio).
.PARAMETER Dev
  DEVELOPMENT ONLY: allow the vite dev server origin (http://127.0.0.1:1421) to call the API (AAS_DEV_ORIGINS) and start
  a UI from it (-Ui tauri attach / window). Without -Dev the variable is never set and is removed from the children's
  environment even when the caller has it; the UI modes then refuse to start (production = the packaged Tauri app).
.PARAMETER NoUi
  Start only backend + preview and print their ports (used by the end-to-end test); stop with Ctrl+C.
#>
param(
  [ValidateSet('auto', 'tauri', 'window')] [string]$Ui = 'auto',
  [int]$ApiPort = 17875,
  [int]$PreviewPort = 17880,
  [string]$DataDir = '',
  [string]$RunnerCli = '',
  [string]$Prompt = '',
  [string]$PromptFile = '',
  [string]$ProjectName = 'Studio project',
  [switch]$NoUi,
  [switch]$Dev
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Product = Split-Path -Parent $PSScriptRoot
$Desktop = Join-Path $Product 'apps\desktop'
$Python = Join-Path $Product '.venv\Scripts\python.exe'
if (-not (Test-Path $Python)) { throw "no virtualenv at $Python - run 'uv sync --frozen' in $Product first" }
$TauriExe = $env:AAS_TAURI_EXE
if (-not $TauriExe) { $TauriExe = if ($env:CARGO_TARGET_DIR) { Join-Path $env:CARGO_TARGET_DIR 'debug\ai-app-studio.exe' } else { Join-Path $Desktop 'src-tauri\target\debug\ai-app-studio.exe' } }

function Test-PortFree([int]$Port) {
  $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $Port)
  try { $l.Start(); return $true } catch { return $false } finally { try { $l.Stop() } catch {} }
}
foreach ($p in @($ApiPort, $PreviewPort)) {
  if (-not (Test-PortFree $p)) { throw "port $p is already in use; refusing to start (another studio instance?)" }
}

# 1. token: in memory only
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes); $rng.Dispose()
$Token = -join ($bytes | ForEach-Object { $_.ToString('x2') })

$children = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

function Start-Child([string]$File, [string]$Arguments, [string]$Cwd, [hashtable]$Env = @{}, [bool]$StdinToken = $false) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $File
  $psi.Arguments = $Arguments
  $psi.WorkingDirectory = $Cwd
  $psi.UseShellExecute = $false
  $psi.RedirectStandardInput = $true
  $psi.CreateNoWindow = $true
  # production path: an inherited dev-origin allow-list never reaches a studio process
  [void]$psi.EnvironmentVariables.Remove('AAS_DEV_ORIGINS')
  foreach ($k in $Env.Keys) { $psi.EnvironmentVariables[$k] = [string]$Env[$k] }
  $proc = [System.Diagnostics.Process]::Start($psi)
  $children.Add($proc)
  if ($StdinToken) {
    $proc.StandardInput.WriteLine($Token)
    $proc.StandardInput.Flush()
    # the pipe stays open on purpose for the preview/UI helpers that watch stdin; the backend ignores further input
  }
  return $proc
}

function Stop-Tree([System.Diagnostics.Process]$P) {
  if ($P -and -not $P.HasExited) {
    & "$env:SystemRoot\System32\taskkill.exe" /PID $P.Id /T /F 2>&1 | Out-Null
  }
}

$common = @{ AAS_API_PORT = "$ApiPort"; AAS_PREVIEW_PORT = "$PreviewPort"; PYTHONUTF8 = '1' }
if ($Dev) { $common['AAS_DEV_ORIGINS'] = 'http://127.0.0.1:1421' }
if ($DataDir) { $common['AAS_DATA_DIR'] = $DataDir }

try {
  # 2. backend (token on stdin)
  $beArgs = '-X utf8 -m apps.backend --token-stdin'
  if ($RunnerCli) { $beArgs += ' --runner-cli-json "' + ($RunnerCli -replace '"', '\"') + '"' }
  $backend = Start-Child $Python $beArgs $Product $common $true
  # 3. preview server: hosted by the backend itself (S9 B3)
  $env:PYTHONPATH = $null

  # wait for /health
  $deadline = (Get-Date).AddSeconds(60)
  $healthy = $false
  while ((Get-Date) -lt $deadline -and -not $backend.HasExited) {
    try {
      $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 -Uri "http://127.0.0.1:$ApiPort/api/v1/health"
      if ($r.StatusCode -eq 200) { $healthy = $true; break }
    } catch { Start-Sleep -Milliseconds 300 }
  }
  if (-not $healthy) { throw 'the backend did not become healthy within 60 s' }
  Write-Host "backend  http://127.0.0.1:$ApiPort   preview  http://127.0.0.1:$PreviewPort (p-<project>.localhost)"

  if ($NoUi) {
    Write-Host 'NoUi: backend and preview are running; press Ctrl+C to stop'
    while (-not $backend.HasExited) { Start-Sleep -Seconds 1 }
    return
  }

  # 4. UI (served by the vite dev server: development only)
  if (-not $Dev) { throw 'the UI modes of this script load the vite dev server and need -Dev (production uses the packaged app)' }
  $mode = $Ui
  if ($mode -eq 'auto') { $mode = if (Test-Path $TauriExe) { 'tauri' } else { 'window' } }
  $pnpm = (Get-Command pnpm.cmd -ErrorAction Stop).Source
  $devServer = Start-Child "$env:SystemRoot\System32\cmd.exe" ('/c ""' + $pnpm + '" dev"') $Desktop @{ AAS_TEST_API_PORT = "$ApiPort" } $false
  $devUrl = 'http://127.0.0.1:1421'
  $deadline = (Get-Date).AddSeconds(60)
  while ((Get-Date) -lt $deadline) {
    try { if ((Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 -Uri $devUrl).StatusCode -eq 200) { break } } catch { Start-Sleep -Milliseconds 400 }
  }
  if ($mode -eq 'tauri') {
    if (-not (Test-Path $TauriExe)) { throw "Tauri debug exe not found: $TauriExe (cargo build in apps/desktop/src-tauri)" }
    $uiProc = Start-Child $TauriExe '' $Desktop (@{ AAS_ATTACH_BACKEND = '1'; AAS_API_PORT = "$ApiPort" }) $true
  } else {
    $node = (Get-Command node -ErrorAction Stop).Source
    $uiProc = Start-Child $node ("scripts/studio_window.mjs --url $devUrl --api http://127.0.0.1:$ApiPort --preview-port $PreviewPort") $Desktop @{} $true
  }
  Write-Host "UI ($mode) started; close its window to stop the studio"
  if ($PromptFile) { $Prompt = Get-Content -Raw -Encoding UTF8 $PromptFile }
  if ($Prompt) {
    # the UI has no "start run" control yet: create a project and start the built-in runner with this prompt
    Start-Sleep -Seconds 4
    $hdr = @{ Authorization = "Bearer $Token" }
    $proj = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$ApiPort/api/v1/projects" -Headers $hdr -ContentType 'application/json' -Body (@{ name = $ProjectName } | ConvertTo-Json)
    $body = @{ projectId = $proj.id; prompt = $Prompt } | ConvertTo-Json
    $run = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$ApiPort/api/v1/runs" -Headers $hdr -ContentType 'application/json' -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
    Write-Host "run $($run.runId) started on project $($proj.id)"
  }
  while (-not $uiProc.HasExited -and -not $backend.HasExited) { Start-Sleep -Milliseconds 500 }
}
finally {
  for ($i = $children.Count - 1; $i -ge 0; $i--) { Stop-Tree $children[$i] }
  $Token = $null
}
