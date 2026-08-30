"""A7 model-execution composition tests."""
import ast
import inspect

from cognitive_core.actual_usage_telemetry import ActualUsageTelemetry
from cognitive_core.council_model_execution import (
    run_council_with_model_execution,
)
from cognitive_core.fake_model_provider import FakeModelProvider
from cognitive_core.model_provider import ModelRequest
from cognitive_core.model_tier_router import ModelTierRouter


class _StubTelemetry:
    raw_context_tokens = 0

    def record_context(self, raw_count, dedup_count, selected, rejected):
        self.raw_context_tokens += raw_count


class _StubCouncilRun:
    __slots__ = ("agent_packs", "telemetry")

    def __init__(self, agent_packs):
        self.agent_packs = agent_packs
        self.telemetry = _StubTelemetry()


def _router():
    providers = {
        "fake": lambda entry: FakeModelProvider(
            provider_name="fake", model_name=entry["model"]
        )
    }
    return ModelTierRouter(
        {
            "light": {"provider": "fake", "model": "fake-light"},
            "standard": {"provider": "fake", "model": "fake-standard"},
            "heavy": {"provider": "fake", "model": "fake-heavy"},
        },
        provider_factories=providers,
    )


def test_disabled_by_default_is_true_no_op():
    council_run = _StubCouncilRun({"agent_a": {"marker": "A"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="t",
        agent_model_tiers={"agent_a": "light"},
        synthesis_model_tier="heavy",
    )
    assert result.model_execution_enabled is False
    assert result.specialist_results == {}
    assert result.synthesis_result is None
    assert result.actual_usage.events == []


def test_enabled_executes_one_specialist_call_per_agent():
    council_run = _StubCouncilRun({"agent_a": {"marker": "A"}, "agent_b": {"marker": "B"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="t",
        agent_model_tiers={"agent_a": "light", "agent_b": "standard"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
    )
    assert set(result.specialist_results) == {"agent_a", "agent_b"}
    assert result.synthesis_result is not None
    assert len(result.actual_usage.events) == 3


def test_regula_1_specialist_receives_only_its_own_pack():
    router = _router()
    council_run = _StubCouncilRun({"agent_a": {"marker": "ONLY_A"}, "agent_b": {"marker": "ONLY_B"}})

    for provider in (router.resolve("light"), router.resolve("standard")):
        provider.calls.clear() if hasattr(provider, "calls") else None

    run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=router,
        task="t",
        agent_model_tiers={"agent_a": "light", "agent_b": "standard"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
    )
    light_calls = router.resolve("light").calls
    standard_calls = router.resolve("standard").calls
    assert "ONLY_A" in light_calls[0].prompt
    assert "ONLY_B" not in light_calls[0].prompt
    assert "ONLY_B" in standard_calls[0].prompt
    assert "ONLY_A" not in standard_calls[0].prompt


def test_regula_2_synthesis_excludes_raw_agent_packs():
    router = _router()
    council_run = _StubCouncilRun({"agent_a": {"raw": "RAW_PACK_CONTENT_XYZ"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=router,
        task="t",
        agent_model_tiers={"agent_a": "light"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
    )
    assert result.synthesis_result is not None
    synthesis_call = router.resolve("heavy").calls[-1]
    assert "RAW_PACK_CONTENT_XYZ" not in synthesis_call.prompt


def test_regula_3_actual_usage_records_every_call():
    council_run = _StubCouncilRun({"agent_a": {"n": "a"}, "agent_b": {"n": "b"}})
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="t",
        agent_model_tiers={"agent_a": "light", "agent_b": "standard"},
        synthesis_model_tier="heavy",
        model_execution_enabled=True,
    )
    assert len(result.actual_usage.events) == 3
    assert result.actual_usage.has_real_provider_usage is True


def test_missing_agent_tier_raises():
    council_run = _StubCouncilRun({"agent_a": {"n": "a"}})
    try:
        run_council_with_model_execution(
            council_run=council_run,
            model_tier_router=_router(),
            task="t",
            agent_model_tiers={},
            synthesis_model_tier="heavy",
            model_execution_enabled=True,
        )
        assert False, "expected ValueError"
    except ValueError:
        pass


def test_estimated_telemetry_passthrough_untouched():
    council_run = _StubCouncilRun({"agent_a": {"n": "a"}})
    original = council_run.telemetry
    result = run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=_router(),
        task="t",
        agent_model_tiers={"agent_a": "light"},
        synthesis_model_tier="heavy",
    )
    assert result.estimated_telemetry is original


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
        synthesis_system_prompt="SYSTEM_MARKER",
    )
    assert router.resolve("heavy").calls[-1].system_prompt == "SYSTEM_MARKER"
