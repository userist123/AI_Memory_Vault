"""The test suite must not mutate the checkout's telemetry/ directory (E-08)."""
from __future__ import annotations

import hashlib
import os
import subprocess
import sys
from pathlib import Path

from observability.memory_trace import _resolve_trace_path

ROOT = Path(__file__).resolve().parents[1]


def test_trace_path_resolves_under_pytest_tmp_dir(tmp_path_factory):
    resolved = _resolve_trace_path().resolve()
    base = tmp_path_factory.getbasetemp().resolve()
    assert base in resolved.parents, resolved
    assert (ROOT / "telemetry") not in resolved.parents


def _digest(path: Path) -> str | None:
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None


def test_known_writer_module_leaves_checkout_telemetry_untouched(tmp_path):
    trace = ROOT / "telemetry" / "observed_memory_traces.jsonl"
    before = _digest(trace)
    env = {k: v for k, v in os.environ.items() if k != "ANTIGRAVITY_TELEMETRY_DIR"}
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    proc = subprocess.run(
        [sys.executable, "-m", "pytest", "-q", "-p", "no:cacheprovider", "--basetemp", str(tmp_path / "bt"),
         str(ROOT / "20_TESTS" / "memory_controller" / "test_cache.py")],
        cwd=ROOT, env=env, capture_output=True, text=True, timeout=300)
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert _digest(trace) == before
