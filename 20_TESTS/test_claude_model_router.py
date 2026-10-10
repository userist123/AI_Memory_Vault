"""Tests for the Claude Code model router (routing/claude_model_router.py and friends)."""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

from routing.claude_model_router import (
    DEFAULT_POLICY_PATH,
    ClaudeModelPolicy,
    ModelRoutingPolicyError,
    classify,
    estimate_cost,
    route,
)
from routing.claude_usage_report import aggregate, iter_usage_records, priced_report, project_slug
from routing.claude_model_cli import main as cli_main

ROOT = Path(__file__).resolve().parents[1]
POLICY = ClaudeModelPolicy.from_file(DEFAULT_POLICY_PATH)


# -- policy file --------------------------------------------------------------

def test_policy_file_is_the_one_in_04_config():
    assert DEFAULT_POLICY_PATH == ROOT / "04_CONFIG" / "claude_model_routing.json"
    assert set(POLICY.models) == {"fable", "opus", "sonnet", "haiku"}
    assert POLICY.escalation_order == ("haiku", "sonnet", "opus", "fable")


def test_tier_ranks_follow_price():
    by_rank = sorted(POLICY.models.values(), key=lambda m: m.tier_rank)
    prices = [m.price.output for m in by_rank]
    assert prices == sorted(prices), "a higher tier must not be cheaper per output token"


def test_malformed_policy_fails_closed(tmp_path):
    raw = json.loads(DEFAULT_POLICY_PATH.read_text())
    raw["task_classes"]["implement"]["model"] = "gpt"
    bad = tmp_path / "p.json"
    bad.write_text(json.dumps(raw))
    with pytest.raises(ModelRoutingPolicyError):
        ClaudeModelPolicy.from_file(bad)
    raw = json.loads(DEFAULT_POLICY_PATH.read_text())
    raw["default_model"] = "nope"
    bad.write_text(json.dumps(raw))
    with pytest.raises(ModelRoutingPolicyError):
        ClaudeModelPolicy.from_file(bad)


# -- classification -----------------------------------------------------------

@pytest.mark.parametrize("goal,expected", [
    ("find which files import memory_controller", "explore"),
    ("summarize the retrieval trace format", "summarize"),
    ("fix the failing test in test_memory_access.py", "implement"),
    ("review this diff for regressions", "review"),
    ("diagnose the root cause of the flaky graph expansion", "design"),
    ("orchestrate an end-to-end rewrite of the whole repository", "frontier"),
])
def test_classify_known_classes(goal, expected):
    tc, hits = classify(POLICY, goal)
    assert tc is not None and tc.name == expected, hits


def test_unclassified_goes_to_default_with_low_confidence():
    d = route(POLICY, "zzz qqq")
    assert d.task_class == "unclassified"
    assert d.confidence == "low"
    assert d.model == POLICY.default_model
    assert "unclassified->default" in d.applied_rules


def test_tie_breaks_toward_the_more_capable_model():
    # one keyword from explore (haiku) and one from design (opus): must not land on haiku
    d = route(POLICY, "find the architecture")
    assert d.model == "opus"


# -- rules ----------------------------------------------------------------------

def test_risk_floor_only_raises():
    cheap = route(POLICY, "find which files import X", risk="low")
    assert cheap.model == "haiku"
    raised = route(POLICY, "find which files import X", risk="high")
    assert raised.model == "opus"
    assert any(r.startswith("risk_floor:high") for r in raised.applied_rules)
    top = route(POLICY, "orchestrate the whole repository migration", risk="critical")
    assert top.model == "fable", "a floor never lowers a tier"


def test_fable_is_never_a_subagent_model():
    main = route(POLICY, "orchestrate an end-to-end long-horizon migration")
    assert main.model == "fable"
    sub = route(POLICY, "orchestrate an end-to-end long-horizon migration", for_subagent=True)
    assert sub.model == "opus"
    assert sub.escalate_to is None


def test_security_requires_independent_verifier():
    d = route(POLICY, "security audit of the token handling")
    assert d.model == "opus" and d.verifier_model == "fable"
    sub = route(POLICY, "security audit of the token handling", for_subagent=True)
    assert sub.verifier_model == "opus"


def test_requested_model_respected_unless_below_risk_floor():
    d = route(POLICY, "fix the test", requested_model="haiku")
    assert d.model == "haiku"
    d = route(POLICY, "fix the test", requested_model="haiku", risk="high")
    assert d.model == "opus"
    with pytest.raises(ModelRoutingPolicyError):
        route(POLICY, "fix the test", requested_model="gpt-5")


# -- cost ---------------------------------------------------------------------

def test_cost_math_matches_rate_card():
    c = estimate_cost(POLICY, "opus", input_tokens=1_000_000, output_tokens=1_000_000)
    assert c.usd == pytest.approx(24.0)
    c = estimate_cost(POLICY, "haiku", cache_read_tokens=1_000_000)
    assert c.usd == pytest.approx(0.01)
    c = estimate_cost(POLICY, "fable", cache_write_tokens=1_000_000, cache_ttl="1h")
    assert c.usd == pytest.approx(20.0)
    with pytest.raises(ValueError):
        estimate_cost(POLICY, "opus", input_tokens=-1)


