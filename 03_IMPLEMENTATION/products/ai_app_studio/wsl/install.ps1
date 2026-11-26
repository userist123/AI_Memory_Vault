# install.ps1 -- owner setup ("aas setup-wsl"): installs the studio device-host scripts into /opt/aas/bin
# (root:root, scripts 0755, data 0644) of Ubuntu-S9AI-D, verifies every installed file against
# opt-aas-bin/MANIFEST.sha256 through \\wsl.localhost, records the verified hashes in /opt/aas/MANIFEST.sha256
# and creates the studio AVDs (aas-phone, aas-tablet) under $HOME/.aas/avd.
# Fixed argv only; the distro name and the target paths are constants.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Distro = 'Ubuntu-S9AI-D'
$Wsl = Join-Path $env:SystemRoot 'System32\wsl.exe'
$Src = Join-Path $PSScriptRoot 'opt-aas-bin'
$Executables = @('aas-preflight', 'aas-serve', 'aas-ns')

# Nothing from the Windows environment may reach the distro shell (N4).
Remove-Item Env:WSLENV -ErrorAction SilentlyContinue
Remove-Item Env:BASH_ENV -ErrorAction SilentlyContinue
Remove-Item Env:ENV -ErrorAction SilentlyContinue

function Invoke-Wsl([string[]]$Tail) {
    foreach ($t in $Tail) { if ($t -notmatch '^[A-Za-z0-9/._-]+$') { throw "unsafe token: $t" } }
    & $Wsl -d $Distro @Tail
    if ($LASTEXITCODE -ne 0) { throw "wsl failed ($LASTEXITCODE): $($Tail -join ' ')" }
}

$manifest = [ordered]@{}
foreach ($line in Get-Content -LiteralPath (Join-Path $Src 'MANIFEST.sha256')) {
    if ($line -notmatch '^([0-9a-f]{64})  ([A-Za-z0-9._-]+)$') { throw "bad manifest line: $line" }
    $manifest[$Matches[2]] = $Matches[1]
}
foreach ($name in $manifest.Keys) {
    $h = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Src $name)).Hash.ToLower()
    if ($h -ne $manifest[$name]) { throw "source $name does not match MANIFEST.sha256" }
}

$full = (Resolve-Path -LiteralPath $Src).Path
if ($full -notmatch '^([A-Za-z]):\\([A-Za-z0-9\\._-]+)$') { throw "source path has unsafe characters" }
$mnt = '/mnt/' + $Matches[1].ToLower() + '/' + ($Matches[2] -replace '\\', '/')

Invoke-Wsl @('-u', 'root', '--', '/usr/bin/install', '-d', '-o', 'root', '-g', 'root', '-m', '0755', '/opt/aas', '/opt/aas/bin')
foreach ($name in $manifest.Keys) {
    $mode = if ($Executables -contains $name) { '0755' } else { '0644' }
    Invoke-Wsl @('-u', 'root', '--', '/usr/bin/install', '-o', 'root', '-g', 'root', '-m', $mode, "$mnt/$name", "/opt/aas/bin/$name")
}
Invoke-Wsl @('-u', 'root', '--', '/usr/bin/install', '-o', 'root', '-g', 'root', '-m', '0644', "$mnt/MANIFEST.sha256", '/opt/aas/MANIFEST.sha256')

$unc = "\\wsl.localhost\$Distro\opt\aas\bin"
foreach ($name in $manifest.Keys) {
    $p = Join-Path $unc $name
    $item = Get-Item -LiteralPath $p -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "$name is a reparse point" }
    $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $p).Hash.ToLower()
    if ($h -ne $manifest[$name]) { throw "installed $name does not match MANIFEST.sha256" }
    Write-Output ("installed {0}  {1}" -f $h, $name)
}

$setup = ($mnt -replace '/opt-aas-bin$', '') + '/setup-avd.sh'
Invoke-Wsl @('--', '/bin/bash', '-p', $setup)
Write-Output 'aas setup-wsl: done'
