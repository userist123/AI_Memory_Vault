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


def _two_pools(pool_a: list[str], pool_b: list[str]) -> bytes:
    """Reviewer PoC: pool A (allowed name) is first, pool B (evil name) is the one Android ends up using."""
    chunks = _pool(pool_a, False) + _pool(pool_b, False) + _start_el(0, [(1, 2)])
    return struct.pack("<HHI", 0x0003, 8, 8 + len(chunks)) + chunks


def test_two_string_pools_refused(tmp_path: Path) -> None:
    p = tmp_path / "a.apk"
    p.write_bytes(apk(_two_pools(["manifest", "package", GOOD_PKG], ["manifest", "package", "com.evil.app"])))
    assert devhost.apk_package(str(p)) is None


def test_unknown_chunk_and_bad_header_refused() -> None:
    good = axml(GOOD_PKG)
    assert devhost._axml_package(good) == GOOD_PKG
    pool_len = struct.unpack_from("<I", good, 12)[0]
    unk = good[:8] + struct.pack("<HHI", 0x7777, 8, 8) + good[8:]
    unk = struct.pack("<HHI", 0x0003, 8, len(unk)) + unk[8:]
    assert devhost._axml_package(unk) is None
    assert pool_len > 0
    # header size larger than the chunk
    bad = bytearray(good)
    struct.pack_into("<H", bad, 10, 0xFFF0)
    assert devhost._axml_package(bytes(bad)) is None


def test_pool_offsets_use_header_size() -> None:
    """A pool whose header is larger than 28 bytes: offsets array starts after the real header."""
    strings = ["manifest", "package", GOOD_PKG]
    data, offs = b"", []
    for s_ in strings:
        offs.append(len(data))
        data += struct.pack("<H", len(s_)) + s_.encode("utf-16-le") + b"\x00\x00"
    hdr = 36  # 8 extra bytes
    start = hdr + 4 * len(strings)
    body = b"\x00" * 8 + b"".join(struct.pack("<I", o) for o in offs) + data
    # the 8 filler bytes sit inside the header, so rebuild as header+filler
    pool = struct.pack("<HHIIIIII", 0x0001, hdr, hdr + len(body) - 8, 3, 0, 0, start, 0) + body
    chunks = pool + _start_el(0, [(1, 2)])
    assert devhost._axml_package(struct.pack("<HHI", 3, 8, 8 + len(chunks)) + chunks) == GOOD_PKG


def test_pool_string_amplification_bounded() -> None:
    """100000 entries all pointing at one long UTF-16 string must be refused fast, not decoded 100000 times."""
    long_s = "a" * 60000
    data = struct.pack("<H", 0x8000 | (len(long_s) >> 16)) + struct.pack("<H", len(long_s) & 0xFFFF)
    data += long_s.encode("utf-16-le") + b"\x00\x00"
    n = 100000
    hdr = 28
    start = hdr + 4 * n
    body = struct.pack("<I", 0) * n + data
    pool = struct.pack("<HHIIIIII", 0x0001, hdr, hdr + len(body), n, 0, 0, start, 0) + body
    chunks = pool + _start_el(0, [])
    t0 = time.monotonic()
    assert devhost._axml_package(struct.pack("<HHI", 3, 8, 8 + len(chunks)) + chunks) is None
    assert time.monotonic() - t0 < 2.0


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
    h.console = None

    def adb(*args: str, timeout: float = 30, data: bool = False) -> Any:
        h.calls.append(("adb",) + args)
        if args[:1] == ("install",):
            h.pkgs.extend(h.install_adds)
        return 0, "Success"

    h.pkgs = ["com.android.settings"]
    h.install_adds = []  # packages the fake "install" makes appear

    def shell(*words: str, timeout: float = 30) -> Any:
        h.calls.append(("shell",) + words)
        if words[:3] == ("pm", "list", "packages"):
            return 0, "\n".join("package:" + p for p in h.pkgs)
        if words[:2] == ("pm", "uninstall") and words[2] in h.pkgs:
            h.pkgs.remove(words[2])
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


