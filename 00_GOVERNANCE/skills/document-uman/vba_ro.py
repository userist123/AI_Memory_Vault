"""VBA helpers for document-uman: static check, build .xlsm/.docm, run a macro, export modules.

Usage (from any folder; Windows + Microsoft Office 16 + pywin32):
  python -P -E <skill dir>\\vba_ro.py verifica <file.bas> [...]       static check, exit 1 on FAIL
  python -P -E <skill dir>\\vba_ro.py construieste <out.xlsm|out.docm> <mod.bas> [...] [--baza file]
  python -P -E <skill dir>\\vba_ro.py ruleaza <file.xlsm|file.docm> <Modul.Macro> [arg ...]
  python -P -E <skill dir>\\vba_ro.py exporta <file.xlsm|file.docm> <out dir>
  python -P -E <skill dir>\\vba_ro.py stare-vbom                      show AccessVBOM values
  python -P -E <skill dir>\\vba_ro.py restaureaza-vbom                restore after a crash

AccessVBOM guard: importing/exporting modules needs "Trust access to the VBA project object model"
(HKCU\\Software\\Microsoft\\Office\\16.0\\<App>\\Security\\AccessVBOM). `acces_vbom()` reads the
current value, writes a backup file, sets 1 only while the build runs and restores the original
value (deletes it if it was absent) in a finally block and at interpreter exit. VBAWarnings, the
macro security level and Trusted Locations are never touched. Running a macro (`ruleaza`) does not
need AccessVBOM.
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
import winreg
from pathlib import Path

OFFICE_VER = "16.0"
BACKUP = Path(tempfile.gettempdir()) / "vba_ro_vbom_backup.json"
_ABSENT = "absent"

# ----------------------------------------------------------------------------- static check
# (regex, message) - any match is a FAIL. Matched on code with comments and strings handled below.
FORBIDDEN = [
    (r"\bShell\s*\(|\bShell\s+\"", "Shell call (runs external programs)"),
    (r"WScript\.|\"WScript\.\w+\"|Wscript\.Shell", "WScript object (shell/registry access)"),
    (r"powershell|cmd\.exe|cmd\s*/c|mshta|rundll32|regsvr32|certutil|bitsadmin", "external tool name"),
    (r"URLDownloadToFile|XMLHTTP|WinHttp|ServerXMLHTTP|InternetExplorer\.Application|MSXML2|"
     r"Inet\w*\.|https?://|\bftp://", "network access"),
    (r"\bKill\s|\bRmDir\b|\.DeleteFile\b|\.DeleteFolder\b", "file/folder deletion"),
    (r"\bSaveSetting\b|\bDeleteSetting\b|RegWrite|RegDelete|StdRegProv", "registry write"),
    (r"\bSendKeys\b", "SendKeys"),
    (r"\bDeclare\s+(PtrSafe\s+)?(Function|Sub)\b", "Declare API call (allowed only with "
     "' vba_ro:allow-declare <documented need> on the same line)"),
    (r"\bSub\s+(Auto_?Open|Auto_?Close|Auto_?Exec|Auto_?New|Auto_?Exit)\b|"
     r"\bSub\s+(Document_Open|Document_New|Document_Close|Workbook_Open|Workbook_BeforeClose|"
     r"Workbook_Activate)\b", "auto-execution macro (macros must run only when the user starts them)"),
    (r"xlSheetVeryHidden|\.Visible\s*=\s*2\b", "very hidden sheet"),
    (r"\.VBProject\b|VBComponents|CodeModule", "code that edits VBA projects"),
    (r"\bExecuteExcel4Macro\b|\bMacScript\b|\bAppleScriptTask\b|\bCallByName\b|\bStrReverse\b",
     "dynamic execution / obfuscation primitive"),
    (r"\bGetObject\s*\(", "GetObject (binds to arbitrary running objects)"),
    (r"\.Protect\w*\s+Password|VBAProject\.Password", "password protection"),
]
# CreateObject is allowed only for these ProgIDs.
CREATEOBJECT_OK = {"scripting.dictionary", "scripting.filesystemobject", "word.application",
                   "excel.application"}
SETTINGS = ("ScreenUpdating", "EnableEvents", "Calculation", "DisplayAlerts", "AutomationSecurity",
            "PrintCommunication")


def _strip_comment(line: str) -> str:
    """Remove a trailing ' comment (outside string literals)."""
    out, inq = [], False
    for ch in line:
        if ch == '"':
            inq = not inq
        elif ch == "'" and not inq:
            break
        out.append(ch)
    return "".join(out)


