"""scripts/run_studio.ps1 -NoUi: starts backend + preview, the token never appears on a command line, everything stops
when the backend goes away. Windows only (skipped elsewhere or when powershell is missing)."""
from __future__ import annotations

import json
import os
import re
import shutil
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

import pytest

PRODUCT = Path(__file__).resolve().parents[2]
SCRIPT = PRODUCT / "scripts" / "run_studio.ps1"
POWERSHELL = shutil.which("powershell")
API, PREVIEW = 27975, 27980
pytestmark = pytest.mark.skipif(sys.platform != "win32" or not POWERSHELL, reason="Windows PowerShell required")


def listening(port: int) -> bool:
    with socket.socket() as s:
        s.settimeout(0.5)
        return s.connect_ex(("127.0.0.1", port)) == 0


def command_lines(match: str) -> list[tuple[int, str]]:
    q = f"Get-CimInstance Win32_Process | Where-Object {{ $_.CommandLine -like '*{match}*' }} | " \
        "ForEach-Object { \"$($_.ProcessId)|$($_.CommandLine)\" }"
    out = subprocess.run([POWERSHELL, "-NoProfile", "-Command", q], capture_output=True, text=True, timeout=60).stdout  # noqa: S603
    rows = []
    for ln in out.splitlines():
        if "|" in ln:
            pid, cmd = ln.split("|", 1)
            rows.append((int(pid), cmd))
    return rows


def test_script_starts_backend_and_preview_hands_token_on_stdin_and_stops_everything(tmp_path: Path) -> None:
    assert not listening(API) and not listening(PREVIEW)
    env = {k: os.environ[k] for k in ("SYSTEMROOT", "PATH", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP")
           if k in os.environ}
    proc = subprocess.Popen([POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT), "-NoUi",  # noqa: S603
                             "-ApiPort", str(API), "-PreviewPort", str(PREVIEW), "-DataDir", str(tmp_path / "data")],
                            env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    try:
        end = time.time() + 90
        health = None
        while time.time() < end and health is None:
            try:
                health = json.load(urllib.request.urlopen(f"http://127.0.0.1:{API}/api/v1/health", timeout=3))  # noqa: S310
            except OSError:
                time.sleep(0.5)
        assert health and health["status"] == "ok"
        assert listening(PREVIEW)
        # the token is on stdin only: no command line of the studio processes holds a 64-hex string
        backend = [(p, c) for p, c in command_lines("apps.backend") if f"{API}" not in c and "Get-CimInstance" not in c]
        assert backend, "backend process not found"
        for _, cmd in backend + command_lines("aas_build"):
            assert "--token-stdin" in cmd or "aas_build" in cmd
            assert not re.search(r"[0-9a-fA-F]{40,}", cmd), cmd
        # unauthenticated routes stay closed (positive control for the boundary: health is the only public one)
        with pytest.raises(urllib.error.HTTPError) as e:
            urllib.request.urlopen(f"http://127.0.0.1:{API}/api/v1/projects", timeout=5)  # noqa: S310
        assert e.value.code == 401
        for pid, _ in backend:  # the backend goes away -> the script stops the preview server and exits
            subprocess.run([os.path.join(os.environ["SystemRoot"], "System32", "taskkill.exe"), "/PID", str(pid), "/T", "/F"],  # noqa: S603
                           capture_output=True, timeout=30)
        assert proc.wait(timeout=60) is not None
        end = time.time() + 20
        while time.time() < end and (listening(API) or listening(PREVIEW)):
            time.sleep(0.5)
        assert not listening(API) and not listening(PREVIEW)
    finally:
        if proc.poll() is None:
            subprocess.run([os.path.join(os.environ["SystemRoot"], "System32", "taskkill.exe"), "/PID", str(proc.pid), "/T", "/F"],  # noqa: S603
                           capture_output=True, timeout=30)


# ---------------------------------------------------------------- S6 review item 9: AAS_DEV_ORIGINS only with -Dev
def _origin_status(tmp_path: Path, *extra: str) -> int:
    """Start the script with -NoUi (+extra), ask /projects with the vite dev Origin and no token: 401 = the origin
    passed the boundary (dev origins on), 403 = refused (dev origins off). The parent env carries AAS_DEV_ORIGINS on
    purpose: the production path must clear it, not inherit it."""
    assert not listening(API) and not listening(PREVIEW)
    env = {k: os.environ[k] for k in ("SYSTEMROOT", "PATH", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP")
           if k in os.environ}
    env["AAS_DEV_ORIGINS"] = "http://127.0.0.1:1421"
    proc = subprocess.Popen([POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT), "-NoUi",  # noqa: S603
                             "-ApiPort", str(API), "-PreviewPort", str(PREVIEW), "-DataDir", str(tmp_path / "data"), *extra],
                            env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    try:
        end = time.time() + 90
        while time.time() < end:
            try:
                urllib.request.urlopen(f"http://127.0.0.1:{API}/api/v1/health", timeout=3)  # noqa: S310
                break
            except OSError:
                time.sleep(0.5)
        req = urllib.request.Request(f"http://127.0.0.1:{API}/api/v1/projects", headers={"Origin": "http://127.0.0.1:1421"})
        try:
            urllib.request.urlopen(req, timeout=5)  # noqa: S310
            return 200
        except urllib.error.HTTPError as e:
            return e.code
    finally:
        subprocess.run([os.path.join(os.environ["SystemRoot"], "System32", "taskkill.exe"), "/PID", str(proc.pid), "/T", "/F"],  # noqa: S603
                       capture_output=True, timeout=30)
        proc.wait(timeout=30)
        end = time.time() + 20
        while time.time() < end and (listening(API) or listening(PREVIEW)):
            time.sleep(0.5)


def test_production_path_never_sets_dev_origins_even_if_inherited(tmp_path: Path) -> None:
    assert _origin_status(tmp_path) == 403


def test_dev_switch_sets_dev_origins_positive_control(tmp_path: Path) -> None:
    assert _origin_status(tmp_path, "-Dev") == 401


def test_script_sets_dev_origins_only_inside_the_dev_branch() -> None:
    text = SCRIPT.read_text(encoding="utf-8")
    assert "[switch]$Dev" in text
    code = text[text.index("#>") + 2:]  # the comment-based help block documents the switch; the code starts after it
    lines = [ln.strip() for ln in code.splitlines() if "AAS_DEV_ORIGINS" in ln and not ln.strip().startswith("#")]
    assert lines, "the script must handle AAS_DEV_ORIGINS explicitly"
    for ln in lines:
        assert ln.startswith("if ($Dev)") or "Remove" in ln or "= $null" in ln or "Remove(" in ln, ln
