# Completes the CycloneDX SBOM that dotnet-CycloneDX writes for one edition (accreditation SW-03, WP-ACR-08).
#
# dotnet-CycloneDX lists the NuGet packages from the lock files. A self-contained single-file edition also ships the .NET runtime
# packs (Microsoft.NETCore.App, Microsoft.WindowsDesktop.App) and the single-file host inside the executable; the tool does not list
# them. This module adds them with version, licence and hash read from the packs themselves, makes the document reproducible
# (no serial number, timestamp = commit time, components sorted) and refuses a document with a component lacking a licence or a hash.
#
# No network access: every value comes from the NuGet cache or the SDK packs folder on the build machine.

Set-StrictMode -Version Latest

function Get-NuspecLicense {
    # Licence of a NuGet package, read from its .nuspec. Returns a CycloneDX licences array or $null.
    param([Parameter(Mandatory)][string] $PackageDirectory)
    $nuspec = Get-ChildItem -Path $PackageDirectory -Filter *.nuspec -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $nuspec) { return $null }
    [xml] $doc = Get-Content -Raw -LiteralPath $nuspec.FullName
    $node = $doc.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    if (-not $node) { return $null }
    $type = $node.GetAttribute('type')
    $value = $node.InnerText.Trim()
    if (-not $value) { return $null }
    if ($type -eq 'expression') {
        if ($value -match '^[A-Za-z0-9.\-+]+$') { return , @([ordered]@{ license = [ordered]@{ id = $value } }) }
        return , @([ordered]@{ expression = $value })
    }
    if ($type -eq 'file') {
        $file = Join-Path $PackageDirectory $value
        if (-not (Test-Path -LiteralPath $file)) { return $null }
        $bytes = [IO.File]::ReadAllBytes($file)
        return , @([ordered]@{ license = [ordered]@{
            name = "$($nuspec.BaseName) licence ($value)"
            text = [ordered]@{ contentType = 'text/plain'; encoding = 'base64'; content = [Convert]::ToBase64String($bytes) }
        } })
    }
    return $null
}

function Get-NupkgSha512 {
    # SHA-512 of the .nupkg as NuGet recorded it (<id>.<version>.nupkg.sha512, base64), in the upper-case hex form dotnet-CycloneDX uses.
    param([Parameter(Mandatory)][string] $PackageDirectory)
    $file = Get-ChildItem -Path $PackageDirectory -Filter *.nupkg.sha512 -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $file) { return $null }
    $bytes = [Convert]::FromBase64String((Get-Content -Raw -LiteralPath $file.FullName).Trim())
    return [Convert]::ToHexString($bytes)
}

function New-RuntimeComponent {
    # A component for a runtime or host pack shipped inside the executable.
    param(
        [Parameter(Mandatory)][string] $Id,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)] $Licenses,
        [Parameter(Mandatory)] $Hashes,
        [string] $Description,
        [hashtable] $Properties = @{}
    )
    $purl = "pkg:nuget/$Id@$Version"
    $c = [ordered]@{
        type        = 'framework'
        'bom-ref'   = $purl
        name        = $Id
        version     = $Version
        scope       = 'required'
        hashes      = @($Hashes)
        licenses    = @($Licenses)
        purl        = $purl
    }
    if ($Description) { $c.description = $Description }
    if ($Properties.Count -gt 0) {
        $c.properties = @($Properties.GetEnumerator() | Sort-Object Name | ForEach-Object { [ordered]@{ name = $_.Name; value = [string]$_.Value } })
    }
    return $c
}

function Add-SbomComponents {
    # Adds components (skipping a bom-ref already present) and makes the metadata component depend on them.
    param([Parameter(Mandatory)] $Bom, [Parameter(Mandatory)] [object[]] $Components)
    $existing = @{}
    foreach ($c in @($Bom.components)) { $existing[$c.'bom-ref'] = $true }
    $list = [Collections.Generic.List[object]]::new()
    foreach ($c in @($Bom.components)) { $list.Add($c) }
    $added = @()
    foreach ($c in $Components) {
        if ($existing.ContainsKey($c.'bom-ref')) { continue }
        $list.Add([pscustomobject]$c)
        $existing[$c.'bom-ref'] = $true
        $added += $c.'bom-ref'
    }
    $Bom.components = $list.ToArray()
    $rootRef = $Bom.metadata.component.'bom-ref'
    $root = @($Bom.dependencies) | Where-Object { $_.ref -eq $rootRef } | Select-Object -First 1
    if ($root) {
        $root.dependsOn = @(@($root.dependsOn) + $added)
    } else {
        $Bom.dependencies = @(@($Bom.dependencies) + [pscustomobject]@{ ref = $rootRef; dependsOn = @($added) })
    }
}

