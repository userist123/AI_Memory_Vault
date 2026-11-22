"""cost-router, boundary of the GLOBAL (user-scope) registration: one test per probe P1-P6.

Threat model: the hooks run in every project of the machine, including repositories cloned from the internet.
Attacker = the content of a project directory, a pasted prompt, or a same-user process that writes project files.
Every test uses a temporary HOME; the real ~/.claude is never read or written.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
INSTALL = SKILL / "install.py"
AGENT = SKILL / "hook_agent_model.py"
PROMPT = SKILL / "hook_prompt_route.py"
SID = "sess-gb"


def _env(home: Path, **extra) -> dict:
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), APPDATA=str(home), **extra)
    for k in ("CLAUDE_PROJECT_DIR", "COST_ROUTER_ENFORCE", "COST_ROUTER_OFF", "COST_ROUTER_VERBOSE_ROUTE",
              "ANTHROPIC_MODEL", "CLAUDE_CODE_SUBAGENT_MODEL", "PYTHONPATH"):
        if k not in extra:
            env.pop(k, None)
    return env


def _run(script: Path, args=(), stdin: str = "", home: Path | None = None, cwd: Path | None = None, **extra):
    return subprocess.run([sys.executable, str(script), *args], input=stdin, capture_output=True, text=True,
                          timeout=60, env=_env(home, **extra), cwd=str(cwd) if cwd else None, encoding="utf-8")


def _install(home: Path, *args):
    r = _run(INSTALL, ["--home", str(home), *args], home=home)
    assert r.returncode == 0, r.stdout + r.stderr
    return r


def _installed_skill(home: Path) -> Path:
    return home / ".claude" / "skills" / "cost-router"


# ---- P6 (M1): --verify from the installed copy -------------------------------------------------------------------

def test_p6_installed_verify_works_and_detects_tamper(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    _install(home)
    inst = _installed_skill(home) / "install.py"
    # the installed copy must not resolve its source to the home directory: it verifies itself (integrity)
    r = _run(inst, ["--home", str(home), "--verify", "--json"], home=home, cwd=tmp_path)
    assert r.returncode == 0, r.stdout + r.stderr
    rep = json.loads(r.stdout)
    assert rep["ok"] is True and rep["layout"] == "installed"
    assert rep["hooks_registered"] is True and rep["agent_hook_on_failure_block"] is True
    # --source-root: compare the installed copy with a trusted checkout
    r = _run(inst, ["--home", str(home), "--verify", "--source-root", str(ROOT)], home=home, cwd=tmp_path)
    assert r.returncode == 0, r.stdout + r.stderr
    # tamper -> 1, both ways
    hook = _installed_skill(home) / "hook_enforce_route.py"
    hook.write_text(hook.read_text(encoding="utf-8") + "\n# tampered\n", encoding="utf-8")
    assert _run(inst, ["--home", str(home), "--verify"], home=home, cwd=tmp_path).returncode == 1
    assert _run(inst, ["--home", str(home), "--verify", "--source-root", str(ROOT)], home=home,
                cwd=tmp_path).returncode == 1


def test_p6_verify_reports_missing_hook_registration_and_onfailure(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    _install(home, "--no-hook")
    inst = _installed_skill(home) / "install.py"
    r = _run(inst, ["--home", str(home), "--verify", "--json"], home=home, cwd=tmp_path)
    rep = json.loads(r.stdout)
    assert r.returncode == 0 and rep["hooks_registered"] is False and rep["agent_hook_on_failure_block"] is False
    assert _run(inst, ["--home", str(home), "--verify", "--require-hooks"], home=home, cwd=tmp_path).returncode == 1
    # registered, but the Agent hook lost its onFailure=block -> reported and refused with --require-hooks
    _install(home)
    settings = home / ".claude" / "settings.json"
    data = json.loads(settings.read_text(encoding="utf-8"))
    for entry in data["hooks"]["PreToolUse"]:
        for h in entry["hooks"]:
            h.pop("onFailure", None)
    settings.write_text(json.dumps(data), encoding="utf-8")
    r = _run(inst, ["--home", str(home), "--verify", "--json", "--require-hooks"], home=home, cwd=tmp_path)
    rep = json.loads(r.stdout)
    assert rep["hooks_registered"] is True and rep["agent_hook_on_failure_block"] is False and r.returncode == 1
