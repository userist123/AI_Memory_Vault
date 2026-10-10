"""The cost-router skill: SKILL.md contract, the prompt hook, the route helper and the installer."""
from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
HOOK = SKILL / "hook_prompt_route.py"
ROUTE = SKILL / "lib" / "route.py"
INSTALL = SKILL / "install.py"


def _frontmatter(text: str) -> dict:
    m = re.match(r"---\n(.*?)\n---", text, re.S)
    assert m, "no frontmatter"
    out = {}
    for line in m.group(1).splitlines():
        k, _, v = line.partition(":")
        out[k.strip()] = v.strip()
    return out


def test_skill_md_contract():
    text = (SKILL / "SKILL.md").read_text(encoding="utf-8")
    fm = _frontmatter(text)
    assert fm["name"] == "cost-router"
    assert len(fm["description"]) + len(fm.get("when_to_use", "")) <= 1536, "skill listing truncates at 1,536 chars"
    assert "model" not in fm and "effort" not in fm, "the skill must not override the owner's session model/effort"
    assert text.count("\n") < 500
    for must in ("Explore", "vault-worker", "vault-reviewer", "Never skip, weaken or delete a test", "UNVERIFIED"):
        assert must in text, must


def _run(args, stdin: str | None = None):
    return subprocess.run([sys.executable, *args], input=stdin, capture_output=True, text=True, timeout=60)


def test_route_helper_resolves_from_repo_layout():
    r = _run([str(ROUTE), "fix the failing test in test_memory_access.py"])
    assert r.returncode == 0, r.stderr
    assert "class=implement" in r.stdout and "vault-worker subagent, sonnet/medium" in r.stdout
    r = _run([str(ROUTE), "security audit of the token handling", "--json"])
    d = json.loads(r.stdout)
    assert d["model"] == "opus" and d["verifier_model"] is None and d["task_class"] == "review"
    r = _run([str(ROUTE), "rotate the production secret", "--json"])
    d = json.loads(r.stdout)
    assert d["model"] == "opus" and d["verifier_model"] == "opus" and d["subagent"] is None


def test_hook_injects_one_line_of_context_for_a_real_prompt():
    r = _run([str(HOOK)], stdin=json.dumps({"prompt": "find which files import memory_controller and list them", "hook_event_name": "UserPromptSubmit"}))
    assert r.returncode == 0, r.stderr
    out = json.loads(r.stdout)
    ctx = out["hookSpecificOutput"]["additionalContext"]
    assert out["hookSpecificOutput"]["hookEventName"] == "UserPromptSubmit"
    assert "class=explore" in ctx and "Explore subagent, haiku/low" in ctx and "cost-router" in ctx
    assert len(ctx) <= 1000


@pytest.mark.parametrize("prompt", ["ok", "da", "/model opus", "yes please", "", "continua"])
def test_hook_is_silent_on_trivial_prompts(prompt):
    r = _run([str(HOOK)], stdin=json.dumps({"prompt": prompt}))
    assert r.returncode == 0 and r.stdout == ""


def test_hook_never_blocks_on_bad_input():
    r = _run([str(HOOK)], stdin="not json")
    assert r.returncode == 0 and r.stdout == ""


def test_install_is_self_contained_idempotent_and_reversible(tmp_path):
    home = tmp_path / "home"
    r = _run([str(INSTALL), "--home", str(home)])
    assert r.returncode == 0, r.stderr
    skill = home / ".claude" / "skills" / "cost-router"
    assert (skill / "SKILL.md").exists() and (skill / "policy.json").exists()
    assert (skill / "lib" / "claude_model_router.py").read_bytes() == (ROOT / "03_IMPLEMENTATION/packages/routing/claude_model_router.py").read_bytes()
    for name in ("Explore.md", "vault-worker.md", "vault-reviewer.md"):
        assert (home / ".claude" / "agents" / name).exists()
    # the installed copy routes without the repository
    r = _run([str(skill / "lib" / "route.py"), "review this diff for regressions"])
    assert r.returncode == 0 and "vault-reviewer subagent, opus/high" in r.stdout
    # hook merged once, even after a second install
    _run([str(INSTALL), "--home", str(home)])
    settings = json.loads((home / ".claude" / "settings.json").read_text())
    entries = settings["hooks"]["UserPromptSubmit"]
    assert len(entries) == 1 and "cost-router/hook_prompt_route.py" in json.dumps(entries)
    # a pre-existing user hook survives
    settings["hooks"]["UserPromptSubmit"].insert(0, {"hooks": [{"type": "command", "command": "echo mine"}]})
    (home / ".claude" / "settings.json").write_text(json.dumps(settings))
    _run([str(INSTALL), "--home", str(home)])
    entries = json.loads((home / ".claude" / "settings.json").read_text())["hooks"]["UserPromptSubmit"]
    assert len(entries) == 2 and entries[0]["hooks"][0]["command"] == "echo mine"
    # --no-hook leaves settings alone; existing agents are kept unless --force
    r = _run([str(INSTALL), "--home", str(home), "--no-hook"])
    assert "kept existing" in r.stdout
    # uninstall removes what it installed and nothing else
    r = _run([str(INSTALL), "--home", str(home), "--uninstall"])
    assert r.returncode == 0 and not skill.exists()
    assert not (home / ".claude" / "agents" / "Explore.md").exists()
    settings = json.loads((home / ".claude" / "settings.json").read_text())
    assert settings["hooks"]["UserPromptSubmit"][0]["hooks"][0]["command"] == "echo mine"


