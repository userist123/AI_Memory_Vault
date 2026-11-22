"""Tests for the document-uman VBA helper (00_GOVERNANCE/skills/document-uman/vba_ro.py).

Static checker: every known bypass probe must FAIL (or WARN where the rule is a warning) and all
eight shipped templates must PASS. AccessVBOM guard (Windows only): every single-run path and the
concurrency order, against a throw-away HKCU key - the real Office keys are never touched.
"""
from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
import time
import uuid
from pathlib import Path

import pytest

SKILL = Path(__file__).resolve().parents[1] / "00_GOVERNANCE" / "skills" / "document-uman"
_spec = importlib.util.spec_from_file_location("vba_ro_under_test", SKILL / "vba_ro.py")
vba_ro = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(vba_ro)

TEMPLATES = sorted((SKILL / "vba_templates").glob("*.bas"))


def modul(body: str, decl: str = "") -> str:
    return (
        'Attribute VB_Name = "Proba"\n'
        "' Scop: proba pentru verificator\n"
        "' Rulare: test automat\n"
        "' Modifica: nimic\n"
        "Option Explicit\n"
        f"{decl}\n"
        "Public Sub Proba()\n"
        "    Dim x As Variant, f As Object, fso As Object\n"
        "    On Error GoTo Eroare\n"
        f"{body}\n"
        '    MsgBox "gata"\n'
        "    Exit Sub\n"
        "Eroare:\n"
        "    MsgBox Err.Description\n"
        "End Sub\n"
    )


# ----------------------------------------------------------------------------- static checker
def test_baseline_module_passes_clean():
    fails, warns = vba_ro.verifica_sursa(modul("    x = 1"))
    assert fails == [] and warns == []


@pytest.mark.parametrize("tpl", TEMPLATES, ids=lambda p: p.stem)
def test_templates_pass(tpl):
    fails, _ = vba_ro.verifica_sursa(vba_ro._citeste_text(tpl), tpl.name)
    assert fails == [], fails


def test_all_eight_templates_present():
    assert len(TEMPLATES) == 8


FAIL_PROBES = {
    "shell_variable_arg": ("    Shell x", ""),
    "shell_qualified": ("    x = VBA.Interaction.Shell(x)", ""),
    "shell_across_continuation": ("    x = VBA.Interaction _\n        .Shell(x)", ""),
    "kill_paren": ("    Kill(x)", ""),
    "kill_across_continuation": ("    Kill _\n        x", ""),
    "rmdir": ("    RmDir x", ""),
    "run_on_object": ("    f.Run x", ""),
    "application_run_computed": ("    Application.Run x", ""),
    "evaluate": ("    x = Application.Evaluate(x)", ""),
    "evaluate_bare": ("    x = Evaluate(x)", ""),
    "bracket_evaluation": ("    x = [A1]", ""),
    "bracket_call": ('    x = [CALL("k","W","JCJ","c",0)]', ""),
    "ontime": ('    Application.OnTime Now, "Proba"', ""),
    "onkey": ('    Application.OnKey "^a", "Proba"', ""),
    "dde": ('    x = DDEInitiate("a", "b")', ""),
    "followhyperlink": ("    ThisWorkbook.FollowHyperlink x", ""),
    "unc_path": ('    x = "\\\\server\\share\\a.docx"', ""),  # hygiene: intentional-absolute-path
    "addins_add": ("    AddIns.Add x", ""),
    "addin_installed": ("    f.Installed = True", ""),
    "name_as_statement": ('    Name "a.txt" As "b.txt"', ""),
    "name_as_after_colon": ('    x = 1: Name x As "b.txt"', ""),
    "very_hidden_xl": ("    f.Visible = xlVeryHidden", ""),
    "very_hidden_named_arg": ("    f.Visible := 2", ""),
    "chrb": ("    x = ChrB(65)", ""),
    "chr_dollar_chain": ("    x = Chr$(99) & Chr$(97) & Chr$(108)", ""),
    "many_chr_calls": ("\n".join(f"    x = Chr({i})" for i in range(65, 73)), ""),
    "createobject_shell_app_continuation": ('    Set f = CreateObject( _\n        "Shell.Application")', ""),
    "createobject_concatenated_progid": ('    Set f = CreateObject("Scripting.Dictionary" & x)', ""),
    "createobject_split_progid": ('    Set f = CreateObject("WScr" & "ipt.Shell")', ""),
    "createobject_computed": ("    Set f = CreateObject(x)", ""),
    "declare_across_continuation": ("    x = 1",
                                    'Private Declare PtrSafe _\n    Function GetTickCount Lib "kernel32" () As Long'),
    "fso_file_var_delete": ("    Set f = fso.GetFile(x)\n    f.Delete", ""),
    "fso_getfile_delete_inline": ("    fso.GetFile(x).Delete", ""),
    "fso_files_loop_delete": ("    For Each f In fso.GetFolder(x).Files\n        f.Delete\n    Next f", ""),
    "fso_with_delete": ("    With fso.GetFile(x)\n        .Delete\n    End With", ""),
    "fso_typed_var_delete": ("    Dim g As Scripting.File\n    g.Delete", ""),
    "startup_path_property": ("    x = Application.StartupPath", ""),
    "xlstart_path": ('    x = Environ$("APPDATA") & "\\Microsoft\\Excel\\XLSTART\\a.xlsm"', ""),  # hygiene: intentional-absolute-path
    "windows_startup_folder": ('    x = "C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\a"', ""),  # hygiene: intentional-absolute-path
    "normal_template": ("    NormalTemplate.Save", ""),
    "automation_security_low": ("    Application.AutomationSecurity = 1", ""),
    "auto_open_across_continuation": ("    x = 1", "Private Sub _\n    Workbook_Open()\nEnd Sub"),
    "activex_painted_event": ("    x = 1", "Private Sub InkPicture1_Painted(ByVal h As Long)\nEnd Sub"),
    "vbe_access": ("    Set f = Application.VBE", ""),
    "getobject": ('    Set f = GetObject("winmgmts:")', ""),
    "wscript_in_string": ('    x = "WScript.Shell"', ""),
    "powershell_in_string": ('    x = "powershell -nop"', ""),
    "network_url": ('    x = "https://example.invalid/a"', ""),
    "comment_continuation_is_still_scanned": ("    x = 1 ' nota _\n    Shell x", ""),
    "sendkeys": ('    SendKeys "x"', ""),
    "registry_write": ('    SaveSetting "a", "b", "c", "d"', ""),
    "excel4_macro_sheet": ("    ThisWorkbook.Sheets.Add Type:=xlExcel4MacroSheet", ""),
}


