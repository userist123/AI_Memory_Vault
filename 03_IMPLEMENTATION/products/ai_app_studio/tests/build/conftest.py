from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

import pytest


def make_link(link: Path, target: Path, kind: str) -> bool:
    """Create a symlink / junction / hardlink; return False when the OS refuses (no privilege)."""
    try:
        if kind == "symlink":
            os.symlink(target, link, target_is_directory=target.is_dir())
        elif kind == "hardlink":
            os.link(target, link)
        elif kind == "junction":
            if sys.platform != "win32":
                return False
            r = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(target)], capture_output=True)  # noqa: S603,S607 - test helper
            return r.returncode == 0
        return True
    except (OSError, NotImplementedError):
        return False


@pytest.fixture()
def canary(tmp_path: Path) -> Path:
    f = tmp_path / "host_canary.txt"
    f.write_text("SECRET-CANARY", encoding="utf-8")
    return f


@pytest.fixture()
def out_dir(tmp_path: Path) -> Path:
    d = tmp_path / "out"
    (d / "dist" / "assets").mkdir(parents=True)
    (d / "dist" / "index.html").write_text("<!doctype html><title>x</title>", encoding="utf-8")
    (d / "dist" / "assets" / "app.js").write_text("console.log(1)", encoding="utf-8")
    return d
