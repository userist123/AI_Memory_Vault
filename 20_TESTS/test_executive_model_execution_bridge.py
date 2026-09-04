import json

import pytest

from cognitive_core.executive_model_execution_bridge import (
    BridgeConfigError,
    build_model_tier_router,
    derive_agent_model_tiers,
    execute_council_models,
)
from cognitive_core.model_tier_router import ModelTierConfigError
from cognitive_core.fake_model_provider import FakeModelProvider


class _StubSubagentSpec:
    def __init__(self, model_tier):
        self.model_tier = model_tier


class _StubCouncilRun:
    def __init__(self, agent_packs, telemetry=None):
        self.agent_packs = agent_packs
        self.telemetry = telemetry


FAKE_FACTORIES = {"fake": lambda model_name: FakeModelProvider(provider_name="fake", model_name=model_name)}


def test_derive_agent_model_tiers_reads_existing_spec():
    specs = {"ROUTER": _StubSubagentSpec("light"), "SYNTHESIZER": _StubSubagentSpec("heavy")}
    tiers = derive_agent_model_tiers(specs)
    assert tiers == {"ROUTER": "light", "SYNTHESIZER": "heavy"}


def test_derive_agent_model_tiers_raises_on_missing_tier():
    specs = {"ROUTER": _StubSubagentSpec(None)}
    with pytest.raises(BridgeConfigError):
        derive_agent_model_tiers(specs)


def test_build_router_rejects_openai_provider(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "openai", "model": "gpt-x"},
    }))
    with pytest.raises(BridgeConfigError):
        build_model_tier_router(config_path=config_path)


def test_build_router_rejects_unknown_provider(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "some_unknown_vendor", "model": "x"},
    }))
    with pytest.raises(BridgeConfigError):
        build_model_tier_router(config_path=config_path)


def test_build_router_allows_fake_only_config(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "fake", "model": "x"},
    }))
    router = build_model_tier_router(config_path=config_path, provider_factories=FAKE_FACTORIES)
    assert router.resolve("light") is not None


def test_build_router_rejects_local_when_not_in_allowed_providers(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "local", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "fake", "model": "x"},
    }))
    with pytest.raises(BridgeConfigError):
        build_model_tier_router(config_path=config_path, allowed_providers=("fake",), provider_factories=FAKE_FACTORIES)


def test_build_router_accepts_local_when_explicitly_allowed_but_needs_factory(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "local", "model": "llama3"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "fake", "model": "x"},
    }))
    with pytest.raises(ModelTierConfigError):
        build_model_tier_router(
            config_path=config_path,
            allowed_providers=("fake", "local"),
            provider_factories=FAKE_FACTORIES,
        )


def test_execute_council_models_disabled_by_default_is_no_op(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "fake", "model": "x"},
    }))
    council_run = _StubCouncilRun(agent_packs={"ROUTER": {"n": "a"}})
    specs = {"ROUTER": _StubSubagentSpec("light"), "SYNTHESIZER": _StubSubagentSpec("heavy")}

    result = execute_council_models(
        council_run=council_run, subagent_specs=specs, task="t",
        config_path=config_path, provider_factories=FAKE_FACTORIES,
    )

    assert result.model_execution_enabled is False
    assert result.specialist_results == {}
    assert result.synthesis_result is None


def test_execute_council_models_enabled_runs_full_fake_end_to_end(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "fake-light"},
        "standard": {"provider": "fake", "model": "fake-standard"},
        "heavy": {"provider": "fake", "model": "fake-heavy"},
    }))
    council_run = _StubCouncilRun(agent_packs={"ROUTER": {"n": "a"}, "VERIFIER": {"n": "b"}})
    specs = {
        "ROUTER": _StubSubagentSpec("light"),
        "VERIFIER": _StubSubagentSpec("light"),
        "SYNTHESIZER": _StubSubagentSpec("heavy"),
    }

    result = execute_council_models(
        council_run=council_run, subagent_specs=specs, task="analyze",
        model_execution_enabled=True, config_path=config_path,
        provider_factories=FAKE_FACTORIES,
    )

    assert set(result.specialist_results) == {"ROUTER", "VERIFIER"}
    assert result.synthesis_result.model_tier == "heavy"
    assert result.actual_usage.has_real_provider_usage is True
    assert len(result.actual_usage.events) == 3


def test_subagent_spec_tier_is_preserved_through_pipeline(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "fake-light"},
        "standard": {"provider": "fake", "model": "fake-standard"},
        "heavy": {"provider": "fake", "model": "fake-heavy"},
    }))
    council_run = _StubCouncilRun(agent_packs={"CONSOLIDATOR": {"n": "a"}})
    specs = {
        "CONSOLIDATOR": _StubSubagentSpec("standard"),
        "SYNTHESIZER": _StubSubagentSpec("heavy"),
    }

    result = execute_council_models(
        council_run=council_run, subagent_specs=specs, task="t",
        model_execution_enabled=True, config_path=config_path,
        provider_factories=FAKE_FACTORIES,
    )

    assert result.specialist_results["CONSOLIDATOR"].model_tier == "standard"


def test_execute_council_models_missing_synthesis_role_raises(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "fake", "model": "x"},
    }))
    council_run = _StubCouncilRun(agent_packs={"ROUTER": {"n": "a"}})
    specs = {"ROUTER": _StubSubagentSpec("light")}

    with pytest.raises(BridgeConfigError):
        execute_council_models(
            council_run=council_run, subagent_specs=specs, task="t",
            model_execution_enabled=True, config_path=config_path,
            provider_factories=FAKE_FACTORIES,
        )


def test_execute_council_models_missing_agent_model_tier_raises(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "fake", "model": "x"},
    }))
    council_run = _StubCouncilRun(agent_packs={"ROUTER": {"n": "a"}})
    specs = {"ROUTER": _StubSubagentSpec(None), "SYNTHESIZER": _StubSubagentSpec("heavy")}

    with pytest.raises(BridgeConfigError):
        execute_council_models(
            council_run=council_run, subagent_specs=specs, task="t",
            model_execution_enabled=True, config_path=config_path,
            provider_factories=FAKE_FACTORIES,
        )


def test_bridge_never_imports_executive_or_orchestrator():
    import inspect
    import cognitive_core.executive_model_execution_bridge as bridge_module

    source = inspect.getsource(bridge_module)
    assert "Council_Orchestrator" not in source
    assert "99_SYSTEM" not in source
    assert "import executive" not in source
