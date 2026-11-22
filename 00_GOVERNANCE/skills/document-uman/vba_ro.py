"""VBA helpers for document-uman: static check, build .xlsm/.docm, run a macro, export modules.

Usage (from any folder; Windows + Microsoft Office 16 + pywin32):
  python -P -E <skill dir>\\vba_ro.py verifica <file.bas|.cls|.xlsm|.docm> [...]  static check, exit 1 on FAIL
  python -P -E <skill dir>\\vba_ro.py construieste <out.xlsm|out.docm> <mod.bas> [...] [--baza file]
  python -P -E <skill dir>\\vba_ro.py ruleaza <file.xlsm|file.docm> <Modul.Macro> [arg ...]
  python -P -E <skill dir>\\vba_ro.py exporta <file.xlsm|file.docm> <out dir>
  python -P -E <skill dir>\\vba_ro.py stare-vbom                      show AccessVBOM values
  python -P -E <skill dir>\\vba_ro.py restaureaza-vbom                restore after a crash

AccessVBOM guard: importing/exporting modules needs "Trust access to the VBA project object model"
(HKCU\\Software\\Microsoft\\Office\\16.0\\<App>\\Security\\AccessVBOM). `acces_vbom()` takes an
exclusive per-app lock (lock file created with O_CREAT|O_EXCL that holds the owner PID and the
owner's process start time), records the original state (which keys existed, whether the value
existed, its registry type and data) in one backup file per app, sets DWORD 1 only while the build
runs and then restores exactly that state: same type and data, value deleted if it was absent, and
the Security key (and any parent) deleted again if the guard created it. A second run while the
lock owner is alive is refused; a lock left by a dead process is recovered from its backup first.
VBAWarnings, the macro security level and Trusted Locations are never touched.

Office starts as a private invisible instance with AutomationSecurity = 3
(msoAutomationSecurityForceDisable) set immediately after it starts, so Workbook_Open,
Document_Open and Auto_Open of a file opened for building or exporting cannot run while AccessVBOM
is 1. `construieste` exports the built project and runs the static check on every module, including
code that came with --baza, before saving; a FAIL aborts the build and nothing is written. `ruleaza`
first exports and checks the file (macros disabled), then reopens it with macros enabled to run the
macro; AccessVBOM is never 1 while macros can run.
"""
from __future__ import annotations

import atexit
import contextlib
import json
import os
import re
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

try:  # the static checker must also run (and be tested) where winreg does not exist
    import winreg
except ImportError:  # pragma: no cover - non-Windows
    winreg = None

OFFICE_VER = "16.0"
VBOM_BASE = rf"Software\Microsoft\Office\{OFFICE_VER}"
STATE_DIR = Path(tempfile.gettempdir()) / "vba_ro_vbom"
LEGACY_BACKUP = Path(tempfile.gettempdir()) / "vba_ro_vbom_backup.json"  # format of version 1
APPS = ("Word", "Excel")
_ABSENT = "absent"
SEC_FORCE_DISABLE = 3  # msoAutomationSecurityForceDisable
SEC_LOW = 1            # msoAutomationSecurityLow (only for `ruleaza`, after the check passed)

