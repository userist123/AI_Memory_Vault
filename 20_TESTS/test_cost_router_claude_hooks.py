"""cost-router native Claude Code hooks: PostToolUse telemetry, idempotency keys, fail-open behaviour."""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
sys.path.insert(0, str(Path(__file__).resolve().parent))
import cost_router_isolation as iso  # noqa: E402

transcript_usage = None  # bound per test by the autouse fixture below


@pytest.fixture(autouse=True)
def _isolated_claude_lib():
    """Import the .claude lib per test and drop it afterwards, so the Antigravity suite's bare names win in its turn."""
    global transcript_usage
    import importlib
    iso.enter(SKILL / "lib")
    transcript_usage = importlib.import_module("transcript_usage")
    yield
    transcript_usage = None
    iso.leave(SKILL / "lib")

USAGE_HOOK = SKILL / "hook_agent_usage.py"
ENFORCE = SKILL / "hook_enforce_route.py"
AGENT = SKILL / "hook_agent_model.py"
RESUME = SKILL / "hook_resume_guard.py"
REPO_MAP = SKILL / "hook_repo_map.py"
POLICY = json.loads((ROOT / "04_CONFIG" / "claude_model_routing.json").read_text(encoding="utf-8"))
SID = "sess-1"


def run(hook, payload, home: Path, args=(), env_extra=None):
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), APPDATA=str(home), **(env_extra or {}))
    env.pop("CLAUDE_PROJECT_DIR", None)
    env.pop("COST_ROUTER_ENFORCE", None)
    stdin = payload if isinstance(payload, str) else json.dumps(payload)
    return subprocess.run([sys.executable, str(hook), *args], input=stdin, capture_output=True, text=True,
                          timeout=60, env=env, encoding="utf-8")


def state_dir(home: Path) -> Path:
    return home / ".claude" / "cache" / "cost-router"


def usage_entries(home: Path, sid=SID):
    f = state_dir(home) / f"route-{sid}.agent_usage.jsonl"
    return [json.loads(ln) for ln in f.read_text(encoding="utf-8").splitlines() if ln] if f.exists() else []


def post_payload(tool_use_id="toolu_1", **over):
    resp = {"status": "completed", "agentId": "a1b2c3", "agentType": "Explore",
            "content": [{"type": "text", "text": "SECRET-CONTENT"}], "resolvedModel": "claude-haiku-5-5",
            "totalDurationMs": 1234, "totalTokens": 43825, "totalToolUseCount": 2,
            "usage": {"input_tokens": 2, "cache_creation_input_tokens": 43818, "cache_read_input_tokens": 100,
                      "output_tokens": 5, "output_tokens_details": {"thinking_tokens": 0}, "iterations": []}}
    data = {"session_id": SID, "hook_event_name": "PostToolUse", "tool_name": "Agent", "tool_use_id": tool_use_id,
            "tool_input": {"model": "opus", "prompt": "SECRET-PROMPT"}, "tool_response": resp}
    data.update(over)
    return data


def write_route(home: Path, **over):
    state = {"task_class": "implement", "model": "sonnet", "subagent": "vault-worker", "main_calls": 0,
             "delegated": False, "vault_routed": False, "direct": False, "epoch": "e1"}
    state.update(over)
    d = state_dir(home)
    d.mkdir(parents=True, exist_ok=True)
    (d / f"route-{SID}.json").write_text(json.dumps(state), encoding="utf-8")


def bash(tid, **extra):
    return {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": "Bash", "tool_use_id": tid,
            "tool_input": {"command": "ls -la src"}, **extra}


# a. telemetry -------------------------------------------------------------------------

def test_agent_usage_recorded_once_and_idempotent(tmp_path):
    for _ in range(2):
        r = run(USAGE_HOOK, post_payload(), tmp_path)
        assert r.returncode == 0 and r.stdout == ""
    entries = usage_entries(tmp_path)
    assert len(entries) == 1
    e = entries[0]
    assert e["resolved_model"] == "claude-haiku-5-5" and e["requested_model"] == "opus"
    assert (e["input_tokens"], e["cache_creation_input_tokens"], e["cache_read_input_tokens"],
            e["output_tokens"]) == (2, 43818, 100, 5)
    assert e["agent_id"] == "a1b2c3" and e["total_tokens"] == 43825 and e["duration_ms"] == 1234
    assert e["est_cost_usd"] > 0
    raw = (state_dir(tmp_path) / f"route-{SID}.agent_usage.jsonl").read_text(encoding="utf-8")
    assert "SECRET" not in raw


def test_distinct_tool_use_ids_are_both_recorded(tmp_path):
    run(USAGE_HOOK, post_payload("t1"), tmp_path)
    run(USAGE_HOOK, post_payload("t2"), tmp_path)
    assert len(usage_entries(tmp_path)) == 2


# b. fail open -------------------------------------------------------------------------

@pytest.mark.parametrize("payload", [
    post_payload(tool_name="Bash"),
    post_payload(tool_response="plain string"),
    "{not json",
    "",
    post_payload(hook_event_name="Weird", tool_name="Other"),
    post_payload(tool_use_id=""),
])
def test_usage_hook_ignores_bad_input(tmp_path, payload):
    r = run(USAGE_HOOK, payload, tmp_path)
    assert r.returncode == 0 and r.stdout == ""
    assert usage_entries(tmp_path) == []


# c. unwritable state --------------------------------------------------------------------

def test_unwritable_state_dir_is_fail_open(tmp_path):
    (tmp_path / ".claude").write_text("i am a file, not a directory", encoding="utf-8")
    r = run(USAGE_HOOK, post_payload(), tmp_path)
    assert r.returncode == 0 and r.stdout == ""
    r = run(ENFORCE, bash("t1"), tmp_path)
    assert r.returncode == 0 and r.stdout == ""


# d. enforce idempotency ---------------------------------------------------------------

def denied(r) -> bool:
    return bool(r.stdout.strip()) and json.loads(r.stdout)["hookSpecificOutput"].get("permissionDecision") == "deny"


def test_enforce_same_tool_use_id_counts_once(tmp_path):
    write_route(tmp_path)
    for _ in range(5):
        assert not denied(run(ENFORCE, bash("same"), tmp_path))
    state = json.loads((state_dir(tmp_path) / f"route-{SID}.json").read_text(encoding="utf-8"))
    assert state["main_calls"] == 1


def test_enforce_fourth_distinct_call_is_denied_and_replay_keeps_decision(tmp_path):
    write_route(tmp_path)
    results = [denied(run(ENFORCE, bash(f"t{i}"), tmp_path)) for i in range(4)]
    assert results == [False, False, False, True]
    assert denied(run(ENFORCE, bash("t3"), tmp_path))      # replay of the denied call: same decision
    assert not denied(run(ENFORCE, bash("t0"), tmp_path))  # replay of an allowed call: still allowed
    state = json.loads((state_dir(tmp_path) / f"route-{SID}.json").read_text(encoding="utf-8"))
    assert state["main_calls"] == 4


def test_enforce_never_denies_a_subagent(tmp_path):
    write_route(tmp_path, main_calls=10)
    for i in range(6):
        r = run(ENFORCE, bash(f"s{i}", agent_id="a1", agent_type="Explore"), tmp_path)
        assert r.returncode == 0 and not denied(r)


# e. agent model -------------------------------------------------------------------------

def agent_out(model, home):
    ti = {"description": "subagent task", "prompt": "find which files import memory_controller",
          "subagent_type": "general-purpose"}
    if model:
        ti["model"] = model
    r = run(AGENT, {"hook_event_name": "PreToolUse", "tool_name": "Agent", "tool_input": ti, "model": "opus"}, home)
    assert r.returncode == 0
    return json.loads(r.stdout)["hookSpecificOutput"] if r.stdout.strip() else None


def test_agent_model_is_lowered_to_a_policy_alias(tmp_path):
    out = agent_out("opus", tmp_path)
    assert out["updatedInput"]["model"] in POLICY["models"]
    assert out["updatedInput"]["model"] == "sonnet"  # explore route: one tier above haiku at most


