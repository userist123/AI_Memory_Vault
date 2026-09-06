"""Guard: a test file outside `testpaths` is a test that never runs.

r009 and r010 each shipped 15 tests — including the adversarial proof that a
graph edge cannot surface a note the lifecycle/principal filters excluded —
into a top-level `tests/` directory. `pytest.ini` sets `testpaths = 20_TESTS`,
so all 30 were collected by nobody. They passed when run by hand and were
invisible to CI, which is the worst combination: the security boundary looked
covered while a regression in it would have gone undetected.

This guard fails the build when that happens again.
"""
import subprocess
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]

#: Areas deliberately outside the collected suite. Each entry is a prefix and
#: needs a reason: these are self-contained products or evaluation labs with
#: their own runners, not runtime-package tests.
ALLOWED_OUTSIDE = {
    "03_IMPLEMENTATION/products/": "shipped products carry their own runners",
    "07_EVALUATION/": "evaluation labs are executed by their own harnesses",
    ".agents/": "vendored external skill material, not our tests",
    "02_PRODUCT/projects/": "imported project working copies",
    "80_ARCHIVE/": "archived",
    "20_TESTS/": "the collected suite itself",
}


def _tracked_test_files():
    out = subprocess.run(
        ["git", "ls-files", "*test_*.py"],
        cwd=REPO, capture_output=True, text=True, check=False,
    )
    if out.returncode != 0:
        pytest.skip("git not available")
    return [
        p for p in out.stdout.splitlines()
        if Path(p).name.startswith("test_") and p.endswith(".py")
    ]


def test_no_test_file_lives_outside_the_collected_suite():
    orphans = [
        p for p in _tracked_test_files()
        if not any(p.startswith(prefix) for prefix in ALLOWED_OUTSIDE)
    ]
    assert not orphans, (
        "These test files are tracked but never collected, because "
        "pytest.ini sets testpaths = 20_TESTS. Move them into 20_TESTS/ or "
        "add an explicit, justified entry to ALLOWED_OUTSIDE:\n  "
        + "\n  ".join(sorted(orphans))
    )


def test_pytest_ini_still_points_at_the_directory_this_guard_assumes():
    """If testpaths changes, this guard's premise changes with it."""
    ini = (REPO / "pytest.ini").read_text(encoding="utf-8")
    assert "testpaths = 20_TESTS" in ini, (
        "pytest.ini no longer collects 20_TESTS; update this guard's "
        "assumptions deliberately rather than letting it silently pass."
    )
