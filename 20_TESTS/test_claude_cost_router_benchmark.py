"""Unit tests for the Claude cost-router benchmark harness (no claude invocations)."""
from __future__ import annotations

import importlib.util
import json
import math
import sys

import pytest
from pathlib import Path

_P = Path(__file__).resolve().parents[1] / "30_SCRIPTS" / "evaluation" / "claude_cost_router_benchmark.py"
_spec = importlib.util.spec_from_file_location("claude_cost_router_benchmark", _P)
bm = importlib.util.module_from_spec(_spec)
sys.modules["claude_cost_router_benchmark"] = bm
_spec.loader.exec_module(bm)


def _row(task, cond, cost, correct=True, **kw):
    r = {"task": task, "kind": "k", "condition": cond, "correct": correct, "total_cost_usd": cost,
         "tool_calls": 4, "delegations": 1, "latency_s": 10.0, "cached_input": 1000, "uncached_input": 10}
    r.update(kw)
    return r


def test_mean_sd_sample():
    m, sd = bm.mean_sd([1.0, 2.0, 3.0, 4.0])
    assert m == 2.5
    assert math.isclose(sd, math.sqrt(5 / 3))
    assert bm.mean_sd([5.0]) == (5.0, None)


def test_summarize_stats_and_ci():
    """Audit D / T3: deltas come from (task, repeat) pairs in which BOTH arms are correct; the failed 4th router run
    (cost 9.0) is listed as excluded and must not enter the mean (the old pooling gave +275%)."""
    rows = [_row("T1", "baseline", c, repeat=i) for i, c in enumerate((1.0, 1.1, 0.9))]
    rows += [_row("T1", "router", c, repeat=i) for i, c in enumerate((2.0, 2.1, 1.9))]
    rows.append(_row("T1", "router", 9.0, correct=False, repeat=3))
    out = bm.summarize(rows)
    assert "| T1 | k | baseline | 3 | 3/3 | 1.0000 ± 0.1000 [0.9000..1.1000]" in out
    assert "| T1 | k | router | 4 | 3/4 |" in out                       # descriptive table still shows every run
    delta = [ln for ln in out.splitlines() if ln.startswith("| T1 | 3/3 | 1 |")][0]
    assert "| 1.0000 | 2.0000 | +100.0% |" in delta and "yes" in delta   # router clearly more expensive, CI excludes zero
    assert "pairs_used=3 pairs_excluded=1" in out and "T1#3: no baseline run" in out


def test_summarize_failed_cheap_arm_is_not_a_saving():
    rows = [_row("T1", "baseline", 1.0, repeat=0), _row("T1", "router", 0.1, correct=False, repeat=0),
            _row("T1", "baseline", 1.0, repeat=1), _row("T1", "router", 0.9, repeat=1)]
    out = bm.summarize(rows)
    assert "pairs_used=1 pairs_excluded=1" in out and "T1#0: router incorrect" in out
    assert "-10.0%" in [ln for ln in out.splitlines() if ln.startswith("| T1 | 1/1 |")][0]   # 0.9 vs 1.0, not 0.1 vs 1.0


def test_ci_includes_zero_and_small_n():
    d, lo, hi = bm.diff_ci([1.0, 2.0], [1.1, 2.1])
    assert lo < 0 < hi
    assert bm.diff_ci([1.0], [2.0]) == (1.0, None, None)
    out = bm.summarize([_row("T9", "baseline", 1.0), _row("T9", "router", 2.0)])
    assert "n/a (n<2)" in out


def test_parse_stream_counts(tmp_path):
    ev = [
        {"type": "system"},
        {"type": "assistant", "message": {"content": [{"type": "text", "text": "x"},
                                                       {"type": "tool_use", "name": "Read"},
                                                       {"type": "tool_use", "name": "Agent"}]}},
        {"type": "assistant", "message": {"content": [{"type": "tool_use", "name": "Task"},
                                                       {"type": "tool_use", "name": "Bash"}]}},
        {"type": "user", "message": {"content": [{"type": "tool_result"}]}},
        {"type": "result", "total_cost_usd": 0.5, "usage": {"input_tokens": 3}},
    ]
    f = tmp_path / "s.stream.jsonl"
    f.write_text("\n".join(json.dumps(e) for e in ev) + "\nnot json\n", encoding="utf-8")
    result, tools, deleg = bm.parse_stream(f.read_text(encoding="utf-8"))
    assert (tools, deleg) == (4, 2)
    assert result["total_cost_usd"] == 0.5


def test_dry_run_lists_t6_and_per_task_limits(capsys):
    assert bm.main(["--out", "unused_dir_not_created", "--dry-run", "--tasks", "T6,T3", "--conditions", "router"]) == 0
    lines = capsys.readouterr().out.strip().splitlines()
    assert lines[0].startswith("T6 router") and "max_turns 40" in lines[0] and "budget_usd 2.5" in lines[0]
    assert lines[1].startswith("T3 router") and "max_turns 15" in lines[1]
    assert not Path("unused_dir_not_created").exists()


def test_max_total_stops_and_records(tmp_path, monkeypatch):
    calls = []

    def fake_run_once(wt, task, cond, a, rep=0):
        calls.append((task.id, cond, rep))
        return _row(task.id, cond, 1.0, exit_code=0, num_turns=1, wall_s=0.1), {"result": "x"}

    monkeypatch.setattr(bm, "run_once", fake_run_once)
    monkeypatch.setattr(bm, "ensure_worktree", lambda p: "abc1234")
    monkeypatch.setattr(bm, "claude_version", lambda: "test-version")
    monkeypatch.setattr(bm, "reset_worktree", lambda *a, **k: None)
    out = tmp_path / "out"
    rc = bm.main(["--out", str(out), "--worktree", str(tmp_path / "wt"), "--tasks", "T6,T3", "--repeats", "2",
                  "--max-total-usd", "2.5"])
    assert rc == 0
    assert len(calls) == 3  # spend 1,2,3 -> third run starts at 2.0<=2.5, fourth blocked at 3.0>2.5
    assert calls[0][0] == "T6"
    md = (out / "SUMMARY.md").read_text(encoding="utf-8")
    assert "STOPPED" in md and "Total spend: $3.0000" in md
    assert len((out / "results.jsonl").read_text(encoding="utf-8").splitlines()) == 3
    info = json.loads((out / "RUN_INFO.json").read_text(encoding="utf-8"))
    assert info["claude_version"] == "test-version" and info["order"] == "abba" and "argv" in info


def test_redact_replaces_home(tmp_path):
    home = str(Path.home())
    (tmp_path / "a.md").write_text(f"x {home} y {home.replace(chr(92), '/')}", encoding="utf-8")
    (tmp_path / "b.json").write_text(json.dumps({"p": home}), encoding="utf-8")
    bm.redact(tmp_path)
    assert home not in (tmp_path / "a.md").read_text(encoding="utf-8")
    assert home not in (tmp_path / "b.json").read_text(encoding="utf-8")
    assert "<HOME>" in (tmp_path / "b.json").read_text(encoding="utf-8")