def test_agent_model_unknown_value_takes_the_route_model(tmp_path):
    out = agent_out("banana-9", tmp_path)  # R2-2: never passed through unchecked
    assert out["updatedInput"]["model"] == "haiku"
    assert out["additionalContext"].endswith("unrecognised model 'banana-9'; capped to haiku")


# f. resume guard ------------------------------------------------------------------------

def resume_payload(tmp_path, tid):
    sub = tmp_path / "proj" / SID / "subagents"
    sub.mkdir(parents=True, exist_ok=True)
    (sub / "agent-abc.jsonl").write_text(json.dumps({"message": {"usage": {"input_tokens": 10}}}) + "\n",
                                         encoding="utf-8")
    return {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": "SendMessage", "tool_use_id": tid,
            "transcript_path": str(tmp_path / "proj" / f"{SID}.jsonl"), "tool_input": {"to": "abc", "message": "go"}}


def test_resume_guard_counts_one_resume_per_tool_use_id(tmp_path):
    p1 = resume_payload(tmp_path, "r1")
    for _ in range(2):
        r = run(RESUME, p1, tmp_path)
        assert r.returncode == 0 and not denied(r)
    counts = json.loads((state_dir(tmp_path) / f"route-{SID}.resumes").read_text(encoding="utf-8"))
    assert counts == {"abc": 1}
    assert denied(run(RESUME, resume_payload(tmp_path, "r2"), tmp_path))


# g. transcript usage --------------------------------------------------------------------

def test_usage_split_reads_the_last_assistant_message(tmp_path):
    t = tmp_path / "t.jsonl"
    rows = [{"message": {"usage": {"input_tokens": 1, "cache_read_input_tokens": 2,
                                   "cache_creation_input_tokens": 3, "output_tokens": 4}}},
            {"type": "user", "message": {"content": "x"}},
            {"message": {"usage": {"input_tokens": 10, "cache_read_input_tokens": 200,
                                   "cache_creation_input_tokens": 3000, "output_tokens": 40}}}]
    t.write_text("\n".join(json.dumps(r) for r in rows) + "\n", encoding="utf-8")
    s = transcript_usage.usage_split(t)
    assert s == {"input_tokens": 10, "cache_read_input_tokens": 200, "cache_creation_input_tokens": 3000,
                 "output_tokens": 40, "cache_creation_1h_input_tokens": 0, "cache_creation_5m_input_tokens": 3000,
                 "context_tokens": 3210}
    assert transcript_usage.context_tokens(t) == 3210
    assert transcript_usage.usage_split(tmp_path / "missing.jsonl") is None


def test_estimate_cost_matches_the_policy_card_and_never_invents_prices():
    spec = POLICY["models"]["opus"]
    p = spec["usd_per_mtok"]
    usage = {"input_tokens": 1000, "cache_read_input_tokens": 20000, "cache_creation_input_tokens": 5000,
             "output_tokens": 700}
    expected = (1000 * p["input"] + 20000 * p["cache_read"] + 5000 * p["cache_write_5m"] + 700 * p["output"]) / 1e6
    assert transcript_usage.estimate_cost_usd(spec["model_id"], usage, POLICY) == pytest.approx(expected)
    assert transcript_usage.estimate_cost_usd("opus", usage, POLICY) == pytest.approx(expected)
    assert transcript_usage.estimate_cost_usd("banana-9", usage, POLICY) is None
    assert transcript_usage.estimate_cost_usd(None, usage, POLICY) is None


def test_estimate_cost_prices_one_hour_cache_writes_at_the_1h_card():
    spec = POLICY["models"]["opus"]
    p = spec["usd_per_mtok"]
    usage = {"input_tokens": 2, "cache_read_input_tokens": 1000, "cache_creation_input_tokens": 9000,
             "cache_creation_1h_input_tokens": 6000, "cache_creation_5m_input_tokens": 3000, "output_tokens": 5}
    expected = (2 * p["input"] + 1000 * p["cache_read"] + 3000 * p["cache_write_5m"]
                + 6000 * p["cache_write_1h"] + 5 * p["output"]) / 1e6
    assert transcript_usage.estimate_cost_usd(spec["model_id"], usage, POLICY) == pytest.approx(expected)


def test_usage_split_and_entry_carry_the_cache_creation_breakdown(tmp_path):
    probe = ROOT / "07_EVALUATION" / "reports" / "cost_router_claude_native_probe_2026-10-21" / "posttooluse_agent.jsonl"
    rec = json.loads(probe.read_text(encoding="utf-8").splitlines()[0])
    assert rec["usage"]["cache_creation"]["ephemeral_5m_input_tokens"] == 43818  # probe payload shape
    usage = dict(rec["usage"], cache_creation={"ephemeral_1h_input_tokens": 40000, "ephemeral_5m_input_tokens": 3818})
    t = tmp_path / "t.jsonl"
    t.write_text(json.dumps({"message": {"usage": usage}}) + "\n", encoding="utf-8")
    s = transcript_usage.usage_split(t)
    assert (s["cache_creation_1h_input_tokens"], s["cache_creation_5m_input_tokens"]) == (40000, 3818)
    card = POLICY["models"]["opus"]["usd_per_mtok"]
    resp = {"resolvedModel": "claude-opus-5-5", "usage": usage}
    assert run(USAGE_HOOK, post_payload("tcc", tool_response=resp), tmp_path).returncode == 0
    e = usage_entries(tmp_path)[0]
    assert (e["cache_creation_1h_input_tokens"], e["cache_creation_5m_input_tokens"]) == (40000, 3818)
    expected = (2 * card["input"] + 40000 * card["cache_write_1h"] + 3818 * card["cache_write_5m"]
                + 5 * card["output"]) / 1e6
    assert e["est_cost_usd"] == pytest.approx(expected)


def test_estimate_cost_uses_the_longest_model_id_prefix_never_the_first_entry():
    pol = {"models": {
        "short": {"model_id": "claude-opus-5", "usd_per_mtok": {"input": 1, "output": 1}},
        "long": {"model_id": "claude-opus-5-5", "usd_per_mtok": {"input": 7, "output": 7}}}}
    u = {"input_tokens": 1_000_000}
    f = transcript_usage.estimate_cost_usd
    assert f("claude-opus-5-5", u, pol) == pytest.approx(7)           # exact
    assert f("claude-opus-5-5-20260101", u, pol) == pytest.approx(7)  # longest prefix, not the first entry
    assert f("claude-opus-5[1m]", u, pol) == pytest.approx(1)
    assert f("claude-opus-50", u, pol) is None                        # not followed by end, "-" or "["


def test_prompt_route_replay_of_the_same_prompt_id_is_a_noop(tmp_path):
    hook = SKILL / "hook_prompt_route.py"
    payload = {"session_id": SID, "hook_event_name": "UserPromptSubmit", "prompt_id": "p-1", "cwd": str(tmp_path),
               "prompt": "implement a new feature in the module with tests and refactor the whole parser"}
    first = run(hook, payload, tmp_path)
    assert first.returncode == 0 and first.stdout.strip()
    f = state_dir(tmp_path) / f"route-{SID}.json"
    epoch = json.loads(f.read_text(encoding="utf-8"))["epoch"]
    second = run(hook, payload, tmp_path)
    assert second.returncode == 0 and second.stdout == ""
    assert json.loads(f.read_text(encoding="utf-8"))["epoch"] == epoch


OWNER_PROMPT = "implement a new feature in the module with tests and refactor the whole parser"
SYNTHETIC_PROMPTS = [
    '<agent-message from="judge-1">{"verdict": "ok", "summary": "explore the repo and list every file please"}</agent-message>',
    '[Subagent hand-back]\n<agent-message from="worker">review the diff and report all findings now</agent-message>',
    "<task-notification>background task bq1 completed with exit code 0 and output</task-notification>",
    "[SYSTEM NOTIFICATION - NOT USER INPUT] background task finished, read its output file",
    "<side-sessions-event>session two finished its refactor and wrote the checkpoint</side-sessions-event>",
    "<ci-monitor-event>pull request checks are failing on the lint job right now</ci-monitor-event>",
]


