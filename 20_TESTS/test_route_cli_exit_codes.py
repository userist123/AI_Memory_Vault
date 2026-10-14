"""route_cli must report dispatch outcome through its exit code (E-02).

Fake executables on an isolated PATH are used; no real agent is ever invoked.
"""
from __future__ import annotations

import json
import os
import stat
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
PKGS = ROOT / "03_IMPLEMENTATION" / "packages"

pytestmark = [pytest.mark.fake_executable, pytest.mark.skipif(os.name == "nt", reason="fake executables are POSIX scripts")]

SCRIPT = "#!{python}\nimport sys\nsys.stdin.read()\n{body}\n"


def _fakes(tmp_path: Path, fail: tuple[str, ...] = ()) -> Path:
    bindir = tmp_path / "fakebin"
    bindir.mkdir()
    for name in ("claude", "codex", "agy", "ollama"):
        body = "sys.exit(1)" if name in fail else (
            'print(\'{"event": "result", "result": {"response": "ok"}}\')' if name == "agy" else 'print("ok")')
        exe = bindir / name
        exe.write_text(SCRIPT.format(python=sys.executable, body=body), encoding="utf-8")
        exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
    return bindir


def _run(tmp_path: Path, bindir: Path, *args: str) -> subprocess.CompletedProcess:
    env = {
        "PATH": str(bindir),
        "PYTHONPATH": os.pathsep.join([str(ROOT), str(PKGS)]),
        "AI_MEMORY_VAULT_HOME": str(tmp_path / "home"),
        "ANTIGRAVITY_ARTIFACT_DIR": str(tmp_path / "art"),
        "PYTHONDONTWRITEBYTECODE": "1",
    }
    return subprocess.run([sys.executable, "-m", "routing.route_cli", *args], env=env, cwd=tmp_path,
                          capture_output=True, text=True, timeout=60)


def test_completed_dispatch_exits_zero(tmp_path):
    p = _run(tmp_path, _fakes(tmp_path), "dispatch", "--runtime", "claude_code", "--execute", "--goal", "g")
    assert p.returncode == 0, p.stdout + p.stderr
    assert '"status": "completed"' in p.stdout


def test_failed_dispatch_exits_one(tmp_path):
    p = _run(tmp_path, _fakes(tmp_path, fail=("claude",)), "dispatch", "--runtime", "claude_code", "--execute", "--goal", "g")
    assert '"status": "failed"' in p.stdout
    assert p.returncode == 1


def test_pending_verification_exits_two(tmp_path):
    p = _run(tmp_path, _fakes(tmp_path), "dispatch", "--code-exec", "--verify", "--execute", "--goal", "g")
    assert '"status": "pending-verification"' in p.stdout, p.stdout + p.stderr
    assert p.returncode == 2


def test_blocked_route_exits_one_and_json_is_unchanged(tmp_path):
    bindir = _fakes(tmp_path)
    p = _run(tmp_path, bindir, "dispatch", "--runtime", "nonexistent_runtime", "--execute", "--goal", "g")
    assert p.returncode == 1
    r = _run(tmp_path, bindir, "route", "--runtime", "nonexistent_runtime", "--goal", "g")
    assert json.loads(r.stdout)["status"] == "BLOCKED"
    assert r.returncode == 1


def test_route_smoke_case_still_exits_zero(tmp_path):
    """Mirrors the agent-routing-tests.yml smoke step."""
    p = _run(tmp_path, _fakes(tmp_path), "route", "--goal",
             "inspect the repository architecture and propose a safe implementation plan")
    assert json.loads(p.stdout)["status"] in {"ROUTED", "PLANNED"}
    assert p.returncode == 0


def test_probe_exits_zero(tmp_path):
    assert _run(tmp_path, _fakes(tmp_path), "probe").returncode == 0
