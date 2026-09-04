import json

from cognitive_core.executive_model_execution_bridge import execute_council_models
from cognitive_core.fake_model_provider import FakeModelProvider
from cognitive_core.model_tier_router import ModelTierRouter


class StubSpec:
    def __init__(self, model_tier):
        self.model_tier = model_tier


class StubCouncilRun:
    def __init__(self):
        self.agent_packs = {
            "retrieval": {"results": [{"id": "r1", "content": "retrieval evidence"}]},
            "verifier": {"results": [{"id": "v1", "content": "verification evidence"}]},
        }
        self.telemetry = object()


def test_b2_real_bridge_fake_provider_end_to_end(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(
        json.dumps(
            {
                "light": {"provider": "fake", "model": "fake-light"},
                "standard": {"provider": "fake", "model": "fake-standard"},
                "heavy": {"provider": "fake", "model": "fake-heavy"},
            }
        ),
        encoding="utf-8",
    )

    fake_instances = {}

    def fake_factory(model_name):
        provider = FakeModelProvider(provider_name="fake", model_name=model_name)
        fake_instances[model_name] = provider
        return provider

    specs = {
        "retrieval": StubSpec("light"),
        "verifier": StubSpec("standard"),
        "SYNTHESIZER": StubSpec("heavy"),
    }

    result = execute_council_models(
        council_run=StubCouncilRun(),
        subagent_specs=specs,
        task="validate the evidence",
        synthesis_role="SYNTHESIZER",
        model_execution_enabled=True,
        config_path=config_path,
        provider_factories={"fake": fake_factory},
    )

    assert set(result.specialist_results) == {"retrieval", "verifier"}
    assert result.specialist_results["retrieval"].model == "fake-light"
    assert result.specialist_results["retrieval"].model_tier == "light"
    assert result.specialist_results["verifier"].model == "fake-standard"
    assert result.specialist_results["verifier"].model_tier == "standard"
    assert result.synthesis_result is not None
    assert result.synthesis_result.model == "fake-heavy"
    assert result.synthesis_result.model_tier == "heavy"
    assert result.actual_usage.has_real_provider_usage is True
    assert len(result.actual_usage.events) == 3
    assert result.actual_usage.actual_total_tokens > 0

    assert fake_instances["fake-light"].calls
    assert fake_instances["fake-standard"].calls
    assert fake_instances["fake-heavy"].calls
