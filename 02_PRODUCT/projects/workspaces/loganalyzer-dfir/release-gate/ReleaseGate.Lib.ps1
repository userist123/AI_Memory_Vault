#requires -Version 7.0
# Functions of the LogAnalyzer release gate. Dot-sourced by Invoke-ReleaseGate.ps1, Sign-ReleaseWaiver.ps1 and the tests.
# Pure evaluation: nothing here runs the product or changes the repository.

Set-StrictMode -Version Latest

$script:WaiverSchema = 'loganalyzer-release-waiver/1'
$script:WaiverAlgorithm = 'ecdsa-p256-sha256'

# ---- test results (trx) -------------------------------------------------------------------------------------------------------

function Read-TrxResults {
    <# One record per executed or skipped test: ClassName, TestName, Outcome (Passed / Failed / NotExecuted / ...). #>
    param([Parameter(Mandatory)][string]$Directory)
    $results = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $Directory)) { return , $results }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -Filter *.trx -File) {
        [xml]$doc = Get-Content -Raw -LiteralPath $file.FullName
        $ns = [System.Xml.XmlNamespaceManager]::new($doc.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $classOf = @{}
        foreach ($u in $doc.SelectNodes('//t:UnitTest', $ns)) {
            $m = $u.SelectSingleNode('t:TestMethod', $ns)
            if ($m) { $classOf[$u.Attributes['id'].Value] = $m.Attributes['className'].Value }
        }
        foreach ($r in $doc.SelectNodes('//t:UnitTestResult', $ns)) {
            $id = $r.Attributes['testId'].Value
            $results.Add([pscustomobject]@{
                    File      = $file.Name
                    ClassName = $(if ($classOf.ContainsKey($id)) { $classOf[$id] } else { '' })
                    TestName  = $r.Attributes['testName'].Value
                    Outcome   = $r.Attributes['outcome'].Value
                })
        }
    }
    return , $results
}

function New-Verdict {
    param([string]$Status, [string]$Detail)
    [pscustomobject]@{ Status = $Status; Detail = $Detail }
}

# ---- individual checks ----------------------------------------------------------------------------------------------------------

function Test-AllTests {
    param($Results)
    if ($Results.Count -eq 0) { return New-Verdict 'FAIL' 'No test results found (no .trx files): the tests did not run, so they cannot be called passing.' }
    $failed = @($Results | Where-Object { $_.Outcome -notin 'Passed', 'NotExecuted' })
    $passed = @($Results | Where-Object { $_.Outcome -eq 'Passed' }).Count
    $skipped = @($Results | Where-Object { $_.Outcome -eq 'NotExecuted' }).Count
    if ($failed.Count -gt 0) {
        $names = ($failed | Select-Object -First 5 | ForEach-Object { "$($_.ClassName).$($_.TestName)" }) -join '; '
        return New-Verdict 'FAIL' "$($failed.Count) failed, $passed passed, $skipped skipped. First failures: $names"
    }
    if ($passed -eq 0) { return New-Verdict 'FAIL' "0 tests passed ($skipped skipped)." }
    New-Verdict 'PASS' "$passed passed, 0 failed, $skipped skipped (skipped tests are the corpus/lab tests that need the owner's machine; they are judged by the real_corpus and differential items)."
}

function Test-ClassItem {
    param($Results, $Item)
    $mine = @($Results | Where-Object { $_.ClassName -match $Item.classNamePattern })
    $executed = @($mine | Where-Object { $_.Outcome -ne 'NotExecuted' })
    $failed = @($executed | Where-Object { $_.Outcome -ne 'Passed' })
    $min = [int]$Item.minExecuted
    if ($mine.Count -eq 0) {
        $pending = if ($Item.PSObject.Properties['pendingWorkPackage']) { " Pending: $($Item.pendingWorkPackage)." } else { '' }
        $scope = if ($Item.PSObject.Properties['scope']) { " $($Item.scope)" } else { '' }
        return New-Verdict 'NOT_IMPLEMENTED' "No test class matching /$($Item.classNamePattern)/ exists in the test results.$pending$scope"
    }
    if ($executed.Count -eq 0) { return New-Verdict 'NOT_RUN' "$($mine.Count) matching tests exist but none was executed (all skipped)." }
    if ($failed.Count -gt 0) { return New-Verdict 'FAIL' "$($failed.Count) of $($executed.Count) executed tests failed: $(($failed | Select-Object -First 3 | ForEach-Object TestName) -join '; ')" }
    if ($executed.Count -lt $min) { return New-Verdict 'FAIL' "Only $($executed.Count) tests executed, at least $min required (tests were removed or skipped)." }
    $scopeNote = if ($Item.PSObject.Properties['scope']) { " Scope: $($Item.scope)" } else { '' }
    New-Verdict 'PASS' "$($executed.Count) executed, all passed.$scopeNote"
}