# ----------------------------------------------------------------------------- static check
# Each rule is (regex, message). FORBIDDEN_ANY is matched on code with string literals kept (names
# hidden in strings count); FORBIDDEN_CODE on code with string literals blanked (keywords only).
# Comments are removed and " _" line continuations joined before matching.
FORBIDDEN_ANY = [
    (r"WScript|Wscript\.Shell|IWshRuntimeLibrary|\bWshShell\b|Shell32\.|Shell\.Application|"
     r"ShellExecute|\bNew\s+Shell\b", "shell object (runs programs / shell access)"),
    (r"powershell|pwsh|cmd\.exe|cmd\s*/[ck]|\bcscript\b|mshta|rundll32|regsvr32|certutil|bitsadmin|"
     r"schtasks|\breg\.exe|msiexec|\bwmic\b|\bcurl(\.exe)?\b", "external tool name"),
    (r"URLDownloadToFile|XMLHTTP|WinHttp|ServerXMLHTTP|InternetExplorer\.Application|MSXML2|"
     r"Inet\w*\.|https?://|\bftp://|\bWEBSERVICE\b", "network access"),
    (r"\\\\", "UNC path (\\\\server\\share - network access, NTLM hash leak)"),
    (r"XLSTART|\\Startup\b|Start Menu|\bStartupPath\b|\bAltStartupPath\b|NormalTemplate|"
     r"Normal\.dotm|PERSONAL\.XLSB", "Startup folder / Normal template / PERSONAL.XLSB (persistence)"),
    (r"\bSaveSetting\b|\bDeleteSetting\b|RegWrite|RegDelete|StdRegProv", "registry write"),
    (r"\bSub\s+(Auto_?Open|Auto_?Close|Auto_?Exec|Auto_?New|Auto_?Exit)\b|"
     r"\bSub\s+(Document_Open|Document_New|Document_Close|Workbook_Open|Workbook_BeforeClose|"
     r"Workbook_Activate)\b|\bSub\s+\w+_(GotFocus|LostFocus|Painted|Painting|Layout|Resize|MouseMove|"
     r"MouseEnter|Change)\s*\(", "auto-execution macro (macros must run only when the user starts them)"),
    (r"xlSheetVeryHidden|xlVeryHidden|\.Visible\s*:?=\s*2\b", "very hidden sheet"),
    (r"\.VBProject\b|VBComponents|CodeModule|\.VBE\b", "code that edits VBA projects"),
    (r"\bExecuteExcel4Macro\b|\bMacScript\b|\bAppleScriptTask\b|\bCallByName\b|\bStrReverse\b|"
     r"xlExcel4(Intl)?MacroSheet|Excel4(Intl)?MacroSheets", "dynamic execution / obfuscation primitive"),
    (r"\bGetObject\s*\(", "GetObject (binds to arbitrary running objects)"),
    (r"\.Protect\w*\s+Password|VBAProject\.Password", "password protection"),
    (r"AutomationSecurity\s*=\s*(1|msoAutomationSecurityLow)\b",
     "AutomationSecurity Low (lets macros of opened files run)"),
]
FORBIDDEN_CODE = [
    (r"\bShell\b", "Shell (runs external programs)"),
    (r"\bKill\b|\bRmDir\b|\.DeleteFile\b|\.DeleteFolder\b", "file/folder deletion"),
    (r"\bSendKeys\b", "SendKeys"),
    (r"\bEvaluate\b", "Evaluate (executes a formula/name given as text)"),
    (r"\[[^\]]*\]", "[ ] evaluation shortcut / bracketed identifier"),
    (r"\.OnTime\b|\.OnKey\b|\.OnEntry\b|\.OnDoubleClick\b|\.OnSheetActivate\b|\.OnRepeat\b|\.OnUndo\b",
     "OnTime/OnKey (schedules or hooks code to run later)"),
    (r"\bDDE\w*", "DDE (runs commands in other programs)"),
    (r"\bFollowHyperlink\b", "FollowHyperlink (opens URLs / files)"),
    (r"\bAddIns\d?\.Add\b|\.Installed\s*:?=", "add-in installation (persistence)"),
    (r"(^|:)\s*Name\s+\S.*\s+As\s+", "Name ... As statement (renames/moves files)"),
    (r"\bChrB\b|\bChrB\$", "ChrB (byte-level string building - obfuscation)"),
    (r"(\bChr[BW]?\$?\s*\([^()]*\)\s*&\s*){2,}\bChr", "Chr()/ChrW() chain (string obfuscation)"),
]
WARN_CODE = [
    (r"\bFileCopy\b|\.CopyFile\b|\.CopyFolder\b|\.MoveFile\b|\.MoveFolder\b",
     "copies/moves files - check the destination is a folder the user chose"),
    (r"(^|:)\s*Open\s+.+\s+For\s+(Output|Append|Binary|Random)\b",
     "Open ... For Output/Append writes a file - check path and overwrite rule"),
    (r"\.CreateTextFile\b|\.OpenTextFile\b", "writes a text file - check path and overwrite rule"),
]
# CreateObject is allowed only for these ProgIDs, as a complete string literal.
CREATEOBJECT_OK = {"scripting.dictionary", "scripting.filesystemobject", "word.application",
                   "excel.application"}
SETTINGS = ("ScreenUpdating", "EnableEvents", "Calculation", "DisplayAlerts", "AutomationSecurity",
            "PrintCommunication")
MAX_CHR = 8  # this many Chr/ChrW/ChrB calls in one module = string obfuscation


def _strip_comment(line: str):
    """Return (code, had_comment): the line without a trailing ' comment (outside string literals)."""
    out, inq = [], False
    for ch in line:
        if ch == '"':
            inq = not inq
        elif ch == "'" and not inq:
            return "".join(out), True
        out.append(ch)
    return "".join(out), False


def _logical_lines(text: str):
    """Yield (first physical line number, raw text, code) per logical line.

    Comments are removed per physical line; a code line ending in " _" is joined with the next one,
    so a keyword split across a continuation is seen whole. Lines that VBA treats as a continued
    comment are still scanned as code (that can only add findings, never hide code)."""
    raw_buf, code_buf, start = [], [], None
    for i, raw in enumerate(text.splitlines(), 1):
        code, had_comment = _strip_comment(raw)
        if start is None:
            start = i
        raw_buf.append(raw)
        stripped = code.rstrip()
        if not had_comment and (stripped.endswith(" _") or stripped.endswith("\t_") or stripped == "_"):
            code_buf.append(stripped[:-1])
            continue
        code_buf.append(code)
        yield start, "\n".join(raw_buf), " ".join(c.strip() if k else c for k, c in enumerate(code_buf))
        raw_buf, code_buf, start = [], [], None
    if raw_buf:
        yield start, "\n".join(raw_buf), " ".join(code_buf)