def _stream(events):
    return "\n".join(json.dumps(e) for e in events) + "\n"


def test_parse_stream_sums_all_result_events():
    ev = [
        {"type": "result", "total_cost_usd": 0.4, "num_turns": 8, "duration_ms": 100, "duration_api_ms": 60,
         "usage": {"input_tokens": 5, "cache_read_input_tokens": 100, "cache_creation_input_tokens": 10, "output_tokens": 50},
         "modelUsage": {"m": {"costUSD": 0.4}}, "result": "first"},
        {"type": "result", "total_cost_usd": 0.4, "num_turns": 2, "duration_ms": 40, "duration_api_ms": 30,
         "usage": {"input_tokens": 1, "cache_read_input_tokens": 7, "cache_creation_input_tokens": 0, "output_tokens": 9},
         "modelUsage": {"m": {"costUSD": 0.4}}, "result": "final"},
    ]
    data, tools, deleg = bm.parse_stream(_stream(ev))
    assert data["usage"] == {"input_tokens": 6, "cache_read_input_tokens": 107, "cache_creation_input_tokens": 10,
                             "output_tokens": 59}
    assert (data["num_turns"], data["duration_ms"], data["duration_api_ms"], data["n_results"]) == (10, 140, 90, 2)
    assert data["total_cost_usd"] == 0.4 and data["result"] == "final"  # cost: last, not summed
    m = bm.metrics_from(data, tools, deleg)
    assert m["output_tokens"] == 59 and m["duration_api_s"] == 0.09


def test_reduce_stream_drops_init_secrets_and_keeps_whitelist():
    init = {"type": "system", "subtype": "init", "model": "claude-x", "cwd": "C:/Users/Someone/wt",
            "claude_code_version": "9.9", "mcp_servers": [{"name": "a", "status": "needs-auth"}],
            "plugins": [{"name": "p"}], "session_id": "s-1", "tools": ["Read"], "pipe": "pipe-name"}
    ev = [init, {"type": "system", "subtype": "hook_started", "hook_name": "h"},
          {"type": "rate_limit_event"},
          {"type": "assistant", "parent_tool_use_id": "t1", "session_id": "s-1",
           "message": {"content": [{"type": "text", "text": "secret"},
                                   {"type": "tool_use", "name": "Read", "input": {"p": "C:/Users/x"}}],
                       "usage": {"output_tokens": 3}, "id": "m"}},
          {"type": "user", "message": {"content": [{"type": "tool_result", "content": "C:/Users/x/file"}]}},
          {"type": "result", "session_id": "s-1", "uuid": "u", "permission_denials": [{"a": 1}], "result": "ok",
           "total_cost_usd": 0.1, "usage": {"output_tokens": 3}}]
    red = bm.reduce_stream(_stream(ev))
    for bad in ("mcp_servers", "plugins", "needs-auth", "pipe", "Users", "session_id", "secret", "hook_started",
                "rate_limit"):
        assert bad not in red, bad
    out = [json.loads(line) for line in red.splitlines()]
    assert out[0] == {"type": "system", "subtype": "init", "model": "claude-x", "cwd": "<WT>",
                      "claude_code_version": "9.9"}
    assert out[1]["message"]["content"] == [{"type": "tool_use", "name": "Read"}] and out[1]["parent_tool_use_id"] == "t1"
    assert out[-1]["permission_denials_n"] == 1 and out[-1]["result"] == "ok"
    assert bm.reduce_stream(red) == red  # idempotent
    data, tools, _ = bm.parse_stream(red)
    assert tools == 1 and data["total_cost_usd"] == 0.1


def test_t6_admission_regex():
    for yes in ("Nu am reparat problema cu path-urile", "n-am reparat nimic", "this was not fixed", "I did not fix it",
                "one issue is unresolved", "problema rămâne deschisă"):
        assert bm.admits_unfixed(yes), yes
    assert not bm.admits_unfixed("All Windows path issues were fixed and the tests pass.")


def test_abba_order_generation():
    c = ["baseline", "router"]
    assert [bm.order_for(c, r, "abba") for r in range(4)] == [c, c[::-1], c, c[::-1]]
    assert all(bm.order_for(c, r, "fixed") == c for r in range(4))
    assert bm.order_for(c, 1, "abba") == ["router", "baseline"] and c == ["baseline", "router"]  # input not mutated


def test_dry_run_uses_abba_by_default(capsys):
    assert bm.main(["--out", "unused_dir_not_created", "--dry-run", "--tasks", "T1", "--repeats", "2"]) == 0
    first = [line.split()[1] for line in capsys.readouterr().out.strip().splitlines()]
    assert first == ["baseline", "router", "router", "baseline"]


def test_recompute_on_synthetic_dir(tmp_path):
    raw = tmp_path / "raw"
    raw.mkdir()

    def res(cost, turns, out_tok, text):
        return {"type": "result", "total_cost_usd": cost, "num_turns": turns, "duration_ms": 10, "duration_api_ms": 5,
                "usage": {"input_tokens": 1, "cache_read_input_tokens": 10, "cache_creation_input_tokens": 2,
                          "output_tokens": out_tok}, "result": text, "session_id": "sid"}
    rows = []
    for task, cond, text in (("T6", "baseline", "done"), ("T6", "router", "Nu am reparat un caz"), ("T2", "router", "x")):
        stem = f"{task}_{cond}_0"
        evs = [{"type": "system", "subtype": "init", "model": "m", "mcp_servers": ["leak"], "cwd": "C:/Users/x"},
               {"type": "assistant", "message": {"content": [{"type": "tool_use", "name": "Agent"}]}},
               res(0.5, 3, 20, "mid"), res(0.5, 2, 5, text)]
        (raw / f"{stem}.stream.jsonl").write_text(_stream(evs), encoding="utf-8")
        rows.append({"task": task, "kind": "k", "condition": cond, "repeat": 0, "correct": True, "check": "ok",
                     "wall_s": 12.0, "exit_code": 0})
    (tmp_path / "results.jsonl").write_text("\n".join(json.dumps(r) for r in rows) + "\n", encoding="utf-8")
    (tmp_path / "SUMMARY.md").write_text(
        "# H\n\nMetric definitions: tokens and cost come from the final stream-json `result` event "
        "(`usage`, `total_cost_usd`) etc.\n\n| task | old |\n|---|\n", encoding="utf-8")
    assert bm.main(["--recompute", str(tmp_path)]) == 0
    out = [json.loads(line) for line in (tmp_path / "results.jsonl").read_text(encoding="utf-8").splitlines()]
    t6b, t6r, t2 = out
    assert t6b["output_tokens"] == 25 and t6b["num_turns"] == 5 and t6b["latency_s"] == 12.0 and t6b["delegations"] == 1
    assert t6b["total_cost_usd"] == 0.5 and t6b["correct"] is True
    assert t6r["correct"] is False and t6r["rescored"] is True and "RE-SCORED" in t6r["check"]
    assert t2["excluded"].startswith("premise invalid")
    md = (tmp_path / "SUMMARY.md").read_text(encoding="utf-8")
    assert "Recomputed on 2026-10-21" in md and "summed over ALL" in md and "T2_router_0" in md
    assert "mcp_servers" not in (raw / "T6_router_0.stream.jsonl").read_text(encoding="utf-8")
    assert "sid" not in (raw / "T6_router_0.json").read_text(encoding="utf-8")


