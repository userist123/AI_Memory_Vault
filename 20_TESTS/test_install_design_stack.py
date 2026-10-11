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
    marker.write_text(json.dumps({"version": mod.VERSION}) + "\n")
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
    fake.write_text("#!/bin/sh\necho '\u2718 already enabled'\nexit 1\n", encoding="utf-8")
    fake.chmod(0o755)
    env = dict(os.environ, PYTHONIOENCODING="cp1252", PATH=f"{fake.parent}{os.pathsep}{os.environ['PATH']}")
    r = subprocess.run([sys.executable, str(SCRIPT), "--home", str(tmp_path / "home"), "--apply"],
                       capture_output=True, text=True, encoding="cp1252", errors="replace", env=env)
    assert "Traceback" not in r.stderr, r.stderr
    assert "ok   enable plugin frontend-design" in r.stdout


def test_plan_copies_the_three_directives_and_only_five_replica_skills(tmp_path):
    names = [d.split()[2] for d, cmd in mod.plan(tmp_path, windows=False) if cmd is None]
    assert names == ["creative-web-director", "security-lockdown", "graph-orchestrator",
                     "replica-recon", "replica-architect", "replica-entrepreneur", "replica-build", "replica-brand"]
    for name in names:
        assert (mod.skill_source(name) / "SKILL.md").is_file(), name
    for name in mod.REPLICA_SKILLS:  # vendored copies keep their provenance
        prov = json.loads((mod.skill_source(name) / "PROVENANCE.json").read_text(encoding="utf-8"))
        assert prov["source_commit"].startswith("77c9436f") and prov["license"] == "MIT"


def test_replica_triggers_are_narrowed_on_install_and_the_vendored_copy_is_untouched(tmp_path):
    folded = "---\nname: x\ndescription: >-\n  Clones apps. Use when the user says how does X work.\n---\nbody\n"
    out = mod.gate_description(folded)
    assert "description: >-\n  " + mod.REPLICA_GATE + "Clones apps." in out and out.endswith("---\nbody\n")
    plain = "---\nname: x\ndescription: Clones apps.\n---\n"
    assert mod.gate_description(plain) == "---\nname: x\ndescription: " + mod.REPLICA_GATE + "Clones apps.\n---\n"
    src = (mod.skill_source("replica-recon") / "SKILL.md").read_text(encoding="utf-8")
    mod.copy_skill("replica-recon", tmp_path)
    installed = (tmp_path / ".claude" / "skills" / "replica-recon" / "SKILL.md").read_text(encoding="utf-8")
    assert mod.REPLICA_GATE in installed and mod.REPLICA_GATE not in src
    assert installed.replace(mod.REPLICA_GATE, "") == src, "only the description gate is added"
    mod.copy_skill("security-lockdown", tmp_path)
    assert mod.REPLICA_GATE not in (tmp_path / ".claude" / "skills" / "security-lockdown" / "SKILL.md").read_text(encoding="utf-8")


def test_an_older_marker_runs_the_new_stack_once(tmp_path):
    marker = tmp_path / ".claude" / mod.MARKER_NAME
    marker.parent.mkdir(parents=True)
    marker.write_text('{"version": "2026-10-11.1"}\n')
    r = subprocess.run([sys.executable, str(SCRIPT), "--home", str(tmp_path), "--apply"], capture_output=True, text=True,
                       env={"PATH": "", "SYSTEMROOT": __import__("os").environ.get("SYSTEMROOT", "")})
    assert "already installed" not in r.stdout
    assert (tmp_path / ".claude" / "skills" / "graph-orchestrator" / "SKILL.md").is_file()


def test_owner_skills_are_bounded():
    sec = (ROOT / ".claude" / "skills" / "security-lockdown" / "SKILL.md").read_text(encoding="utf-8")
    assert sec.startswith("---\nname: security-lockdown\n") and "SECURITY_AUDIT.md" in sec and "19." in sec
    assert "never rewrite or force-push without the owner" in sec
    graph = (ROOT / ".claude" / "skills" / "graph-orchestrator" / "SKILL.md").read_text(encoding="utf-8")
    for must in ("never inside the AI_Memory_Vault", "replica/screens/", "no bulk scraping", "Never copy the"):
        assert must in graph, must