function Test-FindingsRegister {
    param([string]$ProjectRoot, $Item)
    $path = Join-Path $ProjectRoot $Item.path
    if (-not (Test-Path -LiteralPath $path)) {
        return New-Verdict 'NOT_IMPLEMENTED' "No findings register at $($Item.path). Pending: $($Item.pendingWorkPackage)."
    }
    try { $doc = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -ErrorAction Stop }
    catch { return New-Verdict 'FAIL' "$($Item.path) is not valid JSON: $($_.Exception.Message)" }
    if ($null -eq $doc.PSObject.Properties['schema'] -or $doc.schema -ne 'loganalyzer-findings-register/1' -or $null -eq $doc.PSObject.Properties['findings']) {
        return New-Verdict 'FAIL' "$($Item.path) does not follow schema loganalyzer-findings-register/1 (needs 'schema' and 'findings')."
    }
    $bad = @($doc.findings | Where-Object { -not $_.PSObject.Properties['id'] -or -not $_.PSObject.Properties['severity'] -or -not $_.PSObject.Properties['status'] -or $_.severity -notin 'critical', 'high', 'medium', 'low' -or $_.status -notin 'open', 'resolved', 'accepted' })
    if ($bad.Count -gt 0) { return New-Verdict 'FAIL' "$($bad.Count) register entries lack a valid id, severity (critical|high|medium|low) or status (open|resolved|accepted)." }
    $open = @($doc.findings | Where-Object { $_.severity -eq 'critical' -and $_.status -eq 'open' })
    if ($open.Count -gt 0) { return New-Verdict 'FAIL' "$($open.Count) critical finding(s) unresolved: $(($open | ForEach-Object id) -join ', ')" }
    New-Verdict 'PASS' "Register lists $(@($doc.findings).Count) finding(s); none is critical and open."
}

function Test-RealCorpus {
    param($Results, [string]$BinDirectory)
    $report = $null
    if ($BinDirectory -and (Test-Path -LiteralPath $BinDirectory)) {
        $report = Get-ChildItem -LiteralPath $BinDirectory -Recurse -Filter forensic_validation.txt -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    }
    if (-not $report) { return New-Verdict 'NOT_RUN' 'No forensic_validation.txt report found: the tests did not write one.' }
    $text = Get-Content -Raw -LiteralPath $report.FullName
    $first = ($text -split "`r?`n")[0]
    if ($text -notmatch 'FORENSIC VALIDATION = AVAILABLE') { return New-Verdict 'NOT_RUN' "Real corpus not (fully) present: $first" }
    $corpusTests = @($Results | Where-Object { $_.ClassName -match '\.(CorpusRegressionTests|ForensicValidationTests)$' })
    $executed = @($corpusTests | Where-Object { $_.Outcome -ne 'NotExecuted' })
    $skipped = @($corpusTests | Where-Object { $_.Outcome -eq 'NotExecuted' })
    if ($executed.Count -eq 0) { return New-Verdict 'NOT_RUN' 'Corpus report says AVAILABLE but no corpus regression test was executed.' }
    if ($skipped.Count -gt 0) { return New-Verdict 'NOT_RUN' "$($skipped.Count) corpus tests were skipped even though the report says AVAILABLE (run with LADFIR_REQUIRE_CORPUS=1)." }
    $failed = @($executed | Where-Object { $_.Outcome -ne 'Passed' })
    if ($failed.Count -gt 0) { return New-Verdict 'FAIL' "$($failed.Count) corpus tests failed: $(($failed | Select-Object -First 3 | ForEach-Object TestName) -join '; ')" }
    New-Verdict 'PASS' "$first; $($executed.Count) corpus tests executed and passed."
}