@pytest.mark.parametrize("name", sorted(FAIL_PROBES))
def test_bypass_probe_fails(name):
    body, decl = FAIL_PROBES[name]
    fails, _ = vba_ro.verifica_sursa(modul(body, decl))
    assert fails, f"probe {name} was not caught"


WARN_PROBES = {
    "application_run_literal": ('    Application.Run "Modul.Macro"', "Application.Run"),
    "sheet_delete": ("    ThisWorkbook.Worksheets(1).Delete", ".Delete"),
    "open_for_output": ("    Open x For Output As #1\n    Close #1", "Open ... For"),
    "open_for_append": ("    Open x For Append As #1\n    Close #1", "Open ... For"),
    "create_text_file": ("    Set f = fso.CreateTextFile(x)", "text file"),
    "filecopy": ("    FileCopy x, x", "copies/moves"),
    "declare_with_documented_need": (
        "    x = 1", None),
}


@pytest.mark.parametrize("name", sorted(WARN_PROBES))
def test_warning_probe_warns_not_fails(name):
    body, needle = WARN_PROBES[name]
    decl = ""
    if name == "declare_with_documented_need":
        decl = ('Private Declare PtrSafe Function GetTickCount Lib "kernel32" () As Long '
                "' vba_ro:allow-declare masurare timp")
        needle = "Declare with documented need"
    fails, warns = vba_ro.verifica_sursa(modul(body, decl))
    assert fails == [], fails
    assert any(needle in w for w in warns), warns


def test_declare_without_marker_fails():
    decl = 'Private Declare PtrSafe Function GetTickCount Lib "kernel32" () As Long'
    fails, _ = vba_ro.verifica_sursa(modul("    x = 1", decl))
    assert any("Declare" in f for f in fails)


def test_keywords_inside_messages_do_not_trip_code_rules():
    fails, _ = vba_ro.verifica_sursa(modul('    MsgBox "Kill Evaluate Name x As y DDE"'))
    assert fails == []


def test_trivial_document_module_is_skipped(tmp_path):
    cls = tmp_path / "ThisWorkbook.cls"
    cls.write_text('VERSION 1.0 CLASS\nBEGIN\n  MultiUse = -1  \'True\nEND\nAttribute VB_Name = "ThisWorkbook"\n'
                   "Option Explicit\n", encoding="ascii")
    assert vba_ro.verifica_module([cls], quiet=True) == {}
    cls.write_text(cls.read_text() + "Private Sub Workbook_Open()\nEnd Sub\n", encoding="ascii")
    rez = vba_ro.verifica_module([cls], quiet=True)
    assert any("auto-execution" in f for f in rez["ThisWorkbook.cls"])


# ----------------------------------------------------------------------------- AccessVBOM guard
win = pytest.mark.skipif(sys.platform != "win32", reason="registry guard is Windows-only")


