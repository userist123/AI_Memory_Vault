#requires -Version 7.0
<#
.SYNOPSIS
  LogAnalyzer release gate (contract section 22; owner decisions 7 and 8). Evaluates every R22 item and prints the matrix.

.DESCRIPTION
  Report mode  (default): prints the matrix, writes release-gate/out/gate-report.{json,md}, always exits 0. Used on pull requests.
  Enforce mode: same, and exits 1 unless every item is PASS or WAIVED. Used for a release (tag loganalyzer-v*, manual dispatch) and by the owner.

  Items: tests passing, synthetic corpus, evidence integrity, audit integrity, AI claim validation, response verification,
  no critical findings (all blocking, never waivable); real-corpus validation and differential validation (waivable only by a
  signed owner waiver, see docs/dfir/RELEASE_GATE.md). An item whose capability does not exist yet is NOT_IMPLEMENTED and fails enforce mode.

.EXAMPLE
  # Owner, on Windows, with the real corpus, before a release:
  pwsh release-gate/Invoke-ReleaseGate.ps1 -Mode Enforce -Version 0.1.0-dfir -RunTests -CorpusRoot D:\corpus\NanAgentCase -RunDifferential

.EXAMPLE
  # CI: tests already ran, results are on disk
  pwsh release-gate/Invoke-ReleaseGate.ps1 -Mode Report -TestResultsDir . -Commit $env:GITHUB_SHA
#>
[CmdletBinding()]
param(
    [ValidateSet('Report', 'Enforce')][string]$Mode = 'Report',
    [string]$Version,
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    # Where the .trx files are searched. With -RunTests the tests are run first and this is release-gate/out/trx.
    [string]$TestResultsDir,
    [string]$TestBinDir,
    [switch]$RunTests,
    # Folder of the real corpus on this machine (sets LADFIR_CORPUS and LADFIR_REQUIRE_CORPUS=1 for the test run).
    [string]$CorpusRoot,
    [switch]$RunDifferential,
    [string]$WaiverPath,
    [string]$WaiverPublicKeyPath,
    [string]$OutDir,
    [string]$Commit = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseGate.Lib.ps1')

if (-not $Version) {
    $m = Select-String -Path (Join-Path $ProjectRoot 'LogAnalyzer.Dfir.Core/Model/CaseInfo.cs') -Pattern 'ApplicationVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
    if (-not $m) { throw 'Version not given and DfirInfo.ApplicationVersion not found.' }
    $Version = $m.Matches[0].Groups[1].Value
}
if (-not $OutDir) { $OutDir = Join-Path $ProjectRoot 'release-gate/out' }
if (-not $WaiverPath) { $WaiverPath = Join-Path $ProjectRoot "release-gate/waivers/$Version.json" }
if (-not $WaiverPublicKeyPath) { $WaiverPublicKeyPath = Join-Path $ProjectRoot 'release-gate/waiver-signer-public-key.b64' }
if (-not $TestBinDir) { $TestBinDir = Join-Path $ProjectRoot 'LogAnalyzer.Dfir.Tests/bin' }
if (-not $Commit) { $Commit = (& git -C $ProjectRoot rev-parse HEAD 2>$null) ; if (-not $Commit) { $Commit = 'unknown' } }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if ($RunTests) {
    if ($TestResultsDir) { throw '-RunTests and -TestResultsDir are mutually exclusive.' }
    $TestResultsDir = Join-Path $OutDir 'trx'
    if (Test-Path $TestResultsDir) { Remove-Item -Recurse -Force $TestResultsDir }
    if ($CorpusRoot) {
        if (-not (Test-Path -LiteralPath $CorpusRoot)) { throw "Corpus folder not found: $CorpusRoot" }
        $env:LADFIR_CORPUS = $CorpusRoot
        $env:LADFIR_REQUIRE_CORPUS = '1'      # a missing corpus section then FAILS instead of being skipped
    }
    if ($RunDifferential) { $env:LADFIR_LAB = '1' }
    Write-Host "Running the full test suite (Release) into $TestResultsDir ..."
    & dotnet test (Join-Path $ProjectRoot 'LogAnalyzer.slnx') --configuration Release --logger 'trx;LogFilePrefix=gate' --results-directory $TestResultsDir
    # A failing test run is not an error here: the gate reads the .trx files and reports it as an item.
}
elseif (-not $TestResultsDir) { $TestResultsDir = $ProjectRoot }

$ev = Invoke-GateEvaluation -ProjectRoot $ProjectRoot -TestResultsDir $TestResultsDir -Version $Version -TestBinDir $TestBinDir `
    -WaiverPath $WaiverPath -WaiverPublicKeyPath $WaiverPublicKeyPath

$md = Format-GateMarkdown -Evaluation $ev -Mode $Mode -Commit $Commit
$report = [ordered]@{
    schema       = 'loganalyzer-release-gate-report/1'
    version      = $ev.Version
    mode         = $Mode
    commit       = $Commit
    generatedUtc = [datetime]::UtcNow.ToString('o')
    ready        = $ev.Ready
    counts       = $ev.Counts
    waiver       = [ordered]@{ file = $WaiverPath; valid = $ev.Waiver.Valid; detail = $ev.Waiver.Reason }
    items        = @($ev.Rows | ForEach-Object { [ordered]@{ id = $_.Id; r22 = $_.R22; policy = $_.Policy; status = $_.Status; detail = $_.Detail } })
}
$report | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 -LiteralPath (Join-Path $OutDir 'gate-report.json')
Set-Content -Encoding utf8 -LiteralPath (Join-Path $OutDir 'gate-report.md') -Value $md

Write-Host ''
$ev.Rows | Format-Table Id, Policy, Status, @{ n = 'Detail'; e = { if ($_.Detail.Length -gt 110) { $_.Detail.Substring(0, 107) + '...' } else { $_.Detail } } } -AutoSize -Wrap | Out-String -Width 220 | Write-Host
Write-Host "Gate ($Mode) for $($ev.Version): $(if ($ev.Ready) { 'RELEASE READY' } else { 'NOT READY' })  [$($ev.Counts)]"
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Encoding utf8 -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $md }

if ($Mode -eq 'Enforce' -and -not $ev.Ready) {
    Write-Host '::error title=Release gate::Release gate failed in enforce mode (see the matrix above).'
    exit 1
}
exit 0
