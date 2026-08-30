import json
import os

import pytest

from cognitive_core.executive_model_execution_bridge import execute_council_models
from cognitive_core.local_provider import LocalProvider


class StubSpec:
    def __init__(self, model_tier):
        self.model_tier = model_tier


class StubCouncilRun:
    def __init__(self):
        self.agent_packs = {
            "retrieval": {"results": [{"id": "r1", "content": "local retrieval evidence"}]},
        }
        self.telemetry = object()


def test_b3_live_ollama_council_execution():
    if os.getenv("RUN_LIVE_OLLAMA_TESTS") != "1":
        pytest.skip("Set RUN_LIVE_OLLAMA_TESTS=1 to run the real local Ollama smoke test")

    model = os.getenv("OLLAMA_MODEL", "").strip()
    if not model:
        pytest.fail("OLLAMA_MODEL must be set for the live Ollama smoke test")

    config_path = os.getenv("OLLAMA_MODEL_TIERS_CONFIG", "").strip()
    if not config_path:
        pytest.fail("OLLAMA_MODEL_TIERS_CONFIG must point to a local provider model_tiers.json")

    with open(config_path, "r", encoding="utf-8") as handle:
        config = json.load(handle)

    assert all(entry.get("provider") == "local" for entry in config.values())

    provider = LocalProvider(model_name=model)
    health = provider.health()
    assert health.get("status") == "ok", health
    assert health.get("model_available") is True

    result = execute_council_models(
        council_run=StubCouncilRun(),
        subagent_specs={
            "retrieval": StubSpec("light"),
            "SYNTHESIZER": StubSpec("heavy"),
        },
        task="validate local model execution",
        synthesis_role="SYNTHESIZER",
        model_execution_enabled=True,
        config_path=config_path,
        allowed_providers=("local",),
        provider_factories={"local": lambda model_name: LocalProvider(model_name=model_name)},
    )

    assert set(result.specialist_results) == {"retrieval"}
    assert result.specialist_results["retrieval"].provider == "local"
    assert result.synthesis_result is not None
    assert result.synthesis_result.provider == "local"
    assert result.actual_usage.has_real_provider_usage is True
    assert len(result.actual_usage.events) == 2
    assert result.actual_usage.actual_total_tokens > 0