@pytest.fixture
def reg(tmp_path):
    """A scratch HKCU base path (Software\\vba_ro_test\\<uuid>) and state dir; removed afterwards."""
    import winreg
    root = r"Software\vba_ro_test"
    base = rf"{root}\{uuid.uuid4().hex}"

    def delete_tree(path):
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, path) as k:
                subs = []
                i = 0
                while True:
                    try:
                        subs.append(winreg.EnumKey(k, i))
                        i += 1
                    except OSError:
                        break
            for s in subs:
                delete_tree(path + "\\" + s)
            winreg.DeleteKey(winreg.HKEY_CURRENT_USER, path)
        except FileNotFoundError:
            pass

    yield {"base": base, "state": tmp_path / "state", "winreg": winreg}
    delete_tree(base)
    try:  # the shared root goes only when empty
        winreg.DeleteKey(winreg.HKEY_CURRENT_USER, root)
    except OSError:
        pass


def _sec(r):
    return vba_ro._key("Word", r["base"])


def _set(r, typ, data):
    w = r["winreg"]
    with w.CreateKeyEx(w.HKEY_CURRENT_USER, _sec(r), 0, w.KEY_SET_VALUE) as k:
        w.SetValueEx(k, "AccessVBOM", 0, typ, data)


def _get(r):
    w = r["winreg"]
    try:
        with w.OpenKey(w.HKEY_CURRENT_USER, _sec(r)) as k:
            return w.QueryValueEx(k, "AccessVBOM")
    except FileNotFoundError:
        return None


def _guard(r):
    return vba_ro.acces_vbom("Word", base=r["base"], state_dir=r["state"])


def _assert_clean_state_dir(r):
    left = sorted(p.name for p in r["state"].glob("*")) if r["state"].exists() else []
    assert left == [], left


@win
def test_guard_absent_key_is_removed_again(reg):
    assert not vba_ro._exista_cheie(_sec(reg))
    with _guard(reg):
        assert _get(reg) == (1, reg["winreg"].REG_DWORD)
    assert not vba_ro._exista_cheie(_sec(reg))
    assert not vba_ro._exista_cheie(reg["base"])
    _assert_clean_state_dir(reg)


@win
def test_guard_absent_value_key_kept(reg):
    w = reg["winreg"]
    w.CloseKey(w.CreateKey(w.HKEY_CURRENT_USER, _sec(reg)))
    with _guard(reg):
        assert _get(reg)[0] == 1
    assert _get(reg) is None and vba_ro._exista_cheie(_sec(reg))
    _assert_clean_state_dir(reg)


@win
@pytest.mark.parametrize("typ_name,data", [("REG_DWORD", 0), ("REG_DWORD", 1), ("REG_SZ", "0"), ("REG_SZ", "1")])
def test_guard_restores_value_and_type(reg, typ_name, data):
    typ = getattr(reg["winreg"], typ_name)
    _set(reg, typ, data)
    with _guard(reg) as orig:
        assert _get(reg) == (1, reg["winreg"].REG_DWORD)
        assert orig["value"] == {"type": typ, "data": data}
    assert _get(reg) == (data, typ)
    _assert_clean_state_dir(reg)


class _Boom(Exception):
    pass


@win
def test_guard_exception_in_body_propagates_and_restores(reg):
    _set(reg, reg["winreg"].REG_DWORD, 0)
    with pytest.raises(_Boom):
        with _guard(reg):
            raise _Boom("body failed")
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)
    _assert_clean_state_dir(reg)


@win
def test_guard_restore_failure_does_not_mask_body_exception(reg, monkeypatch, capsys):
    _set(reg, reg["winreg"].REG_DWORD, 0)
    real = vba_ro._aplica_stare

    def broken(*a, **k):
        raise PermissionError("simulated registry failure")
    with pytest.raises(_Boom):
        with _guard(reg):
            monkeypatch.setattr(vba_ro, "_aplica_stare", broken)
            raise _Boom("body failed")
    assert "restaureaza-vbom" in capsys.readouterr().err
    assert (reg["state"] / "Word.backup.json").exists()  # kept for recovery
    monkeypatch.setattr(vba_ro, "_aplica_stare", real)
    vba_ro.restaureaza_vbom(base=reg["base"], state_dir=reg["state"])  # lock owner is this live PID
    assert _get(reg)[0] == 1  # live owner: not touched


@win
def test_guard_restore_failure_without_body_exception_raises(reg, monkeypatch):
    _set(reg, reg["winreg"].REG_DWORD, 0)
    with pytest.raises(PermissionError):
        with _guard(reg):
            monkeypatch.setattr(vba_ro, "_aplica_stare",
                                lambda *a, **k: (_ for _ in ()).throw(PermissionError("x")))


