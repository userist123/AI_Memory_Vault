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

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cost_router_isolation as iso  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
PROMPT_HOOK = SKILL / "hook_prompt_route.py"
ENFORCE_HOOK = SKILL / "hook_enforce_route.py"
AGENT_HOOK = SKILL / "hook_agent_model.py"
SID = "test-session-1"
IMPLEMENT = "implement the bounded change in the parser module and add tests"
EXPLORE = "find which files import the router module"


@pytest.fixture(autouse=True)
def _isolated_claude_lib():
    iso.enter(SKILL / "lib")
    yield
    iso.leave(SKILL / "lib")


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
    """The effective state: the JSON plus the monotonic marker files next to it."""
    path = home / ".claude" / "cache" / "cost-router" / f"route-{sid}.json"
    if not path.exists():
        return None
    s = json.loads(path.read_text(encoding="utf-8"))
    for name in ("delegated", "vault_routed"):
        s[name] = bool(s.get(name)) or path.with_suffix("." + name).exists()
    return s


def _prompt(home, prompt, sid=SID):
    return _run(PROMPT_HOOK, {"session_id": sid, "prompt": prompt}, home)


def _tool(home, tool="Edit", tool_input=None, sid=SID, extra=None, **env):
    # default tool is Edit: read-only tools (Read/Grep/Glob) are never denied by default (see the DENY_READS tests)
    payload = {"session_id": sid, "tool_name": tool,
               "tool_input": tool_input or {"file_path": "/x", "old_string": "a", "new_string": "b"}, **(extra or {})}
    r = _run(ENFORCE_HOOK, payload, home, **env)
    return json.loads(r.stdout)["hookSpecificOutput"] if r.stdout.strip() else None


def _agent(home, prompt=IMPLEMENT, project: Path = ROOT, sid=SID, tool_name="Agent", **tool_extra):
    r = _run(AGENT_HOOK, {"session_id": sid, "tool_name": tool_name,
                          "model": "opus",
                          "tool_input": {"description": "worker", "prompt": prompt, **tool_extra}}, home, project)
    return json.loads(r.stdout)["hookSpecificOutput"] if r.stdout.strip() else None


def test_prompt_hook_writes_the_route_state(home):
    _prompt(home, IMPLEMENT)
    s = _state(home)
    assert s["task_class"] == "implement" and s["model"] == "sonnet" and s["subagent"] == "vault-worker"
    assert s["delegated"] is False and s["main_calls"] == 0 and s["vault_routed"] is False
    assert len(s["prompt_hash"]) == 16 and IMPLEMENT not in json.dumps(s)


@pytest.mark.parametrize("short", ["ok", "continua", "da", "do it now", "/clear"])
def test_trivial_prompt_keeps_the_existing_route(home, short):
    _prompt(home, IMPLEMENT)
    before = _state(home)
    _prompt(home, short)
    assert _state(home) == before
    for _ in range(3):
        _tool(home)
    assert _tool(home)["permissionDecision"] == "deny"


def test_new_nontrivial_prompt_replaces_state_and_markers(home):
    _prompt(home, IMPLEMENT)
    _tool(home, "Bash", {"command": "python -m routing.route_cli route --goal x"})
    _agent(home)
    assert _state(home)["delegated"] is True and _state(home)["vault_routed"] is True
    _prompt(home, "implement another bounded change in the cache module and add tests")
    s = _state(home)
    assert s["delegated"] is False and s["vault_routed"] is False and s["main_calls"] == 0


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


