<#
.SYNOPSIS
  Start the Telegram vault bot (local Ollama model, principal telegram.bot).

.DESCRIPTION
  Run from the repository root. Checks Ollama, the context size of the configured model and the
  bot's startup gates, then starts long polling. The token is read from $env:VAULT_TELEGRAM_TOKEN
  or from telegram.token in %APPDATA%\ai-memory-vault (never from the repository).

  Recommended: run it under a dedicated read-only account (Install-VaultReaderAccount.ps1;
  see 10_DOCUMENTATION/procedures/Connecting_Every_AI_To_The_Vault.md, section 5).
#>
[CmdletBinding()]
param(
  [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo

$cfg = Get-Content (Join-Path $repo '04_CONFIG\local_llm.json') -Raw | ConvertFrom-Json
Write-Host "[VAULT_ROOT] $repo"
Write-Host "[MODEL] $($cfg.model) num_ctx=$($cfg.num_ctx) host=$($cfg.host)"

try {
  $tags = Invoke-RestMethod -Uri "$($cfg.host)/api/tags" -TimeoutSec 5
} catch {
  throw "[OLLAMA] not reachable at $($cfg.host). Start Ollama first."
}
if (-not ($tags.models | Where-Object { $_.name -eq $cfg.model -or $_.model -eq $cfg.model })) {
  throw "[OLLAMA] model $($cfg.model) is not pulled. Run: ollama pull $($cfg.model)"
}

python -m cognitive_core.telegram_vault_bot --check
if ($LASTEXITCODE -ne 0) { throw "[BOT] startup checks failed" }
if ($CheckOnly) { return }

python -m cognitive_core.telegram_vault_bot