def _dead_pid():
    p = subprocess.Popen([sys.executable, "-c", "pass"])
    p.wait()
    return p.pid


@win
def test_stale_lock_and_backup_of_dead_run_are_recovered(reg):
    # a crashed run: value forced to 1, its lock and its backup of the original (DWORD 0) left behind
    _set(reg, reg["winreg"].REG_DWORD, 1)
    reg["state"].mkdir(parents=True)
    (reg["state"] / "Word.lock").write_text(json.dumps({"pid": _dead_pid(), "start": 1}), encoding="utf-8")
    (reg["state"] / "Word.backup.json").write_text(json.dumps(
        {"missing_keys": [], "value": {"type": reg["winreg"].REG_DWORD, "data": 0}}), encoding="utf-8")
    with _guard(reg) as orig:
        assert orig["value"]["data"] == 0  # the true original, not the crashed run's 1
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)
    _assert_clean_state_dir(reg)


@win
def test_stale_backup_without_lock_is_restored_first(reg):
    _set(reg, reg["winreg"].REG_DWORD, 1)
    reg["state"].mkdir(parents=True)
    (reg["state"] / "Word.backup.json").write_text(json.dumps(
        {"missing_keys": [], "value": None}), encoding="utf-8")
    with _guard(reg) as orig:
        assert orig["value"] is None
    assert _get(reg) is None
    _assert_clean_state_dir(reg)


@win
def test_restaureaza_vbom_recovers_dead_run(reg):
    _set(reg, reg["winreg"].REG_SZ, "1")
    reg["state"].mkdir(parents=True)
    (reg["state"] / "Word.lock").write_text(json.dumps({"pid": _dead_pid(), "start": 1}), encoding="utf-8")
    (reg["state"] / "Word.backup.json").write_text(json.dumps(
        {"missing_keys": [], "value": {"type": reg["winreg"].REG_SZ, "data": "0"}}), encoding="utf-8")
    rez = vba_ro.restaureaza_vbom(base=reg["base"], state_dir=reg["state"])
    assert "Word" in rez and _get(reg) == ("0", reg["winreg"].REG_SZ)
    _assert_clean_state_dir(reg)


@win
def test_second_guard_in_same_process_is_refused(reg):
    _set(reg, reg["winreg"].REG_DWORD, 0)
    with _guard(reg):
        with pytest.raises(RuntimeError, match="in use"):
            with _guard(reg):
                pass
        assert _get(reg)[0] == 1  # the refused attempt changed nothing
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)
    _assert_clean_state_dir(reg)


HOLDER = r"""
import importlib.util, sys, time
from pathlib import Path
spec = importlib.util.spec_from_file_location("v", sys.argv[1]); v = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v)
with v.acces_vbom("Word", base=sys.argv[2], state_dir=sys.argv[3]):
    Path(sys.argv[4]).write_text("ready")
    for _ in range(600):
        if Path(sys.argv[5]).exists():
            break
        time.sleep(0.05)
"""


@win
def test_concurrent_runs_are_serialized_and_value_restored(reg, tmp_path):
    """Order that used to leave AccessVBOM=1: A sets 1, B starts (and backed up "1"), A restores,
    B restores the wrong value. Now B is refused while A is alive and the original survives."""
    _set(reg, reg["winreg"].REG_DWORD, 0)
    ready, go = tmp_path / "ready", tmp_path / "go"
    a = subprocess.Popen([sys.executable, "-c", HOLDER, str(SKILL / "vba_ro.py"), reg["base"],
                          str(reg["state"]), str(ready), str(go)])
    try:
        for _ in range(400):
            if ready.exists():
                break
            time.sleep(0.05)
        assert ready.exists(), "holder did not start"
        assert _get(reg)[0] == 1
        with pytest.raises(RuntimeError, match=f"PID {a.pid}"):
            with _guard(reg):
                pass
        assert vba_ro.restaureaza_vbom(base=reg["base"], state_dir=reg["state"])["Word"].startswith("in use")
        assert _get(reg)[0] == 1  # neither the refused run nor restaureaza-vbom touched the live run
    finally:
        go.write_text("go")
        assert a.wait(30) == 0
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)
    with _guard(reg):  # free again
        assert _get(reg)[0] == 1
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)
    _assert_clean_state_dir(reg)


