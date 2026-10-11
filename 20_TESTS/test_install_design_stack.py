"""The one-time design-stack installer: scope matches the intake review, dry run by default, runs once."""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "30_SCRIPTS" / "setup" / "install_design_stack.py"
spec = importlib.util.spec_from_file_location("install_design_stack", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


def test_plan_installs_only_what_the_review_cleared(tmp_path):
    text = json.dumps(mod.plan(tmp_path, windows=True))
    for ok in ("frontend-design@claude-plugins-official", "ralph-loop@claude-plugins-official", "chrome-devtools-mcp@1.9.0"):
        assert ok in text
    for rejected in ("impeccable", "skills add", "find-skills", "one-skill"):
        assert rejected not in text, rejected


def test_chrome_devtools_runs_pinned_without_telemetry_and_isolated():
    win = mod.mcp_command(windows=True)
    assert win[win.index("--") + 1:win.index("--") + 4] == ["cmd", "/c", "npx"]
    nix = mod.mcp_command(windows=False)
    for cmd in (win, nix):
        assert "chrome-devtools-mcp@1.9.0" in cmd and "--scope" in cmd and "user" in cmd
        for flag in ("--no-usage-statistics", "--no-performance-crux", "--isolated"):
            assert flag in cmd
        assert "CHROME_DEVTOOLS_MCP_NO_USAGE_STATISTICS=1" in cmd
        assert not any("@latest" in c for c in cmd)


def test_dry_run_by_default_and_second_apply_is_a_no_op(tmp_path):
    r = subprocess.run([sys.executable, str(SCRIPT), "--home", str(tmp_path)], capture_output=True, text=True)
    assert r.returncode == 0 and "Dry run" in r.stdout and not (tmp_path / ".claude").exists()
    marker = tmp_path / ".claude" / mod.MARKER_NAME
    marker.parent.mkdir(parents=True)
    marker.write_text('{"version": "x"}\n')
    r = subprocess.run([sys.executable, str(SCRIPT), "--home", str(tmp_path), "--apply"], capture_output=True, text=True)
    assert r.returncode == 0 and "already installed" in r.stdout


def test_directive_skill_is_automatic_and_bounded():
    text = (ROOT / ".claude" / "skills" / "creative-web-director" / "SKILL.md").read_text(encoding="utf-8")
    assert text.startswith("---\nname: creative-web-director\n")
    for must in ("PRODUCT.md", "DESIGN.md", "PLAN.md", "OKLCH", "chrome-devtools", "--max-iterations",
                 "never `npx skills add`", "bypass-permissions", "memory_propose"):
        assert must in text, must



def test_a_cp1252_console_does_not_crash_the_run(tmp_path):
    """Seen on the owner's PC: `claude plugin enable` prints ✘ and the cp1252 console crashed print()."""
    import os
    if os.name == "nt":
        import pytest
        pytest.skip("fake claude is a POSIX shell script")
    fake = tmp_path / "bin" / "claude"
    fake.parent.mkdir()
    fake.write_text("#!/bin/sh\nprintf '\\342\\234\\230 already enabled\\n'\nexit 1\n", encoding="utf-8")
    fake.chmod(0o755)
    env = dict(os.environ, PYTHONIOENCODING="cp1252", PATH=f"{fake.parent}{os.pathsep}{os.environ['PATH']}")
    r = subprocess.run([sys.executable, str(SCRIPT), "--home", str(tmp_path / "home"), "--apply"],
                       capture_output=True, text=True, encoding="cp1252", errors="replace", env=env)
    assert "Traceback" not in r.stderr, r.stderr
    assert "ok   enable plugin frontend-design" in r.stdout