def _prompt_payload(prompt, pid, tmp_path):
    return {"session_id": SID, "hook_event_name": "UserPromptSubmit", "prompt_id": pid, "cwd": str(tmp_path),
            "prompt": prompt}


@pytest.mark.parametrize("synthetic", SYNTHETIC_PROMPTS)
def test_prompt_route_synthetic_turn_keeps_the_prior_route(tmp_path, synthetic):
    hook = SKILL / "hook_prompt_route.py"
    assert run(hook, _prompt_payload(OWNER_PROMPT, "p-1", tmp_path), tmp_path).returncode == 0
    f = state_dir(tmp_path) / f"route-{SID}.json"
    before = json.loads(f.read_text(encoding="utf-8"))
    r = run(hook, _prompt_payload(synthetic, "p-2", tmp_path), tmp_path)
    assert r.returncode == 0
    assert "route unchanged" in r.stdout
    assert json.loads(f.read_text(encoding="utf-8")) == before


def test_prompt_route_owner_text_quoting_agent_message_mid_text_still_reclassifies(tmp_path):
    hook = SKILL / "hook_prompt_route.py"
    run(hook, _prompt_payload(OWNER_PROMPT, "p-1", tmp_path), tmp_path)
    f = state_dir(tmp_path) / f"route-{SID}.json"
    first = json.loads(f.read_text(encoding="utf-8"))
    quoted = ("please review the security of the auth module, the <agent-message from=\"x\"> envelope and the"
              " [Subagent hand-back] marker are only quoted here")
    r = run(hook, _prompt_payload(quoted, "p-2", tmp_path), tmp_path)
    assert r.returncode == 0 and "route unchanged" not in r.stdout
    assert json.loads(f.read_text(encoding="utf-8"))["epoch"] != first["epoch"]


def test_prompt_route_synthetic_turn_replay_is_silent(tmp_path):
    hook = SKILL / "hook_prompt_route.py"
    p = _prompt_payload(SYNTHETIC_PROMPTS[0], "p-9", tmp_path)
    assert "route unchanged" in run(hook, p, tmp_path).stdout
    assert run(hook, p, tmp_path).stdout == ""


# concurrency ----------------------------------------------------------------------------

def run_parallel(hook, payloads, home):
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), APPDATA=str(home))
    env.pop("CLAUDE_PROJECT_DIR", None)
    env.pop("COST_ROUTER_ENFORCE", None)
    procs = [subprocess.Popen([sys.executable, str(hook)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, text=True, env=env, encoding="utf-8") for _ in payloads]
    return [p.communicate(json.dumps(pl), timeout=120)[0] for p, pl in zip(procs, payloads)]


def test_parallel_usage_hooks_record_everything_and_leave_no_tmp(tmp_path):
    run_parallel(USAGE_HOOK, [post_payload(f"par{i}") for i in range(10)], tmp_path)
    assert sorted(e["tool_use_id"] for e in usage_entries(tmp_path)) == sorted(f"par{i}" for i in range(10))
    assert list(state_dir(tmp_path).glob("*.tmp")) == []


def test_parallel_enforce_hooks_leave_no_tmp_and_reach_a_deny(tmp_path):
    write_route(tmp_path)
    outs = run_parallel(ENFORCE, [bash(f"pe{i}") for i in range(20)], tmp_path)
    assert list(state_dir(tmp_path).glob("*.tmp")) == []
    decisions = [json.loads(o)["hookSpecificOutput"].get("permissionDecision") if o.strip() else "allow" for o in outs]
    assert decisions.count("allow") >= 1  # the free calls are never denied
    assert decisions.count("deny") >= 4  # best effort by design: counts may race


def test_agent_usage_sidecar_skips_corrupt_lines_and_caps_strings(tmp_path, monkeypatch):
    run(USAGE_HOOK, post_payload("c1", tool_input={"model": "m" * 200}), tmp_path)
    with (state_dir(tmp_path) / f"route-{SID}.agent_usage.jsonl").open("a", encoding="utf-8") as fh:
        fh.write("{corrupt\n")
    run(USAGE_HOOK, post_payload("c2"), tmp_path)
    import route_state
    monkeypatch.setenv("HOME", str(tmp_path))
    monkeypatch.setenv("USERPROFILE", str(tmp_path))
    es = route_state.agent_usage(SID)
    assert [e["tool_use_id"] for e in es] == ["c1", "c2"] and len(es[0]["requested_model"]) == 64


# F-DEADLOCK -----------------------------------------------------------------------------

def vault_project(tmp_path):
    cli = tmp_path / "proj" / "03_IMPLEMENTATION" / "packages" / "routing" / "route_cli.py"
    cli.parent.mkdir(parents=True)
    cli.write_text("", encoding="utf-8")
    return tmp_path / "proj"


def agent_spawn(proj):
    return {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": "Agent", "cwd": str(proj),
            "tool_use_id": "sp1", "tool_input": {"description": "implement feature", "prompt": "implement it",
                                                 "subagent_type": "vault-worker"}}


def read_call(tid, tool="Edit"):
    """A counted main-session tool call; Edit by default because Read/Grep/Glob are never denied (see below)."""
    ti = {"file_path": "x.py"} if tool == "Read" else {"file_path": "x.py", "old_string": "a", "new_string": "b"}
    return {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": tool, "tool_use_id": tid,
            "tool_input": ti}


def test_denied_spawn_unlocks_direct_work_with_a_context_line(tmp_path):
    proj = vault_project(tmp_path)
    write_route(tmp_path)
    assert denied(run(AGENT, agent_spawn(proj), tmp_path))
    outs = [run(ENFORCE, read_call(f"r{i}"), tmp_path) for i in range(4)]
    assert not any(denied(o) for o in outs)
    assert all(o.stdout == "" for o in outs[:3])
    ctx = json.loads(outs[3].stdout)["hookSpecificOutput"]["additionalContext"]
    assert ctx == ("cost-router: delegation was refused by the Agent gate; direct work allowed for this prompt"
                   " (run the Vault router to delegate)")


def test_without_the_marker_the_fourth_read_is_still_denied(tmp_path):
    write_route(tmp_path)
    assert [denied(run(ENFORCE, read_call(f"r{i}"), tmp_path)) for i in range(4)] == [False, False, False, True]


def test_new_prompt_epoch_clears_the_delegation_denied_marker(tmp_path):
    proj = vault_project(tmp_path)
    write_route(tmp_path)
    assert denied(run(AGENT, agent_spawn(proj), tmp_path))
    assert (state_dir(tmp_path) / f"route-{SID}.delegation_denied").exists()
    write_route(tmp_path, epoch="e2")  # a new prompt epoch: the old marker content is void
    assert [denied(run(ENFORCE, read_call(f"n{i}"), tmp_path)) for i in range(4)] == [False, False, False, True]


# in-hook Vault router (decision iii) ------------------------------------------------------

FAKE_ROUTER = """import json, os, sys, time
d = os.environ.get("FAKE_ROUTE_DIR")
if d:
    with open(os.path.join(d, "calls.txt"), "a") as fh:
        fh.write("x")
    with open(os.path.join(d, "argv.json"), "w") as fh:
        json.dump(sys.argv[1:], fh)
mode = os.environ.get("FAKE_ROUTE_STATUS", "ROUTED")
if mode == "CRASH":
    sys.exit(1)
if mode == "HANG":
    time.sleep(8)
if mode == "GRANDCHILD":
    import subprocess
    subprocess.Popen([sys.executable, "-c", "import time; time.sleep(20)"])
    time.sleep(30)
print("banner line")
via = os.environ.get("FAKE_EXECUTE_VIA", "claude_code_agent_tool")
print(json.dumps({"status": mode, "policy_gates": ["privacy gate hit"], "rejected": ["codex: no quota"],
                  "primary": {"runtime_id": "codex_cli"},
                  "execution_contract": {"subagent_type": "vault-worker", "execute_via": via}}))
sys.exit(1 if mode == "BLOCKED" else 0)  # route_cli exits 1 on BLOCKED
"""


def fake_vault(tmp_path):
    pkg = tmp_path / "proj" / "03_IMPLEMENTATION" / "packages" / "routing"
    pkg.mkdir(parents=True)
    (pkg / "__init__.py").write_text("", encoding="utf-8")
    (pkg / "route_cli.py").write_text(FAKE_ROUTER, encoding="utf-8")
    return tmp_path / "proj"


def run_in_vault(hook, payload, home: Path, proj: Path, status="ROUTED", **extra):
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), APPDATA=str(home), CLAUDE_PROJECT_DIR=str(proj),
               FAKE_ROUTE_STATUS=status, FAKE_ROUTE_DIR=str(home), **extra)
    env.pop("COST_ROUTER_ENFORCE", None)
    return subprocess.run([sys.executable, str(hook)], input=json.dumps(payload), capture_output=True, text=True,
                          timeout=60, env=env, encoding="utf-8")