@win
def test_killed_holder_is_recovered_by_next_run(reg, tmp_path):
    _set(reg, reg["winreg"].REG_SZ, "0")
    ready, go = tmp_path / "ready", tmp_path / "never"
    a = subprocess.Popen([sys.executable, "-c", HOLDER, str(SKILL / "vba_ro.py"), reg["base"],
                          str(reg["state"]), str(ready), str(go)])
    for _ in range(400):
        if ready.exists():
            break
        time.sleep(0.05)
    assert ready.exists()
    a.kill()
    a.wait(30)
    assert _get(reg)[0] == 1  # killed mid-run: finally never ran
    with _guard(reg) as orig:
        assert orig["value"] == {"type": reg["winreg"].REG_SZ, "data": "0"}
    assert _get(reg) == ("0", reg["winreg"].REG_SZ)
    _assert_clean_state_dir(reg)


@win
def test_guard_never_uses_the_real_office_key_in_tests(reg):
    assert "Microsoft\\Office" not in _sec(reg)
    state = Path(reg["state"]).resolve()
    real = Path(vba_ro.STATE_DIR).resolve()
    assert state != real and real not in state.parents


# ----------------------------------------------------------------------------- checker rules, round 3
BS = chr(92)
EXTRA_FAIL_PROBES = {
    # AutomationSecurity: only the literal 3 (or restoring a value the procedure itself saved)
    "autosec_two": ("    Application.AutomationSecurity = 2", ""),
    "autosec_variable": ("    Application.AutomationSecurity = x", ""),
    "autosec_named_low": ("    Application.AutomationSecurity = msoAutomationSecurityLow", ""),
    "autosec_after_then": ("    If x = 1 Then Application.AutomationSecurity = 1", ""),
    "autosec_on_new_instance_low": ('    Set f = CreateObject("Excel.Application")\n    f.AutomationSecurity = 1', ""),
    "autosec_restore_without_save": ("    Application.AutomationSecurity = 3\n    Application.AutomationSecurity = x", ""),
    # XLM
    "xlm_exec_string": ('    x = "=EXEC(1)"', ""),
    "xlm_call_string": ('    x = "=CALL(1)"', ""),
    "xlm_register_string": ('    x = "=REGISTER(1)"', ""),
    "xlm_concatenated": ('    x = "=EX" & "EC(1)"', ""),
    "xlm_sheets_add_type3": ("    ThisWorkbook.Sheets.Add Type:=3", ""),
    "xlm_worksheets_add_type4": ("    ThisWorkbook.Worksheets.Add(Type:=4)", ""),
    "xlm_sheets_add_positional": ("    ThisWorkbook.Sheets.Add , , 1, 3", ""),
    "xlm_intl_macro_sheet": ("    ThisWorkbook.Sheets.Add Type:=xlExcel4IntlMacroSheet", ""),
    "names_add_auto_open": ('    ThisWorkbook.Names.Add "Auto_Open", "=x"', ""),
    "names_add_auto_concatenated": ('    ThisWorkbook.Names.Add Name:="Auto" & "_Open", RefersTo:="=x"', ""),
    "names_add_auto_with_block": ('    With ThisWorkbook.Names\n        .Add "Auto_Close", "=x"\n    End With', ""),
    # startup persistence
    "startup_word_default_path": ("    x = Options.DefaultFilePath(wdStartupPath)", ""),
    "startup_const_only": ("    x = wdStartupPath", ""),
    "startup_xlstart_split": ('    x = "XLS" & "TART"', ""),
    "startup_start_menu_split": ('    x = "Start" & " Menu"', ""),
    "startup_folder_split": ('    x = "C:' + BS + 'a' + BS + 'Startu" & "p' + BS + 'b.xlsm"', ""),  # hygiene: intentional-absolute-path
    "startup_savecopyas": ('    ThisWorkbook.SaveCopyAs Environ("APPDATA") & "' + BS + 'Microsoft' + BS + 'Excel' + BS + 'XLS" & "TART' + BS + 'a.xlsm"', ""),  # hygiene: intentional-absolute-path
    "startup_copyfile": ('    fso.CopyFile x, "C:' + BS + 'Startup' + BS + '"', ""),  # hygiene: intentional-absolute-path
    "startup_createtextfile": ('    Set f = fso.CreateTextFile("XLS" & "TART' + BS + 'a.txt")', ""),
    "startup_open_for_output": ('    Open "XLS" & "TART' + BS + 'a.txt" For Output As #1', ""),  # hygiene: intentional-absolute-path
    # network
    "unc_two_literals_concatenated": ('    x = "' + BS + '" & "' + BS + 'srv' + BS + 'share"', ""),  # hygiene: intentional-absolute-path
    "unc_chr92_chr92": ('    x = Chr(92) & Chr(92) & "srv"', ""),
    "unc_chr92_hex": ('    x = Chr(&H5C) & Chr(&H5C) & "srv"', ""),
    "unc_prefix_concatenated": ('    x = "' + BS + BS + '" & "srv"', ""),  # hygiene: intentional-absolute-path
    "follow_method": ("    f.Follow", ""),
    "hyperlinks_follow": ("    ActiveDocument.Hyperlinks(1).Follow", ""),
    "querytables_add": ("    ActiveSheet.QueryTables.Add x, x", ""),
    "connections_add": ("    ThisWorkbook.Connections.Add x, x, x", ""),
    "mailmerge_without_marker": ("    ActiveDocument.MailMerge.OpenDataSource Name:=x", ""),
    "fields_add_include": ('    ActiveDocument.Fields.Add f, -1, "INCLUDETEXT x"', ""),
    "fields_add_include_split": ('    ActiveDocument.Fields.Add f, -1, "INCLU" & "DETEXT x"', ""),
    "fields_add_link": ('    ActiveDocument.Fields.Add f, -1, "LINK Excel.Sheet.12 x"', ""),
    "fields_add_dde_split": ('    ActiveDocument.Fields.Add f, -1, "DD" & "E x"', ""),
    # event handlers
    "event_workbook_sheetchange": ("    x = 1", "Private Sub Workbook_SheetChange(ByVal s As Object)\nEnd Sub"),
    "event_worksheet_change": ("    x = 1", "Private Sub Worksheet_Change(ByVal t As Object)\nEnd Sub"),
    "event_document_contentcontrol": ("    x = 1", "Private Sub Document_ContentControlOnEnter(ByVal c As Object)\nEnd Sub"),
    "event_app_documentopen": ("    x = 1", "Private Sub App_DocumentOpen(ByVal d As Object)\nEnd Sub"),
    "event_withevents": ("    x = 1", "Private WithEvents app As Application"),
    # OLE
    "ole_inlineshapes": ('    ActiveDocument.InlineShapes.AddOLEObject ClassType:="x"', ""),
    "ole_shapes": ('    ActiveDocument.Shapes.AddOLEObject ClassType:="x"', ""),
}