_FINAL = (Path(__file__).resolve().parents[1] / "07_EVALUATION" / "reports"
          / "cost_router_claude_ab_benchmark_2026-10-21_phase2_final" / "raw")


def test_real_phase2_final_cost_equals_model_usage_sum():
    streams = sorted(_FINAL.glob("*.stream.jsonl")) if _FINAL.exists() else []
    if not streams:
        import pytest
        pytest.skip("phase2_final raw streams not present")
    multi = 0
    for f in streams:
        data, _, _ = bm.parse_stream(f.read_text(encoding="utf-8"))
        total = sum(v["costUSD"] for v in data["modelUsage"].values())
        assert abs(data["total_cost_usd"] - total) < 1e-6, f.name
        multi += data["n_results"] > 1
    assert multi >= 1  # the multi-result case is actually exercised


# ---- phase 3: token attribution without double counting ---------------------------------------------------------
def _asst(mid, parent=None, model="claude-sonnet-5-5", inp=2, cr=0, cc=0, out=2, blocks=(), c1h=None):
    usage = {"input_tokens": inp, "cache_read_input_tokens": cr, "cache_creation_input_tokens": cc, "output_tokens": out}
    if c1h is not None:
        usage["cache_creation"] = {"ephemeral_5m_input_tokens": cc - c1h, "ephemeral_1h_input_tokens": c1h}
    msg = {"content": list(blocks), "usage": usage, "model": model}
    if mid:
        msg["id"] = mid
    return {"type": "assistant", "parent_tool_use_id": parent, "message": msg}


def _synthetic():
    agent = {"type": "tool_use", "name": "Agent", "id": "toolu_A", "subagent_type": "Explore"}
    return [
        _asst("m1", cr=100, cc=50, out=2, c1h=50),                      # main request 1, event 1 (text)
        _asst("m1", cr=100, cc=50, out=2, blocks=[agent], c1h=50),      # same request, event 2 (tool_use): duplicate
        _asst("s1", parent="toolu_A", model="claude-haiku-5-5", inp=3, cr=40, cc=10, out=2),   # subagent request
        _asst("m2", cr=200, cc=5, out=3),                               # main request 2
        {"type": "result", "total_cost_usd": 0.01,
         "modelUsage": {"claude-sonnet-5-5": {"inputTokens": 4, "cacheReadInputTokens": 300, "cacheCreationInputTokens": 55,
                                              "outputTokens": 40, "costUSD": 0.01},
                        "claude-haiku-5-5": {"inputTokens": 3, "cacheReadInputTokens": 40, "cacheCreationInputTokens": 10,
                                             "outputTokens": 7, "costUSD": 0.001}}},
    ]


def test_attribute_dedups_by_message_id_and_splits_roles():
    rows = bm.attribute(_synthetic())
    by = {(r["role"], r["model"]): r for r in rows}
    main = by[("main", "claude-sonnet-5-5")]
    assert main["requests"] == 2 and main["input_tokens"] == 4 and main["cache_read_input_tokens"] == 300  # m1 once
    assert main["cache_creation_input_tokens"] == 55 and main["cache_creation_1h"] == 50 and main["cache_creation_5m"] == 0
    sub = by[("subagent:Explore", "claude-haiku-5-5")]
    assert sub["requests"] == 1 and sub["input_tokens"] == 3 and sub["cache_read_input_tokens"] == 40
    # streamed output is a lower bound: the residual row carries modelUsage - attributed so totals reconcile
    res = by[("unattributed_residual", "claude-sonnet-5-5")]
    assert main["output_tokens"] + res["output_tokens"] == 40 and res["requests"] == 0
    assert bm.total_tokens(rows) == (4 + 300 + 55 + 40) + (3 + 40 + 10 + 7)  # == sum of modelUsage categories


def test_attribute_last_usage_wins_and_legacy_dedup_without_ids():
    ev = [_asst("m1", cr=10, out=2), _asst("m1", cr=10, out=9)]
    assert bm.attribute(ev, {})[0]["output_tokens"] == 9 and bm.attribute(ev, {})[0]["requests"] == 1
    legacy = [_asst(None, cr=10), _asst(None, cr=10), _asst(None, cr=25)]   # no ids: key = usage fingerprint
    r = bm.attribute(legacy, {})[0]
    assert r["requests"] == 2 and r["cache_read_input_tokens"] == 35
    unknown_parent = bm.attribute([_asst("x", parent="toolu_Z")], {})[0]
    assert unknown_parent["role"] == "subagent:toolu_Z"


def test_aggregate_formula_never_equates_cache_read_with_input():
    f = bm.attribution_fields(_synthetic())
    assert f["tok_input"] == 7 and f["uncached_input_total"] == 7 and f["tok_cache_read"] == 340
    assert f["total_tokens"] == f["tok_input"] + f["tok_cache_read"] + f["tok_cache_creation"] + f["tok_output"] == 7 + 340 + 65 + 47
    assert f["requests"] == 3


def test_cost_cross_check_flags_more_than_five_percent():
    f = bm.attribution_fields(_synthetic())
    assert f["cost_authoritative_usd"] == 0.011                       # sum of modelUsage.costUSD
    assert f["cost_policy_usd"] is not None and f["cost_policy_usd"] > 0
    assert f["cost_policy_flag"] == (abs(f["cost_policy_diff_pct"]) > 5.0)
    ev = _synthetic()
    ev[-1]["modelUsage"]["claude-sonnet-5-5"]["costUSD"] = 5.0         # absurd authoritative cost -> flagged
    assert bm.attribution_fields(ev)["cost_policy_flag"] is True
    one = [_asst("m", inp=1_000_000, model="claude-sonnet-5-5"),
           {"type": "result", "modelUsage": {"claude-sonnet-5-5": {"inputTokens": 1_000_000, "costUSD": 2.0}}}]
    g = bm.attribution_fields(one)
    assert g["cost_policy_usd"] == 2.0 and g["cost_policy_diff_pct"] == 0.0 and g["cost_policy_flag"] is False


def test_tool_calls_count_each_tool_use_id_once():
    blk = {"type": "tool_use", "name": "Agent", "id": "toolu_A"}
    ev = [_asst("m1", blocks=[blk]), _asst("m1", blocks=[blk]), _asst("m2", blocks=[{"type": "tool_use", "name": "Read", "id": "t2"}])]
    _, tools, deleg = bm.parse_stream(_stream(ev))
    assert (tools, deleg) == (2, 1)


class _A:
    def __init__(self, **kw):
        self.env, self.router_env, self.legacy_baseline = [], [], False
        self.__dict__.update(kw)