def router_calls(home: Path) -> int:
    f = home / "calls.txt"
    return len(f.read_text()) if f.exists() else 0


def test_inhook_router_routed_allows_records_receipt_and_runs_once(tmp_path, monkeypatch):
    proj = fake_vault(tmp_path)
    write_route(tmp_path)
    r = run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, "ROUTED")
    assert not denied(r)
    out = json.loads(r.stdout)["hookSpecificOutput"]
    assert out["permissionDecision"] == "allow"
    assert "vault-router: ROUTED -> vault-worker" in out["permissionDecisionReason"]
    assert not (state_dir(tmp_path) / f"route-{SID}.delegation_denied").exists()
    assert (state_dir(tmp_path) / f"route-{SID}.delegated").exists()
    st = json.loads((state_dir(tmp_path) / f"route-{SID}.json").read_text(encoding="utf-8"))
    assert st["vault_route"]["status"] == "ROUTED" and st["vault_route"]["subagent_type"] == "vault-worker"
    assert "implement it" not in json.dumps(st)  # the goal is never stored
    monkeypatch.setenv("HOME", str(tmp_path))
    monkeypatch.setenv("USERPROFILE", str(tmp_path))
    import route_state
    assert route_state.class_routed(SID, "implement")
    assert router_calls(tmp_path) == 1
    assert not denied(run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, "ROUTED"))
    assert router_calls(tmp_path) == 1  # receipt reused: the router did not run again


def test_inhook_router_blocked_denies_then_direct_allowance_is_capped(tmp_path):
    proj = fake_vault(tmp_path)
    write_route(tmp_path)
    r = run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, "BLOCKED")
    reason = json.loads(r.stdout)["hookSpecificOutput"]["permissionDecisionReason"]
    assert reason.startswith("cost-router: Vault router BLOCKED this dispatch (") and "codex: no quota" in reason
    assert "privacy gate hit" not in reason and "unavailable" not in reason  # exit 1 + JSON is BLOCKED, not a crash
    assert reason.endswith("); do the work directly or change the task")
    assert (state_dir(tmp_path) / f"route-{SID}.delegation_denied").exists()
    cap = 3 + 6
    outs = [run(ENFORCE, read_call(f"c{i}"), tmp_path) for i in range(cap + 2)]
    assert [denied(o) for o in outs] == [False] * cap + [True, True]
    assert "delegation was refused" in json.loads(outs[3].stdout)["hookSpecificOutput"]["additionalContext"]
    assert outs[4].stdout == ""
    msg = json.loads(outs[cap].stdout)["hookSpecificOutput"]["permissionDecisionReason"]
    assert msg == ("cost-router: direct-work allowance after a refused dispatch is used up (9 calls); fix the"
                   " blocker named by the Agent gate or put #direct in the prompt")


@pytest.mark.parametrize("mode", ["CRASH", "HANG"])
def test_inhook_router_unavailable_denies_with_fallback_reason(tmp_path, mode):
    import time
    proj = fake_vault(tmp_path)
    write_route(tmp_path)
    t0 = time.time()
    r = run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, mode)
    assert time.time() - t0 < 7
    reason = json.loads(r.stdout)["hookSpecificOutput"]["permissionDecisionReason"]
    assert reason.startswith("cost-router: run the Vault router first:")
    assert "in-hook router unavailable: " + ("exit 1" if mode == "CRASH" else "timeout 5s") in reason
    assert (state_dir(tmp_path) / f"route-{SID}.delegation_denied").exists()


def test_inhook_router_never_runs_for_direct(tmp_path):
    proj = fake_vault(tmp_path)
    write_route(tmp_path, direct=True)
    assert not denied(run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, "BLOCKED"))
    assert router_calls(tmp_path) == 0


def test_inhook_router_blocked_json_with_exit_1_is_blocked_not_unavailable(tmp_path):
    proj = fake_vault(tmp_path)
    write_route(tmp_path)
    r = run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, "BLOCKED")
    reason = json.loads(r.stdout)["hookSpecificOutput"]["permissionDecisionReason"]
    assert reason == ("cost-router: Vault router BLOCKED this dispatch (codex: no quota);"
                      " do the work directly or change the task")


def test_inhook_router_non_claude_route_denies_without_receipt(tmp_path):
    proj = fake_vault(tmp_path)
    write_route(tmp_path)
    r = run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, "ROUTED", FAKE_EXECUTE_VIA="dispatcher")
    reason = json.loads(r.stdout)["hookSpecificOutput"]["permissionDecisionReason"]
    assert reason.startswith("cost-router: Vault router routed this task to dispatcher/codex_cli instead of a Claude"
                             " subagent; dispatch it with python -m routing.route_cli dispatch --goal")
    assert (state_dir(tmp_path) / f"route-{SID}.delegation_denied").exists()
    assert not (state_dir(tmp_path) / f"route-{SID}.delegated").exists()
    assert not (state_dir(tmp_path) / f"route-{SID}.routed_classes").exists()  # no receipt
    st = json.loads((state_dir(tmp_path) / f"route-{SID}.json").read_text(encoding="utf-8"))
    assert st["vault_route"]["execute_via"] == "dispatcher" and st["vault_route"]["status"] == "ROUTED"
    ok = run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, "ROUTED", FAKE_EXECUTE_VIA="claude_code_agent_tool")
    assert not denied(ok) and "vault-router: ROUTED -> vault-worker" in ok.stdout


def test_inhook_router_goal_is_one_argv_element_even_when_dash_leading(tmp_path):
    proj = fake_vault(tmp_path)
    write_route(tmp_path)
    spawn = agent_spawn(proj)
    spawn["tool_input"]["prompt"] = "--help"
    run_in_vault(AGENT, spawn, tmp_path, proj, "ROUTED")
    assert json.loads((tmp_path / "argv.json").read_text()) == ["route", "--goal=--help"]


def test_inhook_router_timeout_kills_a_grandchild_holding_the_handles(tmp_path):
    import time
    proj = fake_vault(tmp_path)
    write_route(tmp_path)
    t0 = time.time()
    r = run_in_vault(AGENT, agent_spawn(proj), tmp_path, proj, "GRANDCHILD")
    assert time.time() - t0 < 7
    reason = json.loads(r.stdout)["hookSpecificOutput"]["permissionDecisionReason"]
    assert "in-hook router unavailable: timeout 5s" in reason


def test_inhook_router_is_never_launched_through_a_shell(monkeypatch):
    import importlib.util
    spec = importlib.util.spec_from_file_location("hook_agent_model_under_test", AGENT)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    import route_state
    seen = []

    def fake(cmd, *a, **kw):
        seen.append((cmd, kw))
        raise OSError("stop")

    monkeypatch.setattr(mod.subprocess, "Popen", fake)
    monkeypatch.setattr(mod.subprocess, "run", fake)
    assert mod._run_vault_router(route_state, {"cwd": "."}, "do it") == (None, "OSError")
    assert seen
    for cmd, kw in seen:
        assert isinstance(cmd, list) and not kw.get("shell")


