"""Token-waste fixes measured on the 2026-10-14..16 sessions (cost-router + Vault router).

A1 resume guard, A3 implementer on Sonnet / reviewer on Opus, A5 Claude Code subagents as router
runtimes + cached route receipt + vault-reviewer verifier fallback, A6 single-command exemptions,
A8 main-transcript size reminder. Every hook runs as a subprocess with HOME in a tmp directory.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "03_IMPLEMENTATION" / "packages"))

from routing.agent_router import AgentRouter  # noqa: E402
from routing.claude_model_router import ClaudeModelPolicy, route  # noqa: E402
from routing.models import RiskLevel, TaskRequest  # noqa: E402
from routing.registry import RouteRegistry  # noqa: E402

SKILL = ROOT / ".claude" / "skills" / "cost-router"
PROMPT_HOOK = SKILL / "hook_prompt_route.py"
ENFORCE_HOOK = SKILL / "hook_enforce_route.py"
AGENT_HOOK = SKILL / "hook_agent_model.py"
RESUME_HOOK = SKILL / "hook_resume_guard.py"
POLICY = ClaudeModelPolicy.from_file(ROOT / "04_CONFIG" / "claude_model_routing.json")
REG = RouteRegistry.from_file(ROOT / "04_CONFIG" / "agent_router.json")
SID = "waste-session-1"
SECURITY_FIX = "fix the auth token validation bug in the signature check and add tests"


def _run(script, payload, home: Path, **env_extra):
    env = dict(os.environ)
    env.update({"HOME": str(home), "USERPROFILE": str(home), "CLAUDE_PROJECT_DIR": str(ROOT)})
    for k in ("COST_ROUTER_ENFORCE", "COST_ROUTER_OLLAMA_ENDPOINT", "COST_ROUTER_RESUME_MAX_TOKENS",
              "COST_ROUTER_MAIN_MAX_TOKENS"):
        env.pop(k, None)
    env["COST_ROUTER_OLLAMA_ENDPOINT"] = "http://127.0.0.1:9"  # never a real local model
    env.update(env_extra)
    r = subprocess.run([sys.executable, str(script)], input=json.dumps(payload), capture_output=True,
                       text=True, timeout=60, env=env)
    assert r.returncode == 0, r.stderr
    return json.loads(r.stdout)["hookSpecificOutput"] if r.stdout.strip() else None


@pytest.fixture
def home(tmp_path):
    h = tmp_path / "home"
    h.mkdir()
    return h


def _prompt(home, prompt, sid=SID, **extra):
    return _run(PROMPT_HOOK, {"session_id": sid, "prompt": prompt, **extra}, home)


def _tool(home, tool, tool_input, sid=SID):
    return _run(ENFORCE_HOOK, {"session_id": sid, "tool_name": tool, "tool_input": tool_input}, home)


def _agent(home, prompt, subagent_type="general-purpose", sid=SID, **ti):
    return _run(AGENT_HOOK, {"session_id": sid, "tool_name": "Agent", "cwd": str(ROOT),
                             "tool_input": {"description": "task", "prompt": prompt,
                                            "subagent_type": subagent_type, **ti}}, home)


# -- A3: implementer on Sonnet, the risk floor on the reviewer -----------------------------------

def test_security_implementation_routes_to_sonnet_with_mandatory_opus_reviewer():
    d = route(POLICY, SECURITY_FIX)
    assert (d.task_class, d.model, d.subagent, d.verifier_model) == ("implement", "sonnet", "vault-worker", "opus")
    assert d.effort == "high"
    assert any(r.startswith("risk_floor_deferred_to_verifier:high->opus") for r in d.applied_rules)
    worker = route(POLICY, SECURITY_FIX + "; the reviewer will verify and audit it", for_subagent=True,
                   role="implementer")
    assert worker.model == "sonnet" and worker.verifier_model == "opus"
    reviewer = route(POLICY, "security review of the auth diff", for_subagent=True, role="reviewer")
    assert reviewer.model == "opus"


@pytest.mark.parametrize("goal", [
    "implement the deploy of the new auth token to production",   # live operation
    "fix the script that deletes the leaked secrets",              # delete / leak
    "rewrite history to remove the token from git history",        # critical
])
def test_live_operations_and_critical_risk_keep_the_opus_floor(goal):
    d = route(POLICY, goal, for_subagent=True, role="implementer")
    assert d.model == "opus", (goal, d.applied_rules)


def test_design_in_the_main_session_keeps_the_floor():
    d = route(POLICY, "diagnose the root cause of the auth bypass")
    assert d.task_class == "design" and d.model == "opus" and d.subagent is None


def test_agent_hook_worker_stays_on_sonnet_reviewer_goes_to_opus(home):
    out = _agent(home, SECURITY_FIX + "; the Opus reviewer will verify it", subagent_type="vault-worker",
                 model="opus")  # an explicit one-notch escalation is still honoured
    assert out is None or out["updatedInput"]["model"] == "opus"
    out = _agent(home, SECURITY_FIX + "; the Opus reviewer will verify it", subagent_type="vault-worker")
    assert out["updatedInput"]["model"] == "sonnet"
    assert "reviewer, not this worker" in out["additionalContext"]
    out = _agent(home, SECURITY_FIX, subagent_type="vault-reviewer", model="sonnet")
    assert out["updatedInput"]["model"] == "opus"


# -- A5: Vault router knows the Claude Code subagents ----------------------------------------------

AVAILABLE = {"claude_code": True, "codex": False, "antigravity": False, "local_llm": True, "perplexity": False,
             "claude_subagent_worker": True, "claude_subagent_reviewer": True, "claude_subagent_explore": True}


def test_router_routes_code_to_vault_worker_inside_claude_code():
    d = AgentRouter(REG).route(TaskRequest(goal="implement the multi-file C# fix and add tests"), AVAILABLE)
    assert d.status.value == "ROUTED"
    assert d.primary.runtime_id == "claude_subagent_worker"
    assert d.execution_contract == {"execute_via": "claude_code_agent_tool", "prompt_profile": "code-engineer",
                                    "subagent_type": "vault-worker"}


def test_security_review_falls_back_to_vault_reviewer_with_a_logged_receipt():
    d = AgentRouter(REG).route(TaskRequest(goal="security review of the signature verification diff"), AVAILABLE)
    assert d.status.value == "ROUTED"
    assert d.primary.runtime_id == "claude_subagent_reviewer"
    assert d.verifier is not None and d.verifier.runtime_id == "claude_subagent_reviewer"
    assert d.verifier.reason.startswith("fallback")
    assert "verifier:no_independent_route->fallback:claude_subagent_reviewer" in d.rejected
    assert any(w.startswith("verifier fallback") for w in d.warnings)
    outside = {k: (v and not k.startswith("claude_subagent")) for k, v in AVAILABLE.items()}
    blocked = AgentRouter(REG).route(TaskRequest(goal="security review of the signature verification diff"), outside)
    assert blocked.status.value == "BLOCKED", "outside Claude Code there is no fallback: fail closed"


def test_a_verifier_must_be_able_to_use_tools():
    d = AgentRouter(REG).route(TaskRequest(goal="security review of the token handling", risk=RiskLevel.HIGH),
                               {**AVAILABLE, "codex": True})
    assert d.verifier is not None and d.verifier.runtime_id != "local_llm"
    assert REG.runtimes[d.verifier.runtime_id].tool_use


def test_route_cli_sees_subagents_only_inside_claude_code(monkeypatch):
    from routing import route_cli
    rt = REG.runtimes["claude_subagent_worker"]
    monkeypatch.setenv("CLAUDECODE", "1")
    assert route_cli._runtime_available(rt)
    monkeypatch.delenv("CLAUDECODE")
    assert not route_cli._runtime_available(rt)


def test_route_cli_dispatch_refuses_a_subagent_route():
    env = {**os.environ, "CLAUDECODE": "1", "PYTHONPATH": str(ROOT / "03_IMPLEMENTATION" / "packages")}
    r = subprocess.run([sys.executable, "-m", "routing.route_cli", "dispatch", "--execute", "--goal",
                        "implement the parser fix and add tests"], capture_output=True, text=True, env=env,
                       timeout=60, cwd=str(ROOT))
    assert r.returncode != 0 and "Agent tool" in r.stderr


def test_cached_route_receipt_is_accepted_for_the_same_task_class(home):
    _prompt(home, "implement the bounded parser change and add tests")
    deny = _agent(home, "implement the bounded parser change and add tests")
    assert deny and deny["permissionDecision"] == "deny"
    _tool(home, "Bash", {"command": "python -m routing.route_cli route --goal \"parser change\""})
    assert _agent(home, "implement the bounded parser change and add tests")["permissionDecision"] == "allow"
    # a new prompt of the same class reuses the receipt; another class still has to route
    _prompt(home, "implement the second parser change and add more tests")
    assert _agent(home, "implement the second parser change and add tests")["permissionDecision"] == "allow"
    _prompt(home, "review the parser diff for regressions and verify the tests")
    deny = _agent(home, "review the parser diff for regressions", subagent_type="vault-reviewer")
    assert deny and deny["permissionDecision"] == "deny"


# -- A6: single-command exemptions ----------------------------------------------------------------

@pytest.mark.parametrize("tool,ti", [
    ("Bash", {"command": "gh pr checks 301"}),
    ("Bash", {"command": "gh pr merge 301 --squash --delete-branch"}),
    ("Bash", {"command": "gh pr view 301 --json state"}),
    ("Bash", {"command": "git -C /repo status --short"}),
    ("Bash", {"command": "git log --oneline -5"}),
    ("Edit", {"file_path": "/home/u/.claude/projects/p/memory/MEMORY.md", "old_string": "a", "new_string": "a\n- [x](x.md) - y"}),
    ("Write", {"file_path": "/r/00_GOVERNANCE/coordination/tasks/todo-claude-x.md", "content": "task\nnext\n"}),
])
def test_single_commands_never_count_toward_delegation(home, tool, ti):
    _prompt(home, "implement the bounded parser change and add tests")
    for _ in range(6):
        assert _tool(home, tool, ti) is None


@pytest.mark.parametrize("ti", [
    {"command": "git status; rm -rf build"},
    {"command": "gh pr checks 301 | tail -5"},
    {"command": "gh pr list > out.txt"},
    {"command": "dotnet test"},
])
def test_chains_and_real_work_still_count(home, ti):
    _prompt(home, "implement the bounded parser change and add tests")
    outs = [_tool(home, "Bash", ti) for _ in range(4)]
    assert outs[:3] == [None, None, None] and outs[3]["permissionDecision"] == "deny"


# -- A1: a subagent is resumed at most once -------------------------------------------------------

def _transcripts(tmp_path, tokens: int, agent="a1b2c3", name=None):
    main = tmp_path / "proj" / f"{SID}.jsonl"
    sub = main.with_suffix("") / "subagents"
    sub.mkdir(parents=True)
    main.write_text("", encoding="utf-8")
    usage = {"input_tokens": 2, "cache_read_input_tokens": tokens - 1002, "cache_creation_input_tokens": 1000}
    (sub / f"agent-{agent}.jsonl").write_text(
        '{"type":"user","message":{"content":"x"}}\n'
        + json.dumps({"type": "assistant", "message": {"usage": usage}}) + "\n", encoding="utf-8")
    (sub / f"agent-{agent}.meta.json").write_text(json.dumps({"agentType": "vault-worker", **({"name": name} if name else {})}),
                                                    encoding="utf-8")
    return main


def _send(home, main, to, **env):
    return _run(RESUME_HOOK, {"session_id": SID, "tool_name": "SendMessage", "transcript_path": str(main),
                              "tool_input": {"to": to, "message": "round 2"}}, home, **env)


def test_large_subagent_resume_warns_then_second_resume_is_denied(home, tmp_path):
    main = _transcripts(tmp_path, 640_000)
    first = _send(home, main, "a1b2c3")
    assert first and "permissionDecision" not in first and "~640k" in first["additionalContext"]
    second = _send(home, main, "agent-a1b2c3")
    assert second["permissionDecision"] == "deny" and "<=40-line checkpoint" in second["permissionDecisionReason"]


def test_small_subagent_first_resume_is_silent_and_names_resolve(home, tmp_path):
    main = _transcripts(tmp_path, 40_000, name="parser-worker")
    assert _send(home, main, "parser-worker") is None
    assert _send(home, main, "parser-worker")["permissionDecision"] == "deny"


def test_resume_guard_ignores_unknown_targets_and_the_override(home, tmp_path):
    main = _transcripts(tmp_path, 900_000)
    assert _send(home, main, "main") is None and _send(home, main, "some-teammate") is None
    for _ in range(3):
        assert _send(home, main, "a1b2c3", COST_ROUTER_ENFORCE="0") is None
    assert _send(home, main, "a1b2c3", COST_ROUTER_RESUME_MAX_TOKENS="1000000") is None  # first real resume


# -- A8: main transcript size reminder ------------------------------------------------------------

def test_prompt_hook_reminds_to_checkpoint_when_the_main_transcript_is_large(home, tmp_path):
    big = tmp_path / "main.jsonl"
    big.write_text(json.dumps({"type": "assistant", "message": {"usage": {
        "input_tokens": 1, "cache_read_input_tokens": 420_000, "cache_creation_input_tokens": 0}}}) + "\n",
        encoding="utf-8")
    for prompt in ("implement the bounded parser change and add tests", "continua"):
        out = _prompt(home, prompt, transcript_path=str(big))
        assert out and "/clear" in out["additionalContext"] and "todo-claude-" in out["additionalContext"]
    small = tmp_path / "small.jsonl"
    small.write_text(json.dumps({"type": "assistant", "message": {"usage": {"input_tokens": 50_000}}}) + "\n",
                     encoding="utf-8")
    assert _prompt(home, "continua", transcript_path=str(small)) is None
