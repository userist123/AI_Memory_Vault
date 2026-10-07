<#
.SYNOPSIS
  One-shot local verification of the vault on the owner's Windows machine.

.DESCRIPTION
  Run from anywhere inside the repository. Each step prints PASS / FAIL / SKIP and the summary
  table comes last. Nothing is installed, pulled or written into the repository, except the
  build output (bin/obj) of the optional -Build step.

    1. Python, git branch and working tree
    2. vault access tests (or the whole suite with -Full)
    3. route registry check, direct route resolve + verbatim read
    4. Ollama: reachable, model pulled, one extractive read and one question through the
       anti-hallucination pipeline (no Telegram involved)
    5. Telegram startup gates (allowlist, token present) - the bot is NOT started
    6. MCP registration in Claude Code, Codex CLI and Gemini CLI, when those CLIs are installed
    7. -Build: dotnet build of LogAnalyzer and XAU_Kinetic.Desktop

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File 30_SCRIPTS\run\Verify-VaultOnWindows.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File 30_SCRIPTS\run\Verify-VaultOnWindows.ps1 -Full -Build
#>
[CmdletBinding()]
param(
  [switch]$Full,
  [switch]$Build,
  [string]$Question = 'Unde este implementarea reala a controllerului de memorie, conform VAULT_STATE?'
)
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo
$env:PYTHONIOENCODING = 'utf-8'
$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Step, [string]$Status, [string]$Detail) {
  $results.Add([pscustomobject]@{ Step = $Step; Status = $Status; Detail = $Detail })
  $color = @{ PASS = 'Green'; FAIL = 'Red'; SKIP = 'Yellow' }[$Status]
  Write-Host ("[{0}] {1} - {2}" -f $Status, $Step, $Detail) -ForegroundColor $color
}

function Invoke-Step([string]$Step, [scriptblock]$Command, [int[]]$OkCodes = @(0)) {
  $out = & $Command 2>&1 | ForEach-Object { "$_" }
  $code = $LASTEXITCODE
  $tail = ($out | Select-Object -Last 1)
  if ($OkCodes -contains $code) { Add-Result $Step 'PASS' $tail } else { Add-Result $Step 'FAIL' "exit $code; $tail" }
  return ,$out
}

# 1. Python and git ------------------------------------------------------------------------
$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) { Add-Result 'python' 'FAIL' 'python not on PATH'; $results | Format-Table -AutoSize; exit 1 }
$pyver = (& python -c "import sys;print('%d.%d' % sys.version_info[:2])")
if ([version]$pyver -ge [version]'3.11') { Add-Result 'python' 'PASS' $pyver } else { Add-Result 'python' 'FAIL' "$pyver (need 3.11+)" }
$branch = (& git rev-parse --abbrev-ref HEAD 2>$null)
$dirty = (& git status --porcelain 2>$null | Measure-Object).Count
Add-Result 'git' $(if ($dirty -eq 0) { 'PASS' } else { 'SKIP' }) "branch $branch, $dirty uncommitted change(s)"
$missing = & python -c "import importlib.util as u;print(' '.join(m for m in ('mcp','yaml','pytest') if not u.find_spec(m)))"
if ($missing) { Add-Result 'python packages' 'FAIL' "missing: $missing (pip install -r requirements.txt)" }
else { Add-Result 'python packages' 'PASS' 'mcp, yaml, pytest' }

# 2. tests ----------------------------------------------------------------------------------
if ($Full) {
  Invoke-Step 'full test suite' { python -m pytest 20_TESTS -q -p no:cacheprovider } | Out-Null
} else {
  Invoke-Step 'vault access tests' {
    python -m pytest -q -p no:cacheprovider 20_TESTS/test_vault_access_core.py 20_TESTS/test_vault_access_ollama_telegram.py 20_TESTS/test_vault_access_clients.py 20_TESTS/test_vault_access_perf.py 20_TESTS/test_memory_mcp_server.py
  } | Out-Null
}