def test_install_removes_newly_installed_foreign_package(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    h = _host(tmp_path)
    h.install_adds = [GOOD_PKG, "com.evil.app"]  # APK actually installs something the check did not see
    rep = _install(h, apk(axml(GOOD_PKG)), monkeypatch)
    assert rep["ok"] is False and rep["err"] == "unexpected package installed"
    assert ("shell", "pm", "uninstall", "com.evil.app") in h.calls
    assert "com.evil.app" not in h.pkgs


def test_install_snapshot_failure_is_fail_closed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    h = _host(tmp_path)
    orig = h.shell

    def shell(*words: str, timeout: float = 30) -> Any:
        if words[:3] == ("pm", "list", "packages"):
            return 1, "error"
        return orig(*words, timeout=timeout)

    h.shell = shell
    rep = _install(h, apk(axml(GOOD_PKG)), monkeypatch)
    assert rep["ok"] is False
    assert not any(c[:2] == ("adb", "install") for c in h.calls)


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


# ---------- emulator console touch injection (N7, second step) ----------

import socket  # noqa: E402
import threading  # noqa: E402

TOKEN = "AbCdEf0123456789"


class _FakeConsole:
    """Emulator-console stand-in on 127.0.0.1: banner, `auth`, `event mouse`; records every command line."""

    def __init__(self, token: str = TOKEN, auth: bool = True, banner_path: str | None = None,
                 drop_after: int | None = None, mute: bool = False, ko: bool = False,
                 refuse_after_drop: bool = False) -> None:
        self.token, self.auth, self.banner_path = token, auth, banner_path
        self.drop_after, self.mute, self.ko = drop_after, mute, ko
        self.refuse_after_drop = refuse_after_drop  # after the drop, close the listener: reconnects fail
        self.lines: list[str] = []
        self.accepts = 0
        self.srv = socket.socket()
        self.srv.bind(("127.0.0.1", 0))
        self.srv.listen(4)
        self.port = self.srv.getsockname()[1]
        self.stop = False
        threading.Thread(target=self._serve, daemon=True).start()

    def _serve(self) -> None:
        while not self.stop:
            try:
                c, _ = self.srv.accept()
            except OSError:
                return
            self.accepts += 1
            threading.Thread(target=self._client, args=(c,), daemon=True).start()

    def _client(self, c: socket.socket) -> None:
        f = c.makefile("rb")
        if self.auth:
            path = "/x/.emulator_console_auth_token" if self.banner_path is None else self.banner_path
            named = (b"'" + path.encode() + b"'\r\n") if path else b""  # "" = banner without the path line
            c.sendall(b"Android Console: Authentication required\r\nAndroid Console: type 'auth <auth_token>' to "
                      b"authenticate\r\nAndroid Console: you can find your <auth_token> in \r\n"
                      + named + b"OK\r\n")
        else:
            c.sendall(b"Android Console: type 'help' for a list of commands\r\nOK\r\n")
        authed = not self.auth
        n = 0
        try:
            for raw in f:
                line = raw.decode().rstrip("\r\n")
                self.lines.append(line)
                if line.startswith("auth "):
                    if line[5:] == self.token:
                        authed = True
                        c.sendall(b"Android Console: type 'help' for a list of commands\r\nOK\r\n")
                    else:
                        c.sendall(b"KO: authentication token does not match ~/.emulator_console_auth_token\r\n")
                    continue
                if not authed:
                    c.sendall(b"KO: unknown command, try 'help'\r\n")
                    continue
                n += 1
                if self.drop_after is not None and n > self.drop_after:
                    self.drop_after = None
                    self.lines.pop()  # never processed
                    c.close()
                    if self.refuse_after_drop:
                        self.stop = True
                        self.srv.close()
                    return
                if self.mute:
                    continue
                c.sendall(b"KO: Invalid arguments. Make sure you provide 4 integers.\r\n" if self.ko else b"OK\r\n")
        except OSError:
            pass
        c.close()

    def close(self) -> None:
        self.stop = True
        self.srv.close()


def _console(tmp_path: Path, fake: _FakeConsole, token: str = TOKEN, size: tuple[int, int] = (1080, 2340)) -> Any:
    tok = tmp_path / ".emulator_console_auth_token"
    tok.write_bytes((token + "\n").encode("latin-1"))
    if fake.banner_path is None:
        fake.banner_path = str(tok)  # the real console names the token file it expects
    return devhost.Console(fake.port, str(tok), list(size))


def _mouse(lines: list[str]) -> list[str]:
    return [ln for ln in lines if ln.startswith("event mouse")]


def test_console_auth_and_tap(tmp_path: Path) -> None:
    fake = _FakeConsole(banner_path=str(tmp_path / ".emulator_console_auth_token"))
    c = _console(tmp_path, fake)
    try:
        t0 = time.monotonic()
        for i in range(20):
            c.tap(10 + i, 20, timeout=5)
        assert (time.monotonic() - t0) / 20 < 0.1
        assert fake.accepts == 1 and c.connects == 1  # one persistent connection
        assert fake.lines[0] == "auth " + TOKEN
        assert _mouse(fake.lines)[:2] == ["event mouse 10 20 0 1", "event mouse 10 20 0 0"]
        assert len(_mouse(fake.lines)) == 40
    finally:
        c.close()
        fake.close()


def test_console_without_auth_banner(tmp_path: Path) -> None:
    fake = _FakeConsole(auth=False)
    c = _console(tmp_path, fake)
    try:
        c.tap(1, 2, timeout=5)
        assert fake.lines == ["event mouse 1 2 0 1", "event mouse 1 2 0 0"]  # token not sent when not asked
    finally:
        c.close()
        fake.close()


def test_console_wrong_token_disables(tmp_path: Path) -> None:
    fake = _FakeConsole()
    c = _console(tmp_path, fake, token="WrongToken000000")
    try:
        with pytest.raises(devhost.ConsoleUnavailable):
            c.tap(1, 2, timeout=5)
        assert c.disabled
        assert _mouse(fake.lines) == []  # nothing injected before auth succeeded
        with pytest.raises(devhost.ConsoleUnavailable):
            c.tap(1, 2, timeout=5)
        assert fake.accepts == 1  # disabled: no retry storm
    finally:
        c.close()
        fake.close()


def test_console_banner_path_mismatch_refused(tmp_path: Path) -> None:
    fake = _FakeConsole(banner_path=str(tmp_path / "elsewhere" / ".emulator_console_auth_token"))
    c = _console(tmp_path, fake)
    try:
        with pytest.raises(devhost.ConsoleUnavailable):
            c.tap(1, 2, timeout=5)
        assert c.disabled
        assert not any(ln.startswith("auth") for ln in fake.lines)  # our token never sent to a foreign console
    finally:
        c.close()
        fake.close()


def test_console_token_file_checks(tmp_path: Path) -> None:
    fake = _FakeConsole()
    try:
        for bad in ("", "has space", "x" * 300, "tok\x00en", "tést"):
            c = _console(tmp_path, fake, token=bad)
            with pytest.raises(devhost.ConsoleUnavailable):
                c.tap(1, 2, timeout=5)
            c.close()
        missing = devhost.Console(fake.port, str(tmp_path / "nope"), [1080, 2340])
        with pytest.raises(devhost.ConsoleUnavailable):
            missing.tap(1, 2, timeout=5)
        assert not any(ln.startswith("auth") for ln in fake.lines)
    finally:
        fake.close()


def test_console_connection_refused_is_unavailable(tmp_path: Path) -> None:
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    tok = tmp_path / "t"
    tok.write_text(TOKEN)
    c = devhost.Console(port, str(tok), [1080, 2340])
    with pytest.raises(devhost.ConsoleUnavailable):
        c.tap(1, 2, timeout=2)
    assert not c.disabled  # transient: the next call tries again


def test_console_validation_and_bounds(tmp_path: Path) -> None:
    fake = _FakeConsole()
    c = _console(tmp_path, fake, size=(1080, 2340))
    try:
        for x, y in ((-1, 0), (1080, 0), (0, 2340), (True, 0), ("1", 0), (1.0, 0), (None, 0)):
            with pytest.raises(ValueError):
                c.tap(x, y, timeout=5)  # type: ignore[arg-type]
        for args in ((0, 0, 1080, 0, 100), (0, 0, 1, 1, 0), (0, 0, 1, 1, 5001), (0, 0, 1, 1, True)):
            with pytest.raises(ValueError):
                c.swipe(*args, timeout=5)  # type: ignore[arg-type]
        assert fake.accepts == 0  # validation happens before any connection
        c.tap(1079, 2339, timeout=5)
        assert _mouse(fake.lines) == ["event mouse 1079 2339 0 1", "event mouse 1079 2339 0 0"]
        for bad in ([0, 0], [1080], ["1080", 2340], [True, 2340], [20000, 2340], (1080, 2340)):
            with pytest.raises(ValueError):
                devhost.Console(fake.port, "t", bad)
        for line in ("kill", "event mouse 1 1 0 1\nkill", "event mouse 1 1 0 1 ", "auth x"):
            with pytest.raises(ValueError):
                c._send(line)
    finally:
        c.close()
        fake.close()


def test_console_swipe_sequence(tmp_path: Path) -> None:
    fake = _FakeConsole()
    c = _console(tmp_path, fake)
    try:
        t0 = time.monotonic()
        c.swipe(100, 1000, 300, 600, 160, timeout=5)
        took = time.monotonic() - t0
        m = _mouse(fake.lines)
        assert m[0] == "event mouse 100 1000 0 1"
        assert m[-1] == "event mouse 300 600 0 0"
        assert m[-2] == "event mouse 300 600 0 1"
        assert all(ln.endswith(" 0 1") for ln in m[:-1])
        assert 4 <= len(m) - 2 <= 101
        assert 0.14 <= took < 1.5, took
    finally:
        c.close()
        fake.close()


def test_console_reconnects_after_drop(tmp_path: Path) -> None:
    fake = _FakeConsole(drop_after=2)  # serves one tap (2 lines), then closes on the next command
    c = _console(tmp_path, fake)
    try:
        c.tap(5, 6, timeout=5)
        c.tap(7, 8, timeout=5)  # stale connection: reconnect once, resend
        assert c.connects == 2 and fake.accepts == 2
        assert _mouse(fake.lines) == ["event mouse 5 6 0 1", "event mouse 5 6 0 0",
                                      "event mouse 7 8 0 1", "event mouse 7 8 0 0"]
    finally:
        c.close()
        fake.close()


def test_console_timeout_closes_then_reconnects(tmp_path: Path) -> None:
    fake = _FakeConsole(mute=True)
    c = _console(tmp_path, fake)
    try:
        with pytest.raises(devhost.subprocess.TimeoutExpired):
            c.tap(1, 2, timeout=0.5)
        assert c.sock is None
        # the touch-down was sent: the release reconnects (connect 2) and sends the up, best effort
        assert "event mouse 1 2 0 0" in fake.lines
        fake.mute = False
        c.tap(3, 4, timeout=5)
        assert c.connects == 3
        assert _mouse(fake.lines)[-2:] == ["event mouse 3 4 0 1", "event mouse 3 4 0 0"]
    finally:
        c.close()
        fake.close()


def test_console_ko_reply_is_error(tmp_path: Path) -> None:
    fake = _FakeConsole(ko=True)
    c = _console(tmp_path, fake)
    try:
        with pytest.raises(devhost.ConsoleError) as ei:
            c.tap(1, 1, timeout=5)
        assert "KO" in str(ei.value)
        assert not c.disabled
    finally:
        c.close()
        fake.close()


def test_host_tap_prefers_console_and_falls_back(tmp_path: Path) -> None:
    h = _host(tmp_path)
    seen: list[tuple[str, ...]] = []

    class _Sh:
        def run(self, words: tuple[str, ...], timeout: float) -> tuple[int, str]:
            seen.append(tuple(words))
            return 0, ""

    class _Con:
        def __init__(self, exc: Exception | None = None, disabled: bool = False) -> None:
            self.calls: list[tuple[Any, ...]] = []
            self.exc, self.disabled = exc, disabled

        def tap(self, x: int, y: int, timeout: float) -> None:
            self.calls.append(("tap", x, y))
            if self.exc:
                raise self.exc

        def swipe(self, *a: int, timeout: float) -> None:
            self.calls.append(("swipe",) + a)
            if self.exc:
                raise self.exc

    h.input = _Sh()
    h.console = _Con()
    assert h.handle({"id": 1, "op": "tap", "x": 10, "y": 20}) == {"ok": True}
    assert h.handle({"id": 2, "op": "swipe", "x1": 1, "y1": 2, "x2": 3, "y2": 4, "ms": 100}) == {"ok": True}
    assert h.console.calls == [("tap", 10, 20), ("swipe", 1, 2, 3, 4, 100)] and seen == []
    h.console = _Con(devhost.ConsoleUnavailable("refused"))
    assert h.handle({"id": 3, "op": "tap", "x": 10, "y": 20})["ok"] is True
    assert seen == [("input", "tap", "10", "20")] and h.console is not None
    h.console = _Con(devhost.ConsoleUnavailable("auth"), disabled=True)
    assert h.handle({"id": 4, "op": "tap", "x": 11, "y": 21})["ok"] is True
    assert h.console is None  # auth failure: adb path for the rest of the session
    h.console = _Con(devhost.ConsoleError("KO: bad"))
    rep = h.handle({"id": 5, "op": "tap", "x": 12, "y": 22})
    assert rep["ok"] is False and "console" in rep["err"]
    assert len(seen) == 2  # a console error after sending is not retried on adb (no double tap)


def test_host_console_home_guard(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    h = _host(tmp_path)
    h.port = 5600
    monkeypatch.setenv("HOME", str(tmp_path / "home"))
    c = h._make_console([1080, 2340])
    assert c is not None and c.token_path == str(tmp_path / "home" / ".emulator_console_auth_token")
    assert c.port == 5600
    monkeypatch.setenv("HOME", str(tmp_path / "elsewhere"))
    assert h._make_console([1080, 2340]) is None  # never the owner's ~/.emulator_console_auth_token
    monkeypatch.setenv("HOME", str(tmp_path / "home"))
    assert h._make_console([0, 0]) is None  # unknown screen size: adb path


# ---------- review fixes: banner path, touch release, strict regexes, screen size + rotation ----------

def test_console_auth_banner_without_path_refused(tmp_path: Path) -> None:
    fake = _FakeConsole(banner_path="")  # asks for auth but names no token file
    c = _console(tmp_path, fake)
    try:
        with pytest.raises(devhost.ConsoleUnavailable):
            c.tap(1, 2, timeout=5)
        assert c.disabled
        assert not any(ln.startswith("auth") for ln in fake.lines)  # token never sent blind
    finally:
        c.close()
        fake.close()


def test_console_banner_naming_two_files_refused(tmp_path: Path) -> None:
    tok = tmp_path / ".emulator_console_auth_token"
    fake = _FakeConsole(banner_path=str(tok) + "'\r\n'" + str(tok))  # two quoted lines, both ours
    c = _console(tmp_path, fake)
    try:
        with pytest.raises(devhost.ConsoleUnavailable):
            c.tap(1, 2, timeout=5)
        assert not any(ln.startswith("auth") for ln in fake.lines)
    finally:
        c.close()
        fake.close()


def test_console_line_regex_is_full_match(tmp_path: Path) -> None:
    fake = _FakeConsole()
    c = _console(tmp_path, fake)
    try:
        for line in ("event mouse 1 1 0 1\n", "event mouse 1 1 0 1\r"):
            with pytest.raises(ValueError):
                c._send(line)
        assert devhost.Console.TOKEN_RE.fullmatch("abc") and not devhost.Console.TOKEN_RE.fullmatch("abc\n")
    finally:
        c.close()
        fake.close()


def test_console_token_with_second_line_refused(tmp_path: Path) -> None:
    fake = _FakeConsole()
    try:
        c = _console(tmp_path, fake, token=TOKEN + "\nmore")  # second line in the token file
        with pytest.raises(devhost.ConsoleUnavailable):
            c.tap(1, 2, timeout=5)
        assert not any(ln.startswith("auth") for ln in fake.lines)
        c.close()
    finally:
        fake.close()


def test_console_up_sent_when_connection_drops_after_down(tmp_path: Path) -> None:
    fake = _FakeConsole(drop_after=1)  # processes the down, drops on the up
    c = _console(tmp_path, fake)
    try:
        with pytest.raises(devhost.ConsoleError):
            c.tap(5, 6, timeout=5)
        assert _mouse(fake.lines) == ["event mouse 5 6 0 1", "event mouse 5 6 0 0"]  # up resent after reconnect
        assert fake.accepts == 2
    finally:
        c.close()
        fake.close()


def test_console_lost_after_down_never_falls_back_to_adb(tmp_path: Path) -> None:
    fake = _FakeConsole(drop_after=2, refuse_after_drop=True)
    c = _console(tmp_path, fake)
    try:
        c.tap(5, 6, timeout=5)
        # stale connection: the down of the 2nd tap is written, the console is gone and refuses a reconnect.
        # That is not "nothing was sent" (ConsoleUnavailable -> adb): the caller must not retry on adb.
        with pytest.raises(devhost.ConsoleError):
            c.tap(7, 8, timeout=3)
    finally:
        c.close()
        fake.close()
    h = _host(tmp_path)
    seen: list[tuple[str, ...]] = []

    class _Sh:
        def run(self, words: tuple[str, ...], timeout: float) -> tuple[int, str]:
            seen.append(tuple(words))
            return 0, ""

    class _Con:
        disabled = False

        def tap(self, x: int, y: int, timeout: float) -> None:
            raise devhost.ConsoleError("console lost after touch down")

    h.input, h.console = _Sh(), _Con()
    assert h.handle({"id": 1, "op": "tap", "x": 1, "y": 2})["ok"] is False
    assert seen == []


def test_console_swipe_error_mid_gesture_releases(tmp_path: Path) -> None:
    fake = _FakeConsole(drop_after=3)  # down + 2 moves, then drop
    c = _console(tmp_path, fake)
    try:
        with pytest.raises(devhost.ConsoleError):
            c.swipe(100, 1000, 300, 600, 160, timeout=5)
        m = _mouse(fake.lines)
        assert len(m) == 4 and all(ln.endswith(" 0 1") for ln in m[:3])
        assert m[-1].endswith(" 0 0")  # released (at the last position written, which the fake never saw)
        assert fake.accepts == 2
    finally:
        c.close()
        fake.close()


@pytest.mark.parametrize("rc, text, want", [
    (0, "Physical size: 1080x2340", [1080, 2340]),
    (0, "Physical size: 1080x2340\nOverride size: 1080x2340", [1080, 2340]),
    (0, "Physical size: 1080x2340\r\n", [1080, 2340]),
    (0, "Physical size: 072x1", None),
    (0, "Physical size: 1080x150", None),
    (0, "Physical size: 199x2340", None),
    (0, "Physical size: 9000x2340", None),
    (0, "error: device offline 1080x2340", None),
    (0, "", None),
    (1, "Physical size: 1080x2340", None),
    (0, "Physical size: 1080x2340\nOverride size: 720x1560", None),  # scaled display: console coords differ
    (0, "Override size: 720x1560", None),
])
def test_parse_wm_size(rc: int, text: str, want: list[int] | None) -> None:
    assert devhost.parse_wm_size(rc, text) == want


@pytest.mark.parametrize("rc, text, want", [
    (0, "    Viewport INTERNAL: displayId=0, uniqueId=local:1, port=0, orientation=0, logicalFrame=[0, 0, 1080, "
        "2340], isActive=[1]", 0),
    (0, "      SurfaceOrientation: 1\n", 1),
    (0, "Viewport INTERNAL: displayId=0, orientation=3, x\nSurfaceOrientation: 3", 3),
    (0, "Viewport INTERNAL: displayId=0, orientation=1, x\nSurfaceOrientation: 3", None),  # disagree
    (0, "SurfaceOrientation: 7", None),
    (0, "nothing here", None),
    (1, "SurfaceOrientation: 0", None),
])
def test_parse_rotation(rc: int, text: str, want: int | None) -> None:
    assert devhost.parse_rotation(rc, text) == want


def _screen_host(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, wm: tuple[int, str],
                 rot: tuple[int, str]) -> Any:
    h = _host(tmp_path)
    h.port = 5600
    monkeypatch.setenv("HOME", str(tmp_path / "home"))

    def adb(*args: str, timeout: float = 30, data: bool = False) -> Any:
        if args[:2] == ("shell", "wm size"):
            return wm
        if args[:1] == ("shell",) and "dumpsys input" in args[1]:
            return rot
        return 0, ""

    h.adb = adb
    return h


def test_bogus_screen_size_means_no_console(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    h = _screen_host(tmp_path, monkeypatch, (0, "Physical size: 072x1"), (0, "SurfaceOrientation: 0"))
    size, con = h._setup_console()
    assert con is None and size == [0, 0]
    h = _screen_host(tmp_path, monkeypatch, (0, "Physical size: 1080x2340"), (0, "no rotation line"))
    assert h._setup_console()[1] is None  # rotation unknown: adb path
    h = _screen_host(tmp_path, monkeypatch, (0, "Physical size: 1080x2340"), (0, "SurfaceOrientation: 0"))
    size, con = h._setup_console()
    assert size == [1080, 2340] and con is not None and con.rotation == 0


@pytest.mark.parametrize("rot, tap, panel", [
    (0, (100, 2000), (100, 2000)),
    (1, (2000, 500), (579, 2000)),  # 90: logical x = panel y, logical y = W-1 - panel x
    (2, (100, 2000), (979, 339)),
    (3, (2000, 500), (500, 339)),
])
def test_console_rotation_maps_logical_to_panel(tmp_path: Path, rot: int, tap: tuple[int, int],
                                                panel: tuple[int, int]) -> None:
    fake = _FakeConsole()
    c = _console(tmp_path, fake)
    c.set_rotation(rot)
    try:
        c.tap(*tap, timeout=5)  # landscape (rot 1/3): x up to 2339 is accepted
        assert _mouse(fake.lines) == ["event mouse %d %d 0 1" % panel, "event mouse %d %d 0 0" % panel]
        if rot in (1, 3):
            with pytest.raises(ValueError):
                c.tap(0, 1080, timeout=5)  # landscape height is 1080
        else:
            with pytest.raises(ValueError):
                c.tap(1080, 0, timeout=5)
    finally:
        c.close()
        fake.close()
    for bad in (-1, 4, True, "1"):
        with pytest.raises(ValueError):
            c.set_rotation(bad)  # type: ignore[arg-type]


def test_rotation_watch_updates_and_disables(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    h = _screen_host(tmp_path, monkeypatch, (0, "Physical size: 1080x2340"), (0, "SurfaceOrientation: 1"))
    h.console = devhost.Console(5600, str(tmp_path / "t"), [1080, 2340])
    assert h._rotation_tick() is True and h.console.rotation == 1
    h.adb = lambda *a, **k: (0, "garbage")
    assert h._rotation_tick() is False and h.console is None  # unknown rotation: adb path from now on


# ---------- S8: "Nested Virtualization" modal ----------

def test_emulator_argv_disables_the_nested_virtualization_dialog() -> None:
    """emulator 31.3.9 `-help`: "-no-nested-warnings  Disable the warning dialog when emulator is running in nested
    virtualization." A command-line flag: nothing is written to any HOME (the owner's emulator settings untouched)."""
    argv = devhost.emulator_argv("/x/emulator", "aas-phone", 5654)
    assert argv[:5] == ["/x/emulator", "-avd", "aas-phone", "-port", "5654"]
    assert "-no-nested-warnings" in argv
    for kept in ("-wipe-data", "-no-snapshot-save", "-no-snapshot-load"):
        assert kept in argv
    assert argv.count(devhost.USER_NET_OPTS) == 2 and devhost.USER_NET_OPTS.startswith("restrict=on")  # both NICs (S9)
