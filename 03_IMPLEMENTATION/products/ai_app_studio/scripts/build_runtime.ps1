<#
.SYNOPSIS
  Builds the self-contained runtime that the AI App Studio installer ships:
  apps/desktop/src-tauri/runtime/runtime.zip + runtime.version (S9b, ported from AI Website Studio build_runtime).

.DESCRIPTION
  The installed (release) Tauri shell must run without this repository. The zip contains:
    python/          relocatable CPython 3.12 (uv-managed build, the base of .venv) with every locked runtime
                     dependency installed into its Lib/site-packages (no venv: venvs carry absolute paths)
    ms-playwright/   the Chromium headless shell revision the locked Playwright expects (preview_capture)
    app/             core/ (studio_core, aas_build, aas_device, aas_runner), apps/backend, apps/mcp-app,
                     apps/device_host, kit/ (studio-owned app kit, no node_modules), images/ (Dockerfiles, build
                     scripts, android template, DIGESTS.json), pyproject.toml, uv.lock,
                     VENDORED.json, RUNTIME.json (what is inside: versions, git commit, kit lock hash, image digests)
  Not inside: Docker images (built on the owner PC from app/images, digests in DIGESTS.json), the Android SDK /
  emulator and the device host files (one-time owner setup from the repository checkout, PM3a: only the launcher
  names that tooling), the Claude Code CLI (the owner's install), Node.js (builds run in Docker).
  On first launch the shell extracts it once into %LOCALAPPDATA%\<identifier>\runtime\<version> (System32 tar.exe).

  Installer: after this script, in apps/desktop:
    pnpm install --frozen-lockfile
    pnpm tauri build --config src-tauri/tauri.release.conf.json
  The NSIS installer is NOT code-signed (docs/SECURITY.md residual "Unsigned installer").
#>
param(
  [string]$Out = '',
  [string]$Python = '',
  [string]$Browsers = '',
  [switch]$KeepStage
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Out) { $Out = Join-Path $Root 'apps\desktop\src-tauri\runtime' }
$Stage = Join-Path $Root 'apps\desktop\src-tauri\runtime-stage'

function Step($name, [scriptblock]$body) {
  Write-Host "== $name"
  $global:LASTEXITCODE = 0
  & $body
  if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "$name failed ($LASTEXITCODE)" }
}

if (-not $Python) {
  $cfg = Join-Path $Root '.venv\pyvenv.cfg'
  if (-not (Test-Path $cfg)) { throw 'no .venv (run uv sync first) and no -Python given' }
  $home_ = (Get-Content $cfg | Where-Object { $_ -match '^home\s*=' } | Select-Object -First 1) -replace '^home\s*=\s*', ''
  $Python = $home_.Trim()
}
if (-not (Test-Path (Join-Path $Python 'python.exe'))) { throw "no python.exe under $Python" }
& (Join-Path $Python 'python.exe') -I -c "import sys; assert sys.version_info[:2] == (3, 12), sys.version" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'the runtime needs CPython 3.12' }

if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force $Stage, $Out | Out-Null

Step 'python (relocatable build)' {
  Copy-Item $Python (Join-Path $Stage 'python') -Recurse
  Get-ChildItem (Join-Path $Stage 'python') -Recurse -Directory -Filter __pycache__ | Remove-Item -Recurse -Force
}

Step 'dependencies (locked, no dev group)' {
  $req = Join-Path $Stage 'requirements.txt'
  Push-Location $Root
  try { uv export --frozen --no-dev --no-hashes --no-emit-project --no-header -o $req | Out-Null } finally { Pop-Location }
  if ($LASTEXITCODE -ne 0) { throw 'uv export failed' }
  uv pip install --python (Join-Path $Stage 'python\python.exe') --break-system-packages --link-mode copy --no-progress -r $req
  if ($LASTEXITCODE -ne 0) { throw 'uv pip install failed' }
  Remove-Item $req
  # console-script launchers embed the build machine's absolute path; the app never uses them
  Get-ChildItem (Join-Path $Stage 'python\Scripts') -Filter *.exe -ErrorAction SilentlyContinue | Remove-Item -Force
}

