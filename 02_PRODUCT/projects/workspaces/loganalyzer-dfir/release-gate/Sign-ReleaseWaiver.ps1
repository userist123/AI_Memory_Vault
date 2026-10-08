#requires -Version 7.0
<#
.SYNOPSIS
  Owner tool: creates the signing key pair, and writes a signed release waiver (decision 7 / 8: only real-corpus and
  differential validation can be waived).

.DESCRIPTION
  Signature: ECDSA P-256 with SHA-256 over a canonical text built from the waiver fields (see Get-WaiverPayload), so line
  endings and JSON formatting cannot invalidate it. The PRIVATE key never goes in the repository; the PUBLIC key goes in
  release-gate/waiver-signer-public-key.b64 (a change to that file is a change of who may waive: review it as such).

.EXAMPLE
  pwsh release-gate/Sign-ReleaseWaiver.ps1 -NewKeyPair -KeyDir $HOME\.loganalyzer-waiver
  pwsh release-gate/Sign-ReleaseWaiver.ps1 -Version 0.1.0-dfir -Waives real_corpus,differential -Signer owner `
       -Reason 'Real corpus not available on the release machine; differential run postponed to 0.1.1' `
       -PrivateKeyPath $HOME\.loganalyzer-waiver\waiver-signer-private-key.pkcs8.b64
#>
[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(ParameterSetName = 'Key', Mandatory)][switch]$NewKeyPair,
    [Parameter(ParameterSetName = 'Key', Mandatory)][string]$KeyDir,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string]$Version,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string[]]$Waives,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string]$Reason,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string]$Signer,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string]$PrivateKeyPath,
    [Parameter(ParameterSetName = 'Sign')][string]$Date = [datetime]::UtcNow.ToString('yyyy-MM-dd'),
    [Parameter(ParameterSetName = 'Sign')][string]$Out
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseGate.Lib.ps1')

if ($PSCmdlet.ParameterSetName -eq 'Key') {
    New-Item -ItemType Directory -Force -Path $KeyDir | Out-Null
    $k = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
    $priv = Join-Path $KeyDir 'waiver-signer-private-key.pkcs8.b64'
    $pub = Join-Path $KeyDir 'waiver-signer-public-key.b64'
    Set-Content -NoNewline -Encoding ascii -LiteralPath $priv -Value ([Convert]::ToBase64String($k.ExportPkcs8PrivateKey()))
    Set-Content -NoNewline -Encoding ascii -LiteralPath $pub -Value ([Convert]::ToBase64String($k.ExportSubjectPublicKeyInfo()))
    Write-Host "Private key (keep it OUT of the repository): $priv"
    Write-Host "Public key (commit it as release-gate/waiver-signer-public-key.b64): $pub"
    return
}

$waivable = @((Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'gate-items.json') | ConvertFrom-Json).items | Where-Object policy -eq 'waivable' | ForEach-Object id)
$bad = @($Waives | Where-Object { $_ -notin $waivable })
if ($bad.Count -gt 0) { throw "Not waivable (blocking items cannot be waived): $($bad -join ', '). Waivable: $($waivable -join ', ')." }
if ($Reason.Trim().Length -lt 20) { throw 'The reason must be a real explanation (at least 20 characters).' }

$k = [System.Security.Cryptography.ECDsa]::Create()
$k.ImportPkcs8PrivateKey([Convert]::FromBase64String((Get-Content -Raw -LiteralPath $PrivateKeyPath).Trim()), [ref]$null)
$payload = [Text.Encoding]::UTF8.GetBytes((Get-WaiverPayload -Version $Version -Date $Date -Signer $Signer -Waives $Waives -Reason $Reason.Trim()))
$sig = $k.SignData($payload, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)

$waiver = [ordered]@{
    schema    = 'loganalyzer-release-waiver/1'
    version   = $Version
    date      = $Date
    waives    = @($Waives | Sort-Object)
    reason    = $Reason.Trim()
    signer    = $Signer
    signature = [ordered]@{ alg = 'ecdsa-p256-sha256'; value = [Convert]::ToBase64String($sig) }
}
if (-not $Out) { $Out = Join-Path $PSScriptRoot "waivers/$Version.json" }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$waiver | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 -LiteralPath $Out
Write-Host "Waiver written: $Out"
