#Requires -RunAsAdministrator
# $LogFile oracle volume (2026-11-14): fresh 64 MB NTFS VHDX, USN journal on, a short list of KNOWN operations right after format (so the
# circular log has not wrapped), each with its time and file id recorded from Windows (not from LogAnalyzer). Then $LogFile, $MFT, the boot
# sector and the USN export are copied by the extents Windows reports.
param([string]$Out = "$PSScriptRoot\logcalib")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$vhd = "$Out\logcalib.vhdx"
if (Test-Path $vhd) { try { Dismount-VHD $vhd } catch {}; Remove-Item $vhd -Force }
New-VHD -Path $vhd -SizeBytes 64MB -Fixed | Out-Null
$disk = Mount-VHD -Path $vhd -Passthru | Get-Disk
Initialize-Disk -Number $disk.Number -PartitionStyle GPT | Out-Null
$part = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
Format-Volume -Partition $part -FileSystem NTFS -NewFileSystemLabel LALOG -Confirm:$false | Out-Null
$d = "$($part.DriveLetter):"
fsutil usn createjournal m=0x800000 a=0x100000 $d | Out-Null
$ops = [System.Collections.Generic.List[object]]::new()
function Fid($p) { (fsutil file queryfileid $p) -replace '.*0x', '0x' }
function Op($kind, $path, $note) { $script:ops.Add([ordered]@{ seq = $script:ops.Count + 1; kind = $kind; path = $path.Substring(2); utc = (Get-Date).ToUniversalTime().ToString('o'); fileId = $(if (Test-Path -LiteralPath $path) { Fid $path } else { '' }); note = $note }) }
Start-Sleep 2
New-Item -ItemType Directory "$d\work" | Out-Null; Op 'mkdir' "$d\work" ''
Set-Content "$d\work\created.txt" 'TEST created' -Encoding ascii; Op 'create' "$d\work\created.txt" 'resident data'
Start-Sleep 2
Set-Content "$d\work\stomp.txt" 'TEST stomp' -Encoding ascii; Op 'create' "$d\work\stomp.txt" ''
Start-Sleep 2
$i = Get-Item "$d\work\stomp.txt"; $i.CreationTimeUtc = [datetime]::new(2019, 3, 14, 9, 26, 53, [DateTimeKind]::Utc); Op 'timestomp' "$d\work\stomp.txt" 'SetFileTime creation only -> 2019-03-14T09:26:53Z'
Start-Sleep 2
Set-Content "$d\work\before.txt" 'TEST rename' -Encoding ascii; Op 'create' "$d\work\before.txt" ''
Start-Sleep 2
Rename-Item "$d\work\before.txt" 'after.txt'; Op 'rename' "$d\work\after.txt" 'from before.txt'
Start-Sleep 2
Set-Content "$d\work\deleted.txt" 'TEST delete' -Encoding ascii; $delId = Fid "$d\work\deleted.txt"; Op 'create' "$d\work\deleted.txt" ''
Start-Sleep 2
Remove-Item "$d\work\deleted.txt"; $ops.Add([ordered]@{ seq = $ops.Count + 1; kind = 'delete'; path = '\work\deleted.txt'; utc = (Get-Date).ToUniversalTime().ToString('o'); fileId = $delId; note = '' })
Start-Sleep 2
[IO.File]::WriteAllBytes("$d\work\big.bin", (New-Object byte[] 200000)); Op 'create' "$d\work\big.bin" 'non-resident 200000 bytes'
Write-VolumeCache -DriveLetter $part.DriveLetter
$ops | ConvertTo-Json -Depth 4 | Out-File "$Out\ops.json" -Encoding utf8
fsutil usn readjournal $d csv | Out-File "$Out\usn.csv" -Encoding utf8
fsutil fsinfo ntfsinfo $d | Out-File "$Out\ntfsinfo.txt" -Encoding utf8
$info = Get-Content "$Out\ntfsinfo.txt"
$bpc = [int]([regex]::Match((($info | Select-String 'Bytes Per Cluster').Line -replace '.*:\s*', ''), '\d+').Value)
$vol = [IO.File]::Open("\\.\$d", 'Open', 'Read', 'ReadWrite')
$b = New-Object byte[] 512; [void]$vol.Read($b, 0, 512); [IO.File]::WriteAllBytes("$Out\boot.bin", $b)
foreach ($pair in @(@('$LogFile', 'logfile.bin'), @('$MFT', 'mft.bin'))) {
  $ext = fsutil file queryextents "$d\$($pair[0])"; $ext | Out-File "$Out\$($pair[1]).extents.txt"
  $fs = [IO.File]::Create("$Out\$($pair[1])")
  foreach ($line in $ext) {
    if ($line -match 'VCN:\s*(0x[0-9a-f]+)\s+Clusters:\s*(0x[0-9a-f]+)\s+LCN:\s*(0x[0-9a-f]+)') {
      $n = [Convert]::ToInt64($Matches[2], 16); $lcn = [Convert]::ToInt64($Matches[3], 16); $buf = New-Object byte[] ($n * $bpc)
      $vol.Position = $lcn * $bpc; $r = 0; while ($r -lt $buf.Length) { $k = $vol.Read($buf, $r, $buf.Length - $r); if ($k -le 0) { throw 'short' }; $r += $k }
      $fs.Write($buf, 0, $buf.Length) } }
  $fs.Close()
}
$vol.Close()
Dismount-VHD $vhd
Get-ChildItem $Out | Select-Object Name, Length | Format-Table | Out-String | Out-File "$Out\done.txt"