def _blank_strings(code: str) -> str:
    return re.sub(r'"[^"\n]*"', '""', code)


def _procedures(logical):
    """Yield (kind, scope, name, has_args, body_lines) for each Sub/Function."""
    head = re.compile(r"^\s*(Public\s+|Private\s+|Friend\s+)?(Static\s+)?(Sub|Function)\s+(\w+)\s*\((.*?)\)",
                      re.I)
    cur = None
    for ln in logical:
        m = head.match(ln)
        if m and cur is None:
            cur = [m.group(3).lower(), (m.group(1) or "public").strip().lower(), m.group(4),
                   bool(m.group(5).strip()), []]
            continue
        if cur is not None:
            if re.match(r"^\s*End\s+(Sub|Function)\b", ln, re.I):
                yield tuple(cur)
                cur = None
            else:
                cur[4].append(ln)


def _fso_vars(logical_ns) -> set:
    """Variables that hold a FileSystemObject File/Folder (their .Delete removes files)."""
    names = set()
    for ln in logical_ns:
        for m in re.finditer(r"\b(\w+)\s+As\s+(Scripting\.)?(File|Folder|Files|Folders)\b", ln, re.I):
            names.add(m.group(1).lower())
        m = re.match(r"^\s*Set\s+(\w+)\s*=.*\b(GetFile|GetFolder|GetSpecialFolder|CreateFolder|ParentFolder|"
                     r"RootFolder|Files|SubFolders)\b", ln, re.I)
        if m:
            names.add(m.group(1).lower())
        m = re.match(r"^\s*For\s+Each\s+(\w+)\s+In\s+.*\.(Files|SubFolders)\b", ln, re.I)
        if m:
            names.add(m.group(1).lower())
    return names


