"""N4: the launcher spawns wsl.exe with a minimal env, so a BASH_ENV canary imported via WSLENV never runs."""
from __future__ import annotations

import os
import subprocess

import pytest

from apps.device_host import launcher
from tests.local_gate.conftest import live

CANARY = "$HOME/.aas/run/n4-canary.sh"
MARK = "$HOME/.aas/run/n4-canary.ran"


def _arm(sh) -> None:  # type: ignore[no-untyped-def]
    r = sh(f'mkdir -p "$HOME/.aas/run"; rm -f "{MARK}"\n'
           f"printf 'touch \"%s\"\\n' \"{MARK}\" > \"{CANARY}\"; chmod 0700 \"{CANARY}\"; echo armed\n")
    assert "armed" in r.stdout, r.stderr


def _ran(sh) -> bool:  # type: ignore[no-untyped-def]
    return "RAN" in sh(f'[ -e "{MARK}" ] && echo RAN || echo CLEAN\n').stdout


@live
@pytest.mark.probe("N4")
def test_bash_env_canary_does_not_run_through_launcher(sh, monkeypatch: pytest.MonkeyPatch) -> None:  # type: ignore[no-untyped-def]
    _arm(sh)
    monkeypatch.setenv("BASH_ENV", CANARY)
    monkeypatch.setenv("WSLENV", "BASH_ENV/u")
    info = launcher.preflight()
    assert info["networkingMode"] == "nat"
    assert not _ran(sh)


@live
@pytest.mark.probe("N4")
def test_positive_control_canary_runs_without_scrub(sh) -> None:  # type: ignore[no-untyped-def]
    _arm(sh)
    env = dict(os.environ, BASH_ENV=CANARY, WSLENV="BASH_ENV/u")
    r = subprocess.run(launcher.argv_for("preflight"), env=env, capture_output=True, timeout=120)
    assert r.returncode == 0, r.stderr
    assert _ran(sh)
    sh(f'rm -f "{MARK}" "{CANARY}"\n')