@pytest.mark.parametrize("name", sorted(EXTRA_FAIL_PROBES))
def test_round3_probe_fails(name):
    body, decl = EXTRA_FAIL_PROBES[name]
    fails, _ = vba_ro.verifica_sursa(modul(body, decl))
    assert fails, f"probe {name} was not caught"


def test_automation_security_literal_3_and_saved_restore_pass():
    ok = ("    Dim sec As Long\n    sec = Application.AutomationSecurity\n"
          "    Application.AutomationSecurity = 3\n    Application.AutomationSecurity = sec")
    fails, _ = vba_ro.verifica_sursa(modul(ok))
    assert fails == [], fails
    fails, _ = vba_ro.verifica_sursa(modul("    If Application.AutomationSecurity = 2 Then x = 1"))
    assert fails == [], fails


def test_createobject_office_warns_unless_security_forced():
    _, warns = vba_ro.verifica_sursa(modul('    Set f = CreateObject("Excel.Application")'))
    assert any("AutomationSecurity = 3" in w for w in warns), warns
    _, warns = vba_ro.verifica_sursa(modul('    Set f = CreateObject("Word.Application")\n    f.AutomationSecurity = 3'))
    assert not any("defaults to Low" in w for w in warns), warns
    fails, _ = vba_ro.verifica_sursa(modul('    Set f = CreateObject("Word.Application")\n    f.AutomationSecurity = 3'))
    assert fails == [], fails
    _, warns = vba_ro.verifica_sursa(modul('    f.AutomationSecurity = 3\n    Set f = CreateObject("Word.Application")'))
    assert any("defaults to Low" in w for w in warns), warns  # the 3 comes before the CreateObject


def test_mailmerge_with_marker_warns_only():
    body = "    ActiveDocument.MailMerge.OpenDataSource Name:=x ' vba_ro:allow-mailmerge registru ales de utilizator"
    fails, warns = vba_ro.verifica_sursa(modul(body))
    assert fails == [], fails
    assert any("OpenDataSource" in w for w in warns), warns


def test_documents_add_nonliteral_template_warns():
    _, warns = vba_ro.verifica_sursa(modul("    Documents.Add Template:=x"))
    assert any("non-literal Template" in w for w in warns), warns
    _, warns = vba_ro.verifica_sursa(modul('    Documents.Add Template:="Normal"'))
    assert not any("non-literal Template" in w for w in warns), warns


def test_messages_with_event_like_words_do_not_trip():
    fails, _ = vba_ro.verifica_sursa(modul('    MsgBox "Worksheet_Change CALL and EXEC words, link, include"'))
    assert fails == [], fails


