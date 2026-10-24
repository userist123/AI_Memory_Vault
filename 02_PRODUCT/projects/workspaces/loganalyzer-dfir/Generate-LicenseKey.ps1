<#
.SYNOPSIS
    Emite o licență LogAnalyzer semnată (ECDSA P-256), prin LogAnalyzer.KeyGen license-sign.
.DESCRIPTION
    Schema veche (SHA-256 cu o "sare" scrisă în codul public) a fost eliminată: oricine putea calcula o cheie validă.
    Acum licența este semnată cu cheia privată de licență, care există doar pe mediul offline al celui care emite
    licențele (creată o singură dată cu: LogAnalyzer.KeyGen license-keygen <fișier-cheie-privată> <fișier-cheie-publică>).
    Scriptul nu conține niciun secret; doar apelează KeyGen.
.PARAMETER PrivateKey
    Fișierul cheii private de licență (creat cu license-keygen).
.PARAMETER HardwareId
    Hardware ID-ul stației (16 caractere hexazecimale, din fereastra de activare a aplicației).
.PARAMETER ExpiryDate
    Data de expirare, AAAA-LL-ZZ (licența e valabilă toată ziua respectivă, UTC). Implicit: peste un an.
.PARAMETER KeyGen
    Calea către LogAnalyzer.KeyGen.exe. Implicit: cel din același folder cu scriptul.
.EXAMPLE
    .\Generate-LicenseKey.ps1 -PrivateKey E:\chei\licenta.key -HardwareId A1B2C3D4E5F67890 -ExpiryDate 2030-12-31
#>
param (
    [Parameter(Mandatory = $true)][string]$PrivateKey,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9A-Fa-f]{16}$')][string]$HardwareId,
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')][string]$ExpiryDate = (Get-Date).ToUniversalTime().AddYears(1).ToString("yyyy-MM-dd"),
    [string]$KeyGen = (Join-Path $PSScriptRoot "LogAnalyzer.KeyGen.exe")
)

if (-not (Test-Path -LiteralPath $KeyGen)) {
    Write-Error "Nu găsesc LogAnalyzer.KeyGen.exe la '$KeyGen'. Indicați-l cu -KeyGen."
    exit 1
}

& $KeyGen license-sign $PrivateKey --hardware-id $HardwareId --expires $ExpiryDate
exit $LASTEXITCODE
