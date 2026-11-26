"""S9a item 9 (device host, unit level): emulator argv (both NICs restrict=on,ipv6=off, gRPC behind a JWT, no snapshot,
GPU mode), console auth token non-empty or no start (an empty ~/.emulator_console_auth_token disables console auth),
and no `adb reverse` / `adb forward` from the device host. The live part is in docs/slices/S9.md."""
from __future__ import annotations

import os
import stat
import sys
from pathlib import Path
from typing import Any

import pytest

from tests.local_gate.test_devhost_unit import _host, devhost


def test_emulator_argv_hardening() -> None:
    a = devhost.emulator_argv("/sdk/emulator/emulator", "aas-phone", 5654)
    opt = {a[i]: a[i + 1] for i in range(len(a) - 1) if a[i].startswith("-") and not a[i + 1].startswith("-")}
    assert opt["-network-user-mode-options"] == "restrict=on,ipv6=off"
    assert opt["-wifi-user-mode-options"] == "restrict=on,ipv6=off"
    assert "-grpc-use-jwt" in a  # gRPC (console port + 3000) only with a JWT signed by the studio-home key
    assert "-no-snapshot" in a and "-wipe-data" in a
    assert opt["-gpu"] == devhost.GPU_MODE and devhost.GPU_MODE in ("host", "swiftshader_indirect", "guest")
    assert "-grpc" not in a  # no explicit gRPC port / no gRPC outside the defaults


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX file modes (the device host runs in WSL)")
def test_console_token_is_created_private_and_non_empty(tmp_path: Path) -> None:
    p = tmp_path / ".emulator_console_auth_token"
    devhost.ensure_console_token(str(p))
    data = p.read_bytes()
    assert len(data.strip()) >= 16 and devhost.Console.TOKEN_RE.fullmatch(data.decode().strip())
    assert stat.S_IMODE(p.stat().st_mode) == 0o600
    devhost.ensure_console_token(str(p))  # an existing valid token is kept
    assert p.read_bytes() == data


def test_empty_console_token_fails_closed(tmp_path: Path) -> None:
    p = tmp_path / ".emulator_console_auth_token"
    p.write_bytes(b"")
    with pytest.raises(devhost.ConsoleTokenError):
        devhost.ensure_console_token(str(p))
    p.write_bytes(b"   \n")
    with pytest.raises(devhost.ConsoleTokenError):
        devhost.ensure_console_token(str(p))
    p.write_bytes(b"bad token with spaces")
    with pytest.raises(devhost.ConsoleTokenError):
        devhost.ensure_console_token(str(p))


@pytest.mark.skipif(sys.platform == "win32", reason="symlinks need privileges on Windows")
def test_console_token_symlink_is_refused(tmp_path: Path) -> None:
    real = tmp_path / "elsewhere"
    real.write_text("abcdefghijklmnop1234")
    link = tmp_path / ".emulator_console_auth_token"
    os.symlink(real, link)
    with pytest.raises(devhost.ConsoleTokenError):
        devhost.ensure_console_token(str(link))


def test_start_refuses_without_a_valid_console_token(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    h = _host(tmp_path)
    home = tmp_path / "home"
    home.mkdir()
    (home / ".emulator_console_auth_token").write_bytes(b"")
    monkeypatch.setenv("HOME", str(home))
    started: list[Any] = []
    monkeypatch.setattr(devhost.subprocess, "Popen", lambda *a, **k: started.append(a))
    monkeypatch.setattr(devhost.subprocess, "run", lambda *a, **k: started.append(a))
    h._clear_locks = lambda: None
    assert h.start() is False
    assert started == []  # neither adb start-server nor the emulator ran
    assert h.out.lines[-1]["reason"] == "console-token"


@pytest.mark.parametrize("verb", ["reverse", "forward"])
def test_device_host_never_runs_adb_reverse_or_forward(tmp_path: Path, verb: str) -> None:
    h = devhost.Host.__new__(devhost.Host)
    h.adb_bin, h.serial = "/sdk/platform-tools/adb", "emulator-5654"
    with pytest.raises(ValueError):
        devhost.Host.adb(h, verb, "tcp:8080", "tcp:8080")
    with pytest.raises(ValueError):
        devhost.Host.adb(h, "-s", "emulator-5654", verb, "--list")
