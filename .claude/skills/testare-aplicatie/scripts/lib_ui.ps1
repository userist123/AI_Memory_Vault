# lib_ui.ps1 - GENERIC, zero-dependency Win32+UIA+GDI toolkit for black-box GUI testing of a
# Windows desktop app, when pywinauto/FlaUI are not available (see SKILL.md: prefer those).
# Dot-source it:  . .\lib_ui.ps1
# Design rules baked in: locate by UIA pattern/AutomationId FIRST and coordinates LAST; assert the
# foreground window belongs to the target process before any synthetic input; wait on conditions,
# not fixed sleeps; capture the window (PrintWindow) not a screen region; never change the desktop
# without restoring it; never print to a physical printer by accident.
# App-specific button maps / steps live in a SEPARATE profile file, NOT here (see profile_example.ps1).

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# --- make the process DPI-aware so UIA rectangles and pixels agree (best-effort) ---
if (-not ([System.Management.Automation.PSTypeName]'Win32Ui').Type) {
Add-Type @"
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public class Win32Ui{
 public delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc f,IntPtr p);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowTextW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern bool BringWindowToTop(IntPtr h);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(POINT p);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint f);
 [DllImport("user32.dll")]public static extern bool PrintWindow(IntPtr h,IntPtr hdc,uint flags);
 [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)]public struct RECT{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]public struct POINT{public int X,Y;}
 public static List<IntPtr> Found=new List<IntPtr>();
 public static List<string> Titles=new List<string>();
 static uint wantPid;
 public static void Collect(uint pid){ wantPid=pid; Found.Clear(); Titles.Clear(); EnumWindows(cb,IntPtr.Zero); }
 static bool cb(IntPtr h,IntPtr p){ if(IsWindowVisible(h)){ uint pp; GetWindowThreadProcessId(h,out pp);
   if(wantPid==0 || pp==wantPid){ var sb=new StringBuilder(256); GetWindowTextW(h,sb,256);
     Found.Add(h); Titles.Add(sb.ToString()); } } return true; }
}
public class Mouse{
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern void mouse_event(uint f,uint dx,uint dy,uint d,UIntPtr e);
 [DllImport("user32.dll")]public static extern void keybd_event(byte vk,byte scan,uint f,UIntPtr e);
 public static void AltNudge(){ keybd_event(0x12,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(20); keybd_event(0x12,0,0x0002,UIntPtr.Zero); } // VK_MENU down/up to clear the foreground lock
 public static void Click(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(120);
   mouse_event(0x0002,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(0x0004,0,0,0,UIntPtr.Zero); }
 public static void DblClick(int x,int y){ Click(x,y); System.Threading.Thread.Sleep(90);
   mouse_event(0x0002,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(40); mouse_event(0x0004,0,0,0,UIntPtr.Zero); }
}
"@
}
try { [void][Win32Ui]::SetProcessDPIAware() } catch {}

# track windows we minimized / set topmost so Restore-Desktop can undo it
$global:UiTouched = @{ Minimized=@(); Topmost=@() }

# ---------- window discovery ----------
function Get-AppWindows([int]$ProcId){
  [Win32Ui]::Collect([uint32]$ProcId)
  $r=@(); for($i=0;$i -lt [Win32Ui]::Found.Count;$i++){ $r += [pscustomobject]@{H=[Win32Ui]::Found[$i];Title=[Win32Ui]::Titles[$i]} }
  $r
}
# Find-Window REQUIRES a real pid unless -AnyProcess is given, so pid 0 never matches "any" by accident.
function Find-Window([int]$ProcId,[string]$TitleLike,[switch]$AnyProcess){
  if($ProcId -le 0 -and -not $AnyProcess){ throw "Find-Window: pass a real ProcId, or -AnyProcess to match any process." }
  (Get-AppWindows $ProcId) | Where-Object { $_.Title -like $TitleLike } | Select-Object -First 1
}
function Test-ProcessAlive([int]$ProcId){ [bool](Get-Process -Id $ProcId -ErrorAction SilentlyContinue) }

# ---------- waits (condition, not fixed sleep) ----------
function Wait-Until([scriptblock]$Condition,[int]$TimeoutMs=8000,[int]$PollMs=250){
  $t=[Diagnostics.Stopwatch]::StartNew()
  while($t.ElapsedMilliseconds -lt $TimeoutMs){ try{ if(& $Condition){ return $true } }catch{} Start-Sleep -Milliseconds $PollMs }
  return $false
}
function Wait-WindowTitle([int]$ProcId,[string]$TitleLike,[int]$TimeoutMs=8000,[switch]$AnyProcess){
  $ok=Wait-Until { (Find-Window $ProcId $TitleLike -AnyProcess:$AnyProcess) -ne $null } $TimeoutMs
  if($ok){ Find-Window $ProcId $TitleLike -AnyProcess:$AnyProcess } else { $null }
}
function Wait-FileStable([string]$Path,[int]$TimeoutMs=20000){
  $last=-1
  Wait-Until { if(-not (Test-Path $Path)){ return $false } $s=(Get-Item $Path).Length; if($s -gt 0 -and $s -eq $last){ return $true } $script:_wf=$s; $last=$s; return $false } $TimeoutMs 800 | Out-Null
  (Test-Path $Path) -and ((Get-Item $Path).Length -gt 0)
}

# ---------- foreground-safe input ----------
function Set-Front([IntPtr]$H){
  [void][Win32Ui]::ShowWindow($H,9); [void][Win32Ui]::BringWindowToTop($H)
  [void][Win32Ui]::SetForegroundWindow($H)
  if([Win32Ui]::GetForegroundWindow() -ne $H){ [Mouse]::AltNudge(); [void][Win32Ui]::SetForegroundWindow($H) }  # clear foreground lock, retry
  Wait-Until { [Win32Ui]::GetForegroundWindow() -eq $H } 1200 100 | Out-Null
}
# True only if the foreground window is $H (or a window of the same process). Guards against focus steal.
function Assert-Foreground([IntPtr]$H,[int]$ExpectPid){
  $fg=[Win32Ui]::GetForegroundWindow()
  if($fg -eq $H){ return $true }
  [uint32]$p=0; [void][Win32Ui]::GetWindowThreadProcessId($fg,[ref]$p)
  return ($ExpectPid -gt 0 -and $p -eq [uint32]$ExpectPid)
}
# Coordinate click of LAST resort. The real safety is: the pixel under the cursor must belong to the
# target process (WindowFromPoint). Foreground equality is NOT required for a click (the click itself
# activates the window) and SetForegroundWindow is often refused, so we don't hard-gate on it.
function Click-PointSafe([int]$X,[int]$Y,[IntPtr]$OwnerH,[int]$ExpectPid){
  Set-Front $OwnerH
  $pt=New-Object Win32Ui+POINT; $pt.X=$X; $pt.Y=$Y
  $under=[Win32Ui]::WindowFromPoint($pt)
  [uint32]$up=0; [void][Win32Ui]::GetWindowThreadProcessId($under,[ref]$up)
  if($ExpectPid -gt 0 -and $up -ne [uint32]$ExpectPid){ return "ABORT_WRONG_TARGET:pid$up" }
  [Mouse]::Click($X,$Y); return "OK"
}
# Escape a string for SendKeys (these are special: + ^ % ~ ( ) { } [ ]).
function ConvertTo-SendKeys([string]$s){ ($s -replace '([+^%~(){}\[\]])','{$1}') }
function Send-KeysSafe([IntPtr]$H,[int]$ExpectPid,[string]$Text){
  if(-not (Assert-Foreground $H $ExpectPid)){ Set-Front $H }
  if(-not (Assert-Foreground $H $ExpectPid)){ return "ABORT_FOCUS" }
  [System.Windows.Forms.SendKeys]::SendWait((ConvertTo-SendKeys $Text)); return "OK"
}
function Send-Enter([IntPtr]$H,[int]$ExpectPid){ if(Assert-Foreground $H $ExpectPid){ [System.Windows.Forms.SendKeys]::SendWait('{ENTER}'); "OK" } else { "ABORT_FOCUS" } }

# ---------- screenshots (window content via PrintWindow; falls back to screen region) ----------
function Save-WindowShot([IntPtr]$H,[string]$Path){
  $r=New-Object Win32Ui+RECT; [void][Win32Ui]::GetWindowRect($H,[ref]$r)
  $w=$r.Right-$r.Left; $h=$r.Bottom-$r.Top
  if($w -le 0 -or $h -le 0){ return $null }
  $bmp=New-Object System.Drawing.Bitmap $w,$h
  $g=[System.Drawing.Graphics]::FromImage($bmp); $hdc=$g.GetHdc()
  $ok=[Win32Ui]::PrintWindow($H,$hdc,2)   # PW_RENDERFULLCONTENT=2
  $g.ReleaseHdc($hdc); $g.Dispose()
  if(-not $ok){ $bmp.Dispose(); $bmp=New-Object System.Drawing.Bitmap $w,$h
    ([System.Drawing.Graphics]::FromImage($bmp)).CopyFromScreen($r.Left,$r.Top,0,0,(New-Object System.Drawing.Size $w,$h)) }
  $bmp.Save($Path,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  [pscustomobject]@{Path=$Path;W=$w;H=$h;Method=$(if($ok){'PrintWindow'}else{'Screen'})}
}
function Save-ScreenShot([string]$Path){
  $b=[System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height
  ([System.Drawing.Graphics]::FromImage($bmp)).CopyFromScreen($b.X,$b.Y,0,0,$bmp.Size)
  $bmp.Save($Path,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $Path
}

# ---------- UI Automation: locate first, click by pattern/rectangle, coordinates last ----------
function Get-UiaRoot([IntPtr]$H){ [System.Windows.Automation.AutomationElement]::FromHandle($H) }
# Rich tree dump: type, name, AutomationId, enabled, bounding rectangle, supported patterns.
function Get-UiaTree([System.Windows.Automation.AutomationElement]$El,[int]$Depth=0,[int]$Max=6){
  if($null -eq $El -or $Depth -gt $Max){ return }
  $c=$El.Current; $ct=$c.ControlType.ProgrammaticName -replace 'ControlType\.',''
  $rc=$c.BoundingRectangle; $rect=if($rc.IsEmpty){''}else{"@[{0:0},{1:0} {2:0}x{3:0}]" -f $rc.X,$rc.Y,$rc.Width,$rc.Height}
  $pats=($El.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers\.','' -replace 'Pattern$','' }) -join ','
  ("  "*$Depth) + "[$ct] '$($c.Name)'" + $(if($c.AutomationId){" #$($c.AutomationId)"}) + $(if(-not $c.IsEnabled){" (disabled)"}) + " $rect" + $(if($pats){" {$pats}"})
  $w=[System.Windows.Automation.TreeWalker]::ControlViewWalker; $ch=$w.GetFirstChild($El)
  while($ch){ Get-UiaTree $ch ($Depth+1) $Max; $ch=$w.GetNextSibling($ch) }
}
function Find-Uia([IntPtr]$H,[string]$Name,[string]$AutomationId){
  $root=Get-UiaRoot $H
  if($AutomationId){ $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$AutomationId) }
  else { $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$Name) }
  $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
}
# Locator ladder: InvokePattern -> SelectionItem/Toggle -> bounding-rect centre click. Returns status.
function Invoke-Uia([IntPtr]$H,[int]$ExpectPid,[string]$Name,[string]$AutomationId){
  $el=Find-Uia $H $Name $AutomationId
  if($null -eq $el){ return "NOT_FOUND" }
  if(-not $el.Current.IsEnabled){ return "DISABLED" }
  try{ $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return "OK:Invoke" }catch{}
  try{ $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); return "OK:Select" }catch{}
  try{ $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); return "OK:Toggle" }catch{}
  $rc=$el.Current.BoundingRectangle
  if(-not $rc.IsEmpty){ return (Click-PointSafe ([int]($rc.X+$rc.Width/2)) ([int]($rc.Y+$rc.Height/2)) $H $ExpectPid) }
  return "NO_INVOKE"
}
function Set-UiaValue([IntPtr]$H,[string]$Name,[string]$AutomationId,[string]$Value){
  $el=Find-Uia $H $Name $AutomationId; if($null -eq $el){ return "NOT_FOUND" }
  try{ $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value); "OK" }catch{ "NO_VALUE_PATTERN" }
}

# ---------- data / hardware safety ----------
function Backup-File([string]$Path){
  if(-not (Test-Path $Path)){ return $null }
  $bk="$Path.bak_{0:yyyyMMdd_HHmmss}" -f (Get-Date)
  Copy-Item $Path $bk -Force; [pscustomobject]@{Original=$Path;Backup=$bk;Sha=(Get-FileHash $Path -Algorithm SHA256).Hash}
}
function Save-OutputPath([string]$Path){  # never silently overwrite: move an existing file aside
  if(Test-Path $Path){ Move-Item $Path ("$Path.prev_{0:HHmmss}" -f (Get-Date)) -Force }
}
# Verify a printer by NAME is the selected one in a standard Print dialog before committing (avoid real print).
function Confirm-PrinterByName([IntPtr]$PrintDlgH,[string]$ExpectNameLike){
  $root=Get-UiaRoot $PrintDlgH
  $sel=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))) |
    Where-Object { try{ $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected }catch{ $false } } | Select-Object -First 1
  if($sel){ $sel.Current.Name -like $ExpectNameLike } else { $false }
}