function Set-SbomReproducible {
    # Same input, same bytes: no serial number, timestamp = the commit time (UTC), components, dependencies and dependsOn sorted.
    param([Parameter(Mandatory)] $Bom, [Parameter(Mandatory)][DateTimeOffset] $Timestamp)
    if ($Bom.PSObject.Properties['serialNumber']) { $Bom.PSObject.Properties.Remove('serialNumber') }
    $Bom.metadata.timestamp = $Timestamp.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
    $Bom.components = Get-OrdinalSorted -Items @($Bom.components) -Key { param($c) $c.'bom-ref' }
    foreach ($d in @($Bom.dependencies)) {
        if ($d.PSObject.Properties['dependsOn'] -and $d.dependsOn) {
            $d.dependsOn = Get-OrdinalSorted -Items @($d.dependsOn | Select-Object -Unique) -Key { param($s) [string]$s }
        }
    }
    $Bom.dependencies = Get-OrdinalSorted -Items @($Bom.dependencies) -Key { param($d) $d.ref }
}

function Get-OrdinalSorted {
    # Ordinal (byte-wise) order, the same on every machine and culture; stable for equal keys.
    param([object[]] $Items, [scriptblock] $Key)
    $keyed = for ($i = 0; $i -lt $Items.Count; $i++) { [pscustomobject]@{ K = [string](& $Key $Items[$i]); I = $i; V = $Items[$i] } }
    $list = [Collections.Generic.List[object]]::new()
    foreach ($k in @($keyed)) { $list.Add($k) }
    $list.Sort([Comparison[object]] { param($x, $y) $c = [string]::CompareOrdinal($x.K, $y.K); if ($c -ne 0) { $c } else { $x.I.CompareTo($y.I) } })
    return , @($list | ForEach-Object { $_.V })
}

function Select-ShippedRuntimePacks {
    # The runtime packs a self-contained publish ships: those whose FrameworkName, or one of whose AdditionalFrameworkReferences, is a framework the
    # project references. Returns Packs (unique by package id) and Uncovered (referenced frameworks no pack covers: the inventory would be incomplete).
    param([string[]] $Frameworks = @(), [object[]] $ResolvedPacks = @())
    $covered = @{}
    $packs = @()
    foreach ($p in $ResolvedPacks) {
        $afr = $p.PSObject.Properties['AdditionalFrameworkReferences']
        $names = @($p.FrameworkName) + @((($(if ($afr) { [string]$afr.Value } else { '' })) -split ';') | Where-Object { $_ })
        $hits = @($names | Where-Object { $Frameworks -contains $_ })
        if ($hits.Count -eq 0) { continue }
        foreach ($h in $hits) { $covered[$h] = $true }
        if (-not @($packs | Where-Object { $_.NuGetPackageId -eq $p.NuGetPackageId })) { $packs += $p }
    }
    $uncovered = @($Frameworks | Where-Object { -not $covered.ContainsKey($_) })
    return [pscustomobject]@{ Packs = @($packs); Uncovered = $uncovered }
}

function Test-SbomComplete {
    # Returns the list of problems (empty = complete).
    param(
        [Parameter(Mandatory)] $Bom,
        [Parameter(Mandatory)][string] $ExpectedVersion,
        [string[]] $RequiredComponents = @()
    )
    $problems = [Collections.Generic.List[string]]::new()
    if ($Bom.metadata.component.version -ne $ExpectedVersion) {
        $problems.Add("versiunea ediției în SBOM este '$($Bom.metadata.component.version)', așteptat '$ExpectedVersion'")
    }
    if ($Bom.PSObject.Properties['serialNumber']) { $problems.Add('SBOM-ul are serialNumber (nu e reproductibil)') }
    $names = @{}
    foreach ($c in @($Bom.components)) {
        $names[$c.name] = $true
        $hasLicence = $c.PSObject.Properties['licenses'] -and @($c.licenses).Count -gt 0
        if (-not $hasLicence) { $problems.Add("componenta $($c.name) $($c.version) nu are licență") }
        $hasHash = $c.PSObject.Properties['hashes'] -and @($c.hashes | Where-Object { $_.content }).Count -gt 0
        if (-not $hasHash) { $problems.Add("componenta $($c.name) $($c.version) nu are hash") }
    }
    foreach ($r in $RequiredComponents) {
        if (-not $names.ContainsKey($r)) { $problems.Add("lipsește componenta obligatorie $r") }
    }
    return , $problems.ToArray()
}

function ConvertTo-SbomJson {
    param([Parameter(Mandatory)] $Bom)
    return ($Bom | ConvertTo-Json -Depth 64) -replace "`r`n", "`n"
}

Export-ModuleMember -Function Get-OrdinalSorted, Select-ShippedRuntimePacks, Get-NuspecLicense, Get-NupkgSha512, New-RuntimeComponent, Add-SbomComponents, Set-SbomReproducible, Test-SbomComplete, ConvertTo-SbomJson
