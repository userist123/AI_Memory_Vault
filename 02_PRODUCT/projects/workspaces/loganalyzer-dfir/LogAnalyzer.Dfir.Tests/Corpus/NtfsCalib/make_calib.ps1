#Requires -RunAsAdministrator
# Calibration volume for the timestomp rule (S4): USN journal active BEFORE any operation; real timestomps and the legitimate
# operations that look like them (copies, cross-volume moves, robocopy /COPY:DAT, archive extraction). Ground truth from
# Get-Item before/after each step. Source volume for cross-volume operations: the existing test volume (label LATEST).
param([string]$Out = "$PSScriptRoot\calib")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$log = "$Out\steps.log"; "start $(Get-Date -Format o) tz=$([TimeZoneInfo]::Local.Id)" | Out-File $log
function L($m) { "$(Get-Date -Format o) $m" | Out-File -Append $log }

$src = (Get-Volume -FileSystemLabel LATEST).DriveLetter + ':'
$vhd = "$Out\calib.vhdx"
if (Test-Path $vhd) { try { Dismount-VHD $vhd } catch {}; Remove-Item $vhd -Force }
New-VHD -Path $vhd -SizeBytes 64MB -Fixed | Out-Null
$disk = Mount-VHD -Path $vhd -Passthru | Get-Disk
Initialize-Disk -Number $disk.Number -PartitionStyle GPT | Out-Null
$part = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
Format-Volume -Partition $part -FileSystem NTFS -NewFileSystemLabel LACALIB -Confirm:$false | Out-Null
$d = "$($part.DriveLetter):"
fsutil usn createjournal m=0x800000 a=0x100000 $d | Out-File -Append $log
L "drive $d source $src"

$truth = [ordered]@{ drive = $d; tz = [TimeZoneInfo]::Local.Id; scenarios = @() }
function Fid($p) { (fsutil file queryfileid $p) -replace '.*0x', '0x' }
function T($p) { $i = Get-Item -LiteralPath $p -Force; [ordered]@{ siCreated = $i.CreationTimeUtc.ToString('o'); siModified = $i.LastWriteTimeUtc.ToString('o'); siAccessed = $i.LastAccessTimeUtc.ToString('o') } }
function S($name, $kind, $path, $note, $before) { $script:truth.scenarios += [ordered]@{ name = $name; expected = $kind; path = $path.Substring(2); fileId = (Fid $path); note = $note; before = $before; after = (T $path) } }
$old = [datetime]::new(2019, 3, 14, 9, 26, 53, [DateTimeKind]::Utc)
$oldFrac = [datetime]::new(2019, 3, 14, 9, 26, 53, [DateTimeKind]::Utc).AddTicks(1234567)
function Stomp($p, $t) { $i = Get-Item -LiteralPath $p; $i.CreationTimeUtc = $t; $i.LastWriteTimeUtc = $t; $i.LastAccessTimeUtc = $t }
foreach ($dir in 'stomp', 'legit', 'robo', 'xmove', 'xcopy', 'zip', 'tar', 'sub') { New-Item -ItemType Directory "$d\$dir" | Out-Null }

# --- timestomps (expected: suspicious) ---
Set-Content "$d\stomp\a_separate.txt" 'TEST' -Encoding ascii; $b = T "$d\stomp\a_separate.txt"; Start-Sleep 2
Stomp "$d\stomp\a_separate.txt" $old; S 'A' 'timestomp' "$d\stomp\a_separate.txt" 'create, close, 2 s later SetFileTime (3 SI times = 2019, zero fraction)' $b
Set-Content "$d\stomp\b_fraction.txt" 'TEST' -Encoding ascii; $b = T "$d\stomp\b_fraction.txt"; Start-Sleep 2
Stomp "$d\stomp\b_fraction.txt" $oldFrac; S 'B' 'timestomp' "$d\stomp\b_fraction.txt" 'same as A with a non-zero fraction (evades the zero-fraction signal)' $b
Set-Content "$d\stomp\c_before_rename.txt" 'TEST' -Encoding ascii; $b = T "$d\stomp\c_before_rename.txt"; Start-Sleep 2
Stomp "$d\stomp\c_before_rename.txt" $old; Start-Sleep 2; Rename-Item "$d\stomp\c_before_rename.txt" 'c_after_rename.txt'
S 'C' 'timestomp' "$d\stomp\c_after_rename.txt" 'stomp then rename (does Windows copy SI into FN?)' $b
Set-Content "$d\stomp\d_moved.txt" 'TEST' -Encoding ascii; $b = T "$d\stomp\d_moved.txt"; Start-Sleep 2
Stomp "$d\stomp\d_moved.txt" $old; Start-Sleep 2; Move-Item "$d\stomp\d_moved.txt" "$d\sub\d_moved.txt"
S 'D' 'timestomp' "$d\sub\d_moved.txt" 'stomp then move on the same volume' $b
Set-Content "$d\stomp\e_created_only.txt" 'TEST' -Encoding ascii; $b = T "$d\stomp\e_created_only.txt"; Start-Sleep 2
(Get-Item "$d\stomp\e_created_only.txt").CreationTimeUtc = $old; S 'E' 'timestomp' "$d\stomp\e_created_only.txt" 'only the creation time changed' $b

