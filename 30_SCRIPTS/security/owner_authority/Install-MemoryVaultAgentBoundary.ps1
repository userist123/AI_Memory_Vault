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
# An explicit DENY beats inherited Modify/Write grants. Deny only the WRITE-class rights:
# denying (M) would also deny Read/Execute (M includes RX) and lock the agent out entirely.
#   W = write data/append/attributes, D = delete, DC = delete child,
#   WDAC = change permissions, WO = take ownership
$denyWrite = "${AgentAccount}:(OI)(CI)(W,D,DC,WDAC,WO)"
& icacls.exe $RepoPath /deny $denyWrite | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls failed on $RepoPath" }
& icacls.exe $RepoPath /grant "${AgentAccount}:(OI)(CI)RX" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls grant RX failed on $RepoPath" }

# .git holds history and hooks: the agent has no business there at all.
$gitPath = Join-Path $RepoPath ".git"
if (Test-Path -LiteralPath $gitPath -PathType Container) {
    & icacls.exe $gitPath /deny "${AgentAccount}:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed on $gitPath" }
}

Write-Host "Memory Vault agent boundary installed."
Write-Host "Agent account: $AgentAccount"
Write-Host "Repository: $RepoPath"
Write-Host "The agent account is not a local administrator, has read-only repository access and no access to .git."
Write-Host "Owner approval must be performed outside the agent account before any owner-controlled mutation."