def verifica_sursa(text: str, nume: str = "<sursa>"):
    """Return (fails, warns) for one VBA module source."""
    fails, warns = [], []
    lines = text.splitlines()
    logical = list(_logical_lines(text))
    code = [c for _, _, c in logical]
    code_ns = [_blank_strings(c) for c in code]
    joined = "\n".join(code)
    joined_ns = "\n".join(code_ns)

    if not re.search(r"^\s*Option\s+Explicit\b", joined, re.I | re.M):
        fails.append("Option Explicit missing")
    head = "\n".join(lines[:40]).lower()
    for lab in ("scop:", "rulare:", "modific"):
        if lab not in head:
            fails.append(f"header comment without '{lab}' (purpose / how to run / what it changes)")

    fso = _fso_vars(code_ns)
    in_fso_with = False
    for idx, ((i, raw, ln), ns) in enumerate(zip(logical, code_ns)):
        short = " ".join(raw.split())[:90]
        for rx, msg in FORBIDDEN_ANY:
            if re.search(rx, ln, re.I):
                fails.append(f"line {i}: {msg}: {short}")
        for rx, msg in FORBIDDEN_CODE:
            if re.search(rx, ns, re.I):
                fails.append(f"line {i}: {msg}: {short}")
        for rx, msg in WARN_CODE:
            if re.search(rx, ns, re.I):
                warns.append(f"line {i}: {msg}: {short}")
        if re.search(r"\bDeclare\s+(PtrSafe\s+)?(Function|Sub)\b", ns, re.I):
            if "vba_ro:allow-declare" in raw:
                warns.append(f"line {i}: Declare with documented need: {short}")
            else:
                fails.append(f"line {i}: Declare API call (allowed only with ' vba_ro:allow-declare "
                             f"<documented need> on the same line): {short}")
        # .Run: Application.Run "LiteralMacro" is a WARN, anything else (WScript .Run, computed name) FAIL
        if re.search(r"\.Run\b", ns, re.I):
            if re.search(r"\bApplication\.Run\s*\(?\s*\"[\w.!']+\"\s*(,|\)|$)", ln, re.I) and \
                    len(re.findall(r"\.Run\b", ns, re.I)) == 1:
                warns.append(f"line {i}: Application.Run with a literal macro name: {short}")
            else:
                fails.append(f"line {i}: .Run with a computed target or on a shell object: {short}")
        # .Delete: on a FileSystemObject File/Folder = file deletion (FAIL); on a sheet/range = WARN
        if re.match(r"^\s*With\s+.*\b(GetFile|GetFolder|Files|SubFolders)\b", ns, re.I):
            in_fso_with = True
        elif re.match(r"^\s*End\s+With\b", ns, re.I):
            in_fso_with = False
        for m in re.finditer(r"(\w*)\s*(\([^()]*\))?\s*\.Delete\b", ns, re.I):
            target = m.group(1).lower()
            if (target in fso or re.search(r"\b(GetFile|GetFolder)\s*\(", ns, re.I)
                    or (in_fso_with and not target)):
                fails.append(f"line {i}: .Delete on a file/folder object (file deletion): {short}")
            else:
                warns.append(f"line {i}: .Delete - make sure it removes only an object the macro created: "
                             f"{short}")
        for m in re.finditer(r"CreateObject\s*\(\s*\"([^\"]*)\"(\s*[,)])?", ln, re.I):
            if m.group(1).lower() not in CREATEOBJECT_OK or not m.group(2):
                fails.append(f"line {i}: CreateObject(\"{m.group(1)}\") not on the allow list (or a "
                             f"concatenated ProgID)")
        if re.search(r"CreateObject\s*\(\s*[^\"\s]", ln, re.I):
            fails.append(f"line {i}: CreateObject with a computed ProgID")
        if re.search(r"\.(Select|Activate)\b(?!\w)", ns) or re.search(r"\bSelection\.", ns):
            fails.append(f"line {i}: Select/Activate/Selection - work with object variables")
        if re.search(r"\bOn\s+Error\s+Resume\s+Next\b", ns, re.I) and not any(
                re.search(r"On\s+Error\s+GoTo", c, re.I) for c in code_ns[idx + 1:idx + 4]):
            warns.append(f"line {i}: On Error Resume Next - keep it to one statement, then On Error GoTo 0")
        if re.search(r"\b(Range|\.Range)\s*\(\s*\"[A-Z]{1,3}\$?\d+", ln) and "Const" not in ln:
            warns.append(f"line {i}: hard-coded cell address - use a named range or a Const")
        if re.search(r"[^\x00-\x7f]", raw):
            warns.append(f"line {i}: non-ASCII character - the VBA editor reads .bas in the ANSI code "
                         "page; use plain letters (or the RO() helper with escape tokens)")
    if len(re.findall(r"\bChr[BW]?\$?\s*\(", joined_ns, re.I)) >= MAX_CHR:
        fails.append(f"{MAX_CHR} or more Chr()/ChrW() calls - looks like string obfuscation "
                     "(use one helper such as RO())")
    if re.search(r'"[A-Za-z0-9+/=]{120,}"', joined):
        fails.append("long base64-like string literal")

    for kind, scope, name, has_args, body in _procedures(code):
        b = "\n".join(body)
        entry = kind == "sub" and scope == "public" and not has_args
        worker = kind == "function" and scope == "public"
        if (entry or worker) and not re.search(r"On\s+Error\s+GoTo\s+(?!0\b)\w+", b, re.I):
            fails.append(f"{name}: public macro without an error handler (On Error GoTo <label>)")
        m = re.search(r"On\s+Error\s+GoTo\s+(?!0\b)(\w+)", b, re.I)
        if m:
            label = m.group(1)
            lab = re.search(rf"^\s*{label}\s*:", b, re.I | re.M)
            if not lab:
                fails.append(f"{name}: error label {label} not found")
            else:
                tail = "\n".join(b[:lab.start()].split("\n")[-6:])
                if not re.search(r"\bExit\s+(Sub|Function)\b|^\s*GoTo\s+\w+", tail, re.I | re.M):
                    fails.append(f"{name}: no Exit Sub/Function (or GoTo cleanup) before {label}: - "
                                 "the normal path falls into the error handler")
        if entry and "MsgBox" not in b:
            warns.append(f"{name}: entry macro without a MsgBox report to the user")
        for s in SETTINGS:
            if re.search(rf"\.{s}\s*=\s*(False|xlCalculationManual|-4135|0|wdAlertsNone|3|"
                         r"msoAutomationSecurityForceDisable)\b", b) and \
                    len(re.findall(rf"\.{s}\s*=", b)) < 2:
                fails.append(f"{name}: sets Application.{s} but never restores it")
        if re.search(r"\.(SaveAs2?|ExportAsFixedFormat)\b", b) and not re.search(
                r"\bDir\s*\(|FileExists|suprascrie|overwrite|vbYesNo", b, re.I):
            warns.append(f"{name}: saves/exports without an existing-file check or confirmation")

    if "MsgBox" not in joined:
        warns.append("module never reports to the user (no MsgBox)")
    return fails, warns


_TRIVIAL = re.compile(r"^\s*(Attribute\s|VERSION\s|BEGIN\b|END\s*$|MultiUse\s*=|Option\s+\w+)", re.I)


def _e_trivial(text: str) -> bool:
    """A document/class module that only holds the headers Office writes itself (no code)."""
    for raw in text.splitlines():
        code, _ = _strip_comment(raw)
        if code.strip() and not _TRIVIAL.match(code):
            return False
    return True


def _citeste_text(p) -> str:
    return Path(p).read_bytes().decode("cp1252", errors="replace")