def test_project_settings_register_the_same_hook():
    settings = json.loads((ROOT / ".claude" / "settings.json").read_text(encoding="utf-8"))
    cmds = json.dumps(settings["hooks"]["UserPromptSubmit"])
    assert ".claude/skills/cost-router/hook_prompt_route.py" in cmds
    assert "exit 2" not in cmds, "the route hint must never block a prompt"
    start = settings["hooks"]["SessionStart"]
    assert start[0]["matcher"] == "startup|resume"
    cmd = start[0]["hooks"][0]["command"]
    assert "cost-router/install.py" in cmd and "--session-start" in cmd, "user-scope install must not duplicate the project prompt hook"
    assert cmd.rstrip().endswith("exit 0") and "exit 2" not in cmd


def test_session_start_command_runs_quietly_and_installs(tmp_path):
    """Run the SessionStart command exactly as settings.json spells it, against a scratch HOME."""
    import os
    settings = json.loads((ROOT / ".claude" / "settings.json").read_text(encoding="utf-8"))
    cmd = settings["hooks"]["SessionStart"][0]["hooks"][0]["command"]
    env = dict(os.environ, CLAUDE_PROJECT_DIR=str(ROOT), HOME=str(tmp_path))
    r = subprocess.run(["bash", "-c", cmd], env=env, capture_output=True, text=True, timeout=60)
    assert r.returncode == 0 and r.stdout == "", (r.stdout, r.stderr)
    assert (tmp_path / ".claude" / "skills" / "cost-router" / "SKILL.md").exists()
    assert (tmp_path / ".claude" / "agents" / "Explore.md").exists()
    assert not (tmp_path / ".claude" / "settings.json").exists(), "--session-start writes no user settings"


def test_uninstall_stops_the_session_start_reinstall(tmp_path):
    """Reviewer finding 3: opening the repo must not undo an uninstall."""
    home = tmp_path / "home"
    assert _run([str(INSTALL), "--home", str(home)]).returncode == 0
    assert _run([str(INSTALL), "--home", str(home), "--uninstall"]).returncode == 0
    marker = home / ".claude" / "cost-router.disabled"
    assert marker.exists()
    r = _run([str(INSTALL), "--home", str(home), "--session-start"])
    assert r.returncode == 0 and "skipped" in r.stdout
    assert not (home / ".claude" / "skills" / "cost-router").exists()
    assert not (home / ".claude" / "agents" / "Explore.md").exists()
    # an explicit install re-enables
    assert _run([str(INSTALL), "--home", str(home), "--no-hook"]).returncode == 0
    assert not marker.exists() and (home / ".claude" / "skills" / "cost-router" / "SKILL.md").exists()


def test_install_refuses_a_settings_file_it_cannot_parse_without_touching_anything(tmp_path):
    """Reviewer finding 4: a settings.json with comments must not half-install."""
    home = tmp_path / "home"
    (home / ".claude").mkdir(parents=True)
    (home / ".claude" / "settings.json").write_text('{ // comment\n "model": "opus" }')
    r = _run([str(INSTALL), "--home", str(home)])
    assert r.returncode == 1 and "not strict JSON" in r.stderr
    assert not (home / ".claude" / "skills").exists() and not (home / ".claude" / "agents").exists()
    assert (home / ".claude" / "settings.json").read_text() == '{ // comment\n "model": "opus" }'
    # --no-hook still works without reading settings
    assert _run([str(INSTALL), "--home", str(home), "--no-hook"]).returncode == 0