Step 'chromium headless shell (revision of the locked Playwright)' {
  $bj = Join-Path $Stage 'python\Lib\site-packages\playwright\driver\package\browsers.json'
  $rev = ((Get-Content $bj -Raw | ConvertFrom-Json).browsers | Where-Object { $_.name -eq 'chromium-headless-shell' }).revision
  if (-not $rev) { throw 'cannot read the chromium-headless-shell revision from Playwright browsers.json' }
  $cands = @($Browsers, $env:PLAYWRIGHT_BROWSERS_PATH, (Join-Path $env:LOCALAPPDATA 'ms-playwright')) | Where-Object { $_ }
  $src = $cands | ForEach-Object { Join-Path $_ "chromium_headless_shell-$rev" } | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $src) { throw "chromium_headless_shell-$rev not found (playwright install chromium-headless-shell, or -Browsers)" }
  Copy-Item $src (Join-Path $Stage "ms-playwright\chromium_headless_shell-$rev") -Recurse
}

Step 'application sources' {
  $app = Join-Path $Stage 'app'
  $dirs = 'core\studio_core', 'core\aas_build', 'core\aas_device', 'core\aas_runner',
          'apps\backend', 'apps\mcp-app', 'apps\device_host', 'kit', 'images'
  foreach ($d in $dirs) {
    $dst = Join-Path $app $d
    New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
    Copy-Item (Join-Path $Root $d) $dst -Recurse
  }
  Get-ChildItem $app -Recurse -Directory -Force -Include __pycache__, .pytest_cache, .mypy_cache, node_modules |
    Remove-Item -Recurse -Force
  Copy-Item (Join-Path $Root 'pyproject.toml'), (Join-Path $Root 'uv.lock'), (Join-Path $Root 'VENDORED.json') $app
  # no link may enter the runtime (the shell extracts it with tar)
  $links = Get-ChildItem $app -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }
  if ($links) { throw "links in the staged app: $($links.FullName -join ', ')" }
}

Step 'metadata (RUNTIME.json)' {
  $commit = ''
  try { $commit = (git -C $Root rev-parse HEAD 2>$null).Trim() } catch { $commit = '' }
  $global:LASTEXITCODE = 0
  $meta = [ordered]@{
    schema         = 'aas.runtime/1'
    built          = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    gitCommit      = $commit
    python         = (& (Join-Path $Stage 'python\python.exe') -I -c "import sys; print(sys.version.split()[0])").Trim()
    playwrightBrowser = (Get-ChildItem (Join-Path $Stage 'ms-playwright') -Directory | Select-Object -First 1).Name
    kitLockSha256  = (Get-FileHash (Join-Path $Root 'kit\pnpm-lock.yaml') -Algorithm SHA256).Hash.ToLower()
    imageDigests   = (Get-Content (Join-Path $Root 'images\DIGESTS.json') -Raw | ConvertFrom-Json)
    notIncluded    = @('docker images (build from app/images)', 'Android SDK, emulator and device host (owner setup from the repository)', 'Claude Code CLI')
  }
  $meta | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Stage 'app\RUNTIME.json') -Encoding utf8
}

Step 'smoke test of the staged runtime (isolated from this checkout)' {
  $smoke = Join-Path $Stage 'smoke.py'
  @(
    'import sys',
    'from pathlib import Path',
    'stage = Path(sys.argv[1]).resolve()',
    'sys.path[:0] = [str(stage / "app" / "core"), str(stage / "app")]',
    'import studio_core, aas_build, aas_device, aas_runner, fastapi, uvicorn, mcp, playwright, PIL, sqlalchemy, alembic, anthropic',
    'from aas_build import export, sight, service',
    'from apps.device_host import launcher, protocol',
    'for m in (studio_core, aas_build, fastapi, playwright, mcp, PIL):',
    '    p = Path(m.__file__).resolve()',
    '    assert stage in p.parents, f"{m.__name__} loaded from {p}, outside the runtime"',
    'assert service.KIT == stage / "app" / "kit", service.KIT',
    'assert (stage / "app" / "apps" / "backend" / "__main__.py").is_file()',
    'print("runtime imports ok from", stage)'
  ) | Set-Content $smoke -Encoding utf8
  & (Join-Path $Stage 'python\python.exe') -I $smoke $Stage
  if ($LASTEXITCODE -ne 0) { throw 'smoke test failed' }
  Remove-Item $smoke
}

Step 'zip + version' {
  $zip = Join-Path $Out 'runtime.zip'
  if (Test-Path $zip) { Remove-Item $zip -Force }
  & "$env:SystemRoot\System32\tar.exe" -a -c -f $zip -C $Stage python ms-playwright app
  if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
  $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
  Set-Content (Join-Path $Out 'runtime.version') $hash.Substring(0, 16) -NoNewline -Encoding ascii
  '{0} MB, version {1}, sha256 {2}' -f [math]::Round((Get-Item $zip).Length / 1MB), $hash.Substring(0, 16), $hash
}
if (-not $KeepStage) { Remove-Item $Stage -Recurse -Force }