# ---- waiver ---------------------------------------------------------------------------------------------------------------------

function Get-WaiverPayload {
    <# The exact text that is signed. Built from fields, never from the JSON bytes, so formatting and line endings cannot break a signature. #>
    param([string]$Version, [string]$Date, [string]$Signer, [string[]]$Waives, [string]$Reason)
    $reasonHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Reason)))
    @($script:WaiverSchema, "version=$Version", "date=$Date", "signer=$Signer", "waives=$((@($Waives) | Sort-Object) -join ',')", "reason-sha256=$reasonHash") -join "`n"
}

function Test-Waiver {
    <# Validates a waiver file. Fails closed: any missing piece (file, public key, field, signature) means 'no valid waiver'. #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$PublicKeyPath,
        [Parameter(Mandatory)][string[]]$WaivableIds
    )
    $fail = { param($why) [pscustomobject]@{ Valid = $false; Reason = $why; Waives = @(); Signer = ''; Date = '' } }
    if (-not (Test-Path -LiteralPath $Path)) { return & $fail "No waiver file at $Path." }
    try { $w = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -ErrorAction Stop } catch { return & $fail "Waiver is not valid JSON: $($_.Exception.Message)" }
    foreach ($f in 'schema', 'version', 'date', 'reason', 'waives', 'signer', 'signature') {
        if ($null -eq $w.PSObject.Properties[$f]) { return & $fail "Waiver lacks required field '$f'." }
    }
    if ($w.schema -ne $script:WaiverSchema) { return & $fail "Unknown waiver schema '$($w.schema)'." }
    if ($w.version -ne $Version) { return & $fail "Waiver is for version '$($w.version)', not '$Version'." }
    $date = [datetime]::MinValue
    if (-not [datetime]::TryParseExact([string]$w.date, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture, 'AssumeUniversal,AdjustToUniversal', [ref]$date)) { return & $fail "Waiver date '$($w.date)' is not yyyy-MM-dd." }
    if ($date -gt [datetime]::UtcNow.AddDays(1)) { return & $fail "Waiver date $($w.date) is in the future." }
    if ([string]$w.reason -notmatch '\S' -or ([string]$w.reason).Trim().Length -lt 20) { return & $fail 'Waiver reason must be a real explanation (at least 20 characters).' }
    if ([string]$w.signer -notmatch '\S') { return & $fail 'Waiver signer is empty.' }
    $waives = @($w.waives | ForEach-Object { [string]$_ })
    if ($waives.Count -eq 0) { return & $fail 'Waiver waives nothing.' }
    $notWaivable = @($waives | Where-Object { $_ -notin $WaivableIds })
    if ($notWaivable.Count -gt 0) { return & $fail "Waiver tries to waive items that cannot be waived: $($notWaivable -join ', ') (waivable: $($WaivableIds -join ', '))." }
    if ($w.signature.alg -ne $script:WaiverAlgorithm) { return & $fail "Unsupported signature algorithm '$($w.signature.alg)' (expected $script:WaiverAlgorithm)." }
    if (-not (Test-Path -LiteralPath $PublicKeyPath)) { return & $fail "Waiver cannot be verified: no public key at $PublicKeyPath (fails closed)." }
    try {
        $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
        $ecdsa.ImportSubjectPublicKeyInfo([Convert]::FromBase64String((Get-Content -Raw -LiteralPath $PublicKeyPath).Trim()), [ref]$null)
        if ($ecdsa.KeySize -ne 256) { return & $fail "Public key is not a P-256 key (KeySize $($ecdsa.KeySize))." }
        $payload = [Text.Encoding]::UTF8.GetBytes((Get-WaiverPayload -Version $w.version -Date $w.date -Signer $w.signer -Waives $waives -Reason ([string]$w.reason)))
        $sig = [Convert]::FromBase64String([string]$w.signature.value)
        $ok = $ecdsa.VerifyData($payload, $sig, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    }
    catch { return & $fail "Signature could not be verified: $($_.Exception.Message)" }
    if (-not $ok) { return & $fail 'Signature does not match the waiver content and the configured public key.' }
    [pscustomobject]@{ Valid = $true; Reason = "Signed by '$($w.signer)' on $($w.date): $(([string]$w.reason).Trim())"; Waives = $waives; Signer = [string]$w.signer; Date = [string]$w.date }
}