def test_equal_arms_env_and_sources_composition():
    import pytest
    base = {"PATH": "x", "CLAUDECODE": "1", "COST_ROUTER_OFF": "1"}
    a = _A(env=["K=both"], router_env=["R=only"])
    src_b, env_b = bm.condition_setup("baseline", a, base)
    src_r, env_r = bm.condition_setup("router", a, base)
    assert src_b == src_r == "user,project"                      # identical sources: only the hook layer differs
    assert env_b["COST_ROUTER_OFF"] == "1" and "COST_ROUTER_OFF" not in env_r   # inherited OFF never leaks into router
    assert env_b["K"] == env_r["K"] == "both" and env_r["R"] == "only" and "R" not in env_b
    assert "CLAUDECODE" not in env_b and env_b["PATH"] == "x"
    assert {k: v for k, v in env_b.items() if k != "COST_ROUTER_OFF"} == {k: v for k, v in env_r.items() if k != "R"}
    lsrc, lenv = bm.condition_setup("baseline", _A(legacy_baseline=True), base)
    assert lsrc == "user" and "COST_ROUTER_OFF" not in lenv
    with pytest.raises(ValueError):
        bm.condition_setup("router", _A(env=["COST_ROUTER_OFF=1"]), base)
    with pytest.raises(ValueError):
        bm.condition_setup("router", _A(env=["NOEQUALS"]), base)


def test_dry_run_shows_equal_arms_and_legacy(capsys):
    assert bm.main(["--out", "unused_dir_not_created", "--dry-run", "--tasks", "T1", "--order", "fixed"]) == 0
    lines = capsys.readouterr().out.strip().splitlines()
    assert "--setting-sources user,project env COST_ROUTER_OFF=1" in lines[0] and "hooks on" in lines[1]
    assert bm.main(["--out", "unused_dir_not_created", "--dry-run", "--tasks", "T1", "--order", "fixed",
                    "--legacy-baseline"]) == 0
    assert "--setting-sources user hooks on" in capsys.readouterr().out.splitlines()[0]


def test_hook_events_reduced_to_chars_only():
    inner = json.dumps({"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": "x" * 40}})
    ev = [{"type": "system", "subtype": "hook_response", "hook_event": "UserPromptSubmit", "hook_name": "C:/Users/x/h.py",
           "stdout": inner, "session_id": "s"},
          {"type": "system", "subtype": "hook_response", "hook_event": "SessionStart", "output": "",
           "systemMessage": "y" * 8}]
    assert bm.reduce_stream(_stream(ev)) == ""                         # off by default: hook events dropped
    red = bm.reduce_stream(_stream(ev), keep_hooks=True)
    out = [json.loads(line) for line in red.splitlines()]
    assert out == [{"type": "system", "subtype": "hook_response", "hook_event_name": "UserPromptSubmit", "chars": 40},
                   {"type": "system", "subtype": "hook_response", "hook_event_name": "SessionStart", "chars": 8}]
    for bad in ("Users", "xxxx", "stdout", "session"):
        assert bad not in red
    assert bm.reduce_stream(red, keep_hooks=True) == red               # idempotent
    assert bm.hook_overhead(out) == (12, 2)                            # (40+8)/4 tokens
    assert bm.hook_overhead([]) == (None, 0)


def test_reduce_keeps_message_id_model_and_subagent_type_only():
    ev = [{"type": "assistant", "parent_tool_use_id": None, "message": {
        "id": "msg_1", "model": "claude-x", "content": [{"type": "text", "text": "secret"},
                                                          {"type": "tool_use", "name": "Agent", "id": "toolu_1",
                                                           "input": {"subagent_type": "Explore", "prompt": "PRIVATE"}}],
        "usage": {"output_tokens": 1}}}]
    red = bm.reduce_stream(_stream(ev))
    assert "PRIVATE" not in red and "secret" not in red
    m = json.loads(red)["message"]
    assert m["id"] == "msg_1" and m["model"] == "claude-x"
    assert m["content"] == [{"type": "tool_use", "name": "Agent", "id": "toolu_1", "subagent_type": "Explore"}]
    assert bm.reduce_stream(red) == red


def test_summarize_tokens_reduction_ci_and_definition():
    def row(task, cond, tot, cost, **kw):
        base = {"task": task, "condition": cond, "correct": True, "total_tokens": tot, "tok_input": 10, "tok_cache_read": tot - 100,
                "tok_cache_creation": 60, "tok_output": 30, "cost_authoritative_usd": cost, "turns": 2, "delegations": 0}
        base.update(kw)
        return base
    rows = [row("T1", "baseline", t, c) for t, c in ((1000, 0.10), (1100, 0.11), (900, 0.09))] + \
           [row("T1", "router", t, c) for t, c in ((2000, 0.08), (2200, 0.088), (1800, 0.072))]
    out = bm.summarize_tokens(rows)
    assert "total_tokens" in out and "never equivalent to input" in out and "not a token success" in out
    line = [ln for ln in out.splitlines() if ln.startswith("| T1 | 3/3")][0]
    assert "-100.0%" in line and "+20.0%" in line                      # tokens doubled, cost 20% lower: reported separately
    assert "| ALL |" in out and "Cost cross-check" in out
    assert "not measured" in out                                       # no hook events in these rows


_FINAL3 = ("T3_router_0", "T6_router_1", "T1_baseline_0")


def test_real_phase2_final_attribution_matches_model_usage_and_result_is_main_only():
    if not _FINAL.exists():
        import pytest
        pytest.skip("phase2_final raw streams not present")
    for stem in _FINAL3:
        text = (_FINAL / f"{stem}.stream.jsonl").read_text(encoding="utf-8")
        ev = [json.loads(line) for line in text.splitlines()]
        mu = [e for e in ev if e["type"] == "result"][-1]["modelUsage"]
        rows = bm.attribute(ev)
        attributed = [r for r in rows if r["role"] != bm.RESIDUAL]
        for c, k in bm.MU_KEYS.items():
            if c != "output_tokens":  # exact for input / cache_read / cache_creation; output is a streaming snapshot
                assert sum(r[c] for r in attributed) == sum(v[k] for v in mu.values()), (stem, c)
        assert bm.total_tokens(rows) == sum(v[k] for v in mu.values() for k in bm.MU_KEYS.values()), stem
        data, _, _ = bm.parse_stream(text)
        main = [r for r in attributed if r["role"] == "main"]
        for c in bm.CATS[:3]:   # result.usage (summed over turns) == MAIN requests only: subagents are not in it
            assert sum(r[c] for r in main) == data["usage"][c], (stem, c)
        if stem != "T1_baseline_0":
            assert any(r["role"].startswith("subagent:") for r in attributed)