# session model (O-1): delegation is advisory when it would not be cheaper ------------------

def set_session_model(home: Path, model):
    r = run(REPO_MAP, {"session_id": SID, "hook_event_name": "SessionStart", "source": "startup",
                       "model": model, "cwd": str(home)}, home)
    assert r.returncode == 0, r.stderr


@pytest.mark.parametrize("session_model,advisory", [
    ("claude-sonnet-5-5", True),   # same tier as the sonnet route: delegation is pure overhead
    ("haiku", False),              # route above the session: the stronger model must do it
    ("claude-opus-5-5", False),    # route below the session: delegation saves money
    (None, False)])                # unknown session model: old behaviour
def test_delegation_is_advisory_only_when_the_session_model_equals_the_route_tier(tmp_path, session_model, advisory):
    if session_model:
        set_session_model(tmp_path, session_model)
    write_route(tmp_path)  # route: implement -> vault-worker on sonnet
    outs = [run(ENFORCE, read_call(f"s{i}"), tmp_path) for i in range(5)]
    assert [denied(o) for o in outs] == ([False] * 5 if advisory else [False] * 3 + [True, True])
    if advisory:
        ctx = json.loads(outs[3].stdout)["hookSpecificOutput"]["additionalContext"]
        assert ctx.startswith("cost-router: delegation is advisory here (session model " + session_model)
        assert outs[4].stdout == ""  # the note is shown once


@pytest.mark.parametrize("session_model", ["claude-sonnet-5-5", "claude-opus-5-5"])
def test_risk_floored_review_route_is_still_enforced_whatever_the_session_model(tmp_path, session_model):
    """Round-2 (sonnet) and final (opus, finding N1) review findings: a review route is never made advisory by
    the equal-tier fallback, even when the session already runs on the route's tier."""
    set_session_model(tmp_path, session_model)
    write_route(tmp_path, task_class="review", model="opus", subagent="vault-reviewer")
    outs = [run(ENFORCE, read_call(f"r{i}"), tmp_path) for i in range(5)]
    assert [denied(o) for o in outs] == [False] * 3 + [True, True]


def test_risk_floored_implement_route_is_still_enforced_on_the_same_tier(tmp_path):
    set_session_model(tmp_path, "claude-opus-5-5")
    write_route(tmp_path, task_class="implement", model="opus", subagent="vault-worker", risk="high")
    outs = [run(ENFORCE, read_call(f"h{i}"), tmp_path) for i in range(5)]
    assert [denied(o) for o in outs] == [False] * 3 + [True, True]


def test_session_model_survives_a_new_prompt_epoch(tmp_path):
    set_session_model(tmp_path, "claude-sonnet-5-5")
    write_route(tmp_path, epoch="e2")
    assert not any(denied(run(ENFORCE, read_call(f"e{i}"), tmp_path)) for i in range(5))


def test_compact_route_line_names_a_high_risk(tmp_path):
    r = run(SKILL / "hook_prompt_route.py", {"session_id": "risk-1", "prompt":
            "fix the auth token validation bug in the signature check and add tests"}, tmp_path)
    ctx = json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]
    # phase 6: pinned (was "risk=high or risk=critical"): the brief is risk high, routed to a sonnet worker
    assert ctx.startswith("cost-router: implement -> vault-worker subagent, sonnet risk=high"), ctx
    assert len(ctx) <= 120
    plain = run(SKILL / "hook_prompt_route.py", {"session_id": "risk-2", "prompt":
                "summarize the notes in this folder for me please"}, tmp_path)
    assert "risk=" not in plain.stdout


# h. settings ----------------------------------------------------------------------------

def test_project_settings_reference_existing_hook_files_and_post_tool_use():
    s = json.loads((ROOT / ".claude" / "settings.json").read_text(encoding="utf-8"))
    commands = [h["command"] for entries in s["hooks"].values() for e in entries for h in e["hooks"]]
    files = {m for c in commands for m in re.findall(r"\.claude/skills/cost-router/([\w./-]+\.py)", c)}
    assert "hook_agent_usage.py" in files
    for f in files:
        assert (SKILL / f).is_file(), f
    post = s["hooks"]["PostToolUse"]
    assert post[0]["matcher"] == "Agent|Task" and "hook_agent_usage.py" in post[0]["hooks"][0]["command"]
    assert post[0]["hooks"][0]["timeout"] == 10


def test_report_runs_on_empty_state_and_lists_recorded_usage(tmp_path):
    r = run(USAGE_HOOK, "", tmp_path, args=["--report"])
    assert r.returncode == 0
    run(USAGE_HOOK, post_payload(), tmp_path)
    r = run(USAGE_HOOK, "", tmp_path, args=["--report", SID])
    assert r.returncode == 0 and "claude-haiku-5-5" in r.stdout and "sum est_cost_usd" in r.stdout


def test_missing_usage_is_stored_as_unknown_not_zero_and_reported_unknown(tmp_path):
    """Audit D / T1: a tool_response without usage categories must not become 0 tokens / a priced 0-cost spawn."""
    payload = post_payload("t_missing")
    payload["tool_response"]["usage"] = {"input_tokens": 2, "output_tokens": 5}   # cache categories absent
    run(USAGE_HOOK, payload, tmp_path)
    nousage = post_payload("t_nousage")
    del nousage["tool_response"]["usage"], nousage["tool_response"]["totalTokens"]
    run(USAGE_HOOK, nousage, tmp_path)
    run(USAGE_HOOK, post_payload("t_full"), tmp_path)
    e1, e2, e3 = usage_entries(tmp_path)
    assert e1["usage_complete"] is False
    assert e1["missing_fields"] == ["cache_read_input_tokens", "cache_creation_input_tokens"]
    assert e1["cache_read_input_tokens"] is None and e1["cache_creation_input_tokens"] is None
    assert (e1["input_tokens"], e1["output_tokens"]) == (2, 5) and e1["est_cost_usd"] is None
    assert e2["usage_complete"] is False and e2["total_tokens"] is None and len(e2["missing_fields"]) == 4
    assert all(e2[k] is None for k in e2["missing_fields"])
    assert e3["usage_complete"] is True and e3["missing_fields"] == [] and e3["est_cost_usd"] > 0
    out = run(USAGE_HOOK, "", tmp_path, args=["--report", SID]).stdout
    assert out.count("unknown") >= 5                                     # unknown cells, never 0
    first = [ln for ln in out.splitlines() if ln.startswith("claude-haiku-5-5")][0]
    assert first.split()[2:6] == ["2", "unknown", "unknown", "5"]        # in, cache_r, cache_w, out
    assert "lower bound: 2 spawn(s) with unknown usage" in out


# i. repo map ----------------------------------------------------------------------------

def test_repo_map_adds_state_line_only_on_compact(tmp_path):
    write_route(tmp_path)
    base = {"session_id": SID, "hook_event_name": "SessionStart", "cwd": str(tmp_path)}
    run(USAGE_HOOK, post_payload(), tmp_path)
    out = run(REPO_MAP, {**base, "source": "compact"}, tmp_path)
    assert out.returncode == 0
    ctx = json.loads(out.stdout)["hookSpecificOutput"]["additionalContext"]
    line = [ln for ln in ctx.splitlines() if ln.startswith("cost-router:")][0]
    assert "class=implement" in line and "subagents=1" in line and len(line) < 200
    out = run(REPO_MAP, {**base, "source": "startup"}, tmp_path)
    assert out.returncode == 0 and "cost-router:" not in out.stdout


# -- phase 2: compact route line is the default, long form is opt-in ---------------------

PROMPT_HOOK = SKILL / "hook_prompt_route.py"
LONG_PROMPT = {"prompt": "find which files import memory_controller and list them", "session_id": "s-compact"}


def prompt_ctx(home, **env):
    old = {k: os.environ.get(k) for k in env}
    os.environ.update(env)
    try:
        r = run(PROMPT_HOOK, LONG_PROMPT, home)
    finally:
        for k, v in old.items():
            os.environ.pop(k, None) if v is None else os.environ.__setitem__(k, v)
    assert r.returncode == 0, r.stderr
    return json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]


