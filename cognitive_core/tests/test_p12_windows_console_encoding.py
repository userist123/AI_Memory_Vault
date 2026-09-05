"""Windows console UnicodeEncodeError regression tests.

Empirically reproduced during the P1.2/P2.1 audit: on stock Windows console
encoding (cp1252, no PYTHONUTF8), every one of the 5 CLI entry points in the
original package crashed on a print() containing Romanian diacritics or
check-mark symbols -- and for edge_proposer.py and plasticity_update.py the
crash occurred BEFORE the artifact was written, i.e. the primary deliverable
was never produced at all on this repo's actual platform (win32).

These tests simulate that exact failure mode (a cp1252, strict-errors text
stream, matching Windows' real default) and assert `_ensure_utf8_stdout()`
prevents it in every affected module.
"""
from __future__ import annotations

import importlib.util
import io
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]


def _load(module_name: str, path: Path):
    spec = importlib.util.spec_from_file_location(module_name, path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = mod
    spec.loader.exec_module(mod)
    return mod


@pytest.fixture(params=[
    "cognitive_core.brain_pack",
    "cognitive_core.benchmarks.retrieval_ab",
])
def package_module(request):
    import importlib
    return importlib.import_module(request.param)


@pytest.fixture(params=[
    ("edge_proposer", ROOT / "30_SCRIPTS" / "knowledge" / "edge_proposer.py"),
    ("vault_hygiene", ROOT / "30_SCRIPTS" / "knowledge" / "vault_hygiene.py"),
    ("plasticity_update", ROOT / "30_SCRIPTS" / "knowledge" / "plasticity_update.py"),
])
def script_module(request):
    name, path = request.param
    return _load(f"{name}_encoding_test", path)


def _cp1252_strict_stream() -> io.TextIOWrapper:
    """A text stream that behaves exactly like the Windows console default
    that caused the original crash: cp1252 codepage, strict error handling."""
    return io.TextIOWrapper(io.BytesIO(), encoding="cp1252", errors="strict")


PROBLEM_STRINGS = [
    "densitate estimată: 0.5 muchii/nod",   # ă -- crashed edge_proposer.py
    "✓ succes",                              # check mark -- crashed plasticity_update.py
    "eșantion 80 · interogări",     # ș / ț family -- crashed retrieval_ab.py
]


def test_package_module_ensure_utf8_stdout_survives_cp1252_console(package_module, monkeypatch):
    fake_stdout = _cp1252_strict_stream()
    monkeypatch.setattr(sys, "stdout", fake_stdout)
    package_module._ensure_utf8_stdout()
    for s in PROBLEM_STRINGS:
        print(s)  # must not raise UnicodeEncodeError anymore
    sys.stdout.flush()


def test_script_module_ensure_utf8_stdout_survives_cp1252_console(script_module, monkeypatch):
    fake_stdout = _cp1252_strict_stream()
    monkeypatch.setattr(sys, "stdout", fake_stdout)
    script_module._ensure_utf8_stdout()
    for s in PROBLEM_STRINGS:
        print(s)
    sys.stdout.flush()


def test_baseline_confirms_the_original_bug_would_have_reproduced(monkeypatch):
    """Sanity check that PROBLEM_STRINGS actually DO crash a raw cp1252
    stream when _ensure_utf8_stdout is NOT called -- otherwise the tests
    above would be vacuously true."""
    fake_stdout = _cp1252_strict_stream()
    monkeypatch.setattr(sys, "stdout", fake_stdout)
    with pytest.raises(UnicodeEncodeError):
        print(PROBLEM_STRINGS[0])
        sys.stdout.flush()


def test_ensure_utf8_stdout_is_idempotent_and_safe_on_a_normal_stream(package_module):
    # Calling it twice, or on an already-UTF8 stream, must never raise.
    package_module._ensure_utf8_stdout()
    package_module._ensure_utf8_stdout()
