#Requires -RunAsAdministrator
# fsutil refuses the extents of $LogFile (Access denied). Read it by an INDEPENDENT decoder (PowerShell, not the C# reader under test):
# boot -> MFT record 2 ($LogFile) -> unnamed $DATA runs -> raw copy. The VHDX is mounted read-only.
param([string]$Out = "$PSScriptRoot\logcalib")
$ErrorActionPreference = 'Stop'
$vhd = "$Out\logcalib.vhdx"
$disk = Mount-VHD -Path $vhd -ReadOnly -Passthru | Get-Disk
try {
  $part = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.Type -eq 'Basic' -or $_.GptType -eq '{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}' } | Select-Object -First 1
  if (-not $part.DriveLetter) { $part | Add-PartitionAccessPath -AssignDriveLetter; $part = Get-Partition -DiskNumber $disk.Number -PartitionNumber $part.PartitionNumber }
  $h = [IO.File]::Open("\\.\$($part.DriveLetter):", 'Open', 'Read', 'ReadWrite')
  $boot = New-Object byte[] 512; [void]$h.Read($boot, 0, 512)
  $cs = [BitConverter]::ToUInt16($boot, 11) * $boot[13]; $mftLcn = [BitConverter]::ToInt64($boot, 48)
  $c = [int]$boot[64]; if ($c -gt 127) { $c -= 256 }; $rs = if ($c -lt 0) { 1 -shl (-$c) } else { $c * $cs }
  $rec = New-Object byte[] $rs; $h.Position = $mftLcn * $cs + 2 * $rs; [void]$h.Read($rec, 0, $rs)
  if ([Text.Encoding]::ASCII.GetString($rec, 0, 4) -ne 'FILE') { throw 'record 2 is not FILE' }
  $uo = [BitConverter]::ToUInt16($rec, 4); $uc = [BitConverter]::ToUInt16($rec, 6); $usn = [BitConverter]::ToUInt16($rec, $uo)
  for ($i = 1; $i -lt $uc; $i++) { if ([BitConverter]::ToUInt16($rec, $i * 512 - 2) -ne $usn) { throw "fixup $i" } }
  for ($i = 1; $i -lt $uc; $i++) { $rec[$i * 512 - 2] = $rec[$uo + 2 * $i]; $rec[$i * 512 - 1] = $rec[$uo + 2 * $i + 1] }
  $p = [BitConverter]::ToUInt16($rec, 20); $runs = @(); $size = -1
  while ($p + 8 -le $rs) {
    $t = [BitConverter]::ToUInt32($rec, $p); $len = [BitConverter]::ToUInt32($rec, $p + 4); if ($t -eq 0xFFFFFFFF -or $len -eq 0) { break }
    if ($t -eq 0x80 -and $rec[$p + 8] -eq 1 -and $rec[$p + 9] -eq 0) {
      $size = [BitConverter]::ToInt64($rec, $p + 48); $q = $p + [BitConverter]::ToUInt16($rec, $p + 32); $cur = 0L
      while ($rec[$q] -ne 0) { $hb = $rec[$q]; $lv = $hb -band 0xF; $lo = $hb -shr 4; $q++
        $n = 0L; for ($k = 0; $k -lt $lv; $k++) { $n = $n -bor ([long]$rec[$q + $k] -shl (8 * $k)) }; $q += $lv
        $off = 0L; for ($k = 0; $k -lt $lo; $k++) { $off = $off -bor ([long]$rec[$q + $k] -shl (8 * $k)) }
        if ($lo -gt 0 -and ($rec[$q + $lo - 1] -band 0x80)) { $off -= ([long]1 -shl (8 * $lo)) }; $q += $lo; $cur += $off; $runs += , @($cur, $n) } }
    $p += $len }
  $fs = [IO.File]::Create("$Out\logfile.bin"); $left = $size
  foreach ($r in $runs) { $buf = New-Object byte[] ([Math]::Min($left, $r[1] * $cs)); $h.Position = $r[0] * $cs; $got = 0
    while ($got -lt $buf.Length) { $k = $h.Read($buf, $got, $buf.Length - $got); if ($k -le 0) { throw 'short' }; $got += $k }
    $fs.Write($buf, 0, $buf.Length); $left -= $buf.Length; if ($left -le 0) { break } }
  $fs.Close(); $h.Close()
  "size=$size runs=$($runs.Count) first=$($runs[0] -join ',') clusterSize=$cs" | Out-File "$Out\logfile.txt"
} finally { Dismount-VHD $vhd }