def test_route_line_is_compact_by_default(tmp_path):
    ctx = prompt_ctx(tmp_path)
    assert len(ctx) <= 120 and "explore" in ctx and "Explore" in ctx and "haiku" in ctx
    assert "Enforced" not in ctx and "Apply the cost-router skill" not in ctx


def test_compact_route_appends_the_owner_language(tmp_path):
    (tmp_path / ".claude").mkdir()
    (tmp_path / ".claude" / "settings.json").write_text(json.dumps({"language": "ro"}), encoding="utf-8")
    ctx = prompt_ctx(tmp_path)
    assert ctx.endswith(" lang=ro") and len(ctx) <= 120
    assert " lang=" not in prompt_ctx(tmp_path / "other")


def test_verbose_route_restores_the_long_form(tmp_path):
    ctx = prompt_ctx(tmp_path, COST_ROUTER_VERBOSE_ROUTE="1")
    assert "class=explore" in ctx and "Agent hook sets its model" in ctx and len(ctx) > 120


def test_compact_route_still_persists_the_enforced_state(tmp_path):
    prompt_ctx(tmp_path)
    states = list(state_dir(tmp_path).glob("route-*.json"))
    assert states and json.loads(states[0].read_text(encoding="utf-8")).get("subagent") == "Explore"


# ---- COST_ROUTER_OFF=1: every hook is inert (equal-arm benchmark baseline) ----------------------------------------
OFF_PAYLOADS = {
    "hook_prompt_route.py": {"session_id": SID, "hook_event_name": "UserPromptSubmit", "prompt_id": "p1",
                             "prompt": "implement a new feature across several files in the vault and add tests " * 3},
    "hook_repo_map.py": {"session_id": SID, "hook_event_name": "SessionStart", "source": "startup",
                         "model": "claude-sonnet-5-5", "cwd": str(ROOT)},
    "hook_agent_model.py": {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": "Agent", "tool_use_id": "t1",
                            "tool_input": {"model": "opus", "prompt": "explore the repo", "subagent_type": "Explore"}},
    "hook_enforce_route.py": {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": "Read", "tool_use_id": "t2",
                              "tool_input": {"file_path": "x.py"}},
    "hook_resume_guard.py": {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": "SendMessage",
                             "tool_use_id": "t3", "tool_input": {"to": "agent-abc", "message": "again"}},
    "hook_agent_usage.py": post_payload(),
    "hook_self_update.py": {"session_id": SID, "hook_event_name": "SessionStart"},
}


@pytest.mark.parametrize("hook", sorted(OFF_PAYLOADS))
def test_cost_router_off_makes_every_hook_inert(tmp_path, hook):
    write_route(tmp_path)  # a live route would otherwise make enforce/usage hooks write or deny
    before = sorted(p.name for p in (tmp_path).rglob("*") if p.is_file())
    env = dict(os.environ, HOME=str(tmp_path), USERPROFILE=str(tmp_path), APPDATA=str(tmp_path), COST_ROUTER_OFF="1")
    env.pop("CLAUDE_PROJECT_DIR", None)
    r = subprocess.run([sys.executable, str(SKILL / hook)], input=json.dumps(OFF_PAYLOADS[hook]), capture_output=True,
                       text=True, timeout=60, env=env, encoding="utf-8")
    assert r.returncode == 0 and r.stdout == ""
    assert sorted(p.name for p in tmp_path.rglob("*") if p.is_file()) == before  # no state file created or changed


def test_cost_router_off_is_off_by_default_and_unset_hooks_still_work(tmp_path):
    r = run(SKILL / "hook_agent_usage.py", post_payload(), tmp_path)
    assert r.returncode == 0 and len(usage_entries(tmp_path)) == 1


# phase 3: fewer model tokens per task (behavioural defaults, each env-switchable) -----------------

def test_fourth_read_is_allowed_with_the_delegate_reason_once_but_fourth_edit_is_denied(tmp_path):
    write_route(tmp_path)
    outs = [run(ENFORCE, read_call(f"r{i}", "Read"), tmp_path) for i in range(6)]
    assert not any(denied(o) for o in outs) and all(o.stdout == "" for o in outs[:3])
    ctx = json.loads(outs[3].stdout)["hookSpecificOutput"]["additionalContext"]
    assert "delegate to vault-worker" in ctx and outs[4].stdout == "" and outs[5].stdout == ""
    # state still counts reads
    assert json.loads((state_dir(tmp_path) / f"route-{SID}.json").read_text(encoding="utf-8"))["main_calls"] == 6
    tmp2 = tmp_path / "e"
    write_route(tmp2)
    assert [denied(run(ENFORCE, read_call(f"e{i}"), tmp2)) for i in range(4)] == [False, False, False, True]


def test_deny_reads_env_restores_the_old_deny(tmp_path):
    write_route(tmp_path)
    r = [denied(run(ENFORCE, read_call(f"r{i}", g), tmp_path, env_extra={"COST_ROUTER_DENY_READS": "1"}))
         for i, g in enumerate(("Read", "Grep", "Glob", "Read"))]
    assert r == [False, False, False, True]


def prompt_payload(prompt, pid):
    return {"session_id": SID, "hook_event_name": "UserPromptSubmit", "prompt_id": pid, "cwd": ".", "prompt": prompt}


def test_route_line_only_when_the_route_changes(tmp_path):
    hook = SKILL / "hook_prompt_route.py"
    impl = "implement a new feature in the module with tests and refactor the parser"
    first = run(hook, prompt_payload(impl, "p1"), tmp_path)
    assert "cost-router:" in first.stdout
    second = run(hook, prompt_payload(impl + " again please", "p2"), tmp_path)
    assert second.returncode == 0 and second.stdout == ""
    f = state_dir(tmp_path) / f"route-{SID}.json"
    assert json.loads(f.read_text(encoding="utf-8"))["subagent"] == "vault-worker"  # state written on the silent prompt
    changed = run(hook, prompt_payload("find which files import memory_controller and list them all", "p3"), tmp_path)
    assert "explore" in changed.stdout
    verbose = run(hook, prompt_payload("find which files import memory_controller and list them all", "p4"), tmp_path,
                  env_extra={"COST_ROUTER_VERBOSE_ROUTE": "1"})
    assert "cost-router" in verbose.stdout


def test_route_line_repeats_on_high_risk(tmp_path):
    hook = SKILL / "hook_prompt_route.py"
    p = "rotate the production secret and delete the old key now"
    assert "risk=" in run(hook, prompt_payload(p, "a1"), tmp_path).stdout
    assert "risk=" in run(hook, prompt_payload(p + " today", "a2"), tmp_path).stdout


def agent_call(**ti):
    return {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": "Agent", "tool_input": ti,
            "model": "opus"}  # an opus session: these tests pin the route itself, the escalation policy has its own matrix


def _agent_project(tmp_path, name, frontmatter_tools):
    """A temp project dir with .claude/agents/<name>.md; frontmatter_tools is the raw text after `tools:` (None: no line)."""
    proj = tmp_path / "proj"
    (proj / ".claude" / "agents").mkdir(parents=True, exist_ok=True)
    lines = ["---", f"name: {name}", "description: test agent"]
    if frontmatter_tools is not None:
        lines.append("tools:" + frontmatter_tools)
    lines += ["model: haiku", "---", "body"]
    (proj / ".claude" / "agents" / f"{name}.md").write_text("\n".join(lines), encoding="utf-8")
    return proj


def _agent_model(tmp_path, proj, stype, prompt, **extra):
    payload = agent_call(prompt=prompt, subagent_type=stype, **extra)
    payload["cwd"] = str(proj)
    out = json.loads(run(AGENT, payload, tmp_path).stdout)["hookSpecificOutput"]
    return out["updatedInput"]["model"]


IMPL_BRIEF = "implement a new feature in the module with tests and refactor the parser"
CRIT_BRIEF = "audit the security of the auth bypass and rotate the production secret, find the files"


