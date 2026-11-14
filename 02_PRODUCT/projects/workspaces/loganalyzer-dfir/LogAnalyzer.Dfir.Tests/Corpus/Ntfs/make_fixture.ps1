#Requires -RunAsAdministrator
# Builds a small NTFS volume written by Windows itself, with known scenarios, and records ground truth
# independently of LogAnalyzer code (fsutil + Get-Item). Output: $Out\{volume.vhdx, truth.json, steps.log, layout_*.txt}
param([string]$Out = "$PSScriptRoot\out")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$log = "$Out\steps.log"; "start $(Get-Date -Format o)" | Out-File $log
function L($m) { $m | Out-File -Append $log }

$vhd = "$Out\volume.vhdx"
if (Test-Path $vhd) { try { Dismount-VHD $vhd -ErrorAction SilentlyContinue } catch {} ; Remove-Item $vhd -Force }
New-VHD -Path $vhd -SizeBytes 64MB -Fixed | Out-Null
$disk = Mount-VHD -Path $vhd -Passthru | Get-Disk
Initialize-Disk -Number $disk.Number -PartitionStyle GPT | Out-Null
$part = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
Format-Volume -Partition $part -FileSystem NTFS -NewFileSystemLabel LATEST -Confirm:$false | Out-Null
$d = "$($part.DriveLetter):"
L "drive $d"
fsutil 8dot3name set $d 0 | Out-File -Append $log      # enable short names on this volume only

$truth = [ordered]@{ drive = $d; files = @(); deleted = @(); renamed = @(); stomped = @() }
function Fid($p) { (fsutil file queryfileid $p) -replace '.*0x', '0x' }
function Rec($p, $role) {
  $i = Get-Item -LiteralPath $p -Force
  [ordered]@{ role = $role; path = $p.Substring(2); fileId = (Fid $p); size = $(if ($i.PSIsContainer) { 0 } else { $i.Length });
              siCreated = $i.CreationTimeUtc.ToString('o'); siModified = $i.LastWriteTimeUtc.ToString('o'); siAccessed = $i.LastAccessTimeUtc.ToString('o') }
}

# 1. plain files, a non-resident one, unicode name, long name (gets an 8.3 name)
New-Item -ItemType Directory "$d\docs" | Out-Null
Set-Content -LiteralPath "$d\docs\small.txt" -Value 'TEST small resident file' -Encoding ascii
[IO.File]::WriteAllBytes("$d\docs\big.bin", (New-Object byte[] 300000))
Set-Content -LiteralPath "$d\docs\raport_țară_ăîâșț.txt" -Value 'TEST unicode' -Encoding utf8
Set-Content -LiteralPath "$d\docs\A very long file name for the short name test.txt" -Value 'TEST long' -Encoding ascii
foreach ($p in "$d\docs", "$d\docs\small.txt", "$d\docs\big.bin", "$d\docs\raport_țară_ăîâșț.txt", "$d\docs\A very long file name for the short name test.txt") { $truth.files += (Rec $p 'live') }

# 2. a directory with 60 files (index allocation, INDX), then 20 deleted (deleted MFT records + $I30 slack)
New-Item -ItemType Directory "$d\many" | Out-Null
1..60 | ForEach-Object { Set-Content -LiteralPath ("$d\many\file_{0:D3}.txt" -f $_) -Value "TEST $_" -Encoding ascii }
$truth.files += (Rec "$d\many" 'live')
1..60 | ForEach-Object { $p = "$d\many\file_{0:D3}.txt" -f $_; $truth.files += (Rec $p $(if ($_ % 3 -eq 0) { 'to_delete' } else { 'live' })) }
1..60 | Where-Object { $_ % 3 -eq 0 } | ForEach-Object { $p = "$d\many\file_{0:D3}.txt" -f $_; $truth.deleted += $p.Substring(2); Remove-Item -LiteralPath $p }

# 3. rename (the $FILE_NAME is rewritten)
Set-Content -LiteralPath "$d\docs\before_rename.txt" -Value 'TEST rename' -Encoding ascii
$fidR = Fid "$d\docs\before_rename.txt"
Start-Sleep -Seconds 2
Rename-Item -LiteralPath "$d\docs\before_rename.txt" -NewName 'after_rename.txt'
$truth.renamed += [ordered]@{ fileId = $fidR; from = '\docs\before_rename.txt'; to = '\docs\after_rename.txt' }
$truth.files += (Rec "$d\docs\after_rename.txt" 'renamed')

# 4. timestomp through the Win32 API (SetFileTime changes $STANDARD_INFORMATION only)
Set-Content -LiteralPath "$d\docs\stomped.txt" -Value 'TEST stomp' -Encoding ascii
$before = Rec "$d\docs\stomped.txt" 'stomp_before'
$old = [datetime]::new(2019, 3, 14, 9, 26, 53, [DateTimeKind]::Utc)
$i = Get-Item -LiteralPath "$d\docs\stomped.txt"; $i.CreationTimeUtc = $old; $i.LastWriteTimeUtc = $old; $i.LastAccessTimeUtc = $old
$truth.stomped += [ordered]@{ before = $before; after = (Rec "$d\docs\stomped.txt" 'stomp_after'); setTo = $old.ToString('o') }

# 5. moved within the volume (same $MFT record, new parent)
Set-Content -LiteralPath "$d\docs\moved.txt" -Value 'TEST move' -Encoding ascii
$fidM = Fid "$d\docs\moved.txt"
Move-Item -LiteralPath "$d\docs\moved.txt" -Destination "$d\many\moved.txt"
$truth.files += (Rec "$d\many\moved.txt" 'moved')

# flush, record $MFT layout, then copy the $MFT extents raw (independent of LogAnalyzer)
Write-VolumeCache -DriveLetter $part.DriveLetter
fsutil file layout "$d\`$MFT" | Out-File "$Out\layout_mft.txt" -Encoding utf8
fsutil fsinfo ntfsinfo $d | Out-File "$Out\ntfsinfo.txt" -Encoding utf8
fsutil file layout "$d\docs\stomped.txt" | Out-File "$Out\layout_stomped.txt" -Encoding utf8
fsutil file layout "$d\many" | Out-File "$Out\layout_many.txt" -Encoding utf8
$truth | ConvertTo-Json -Depth 6 | Out-File "$Out\truth.json" -Encoding utf8
L "done $(Get-Date -Format o)"
