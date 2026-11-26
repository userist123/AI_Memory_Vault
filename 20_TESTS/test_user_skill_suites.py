"""Run the self-tests that ship inside skill folders, so CI really executes them.

The suites live next to their skill (``.claude/skills/<name>/...``) because the copy
installed under ``~/.claude/skills`` must stay self-testing. ``pytest.ini`` only
collects ``20_TESTS``, so ``test_no_orphaned_test_files.py`` allows these exact
paths and this module is what keeps that allowance honest: each suite runs in its
own isolated interpreter (``-I -X utf8 -B``) from its skill folder, exactly as it is
run by hand, and must exit 0 with unittest's ``OK``.

Tests that need an external binary (ffmpeg/ffprobe, Blender) skip themselves when
that binary is absent; the number of skipped tests is recorded and reported, never
hidden.
"""
import re
import subprocess
import sys
import warnings
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]

SKILL_SUITES = (
    ".claude/skills/analist-juridic/tests/test_scripts.py",
    ".claude/skills/animatie-3d/test_animatie3d.py",
    ".claude/skills/clips-kitty/test_hook_block_autopublish.py",
    ".claude/skills/prompt-director/test_hook_prompt_intake.py",
)


def _tracked_skill_tests():
    out = subprocess.run(
        ["git", "ls-files", ".claude/skills/"],
        cwd=REPO, capture_output=True, text=True, check=False,
    )
    if out.returncode != 0:
        pytest.skip("git not available")
    return sorted(
        p for p in out.stdout.splitlines()
        if Path(p).name.startswith("test_") and p.endswith(".py")
    )


def test_every_tracked_skill_test_is_run_here():
    assert _tracked_skill_tests() == sorted(SKILL_SUITES), (
        "A skill self-test was added or removed; list it in SKILL_SUITES (and in "
        "ALLOWED_OUTSIDE of test_no_orphaned_test_files.py) so CI keeps running it."
    )


@pytest.mark.parametrize("rel", SKILL_SUITES)
def test_skill_suite_passes(rel, record_property):
    suite = REPO / rel
    assert suite.is_file(), rel
    r = subprocess.run(
        [sys.executable, "-I", "-X", "utf8", "-B", suite.name],
        cwd=suite.parent, capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=600,
    )
    out = r.stdout + r.stderr
    assert r.returncode == 0, f"{rel} exited {r.returncode}:\n{out[-4000:]}"
    ran = re.search(r"^Ran (\d+) tests? in ", out, re.M)
    assert ran and int(ran.group(1)) > 0, f"{rel} ran no tests:\n{out[-2000:]}"
    ok = re.search(r"^OK(?: \((.*)\))?\s*$", out, re.M)
    assert ok, f"{rel} did not report OK:\n{out[-4000:]}"
    skipped = 0
    if ok.group(1):
        m = re.search(r"skipped=(\d+)", ok.group(1))
        skipped = int(m.group(1)) if m else 0
    record_property("tests_run", int(ran.group(1)))
    record_property("tests_skipped", skipped)
    if skipped:
        warnings.warn(
            f"{rel}: {skipped} of {ran.group(1)} tests skipped (external binary absent)",
            stacklevel=1,
        )