# --- legitimate look-alikes (expected: not High) ---
Set-Content "$d\legit\src_old.txt" 'TEST' -Encoding ascii; (Get-Item "$d\legit\src_old.txt").LastWriteTimeUtc = $old.AddYears(1); Start-Sleep 2
Copy-Item "$d\legit\src_old.txt" "$d\legit\f_copy_same_volume.txt"; S 'F' 'legit' "$d\legit\f_copy_same_volume.txt" 'Copy-Item on the same volume of a file with an old modification time' $null
Copy-Item "$src\docs\stomped.txt" "$d\xcopy\g_copy_cross_volume.txt"; S 'G' 'legit' "$d\xcopy\g_copy_cross_volume.txt" 'Copy-Item from another volume (source SI = 2019)' $null
robocopy "$src\docs" "$d\robo" stomped.txt /COPY:DAT /R:0 /W:0 /NP /NJH /NJS | Out-File -Append $log
S 'H' 'legit' "$d\robo\stomped.txt" 'robocopy /COPY:DAT from another volume (source SI = 2019)' $null
Copy-Item "$src\docs\after_rename.txt" "$src\docs\to_move.txt"; (Get-Item "$src\docs\to_move.txt").CreationTimeUtc = $old.AddYears(2)
Move-Item "$src\docs\to_move.txt" "$d\xmove\i_moved_cross_volume.txt"; S 'I' 'legit' "$d\xmove\i_moved_cross_volume.txt" 'Move-Item from another volume (source creation 2021)' $null
$zsrc = "$env:TEMP\lacalib_zip"; Remove-Item $zsrc -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $zsrc | Out-Null
Set-Content "$zsrc\j_from_zip.txt" 'TEST' -Encoding ascii; (Get-Item "$zsrc\j_from_zip.txt").LastWriteTime = [datetime]'2018-06-01 10:00:00'
(Get-Item "$zsrc\j_from_zip.txt").CreationTime = [datetime]'2018-06-01 10:00:00'
Compress-Archive "$zsrc\j_from_zip.txt" "$zsrc\a.zip" -Force; Expand-Archive "$zsrc\a.zip" "$d\zip" -Force
S 'J' 'legit' "$d\zip\j_from_zip.txt" 'Expand-Archive (zip, entry time 2018)' $null
Copy-Item "$zsrc\j_from_zip.txt" "$zsrc\k_from_tar.txt"; tar -cf "$zsrc\a.tar" -C $zsrc k_from_tar.txt; tar -xf "$zsrc\a.tar" -C "$d\tar"
S 'K' 'legit' "$d\tar\k_from_tar.txt" 'tar -xf (bsdtar), entry time 2018' $null
Remove-Item $zsrc -Recurse -Force

Write-VolumeCache -DriveLetter $part.DriveLetter
fsutil usn readjournal $d csv | Out-File "$Out\usn.csv" -Encoding utf8
$truth | ConvertTo-Json -Depth 6 | Out-File "$Out\truth.json" -Encoding utf8
# raw copies: boot sector + $MFT by Windows' own extents
$info = fsutil fsinfo ntfsinfo $d
$bpc = [int]([regex]::Match((($info | Select-String 'Bytes Per Cluster').Line -replace '.*:\s*', ''), '\d+').Value)
$vol = [IO.File]::Open("\\.\$d", 'Open', 'Read', 'ReadWrite')
$b = New-Object byte[] 512; [void]$vol.Read($b, 0, 512); [IO.File]::WriteAllBytes("$Out\boot.bin", $b)
$fs = [IO.File]::Create("$Out\mft.bin")
foreach ($line in (fsutil file queryextents "$d\`$MFT")) {
  if ($line -match 'VCN:\s*(0x[0-9a-f]+)\s+Clusters:\s*(0x[0-9a-f]+)\s+LCN:\s*(0x[0-9a-f]+)') {
    $n = [Convert]::ToInt64($Matches[2], 16); $lcn = [Convert]::ToInt64($Matches[3], 16); $buf = New-Object byte[] ($n * $bpc)
    $vol.Position = $lcn * $bpc; $r = 0; while ($r -lt $buf.Length) { $k = $vol.Read($buf, $r, $buf.Length - $r); if ($k -le 0) { throw 'short' }; $r += $k }
    $fs.Write($buf, 0, $buf.Length) } }
$fs.Close(); $vol.Close()
L 'done'