# ----------------------------------------------------------------------------- guard: D1 (fixed state dir)
def _load_copy(tag):
    spec = importlib.util.spec_from_file_location(f"vba_ro_copy_{tag}", SKILL / "vba_ro.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


@pytest.fixture
def two_temps(tmp_path, monkeypatch):
    """Two copies of the module, each imported under a different %TEMP%."""
    import tempfile
    mods = []
    for tag in ("a", "b"):
        t = tmp_path / f"temp_{tag}"
        t.mkdir()
        monkeypatch.setenv("TEMP", str(t))
        monkeypatch.setenv("TMP", str(t))
        monkeypatch.setattr(tempfile, "tempdir", None)
        mods.append(_load_copy(tag))
    return mods


def test_state_dir_does_not_depend_on_temp(two_temps, tmp_path):
    m1, m2 = two_temps
    assert m1.LEGACY_DIR != m2.LEGACY_DIR  # the old location did follow TEMP
    assert m1.STATE_DIR == m2.STATE_DIR
    for m in (m1, m2):
        assert str(tmp_path) not in str(m.STATE_DIR)


@win
def test_guards_under_different_temp_exclude_each_other(reg, two_temps):
    m1, m2 = two_temps
    for m in (m1, m2):
        m.STATE_DIR = reg["state"]  # default state_dir=None resolves STATE_DIR at call time
    _set(reg, reg["winreg"].REG_DWORD, 0)
    with m1.acces_vbom("Word", base=reg["base"]):
        with pytest.raises(RuntimeError, match="in use"):
            with m2.acces_vbom("Word", base=reg["base"]):
                pass
        assert _get(reg)[0] == 1
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)
    _assert_clean_state_dir(reg)


@win
def test_restore_from_other_temp_finds_crash_backup(reg, two_temps):
    m1, m2 = two_temps
    for m in (m1, m2):
        m.STATE_DIR = reg["state"]
    _set(reg, reg["winreg"].REG_DWORD, 1)  # forced value left by a crashed run under TEMP a
    reg["state"].mkdir(parents=True)
    (reg["state"] / "Word.lock").write_text(json.dumps({"pid": _dead_pid(), "start": 1}), encoding="utf-8")
    (reg["state"] / "Word.backup.json").write_text(json.dumps(
        {"missing_keys": [], "value": {"type": reg["winreg"].REG_DWORD, "data": 0}}), encoding="utf-8")
    rez = m2.restaureaza_vbom(base=reg["base"], legacy_dir=reg["state"] / "none")
    assert "Word" in rez and _get(reg) == (0, reg["winreg"].REG_DWORD)
    _assert_clean_state_dir(reg)


@win
def test_legacy_temp_backup_is_restored_once(reg, tmp_path):
    _set(reg, reg["winreg"].REG_DWORD, 1)
    legacy = tmp_path / "legacy"
    legacy.mkdir()
    (legacy / "Word.backup.json").write_text(json.dumps(
        {"missing_keys": [], "value": {"type": reg["winreg"].REG_DWORD, "data": 0}}), encoding="utf-8")
    rez = vba_ro.restaureaza_vbom(base=reg["base"], state_dir=reg["state"], legacy_dir=legacy)
    assert rez["Word"].startswith("legacy") and _get(reg) == (0, reg["winreg"].REG_DWORD)
    assert not (legacy / "Word.backup.json").exists()


@win
def test_legacy_backup_respects_a_live_lock(reg, tmp_path):
    _set(reg, reg["winreg"].REG_DWORD, 0)
    legacy = tmp_path / "legacy"
    legacy.mkdir()
    (legacy / "Word.backup.json").write_text(json.dumps(
        {"missing_keys": [], "value": None}), encoding="utf-8")
    with _guard(reg):  # live run (this process) holds the lock in the fixed state dir
        rez = vba_ro.restaureaza_vbom(base=reg["base"], state_dir=reg["state"], legacy_dir=legacy)
        assert "in use" in rez["Word"]
        assert _get(reg)[0] == 1  # not touched
        assert (legacy / "Word.backup.json").exists()  # kept for later
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)


@win
def test_legacy_lock_of_live_run_also_blocks_its_backup(reg, tmp_path):
    _set(reg, reg["winreg"].REG_DWORD, 1)
    legacy = tmp_path / "legacy"
    legacy.mkdir()
    (legacy / "Word.lock").write_text(json.dumps(vba_ro._Garda("Word", reg["base"], legacy).eu), encoding="utf-8")
    (legacy / "Word.backup.json").write_text(json.dumps(
        {"missing_keys": [], "value": None}), encoding="utf-8")
    rez = vba_ro.restaureaza_vbom(base=reg["base"], state_dir=reg["state"], legacy_dir=legacy)
    assert "in use" in rez["Word"] and _get(reg)[0] == 1


