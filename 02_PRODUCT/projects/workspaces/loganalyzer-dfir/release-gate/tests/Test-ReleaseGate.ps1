#requires -Version 7.0
# Self-test of the release gate: it must say PASS only with evidence, NOT_IMPLEMENTED when a capability has no tests,
# refuse every invalid waiver, and be able to reach RELEASE READY when all evidence exists (so it is not stuck by construction).
# No test framework needed:  pwsh release-gate/tests/Test-ReleaseGate.ps1   (exit code 1 on any failed assertion)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$release = Split-Path -Parent $here
. (Join-Path $release 'ReleaseGate.Lib.ps1')

$script:failures = 0
$script:checks = 0
function Assert-That([bool]$Condition, [string]$Message) {
    $script:checks++
    if (-not $Condition) { $script:failures++; Write-Host "  FAIL: $Message" -ForegroundColor Red } else { Write-Host "  ok:   $Message" }
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("gate_test_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
try {
    # A project root with the real gate definition.
    function New-Root([string]$name) {
        $root = Join-Path $tmp $name
        New-Item -ItemType Directory -Force -Path (Join-Path $root 'release-gate/waivers'), (Join-Path $root 'trx'), (Join-Path $root 'bin') | Out-Null
        Copy-Item (Join-Path $release 'gate-items.json') (Join-Path $root 'release-gate/gate-items.json')
        $root
    }
    function New-Trx([string]$root, [object[]]$tests) {
        $ns = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
        $sb = [Text.StringBuilder]::new()
        [void]$sb.Append("<?xml version=`"1.0`" encoding=`"UTF-8`"?><TestRun xmlns=`"$ns`"><Results>")
        $defs = [Text.StringBuilder]::new()
        $i = 0
        foreach ($t in $tests) {
            $id = [guid]::NewGuid().ToString()
            [void]$sb.Append("<UnitTestResult testId=`"$id`" testName=`"$($t.Name)`" outcome=`"$($t.Outcome)`" />")
            [void]$defs.Append("<UnitTest name=`"$($t.Name)`" id=`"$id`"><TestMethod className=`"$($t.Class)`" name=`"$($t.Name)`" /></UnitTest>")
            $i++
        }
        [void]$sb.Append("</Results><TestDefinitions>$defs</TestDefinitions></TestRun>")
        Set-Content -Encoding utf8 -LiteralPath (Join-Path $root 'trx/x.trx') -Value $sb.ToString()
    }
    function Many([string]$class, [int]$n, [string]$outcome = 'Passed') { 1..$n | ForEach-Object { [pscustomobject]@{ Class = $class; Name = "T$_"; Outcome = $outcome } } }
    function Row($ev, [string]$id) { $ev.Rows | Where-Object Id -eq $id }
    function Eval($root, $waiver = $null, $pub = $null) {
        Invoke-GateEvaluation -ProjectRoot $root -TestResultsDir (Join-Path $root 'trx') -Version '9.9.9-test' -TestBinDir (Join-Path $root 'bin') `
            -WaiverPath ($waiver ?? (Join-Path $root 'release-gate/waivers/9.9.9-test.json')) -WaiverPublicKeyPath ($pub ?? (Join-Path $root 'release-gate/waiver-signer-public-key.b64'))
    }

    $base = @(
        (Many 'LogAnalyzer.Dfir.Tests.CoreTests' 20)
        (Many 'LogAnalyzer.Dfir.Tests.Synthetic.SyntheticCorpusTests' 14)
        (Many 'LogAnalyzer.Dfir.Tests.EvidenceIntegrityTests' 7)
        (Many 'LogAnalyzer.Dfir.Tests.EvidenceContractTests' 5)
        (Many 'LogAnalyzer.Dfir.Tests.CorpusRegressionTests' 3 'NotExecuted')
        (Many 'LogAnalyzer.Dfir.Tests.ForensicLab' 2 'NotExecuted')
    )

    Write-Host '== today''s situation: implemented checks pass, later work packages are NOT_IMPLEMENTED, corpus not run'
    $r1 = New-Root 'today'; New-Trx $r1 $base
    $e = Eval $r1
    Assert-That ((Row $e 'tests_passing').Status -eq 'PASS') 'tests_passing PASS when all tests pass'
    Assert-That ((Row $e 'synthetic_corpus').Status -eq 'PASS') 'synthetic_corpus PASS with 14 executed'
    Assert-That ((Row $e 'evidence_integrity').Status -eq 'PASS') 'evidence_integrity PASS'
    foreach ($id in 'audit_integrity', 'ai_claim_validation', 'response_verification', 'critical_findings') {
        Assert-That ((Row $e $id).Status -eq 'NOT_IMPLEMENTED') "$id is NOT_IMPLEMENTED (no tests / no register), never silently PASS"
    }
    Assert-That ((Row $e 'real_corpus').Status -eq 'NOT_RUN') 'real_corpus NOT_RUN without the corpus report'
    Assert-That ((Row $e 'differential').Status -eq 'NOT_RUN') 'differential NOT_RUN when the lab tests were skipped'
    Assert-That (-not $e.Ready) 'gate is NOT READY'

    Write-Host '== failures and removed tests'
    $r2 = New-Root 'failing'; New-Trx $r2 ($base + (Many 'LogAnalyzer.Dfir.Tests.CoreTests' 1 'Failed' | ForEach-Object { $_.Name = 'Broken'; $_ }))
    Assert-That ((Row (Eval $r2) 'tests_passing').Status -eq 'FAIL') 'a failed test fails tests_passing'
    $r3 = New-Root 'fewsynth'; New-Trx $r3 (@(Many 'LogAnalyzer.Dfir.Tests.CoreTests' 5) + (Many 'LogAnalyzer.Dfir.Tests.Synthetic.SyntheticCorpusTests' 3))
    Assert-That ((Row (Eval $r3) 'synthetic_corpus').Status -eq 'FAIL') 'too few synthetic tests executed fails (tests were removed)'
    $r3b = New-Root 'notrx'
    Assert-That ((Row (Eval $r3b) 'tests_passing').Status -eq 'FAIL') 'no test results at all fails tests_passing'

    Write-Host '== signed waiver'
    $r4 = New-Root 'waiver'; New-Trx $r4 $base
    $keys = Join-Path $tmp 'keys'
    & (Join-Path $release 'Sign-ReleaseWaiver.ps1') -NewKeyPair -KeyDir $keys | Out-Null
    Copy-Item (Join-Path $keys 'waiver-signer-public-key.b64') (Join-Path $r4 'release-gate/waiver-signer-public-key.b64')
    $wpath = Join-Path $r4 'release-gate/waivers/9.9.9-test.json'
    $sign = { param($v = '9.9.9-test', $waives = @('real_corpus', 'differential'), $reason = 'Real corpus unavailable on the release machine; differential postponed', $date = $null, $out = $wpath)
        $a = @{ Version = $v; Waives = $waives; Reason = $reason; Signer = 'owner'; PrivateKeyPath = (Join-Path $keys 'waiver-signer-private-key.pkcs8.b64'); Out = $out }
        if ($date) { $a.Date = $date }
        & (Join-Path $release 'Sign-ReleaseWaiver.ps1') @a | Out-Null }
    & $sign
    $e = Eval $r4
    Assert-That ((Row $e 'real_corpus').Status -eq 'WAIVED') 'valid signed waiver waives real_corpus'
    Assert-That ((Row $e 'differential').Status -eq 'WAIVED') 'valid signed waiver waives differential'
    Assert-That ((Row $e 'audit_integrity').Status -eq 'NOT_IMPLEMENTED') 'the waiver does not touch blocking items'
    Assert-That (-not $e.Ready) 'still NOT READY: blocking items remain'

    $json = Get-Content -Raw $wpath | ConvertFrom-Json
    $json.reason = 'A different reason that was never signed by the owner at all'
    $json | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $wpath
    $e = Eval $r4
    Assert-That ((Row $e 'real_corpus').Status -eq 'FAIL' -and -not $e.Waiver.Valid) 'tampered reason invalidates the signature (and fails the item)'

    & $sign -v '1.0.0-other'
    Assert-That (-not (Eval $r4).Waiver.Valid) 'waiver for another version is refused'

    $threw = $false
    try { & $sign -waives @('audit_integrity') } catch { $threw = $true }
    Assert-That $threw 'the signing tool refuses to waive a blocking item'
    # A hand-written waiver that waives a blocking item must also be refused by the gate.
    & $sign
    $json = Get-Content -Raw $wpath | ConvertFrom-Json; $json.waives = @('real_corpus', 'audit_integrity')
    $json | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $wpath
    Assert-That (-not (Eval $r4).Waiver.Valid) 'gate refuses a waiver that lists a blocking item'

    & $sign -date '2999-01-01'
    Assert-That (-not (Eval $r4).Waiver.Valid) 'waiver dated in the future is refused'
    $threw = $false
    try { & $sign -reason 'too short' } catch { $threw = $true }
    Assert-That $threw 'the signing tool refuses a one-line reason'

    & $sign
    Remove-Item (Join-Path $r4 'release-gate/waiver-signer-public-key.b64')
    $e = Eval $r4
    Assert-That (-not $e.Waiver.Valid -and (Row $e 'real_corpus').Status -eq 'FAIL') 'without a configured public key the waiver fails closed'

    # A waiver signed by somebody else's key.
    & (Join-Path $release 'Sign-ReleaseWaiver.ps1') -NewKeyPair -KeyDir (Join-Path $tmp 'keys2') | Out-Null
    Copy-Item (Join-Path $tmp 'keys2/waiver-signer-public-key.b64') (Join-Path $r4 'release-gate/waiver-signer-public-key.b64')
    Assert-That (-not (Eval $r4).Waiver.Valid) 'waiver signed with a different key is refused'

    Write-Host '== everything present: the gate can reach RELEASE READY'
    $r5 = New-Root 'ready'
    New-Trx $r5 (@($base | Where-Object { $_.Class -notmatch 'CorpusRegression|ForensicLab' }) + (Many 'LogAnalyzer.Dfir.Tests.AuditChainTests' 2) + (Many 'LogAnalyzer.Dfir.Tests.AiClaimValidationTests' 2) +
        (Many 'LogAnalyzer.Dfir.Tests.ResponseVerificationTests' 2) + (Many 'LogAnalyzer.Dfir.Tests.CorpusRegressionTests' 3) + (Many 'LogAnalyzer.Dfir.Tests.ForensicLab' 2))
    '{"schema":"loganalyzer-findings-register/1","findings":[{"id":"F1","severity":"high","status":"open"},{"id":"F2","severity":"critical","status":"resolved"}]}' | Set-Content (Join-Path $r5 'release-gate/findings-register.json')
    New-Item -ItemType Directory -Force (Join-Path $r5 'bin/Release') | Out-Null
    'FORENSIC VALIDATION = AVAILABLE (21/21 sections present)' | Set-Content (Join-Path $r5 'bin/Release/forensic_validation.txt')
    $e = Eval $r5
    Assert-That $e.Ready ("RELEASE READY when every item has evidence ($($e.Counts)); not-ready rows: " + (($e.Rows | Where-Object { $_.Status -notin 'PASS', 'WAIVED' } | ForEach-Object { "$($_.Id)=$($_.Status)" }) -join ','))

    Write-Host '== findings register'
    '{"schema":"loganalyzer-findings-register/1","findings":[{"id":"F9","severity":"critical","status":"open"}]}' | Set-Content (Join-Path $r5 'release-gate/findings-register.json')
    Assert-That ((Row (Eval $r5) 'critical_findings').Status -eq 'FAIL') 'an open critical finding fails the gate'
    '{"nonsense":true}' | Set-Content (Join-Path $r5 'release-gate/findings-register.json')
    Assert-That ((Row (Eval $r5) 'critical_findings').Status -eq 'FAIL') 'a malformed register fails (not silently PASS)'

    Write-Host '== real corpus report that is not AVAILABLE'
    'FORENSIC VALIDATION = PARTIAL (5/21)' | Set-Content (Join-Path $r5 'bin/Release/forensic_validation.txt')
    Assert-That ((Row (Eval $r5) 'real_corpus').Status -eq 'NOT_RUN') 'PARTIAL corpus is NOT_RUN, not PASS'

    Write-Host '== script exit codes'
    $r6 = New-Root 'exit'; New-Trx $r6 $base
    New-Item -ItemType Directory -Force -Path (Join-Path $r6 'release-gate') | Out-Null
    Copy-Item (Join-Path $release '*.ps1') (Join-Path $r6 'release-gate')
    $pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue)?.Source ?? (Get-Process -Id $PID).Path
    & $pwsh -NoProfile -File (Join-Path $r6 'release-gate/Invoke-ReleaseGate.ps1') -Mode Report -ProjectRoot $r6 -TestResultsDir (Join-Path $r6 'trx') -Version 9.9.9-test -OutDir (Join-Path $r6 'out1') *> (Join-Path $r6 'log1.txt')
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $r6 'log1.txt') | Select-Object -Last 15 | Write-Host }
    Assert-That ($LASTEXITCODE -eq 0) 'report mode exits 0 although the gate is NOT READY'
    Assert-That (Test-Path (Join-Path $r6 'out1/gate-report.json')) 'report mode writes gate-report.json'
    $rep = Get-Content -Raw (Join-Path $r6 'out1/gate-report.json') | ConvertFrom-Json
    Assert-That ($rep.ready -eq $false -and @($rep.items).Count -eq 9) 'report lists the 9 gate items and ready=false'
    & $pwsh -NoProfile -File (Join-Path $r6 'release-gate/Invoke-ReleaseGate.ps1') -Mode Enforce -ProjectRoot $r6 -TestResultsDir (Join-Path $r6 'trx') -Version 9.9.9-test -OutDir (Join-Path $r6 'out2') *> $null
    Assert-That ($LASTEXITCODE -eq 1) 'enforce mode exits 1 while any item is not PASS/WAIVED'
}
finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }

Write-Host ''
Write-Host "$($script:checks - $script:failures)/$($script:checks) assertions passed"
if ($script:failures -gt 0) { exit 1 }
exit 0
