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
    assert os.path.commonpath([str(reg["state"]), str(vba_ro.STATE_DIR)]) != str(vba_ro.STATE_DIR)
