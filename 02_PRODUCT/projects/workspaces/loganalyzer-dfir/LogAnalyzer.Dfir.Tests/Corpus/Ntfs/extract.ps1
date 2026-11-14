#Requires -RunAsAdministrator
# Copies a file's clusters raw from the volume, using the extents Windows reports (fsutil file queryextents),
# so the fixture does not depend on LogAnalyzer's own run decoding.
param([string]$Drive = 'F:', [string]$Out = "$PSScriptRoot\out")
$ErrorActionPreference = 'Stop'
$info = fsutil fsinfo ntfsinfo $Drive
$bpc = [int]([regex]::Match((($info | Select-String 'Bytes Per Cluster').Line -replace '.*:\s*', ''), '\d+').Value)
"bytesPerCluster=$bpc" | Out-File "$Out\extract.log"
$vol = [IO.File]::Open("\\.\$Drive", 'Open', 'Read', 'ReadWrite')
function CopyExtents([string]$path, [string]$dest, [string]$stream) {
  $target = if ($stream) { "$path`:$stream" } else { $path }
  $ext = fsutil file queryextents $target
  $ext | Out-File "$dest.extents.txt"
  $fs = [IO.File]::Create($dest)
  foreach ($line in $ext) {
    if ($line -match 'VCN:\s*(0x[0-9a-f]+)\s+Clusters:\s*(0x[0-9a-f]+)\s+LCN:\s*(0x[0-9a-f]+)') {
      $n = [Convert]::ToInt64($Matches[2], 16); $lcn = [Convert]::ToInt64($Matches[3], 16)
      $buf = New-Object byte[] ($n * $bpc)
      $vol.Position = $lcn * $bpc; $read = 0
      while ($read -lt $buf.Length) { $r = $vol.Read($buf, $read, $buf.Length - $read); if ($r -le 0) { throw 'short read' }; $read += $r }
      $fs.Write($buf, 0, $buf.Length)
    }
  }
  $fs.Close()
}
CopyExtents "$Drive\`$MFT" "$Out\mft.bin" ''
CopyExtents "$Drive\many" "$Out\many_i30.bin" '$I30:$INDEX_ALLOCATION'
# boot sector
$b = New-Object byte[] 512; $vol.Position = 0; [void]$vol.Read($b, 0, 512); [IO.File]::WriteAllBytes("$Out\boot.bin", $b)
# whole raw volume (kept out of the repo; for collector tests)
$img = [IO.File]::Create("$Out\volume.raw"); $vol.Position = 0; $vol.CopyTo($img); $img.Close()
$vol.Close()
Get-ChildItem $Out | Select-Object Name, Length | Format-Table | Out-File -Append "$Out\extract.log"