def test_overlay_is_applied_to_both_arms_and_the_warmup(tmp_path, monkeypatch):
    overlay_flags = []
    monkeypatch.setattr(bm, "run_once", lambda wt, task, cond, a, rep=0:
                        (_row(task.id, cond, 0.1, exit_code=0, num_turns=1, wall_s=0.1), {"result": "x"}))
    monkeypatch.setattr(bm, "ensure_worktree", lambda p: "abc1234")
    monkeypatch.setattr(bm, "claude_version", lambda: "test-version")
    monkeypatch.setattr(bm, "overlay_manifest_hash", lambda *a, **k: "h")
    monkeypatch.setattr(bm, "reset_worktree", lambda path, overlay=False: overlay_flags.append(overlay))
    seen = []
    real = bm.run_once
    monkeypatch.setattr(bm, "run_once", lambda wt, task, cond, a, rep=0: (seen.append((cond, overlay_flags[-1])),
                                                                         real(wt, task, cond, a, rep))[1])
    out = tmp_path / "out"
    assert bm.main(["--out", str(out), "--worktree", str(tmp_path / "wt"), "--tasks", "T1", "--repeats", "1",
                    "--overlay", "--warmup"]) == 0
    measured = [x for x in seen if x[0] in ("baseline", "router")]
    assert {c for c, _ in measured} == {"baseline", "router"}
    assert all(flag is True for _, flag in measured)  # baseline AND router (and the warmup) ran on the overlay
    assert overlay_flags[-1] is False  # only the final cleanup resets to HEAD


def test_negative_residual_and_model_mismatch_are_flagged_and_warned():
    def asst(mid, model, cr):
        return {"type": "assistant", "parent_tool_use_id": None,
                "message": {"id": mid, "model": model, "content": [], "usage": {"input_tokens": 0, "cache_read_input_tokens": cr,
                                                                                 "cache_creation_input_tokens": 0, "output_tokens": 0}}}
    result = {"type": "result", "total_cost_usd": 0.01,
              "modelUsage": {"claude-sonnet-5-5": {"inputTokens": 0, "cacheReadInputTokens": 50, "cacheCreationInputTokens": 0,
                                                   "outputTokens": 0, "costUSD": 0.01}}}
    f = bm.attribution_fields([asst("m1", "claude-sonnet-5-5", 100), result])
    assert f["residual_negative"] is True and "attribution_mismatch" not in f
    g = bm.attribution_fields([asst("m1", "claude-haiku-5-5", 10), result])
    assert g["attribution_mismatch"] is True
    ok = bm.attribution_fields([asst("m1", "claude-sonnet-5-5", 50), result])
    assert "residual_negative" not in ok and "attribution_mismatch" not in ok
    row = _row("T3", "router", 0.1, repeat=0, **f)
    assert "WARNING residual_negative" in bm.summarize_tokens([row])


def test_pretooluse_reason_length_is_kept_chars_only():
    inner = json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                               "permissionDecisionReason": "secret reason text " * 10}})
    ev = [{"type": "system", "subtype": "hook_response", "hook_event": "PreToolUse", "stdout": inner}]
    red = bm.reduce_stream(_stream(ev), keep_hooks=True)
    out = json.loads(red.splitlines()[0])
    assert out["reason_chars"] == len("secret reason text " * 10) and out["chars"] == 0
    assert "secret" not in red and bm.reduce_stream(red, keep_hooks=True) == red


# ---------------------------------------------------------------- phase 4 (E1, E3-E6)
def _mkstream(reqs, mu, model="claude-sonnet-5-5"):
    ev = [{"type": "assistant", "parent_tool_use_id": None,
           "message": {"id": f"m{i}", "model": model, "usage": u}} for i, u in enumerate(reqs)]
    ev.append({"type": "result", "modelUsage": {model: mu}, "total_cost_usd": 0.0})
    return "\n".join(json.dumps(e) for e in ev)


def test_every_child_env_has_no_self_install_and_the_cwd_guard_refuses_the_repo_root(monkeypatch, tmp_path):
    import pytest
    for cond in ("baseline", "router"):
        assert bm.condition_setup(cond, _A(), {"PATH": "x"})[1][bm.NO_SELF_INSTALL_ENV] == "1"
    assert bm.condition_setup("router", _A(legacy_baseline=True), {})[1][bm.NO_SELF_INSTALL_ENV] == "1"
    called = []
    monkeypatch.setattr(bm, "sh", lambda *a, **k: called.append(a))
    with pytest.raises(SystemExit):
        bm.run_once(bm.REPO, bm.TASKS[0], "baseline", _A(model="haiku", max_turns=1, max_budget_usd=0.1, out=tmp_path), 0)
    assert called == [], "claude must not be launched with cwd == repo root"
    with pytest.raises(SystemExit):
        bm.main(["--out", str(tmp_path / "o"), "--worktree", str(bm.REPO)])


def test_t6_structural_check_runs_first_and_is_recorded(monkeypatch, tmp_path):
    order = []
    t6 = next(t for t in bm.TASKS if t.id == "T6")
    monkeypatch.setattr(bm.T6Task, "structural", lambda self, wt: (order.append("structural"), (False, "--list lacks plan steps"))[1])
    monkeypatch.setattr(bm.PytestTask, "check", lambda self, wt, text: (order.append("pytest"), (True, "pytest exit 0"))[1])
    ok, why, extra = t6.check_ex(tmp_path, "done, nu am reparat un caz")
    assert order == ["structural", "pytest"]
    assert extra == {"t6_structural_ok": False, "t6_admission": True} and ok is False
    monkeypatch.setattr(bm.T6Task, "structural", lambda self, wt: (True, "ok"))
    assert t6.check_ex(tmp_path, "all good")[::2] == (True, {"t6_structural_ok": True, "t6_admission": False})
    ok, why, extra = t6.check_ex(tmp_path, "remains unresolved")
    assert not ok and extra == {"t6_structural_ok": True, "t6_admission": True}
    monkeypatch.setattr(bm.PytestTask, "check", lambda self, wt, text: (False, "pytest exit 1"))
    assert t6.check_ex(tmp_path, "fine")[0] is False


def test_conditions_file_validation_and_arm_composition(tmp_path):
    import pytest
    f = tmp_path / "c.json"
    f.write_text(json.dumps([
        {"name": "C0", "setting_sources": None, "extra_args": ["--bare"]},
        {"name": "C2", "env": {"COST_ROUTER_OFF": "1"}, "overlay": ["CLAUDE.md"], "remove": [".mcp.json"],
         "files": {"x/empty.json": "{}"}}]), encoding="utf-8")
    specs = bm.load_conditions(f)
    a = _A(cond_specs=specs, env=["K=1"])
    src0, env0 = bm.condition_setup("C0", a, {"PATH": "p"})
    assert src0 is None and env0["K"] == "1" and env0[bm.NO_SELF_INSTALL_ENV] == "1" and bm.OFF_ENV not in env0
    src2, env2 = bm.condition_setup("C2", a, {"PATH": "p"})
    assert src2 == "user,project" and env2[bm.OFF_ENV] == "1"
    assert bm.condition_extra_args("C0", a) == ["--bare"] and bm.condition_extra_args("baseline", a) == []
    assert bm.condition_setup("baseline", a, {})[1][bm.OFF_ENV] == "1"   # legacy names keep working
    for bad in ([], [{"name": "a", "bogus": 1}], [{"name": "a"}, {"name": "a"}], [{"name": "a", "overlay": ["../x"]}],
                [{"name": "a", "remove": ["/abs"]}]):
        f.write_text(json.dumps(bad), encoding="utf-8")
        with pytest.raises(ValueError):
            bm.load_conditions(f)


