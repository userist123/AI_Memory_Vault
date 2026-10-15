"""cost-router binding route: per-session state, main-session enforcement and the Vault-router gate.

Every hook runs as a subprocess with HOME pointed at a tmp directory, so the real ~/.claude is
never read or written.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
PROMPT_HOOK = SKILL / "hook_prompt_route.py"
ENFORCE_HOOK = SKILL / "hook_enforce_route.py"
AGENT_HOOK = SKILL / "hook_agent_model.py"
SID = "test-session-1"
IMPLEMENT = "implement the bounded change in the parser module and add tests"
EXPLORE = "find which files import the router module"


@pytest.fixture
def home(tmp_path, monkeypatch):
    h = tmp_path / "home"
    h.mkdir()
    monkeypatch.setenv("HOME", str(h))
    monkeypatch.setenv("USERPROFILE", str(h))
    return h


def _run(script, payload, home: Path, project: Path = ROOT, **env_extra):
    env = dict(os.environ)
    env.update({"HOME": str(home), "USERPROFILE": str(home), "CLAUDE_PROJECT_DIR": str(project)})
    env.pop("COST_ROUTER_ENFORCE", None)
    env.pop("COST_ROUTER_OLLAMA_ENDPOINT", None)
    env.update(env_extra)
    stdin = payload if isinstance(payload, str) else json.dumps(payload)
    r = subprocess.run([sys.executable, str(script)], input=stdin, capture_output=True, text=True,
                       timeout=60, env=env)
    assert r.returncode == 0, r.stderr
    return r


def _state(home: Path, sid: str = SID):
    path = home / ".claude" / "cache" / "cost-router" / f"route-{sid}.json"
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else None


def _prompt(home, prompt, sid=SID):
    return _run(PROMPT_HOOK, {"session_id": sid, "prompt": prompt}, home)


def _tool(home, tool="Read", tool_input=None, sid=SID, **env):
    r = _run(ENFORCE_HOOK, {"session_id": sid, "tool_name": tool, "tool_input": tool_input or {"file_path": "/x"}},
             home, **env)
    return json.loads(r.stdout)["hookSpecificOutput"] if r.stdout.strip() else None


def _agent(home, prompt=IMPLEMENT, project: Path = ROOT, sid=SID):
    r = _run(AGENT_HOOK, {"session_id": sid, "tool_name": "Agent",
                          "tool_input": {"description": "worker", "prompt": prompt}}, home, project)
    return json.loads(r.stdout)["hookSpecificOutput"] if r.stdout.strip() else None


def test_prompt_hook_writes_the_route_state(home):
    _prompt(home, IMPLEMENT)
    s = _state(home)
    assert s["task_class"] == "implement" and s["model"] == "sonnet" and s["subagent"] == "vault-worker"
    assert s["delegated"] is False and s["main_calls"] == 0 and s["vault_routed"] is False
    assert len(s["prompt_hash"]) == 16 and IMPLEMENT not in json.dumps(s)


def test_trivial_prompt_resets_state_to_a_noop_route(home):
    _prompt(home, IMPLEMENT)
    _prompt(home, "ok")
    s = _state(home)
    assert s["subagent"] is None and s["main_calls"] == 0
    assert all(_tool(home) is None for _ in range(6))


def test_prompt_without_session_id_writes_nothing(home):
    _run(PROMPT_HOOK, {"prompt": IMPLEMENT}, home)
    assert not (home / ".claude" / "cache" / "cost-router").exists()


def test_enforce_allows_three_calls_then_denies(home):
    _prompt(home, IMPLEMENT)
    assert [_tool(home) for _ in range(3)] == [None, None, None]
    out = _tool(home, "Edit", {"file_path": "/x", "old_string": "a", "new_string": "b"})
    assert out["hookEventName"] == "PreToolUse" and out["permissionDecision"] == "deny"
    reason = out["permissionDecisionReason"]
    assert "route is implement -> delegate to vault-worker (sonnet)" in reason and "#direct" in reason
    assert _state(home)["main_calls"] == 4


def test_delegation_unlocks_the_main_session(home):
    _prompt(home, EXPLORE)
    for _ in range(3):
        _tool(home)
    assert _tool(home)["permissionDecision"] == "deny"
    _agent(home, EXPLORE)  # explore class: not Vault-gated, so the spawn is allowed and delegates
    assert _state(home)["delegated"] is True
    assert all(_tool(home) is None for _ in range(5))


def test_direct_override_stores_no_subagent(home):
    _prompt(home, IMPLEMENT + " #direct")
    assert _state(home)["subagent"] is None
    assert all(_tool(home) is None for _ in range(6))


def test_env_switch_disables_enforcement(home):
    _prompt(home, IMPLEMENT)
    assert all(_tool(home, COST_ROUTER_ENFORCE="0") is None for _ in range(6))


def test_route_cli_bash_marks_vault_routed_and_is_always_allowed(home):
    _prompt(home, IMPLEMENT)
    for _ in range(4):
        _tool(home)
    cmd = {"command": 'PYTHONPATH=03_IMPLEMENTATION/packages python -m routing.route_cli route --goal "x"'}
    assert _tool(home, "Bash", cmd) is None
    assert _state(home)["vault_routed"] is True
    assert _tool(home, "Bash", {"command": "python -m routing.route_cli dispatch --goal x --execute"}) is None


def test_agent_hook_requires_the_vault_router_for_implement_in_the_vault(home):
    _prompt(home, IMPLEMENT)
    out = _agent(home)
    assert out["permissionDecision"] == "deny"
    assert "python -m routing.route_cli route --goal" in out["permissionDecisionReason"]
    assert _state(home)["delegated"] is False
    _tool(home, "Bash", {"command": "python -m routing.route_cli route --goal x"})
    out = _agent(home)
    assert out is None or out["permissionDecision"] == "allow"
    assert _state(home)["delegated"] is True


def test_agent_hook_never_requires_the_vault_router_outside_the_vault(home, tmp_path):
    _prompt(home, IMPLEMENT)
    out = _agent(home, project=tmp_path)
    assert out is None or out["permissionDecision"] == "allow"
    assert _state(home)["delegated"] is True


def test_no_state_allows(home):
    assert _tool(home, sid="never-seen") is None
    assert all(_tool(home, sid="") is None for _ in range(5))
    out = _agent(home, sid="never-seen")
    assert out is None or out["permissionDecision"] == "allow"


@pytest.mark.parametrize("script", [ENFORCE_HOOK, AGENT_HOOK, PROMPT_HOOK])
@pytest.mark.parametrize("stdin", ["", "not json", "[1, 2]", '{"session_id": 5}'])
def test_malformed_stdin_exits_zero_silently(home, script, stdin):
    r = _run(script, stdin, home)
    assert r.stdout == ""


def test_corrupt_state_file_allows(home):
    path = home / ".claude" / "cache" / "cost-router" / f"route-{SID}.json"
    path.parent.mkdir(parents=True)
    path.write_text("{broken", encoding="utf-8")
    assert all(_tool(home) is None for _ in range(5))


def test_session_id_cannot_escape_the_cache_dir(home):
    _prompt(home, IMPLEMENT, sid="../../evil")
    assert not list(home.rglob("*evil*"))
