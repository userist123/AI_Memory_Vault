[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$RepoPath,

    [Parameter(Mandatory=$true)]
    [string]$AgentAccount
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent()
)
if (-not $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)) {
    throw "Run this installer from an elevated PowerShell session."
}

if (-not (Test-Path -LiteralPath $RepoPath -PathType Container)) {
    throw "Repository path does not exist: $RepoPath"
}

# The agent account must never be a local administrator.
$admins = Get-LocalGroupMember -Group "Administrators" -ErrorAction SilentlyContinue
if ($admins | Where-Object { $_.Name -ieq "$env:COMPUTERNAME\$AgentAccount" }) {
    Remove-LocalGroupMember -Group "Administrators" -Member "$env:COMPUTERNAME\$AgentAccount"
}

# Strong local boundary: the agent may inspect the repository but cannot modify it.
# An explicit DENY beats inherited Modify/Write grants.
icacls.exe $RepoPath /deny "${AgentAccount}:(OI)(CI)(M)" | Out-Null

$gitPath = Join-Path $RepoPath ".git"
if (Test-Path -LiteralPath $gitPath -PathType Container) {
    icacls.exe $gitPath /deny "$AgentAccount:(OI)(CI)(M)" | Out-Null
}

Write-Host "Memory Vault agent boundary installed."
Write-Host "Agent account: $AgentAccount"
Write-Host "Repository: $RepoPath"
Write-Host "The agent account is not a local administrator and has read-only repository access."
Write-Host "Owner approval must be performed outside the agent account before any owner-controlled mutation."