def test_strict_read_only_agent_is_clamped_to_explore_on_a_non_risky_brief(tmp_path):
    proj = _agent_project(tmp_path, "roaudit", " Read, Grep, Glob")
    assert _agent_model(tmp_path, proj, "roaudit", IMPL_BRIEF) == "haiku"
    assert _agent_model(tmp_path, proj, "roaudit", IMPL_BRIEF, model="opus") == "sonnet"  # one tier above, no more
    # the same brief on a general-purpose agent keeps the policy route
    assert _agent_model(tmp_path, proj, "general-purpose", IMPL_BRIEF) == "sonnet"
    # env restores the old floor
    payload = agent_call(prompt=IMPL_BRIEF, subagent_type="roaudit")
    payload["cwd"] = str(proj)
    out = json.loads(run(AGENT, payload, tmp_path, env_extra={"COST_ROUTER_READONLY_FLOOR": "1"}).stdout)["hookSpecificOutput"]
    assert out["updatedInput"]["model"] == "sonnet"


@pytest.mark.parametrize("tools", [
    " Read, Grep, Glob, Bash",
    "\n  - Read\n  - Edit",
    " [Read, Edit]",
    " Read, PowerShell",
    " Read, MultiEdit",
    " Read, mcp__desktop-commander__write_file",
    " Read, mcp__vault-memory__memory_propose",
    " [Read, Grep",
    " read, grep",
    " ",
    None])
def test_agents_with_any_non_allowlisted_or_unparseable_tools_are_not_read_only(tmp_path, tools):
    proj = _agent_project(tmp_path, "roaudit", tools)
    assert _agent_model(tmp_path, proj, "roaudit", IMPL_BRIEF) == "sonnet"  # old floor: not clamped
    assert _agent_model(tmp_path, proj, "roaudit", CRIT_BRIEF) == "opus"


@pytest.mark.parametrize("tools", [
    "\n  - Read\n  - Grep",
    " [Read, Grep, Glob, LS, WebFetch, WebSearch]",
    " Read, mcp__vault-memory__vault_read, mcp__vault-memory__memory_search"])
def test_read_only_tool_list_forms_are_accepted(tmp_path, tools):
    proj = _agent_project(tmp_path, "roaudit", tools)
    assert _agent_model(tmp_path, proj, "roaudit", IMPL_BRIEF) == "haiku"


def test_explore_with_bash_in_the_project_file_and_a_missing_file_are_not_read_only(tmp_path):
    proj = tmp_path / "proj"
    (proj / ".claude" / "agents").mkdir(parents=True)
    (proj / ".claude" / "agents" / "Explore.md").write_text(
        "---\nname: Explore\ntools: Read, Grep, Glob, Bash\n---\nx", encoding="utf-8")
    assert _agent_model(tmp_path, proj, "Explore", CRIT_BRIEF) == "opus"  # old floor (critical brief)
    assert _agent_model(tmp_path, proj, "Explore", IMPL_BRIEF) == "sonnet"
    bare = tmp_path / "bare"
    bare.mkdir()
    assert _agent_model(tmp_path, bare, "Explore", CRIT_BRIEF) == "opus"  # built-in Explore, no project file
    assert _agent_model(tmp_path, bare, "Explore", IMPL_BRIEF) == "sonnet"  # no bare `explore` shortcut


def test_read_only_clamp_never_applies_to_review_or_risky_briefs_or_reviewer_roles(tmp_path):
    proj = _agent_project(tmp_path, "roaudit", " Read, Grep, Glob")
    assert _agent_model(tmp_path, proj, "roaudit", "Security review: verify the critical auth bypass fix") == "opus"
    assert _agent_model(tmp_path, proj, "roaudit", CRIT_BRIEF) == "opus"
    out = json.loads(run(AGENT, agent_call(prompt=CRIT_BRIEF, subagent_type="vault-reviewer"), tmp_path).stdout)["hookSpecificOutput"]
    assert out["updatedInput"]["model"] == "opus"


def test_agent_allow_context_only_when_verbose_and_prompt_is_never_touched(tmp_path):
    ti = dict(description="d", prompt="find which files import memory_controller" + chr(10) + "  x",
              subagent_type="general-purpose", model="opus")
    out = json.loads(run(AGENT, agent_call(**ti), tmp_path).stdout)["hookSpecificOutput"]
    # phase 6: exact reason and model (was a "cost-router" substring): explore route on an opus session, opus capped
    reason = "cost-router: subagent model opus -> sonnet (class explore)"
    assert "additionalContext" not in out and out["permissionDecisionReason"] == reason
    assert out["updatedInput"] == dict(ti, model="sonnet")
    out = json.loads(run(AGENT, agent_call(**ti), tmp_path, env_extra={"COST_ROUTER_VERBOSE_ROUTE": "1"}).stdout)["hookSpecificOutput"]
    assert out["additionalContext"] == reason + "." and out["updatedInput"] == dict(ti, model="sonnet")


# resolve_session_model + same-tier route line (phase 3 rerun) -------------------------------

def _rs(tmp_path, monkeypatch):
    import importlib
    monkeypatch.setenv("HOME", str(tmp_path))
    monkeypatch.setenv("USERPROFILE", str(tmp_path))
    monkeypatch.delenv("CLAUDE_PROJECT_DIR", raising=False)
    return importlib.import_module("route_state")


def _transcript(path: Path, model):
    rec = {"type": "assistant", "message": {"role": "assistant", "model": model, "usage": {"input_tokens": 1}}}
    path.write_text(json.dumps({"type": "user", "message": {"role": "user", "content": "hi"}}) + chr(10)
                    + json.dumps(rec) + chr(10), encoding="utf-8")


def _project_settings(tmp_path, model):
    proj = tmp_path / "proj"
    (proj / ".claude").mkdir(parents=True)
    (proj / ".claude" / "settings.json").write_text(json.dumps({"model": model}), encoding="utf-8")
    return proj


def test_resolve_session_model_payload_wins_and_is_stored(tmp_path, monkeypatch):
    rs = _rs(tmp_path, monkeypatch)
    proj = _project_settings(tmp_path, "opus")
    t = tmp_path / "t.jsonl"
    _transcript(t, "claude-haiku-5-5")
    assert rs.resolve_session_model("s1", {"model": "claude-sonnet-5-5", "cwd": str(proj)}, str(t)) == "claude-sonnet-5-5"
    assert rs.session_model("s1") == "claude-sonnet-5-5"
    assert rs.resolve_session_model("s1", {"cwd": str(proj)}, str(t)) == "claude-haiku-5-5"  # the transcript's last model beats the stored value


def test_resolve_session_model_transcript_then_settings_then_none(tmp_path, monkeypatch):
    rs = _rs(tmp_path, monkeypatch)
    proj = _project_settings(tmp_path, "opus")
    t = tmp_path / "t.jsonl"
    _transcript(t, "claude-sonnet-5-5")
    assert rs.resolve_session_model("s2", {"cwd": str(proj)}, str(t)) == "claude-sonnet-5-5"  # transcript beats settings
    assert rs.session_model("s2") == "claude-sonnet-5-5"
    assert rs.resolve_session_model("s3", {"cwd": str(proj)}, str(tmp_path / "missing.jsonl")) == "opus"  # settings
    (proj / ".claude" / "settings.local.json").write_text(json.dumps({"model": "haiku"}), encoding="utf-8")
    assert rs.resolve_session_model("s3", {"cwd": str(proj)}, None) == "haiku"  # local settings first
    monkeypatch.setenv("ANTHROPIC_MODEL", "sonnet")
    assert rs.resolve_session_model("s5", {"cwd": str(proj)}, None) == "sonnet"  # env beats the settings files
    monkeypatch.delenv("ANTHROPIC_MODEL")
    empty = tmp_path / "empty"
    empty.mkdir()
    assert rs.resolve_session_model("s4", {"cwd": str(empty)}, None) is None
    assert rs.resolve_session_model("s4", {"cwd": str(empty)}, "no/such/file") is None
    monkeypatch.chdir(empty)
    assert rs.resolve_session_model(None, None, None) is None