def verifica_module(paths, quiet: bool = False):
    """Static check of exported/loose modules. Returns {file name: fails}; trivial document modules
    (only Attribute/Option lines) are skipped."""
    rez = {}
    for p in paths:
        p = Path(p)
        if p.suffix.lower() == ".frx":
            continue
        text = _citeste_text(p)
        if p.suffix.lower() in (".cls", ".frm") and _e_trivial(text):
            if not quiet:
                print(f"SKIP  {p.name}: empty document module")
            continue
        fails, warns = verifica_sursa(text, p.name)
        if not quiet:
            for f in fails:
                print(f"FAIL  {p.name}: {f}")
            for w in warns:
                print(f"WARN  {p.name}: {w}")
            print(f"RESULT {'FAIL' if fails else 'PASS'}  {p.name}  fails={len(fails)} warns={len(warns)}")
        rez[p.name] = fails
    return rez


def verifica(paths) -> int:
    bad = 0
    for p in paths:
        if Path(p).suffix.lower() in (".xlsm", ".xlsb", ".xls", ".xltm", ".docm", ".dotm", ".doc"):
            rez = verifica_fisier(p)
        else:
            rez = verifica_module([p])
        bad += any(rez.values())
    return 1 if bad else 0


# ----------------------------------------------------------------------------- AccessVBOM guard
def _key(app: str, base: str = VBOM_BASE) -> str:
    return rf"{base}\{app}\Security"


def _exista_cheie(path: str) -> bool:
    try:
        winreg.CloseKey(winreg.OpenKey(winreg.HKEY_CURRENT_USER, path))
        return True
    except FileNotFoundError:
        return False


def citeste_stare(app: str, base: str = VBOM_BASE) -> dict:
    """Full original state: keys missing on the path to Security, and the AccessVBOM value."""
    parts = _key(app, base).split("\\")
    missing = []
    for n in range(1, len(parts) + 1):
        sub = "\\".join(parts[:n])
        if not _exista_cheie(sub):
            missing = ["\\".join(parts[:k]) for k in range(n, len(parts) + 1)]
            break
    value = None
    if not missing:
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, _key(app, base)) as k:
                data, typ = winreg.QueryValueEx(k, "AccessVBOM")
            if isinstance(data, bytes):
                value = {"type": typ, "hex": data.hex()}
            else:
                value = {"type": typ, "data": data}
        except FileNotFoundError:
            value = None
    return {"missing_keys": missing, "value": value}


def citeste_vbom(app: str, base: str = VBOM_BASE):
    v = citeste_stare(app, base)["value"]
    if v is None:
        return _ABSENT
    return bytes.fromhex(v["hex"]) if "hex" in v else v["data"]


def _aplica_stare(app: str, base: str, stare: dict) -> None:
    path = _key(app, base)
    v = stare["value"]
    if v is None:
        if _exista_cheie(path):
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, path, 0, winreg.KEY_SET_VALUE) as k:
                with contextlib.suppress(FileNotFoundError):
                    winreg.DeleteValue(k, "AccessVBOM")
    else:
        data = bytes.fromhex(v["hex"]) if "hex" in v else v["data"]
        with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, path, 0, winreg.KEY_SET_VALUE) as k:
            winreg.SetValueEx(k, "AccessVBOM", 0, v["type"], data)
    for sub in reversed(stare["missing_keys"]):  # keys the guard created: remove them if still empty
        if not _exista_cheie(sub):
            continue
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, sub) as k:
            subkeys, values, _ = winreg.QueryInfoKey(k)
        if subkeys or values:
            break
        winreg.DeleteKey(winreg.HKEY_CURRENT_USER, sub)


def _start_proces(pid: int):
    """Process creation time (FILETIME as int), None if no such process, "denied" if not queryable."""
    import ctypes
    from ctypes import wintypes
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = wintypes.HANDLE
    h = k32.OpenProcess(0x1000, False, int(pid))  # PROCESS_QUERY_LIMITED_INFORMATION
    if not h:
        return "denied" if ctypes.get_last_error() == 5 else None
    try:
        code = wintypes.DWORD()
        if not k32.GetExitCodeProcess(h, ctypes.byref(code)) or code.value != 259:  # STILL_ACTIVE
            return None
        c, e, kt, ut = (wintypes.FILETIME() for _ in range(4))
        if not k32.GetProcessTimes(h, ctypes.byref(c), ctypes.byref(e), ctypes.byref(kt), ctypes.byref(ut)):
            return "denied"
        return (c.dwHighDateTime << 32) | c.dwLowDateTime
    finally:
        k32.CloseHandle(h)


def _proprietar_viu(owner) -> bool:
    if not isinstance(owner, dict) or "pid" not in owner:
        return False
    start = _start_proces(owner["pid"])
    if start == "denied":
        return True  # cannot prove it is dead: refuse rather than steal the lock
    return start is not None and start == owner.get("start")