# 3. routes ---------------------------------------------------------------------------------
Invoke-Step 'route registry' { python 30_SCRIPTS/routing/build_route_manifest.py --check --summary } | Out-Null
$resolved = (& python -m cognitive_core.vault_cli resolve VAULT_STATE 2>&1) -join "`n"
if ($resolved -match 'vault://governance/vault_state') { Add-Result 'resolve VAULT_STATE' 'PASS' 'vault://governance/vault_state' }
else { Add-Result 'resolve VAULT_STATE' 'FAIL' ($resolved -split "`n" | Select-Object -Last 1) }
$read = (& python -m cognitive_core.vault_cli read vault://governance/vault_state --text 2>&1) -join "`n"
if ($read -match '# VAULT STATE') { Add-Result 'read VAULT_STATE' 'PASS' 'verbatim text with sha256' }
else { Add-Result 'read VAULT_STATE' 'FAIL' ($read -split "`n" | Select-Object -Last 1) }

# 4. Ollama ---------------------------------------------------------------------------------
$cfg = Get-Content (Join-Path $repo '04_CONFIG\local_llm.json') -Raw | ConvertFrom-Json
$tags = $null
try { $tags = Invoke-RestMethod -Uri "$($cfg.host)/api/tags" -TimeoutSec 5 } catch { }
if (-not $tags) {
  Add-Result 'ollama' 'SKIP' "not reachable at $($cfg.host) (start Ollama, then re-run)"
} elseif (-not ($tags.models | Where-Object { $_.name -eq $cfg.model -or $_.model -eq $cfg.model })) {
  Add-Result 'ollama' 'FAIL' "model $($cfg.model) not pulled: ollama pull $($cfg.model)"
} else {
  Add-Result 'ollama' 'PASS' "$($cfg.model), num_ctx $($cfg.num_ctx)"
  Write-Host "`n--- extractive read (the model must NOT be called) ---" -ForegroundColor Cyan
  $out = Invoke-Step 'ollama: read VAULT_STATE' { python -m cognitive_core.telegram_vault_bot --ask 'citeste VAULT_STATE.md' }
  if ($out -match '\[OLLAMA\]') { Add-Result 'ollama: read is extractive' 'FAIL' 'the model was called for a read' }
  else { Add-Result 'ollama: read is extractive' 'PASS' 'no model call' }
  Write-Host "`n--- question (verified quotes or real fragments, never free text) ---" -ForegroundColor Cyan
  $out = & python -m cognitive_core.telegram_vault_bot --ask $Question 2>&1 | ForEach-Object { "$_" }
  $out | Select-Object -First 40 | ForEach-Object { Write-Host $_ }
  $mode = ($out | Where-Object { $_ -match '^\[REPLY\]' } | Select-Object -First 1)
  if ($LASTEXITCODE -eq 0) { Add-Result 'ollama: question' 'PASS' $mode }
  elseif ($mode -match 'mode=fallback') { Add-Result 'ollama: question' 'SKIP' "$mode (verification refused the model; real fragments shown)" }
  else { Add-Result 'ollama: question' 'FAIL' $mode }
}

# 5. Telegram gates (the bot is not started) --------------------------------------------------
$check = & python -m cognitive_core.telegram_vault_bot --check 2>&1 | ForEach-Object { "$_" }
$allow = ($check | Where-Object { $_ -match '^\[ALLOWLIST\]' }) -join ''
$tokenFile = Join-Path $env:APPDATA 'ai-memory-vault\telegram.token'
$hasToken = [bool]$env:VAULT_TELEGRAM_TOKEN -or (Test-Path $tokenFile)
if ($LASTEXITCODE -eq 0 -and $hasToken) { Add-Result 'telegram gates' 'PASS' "$allow; token present" }
else { Add-Result 'telegram gates' 'SKIP' "$allow; token present: $hasToken (see Connecting_Every_AI_To_The_Vault.md section 5)" }

# 6. MCP clients ----------------------------------------------------------------------------
foreach ($cli in @(@{ Name = 'claude'; Args = @('mcp', 'list') }, @{ Name = 'codex'; Args = @('mcp', 'list') }, @{ Name = 'gemini'; Args = @('mcp', 'list') })) {
  if (-not (Get-Command $cli.Name -ErrorAction SilentlyContinue)) { Add-Result "mcp: $($cli.Name)" 'SKIP' 'CLI not installed'; continue }
  $listing = (& $cli.Name @($cli.Args) 2>&1 | ForEach-Object { "$_" }) -join ' '
  if ($listing -match 'vault-memory') { Add-Result "mcp: $($cli.Name)" 'PASS' 'vault-memory registered' }
  else { Add-Result "mcp: $($cli.Name)" 'FAIL' 'vault-memory not listed (project trusted? started from the repo root?)' }
}

# 7. .NET builds ----------------------------------------------------------------------------
if ($Build) {
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Add-Result 'dotnet' 'SKIP' 'dotnet SDK not installed' }
  else {
    Invoke-Step 'build LogAnalyzer' { dotnet build 02_PRODUCT/projects/workspaces/loganalyzer-dfir/LogAnalyzer.slnx -c Release -nologo -v q } | Out-Null
    Invoke-Step 'build XAU_Kinetic.Desktop' { dotnet build 03_IMPLEMENTATION/products/xau_kinetic/desktop/XAU_Kinetic.Desktop.csproj -c Release -nologo -v q } | Out-Null
  }
}

Write-Host ''
$results | Format-Table -AutoSize | Out-String -Width 240 | Write-Host
$failed = @($results | Where-Object { $_.Status -eq 'FAIL' }).Count
if ($failed) { Write-Host "$failed step(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'No failures.' -ForegroundColor Green
exit 0
