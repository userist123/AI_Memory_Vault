#Requires -RunAsAdministrator
# Adds tunneling / safe-save scenarios (L, M, N) to the calibration volume (label LACALIB), then re-exports $MFT, boot sector and USN.
param([string]$Out = "$PSScriptRoot\calib")
$ErrorActionPreference = 'Stop'
$d = (Get-Volume -FileSystemLabel LACALIB).DriveLetter + ':'
$truthPath = "$Out\truth.json"; $truth = Get-Content $truthPath -Raw | ConvertFrom-Json
function Fid($p) { (fsutil file queryfileid $p) -replace '.*0x', '0x' }
function T($p) { $i = Get-Item -LiteralPath $p -Force; [ordered]@{ siCreated = $i.CreationTimeUtc.ToString('o'); siModified = $i.LastWriteTimeUtc.ToString('o'); siAccessed = $i.LastAccessTimeUtc.ToString('o') } }
function Add($name, $path, $note, $oldId) { $truth.scenarios += [pscustomobject][ordered]@{ name = $name; expected = 'legit'; path = $path.Substring(2); fileId = (Fid $path); note = "$note (old file id $oldId)"; before = $null; after = (T $path) } }
New-Item -ItemType Directory -Force "$d\save" | Out-Null
$old = [datetime]::new(2024, 1, 1, 8, 0, 0, [DateTimeKind]::Utc).AddTicks(4567890)

# Old documents: created through a temporary name then renamed, with the creation time set in the SAME handle session as the write
# (a document copied in earlier with robocopy would look the same); what matters is the NEW record created below.
foreach ($n in 'l_safe_save.docx', 'm_replace.docx', 'n_tunnel.txt') {
  Set-Content "$d\save\$n" 'TEST old' -Encoding ascii; (Get-Item "$d\save\$n").CreationTimeUtc = $old }
Start-Sleep -Seconds 3

# L: Office-style safe save: temp created, written, attribute changed in a separate operation, original renamed to .bak and deleted, temp renamed to the original name (< 15 s)
$oldId = Fid "$d\save\l_safe_save.docx"
Set-Content "$d\save\~WRL0001.tmp" 'TEST new content' -Encoding ascii
attrib +h "$d\save\~WRL0001.tmp"; attrib -h "$d\save\~WRL0001.tmp"
Rename-Item "$d\save\l_safe_save.docx" 'l_safe_save.bak'; Remove-Item "$d\save\l_safe_save.bak"
Rename-Item "$d\save\~WRL0001.tmp" 'l_safe_save.docx'
Add 'L' "$d\save\l_safe_save.docx" 'safe save: temp + attrib + rename original away + delete + rename temp' $oldId

# M: ReplaceFile (File.Replace) of the original by a new temp
$oldId = Fid "$d\save\m_replace.docx"
Set-Content "$d\save\m_new.tmp" 'TEST replacement' -Encoding ascii
[IO.File]::Replace("$d\save\m_new.tmp", "$d\save\m_replace.docx", "$d\save\m_replace.backup")
Add 'M' "$d\save\m_replace.docx" 'File.Replace (ReplaceFile) with a backup' $oldId

# N: classic tunneling: delete then create the same name within 15 s
$oldId = Fid "$d\save\n_tunnel.txt"
Remove-Item "$d\save\n_tunnel.txt"; Set-Content "$d\save\n_tunnel.txt" 'TEST recreated' -Encoding ascii
Add 'N' "$d\save\n_tunnel.txt" 'delete then create the same name (tunneling)' $oldId

Write-VolumeCache -DriveLetter $d.Substring(0, 1)
$truth | ConvertTo-Json -Depth 6 | Out-File $truthPath -Encoding utf8
fsutil usn readjournal $d csv | Out-File "$Out\usn.csv" -Encoding utf8
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
