"""The router enforces the council limits of AGENTS.md ("Runtime Context Contract").

MAX_COUNCIL_AGENTS = 3 (primary + support + verifier) and MAX_TOTAL_SELECTED_SKILLS = 4.
Before this, support was `candidates[1:3]` unconditionally: a route with a verifier carried
4 agents, nothing capped the total skills, and the primary could reappear as support.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from routing.agent_router import AgentRouter
from routing.models import RiskLevel, TaskRequest
from routing.registry import RouteRegistry

ROOT = Path(__file__).resolve().parents[1]
CONFIG = ROOT / "04_CONFIG" / "agent_router.json"
REG = RouteRegistry.from_file(CONFIG)
ALL_UP = {"codex": True, "claude_code": True, "antigravity": True, "local_llm": True, "perplexity": True}

GOALS = [
    ("fix the Python repository tests and commit the regression fix", ("coding", "repo", "testing")),
    ("review the architecture of the memory controller", ("architecture",)),
    ("audit the authentication code for security vulnerabilities", ("security",)),
    ("research current external sources and compare findings", ()),
]


def _route(goal, caps, *, verifier, parallel=True, registry=REG, available=ALL_UP):
    req = TaskRequest(goal=goal, capabilities=caps, allow_parallel=parallel,
                      require_independent_verifier=verifier)
    return AgentRouter(registry).route(req, available)


def _agents(d):
    return (1 if d.primary else 0) + len(d.support) + (1 if d.verifier else 0)


def _skills(d):
    return len(d.primary.selected_skills) + sum(len(s.selected_skills) for s in d.support)


def test_the_config_declares_the_limits():
    assert REG.defaults["max_council_agents"] == 3
    assert REG.defaults["max_total_selected_skills"] == 4


@pytest.mark.parametrize("goal,caps", GOALS)
@pytest.mark.parametrize("verifier", [False, True])
def test_a_route_never_exceeds_three_agents_or_four_skills(goal, caps, verifier):
    d = _route(goal, caps, verifier=verifier)
    assert d.primary is not None
    assert _agents(d) <= 3, (d.primary.agent_id, [s.agent_id for s in d.support], d.verifier)
    assert _skills(d) <= 4
    assert all(s is not d.primary and (s.agent_id, s.runtime_id) != (d.primary.agent_id, d.primary.runtime_id)
               for s in d.support)


def test_with_a_verifier_the_cap_actually_bites():
    # the case the old code got wrong: 1 primary + 2 support + 1 verifier = 4
    d = _route(*GOALS[0], verifier=True)
    assert d.verifier is not None and d.candidate_count >= 3
    assert len(d.support) == 1
    assert any(r.startswith("support:capped:2->1") for r in d.rejected), d.rejected


def test_without_a_verifier_two_support_agents_are_allowed():
    d = _route(*GOALS[0], verifier=False)
    assert d.verifier is None
    assert len(d.support) == 2 and _agents(d) == 3


def test_no_support_without_allow_parallel():
    assert _route(*GOALS[0], verifier=True, parallel=False).support == ()


def _registry_with(tmp_path, **defaults):
    raw = json.loads(CONFIG.read_text(encoding="utf-8"))
    raw["defaults"].update(defaults)
    tmp_path.mkdir(parents=True, exist_ok=True)
    cfg = tmp_path / "agent_router.json"
    cfg.write_text(json.dumps(raw), encoding="utf-8")
    return RouteRegistry.from_file(cfg)


def test_support_skills_are_trimmed_to_the_total_budget(tmp_path):
    # the budget is set just below what the scenario selects uncapped, so trimming always happens
    unbounded = _route(*GOALS[0], verifier=False, registry=_registry_with(tmp_path / "u", max_total_selected_skills=99))
    budget = _skills(unbounded) - 1
    assert budget >= len(unbounded.primary.selected_skills), "scenario too small to exercise the budget"
    d = _route(*GOALS[0], verifier=False, registry=_registry_with(tmp_path / "b", max_total_selected_skills=budget))
    assert [(s.agent_id, s.runtime_id) for s in d.support] == [(s.agent_id, s.runtime_id) for s in unbounded.support]
    assert _skills(d) == budget
    assert any(f"skills_capped:max_total_selected_skills={budget}" in r for r in d.rejected), d.rejected


@pytest.mark.parametrize("goal,caps", GOALS)
@pytest.mark.parametrize("verifier", [False, True])
def test_support_agents_are_distinct_specialists(goal, caps, verifier):
    # owner decision 2026-10-24: the primary's agent on another runtime is a duplicate, not support
    d = _route(goal, caps, verifier=verifier)
    ids = [s.agent_id for s in d.support]
    assert d.primary.agent_id not in ids
    assert len(ids) == len(set(ids))


def test_the_limits_come_from_the_config(tmp_path):
    reg = _registry_with(tmp_path, max_council_agents=2, max_total_selected_skills=2)
    with_verifier = _route(*GOALS[0], verifier=True, registry=reg)
    assert with_verifier.verifier is not None and with_verifier.support == ()
    without = _route(*GOALS[0], verifier=False, registry=reg)
    assert len(without.support) == 1 and _skills(without) <= 2


def test_high_risk_gets_a_verifier_and_stays_within_the_cap():
    req = TaskRequest(goal="audit the authentication code for security vulnerabilities",
                      capabilities=("security",), risk=RiskLevel.HIGH, allow_parallel=True)
    d = AgentRouter(REG).route(req, ALL_UP)
    assert d.verifier is not None
    assert _agents(d) <= 3
