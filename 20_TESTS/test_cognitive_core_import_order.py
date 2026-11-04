"""Every memory-package module imports cleanly when it is the FIRST thing imported.

`memory/controller.py` used to import Executive, ReasoningEngine, WorkingMemory and
GlobalWorkspace at the bottom of the file. That broke the import cycle only when the
controller was imported first. `import cognitive_core.executive` (or reasoning, recall,
orchestrator, working_memory, global_workspace, controller) as a first import failed with
"partially initialized module", so `20_TESTS/test_council_memory_budget.py` failed when run
alone and passed in the full suite only because another file had imported the controller.

Each import runs in a fresh interpreter: inside pytest, earlier test files have already
populated sys.modules and would hide exactly this failure.
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
PACKAGES = REPO / "03_IMPLEMENTATION" / "packages"
MEMORY = PACKAGES / "memory"

MODULES = sorted(p.stem for p in MEMORY.glob("*.py") if p.stem != "__init__")


def _first_import(dotted: str) -> subprocess.CompletedProcess:
    env = dict(os.environ)
    env["PYTHONPATH"] = str(PACKAGES)
    env.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
    return subprocess.run([sys.executable, "-c", f"import {dotted}"], cwd=REPO, env=env,
                          capture_output=True, text=True, timeout=120)


def test_there_are_modules_to_check():
    assert {"controller", "executive", "reasoning", "recall"} <= set(MODULES)


@pytest.mark.parametrize("dotted", [
    *(f"cognitive_core.{m}" for m in ("controller", "executive", "reasoning", "recall", "orchestrator",
                                      "working_memory", "global_workspace")),
    # the namespace production code and most tests use for the controller; the other memory
    # modules were never importable through `memory_controller` (its __path__ has no graph/ or
    # learning/, so their relative imports fail regardless of order)
    "memory_controller.controller",
])
def test_the_modules_that_used_to_cycle_import_first(dotted):
    done = _first_import(dotted)
    assert done.returncode == 0, done.stderr[-2000:]


@pytest.mark.parametrize("module", MODULES)
def test_every_memory_module_imports_first(module):
    done = _first_import(f"cognitive_core.{module}")
    assert done.returncode == 0, done.stderr[-2000:]