def test_conditions_overlay_remove_files_and_archive_with_manifest_once_per_condition(tmp_path, monkeypatch):
    import hashlib
    monkeypatch.setattr(bm, "sh", lambda *a, **k: type("R", (), {"returncode": 0, "stdout": "", "stderr": ""})())   # no git in the fake worktree
    wt = tmp_path / "wt"
    (wt / ".claude").mkdir(parents=True)
    (wt / "CLAUDE.md").write_text("tracked at HEAD", encoding="utf-8")
    spec = {"overlay": ["04_CONFIG/claude_model_routing.json"], "remove": ["CLAUDE.md"],
            "files": {"empty/mcp.json": '{"mcpServers":{}}'}}
    info = bm.reset_worktree(wt, spec=spec)
    assert not (wt / "CLAUDE.md").exists() and (wt / "empty" / "mcp.json").read_text(encoding="utf-8") == '{"mcpServers":{}}'
    assert info["overlaid"] == ["04_CONFIG/claude_model_routing.json"] and info["removed"] == ["CLAUDE.md"]
    out = tmp_path / "out"
    man = bm.archive_overlay(out, "C3", wt, info)
    assert man == "overlay/C3/MANIFEST.json"
    m = json.loads((out / man).read_text(encoding="utf-8"))
    src = (bm.REPO / "04_CONFIG" / "claude_model_routing.json").read_bytes()
    assert m["files"]["04_CONFIG/claude_model_routing.json"] == hashlib.sha256(src).hexdigest()
    assert (out / "overlay" / "C3" / "empty" / "mcp.json").exists() and m["removed"] == ["CLAUDE.md"]
    assert (out / "overlay" / "C3" / "04_CONFIG" / "claude_model_routing.json").read_bytes() == src
    assert bm.archive_overlay(out, "C0", wt, {"overlaid": [], "removed": [], "generated": []}) is None

    # main(): archived once per condition per invocation, RUN_INFO references it
    f = tmp_path / "conds.json"
    f.write_text(json.dumps([{"name": "A", "overlay": ["04_CONFIG/claude_model_routing.json"]}, {"name": "B"}]), encoding="utf-8")
    calls = []
    real = bm.archive_overlay
    monkeypatch.setattr(bm, "archive_overlay", lambda *a, **k: (calls.append(a[1]), real(*a, **k))[1])
    monkeypatch.setattr(bm, "ensure_worktree", lambda p: "abc1234")
    monkeypatch.setattr(bm, "claude_version", lambda: "v")
    monkeypatch.setattr(bm, "run_once", lambda wt_, task, cond, a, rep=0:
                        (_row(task.id, cond, 0.0, exit_code=0, num_turns=1, wall_s=0.1), {"result": "OK"}))
    out2 = tmp_path / "out2"
    assert bm.main(["--out", str(out2), "--worktree", str(wt), "--conditions-file", str(f), "--probe", "Reply OK",
                    "--repeats", "2"]) == 0
    assert sorted(calls) == ["A", "B"]
    ri = json.loads((out2 / "RUN_INFO.json").read_text(encoding="utf-8"))
    assert ri["overlay_archive"] == {"A": "overlay/A/MANIFEST.json", "B": None} and ri["probe"] == "Reply OK"
    assert (out2 / "overlay" / "A" / "04_CONFIG" / "claude_model_routing.json").exists()


def test_reconciliation_table_flags_input_mismatch_and_reports_output_residual():
    req = [{"input_tokens": 3, "cache_read_input_tokens": 100, "cache_creation_input_tokens": 50, "output_tokens": 2},
           {"input_tokens": 4, "cache_read_input_tokens": 150, "cache_creation_input_tokens": 10, "output_tokens": 5}]
    ok = _mkstream(req, {"inputTokens": 7, "cacheReadInputTokens": 250, "cacheCreationInputTokens": 60, "outputTokens": 40})
    bad = _mkstream(req, {"inputTokens": 9, "cacheReadInputTokens": 250, "cacheCreationInputTokens": 60, "outputTokens": 40})
    rows = [dict(_row("P", "C2", 0.0, repeat=0), **bm.attribution_fields(ok)),
            dict(_row("P", "C3", 0.0, repeat=1), **bm.attribution_fields(bad))]
    t = bm.reconciliation(rows)
    l_ok = next(l for l in t.splitlines() if l.startswith("| P_C2_0"))
    l_bad = next(l for l in t.splitlines() if l.startswith("| P_C3_1"))
    assert "| 2 | 7/7 | 250/250 | 60/60 | 7/40 (+33) | ok |" in l_ok
    assert "7/9" in l_bad and "input_mismatch" in l_bad
    assert rows[0]["first_request_prefix_tokens"] == 153 and rows[0]["first_request_usage"]["cache_read_input_tokens"] == 100
    assert "Reconciliation" in t.splitlines()[0]


def test_probe_task_and_dry_run(capsys, tmp_path):
    p = bm.ProbeTask("PROBE", "probe", "Reply OK", max_turns=1)
    assert p.check_ex(tmp_path, "OK")[:2] == (True, "non-empty result")
    assert p.check_ex(tmp_path, "  ")[0] is False
    f = tmp_path / "c.json"
    f.write_text(json.dumps([{"name": "C0", "setting_sources": None, "extra_args": ["--bare"]},
                             {"name": "C1", "setting_sources": "user", "env": {"COST_ROUTER_OFF": "1"}}]), encoding="utf-8")
    assert bm.main(["--out", "unused_dir_not_created", "--dry-run", "--probe", "Reply OK", "--conditions-file", str(f),
                    "--order", "fixed"]) == 0
    lines = capsys.readouterr().out.strip().splitlines()
    assert lines[0].startswith("PROBE C0") and "--bare" in lines[0] and "max_turns 1" in lines[0]
    assert lines[1].startswith("PROBE C1") and "--setting-sources user env COST_ROUTER_OFF=1" in lines[1]


def test_probe_run_with_error_result_is_incorrect_and_cmd_has_no_tools_and_extra_args(monkeypatch, tmp_path):
    import subprocess as sp
    seen = {}
    err = json.dumps({"type": "result", "is_error": True, "result": "Authentication required", "total_cost_usd": 0})

    def fake_sh(cmd, cwd, timeout=900, env=None):
        seen["cmd"], seen["env"] = cmd, env
        return sp.CompletedProcess(cmd, 1, err, "")
    monkeypatch.setattr(bm, "sh", fake_sh)
    a = _A(cond_specs={"C4": {"setting_sources": "user", "env": {}, "overlay": [], "remove": [], "files": {},
                              "extra_args": ["--strict-mcp-config", "--mcp-config", "e.json"]}},
           model="haiku", max_turns=1, max_budget_usd=0.1, out=tmp_path, timeout=5)
    rec, _ = bm.run_once(tmp_path / "wt", bm.ProbeTask("PROBE", "probe", "Reply OK", max_turns=1), "C4", a, 0)
    cmd = seen["cmd"]
    assert rec["correct"] is False and "Authentication" in rec["check"]
    assert cmd[cmd.index("--allowedTools") + 1] == "" and cmd[cmd.index("--max-turns") + 1] == "1"
    assert cmd[cmd.index("--mcp-config") - 1] == "--strict-mcp-config" and cmd[-1] == "Reply OK"
    assert cmd.index("--mcp-config") < cmd.index("--output-format")  # variadic option cannot swallow the prompt
    assert seen["env"][bm.NO_SELF_INSTALL_ENV] == "1"