# ---- the gate -----------------------------------------------------------------------------------------------------------------

function Invoke-GateEvaluation {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$TestResultsDir,
        [Parameter(Mandatory)][string]$Version,
        [string]$TestBinDir,
        [string]$WaiverPath,
        [string]$WaiverPublicKeyPath
    )
    $defs = Get-Content -Raw -LiteralPath (Join-Path $ProjectRoot 'release-gate/gate-items.json') | ConvertFrom-Json
    $results = Read-TrxResults -Directory $TestResultsDir
    $waivableIds = @($defs.items | Where-Object { $_.policy -eq 'waivable' } | ForEach-Object id)
    $waiver = Test-Waiver -Path $WaiverPath -Version $Version -PublicKeyPath $WaiverPublicKeyPath -WaivableIds $waivableIds
    $waiverFileExists = Test-Path -LiteralPath $WaiverPath

    $rows = foreach ($item in $defs.items) {
        $v = switch ($item.kind) {
            'all-tests' { Test-AllTests -Results $results }
            'test-classes' { Test-ClassItem -Results $results -Item $item }
            'findings-register' { Test-FindingsRegister -ProjectRoot $ProjectRoot -Item $item }
            'real-corpus' { Test-RealCorpus -Results $results -BinDirectory $TestBinDir }
            default { New-Verdict 'FAIL' "Unknown item kind '$($item.kind)'." }
        }
        $status = $v.Status
        $detail = $v.Detail
        # Only an absent run can be waived (the real corpus / the lab did not run). A run that executed and failed must be fixed.
        if ($item.policy -eq 'waivable' -and $status -in 'NOT_RUN', 'NOT_IMPLEMENTED') {
            if ($waiver.Valid -and $item.id -in $waiver.Waives) {
                $status = 'WAIVED'
                $detail = "$($v.Status): $($v.Detail) | WAIVER: $($waiver.Reason)"
            }
            elseif ($waiverFileExists -and -not $waiver.Valid) {
                $detail = "$($v.Status): $($v.Detail) | Waiver file present but INVALID: $($waiver.Reason)"
                $status = 'FAIL'
            }
            else { $detail = "$($v.Detail) | Not waived: needs a signed owner waiver or a real run." }
        }
        [pscustomobject]@{ Id = $item.id; R22 = $item.r22; Title = $item.title; Policy = $item.policy; Status = $status; Detail = $detail }
    }
    $rows = @($rows)
    $ok = @($rows | Where-Object { $_.Status -in 'PASS', 'WAIVED' }).Count
    [pscustomobject]@{
        Version  = $Version
        Rows     = $rows
        Ready    = ($ok -eq $rows.Count)
        Waiver   = $waiver
        Counts   = ($rows | Group-Object Status | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ' '
        TestCount = $results.Count
    }
}

function Format-GateMarkdown {
    param($Evaluation, [string]$Mode, [string]$Commit)
    $sb = [System.Text.StringBuilder]::new()
    $verdict = if ($Evaluation.Ready) { 'RELEASE READY' } else { 'NOT READY' }
    [void]$sb.AppendLine("## LogAnalyzer release gate: $verdict")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Mode: **$Mode** | Version: ``$($Evaluation.Version)`` | Commit: ``$Commit`` | Tests read: $($Evaluation.TestCount) | $($Evaluation.Counts)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| Item | R22 | Policy | Status | Detail |')
    [void]$sb.AppendLine('|---|---|---|---|---|')
    foreach ($r in $Evaluation.Rows) {
        $d = ($r.Detail -replace '\|', '/') -replace "`r?`n", ' '
        [void]$sb.AppendLine("| $($r.Id) | $($r.R22) | $($r.Policy) | **$($r.Status)** | $d |")
    }
    [void]$sb.AppendLine()
    if ($Mode -eq 'Report') { [void]$sb.AppendLine('Report mode: this matrix is informational and does not fail the build. A release is only possible when every item is PASS or WAIVED (enforce mode).') }
    $sb.ToString()
}
