"""P10a (T10): studio code and scripts never name the owner's emulator state (static scan, non-live)."""
from __future__ import annotations

import re
from pathlib import Path

import pytest

from tests.local_gate.conftest import PRODUCT

ROOTS = ("core", "wsl", "kit", "images", "apps")
FORBIDDEN = ("s9emu", "debug.keystore", "/.android", "~/android", "HOME/android", "5037")
# "debug.keystore" only as the owner's file name: the studio's own key is aas-debug.keystore.
# "5037" only as a standalone number (the adb port): a sha256 digest in the committed Gradle verification metadata may
# contain those digits inside hex text.
PATTERNS = {w: re.compile(r"(?<![\w-])" + re.escape(w)) if w == "debug.keystore"
            else re.compile(r"(?<![0-9A-Za-z])5037(?![0-9A-Za-z])") if w == "5037" else re.compile(re.escape(w))
            for w in FORBIDDEN}
SKIP_DIRS = {"node_modules", "__pycache__", ".venv", "target", "dist"}


def scan(base: Path) -> list[tuple[str, str]]:
    hits = []
    for root in ROOTS:
        d = base / root
        if not d.exists():
            continue
        for p in d.rglob("*"):
            if not p.is_file() or SKIP_DIRS & set(p.parts):
                continue
            try:
                text = p.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                text = p.read_bytes().decode("latin-1")
            for word in FORBIDDEN:
                if PATTERNS[word].search(text):
                    hits.append((p.relative_to(base).as_posix(), word))
    return hits


@pytest.mark.probe("P10a")
def test_studio_code_never_names_owner_state(request: pytest.FixtureRequest) -> None:
    hits = scan(PRODUCT)
    request.node.aas_detail = {"roots": list(ROOTS), "forbidden": list(FORBIDDEN), "hits": hits}
    assert hits == []


@pytest.mark.probe("P10a")
def test_scan_positive_control_flags_fixture(tmp_path: Path) -> None:
    (tmp_path / "wsl").mkdir()
    (tmp_path / "wsl" / "x.sh").write_text("emulator -avd s9emu\n", encoding="utf-8")
    (tmp_path / "core").mkdir()
    (tmp_path / "core" / "y.py").write_text("ADB = 5037\n", encoding="utf-8")
    assert sorted(scan(tmp_path)) == [("core/y.py", "5037"), ("wsl/x.sh", "s9emu")]
    (tmp_path / "apps").mkdir()
    (tmp_path / "apps" / "h.xml").write_text('<sha256 value="fc4917dc5d41fe50372154fa"/>\n', encoding="utf-8")  # hex, not a port
    assert sorted(scan(tmp_path)) == [("core/y.py", "5037"), ("wsl/x.sh", "s9emu")]