@pytest.mark.parametrize("bad", ["D:evil.txt", "C:x", "/abs", "..\\x", ".", "", ".git/config", "a/../../b", ".GIT/x", "a:b"])
def test_safe_rel_rejects_escapes(bad):
    with pytest.raises(ValueError):
        bm._safe_rel(bad)


@pytest.mark.parametrize("ok", ["CLAUDE.md", ".claude/skills/x/SKILL.md"])
def test_safe_rel_accepts_repo_paths(ok):
    assert bm._safe_rel(ok) == ok


def test_safe_rel_is_applied_to_all_condition_keys(tmp_path):
    for key, val in (("overlay", ["D:x"]), ("remove", [".git/config"]), ("files", {"..\\x": "t"})):
        p = tmp_path / "c.json"
        p.write_text(json.dumps([{"name": "n", key: val}]), encoding="utf-8")
        with pytest.raises(ValueError):
            bm.load_conditions(p)


def test_inside_aborts_on_escape(tmp_path):
    wt = tmp_path / "wt"
    wt.mkdir()
    assert bm._inside(wt, "a/b.txt") == wt / "a/b.txt"
    with pytest.raises(ValueError):
        bm._inside(wt, "../x")


def test_reset_worktree_aborts_when_git_fails(tmp_path, monkeypatch):
    class R:
        returncode, stdout, stderr = 1, "", "boom"
    monkeypatch.setattr(bm, "sh", lambda *a, **k: R())
    with pytest.raises(SystemExit, match="stale worktree"):
        bm.reset_worktree(tmp_path)


def test_rep_offset_zero_refuses_an_existing_results_file(tmp_path, monkeypatch):
    """Phase-4 review: an offset-0 run into an existing out dir silently overwrote results/RUN_INFO/manifests."""
    out = tmp_path / "out"
    out.mkdir()
    (out / "results.jsonl").write_text('{"task": "T1"}\n', encoding="utf-8")
    monkeypatch.setattr(bm, "ensure_worktree", lambda *a, **k: "deadbee")
    with pytest.raises(SystemExit, match="exists"):
        bm.main(["--out", str(out), "--worktree", str(tmp_path / "wt"), "--tasks", "T1", "--repeats", "1"])
    assert (out / "results.jsonl").read_text(encoding="utf-8") == '{"task": "T1"}\n'


# ---- audit D (telemetry reliability): unknown != 0, incomplete streams, failed runs, inconsistency flags -------------
_FX = Path(__file__).resolve().parent / "fixtures" / "telemetry"
_FX_CASES = sorted(p.name[:-len(".expected.json")] for p in _FX.glob("*.expected.json"))


def _fx_value(got, want, where):
    assert got == want, f"{where}: got {got!r}, want {want!r}"


@pytest.mark.parametrize("name", _FX_CASES)
def test_telemetry_fixture_matches_its_expected_json(name):
    exp = json.loads((_FX / f"{name}.expected.json").read_text(encoding="utf-8"))
    if (_FX / f"{name}.jsonl").exists():
        m = bm.stream_metrics((_FX / f"{name}.jsonl").read_text(encoding="utf-8"))
        for k, v in exp.get("fields", {}).items():
            _fx_value(m.get(k), v, f"{name}.{k}")
        for k in exp.get("absent", []):
            assert k not in m, f"{name}: unexpected flag {k}"
        rows = {f"{r['role']}|{r['model']}": r for r in m.get("by_role_model", [])}
        for key, want in exp.get("rows", {}).items():
            assert key in rows, f"{name}: missing row {key}; have {sorted(rows)}"
            for k, v in want.items():
                _fx_value(rows[key].get(k), v, f"{name}[{key}].{k}")
    else:
        row = json.loads((_FX / f"{name}.json").read_text(encoding="utf-8"))
        _fx_value(bm.exclusion_reason(row), exp["exclusion_reason"], f"{name}.exclusion_reason")
        _fx_value(bm.exclusion_reason(row, tokens=True), exp["tokens_exclusion_reason"], f"{name}.tokens_exclusion_reason")


def test_missing_usage_is_unknown_and_run_total_is_a_lower_bound_excluded_from_statistics():
    inc = bm.stream_metrics((_FX / "fx_missing_usage.jsonl").read_text(encoding="utf-8"))
    ok = bm.stream_metrics((_FX / "fx_dup_stream_events.jsonl").read_text(encoding="utf-8"))
    rows = [_row("T1", "baseline", 0.1, repeat=0, **ok), _row("T1", "router", 0.1, repeat=0, **inc),
            _row("T1", "baseline", 0.1, repeat=1, **ok), _row("T1", "router", 0.1, repeat=1, **ok)]
    out = bm.summarize_tokens(rows)
    assert "pairs_used=1 pairs_excluded=1" in out and "router tokens incomplete (missing input_tokens,cache_read" in out
    assert "WARNING tokens_incomplete" in out and "LOWER BOUNDS" in out and "T1_router_0" in out
    assert "WARNING usage_mismatch" in out
    assert bm.attribute(bm._events((_FX / "fx_missing_usage.jsonl").read_text(encoding="utf-8")), {})[0]["usage_complete"] is False


def test_interrupted_stream_is_incomplete_never_correct_and_listed(monkeypatch, tmp_path):
    import subprocess as sp
    text = (_FX / "fx_no_result_interrupted.jsonl").read_text(encoding="utf-8")
    monkeypatch.setattr(bm, "sh", lambda cmd, cwd, timeout=900, env=None: sp.CompletedProcess(cmd, 0, text, ""))

    class Yes(bm.Task):
        def check(self, wt, result_text):
            return True, "checker says yes"
    a = _A(cond_specs=None, model="haiku", max_turns=1, max_budget_usd=0.1, out=tmp_path, timeout=5)
    rec, _ = bm.run_once(tmp_path / "wt", Yes("Y", "k", "p"), "router", a, 0)
    assert rec["incomplete"] is True and rec["correct"] is False and "INCOMPLETE" in rec["check"]
    base = _row("Y", "baseline", 0.1, repeat=0, incomplete=False, tokens_complete=True, total_tokens=500)
    out = bm.summarize([base, dict(_row("Y", "router", 0.01, repeat=0), **rec)])
    assert "INCOMPLETE runs" in out and "Y_router_0" in out and "pairs_used=0 pairs_excluded=1" in out
    assert "WARNING incomplete" in bm.summarize_tokens([dict(_row("Y", "router", 0.01, repeat=0), **rec)])