def _prompt(tmp_path, text, pid, session_model=None):
    if session_model:
        set_session_model(tmp_path, session_model)
    return run(SKILL / "hook_prompt_route.py", prompt_payload(text, pid), tmp_path)


def _edits_denied(tmp_path, n=6):
    return [denied(run(ENFORCE, read_call(f"e{i}"), tmp_path)) for i in range(n)]


def test_same_tier_route_line_says_direct_work_ok_and_never_denies(tmp_path):
    r = _prompt(tmp_path, IMPL_BRIEF, "p1", "claude-sonnet-5-5")
    ctx = json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]
    assert ctx.startswith("cost-router: implement same tier (sonnet): direct work ok") and "subagent" not in ctx
    state = json.loads((state_dir(tmp_path) / f"route-{SID}.json").read_text(encoding="utf-8"))
    assert state["subagent"] == "vault-worker" and state["same_tier"] is True  # telemetry keeps the subagent
    outs = [run(ENFORCE, read_call(f"e{i}"), tmp_path) for i in range(9)]
    assert not any(denied(o) for o in outs)
    assert sum("additionalContext" in o.stdout for o in outs) == 1  # one note


def test_advisory_note_honours_state_same_tier_without_a_session_model(tmp_path):
    write_route(tmp_path, same_tier=True)
    outs = [run(ENFORCE, read_call(f"n{i}"), tmp_path) for i in range(6)]
    assert not any(denied(o) for o in outs)
    assert "same tier" in json.loads(outs[3].stdout)["hookSpecificOutput"]["additionalContext"]


def test_opus_session_on_a_sonnet_route_keeps_the_old_line_and_denies(tmp_path):
    r = _prompt(tmp_path, IMPL_BRIEF, "p1", "claude-opus-5-5")
    ctx = json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]
    assert "-> vault-worker subagent" in ctx and "same tier" not in ctx
    assert "same_tier" not in json.loads((state_dir(tmp_path) / f"route-{SID}.json").read_text(encoding="utf-8"))
    assert _edits_denied(tmp_path, 5) == [False] * 3 + [True, True]


def test_sonnet_session_on_a_review_opus_route_keeps_the_old_line_and_denies(tmp_path):
    r = _prompt(tmp_path, "Security review: verify the critical auth bypass fix and report findings", "p1",
                "claude-sonnet-5-5")
    ctx = json.loads(r.stdout)["hookSpecificOutput"]["additionalContext"]
    assert "same tier" not in ctx and "opus" in ctx
    assert _edits_denied(tmp_path, 5) == [False] * 3 + [True, True]


def test_prompt_hook_derives_the_session_model_from_the_transcript_when_session_start_had_none(tmp_path):
    t = tmp_path / "t.jsonl"
    _transcript(t, "claude-sonnet-5-5")
    payload = prompt_payload(IMPL_BRIEF, "p1")
    payload["transcript_path"] = str(t)
    ctx = json.loads(run(SKILL / "hook_prompt_route.py", payload, tmp_path).stdout)["hookSpecificOutput"]["additionalContext"]
    assert "same tier (sonnet)" in ctx
    assert json.loads((state_dir(tmp_path) / f"route-{SID}.session_model").read_text(encoding="utf-8"))["model"] \
        == "claude-sonnet-5-5"


def test_sonnet_card_reproduces_a_real_stream_cost_within_one_percent(monkeypatch):
    """E2 (phase 4): the sonnet cache_read price was fitted to 50 real modelUsage records (0.10, not 0.2)."""
    import importlib.util
    spec = importlib.util.spec_from_file_location(
        "bm_pricing", ROOT / "30_SCRIPTS" / "evaluation" / "claude_cost_router_benchmark.py")
    bm = importlib.util.module_from_spec(spec)
    monkeypatch.setitem(sys.modules, "bm_pricing", bm)  # restored on teardown: no leak into other tests
    spec.loader.exec_module(bm)
    stream = (ROOT / "07_EVALUATION" / "reports" / "cost_router_claude_ab_benchmark_2026-10-21_phase3_final" / "raw"
              / "T1_baseline_0.stream.jsonl").read_text(encoding="utf-8")
    ev = bm._events(stream)
    mu = bm._last_model_usage(ev)
    model, v = next(iter(mu.items()))
    assert "sonnet" in model and v["costUSD"] > 0
    rows = [r for r in bm.attribute(ev, mu) if r["model"] == model]
    usage = {"input_tokens": v["inputTokens"], "cache_read_input_tokens": v["cacheReadInputTokens"],
             "output_tokens": v["outputTokens"],
             "cache_creation_5m_input_tokens": sum(r["cache_creation_5m"] for r in rows),
             "cache_creation_1h_input_tokens": sum(r["cache_creation_1h"] for r in rows)}
    assert usage["cache_creation_5m_input_tokens"] + usage["cache_creation_1h_input_tokens"] == v["cacheCreationInputTokens"]
    est = transcript_usage.estimate_cost_usd(model, usage, POLICY)
    assert est == pytest.approx(v["costUSD"], rel=0.01)
    assert POLICY["models"]["sonnet"]["usd_per_mtok"]["cache_read"] == 0.1


# h. LOW-6: atomic resume counter, no fail-open on an unresolved transcript ---------------------------------

_RECORD_ONE = """import os, sys, time
sys.path.insert(0, sys.argv[1])
import route_state
go = sys.argv[2]
while not os.path.exists(go):
    time.sleep(0.002)
sys.exit(0 if route_state.record_resume("sess-1", "abc") else 3)
"""


def test_twenty_concurrent_record_resume_lose_no_increment(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    script = tmp_path / "one.py"
    script.write_text(_RECORD_ONE, encoding="utf-8")
    go = tmp_path / "go"
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), APPDATA=str(home))
    procs = [subprocess.Popen([sys.executable, str(script), str(SKILL / "lib"), str(go)], env=env)
             for _ in range(20)]
    import time
    time.sleep(1.5)  # every child is waiting on the start line
    go.write_text("go", encoding="utf-8")
    codes = [p.wait(timeout=60) for p in procs]
    assert codes == [0] * 20, codes
    counts = json.loads((state_dir(home) / f"route-{SID}.resumes").read_text(encoding="utf-8"))
    assert counts == {"abc": 20}
    assert not list(state_dir(home).glob("*.lock")), "the lock file is released"


def test_resume_guard_counts_by_agent_id_when_the_transcript_cannot_be_resolved(tmp_path):
    for payload in ({"to": "agent-f00d", "message": "go"},):
        base = {"session_id": SID, "hook_event_name": "PreToolUse", "tool_name": "SendMessage",
                "tool_input": payload}  # no transcript_path at all
        r1 = run(RESUME, dict(base, tool_use_id="u1"), tmp_path)
        assert r1.returncode == 0 and not denied(r1)
        r2 = run(RESUME, dict(base, tool_use_id="u2", tool_input={"to": "f00d", "message": "again"}), tmp_path)
        assert denied(r2), r2.stdout
        assert "resumed at most once" in json.loads(r2.stdout)["hookSpecificOutput"]["permissionDecisionReason"]
    # a transcript path whose subagents directory does not exist is unresolved too
    base = {"session_id": "sess-2", "hook_event_name": "PreToolUse", "tool_name": "SendMessage",
            "transcript_path": str(tmp_path / "nowhere" / "sess-2.jsonl"), "tool_input": {"to": "beef"}}
    assert not denied(run(RESUME, dict(base, tool_use_id="v1"), tmp_path))
    assert denied(run(RESUME, dict(base, tool_use_id="v2"), tmp_path))
    # a replayed tool_use_id is still one resume
    base = dict(base, session_id="sess-3")
    assert not denied(run(RESUME, dict(base, tool_use_id="w1"), tmp_path))
    assert not denied(run(RESUME, dict(base, tool_use_id="w1"), tmp_path))