class _Garda:
    """One AccessVBOM change for one app, serialized by an exclusive lock file."""

    def __init__(self, app: str, base: str = VBOM_BASE, state_dir=STATE_DIR):
        if app not in APPS:
            raise ValueError(f"unknown app {app!r}")
        self.app, self.base = app, base
        self.dir = Path(state_dir)
        self.lock = self.dir / f"{app}.lock"
        self.backup = self.dir / f"{app}.backup.json"
        self.orig = None
        self.activ = False
        self.eu = {"pid": os.getpid(), "start": _start_proces(os.getpid())}

    # -- lock
    def _citeste_lacat(self):
        try:
            return json.loads(self.lock.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return None

    def _ia_lacat(self) -> None:
        self.dir.mkdir(parents=True, exist_ok=True)
        for _ in range(5):
            try:
                fd = os.open(self.lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
            except FileExistsError:
                owner = self._citeste_lacat()
                if owner is None:
                    try:
                        age = time.time() - self.lock.stat().st_mtime
                    except FileNotFoundError:
                        continue
                    if age < 10:  # being written by its owner right now
                        raise RuntimeError(f"AccessVBOM for {self.app} is in use by another vba_ro run "
                                           f"(lock {self.lock} just created); refusing")
                elif _proprietar_viu(owner):
                    raise RuntimeError(f"AccessVBOM for {self.app} is in use by another vba_ro run "
                                       f"(PID {owner['pid']}); refusing - try again when it ends")
                self._recupereaza_orfan(owner)
                continue
            with os.fdopen(fd, "w", encoding="utf-8") as f:
                json.dump(self.eu, f)
            return
        raise RuntimeError(f"could not take the AccessVBOM lock for {self.app}")

    def _recupereaza_orfan(self, owner) -> None:
        """The lock owner is dead: move its lock aside atomically, restore its backup, drop both."""
        aside = self.lock.with_name(f"{self.lock.name}.stale.{os.getpid()}.{time.monotonic_ns()}")
        try:
            os.rename(self.lock, aside)
        except FileNotFoundError:
            return  # someone else recovered it first
        try:
            got = json.loads(aside.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            got = None
        if got != owner and got is not None and _proprietar_viu(got):
            # a live run replaced the stale lock between our read and the rename: give it back
            with contextlib.suppress(OSError):
                os.rename(aside, self.lock)
            raise RuntimeError(f"AccessVBOM for {self.app} is in use by PID {got.get('pid')}; refusing")
        self._restaureaza_backup()
        aside.unlink(missing_ok=True)

    def _restaureaza_backup(self):
        if not self.backup.exists():
            return None
        stare = json.loads(self.backup.read_text(encoding="utf-8"))
        _aplica_stare(self.app, self.base, stare)
        if citeste_stare(self.app, self.base)["value"] != stare["value"]:
            raise RuntimeError(f"AccessVBOM for {self.app} could not be restored from {self.backup}")
        self.backup.unlink()
        return stare

    # -- change and restore
    def ia(self):
        self._ia_lacat()
        try:
            self._restaureaza_backup()  # backup without a live owner (crash between steps)
            self.orig = citeste_stare(self.app, self.base)
            tmp = self.backup.with_suffix(".tmp")
            tmp.write_text(json.dumps(self.orig), encoding="utf-8")
            os.replace(tmp, self.backup)
            self.activ = True
            atexit.register(self.elibereaza, False)
            with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, _key(self.app, self.base), 0,
                                    winreg.KEY_SET_VALUE) as k:
                winreg.SetValueEx(k, "AccessVBOM", 0, winreg.REG_DWORD, 1)
        except BaseException:
            self.elibereaza(False)
            raise
        return self.orig

    def elibereaza(self, strict: bool = True) -> bool:
        """Restore the original state. strict=False never raises (an exception is already on its way):
        it reports on stderr and leaves lock + backup for `restaureaza-vbom`."""
        if not self.activ and self.orig is None:
            self._elibereaza_lacat()
            return True
        ok = False
        try:
            if self.orig is not None:
                _aplica_stare(self.app, self.base, self.orig)
                ok = citeste_stare(self.app, self.base) == self.orig
            if ok:
                self.backup.unlink(missing_ok=True)
                self._elibereaza_lacat()
        except Exception as e:  # noqa: BLE001 - reported below, never masks the caller's exception
            if strict:
                raise
            print(f"vba_ro: AccessVBOM restore for {self.app} failed: {e!r}; run restaureaza-vbom",
                  file=sys.stderr)
            return False
        finally:
            self.activ = False
            with contextlib.suppress(Exception):
                atexit.unregister(self.elibereaza)
        if not ok:
            msg = (f"AccessVBOM for {self.app} not restored (now {citeste_stare(self.app, self.base)}, "
                   f"expected {self.orig}); backup kept in {self.backup} - run restaureaza-vbom")
            if strict:
                raise RuntimeError(msg)
            print("vba_ro: " + msg, file=sys.stderr)
        self.orig = None
        return ok

    def _elibereaza_lacat(self):
        if self._citeste_lacat() == self.eu:
            self.lock.unlink(missing_ok=True)


@contextlib.contextmanager
def acces_vbom(app: str, base: str = VBOM_BASE, state_dir=STATE_DIR):
    """Temporarily set AccessVBOM=1 for Word or Excel; always restore the exact original state.
    Refuses (RuntimeError) while another run holds the same app. An exception from the body is never
    replaced by a restore error."""
    if winreg is None:
        raise RuntimeError("AccessVBOM guard needs Windows (winreg)")
    g = _Garda(app, base, state_dir)
    orig = g.ia()
    try:
        yield orig
    except BaseException:
        g.elibereaza(strict=False)
        raise
    else:
        g.elibereaza(strict=True)


def restaureaza_vbom(base: str = VBOM_BASE, state_dir=STATE_DIR) -> dict:
    """Restore what a crashed run left behind (idempotent). Live runs are left alone."""
    rez = {}
    for app in APPS:
        g = _Garda(app, base, state_dir)
        owner = g._citeste_lacat() if g.lock.exists() else None
        if owner is not None and _proprietar_viu(owner):
            rez[app] = f"in use by PID {owner['pid']} - not touched"
            continue
        if g.lock.exists():
            g._recupereaza_orfan(owner)
            rez[app] = "restored from the backup of a dead run"
        elif g.backup.exists():
            g._restaureaza_backup()
            rez[app] = "restored from backup"
    if LEGACY_BACKUP.exists() and base == VBOM_BASE:  # backup written by version 1 (DWORD only)
        for app, val in json.loads(LEGACY_BACKUP.read_text(encoding="utf-8")).items():
            _aplica_stare(app, base, {"missing_keys": [], "value": None if val == _ABSENT else
                                      {"type": winreg.REG_DWORD, "data": int(val)}})
            rez[app] = f"restored from legacy backup: {val}"
        LEGACY_BACKUP.unlink()
    return rez


# ----------------------------------------------------------------------------- COM helpers
def _app_name(path) -> str:
    ext = Path(path).suffix.lower()
    if ext in (".xlsm", ".xlsx", ".xlsb", ".xls", ".xltm"):
        return "Excel"
    if ext in (".docm", ".docx", ".doc", ".dotm"):
        return "Word"
    raise ValueError(f"unknown Office file type: {path}")


def _pids(exe: str) -> set:
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {exe}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    return {int(r.split('","')[1]) for r in out.splitlines() if r.startswith('"') and '","' in r}


@contextlib.contextmanager
def office(app: str, timeout: int = 300, securitate: int = SEC_FORCE_DISABLE):
    """Start a private, invisible Word/Excel with AutomationSecurity set first (default 3 = macros of
    every file it opens are disabled); quit it afterwards; kill it if it hangs (dialog)."""
    import pythoncom
    import win32com.client
    pythoncom.CoInitialize()
    exe = "EXCEL.EXE" if app == "Excel" else "WINWORD.EXE"
    before = _pids(exe)
    o = win32com.client.DispatchEx(f"{app}.Application")
    new = _pids(exe) - before
    done = threading.Event()

    def watchdog():
        if not done.wait(timeout):
            for pid in new:
                subprocess.run(["taskkill", "/F", "/PID", str(pid)], capture_output=True)
    threading.Thread(target=watchdog, daemon=True).start()
    try:
        o.AutomationSecurity = securitate  # before any file is opened
        if o.AutomationSecurity != securitate:
            raise RuntimeError(f"{app}: AutomationSecurity could not be set to {securitate}")
        o.Visible = False
        o.DisplayAlerts = 0 if app == "Word" else False
        yield o
    finally:
        done.set()
        with contextlib.suppress(Exception):
            o.Quit(0) if app == "Word" else o.Quit()
        o = None
        time.sleep(1)
        for pid in new & _pids(exe):  # still alive -> stop only the process we started
            subprocess.run(["taskkill", "/F", "/PID", str(pid)], capture_output=True)
        pythoncom.CoUninitialize()


def _crlf_copy(path, tmpdir) -> str:
    """The VBA editor needs CRLF line ends and cp1252 text; a git checkout may have LF."""
    raw = Path(path).read_bytes().replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    dest = Path(tmpdir) / Path(path).name
    dest.write_bytes(raw)
    return str(dest)


_EXT = {1: ".bas", 2: ".cls", 3: ".frm", 100: ".cls"}


def _exporta_proiect(proj, outdir) -> list:
    outdir = Path(outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    res = []
    for c in proj.VBComponents:
        if c.CodeModule.CountOfLines == 0:
            continue
        dest = outdir / (c.Name + _EXT.get(c.Type, ".txt"))
        c.Export(str(dest))
        res.append(dest)
    return res


def _deschide(o, app, path, read_only=False):
    if app == "Excel":
        return o.Workbooks.Open(str(path), UpdateLinks=0, ReadOnly=read_only, AddToMru=False)
    return o.Documents.Open(str(path), ReadOnly=read_only, AddToRecentFiles=False, Visible=False)


def _inchide(app, f):
    f.Close(False) if app == "Excel" else f.Close(0)


def construieste(out, module, baza=None) -> Path:
    """Build a .xlsm/.docm that contains the given .bas/.cls modules (needs the AccessVBOM guard).
    Every module passes the static check first; then the whole built project (with any code that came
    with --baza) is exported and checked again before anything is saved."""
    out = Path(out).resolve()
    app = _app_name(out)
    for m in module:
        fails, _ = verifica_sursa(_citeste_text(m))
        if fails:
            raise ValueError(f"{Path(m).name} fails the static check: {fails[:3]}")
    with tempfile.TemporaryDirectory(prefix="vba_ro_") as tmp:
        return _construieste(out, app, [_crlf_copy(m, tmp) for m in module], baza, Path(tmp))


def _construieste(out, app, module, baza, tmp) -> Path:
    with acces_vbom(app), office(app) as o:
        f = _deschide(o, app, Path(baza).resolve()) if baza else \
            (o.Workbooks.Add() if app == "Excel" else o.Documents.Add())
        try:
            for m in module:
                f.VBProject.VBComponents.Import(str(Path(m).resolve()))
            rez = verifica_module(_exporta_proiect(f.VBProject, tmp / "exportat"), quiet=True)
            bad = {k: v for k, v in rez.items() if v}
            if bad:
                raise ValueError(f"the built project fails the static check (nothing saved): {bad}")
            if app == "Excel":
                f.SaveAs(str(out), FileFormat=52)  # xlOpenXMLWorkbookMacroEnabled
            else:
                f.SaveAs2(str(out), FileFormat=13)  # wdFormatXMLDocumentMacroEnabled
        finally:
            with contextlib.suppress(Exception):
                _inchide(app, f)
    return out


def exporta(path, outdir) -> list:
    """Export every code module of a .xlsm/.docm to outdir for review (needs the AccessVBOM guard).
    The file is opened read-only with its macros disabled."""
    path, outdir = Path(path).resolve(), Path(outdir).resolve()
    app = _app_name(path)
    with acces_vbom(app), office(app) as o:
        f = _deschide(o, app, path, read_only=True)
        try:
            return _exporta_proiect(f.VBProject, outdir)
        finally:
            with contextlib.suppress(Exception):
                _inchide(app, f)


def verifica_fisier(path, quiet: bool = False) -> dict:
    """Export the VBA project of an Office file and run the static check on every module."""
    with tempfile.TemporaryDirectory(prefix="vba_ro_exp_") as tmp:
        return verifica_module(exporta(path, tmp), quiet=quiet)


def ruleaza(path, macro: str, *args, timeout: int = 300):
    """Check the file's code (export + static check), then open it in a private Office instance with
    macros enabled, run Module.Macro(args) and return its result. Refuses a file that fails."""
    path = Path(path).resolve()
    app = _app_name(path)
    bad = {k: v for k, v in verifica_fisier(path, quiet=True).items() if v}
    if bad:
        raise ValueError(f"{path.name} fails the static check; not running it: {bad}")
    with office(app, timeout, securitate=SEC_LOW) as o:
        if app == "Excel":
            o.EnableEvents = False  # no Workbook_Open while opening
            wb = _deschide(o, app, path)
            o.EnableEvents = True
            try:
                return o.Run(f"'{wb.Name}'!{macro}", *args)
            finally:
                wb.Close(False)
        with contextlib.suppress(Exception):
            o.WordBasic.DisableAutoMacros(1)
        doc = _deschide(o, app, path)
        try:
            return o.Run(macro, *args)
        finally:
            doc.Close(0)


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    cmd, rest = argv[0], argv[1:]
    if cmd == "verifica":
        return verifica(rest)
    if cmd == "construieste":
        baza = None
        if "--baza" in rest:
            i = rest.index("--baza")
            baza = rest[i + 1]
            rest = rest[:i] + rest[i + 2:]
        print(construieste(rest[0], rest[1:], baza))
        return 0
    if cmd == "ruleaza":
        print(ruleaza(rest[0], rest[1], *rest[2:]))
        return 0
    if cmd == "exporta":
        for p in exporta(rest[0], rest[1]):
            print(p)
        return 0
    if cmd == "stare-vbom":
        for app in APPS:
            print(f"{app}: AccessVBOM = {citeste_vbom(app)}")
        return 0
    if cmd == "restaureaza-vbom":
        print(restaureaza_vbom() or "nothing to restore")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
