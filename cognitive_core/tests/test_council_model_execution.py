"""A7 model execution contract tests.

Uses a duck-typed CouncilRun stub and FakeModelProvider so the suite is fully
local, deterministic, and independent of the frozen Council_Orchestrator.
"""
import ast
import inspect

import pytest

from cognitive_core.actual_usage_telemetry import ActualUsageTelemetry
from cognitive_core.council_model_execution import CouncilRunWithExecution, run_council_with_model_execution
from cognitive_core.fake_model_provider import FakeModelProvider
from cognitive_core.model_tier_router import ModelTierRouter, TierConfig


class _StubCouncilRun:
    def __init__(self, agent_packs, telemetry=None):
        self.agent_packs = agent_packs
        self.telemetry = telemetry


FAKE_FACTORIES = {
    "fake": lambda model_name: FakeModelProvider(provider_name="fake", model_name=model_name),
}


def _router():
    tier_config = {
        "light": TierConfig(provider="fake", model="fake-light"),
        "standard": TierConfig(provider="fake", model="fake-standard"),
        "heavy": TierConfig(provider="fake", model="fake-heavy"),
    }
    return ModelTierRouter(tier_config, FAKE_FACTORIES)


def test_disabled_by_default_is_true_no_op():
    council_run = _StubCouncilRun({"agent_a": {"note": "x"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="do something",
        agent_model_tiers={"agent_a": "light"},
        synthesis_model_tier="heavy",
    )
    assert result.model_execution_enabled is False
    assert result.specialist_results == {}
    assert result.synthesis_result is None
    assert result.actual_usage.actual_total_tokens == 0
    assert result.council_run is council_run


def test_enabled_executes_one_specialist_call_per_agent():
    council_run = _StubCouncilRun({"agent_a": {"note": "pack A"}, "agent_b": {"note": "pack B"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="analyze",
        agent_model_tiers={"agent_a": "light", "agent_b": "standard"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
    )
    assert set(result.specialist_results) == {"agent_a", "agent_b"}
    assert result.specialist_results["agent_a"].model_tier == "light"
    assert result.specialist_results["agent_b"].model_tier == "standard"
    assert result.synthesis_result is not None
    assert result.synthesis_result.model_tier == "heavy"


def test_regula_1_specialist_receives_only_its_own_pack():
    router = _router()
    council_run = _StubCouncilRun({
        "agent_a": {"secret_marker": "ONLY_FOR_AGENT_A"},
        "agent_b": {"secret_marker": "ONLY_FOR_AGENT_B"},
    })
    run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=router,
        task="t",
        agent_model_tiers={"agent_a": "light", "agent_b": "light"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
    )
    specialist_provider = router.resolve("light")
    prompts = [call.prompt for call in specialist_provider.calls]
    a_prompt = [prompt for prompt in prompts if "ONLY_FOR_AGENT_A" in prompt][0]
    b_prompt = [prompt for prompt in prompts if "ONLY_FOR_AGENT_B" in prompt][0]
    assert "ONLY_FOR_AGENT_B" not in a_prompt
    assert "ONLY_FOR_AGENT_A" not in b_prompt


def test_regula_2_synthesis_excludes_raw_agent_packs():
    router = _router()
    council_run = _StubCouncilRun({"agent_a": {"secret_marker": "RAW_PACK_CONTENT_XYZ"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=router,
        task="t",
        agent_model_tiers={"agent_a": "light"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
    )
    assert result.synthesis_result is not None
    synthesis_prompt = router.resolve("heavy").calls[0].prompt
    assert "RAW_PACK_CONTENT_XYZ" not in synthesis_prompt


def test_regula_3_actual_usage_records_every_call():
    council_run = _StubCouncilRun({"agent_a": {"n": "a"}, "agent_b": {"n": "b"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="t",
        agent_model_tiers={"agent_a": "light", "agent_b": "light"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
    )
    assert len(result.actual_usage.events) == 3
    kinds = [event.kind for event in result.actual_usage.events]
    assert kinds.count("specialist") == 2
    assert kinds.count("synthesis") == 1
    assert result.actual_usage.has_real_provider_usage is True


def test_missing_agent_tier_raises():
    council_run = _StubCouncilRun({"agent_a": {"n": "a"}})
    with pytest.raises(ValueError):
        run_council_with_model_execution(
            council_run=council_run,
            model_tier_router=_router(),
            task="t",
            agent_model_tiers={},
            synthesis_model_tier="heavy",
            model_execution_enabled=True,
        )


def test_estimated_telemetry_passthrough_untouched():
    class _FakeEstimatedTelemetry:
        agents_selected = 2

    telemetry = _FakeEstimatedTelemetry()
    council_run = _StubCouncilRun({"agent_a": {"n": "a"}}, telemetry=telemetry)
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="t",
        agent_model_tiers={"agent_a": "light"},
        synthesis_model_tier="heavy",
    )
    assert result.estimated_telemetry is telemetry
    assert result.estimated_telemetry.agents_selected == 2


def test_agent_packs_property_passthrough():
    council_run = _StubCouncilRun({"agent_a": {"n": "a"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="t",
        agent_model_tiers={"agent_a": "light"},
        synthesis_model_tier="heavy",
    )
    assert result.agent_packs is council_run.agent_packs


def test_council_orchestrator_file_not_modified_by_this_module():
    import cognitive_core.council_model_execution as cme_module

    tree = ast.parse(inspect.getsource(cme_module))
    imported_modules = []
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imported_modules.extend(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            imported_modules.append(node.module)

    assert all("Council_Orchestrator" not in module for module in imported_modules)
    assert all(not module.startswith("99_SYSTEM") for module in imported_modules)


def test_synthesis_system_prompt_is_forwarded():
    router = _router()
    council_run = _StubCouncilRun({"agent_a": {"n": "a"}})
    run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=router,
        task="t",
        agent_model_tiers={"agent_a": "light"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
        synthesis_system_prompt="synthesize concisely",
    )
    assert router.resolve("heavy").calls[0].system_prompt == "synthesize concisely"