def test_agent_hook_requires_the_vault_router_for_implement_in_the_vault(home, tmp_path):
    # a Vault whose router is unavailable (crashes): the in-hook router fails, so the gate denies with the
    # fallback text; a Bash receipt then lets the spawn through without running the router again
    proj = tmp_path / "proj"
    cli = proj / "03_IMPLEMENTATION" / "packages" / "routing" / "route_cli.py"
    cli.parent.mkdir(parents=True)
    # a regular package (like the real one): with `pip install -e .` (CI) the real `routing` is a regular package
    # and would win over a namespace-only fake, so the real router would run instead of this crashing one
    (cli.parent / "__init__.py").write_text("", encoding="utf-8")
    cli.write_text("import sys; sys.exit(1)", encoding="utf-8")
    _prompt(home, IMPLEMENT)
    out = _agent(home, project=proj)
    assert out["permissionDecision"] == "deny"
    assert "python -m routing.route_cli route --goal" in out["permissionDecisionReason"]
    assert "in-hook router unavailable: exit 1" in out["permissionDecisionReason"]
    assert _state(home)["delegated"] is False
    _tool(home, "Bash", {"command": "python -m routing.route_cli route --goal x"})
    out = _agent(home, project=proj)
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


# -- review fixes ------------------------------------------------------------------

@pytest.mark.parametrize("field", ["agent_id", "agent_type"])
def test_subagent_tool_calls_are_never_counted_or_blocked(home, field):
    _prompt(home, IMPLEMENT)
    for _ in range(3):
        _tool(home)
    before = _state(home)
    assert all(_tool(home, extra={field: "vault-worker-1"}) is None for _ in range(5))
    assert _state(home) == before
    assert _tool(home)["permissionDecision"] == "deny"  # the main session is still bound


def test_delegated_survives_a_concurrent_counter_write(home):
    """Lost update: a counter write based on a pre-spawn read must not clear `delegated`."""
    import route_state  # the autouse fixture put the .claude lib on sys.path
    _prompt(home, EXPLORE)
    stale = _state(home)               # the enforce hook read this before the spawn
    _agent(home, EXPLORE)              # the spawn marks delegated
    stale["main_calls"] = 1
    assert route_state.save(SID, stale)  # the enforce hook writes its stale copy back
    assert route_state.load(SID)["delegated"] is True
    assert all(_tool(home) is None for _ in range(5))


def test_vault_routed_survives_a_concurrent_counter_write(home):
    import route_state  # the autouse fixture put the .claude lib on sys.path
    _prompt(home, IMPLEMENT)
    stale = _state(home)
    _tool(home, "Bash", {"command": "python -m routing.route_cli route --goal x"})
    stale["main_calls"] = 2
    route_state.save(SID, stale)
    assert route_state.load(SID)["vault_routed"] is True
    out = _agent(home)
    assert out is None or out["permissionDecision"] == "allow"


def test_legacy_task_tool_name_is_handled(home):
    _prompt(home, EXPLORE)
    for _ in range(4):
        _tool(home)
    out = _agent(home, EXPLORE, tool_name="Task")
    assert out is not None and out["updatedInput"]["model"] == "haiku"
    assert _state(home)["delegated"] is True


def test_settings_and_installer_match_agent_and_task(tmp_path):
    s = json.loads((ROOT / ".claude" / "settings.json").read_text(encoding="utf-8"))
    assert s["hooks"]["PreToolUse"][0]["matcher"] == "Agent|Task"
    h = tmp_path / "h"
    r = subprocess.run([sys.executable, str(SKILL / "install.py"), "--home", str(h)], capture_output=True, text=True,
                       timeout=120)
    assert r.returncode == 0, r.stderr
    hooks = json.loads((h / ".claude" / "settings.json").read_text(encoding="utf-8"))["hooks"]["PreToolUse"]
    agent = [e for e in hooks if "hook_agent_model.py" in json.dumps(e)]
    assert len(agent) == 1 and agent[0]["matcher"] == "Agent|Task"


@pytest.mark.parametrize("subagent_type", ["vault-reviewer", "code-review", "verifier"])
def test_reviewer_subagents_are_never_below_opus(home, subagent_type):
    out = _agent(home, IMPLEMENT, project=home, subagent_type=subagent_type)
    assert out is not None and out["updatedInput"]["model"] == "opus"


