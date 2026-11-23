"""Non-live tests of wsl/opt-aas-bin/aas_devhost.py: persistent input shell (N7), APK package allow-list before
install, uninstall op, idle timeout. Fakes stand in for adb; nothing here starts WSL or an emulator."""
from __future__ import annotations

import base64
import hashlib
import importlib.util
import io
import json
import os
import struct
import sys
import time
import zipfile
from pathlib import Path
from typing import Any

import pytest

from tests.local_gate.conftest import PRODUCT

_SPEC = importlib.util.spec_from_file_location("aas_devhost", PRODUCT / "wsl" / "opt-aas-bin" / "aas_devhost.py")
assert _SPEC and _SPEC.loader
devhost = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(devhost)

GOOD_PKG = "dev.aiappstudio.p0123456789abcdef"


# ---------- minimal binary AndroidManifest (AXML) writer, test fixture only ----------

def _pool(strings: list[str], utf8: bool) -> bytes:
    data = b""
    offs = []
    for s in strings:
        offs.append(len(data))
        if utf8:
            b = s.encode("utf-8")
            data += bytes([len(s), len(b)]) + b + b"\x00"
        else:
            data += struct.pack("<H", len(s)) + s.encode("utf-16-le") + b"\x00\x00"
    while len(data) % 4:
        data += b"\x00"
    hdr = 28
    start = hdr + 4 * len(strings)
    body = b"".join(struct.pack("<I", o) for o in offs) + data
    return struct.pack("<HHIIIIII", 0x0001, hdr, hdr + len(body), len(strings), 0, 0x100 if utf8 else 0,
                       start, 0) + body


def _start_el(name: int, attrs: list[tuple[int, int]]) -> bytes:
    a = b"".join(struct.pack("<IIIHBBI", 0xFFFFFFFF, n, v, 8, 0, 0x03, v) for n, v in attrs)
    body = struct.pack("<IIIIHHHHHH", 1, 0xFFFFFFFF, 0xFFFFFFFF, name, 20, 20, len(attrs), 0, 0, 0) + a
    return struct.pack("<HHI", 0x0102, 16, 8 + len(body)) + body


def axml(pkg: str, root: str = "manifest", utf8: bool = False, extra_pkg: str | None = None) -> bytes:
    strings = [root, "package", pkg, "versionCode"] + ([extra_pkg] if extra_pkg else [])
    attrs = [(1, 2)] + ([(1, 4)] if extra_pkg else [])
    chunks = _pool(strings, utf8) + _start_el(0, attrs)
    return struct.pack("<HHI", 0x0003, 8, 8 + len(chunks)) + chunks


def apk(manifest: bytes, dup: bool = False) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        z.writestr("AndroidManifest.xml", manifest)
        z.writestr("classes.dex", b"dex\n035\x00")
        if dup:
            import warnings
            with warnings.catch_warnings():
                warnings.simplefilter("ignore")
                z.writestr("AndroidManifest.xml", axml(GOOD_PKG))
    return buf.getvalue()


@pytest.mark.parametrize("utf8", [False, True])
def test_apk_package_parsed(tmp_path: Path, utf8: bool) -> None:
    p = tmp_path / "a.apk"
    p.write_bytes(apk(axml(GOOD_PKG, utf8=utf8)))
    assert devhost.apk_package(str(p)) == GOOD_PKG


@pytest.mark.parametrize("data", [
    apk(axml("com.evil.app")),
    apk(axml(GOOD_PKG, root="application")),
    apk(axml(GOOD_PKG, extra_pkg="com.evil.app")),
    apk(axml("com.evil.app"), dup=True),
    b"not a zip",
    apk(b"\x03\x00\x08\x00\xff\xff\xff\x7f"),
])
def test_apk_package_refused(tmp_path: Path, data: bytes) -> None:
    p = tmp_path / "a.apk"
    p.write_bytes(data)
    got = devhost.apk_package(str(p))
    assert got is None or not devhost.PKG_RE.match(got)


