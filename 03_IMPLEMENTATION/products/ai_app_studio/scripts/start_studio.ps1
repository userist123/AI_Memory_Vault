<#
.SYNOPSIS
  The owner's one entry point: checks the prerequisites in a few seconds, then starts AI App Studio in its own
  window (run_studio.ps1 -Dev -Ui tauri). Used by the desktop shortcut "AI App Studio" that scripts/setup.ps1 creates.

.DESCRIPTION
  - first run (no virtualenv or no node_modules): runs scripts/setup.ps1 -NoShortcut once;
  - Docker Desktop stopped: starts it when installed (builds need it), otherwise warns and continues;
  - Claude CLI missing or logged out: warns (the studio opens, but the AI cannot build until you log in);
  - then hands over to run_studio.ps1 (token in memory/stdin only, everything on 127.0.0.1, closing the window stops
    every process it started). -Dev here only lets the loopback vite origin (http://127.0.0.1:1421) reach the API.
#>
param([ValidateSet('tauri', 'window')] [string]$Ui = 'tauri')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$Product = Split-Path -Parent $PSScriptRoot
Write-Host 'AI App Studio pornește...' -ForegroundColor Cyan

if (-not (Test-Path (Join-Path $Product '.venv\Scripts\python.exe')) -or -not (Test-Path (Join-Path $Product 'apps\desktop\node_modules'))) {
  Write-Host 'prima pornire: instalez dependențele (scripts\setup.ps1)...'
  & (Join-Path $PSScriptRoot 'setup.ps1') -NoShortcut
}

$ErrorActionPreference = 'Continue'
$docker = Get-Command docker -ErrorAction SilentlyContinue
if ($docker) {
  & $docker.Source info --format '{{.ServerVersion}}' *> $null
  if ($LASTEXITCODE -ne 0) {
    $dd = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (Test-Path $dd) {
      Write-Host 'pornesc Docker Desktop (necesar pentru construirea aplicațiilor)...'
      Start-Process -FilePath $dd | Out-Null
    } else {
      Write-Host 'ATENȚIE: Docker nu rulează; studioul se deschide, dar construirea aplicațiilor va eșua până pornești Docker Desktop.' -ForegroundColor Yellow
    }
  }
} else {
  Write-Host 'ATENȚIE: Docker Desktop nu este instalat; aplicațiile nu se pot construi (vezi docs\GHID_UTILIZARE.md).' -ForegroundColor Yellow
}
$claude = Get-Command claude -ErrorAction SilentlyContinue
if (-not $claude) {
  Write-Host 'ATENȚIE: Claude CLI nu este instalat; AI-ul nu poate construi nimic (vezi docs\GHID_UTILIZARE.md).' -ForegroundColor Yellow
} else {
  $st = & $claude.Source auth status 2>$null | Out-String
  if ($LASTEXITCODE -ne 0 -or $st -notmatch '"loggedIn"\s*:\s*true') {
    Write-Host 'ATENȚIE: Claude CLI nu este autentificat. Deschide PowerShell, scrie claude și fă login, apoi repornește studioul.' -ForegroundColor Yellow
  }
}
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'run_studio.ps1') -Dev -Ui $Ui