def test_route_reports_savings_against_fable():
    d = route(POLICY, "find which files import X", input_tokens=100_000, output_tokens=10_000)
    assert d.model == "haiku"
    assert d.cost_on_fable_usd == pytest.approx(1.5)
    assert d.savings_vs_fable_usd == pytest.approx(1.5 - d.estimated_cost_usd)


# -- subagent files agree with the policy -----------------------------------------

def _frontmatter(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    m = re.match(r"---\n(.*?)\n---", text, re.S)
    assert m, f"{path} has no frontmatter"
    out = {}
    for line in m.group(1).splitlines():
        k, _, v = line.partition(":")
        out[k.strip()] = v.strip()
    return out


def test_subagent_definitions_match_policy():
    agents_dir = ROOT / ".claude" / "agents"
    seen = set()
    for tc in POLICY.task_classes.values():
        if tc.subagent is None:
            continue
        fm = _frontmatter(agents_dir / f"{tc.subagent}.md")
        assert fm["name"] == tc.subagent
        assert fm["model"] == tc.model, f"{tc.subagent}: policy says {tc.model}, file says {fm['model']}"
        assert fm["effort"] == tc.effort
        assert fm["model"] != "fable"
        seen.add(tc.subagent)
    assert seen == {"Explore", "vault-worker", "vault-reviewer"}


def test_reviewer_and_explorer_are_read_only():
    for name in ("Explore", "vault-reviewer"):
        fm = _frontmatter(ROOT / ".claude" / "agents" / f"{name}.md")
        tools = {t.strip() for t in fm["tools"].split(",")}
        assert not tools & {"Edit", "Write", "NotebookEdit"}, name


# -- usage report --------------------------------------------------------------

def _line(rid, model, usage, typ="assistant"):
    return json.dumps({"type": typ, "requestId": rid, "message": {"model": model, "usage": usage}})


def test_usage_report_dedupes_by_request_id_and_prices(tmp_path):
    u1 = {"input_tokens": 2, "cache_creation_input_tokens": 52653, "cache_read_input_tokens": 44177,
          "output_tokens": 995, "output_tokens_details": {"thinking_tokens": 661},
          "cache_creation": {"ephemeral_1h_input_tokens": 52653, "ephemeral_5m_input_tokens": 0}}
    u2 = {"input_tokens": 10, "cache_creation_input_tokens": 100, "cache_read_input_tokens": 1000,
          "output_tokens": 50}
    t = tmp_path / "s1.jsonl"
    t.write_text("\n".join([
        _line("req_a", "claude-fable-5-1", u1),
        _line("req_a", "claude-fable-5-1", u1),          # duplicate line, same request
        _line("req_b", "claude-haiku-5-5", u2),
        _line("req_c", "claude-haiku-5-5", u2, typ="user"),  # not an assistant turn
        "not json",
        json.dumps({"type": "assistant", "message": {"model": "x"}}),  # no usage
    ]) + "\n")
    agg = aggregate(iter_usage_records([t]))
    assert set(agg) == {"claude-fable-5-1", "claude-haiku-5-5"}
    f = agg["claude-fable-5-1"]
    assert f.requests == 1 and f.output_tokens == 995 and f.thinking_tokens == 661
    assert f.cache_write_1h_tokens == 52653 and f.cache_write_5m_tokens == 0
    h = agg["claude-haiku-5-5"]
    assert h.cache_write_5m_tokens == 100, "legacy field used when cache_creation is absent"
    rep = priced_report(POLICY, agg)
    fable_row = next(r for r in rep["models"] if r["alias"] == "fable")
    expected = (2 * 10 + 995 * 50 + 44177 * 0.25 + 52653 * 20) / 1e6
    assert fable_row["usd"] == pytest.approx(expected, abs=1e-4)
    assert fable_row["same_tokens_on"]["opus"] < fable_row["usd"]
    assert rep["total_usd"] == pytest.approx(sum(r["usd"] for r in rep["models"]), abs=1e-4)


def test_project_slug_matches_claude_code_layout():
    assert project_slug(Path("/home/user/AI_Memory_Vault_CODEX_READY")) == "-home-user-AI-Memory-Vault-CODEX-READY"


# -- CLI ----------------------------------------------------------------------

def test_cli_route_json(capsys):
    assert cli_main(["route", "--goal", "fix the failing test", "--json"]) == 0
    out = json.loads(capsys.readouterr().out)
    assert out["model"] == "sonnet" and out["task_class"] == "implement"


def test_cli_report_on_fixture(tmp_path, capsys):
    proj = tmp_path / "proj"
    proj.mkdir()
    folder = tmp_path / "projects" / project_slug(proj)
    folder.mkdir(parents=True)
    (folder / "s.jsonl").write_text(_line("r1", "claude-sonnet-5-5", {"input_tokens": 100, "output_tokens": 10}) + "\n")
    assert cli_main(["report", "--project-dir", str(proj), "--projects-dir", str(tmp_path / "projects"), "--json"]) == 0
    rep = json.loads(capsys.readouterr().out)
    assert rep["transcripts"] == 1 and rep["models"][0]["alias"] == "sonnet"
    assert cli_main(["report", "--project-dir", str(tmp_path / "nope"), "--projects-dir", str(tmp_path / "projects")]) == 1
