"""S6 review item 6: the Tauri shell starts the backend isolated (`python -I -c <launcher> <root> ...`). The launcher
string is taken from apps/desktop/src-tauri/src/lib.rs and run for real with a poisoned PYTHONPATH and a poisoned cwd:
the real `apps.backend` must be the one that runs."""
from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path

PRODUCT = Path(__file__).resolve().parents[2]
LIB_RS = PRODUCT / "apps" / "desktop" / "src-tauri" / "src" / "lib.rs"


def launcher() -> str:
    m = re.search(r'BACKEND_LAUNCHER: &str = "((?:[^"\\]|\\.)*)";', LIB_RS.read_text(encoding="utf-8"), re.S)
    assert m, "BACKEND_LAUNCHER not found in lib.rs"
    return re.sub(r"\\\r?\n\s*", "", m.group(1))  # Rust string continuation lines


def _poison(d: Path) -> None:
    pkg = d / "apps" / "backend"
    pkg.mkdir(parents=True)
    (d / "apps" / "__init__.py").write_text("", encoding="utf-8")
    (pkg / "__init__.py").write_text("", encoding="utf-8")
    (pkg / "__main__.py").write_text("print('POISONED'); raise SystemExit(7)\n", encoding="utf-8")
    (d / "fastapi").mkdir()  # shadows a dependency: what PYTHONPATH can do to the old `python -m apps.backend`
    (d / "fastapi" / "__init__.py").write_text("print('POISONED'); raise SystemExit(7)\n", encoding="utf-8")


def test_isolated_launcher_runs_the_real_backend_despite_poisoned_pythonpath_and_cwd(tmp_path: Path) -> None:
    evil_path, evil_cwd = tmp_path / "pp", tmp_path / "cwd"
    _poison(evil_path)
    _poison(evil_cwd)
    env = {**os.environ, "PYTHONPATH": str(evil_path), "AAS_DEV_ORIGINS": "http://127.0.0.1:1421"}
    r = subprocess.run([sys.executable, "-I", "-X", "utf8", "-c", launcher(), str(PRODUCT), "--help"],  # noqa: S603
                       cwd=evil_cwd, env=env, capture_output=True, text=True, timeout=120)
    assert r.returncode == 0, r.stdout + r.stderr
    assert "POISONED" not in r.stdout and "--token-stdin" in r.stdout


def test_positive_control_without_isolation_the_poison_wins(tmp_path: Path) -> None:
    evil = tmp_path / "pp"
    _poison(evil)
    r = subprocess.run([sys.executable, "-m", "apps.backend", "--help"], cwd=PRODUCT,  # noqa: S603
                       env={**os.environ, "PYTHONPATH": str(evil)}, capture_output=True, text=True, timeout=120)
    assert "POISONED" in r.stdout and r.returncode == 7
