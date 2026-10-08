<#
.SYNOPSIS
  OS-level read-only enforcement for the vault bot: a dedicated local account with read/execute
  on the vault and NO access to the per-user secret directories.

.DESCRIPTION
  Run once, elevated. Prints every icacls command before running it; -WhatIf prints only.
  The Telegram bot and the Ollama assistant should run under this account (Task Scheduler,
  "Run whether user is logged on or not"). MCP servers started by Claude Code / Codex /
  Antigravity are child processes of those clients and run as you: for them the protection is
  the access policy, the denylist and keeping secrets outside the repository.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [string]$Account = 'svc_vaultreader',
  [string]$VaultRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
  [string]$PrivateRoot = $env:AI_MEMORY_VAULT_PRIVATE_ROOT
)
$ErrorActionPreference = 'Stop'

if (-not (Get-LocalUser -Name $Account -ErrorAction SilentlyContinue)) {
  $pw = Read-Host -AsSecureString "Password for $Account (long, random)"
  if ($PSCmdlet.ShouldProcess($Account, 'New-LocalUser')) {
    New-LocalUser -Name $Account -Password $pw -PasswordNeverExpires -UserMayNotChangePassword `
      -Description 'AI Memory Vault read-only reader' | Out-Null
  }
}

function Invoke-Acl([string]$Target, [string[]]$AclArgs) {
  if (-not (Test-Path $Target)) { Write-Host "[SKIP] $Target (missing)"; return }
  Write-Host "icacls `"$Target`" $($AclArgs -join ' ')"
  if ($PSCmdlet.ShouldProcess($Target, 'icacls')) { & icacls $Target @AclArgs | Out-Null }
}

Invoke-Acl $VaultRoot @('/grant', "${Account}:(OI)(CI)RX")
Invoke-Acl (Join-Path $VaultRoot '.git') @('/deny', "${Account}:(OI)(CI)R")
if ($PrivateRoot) { Invoke-Acl $PrivateRoot @('/grant', "${Account}:(OI)(CI)RX") }
Invoke-Acl (Join-Path $env:APPDATA 'ai-memory-vault') @('/deny', "${Account}:(OI)(CI)F")
Write-Host "Done. The bot account needs its own %APPDATA%\ai-memory-vault (HMAC secret, telegram.token)."
