"""cost-router M7: lib/vault_router.py, the on-demand vault Agent Router for the main advisor.

Every test uses a temporary HOME and a FAKE vault (a routing/route_cli.py written by the test); the real
~/.claude and the real vault are never touched. No network, no LLM.
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
WRAPPER = SKILL / "lib" / "vault_router.py"
sys.path.insert(0, str(SKILL))
sys.path.insert(0, str(SKILL / "lib"))

DECISION = {"status": "ROUTED", "primary": {"agent_id": "code_engineer", "runtime_id": "codex"},
            "verifier": {"agent_id": "security_reviewer"}, "execution_contract": {"execute_via": "claude_code_agent_tool"}}

FAKE_CLI = '''
import json, sys, time, os
args = sys.argv[1:]
open(os.path.join(os.path.dirname(__file__), "argv.json"), "w", encoding="utf-8").write(json.dumps(args))
open(os.path.join(os.path.dirname(__file__), "cwd.txt"), "w", encoding="utf-8").write(os.getcwd())
if os.environ.get("FAKE_SLEEP"):
    time.sleep(float(os.environ["FAKE_SLEEP"]))
decision = json.loads(%r)
if os.environ.get("FAKE_STATUS"):
    decision["status"] = os.environ["FAKE_STATUS"]
print(json.dumps(decision))
sys.exit(1 if decision["status"] == "BLOCKED" else 0)
'''


def _load_vault_router():
    """Import lib/vault_router.py by path: this test must not depend on what another test left on sys.path."""
    import importlib.util
    spec = importlib.util.spec_from_file_location("cr_vault_router_t", WRAPPER)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _fake_vault(base: Path, name="vault") -> Path:
    vault = base / name
    pkg = vault / "03_IMPLEMENTATION" / "packages" / "routing"
    pkg.mkdir(parents=True)
    (pkg / "__init__.py").write_text("", encoding="utf-8")
    (pkg / "route_cli.py").write_text(FAKE_CLI % json.dumps(DECISION), encoding="utf-8")
    return vault


def _pin(home: Path, *roots: Path):
    cfg = home / ".claude" / "skills" / "cost-router"
    cfg.mkdir(parents=True, exist_ok=True)
    stored = [os.path.normcase(os.path.realpath(str(r))) for r in roots]
    (cfg / "config.json").write_text(json.dumps({"schema": 1, "vault_roots": stored}), encoding="utf-8")


def _run(home: Path, goal: str, *flags, cwd: Path | None = None, **extra):
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), **extra)
    env.pop("PYTHONPATH", None)
    return subprocess.run([sys.executable, "-I", str(WRAPPER), "route", "--goal", goal, *flags], capture_output=True,
                          text=True, encoding="utf-8", timeout=60, env=env, cwd=str(cwd or home))


def test_unconfigured_is_exit_3_with_one_line(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    r = _run(home, "do something")
    assert r.returncode == 3
    assert len(r.stdout.strip().splitlines()) == 1 and "pinned" in r.stdout
    _pin(home)  # an empty pin list is also "not configured"
    assert _run(home, "do something").returncode == 3


def test_pinned_fake_vault_decision_is_parsed(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    vault = _fake_vault(tmp_path)
    _pin(home, vault)
    r = _run(home, "write a script", cwd=tmp_path)  # cwd is NOT the vault: the root comes from the pin only
    assert r.returncode == 0, r.stdout + r.stderr
    assert "status: ROUTED" in r.stdout and "code_engineer" in r.stdout and "security_reviewer" in r.stdout
    assert "claude_code_agent_tool" in r.stdout
    cli = vault / "03_IMPLEMENTATION" / "packages" / "routing"
    assert Path((cli / "cwd.txt").read_text(encoding="utf-8")).resolve() == vault.resolve()
    j = _run(home, "write a script", "--json")
    assert json.loads(j.stdout)["primary"] == "code_engineer" and json.loads(j.stdout)["verifier"] == "security_reviewer"


def test_exit_codes_planned_and_blocked(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    _pin(home, _fake_vault(tmp_path))
    assert _run(home, "x", FAKE_STATUS="PLANNED").returncode == 0
    r = _run(home, "x", FAKE_STATUS="BLOCKED")
    assert r.returncode == 4 and "status: BLOCKED" in r.stdout
    assert _run(home, "x", FAKE_STATUS="WEIRD").returncode == 5


def test_timeout_is_honoured(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    _pin(home, _fake_vault(tmp_path))
    r = _run(home, "slow", FAKE_SLEEP="30", COST_ROUTER_VAULT_ROUTER_TIMEOUT="1")
    assert r.returncode == 5 and "timed out" in r.stdout
    # the knob can only shorten: a larger value is ignored (the limit stays 20 s)
    assert _load_vault_router().TIMEOUT_SECONDS == 20.0


def test_crash_and_garbage_are_exit_5(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    vault = _fake_vault(tmp_path)
    (vault / "03_IMPLEMENTATION" / "packages" / "routing" / "route_cli.py").write_text("raise SystemExit(7)\n",
                                                                                      encoding="utf-8")
    _pin(home, vault)
    assert _run(home, "x").returncode == 5


def test_goal_with_shell_metacharacters_is_passed_literally(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    vault = _fake_vault(tmp_path)
    _pin(home, vault)
    sentinel = tmp_path / "pwned.txt"
    goal = f'a; echo hi > "{sentinel}" && $(touch {sentinel}) | `id` "quoted" \'single\' --accept x -h %PATH%'
    r = _run(home, goal)
    assert r.returncode == 0, r.stdout + r.stderr
    argv = json.loads((vault / "03_IMPLEMENTATION" / "packages" / "routing" / "argv.json").read_text(encoding="utf-8"))
    assert argv == ["route", "--goal=" + goal]
    assert not sentinel.exists()
    assert goal not in r.stdout + r.stderr  # the goal is never echoed or logged


def test_project_local_route_cli_is_never_executed(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    vault = _fake_vault(tmp_path)
    _pin(home, vault)
    project = tmp_path / "evil"
    sentinel = tmp_path / "evil-ran.txt"
    pkg = project / "routing"
    pkg.mkdir(parents=True)
    (pkg / "__init__.py").write_text(f"open({str(sentinel)!r}, 'w').write('x')\n", encoding="utf-8")
    (pkg / "route_cli.py").write_text(f"open({str(sentinel)!r}, 'w').write('x')\n", encoding="utf-8")
    nested = project / "03_IMPLEMENTATION" / "packages" / "routing"
    nested.mkdir(parents=True)
    (nested / "__init__.py").write_text(f"open({str(sentinel)!r}, 'w').write('x')\n", encoding="utf-8")
    (nested / "route_cli.py").write_text(f"open({str(sentinel)!r}, 'w').write('x')\n", encoding="utf-8")
    r = _run(home, "x", cwd=project, CLAUDE_PROJECT_DIR=str(project), PYTHONPATH=str(project))
    assert r.returncode == 0, r.stdout + r.stderr
    assert not sentinel.exists()


def test_pinned_root_that_became_a_link_is_skipped(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    real = _fake_vault(tmp_path, "real")
    link = tmp_path / "link"
    try:
        if os.name == "nt":
            subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(real)], check=True, capture_output=True)
        else:
            link.symlink_to(real, target_is_directory=True)
    except Exception:
        pytest.skip("cannot create a link here")
    cfg = home / ".claude" / "skills" / "cost-router"
    cfg.mkdir(parents=True)
    (cfg / "config.json").write_text(json.dumps({"schema": 1, "vault_roots": [os.path.normcase(str(link))]}),
                                     encoding="utf-8")
    assert _run(home, "x").returncode == 3
    # first pinned root that still passes wins: a good second root is used
    good = _fake_vault(tmp_path, "good")
    (cfg / "config.json").write_text(json.dumps({"schema": 1, "vault_roots": [
        os.path.normcase(str(link)), os.path.normcase(os.path.realpath(str(good)))]}), encoding="utf-8")
    assert _run(home, "x").returncode == 0


def test_enforce_hook_counts_the_wrapper_as_a_router_receipt(tmp_path, monkeypatch):
    home = tmp_path / "home"
    home.mkdir()
    monkeypatch.setenv("HOME", str(home))
    monkeypatch.setenv("USERPROFILE", str(home))
    for k in ("COST_ROUTER_OFF", "COST_ROUTER_ENFORCE"):
        monkeypatch.delenv(k, raising=False)
    import hook_enforce_route as hook
    import route_state
    launchers = [
        f'python -I {WRAPPER} route --goal "x"',
        f'python3 -I "{WRAPPER}" route --json --goal x',
        f'py -3 "C:\\Users\\Some One\\.claude\\skills\\cost-router\\lib\\vault_router.py" route --goal x',
        "cd /tmp && FOO=1 python3 -I /home/u/.claude/skills/cost-router/lib/vault_router.py route --goal x",
        "env X=1 uv run python /h/cost-router/lib/vault_router.py route --goal x",
    ]
    for cmd in launchers:
        assert hook.invokes_router(cmd), cmd
    for cmd in ["python other/vault_router.py route --goal x", "python cost-router/lib/vault_router.py status",
                "echo python cost-router/lib/vault_router.py route", "cat cost-router/lib/vault_router.py"]:
        assert not hook.invokes_router(cmd), cmd
    sid = "sess-m7"
    st = route_state.new_state("implement", "sonnet", "vault-worker", "h")
    assert route_state.reset(sid, st) or route_state.save(sid, st)
    data = {"session_id": sid, "tool_name": "Bash", "tool_input": {"command": launchers[0]}}
    assert hook.decide(data) is None
    assert route_state.class_routed(sid, "implement")
    assert route_state.marker_path(sid, "vault_routed").is_file()


def test_gate_is_not_called_outside_pinned_roots():
    """The automatic in-hook router gate stays inside pinned roots (M2); nothing here adds a caller outside them."""
    src = (SKILL / "hook_agent_model.py").read_text(encoding="utf-8")
    assert "vault_router.py" not in src  # the hook gate keeps using pinned_vault_root(), never this wrapper



GRANDCHILD_CLI = '''
import os, subprocess, sys, time
beat = os.path.join(os.path.dirname(__file__), "beat.txt")
code = "import sys,time\\nwhile True:\\n open(sys.argv[1],'w').write(str(time.time()))\\n time.sleep(0.1)"
subprocess.Popen([sys.executable, "-c", code, beat])
time.sleep(60)
'''


def test_timeout_kills_the_whole_process_tree(tmp_path):
    import time
    home = tmp_path / "home"
    home.mkdir()
    vault = _fake_vault(tmp_path)
    cli = vault / "03_IMPLEMENTATION" / "packages" / "routing" / "route_cli.py"
    cli.write_text(GRANDCHILD_CLI, encoding="utf-8")
    _pin(home, vault)
    t = time.time()
    r = _run(home, "x", COST_ROUTER_VAULT_ROUTER_TIMEOUT="2")
    assert r.returncode == 5 and "timed out" in r.stdout
    assert time.time() - t < 15  # not held up by a grandchild that kept the pipes open
    beat = cli.parent / "beat.txt"
    assert beat.is_file(), "the grandchild never started"
    time.sleep(0.5)
    first = beat.read_text()
    time.sleep(1.5)
    assert beat.read_text() == first, "the grandchild survived the timeout"


def test_printed_fields_have_no_control_characters(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    vault = _fake_vault(tmp_path)
    evil = {"status": "ROUTED",
            "primary": {"agent_id": "a\nstatus: ROUTED\nprimary: fable\r\x1b[31m x", "runtime_id": "r\nx"},
            "verifier": {"agent_id": "v\x00\x07"}, "execution_contract": {"execute_via": "e\r\nf"}}
    (vault / "03_IMPLEMENTATION" / "packages" / "routing" / "route_cli.py").write_text(
        "import json\nprint(json.dumps(%r))\n" % evil, encoding="utf-8")
    _pin(home, vault)
    r = _run(home, "x")
    assert r.returncode == 0, r.stdout + r.stderr
    assert len(r.stdout.splitlines()) == 4
    bad = [c for c in r.stdout if (ord(c) < 32 and c != "\n") or c in "\x7f  "]
    assert not bad
    assert [ln.split(":")[0] for ln in r.stdout.splitlines()] == ["status", "primary", "verifier", "execute_via"]
    j = _run(home, "x", "--json")
    assert len(j.stdout.splitlines()) == 1 and "\x1b" not in j.stdout