def test_worker_with_the_same_brief_stays_cheap(home):
    out = _agent(home, IMPLEMENT, project=home, subagent_type="vault-worker")
    assert out is not None and out["updatedInput"]["model"] == "sonnet"


@pytest.mark.parametrize("command", [
    "echo routing.route_cli route",
    "grep -r 'python -m routing.route_cli route' .",
    "cat notes.txt # routing.route_cli dispatch",
])
def test_mentioning_the_router_does_not_mark_vault_routed(home, command):
    _prompt(home, IMPLEMENT)
    _tool(home, "Bash", {"command": command})
    assert _state(home)["vault_routed"] is False


@pytest.mark.parametrize("command", [
    "python -m routing.route_cli route --goal x",
    "PYTHONPATH=03_IMPLEMENTATION/packages python3 -m routing.route_cli dispatch --goal x --execute",
    "cd /repo && /usr/bin/python3 -m routing.route_cli route --goal x",
    "export PYTHONPATH=p; python -m routing.route_cli route --goal x",
])
def test_invoking_the_router_marks_vault_routed(home, command):
    _prompt(home, IMPLEMENT)
    assert _tool(home, "Bash", {"command": command}) is None
    assert _state(home)["vault_routed"] is True


def test_policy_doc_states_the_opus_ceiling():
    text = (ROOT / "00_GOVERNANCE" / "protocols" / "Claude_Model_Routing_Policy_V1.md").read_text(encoding="utf-8")
    assert "Fable is reachable only for the `frontier` class" in text


# -- re-review fixes ---------------------------------------------------------------

def _route_state():
    import route_state  # the autouse fixture put the .claude lib on sys.path
    return route_state


def test_short_direct_prompt_switches_enforcement_off(home):
    _prompt(home, IMPLEMENT)
    _prompt(home, "#direct do it")
    assert _state(home)["subagent"] is None
    assert all(_tool(home) is None for _ in range(6))


@pytest.mark.parametrize("command", [
    "py -3 -m routing.route_cli route --goal x",
    "py -m routing.route_cli dispatch --goal x",
    "uv run python -m routing.route_cli route --goal x",
    "env PYTHONPATH=03_IMPLEMENTATION/packages python -m routing.route_cli route --goal x",
    "python -I -m routing.route_cli route --goal x",
    "python3 -X utf8 -m routing.route_cli route --goal x",
])
def test_other_launchers_mark_vault_routed(home, command):
    _prompt(home, IMPLEMENT)
    assert _tool(home, "Bash", {"command": command}) is None
    assert _state(home)["vault_routed"] is True


def test_reset_overwrites_state_and_voids_markers_it_cannot_delete(home, monkeypatch):
    rs = _route_state()
    _prompt(home, EXPLORE)  # explore is not Vault-gated: the spawn marks delegated
    _agent(home, EXPLORE)
    assert rs.load(SID)["delegated"] is True
    real_unlink = Path.unlink

    def deny_markers(self, *a, **kw):
        if self.suffix in (".delegated", ".vault_routed"):
            raise PermissionError("locked")
        return real_unlink(self, *a, **kw)

    monkeypatch.setattr(Path, "unlink", deny_markers)
    new = rs.new_state("implement", "sonnet", "vault-worker", "feedbeef")
    assert rs.reset(SID, new) is True
    s = rs.load(SID)
    assert s["prompt_hash"] == "feedbeef" and s["delegated"] is False


def test_reset_prunes_cache_files_older_than_seven_days(home):
    cache = home / ".claude" / "cache" / "cost-router"
    cache.mkdir(parents=True)
    old = [cache / "route-old.json", cache / "route-old.delegated", cache / "route-old.vault_routed"]
    fresh = cache / "route-fresh.json"
    for p in (*old, fresh):
        p.write_text("{}", encoding="utf-8")
    stamp = __import__("time").time() - 8 * 86400
    for p in old:
        os.utime(p, (stamp, stamp))
    _prompt(home, IMPLEMENT)
    assert not any(p.exists() for p in old)
    assert fresh.exists() and _state(home) is not None