# ---------- install / uninstall through Host.handle with a fake adb ----------

class _Out:
    def __init__(self) -> None:
        self.lines: list[dict[str, Any]] = []
        self.dropped = 0

    def control(self, obj: dict[str, Any]) -> None:
        self.lines.append(obj)

    def flush_wait(self, timeout: float = 5.0) -> None:
        pass


def _host(tmp_path: Path) -> Any:
    h = devhost.Host.__new__(devhost.Host)
    (tmp_path / "run").mkdir(exist_ok=True)
    h.state = str(tmp_path)
    h.install = None
    h.out = _Out()
    h.calls = []
    h.stopping = False

    def adb(*args: str, timeout: float = 30, data: bool = False) -> Any:
        h.calls.append(("adb",) + args)
        return 0, "Success"

    def shell(*words: str, timeout: float = 30) -> Any:
        h.calls.append(("shell",) + words)
        return 0, "Success"

    h.adb, h.shell = adb, shell
    return h


def _install(h: Any, data: bytes, monkeypatch: pytest.MonkeyPatch) -> dict[str, Any]:
    monkeypatch.setattr(os, "O_NOFOLLOW", 0, raising=False)
    rep = h.handle({"id": 1, "op": "install", "sha256": hashlib.sha256(data).hexdigest(), "size": len(data)})
    assert rep["ok"]
    for i in range(0, len(data), devhost.RAW_CHUNK):
        rep = h.handle({"id": 2, "op": "chunk", "b64": base64.b64encode(data[i:i + devhost.RAW_CHUNK]).decode()})
    return rep


