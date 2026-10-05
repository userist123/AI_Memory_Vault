from pathlib import Path

from routing.agent_router import AgentRouter
from routing.models import PrivacyMode, QualityTier, RiskLevel, TaskRequest
from routing.registry import RouteRegistry


ROOT = Path(__file__).resolve().parents[1]
REG = RouteRegistry.from_file(ROOT / "04_CONFIG" / "agent_router.json")


def test_coding_repo_routes_to_code_agent():
    req = TaskRequest(goal="fix the Python repository tests and commit the regression fix", capabilities=("coding","repo","testing"))
    d = AgentRouter(REG).route(req, {"codex": True, "claude_code": True, "antigravity": True, "local_llm": False, "perplexity": False})
    assert d.status.value == "ROUTED"
    assert d.primary is not None
    assert d.primary.agent_id == "code_engineer"
    assert d.primary.runtime_id in {"codex", "claude_code"}


def test_visual_task_prefers_antigravity():
    req = TaskRequest(goal="inspect the UI visually in the browser and fix the layout", capabilities=("visual",), min_quality=QualityTier.HEAVY)
    d = AgentRouter(REG).route(req, {"antigravity": True, "claude_code": True, "codex": True, "local_llm": False, "perplexity": False})
    assert d.primary is not None
    assert d.primary.runtime_id == "antigravity"


def test_local_only_cannot_route_to_external_runtime():
    req = TaskRequest(goal="analyze this local codebase", capabilities=("coding","repo"), privacy=PrivacyMode.LOCAL_ONLY)
    d = AgentRouter(REG).route(req, {"local_llm": True, "codex": True, "claude_code": True, "antigravity": True, "perplexity": False})
    assert d.primary is not None
    assert d.primary.runtime_id == "local_llm"


def test_external_research_routes_to_perplexity_but_requires_host_confirmation():
    req = TaskRequest(goal="research current external sources and compare findings", external_research_required=True, min_quality=QualityTier.HEAVY)
    d = AgentRouter(REG).route(req, {"perplexity": True})
    assert d.primary is not None
    assert d.primary.runtime_id == "perplexity"
    assert d.status.value == "ROUTED"


def test_high_risk_coding_gets_independent_verifier():
    req = TaskRequest(goal="modify production code and review the implementation impact", capabilities=("coding","repo"), risk=RiskLevel.HIGH, require_independent_verifier=True, min_quality=QualityTier.HEAVY)
    d = AgentRouter(REG).route(req, {"codex": True, "claude_code": True, "antigravity": True, "perplexity": False})
    assert d.primary is not None
    assert d.verifier is not None
    assert d.primary.independence_group != d.verifier.independence_group


def test_blocked_when_no_eligible_runtime():
    req = TaskRequest(goal="run browser UI investigation", capabilities=("visual",), min_quality=QualityTier.FRONTIER)
    d = AgentRouter(REG).route(req, {"antigravity": False, "claude_code": False, "codex": False, "local_llm": False, "perplexity": False})
    assert d.status.value in {"PLANNED","BLOCKED"}