def _procedures(code_lines):
    """Yield (kind, scope, name, has_args, body_lines) for each Sub/Function."""
    head = re.compile(r"^\s*(Public\s+|Private\s+|Friend\s+)?(Static\s+)?(Sub|Function)\s+(\w+)\s*\((.*?)\)",
                      re.I)
    cur = None
    for ln in code_lines:
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


def verifica_sursa(text: str, nume: str = "<sursa>"):
    """Return (fails, warns) for one VBA module source."""
    fails, warns = [], []
    lines = text.splitlines()
    code = [_strip_comment(l) for l in lines]
    joined = "\n".join(code)
    no_strings = re.sub(r'"[^"\n]*"', '""', joined)

    if not re.search(r"^\s*Option\s+Explicit\b", joined, re.I | re.M):
        fails.append("Option Explicit missing")
    head = "\n".join(lines[:40]).lower()
    for lab in ("scop:", "rulare:", "modific"):
        if lab not in head:
            fails.append(f"header comment without '{lab}' (purpose / how to run / what it changes)")

    for i, (raw, ln) in enumerate(zip(lines, code), 1):
        for rx, msg in FORBIDDEN:
            if re.search(rx, ln, re.I):
                if msg.startswith("Declare") and "vba_ro:allow-declare" in raw:
                    warns.append(f"line {i}: Declare with documented need: {raw.strip()[:80]}")
                    continue
                fails.append(f"line {i}: {msg}: {raw.strip()[:80]}")
        for prog in re.findall(r"CreateObject\s*\(\s*\"([^\"]+)\"", ln, re.I):
            if prog.lower() not in CREATEOBJECT_OK:
                fails.append(f"line {i}: CreateObject(\"{prog}\") not on the allow list")
        if re.search(r"CreateObject\s*\(\s*[^\"\s]", ln, re.I):
            fails.append(f"line {i}: CreateObject with a computed ProgID")
        if re.search(r"\.(Select|Activate)\b(?!\w)", ln) or re.search(r"\bSelection\.", ln):
            fails.append(f"line {i}: Select/Activate/Selection - work with object variables")
        if re.search(r"\bOn\s+Error\s+Resume\s+Next\b", ln, re.I) and not any(
                re.search(r"On\s+Error\s+GoTo", c, re.I) for c in code[i:i + 3]):
            warns.append(f"line {i}: On Error Resume Next - keep it to one statement, then On Error GoTo 0")
        if re.search(r"\b(Range|\.Range)\s*\(\s*\"[A-Z]{1,3}\$?\d+", ln) and "Const" not in ln:
            warns.append(f"line {i}: hard-coded cell address - use a named range or a Const")
        if re.search(r"[^\x00-\x7f]", raw):
            warns.append(f"line {i}: non-ASCII character - the VBA editor reads .bas in the ANSI code "
                         "page; use plain letters (or ChrW in one helper)")
    if len(re.findall(r"\bChrW?\s*\(", no_strings, re.I)) > 12:
        fails.append("many Chr()/ChrW() calls - looks like string obfuscation")
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


def verifica(paths) -> int:
    bad = 0
    for p in paths:
        p = Path(p)
        text = p.read_bytes().decode("cp1252", errors="replace")
        fails, warns = verifica_sursa(text, p.name)
        for f in fails:
            print(f"FAIL  {p.name}: {f}")
        for w in warns:
            print(f"WARN  {p.name}: {w}")
        print(f"RESULT {'FAIL' if fails else 'PASS'}  {p.name}  fails={len(fails)} warns={len(warns)}")
        bad += bool(fails)
    return 1 if bad else 0


# ----------------------------------------------------------------------------- AccessVBOM guard
def _key(app: str) -> str:
    return rf"Software\Microsoft\Office\{OFFICE_VER}\{app}\Security"


def citeste_vbom(app: str):
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, _key(app)) as k:
            return winreg.QueryValueEx(k, "AccessVBOM")[0]
    except FileNotFoundError:
        return _ABSENT


def _scrie_vbom(app: str, val):
    with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, _key(app), 0, winreg.KEY_SET_VALUE) as k:
        if val == _ABSENT:
            with contextlib.suppress(FileNotFoundError):
                winreg.DeleteValue(k, "AccessVBOM")
        else:
            winreg.SetValueEx(k, "AccessVBOM", 0, winreg.REG_DWORD, int(val))


def restaureaza_vbom() -> dict:
    """Restore values saved in the backup file (idempotent). Returns {app: restored value}."""
    if not BACKUP.exists():
        return {}
    data = json.loads(BACKUP.read_text(encoding="utf-8"))
    for app, val in data.items():
        _scrie_vbom(app, val)
    BACKUP.unlink()
    return data