# ----------------------------------------------------------------------------- guard: D2 (recovery race)
def _crashed_run(reg, forced=1, original=0):
    _set(reg, reg["winreg"].REG_DWORD, forced)
    reg["state"].mkdir(parents=True)
    (reg["state"] / "Word.lock").write_text(json.dumps({"pid": _dead_pid(), "start": 1}), encoding="utf-8")
    (reg["state"] / "Word.backup.json").write_text(json.dumps(
        {"missing_keys": [], "value": {"type": reg["winreg"].REG_DWORD, "data": original}}), encoding="utf-8")


def _other_recoverer_wins_rename(monkeypatch, state, pid, real_rename=os.rename):
    """The first rename of Word.lock is won by 'another recoverer' (moved to a .stale file) and ours
    raises FileNotFoundError, as in the reviewer's interleaving."""
    other = state / f"Word.lock.stale.{pid}.1"
    first = [True]

    def fake_rename(src, dst):
        if first[0] and Path(src).name == "Word.lock":
            first[0] = False
            real_rename(src, other)
            raise FileNotFoundError(src)
        return real_rename(src, dst)
    monkeypatch.setattr(vba_ro.os, "rename", fake_rename)
    return other


@win
def test_recovery_waits_for_the_other_recoverer_before_taking_the_lock(reg, monkeypatch):
    _crashed_run(reg)
    state = reg["state"]
    other = _other_recoverer_wins_rename(monkeypatch, state, os.getpid())  # alive recoverer
    seen = []

    def fake_sleep(_):
        seen.append({"lock": (state / "Word.lock").exists(), "backup": (state / "Word.backup.json").exists(),
                     "value": _get(reg)[0]})
        g2 = vba_ro._Garda("Word", reg["base"], state)  # the other recoverer finishes its restore now
        g2._restaureaza_backup()
        other.unlink()
    monkeypatch.setattr(vba_ro.time, "sleep", fake_sleep)
    with _guard(reg) as orig:
        assert orig["value"] == {"type": reg["winreg"].REG_DWORD, "data": 0}  # the true original, not 1
    assert seen, "the caller did not wait for the other recoverer"
    assert seen[0] == {"lock": False, "backup": True, "value": 1}  # lock not taken while it restores
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)
    _assert_clean_state_dir(reg)


@win
def test_recovery_wait_is_bounded_and_refuses(reg, monkeypatch):
    _crashed_run(reg)
    _other_recoverer_wins_rename(monkeypatch, reg["state"], os.getpid())
    monkeypatch.setattr(vba_ro, "RECOVERY_WAIT", 0.2)
    with pytest.raises(RuntimeError, match="still restoring"):
        with _guard(reg):
            pass
    assert not (reg["state"] / "Word.lock").exists()  # never took the lock
    assert _get(reg)[0] == 1  # nothing was changed behind the recoverer's back


@win
def test_dead_recoverer_is_finished_by_the_waiting_run(reg, monkeypatch):
    _crashed_run(reg)
    _other_recoverer_wins_rename(monkeypatch, reg["state"], _dead_pid())
    with _guard(reg) as orig:
        assert orig["value"] == {"type": reg["winreg"].REG_DWORD, "data": 0}
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)
    _assert_clean_state_dir(reg)


@win
def test_backup_replaced_during_restore_is_not_deleted(reg, monkeypatch):
    """A new run writes its own backup between our restore and our unlink: ours must not delete it."""
    _set(reg, reg["winreg"].REG_DWORD, 1)
    state = reg["state"]
    state.mkdir(parents=True)
    backup = state / "Word.backup.json"
    backup.write_text(json.dumps({"missing_keys": [], "value": {"type": reg["winreg"].REG_DWORD, "data": 0}}),
                      encoding="utf-8")
    newer = json.dumps({"missing_keys": [], "value": {"type": reg["winreg"].REG_DWORD, "data": 7}})

    def hook(nume):
        if nume == "inainte_de_stergere_backup":
            backup.write_text(newer, encoding="utf-8")
    monkeypatch.setattr(vba_ro, "_punct", hook)
    vba_ro._Garda("Word", reg["base"], state)._restaureaza_backup()
    assert backup.read_text(encoding="utf-8") == newer  # the newer run's backup survived
    assert _get(reg) == (0, reg["winreg"].REG_DWORD)  # and the restore itself was applied


@win
def test_backup_removed_during_restore_is_tolerated(reg, monkeypatch):
    _set(reg, reg["winreg"].REG_DWORD, 1)
    state = reg["state"]
    state.mkdir(parents=True)
    backup = state / "Word.backup.json"
    backup.write_text(json.dumps({"missing_keys": [], "value": None}), encoding="utf-8")
    monkeypatch.setattr(vba_ro, "_punct", lambda nume: backup.unlink())
    assert vba_ro._Garda("Word", reg["base"], state)._restaureaza_backup() is not None
    assert _get(reg) is None