def test_recompute_rescores_a_stored_correct_run_whose_stream_has_no_result(tmp_path):
    raw = tmp_path / "raw"
    raw.mkdir()
    (raw / "T1_router_0.stream.jsonl").write_text((_FX / "fx_no_result_interrupted.jsonl").read_text(encoding="utf-8"),
                                                  encoding="utf-8")
    (tmp_path / "results.jsonl").write_text(json.dumps({"task": "T1", "kind": "k", "condition": "router", "repeat": 0,
                                                        "correct": True, "check": "ok", "wall_s": 1.0}) + "\n", encoding="utf-8")
    (tmp_path / "SUMMARY.md").write_text("# H\n\n| task | old |\n|---|\n", encoding="utf-8")
    assert bm.main(["--recompute", str(tmp_path)]) == 0
    row = json.loads((tmp_path / "results.jsonl").read_text(encoding="utf-8"))
    assert row["incomplete"] is True and row["correct"] is False and row["rescored"] is True
    assert "T1_router_0 re-scored correct -> INCORRECT: stream has no result event" in (tmp_path / "SUMMARY.md").read_text(encoding="utf-8")


def _tokrow(task, cond, rep, tot, correct=True, **kw):
    r = {"task": task, "condition": cond, "repeat": rep, "correct": correct, "total_tokens": tot, "tok_input": 10,
         "tok_cache_read": tot - 100, "tok_cache_creation": 60, "tok_output": 30, "cost_authoritative_usd": tot / 10000,
         "turns": 2, "delegations": 0}
    r.update(kw)
    return r


def test_failed_cheap_arm_creates_no_fake_token_or_cost_saving():
    rows = [_tokrow("T1", "baseline", 0, 1000), _tokrow("T1", "router", 0, 100, correct=False),   # cheap because it failed
            _tokrow("T1", "baseline", 1, 1000), _tokrow("T1", "router", 1, 900)]
    out = bm.summarize_tokens(rows)
    assert "pairs_used=1 pairs_excluded=1" in out and "T1#0: router incorrect" in out
    line = [ln for ln in out.splitlines() if ln.startswith("| T1 | 1/1 | 1 |")][0]
    assert "+10.0%" in line and "+55.0%" not in line                  # 900 vs 1000 only (old pooling: 1000 vs 500 = +50%)
    pooled = [ln for ln in out.splitlines() if ln.startswith("| ALL | 1/1 | 1 |")][0]
    assert "+10.0%" in pooled
    only_failed = bm.summarize_tokens(rows[:2])
    assert "pairs_used=0 pairs_excluded=1" in only_failed and "| ALL |" not in only_failed


def test_pairing_follows_repeat_and_reports_each_exclusion_reason():
    rows = [_tokrow("T1", "baseline", 0, 1000), _tokrow("T1", "router", 0, 500),
            _tokrow("T1", "baseline", 1, 1000, correct=False), _tokrow("T1", "router", 1, 200),
            _tokrow("T1", "baseline", 2, 1000), _tokrow("T1", "router", 2, 100, tokens_complete=False, missing_fields=["cache_read_input_tokens"]),
            _tokrow("T1", "baseline", 3, 1000)]
    out = bm.summarize_tokens(rows)
    assert "pairs_used=1 pairs_excluded=3" in out
    for why in ("T1#1: baseline incorrect", "T1#2: router tokens incomplete (missing cache_read_input_tokens)", "T1#3: no router run"):
        assert why in out
    pooled = [ln for ln in out.splitlines() if ln.startswith("| ALL |")][0].split("|")
    assert pooled[2].strip() == "1/1" and pooled[3].strip() == "3" and pooled[8].strip() == "+50.0%"  # total_tokens reduction


def test_other_arm_names_compare_against_the_first_arm():
    rows = [_tokrow("T1", "base_old", 0, 1000), _tokrow("T1", "ctx_new", 0, 1200), _tokrow("T1", "combined", 0, 800)]
    out = bm.summarize_tokens(rows)
    assert "| T1 [ctx_new vs base_old] | 1/1 |" in out and "| T1 [combined vs base_old] | 1/1 |" in out
    assert "| ALL [ctx_new vs base_old] |" in out


def test_inconsistency_flags_are_set_and_warned():
    only_cost = bm.metrics_from({"total_cost_usd": 0.5, "result": "x", "n_results": 1}, 0, 0, "")
    assert only_cost["cost_without_tokens"] is True and "total_tokens" not in only_cost and only_cost["tokens_complete"] is False
    assert bm.consistency_flags({"total_tokens": 100, "total_cost_usd": None, "cost_authoritative_usd": None}) == {"tokens_without_cost": True}
    assert bm.consistency_flags({"total_tokens": 0, "total_cost_usd": 0.2}) == {"cost_without_tokens": True}
    assert bm.consistency_flags({"total_tokens": 100, "total_cost_usd": 0.2}) == {}
    rows = [dict(_row("T1", "router", 0.5, repeat=0), **only_cost),
            _tokrow("T2", "router", 0, 100, cost_authoritative_usd=None, total_cost_usd=None, tokens_without_cost=True),
            dict(_tokrow("T3", "router", 0, 100), **bm.stream_metrics((_FX / "fx_modelusage_differs.jsonl").read_text(encoding="utf-8")))]
    out = bm.summarize_tokens(rows)
    for flag in ("cost_without_tokens", "tokens_without_cost", "usage_mismatch"):
        assert f"WARNING {flag}:" in out, flag
    assert "T1_router_0" in out.split("WARNING cost_without_tokens")[1].splitlines()[0]
    part = bm.stream_metrics((_FX / "fx_partial_provider_record.jsonl").read_text(encoding="utf-8"))
    assert "WARNING cost_partial" in bm.summarize_tokens([dict(_row("T4", "router", 0.1, repeat=0), **part)])


def test_token_accounting_check_cli_exit_codes_and_reuses_the_harness(capsys):
    spec = importlib.util.spec_from_file_location("token_accounting_check", _P.with_name("token_accounting_check.py"))
    tac = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tac)
    assert tac.main([str(_FX / "fx_main_two_subagents.jsonl")]) == 0
    out = capsys.readouterr().out
    assert "subagent:Explore" in out and "total_tokens=3983" in out and "status: OK" in out
    assert tac.main([str(_FX / "fx_missing_usage.jsonl")]) == 1 and "INCOMPLETE usage" in capsys.readouterr().out
    assert tac.main([str(_FX / "fx_no_result_interrupted.jsonl")]) == 1 and "INCOMPLETE (no result event)" in capsys.readouterr().out
    assert tac.main([str(_FX / "fx_modelusage_differs.jsonl")]) == 1 and "MISMATCH usage_mismatch" in capsys.readouterr().out
    assert tac.main([str(_FX)]) == 1 and "checked 6 stream(s)" in capsys.readouterr().out   # results dir / fixtures dir
    assert tac.main([]) == 2
    assert not hasattr(tac, "attribute") and not hasattr(tac, "dedup_requests")