@contextlib.contextmanager
def acces_vbom(app: str):
    """Temporarily set AccessVBOM=1 for Word or Excel; always restore the original value."""
    restaureaza_vbom()  # leftovers of a crashed run first
    orig = citeste_vbom(app)
    BACKUP.write_text(json.dumps({app: orig}), encoding="utf-8")
    atexit.register(restaureaza_vbom)
    try:
        _scrie_vbom(app, 1)
        yield orig
    finally:
        restaureaza_vbom()
        assert citeste_vbom(app) == orig, "AccessVBOM not restored"


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
def office(app: str, timeout: int = 300):
    """Start a private, invisible Word/Excel; quit it afterwards; kill it if it hangs (dialog)."""
    import pythoncom
    import win32com.client
    pythoncom.CoInitialize()
    exe = "EXCEL.EXE" if app == "Excel" else "WINWORD.EXE"
    before = _pids(exe)
    o = win32com.client.DispatchEx(f"{app}.Application")
    o.Visible = False
    o.DisplayAlerts = 0 if app == "Word" else False
    new = _pids(exe) - before
    done = threading.Event()

    def watchdog():
        if not done.wait(timeout):
            for pid in new:
                subprocess.run(["taskkill", "/F", "/PID", str(pid)], capture_output=True)
    threading.Thread(target=watchdog, daemon=True).start()
    try:
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


def construieste(out, module, baza=None) -> Path:
    """Build a .xlsm/.docm that contains the given .bas/.cls modules (needs the AccessVBOM guard).
    Every module must pass the static check first."""
    out = Path(out).resolve()
    app = _app_name(out)
    for m in module:
        fails, _ = verifica_sursa(Path(m).read_bytes().decode("cp1252", errors="replace"))
        if fails:
            raise ValueError(f"{Path(m).name} fails the static check: {fails[:3]}")
    tmp = tempfile.mkdtemp(prefix="vba_ro_")
    module = [_crlf_copy(m, tmp) for m in module]
    with acces_vbom(app), office(app) as o:
        if app == "Excel":
            wb = o.Workbooks.Open(str(Path(baza).resolve())) if baza else o.Workbooks.Add()
            for m in module:
                wb.VBProject.VBComponents.Import(str(Path(m).resolve()))
            wb.SaveAs(str(out), FileFormat=52)  # xlOpenXMLWorkbookMacroEnabled
            wb.Close(False)
        else:
            doc = o.Documents.Open(str(Path(baza).resolve())) if baza else o.Documents.Add()
            for m in module:
                doc.VBProject.VBComponents.Import(str(Path(m).resolve()))
            doc.SaveAs2(str(out), FileFormat=13)  # wdFormatXMLDocumentMacroEnabled
            doc.Close(0)
    return out


def ruleaza(path, macro: str, *args, timeout: int = 300):
    """Open the file in a private Office instance, run Module.Macro(args) and return its result."""
    path = Path(path).resolve()
    app = _app_name(path)
    with office(app, timeout) as o:
        if app == "Excel":
            wb = o.Workbooks.Open(str(path))
            res = o.Run(f"'{wb.Name}'!{macro}", *args)
            wb.Close(False)
        else:
            doc = o.Documents.Open(str(path), AddToRecentFiles=False)
            res = o.Run(macro, *args)
            doc.Close(0)
    return res


def exporta(path, outdir) -> list:
    """Export every code module of a .xlsm/.docm to outdir for review (needs the AccessVBOM guard)."""
    path, outdir = Path(path).resolve(), Path(outdir).resolve()
    outdir.mkdir(parents=True, exist_ok=True)
    app = _app_name(path)
    ext = {1: ".bas", 2: ".cls", 3: ".frm", 100: ".cls"}
    res = []
    with acces_vbom(app), office(app) as o:
        f = o.Workbooks.Open(str(path), ReadOnly=True) if app == "Excel" else \
            o.Documents.Open(str(path), ReadOnly=True, AddToRecentFiles=False)
        for c in f.VBProject.VBComponents:
            if c.CodeModule.CountOfLines == 0:
                continue
            dest = outdir / (c.Name + ext.get(c.Type, ".txt"))
            c.Export(str(dest))
            res.append(dest)
        f.Close(False) if app == "Excel" else f.Close(0)
    return res


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
        for app in ("Word", "Excel"):
            print(f"{app}: AccessVBOM = {citeste_vbom(app)}")
        return 0
    if cmd == "restaureaza-vbom":
        print(restaureaza_vbom() or "nothing to restore")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
