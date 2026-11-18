# Offline self-test of eng/Sbom.psm1 (no dotnet, no network): fake package folders, a minimal BOM, every check must hold.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '..\Sbom.psm1') -Force

$failures = [Collections.Generic.List[string]]::new()
function Check([bool] $condition, [string] $name) {
    if ($condition) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $failures.Add($name) }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ("sbom-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    # Package with a licence expression and a NuGet .nupkg.sha512.
    $expr = Join-Path $root 'expr'; New-Item -ItemType Directory $expr | Out-Null
    Set-Content -LiteralPath (Join-Path $expr 'pkg.nuspec') -Value '<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>Pkg</id><license type="expression">MIT</license></metadata></package>'
    $digest = [byte[]](1..64)
    Set-Content -LiteralPath (Join-Path $expr 'pkg.1.0.0.nupkg.sha512') -Value ([Convert]::ToBase64String($digest)) -NoNewline
    # Package with a compound expression.
    $compound = Join-Path $root 'compound'; New-Item -ItemType Directory $compound | Out-Null
    Set-Content -LiteralPath (Join-Path $compound 'c.nuspec') -Value '<package><metadata><license type="expression">MIT OR Apache-2.0</license></metadata></package>'
    # Package with a licence file.
    $file = Join-Path $root 'file'; New-Item -ItemType Directory $file | Out-Null
    Set-Content -LiteralPath (Join-Path $file 'f.nuspec') -Value '<package><metadata><license type="file">LICENSE.md</license></metadata></package>'
    Set-Content -LiteralPath (Join-Path $file 'LICENSE.md') -Value 'Licence text' -NoNewline
    # Package with no licence at all, and with a licence file that is missing.
    $none = Join-Path $root 'none'; New-Item -ItemType Directory $none | Out-Null
    Set-Content -LiteralPath (Join-Path $none 'n.nuspec') -Value '<package><metadata><id>N</id></metadata></package>'
    $missing = Join-Path $root 'missing'; New-Item -ItemType Directory $missing | Out-Null
    Set-Content -LiteralPath (Join-Path $missing 'm.nuspec') -Value '<package><metadata><license type="file">LICENSE.txt</license></metadata></package>'

    $l = Get-NuspecLicense -PackageDirectory $expr
    Check ($l.Count -eq 1 -and $l[0].license.id -eq 'MIT') 'expression -> license.id'
    $l = Get-NuspecLicense -PackageDirectory $compound
    Check ($l.Count -eq 1 -and $l[0].expression -eq 'MIT OR Apache-2.0') 'compound expression -> expression'
    $l = Get-NuspecLicense -PackageDirectory $file
    Check ($l.Count -eq 1 -and [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($l[0].license.text.content)) -eq 'Licence text') 'licence file -> embedded text'
    Check ($null -eq (Get-NuspecLicense -PackageDirectory $none)) 'no licence -> null'
    Check ($null -eq (Get-NuspecLicense -PackageDirectory $missing)) 'missing licence file -> null'
    Check ($null -eq (Get-NuspecLicense -PackageDirectory (Join-Path $root 'absent'))) 'no nuspec -> null'
    Check ((Get-NupkgSha512 -PackageDirectory $expr) -eq [Convert]::ToHexString($digest)) 'nupkg.sha512 base64 -> upper hex'
    Check ($null -eq (Get-NupkgSha512 -PackageDirectory $file)) 'no nupkg.sha512 -> null'

    function New-TestBom {
        '{"bomFormat":"CycloneDX","specVersion":"1.7","serialNumber":"urn:uuid:00000000-0000-0000-0000-000000000001","version":1,
          "metadata":{"timestamp":"2030-01-01T00:00:00Z","component":{"type":"application","bom-ref":"App@1.0","name":"App","version":"1.0"}},
          "components":[{"type":"library","bom-ref":"pkg:nuget/Zeta@1","name":"Zeta","version":"1","hashes":[{"alg":"SHA-512","content":"AA"}],"licenses":[{"license":{"id":"MIT"}}]},
                        {"type":"library","bom-ref":"pkg:nuget/Alpha@1","name":"Alpha","version":"1","hashes":[{"alg":"SHA-512","content":"BB"}],"licenses":[{"license":{"id":"MIT"}}]}],
          "dependencies":[{"ref":"pkg:nuget/Zeta@1","dependsOn":[]},{"ref":"App@1.0","dependsOn":["pkg:nuget/Zeta@1","pkg:nuget/Alpha@1"]}]}' | ConvertFrom-Json
    }

    $runtime = New-RuntimeComponent -Id 'Microsoft.NETCore.App.Runtime.win-x64' -Version '10.0.1' -Licenses (Get-NuspecLicense -PackageDirectory $expr) `
        -Hashes @([ordered]@{ alg = 'SHA-512'; content = 'CC' }) -Properties @{ 'la:b' = '2'; 'la:a' = '1' }
    Check ($runtime.purl -eq 'pkg:nuget/Microsoft.NETCore.App.Runtime.win-x64@10.0.1' -and $runtime.'bom-ref' -eq $runtime.purl -and $runtime.type -eq 'framework') 'runtime component purl/bom-ref/type'
    Check (($runtime.properties | ForEach-Object { $_.name }) -join ',' -eq 'la:a,la:b') 'runtime component properties sorted'

    $bom = New-TestBom
    Add-SbomComponents -Bom $bom -Components @($runtime, $runtime)
    Check (@($bom.components).Count -eq 3) 'added once (duplicate bom-ref skipped)'
    $rootDeps = (@($bom.dependencies) | Where-Object ref -eq 'App@1.0').dependsOn
    Check (@($rootDeps) -contains $runtime.'bom-ref') 'root depends on the added runtime'

    Set-SbomReproducible -Bom $bom -Timestamp ([DateTimeOffset]::Parse('2026-11-18T19:47:31+02:00', [Globalization.CultureInfo]::InvariantCulture))
    Check (-not $bom.PSObject.Properties['serialNumber']) 'serial number removed'
    Check ($bom.metadata.timestamp -eq '2026-11-18T17:47:31Z') 'timestamp = commit time in UTC'
    Check ((@($bom.components) | ForEach-Object { $_.name }) -join ',' -eq 'Alpha,Microsoft.NETCore.App.Runtime.win-x64,Zeta') 'components sorted by bom-ref'
    Check ((@($bom.dependencies) | ForEach-Object { $_.ref }) -join ',' -eq 'App@1.0,pkg:nuget/Zeta@1') 'dependencies sorted by ref'
    Check ((@(@($bom.dependencies)[0].dependsOn)) -join ',' -eq 'pkg:nuget/Alpha@1,pkg:nuget/Microsoft.NETCore.App.Runtime.win-x64@10.0.1,pkg:nuget/Zeta@1') 'dependsOn sorted'

    # Same input twice gives the same bytes; input order does not matter.
    $a = New-TestBom; Add-SbomComponents -Bom $a -Components @($runtime); Set-SbomReproducible -Bom $a -Timestamp ([DateTimeOffset]'2026-01-01Z')
    $b = New-TestBom; $b.components = @($b.components[1], $b.components[0]); Add-SbomComponents -Bom $b -Components @($runtime); Set-SbomReproducible -Bom $b -Timestamp ([DateTimeOffset]'2026-01-01Z')
    Check ((ConvertTo-SbomJson -Bom $a) -ceq (ConvertTo-SbomJson -Bom $b)) 'reproducible: same bytes whatever the input order'
    $o = New-TestBom; $o.components = @($o.components[0], $o.components[1]); $o.components[0].'bom-ref' = 'pkg:nuget/b@1'; $o.components[1].'bom-ref' = 'pkg:nuget/C@1'
    Set-SbomReproducible -Bom $o -Timestamp ([DateTimeOffset]'2026-01-01Z')
    Check ((@($o.components) | ForEach-Object { $_.'bom-ref' }) -join ',' -eq 'pkg:nuget/C@1,pkg:nuget/b@1') 'ordinal order (upper-case before lower-case), not culture order'
    Check (-not ((ConvertTo-SbomJson -Bom $a).Contains("`r"))) 'LF line endings'

    $ok = Test-SbomComplete -Bom $a -ExpectedVersion '1.0' -RequiredComponents @('Microsoft.NETCore.App.Runtime.win-x64')
    Check ($ok.Count -eq 0) 'complete BOM -> no problems'
    $p = Test-SbomComplete -Bom $a -ExpectedVersion '2.0' -RequiredComponents @('Microsoft.WindowsDesktop.App.Runtime.win-x64')
    Check ($p.Count -eq 2 -and ($p -join '|') -match "așteptat '2.0'" -and ($p -join '|') -match 'WindowsDesktop') 'wrong version and missing runtime reported'
    $c = New-TestBom
    $c.components[0].PSObject.Properties.Remove('licenses')
    $c.components[1].hashes = @()
    $p = Test-SbomComplete -Bom $c -ExpectedVersion '1.0'
    Check ($p.Count -eq 3 -and ($p -join '|') -match 'Zeta 1 nu are licență' -and ($p -join '|') -match 'Alpha 1 nu are hash' -and ($p -join '|') -match 'serialNumber') 'missing licence, missing hash and serial number reported'
    $d = New-TestBom; $d.components[0].licenses = @(); $d.components[1].hashes = @([pscustomobject]@{ alg = 'SHA-512'; content = '' })
    $p = Test-SbomComplete -Bom $d -ExpectedVersion '1.0'
    Check (($p -join '|') -match 'Zeta 1 nu are licență' -and ($p -join '|') -match 'Alpha 1 nu are hash') 'empty licence list and empty hash content reported'

    # Runtime pack selection: referenced frameworks only (direct or through AdditionalFrameworkReferences); an uncovered framework is reported.
    $resolved = @(
        [pscustomobject]@{ NuGetPackageId = 'Microsoft.NETCore.App.Runtime.win-x64'; FrameworkName = 'Microsoft.NETCore.App' },
        [pscustomobject]@{ NuGetPackageId = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'; FrameworkName = 'Microsoft.WindowsDesktop.App'; AdditionalFrameworkReferences = 'Microsoft.WindowsDesktop.App.WPF;Microsoft.WindowsDesktop.App.WindowsForms' },
        [pscustomobject]@{ NuGetPackageId = 'Microsoft.AspNetCore.App.Runtime.win-x64'; FrameworkName = 'Microsoft.AspNetCore.App' })
    $sel = Select-ShippedRuntimePacks -Frameworks @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App.WPF') -ResolvedPacks $resolved
    Check ((@($sel.Packs | ForEach-Object NuGetPackageId) -join ',') -eq 'Microsoft.NETCore.App.Runtime.win-x64,Microsoft.WindowsDesktop.App.Runtime.win-x64' -and $sel.Uncovered.Count -eq 0) 'packs: referenced only (WPF via AdditionalFrameworkReferences), resolved-but-unused AspNetCore left out'
    $sel = Select-ShippedRuntimePacks -Frameworks @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App.WPF') -ResolvedPacks @($resolved[0], $resolved[2])
    Check ($sel.Uncovered.Count -eq 1 -and $sel.Uncovered[0] -eq 'Microsoft.WindowsDesktop.App.WPF') 'packs: a referenced framework no pack covers is reported (incomplete inventory)'
    $sel = Select-ShippedRuntimePacks -Frameworks @('Microsoft.NETCore.App') -ResolvedPacks @($resolved[0], $resolved[0])
    Check (@($sel.Packs).Count -eq 1) 'packs: duplicate pack listed once'
} finally {
    Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host "::error::Test-Sbom: $($failures.Count) verificări au eșuat: $($failures -join '; ')"; exit 1 }
Write-Host 'Test-Sbom: toate verificările au trecut'