def test_install_refuses_foreign_package(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    h = _host(tmp_path)
    rep = _install(h, apk(axml("com.android.evil")), monkeypatch)
    assert rep["ok"] is False and rep["err"] == "package not allowed"
    assert not any(c[:2] == ("adb", "install") for c in h.calls)
    assert list((tmp_path / "run").iterdir()) == []


def test_install_allowed_package_uninstalls_then_installs(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    h = _host(tmp_path)
    rep = _install(h, apk(axml(GOOD_PKG)), monkeypatch)
    assert rep["ok"] is True and rep["pkg"] == GOOD_PKG
    kinds = [c[:3] for c in h.calls]
    assert kinds.index(("shell", "pm", "uninstall")) < kinds.index(("adb", "install", "-r"))
    assert list((tmp_path / "run").iterdir()) == []


def test_uninstall_op_validates_pkg(tmp_path: Path) -> None:
    h = _host(tmp_path)
    assert h.handle({"id": 1, "op": "uninstall", "pkg": GOOD_PKG})["ok"] is True
    assert ("shell", "pm", "uninstall", GOOD_PKG) in h.calls
    with pytest.raises(ValueError):
        h.handle({"id": 1, "op": "uninstall", "pkg": "com.android.settings"})
    assert devhost.FIELDS["uninstall"] == {"pkg"}


# ---------- idle timeout ----------

def test_idle_watch_stops_session(tmp_path: Path) -> None:
    h = _host(tmp_path)
    exits: list[int] = []
    h.busy = 0
    h.last_activity = time.monotonic()
    h.stop = lambda: setattr(h, "stopping", True)
    h._exit = exits.append
    h._idle_watch(limit=0.05, tick=0.01)
    assert exits == [35] and h.stopping
    assert {"ch": "e", "kind": "exit", "code": 35, "reason": "idle"} in h.out.lines


def test_idle_watch_not_triggered_while_active(tmp_path: Path) -> None:
    h = _host(tmp_path)
    exits: list[int] = []
    h.busy = 1  # a request in progress
    h.last_activity = time.monotonic() - 10_000
    h.stop = lambda: setattr(h, "stopping", True)
    h._exit = exits.append
    import threading
    t = threading.Thread(target=h._idle_watch, kwargs={"limit": 0.01, "tick": 0.01}, daemon=True)
    t.start()
    time.sleep(0.2)
    assert exits == []
    h.stopping = True
    t.join(1)
    assert devhost.IDLE_S == 15 * 60


# ---------- persistent input shell (N7) ----------

FAKE_SH = r"""
import re, sys, time
delay = float(sys.argv[1])
for line in sys.stdin:
    with open(sys.argv[2], "a") as f:
        f.write(line)
    m = re.search(r"; echo (__aas_\d+__) \$\?$", line.rstrip("\n"))
    if delay < 0:
        continue
    time.sleep(delay)
    if m:
        sys.stdout.write("noise\n" + m.group(1) + " 0\n")
        sys.stdout.flush()
"""


def _shell(tmp_path: Path, delay: float) -> Any:
    script = tmp_path / "fake_sh.py"
    script.write_text(FAKE_SH, encoding="utf-8")
    log = tmp_path / "sh.log"
    return devhost.InputShell([sys.executable, "-I", str(script), str(delay), str(log)]), log


def test_input_shell_one_process_fast(tmp_path: Path) -> None:
    sh, log = _shell(tmp_path, 0.0)
    try:
        t0 = time.monotonic()
        for i in range(30):
            assert sh.run(("input", "tap", str(i), "20"), timeout=5) == (0, "")
        per = (time.monotonic() - t0) / 30
        assert sh.spawned == 1
        assert per < 0.2, per
        lines = log.read_text().splitlines()
        assert lines[0] == "'input' 'tap' '0' '20' >/dev/null 2>&1; echo __aas_1__ $?"
    finally:
        sh.close()


def test_input_shell_quotes_words(tmp_path: Path) -> None:
    sh, log = _shell(tmp_path, 0.0)
    try:
        sh.run(("input", "text", "a'b;$(id)"), timeout=5)
        assert log.read_text().splitlines()[0].startswith("'input' 'text' 'a'\\''b;$(id)' >")
    finally:
        sh.close()


def test_input_shell_timeout_kills_and_respawns(tmp_path: Path) -> None:
    sh, _log = _shell(tmp_path, -1.0)  # never answers
    try:
        with pytest.raises(devhost.subprocess.TimeoutExpired):
            sh.run(("input", "tap", "1", "2"), timeout=0.5)
        assert sh.p is None
        with pytest.raises(devhost.subprocess.TimeoutExpired):
            sh.run(("input", "tap", "1", "2"), timeout=0.5)
        assert sh.spawned == 2
    finally:
        sh.close()


def test_tap_uses_input_shell(tmp_path: Path) -> None:
    h = _host(tmp_path)
    seen: list[tuple[str, ...]] = []

    class _Sh:
        def run(self, words: tuple[str, ...], timeout: float) -> tuple[int, str]:
            seen.append(tuple(words))
            return 0, ""

    h.input = _Sh()
    assert h.handle({"id": 1, "op": "tap", "x": 10, "y": 20})["ok"] is True
    assert h.handle({"id": 2, "op": "swipe", "x1": 1, "y1": 2, "x2": 3, "y2": 4, "ms": 100})["ok"] is True
    assert seen == [("input", "tap", "10", "20"), ("input", "swipe", "1", "2", "3", "4", "100")]
    assert not any(c[0] == "shell" for c in h.calls)
    for bad in ({"x": -1, "y": 0}, {"x": True, "y": 0}, {"x": "1", "y": 0}, {"x": 5000, "y": 0}):
        with pytest.raises(ValueError):
            h.handle(dict({"id": 3, "op": "tap"}, **bad))
    assert len(seen) == 2


def test_reply_pkg_field_validated() -> None:
    from apps.device_host import protocol
    assert protocol.parse_device_line(json.dumps({"ch": "c", "id": 1, "ok": True, "pkg": GOOD_PKG}).encode())
    with pytest.raises(protocol.ProtocolError):
        protocol.parse_device_line(json.dumps({"ch": "c", "id": 1, "ok": True, "pkg": "com.x"}).encode())