# ---------- cleanup / restore ----------
function Hide-Window([IntPtr]$H){ [void][Win32Ui]::ShowWindow($H,6); $global:UiTouched.Minimized += $H }
function Set-Topmost([IntPtr]$H,[int]$X,[int]$Y){ [void][Win32Ui]::SetWindowPos($H,[IntPtr](-1),$X,$Y,0,0,0x1); $global:UiTouched.Topmost += $H }
function Restore-Desktop(){
  foreach($h in $global:UiTouched.Topmost){ try{ [void][Win32Ui]::SetWindowPos($h,[IntPtr](-2),0,0,0,0,0x1 -bor 0x2) }catch{} } # NOTOPMOST, NOSIZE|NOMOVE
  foreach($h in $global:UiTouched.Minimized){ try{ [void][Win32Ui]::ShowWindow($h,9) }catch{} } # restore
  $global:UiTouched = @{ Minimized=@(); Topmost=@() }
}

# ---------- bounded retry + default-printer safety (Fable hardening) ----------
# Try an action at most $Max+1 times; PASS as soon as $Check is true, else FAIL (never loops forever).
function Invoke-WithRetry([scriptblock]$Action,[scriptblock]$Check,[int]$Max=2){
  for($i=0;$i -le $Max;$i++){ try{ & $Action | Out-Null }catch{}; if(& $Check){ return "OK:try$i" }; Start-Sleep -Milliseconds 400 }
  return "FAIL_AFTER_$Max"
}
# Save the current default printer before a print test, restore it afterwards (so a test never leaves
# a test printer as the machine default).
function Save-DefaultPrinter(){ try{ (Get-CimInstance Win32_Printer -Filter 'Default=TRUE' -EA Stop).Name }catch{ $null } }
function Restore-DefaultPrinter([string]$Name){ if($Name){ try{ (New-Object -ComObject WScript.Network).SetDefaultPrinter($Name) }catch{} } }
