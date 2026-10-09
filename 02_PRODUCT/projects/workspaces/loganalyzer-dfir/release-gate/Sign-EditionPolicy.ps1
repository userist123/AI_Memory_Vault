#requires -Version 7.0
<#
.SYNOPSIS
  Owner tool: creates the edition-policy signing key pair and writes a signed LogAnalyzer.policy for the unclassified edition
  (P2 air-gapped / P3 connected). docs/dfir/EDITIONS.md describes the procedure.

.DESCRIPTION
  Same scheme as Sign-ReleaseWaiver.ps1: ECDSA P-256 / SHA-256, IEEE P1363 signature, canonical text payload built from the
  fields (must match EditionPolicy.Payload in LogAnalyzer.Core/Services/Edition/EditionPolicy.cs). The PRIVATE key never goes in
  the repository. The PUBLIC key is embedded in the build as LogAnalyzer.App/Edition/Unclassified/edition-policy-public-key.b64;
  without it no policy is valid and the application stays air-gapped.

.EXAMPLE
  pwsh release-gate/Sign-EditionPolicy.ps1 -NewKeyPair -KeyDir $HOME\.loganalyzer-policy
  pwsh release-gate/Sign-EditionPolicy.ps1 -Mode connected -Version 1 -NotBefore 2026-10-09 -Audience '*' -Signer owner `
       -PrivateKeyPath $HOME\.loganalyzer-policy\policy-signer-private-key.pkcs8.b64 -Out .\LogAnalyzer.policy
  # install: copy LogAnalyzer.policy to %ProgramData%\LogAnalyzer\ (ACL: administrators write, users read)
#>
[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(ParameterSetName = 'Key', Mandatory)][switch]$NewKeyPair,
    [Parameter(ParameterSetName = 'Key', Mandatory)][string]$KeyDir,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][ValidateSet('airgapped', 'connected')][string]$Mode,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][int64]$Version,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string]$NotBefore,
    [Parameter(ParameterSetName = 'Sign')][string]$NotAfter = '',
    [Parameter(ParameterSetName = 'Sign')][string]$Audience = '*',
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string]$Signer,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string]$PrivateKeyPath,
    [Parameter(ParameterSetName = 'Sign', Mandatory)][string]$Out
)
$ErrorActionPreference = 'Stop'

if ($PSCmdlet.ParameterSetName -eq 'Key') {
    New-Item -ItemType Directory -Force -Path $KeyDir | Out-Null
    $k = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
    Set-Content -NoNewline -Encoding ascii -LiteralPath (Join-Path $KeyDir 'policy-signer-private-key.pkcs8.b64') -Value ([Convert]::ToBase64String($k.ExportPkcs8PrivateKey()))
    Set-Content -NoNewline -Encoding ascii -LiteralPath (Join-Path $KeyDir 'edition-policy-public-key.b64') -Value ([Convert]::ToBase64String($k.ExportSubjectPublicKeyInfo()))
    Write-Host "Private key (keep OUT of the repository): $(Join-Path $KeyDir 'policy-signer-private-key.pkcs8.b64')"
    Write-Host "Public key (commit as LogAnalyzer.App/Edition/Unclassified/edition-policy-public-key.b64, then rebuild): $(Join-Path $KeyDir 'edition-policy-public-key.b64')"
    return
}

$payload = @('loganalyzer-edition-policy/1', "mode=$Mode", "version=$Version", "notBefore=$NotBefore", "notAfter=$NotAfter", "audience=$Audience", "signer=$Signer") -join "`n"
$k = [System.Security.Cryptography.ECDsa]::Create()
$k.ImportPkcs8PrivateKey([Convert]::FromBase64String((Get-Content -Raw -LiteralPath $PrivateKeyPath).Trim()), [ref]$null)
$sig = $k.SignData([Text.Encoding]::UTF8.GetBytes($payload), [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
$policy = [ordered]@{
    schema = 'loganalyzer-edition-policy/1'; mode = $Mode; version = $Version; notBefore = $NotBefore
    notAfter = $(if ($NotAfter) { $NotAfter } else { $null }); audience = $Audience; signer = $Signer
    signature = [ordered]@{ alg = 'ecdsa-p256-sha256'; value = [Convert]::ToBase64String($sig) }
}
$policy | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 -LiteralPath $Out
Write-Host "Policy written: $Out"
