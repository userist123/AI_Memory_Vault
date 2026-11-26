<#
.SYNOPSIS
  Writes the CycloneDX SBOM of one edition (accreditation SW-03, WP-ACR-08) and, with -PackageDirectory, puts it in the package
  next to a SHA256SUMS manifest of every file in the package.

.DESCRIPTION
  1. dotnet-CycloneDX (pinned in .config/dotnet-tools.json) lists the NuGet packages from the lock files (build-only packages such as
     ILLink stay listed with scope "excluded"), with the edition's version (DfirInfo.ApplicationVersion) and the licence text of packages that ship a licence file.
  2. The .NET runtime packs and the single-file host that `dotnet publish` puts inside the self-contained executable are added,
     with version, licence (from the pack's .nuspec) and hash (NuGet's .nupkg SHA-512; for the host, the SHA-256 of singlefilehost.exe).
  3. The document is made reproducible (no serial number, timestamp = commit time, ordinal sort) and refused if any component lacks a licence
     or a hash, or if a framework the project references is covered by no runtime pack.

  No network: run after `dotnet restore` / `dotnet publish` of the same project.

.EXAMPLE
  ./eng/New-EditionSbom.ps1 -Project LogAnalyzer.App.Classified/LogAnalyzer.App.Classified.csproj -Edition LogAnalyzer.Standalone `
      -OutputDirectory sbom -PackageDirectory publish/LogAnalyzer.Standalone/win-x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Project,
    [Parameter(Mandatory)][string] $Edition,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $PackageDirectory,
    [string] $RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$raw = $null
Import-Module (Join-Path $PSScriptRoot 'Sbom.psm1') -Force

$workspace = Split-Path $PSScriptRoot -Parent
Push-Location $workspace
try {
    # Edition version: the same constant the application records in every case (DfirInfo.ApplicationVersion).
    $caseInfo = Get-Content -Raw (Join-Path $workspace 'LogAnalyzer.Dfir.Core/Model/CaseInfo.cs')
    $m = [regex]::Match($caseInfo, 'public const string ApplicationVersion\s*=\s*"([^"]+)"')
    if (-not $m.Success) { throw 'DfirInfo.ApplicationVersion nu a fost găsit în CaseInfo.cs' }
    $version = $m.Groups[1].Value

    # Timestamp: the commit time (SOURCE_DATE_EPOCH wins when set), so two builds of one commit give the same bytes.
    if ($env:SOURCE_DATE_EPOCH) {
        $timestamp = [DateTimeOffset]::FromUnixTimeSeconds([long]$env:SOURCE_DATE_EPOCH)
    } else {
        $iso = (git log -1 --format=%cI 2>$null)
        if ($LASTEXITCODE -ne 0 -or -not $iso) { throw 'Nu pot citi ora commit-ului (git log); setați SOURCE_DATE_EPOCH' }
        $timestamp = [DateTimeOffset]::Parse($iso.Trim(), [Globalization.CultureInfo]::InvariantCulture)
    }

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $raw = Join-Path ([IO.Path]::GetTempPath()) ("sbom-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $raw | Out-Null

    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore a eșuat' }
    dotnet tool run dotnet-CycloneDX $Project --recursive --no-serial-number --include-license-text `
        --runtime $RuntimeIdentifier --configuration Release --disable-package-restore `
        --set-name $Edition --set-version $version --output $raw --filename bom.json --output-format Json | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $raw 'bom.json'))) { throw 'dotnet-CycloneDX nu a scris SBOM-ul' }
    $bom = Get-Content -Raw (Join-Path $raw 'bom.json') | ConvertFrom-Json

    # Runtime packs and the single-file host, exactly as the SDK resolves them for this publish.
    $query = dotnet msbuild $Project -t:ResolveFrameworkReferences -p:Configuration=Release -p:RuntimeIdentifier=$RuntimeIdentifier `
        -p:SelfContained=true -p:PublishSingleFile=true `
        -getItem:ResolvedFrameworkReference -getItem:ResolvedRuntimePack -getItem:ResolvedSingleFileHostPack 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet msbuild (ResolveFrameworkReferences) a eșuat' }
    $items = ($query | Out-String | ConvertFrom-Json).Items

    $frameworks = @($items.ResolvedFrameworkReference | ForEach-Object { $_.Identity })
    if ($frameworks.Count -eq 0) { throw 'Niciun framework referit rezolvat pentru publicarea self-contained' }
    $selection = Select-ShippedRuntimePacks -Frameworks $frameworks -ResolvedPacks @($items.ResolvedRuntimePack)
    # every referenced framework must be covered by a pack, else the executable ships code the SBOM does not list
    if ($selection.Uncovered.Count -gt 0) { throw "Framework-uri referite fără runtime pack în SBOM: $($selection.Uncovered -join ', ')" }
    $packs = @($selection.Packs)

    $added = @()
    $required = @()
    $coreLicense = $null
    $coreVersion = $null
    foreach ($p in $packs) {
        $licenses = Get-NuspecLicense -PackageDirectory $p.PackageDirectory
        $sha512 = Get-NupkgSha512 -PackageDirectory $p.PackageDirectory
        if (-not $licenses) { throw "Licența pentru $($p.NuGetPackageId) nu a fost găsită în $($p.PackageDirectory)" }
        if (-not $sha512) { throw "Hash-ul .nupkg pentru $($p.NuGetPackageId) nu a fost găsit în $($p.PackageDirectory)" }
        if ($p.FrameworkName -eq 'Microsoft.NETCore.App') { $coreLicense = $licenses; $coreVersion = $p.NuGetPackageVersion }
        $added += New-RuntimeComponent -Id $p.NuGetPackageId -Version $p.NuGetPackageVersion -Licenses $licenses `
            -Hashes @([ordered]@{ alg = 'SHA-512'; content = $sha512 }) `
            -Description "Runtime $($p.FrameworkName), inclus în executabilul self-contained"
        $required += $p.NuGetPackageId
    }

    $host_ = @($items.ResolvedSingleFileHostPack) | Select-Object -First 1
    if (-not $host_ -or -not $host_.Path -or -not (Test-Path -LiteralPath $host_.Path)) { throw 'Gazda single-file (singlefilehost.exe) nu a fost rezolvată' }
    $hostDir = $host_.PackageDirectory
    $hostVersion = Split-Path $hostDir -Leaf
    $hostId = "Microsoft.NETCore.App.Host.$RuntimeIdentifier"
    $hostLicenses = Get-NuspecLicense -PackageDirectory $hostDir
    $props = @{ 'la:file' = 'runtimes/' + $RuntimeIdentifier + '/native/singlefilehost.exe' }
    if (-not $hostLicenses) {
        # The host pack bundled with the SDK (dotnet/packs) carries no .nuspec; it is released with the runtime of the same version.
        if (-not $coreLicense) { throw 'Licența gazdei single-file nu poate fi stabilită (lipsește Microsoft.NETCore.App)' }
        if ($hostVersion -ne $coreVersion) { throw "Gazda single-file are versiunea $hostVersion, runtime-ul $($coreVersion): licența nu poate fi preluată" }
        $hostLicenses = $coreLicense
        $props['la:licenseSource'] = "nuspec Microsoft.NETCore.App.Runtime.$RuntimeIdentifier (aceeași lansare; pachetul gazdei din SDK nu are .nuspec)"
    }
    $hostHash = (Get-FileHash -LiteralPath $host_.Path -Algorithm SHA256).Hash
    $added += New-RuntimeComponent -Id $hostId -Version $hostVersion -Licenses $hostLicenses `
        -Hashes @([ordered]@{ alg = 'SHA-256'; content = $hostHash }) -Properties $props `
        -Description 'Gazda single-file .NET, partea nativă a executabilului'
    $required += $hostId

    Add-SbomComponents -Bom $bom -Components $added
    Set-SbomReproducible -Bom $bom -Timestamp $timestamp

    $problems = Test-SbomComplete -Bom $bom -ExpectedVersion $version -RequiredComponents $required
    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "::error::SBOM $Edition - $_" }
        throw "SBOM-ul ediției $Edition este incomplet ($($problems.Count) probleme)"
    }

    $json = ConvertTo-SbomJson -Bom $bom
    $target = Join-Path $OutputDirectory "$Edition.cdx.json"
    [IO.File]::WriteAllText((Resolve-Path $OutputDirectory | Join-Path -ChildPath "$Edition.cdx.json"), $json, [Text.UTF8Encoding]::new($false))
    Write-Host "OK: $target ($(@($bom.components).Count) componente, versiunea $version)"

    if ($PackageDirectory) {
        if (-not (Test-Path -LiteralPath $PackageDirectory)) { throw "Folderul pachetului nu există: $PackageDirectory" }
        $pkg = (Resolve-Path -LiteralPath $PackageDirectory).Path
        Copy-Item -LiteralPath $target -Destination (Join-Path $pkg "$Edition.cdx.json") -Force
        # SHA256SUMS: one line per file in the package, sorted by path, in the `sha256sum -c` format.
        # every file except the top-level manifest itself (a file of that name deeper in the package is listed like any other)
        $entries = @(Get-ChildItem -LiteralPath $pkg -Recurse -File | ForEach-Object {
            [pscustomobject]@{ Rel = [IO.Path]::GetRelativePath($pkg, $_.FullName).Replace('\', '/'); Full = $_.FullName }
        } | Where-Object { $_.Rel -cne 'SHA256SUMS' })
        $sorted = Get-OrdinalSorted -Items $entries -Key { param($e) $e.Rel }
        $lines = @($sorted | ForEach-Object { "$((Get-FileHash -LiteralPath $_.Full -Algorithm SHA256).Hash.ToLowerInvariant()) *$($_.Rel)" })
        [IO.File]::WriteAllText((Join-Path $pkg 'SHA256SUMS'), (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
        Write-Host "OK: SHA256SUMS ($(@($lines).Count) fișiere) și $Edition.cdx.json în $PackageDirectory"
    }
} finally {
    if ($raw -and (Test-Path -LiteralPath $raw)) { Remove-Item -Recurse -Force -LiteralPath $raw -ErrorAction SilentlyContinue }
    Pop-Location
}
